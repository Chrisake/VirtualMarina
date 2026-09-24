using VirtualMarina.Core.Api;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Serialization;
using VirtualMarina.Designer.Resources;

namespace VirtualMarina.Designer;

/// <summary>Which settings sit beside the marina, under the current tool's own.</summary>
public enum DesignerSidePanel
{
    /// <summary>Only the current tool's settings.</summary>
    Tools = 0,

    /// <summary>The tool's settings, then how the marina is drawn.</summary>
    Look = 1,

    /// <summary>The tool's settings, then the saved and automatic views.</summary>
    Cameras = 2,
}

/// <summary>Something the user should see without being stopped: a change the designer refused, and why.</summary>
/// <param name="message">The message.</param>
public sealed class DesignerNoticeEventArgs(string message) : EventArgs
{
    /// <summary>The message.</summary>
    public string Message { get; } = message;
}

/// <summary>
/// One Designer window's worth of state that is not drawing: the design file it came from, whether it has unsaved
/// changes, the title that says so, the activity log, and the New / Open / Save / rename workflow. Both Designer
/// apps run on one of these and differ only in the <see cref="IDesignerDialogs"/> they give it.
/// </summary>
/// <remarks>
/// <para>
/// The session keeps the <see cref="MarinaDocument"/> a design was opened from and saves through
/// <see cref="MarinaDocument.UpdateFrom"/>, so whatever this version does not understand — a host's own sections,
/// newer properties, the description — is written back rather than dropped.
/// </para>
/// <para>
/// It listens to the designer and the marina for everything that changes the design, marks it dirty and writes the
/// activity log, so neither app has to. Changes made outside the designer (the look settings, the camera views) are
/// reported with <see cref="MarkDirty"/>.
/// </para>
/// </remarks>
public sealed class DesignerSession : IDisposable
{
    /// <summary>
    /// How much of the scene's haze is drawn while the marina is being traced. Damped, so distant shapes stay crisp;
    /// the look settings put it back to full while they are open, since the haze is one of the things they set. It is the
    /// designer's own default.
    /// </summary>
    public static float DesigningFogFactor => DesignerLimits.FogFactor.Default;

    /// <summary>How far back the camera starts on a new, empty marina, in meters.</summary>
    public const float NewMarinaCameraDistance = 220f;

    private readonly IDesignerDialogs _dialogs;
    private readonly string _generator;
    private int _busy;
    private bool _disposed;

    /// <summary>Starts a session over a marina. Call <see cref="NewAsync"/> to begin with an empty design.</summary>
    /// <param name="marina">The marina the window shows.</param>
    /// <param name="dialogs">How this app asks the user things.</param>
    /// <param name="generator">What a saved file says wrote it, e.g. "VirtualMarina Designer 1.2.0".</param>
    /// <param name="time">The clock for the activity log; the system clock when null.</param>
    public DesignerSession(MarinaVisualizer marina, IDesignerDialogs dialogs, string generator, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(marina);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator);
        Marina = marina;
        _dialogs = dialogs;
        _generator = generator;
        Log = new ActivityLog(time);
        Title = DesignerText.WindowTitle(dirty: false, marina.MarinaName);

        var designer = marina.Designer;
        designer.ElementCreated += OnElementCreated;
        designer.ElementErased += OnElementErased;
        designer.TreesPlanted += OnTreesPlanted;
        designer.ActionUndone += OnActionUndone;
        designer.ActionRedone += OnActionRedone;
        designer.ActionFailed += OnActionFailed;
        designer.ScaleLineDrawn += OnScaleLineDrawn;
        designer.ReferenceImageChanged += OnReferenceImageChanged;
        designer.ElementRenaming += OnElementRenaming;
        marina.LayoutChanged += OnLayoutChanged;
    }

    /// <summary>The marina the window shows.</summary>
    public MarinaVisualizer Marina { get; }

    /// <summary>The marina's designer.</summary>
    public MarinaDesigner Designer => Marina.Designer;

    /// <summary>What has happened, newest first.</summary>
    public ActivityLog Log { get; }

    /// <summary>True when the design has changes that are not saved.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>The design file's name, or null for a design never saved or opened.</summary>
    public string? FileName { get; private set; }

    /// <summary>Where the design file is, for Save to write back to: a path, a browser file handle's key, or null.</summary>
    public string? FileLocation { get; private set; }

    /// <summary>The document the design was opened from or last saved to; null for a new design.</summary>
    public MarinaDocument? Document { get; private set; }

    /// <summary>The window title: an unsaved marker, the file or marina name, and the app's name.</summary>
    public string Title { get; private set; }

    /// <summary>Which settings sit beside the marina.</summary>
    public DesignerSidePanel SidePanel { get; private set; }

    /// <summary>
    /// True while a question is being asked (Save first?, a file picker, the rename dialog), so a second command that
    /// would ask its own can wait rather than stack another dialog on top.
    /// </summary>
    public bool IsBusy => Volatile.Read(ref _busy) > 0;

    /// <summary>The title, the file or the unsaved state changed.</summary>
    public event EventHandler? TitleChanged;

    /// <summary>A new design replaced the one shown (New or Open): every panel should read the marina afresh.</summary>
    public event EventHandler? DocumentReplaced;

    /// <summary>The side panel changed.</summary>
    public event EventHandler? SidePanelChanged;

    /// <summary>
    /// Something the user should see without being stopped: the designer refused a change (a name already taken,
    /// say). The same text is in the log; show it as a passing notice.
    /// </summary>
    public event EventHandler<DesignerNoticeEventArgs>? Notice;

    /// <summary>Marks the design as changed, e.g. after a look setting or a camera view changed.</summary>
    public void MarkDirty()
    {
        if (IsDirty) return;
        IsDirty = true;
        UpdateTitle();
    }

    /// <summary>Works the title out again, e.g. after the marina was renamed.</summary>
    public void UpdateTitle()
    {
        var title = DesignerText.WindowTitle(IsDirty, FileName ?? Marina.MarinaName);
        if (string.Equals(title, Title, StringComparison.Ordinal)) return;
        Title = title;
        TitleChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- File ---------------------------------------------------------------------------------------

    /// <summary>
    /// Replaces the design with an empty one: a fresh style, no saved views, every automatic view switched on and no
    /// tracing image. The designer's tool settings are the user's, not the design's, so they stay.
    /// </summary>
    /// <param name="askToSave">Ask about unsaved changes first; false when starting up.</param>
    /// <returns>False when the user chose to keep the design there is.</returns>
    public async Task<bool> NewAsync(bool askToSave = true)
    {
        using var busy = Busy();
        if (askToSave && !await ConfirmDiscardChangesCoreAsync()) return false;

        new MarinaDocument { Name = Strings.NewMarinaName }.ApplyTo(Marina, applyDesignerSettings: false);
        Designer.ClearHistory();
        Designer.IsActive = true;
        Designer.Tool = DesignTool.Navigate;
        Designer.ViewTopDown(immediate: true);
        Marina.Camera.SetPose(Marina.Camera.DesiredPose with { Distance = NewMarinaCameraDistance }, immediate: true);
        SetFile(null, null, null, dirty: false);
        DocumentReplaced?.Invoke(this, EventArgs.Empty);
        Log.Add(Strings.LogNewMarina);
        return true;
    }

    /// <summary>Asks about unsaved changes, then for a design file, and opens it.</summary>
    /// <returns>True when a design was opened.</returns>
    public async Task<bool> OpenAsync()
    {
        using var busy = Busy();
        if (!await ConfirmDiscardChangesCoreAsync()) return false;

        var file = await _dialogs.PickDesignAsync();
        return file is not null && await OpenFileAsync(file);
    }

    /// <summary>Opens a design file already picked, without asking about unsaved changes.</summary>
    /// <param name="file">The file.</param>
    /// <returns>True when it opened; false when it could not be read (the user has been told why).</returns>
    public async Task<bool> OpenFileAsync(DesignerOpenedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        MarinaDocument document;
        try
        {
            document = file.Read();
            document.ApplyTo(Marina);
        }
        catch (Exception ex) when (ex is MarinaFormatException or MarinaLayoutException or IOException or UnauthorizedAccessException)
        {
            await WarnAsync(Strings.OpenFailed, ex);
            return false;
        }

        Designer.ClearHistory();
        Designer.IsActive = true;
        Designer.Tool = DesignTool.Navigate;
        SetFile(file.Name, file.Location, document, dirty: false);
        DocumentReplaced?.Invoke(this, EventArgs.Empty);
        Log.Add(Strings.Format(Strings.LogOpened, file.Name, document.Layout.Berths.Count, document.Layout.Piers.Count) +
            (document.IsFromNewerVersion ? Strings.Format(Strings.LogOpenedNewerVersion, document.Version) : string.Empty));
        return true;
    }

    /// <summary>
    /// Saves the design: to the file it came from, or asking where when <paramref name="saveAs"/> is set or it was
    /// never saved. Unsaved changes stay marked when the user cancels or the write fails.
    /// </summary>
    /// <param name="saveAs">Ask where even when the design has a file.</param>
    /// <returns>True when it was saved.</returns>
    public async Task<bool> SaveAsync(bool saveAs = false)
    {
        using var busy = Busy();
        return await SaveCoreAsync(saveAs);
    }

    /// <summary>
    /// Asks whether to save unsaved changes before they would be lost, and saves them if asked to. True when it is
    /// fine to go on: nothing was unsaved, the user saved, or chose not to.
    /// </summary>
    public async Task<bool> ConfirmDiscardChangesAsync()
    {
        using var busy = Busy();
        return await ConfirmDiscardChangesCoreAsync();
    }

    /// <summary>Asks for the marina's name.</summary>
    /// <returns>True when it changed.</returns>
    public async Task<bool> EditMarinaPropertiesAsync()
    {
        using var busy = Busy();
        var answer = await _dialogs.PromptAsync(new DesignerPrompt(
            Strings.MarinaNameTitle, [new DesignerPromptField(Strings.MarinaNameQuestion, Marina.MarinaName)]));
        if (answer is not { Count: > 0 } || string.IsNullOrWhiteSpace(answer[0])) return false;

        var name = answer[0].Trim();
        if (string.Equals(name, Marina.MarinaName, StringComparison.Ordinal)) return false;
        Marina.MarinaName = name;
        MarkDirty();
        UpdateTitle();
        return true;
    }

    private async Task<bool> ConfirmDiscardChangesCoreAsync()
    {
        if (!IsDirty) return true;
        var choice = await _dialogs.AskToSaveChangesAsync(Strings.Format(Strings.ConfirmDiscard, FileName ?? Marina.MarinaName));
        return choice switch
        {
            SaveChangesChoice.Discard => true,
            SaveChangesChoice.Save => await SaveCoreAsync(saveAs: false),
            _ => false,
        };
    }

    private async Task<bool> SaveCoreAsync(bool saveAs)
    {
        var document = Document ?? new MarinaDocument();
        document.UpdateFrom(Marina, _generator);
        var suggested = FileName ?? DesignerText.SanitizeFileName(Marina.MarinaName) + MarinaDocument.FileExtension;

        DesignerSavedFile? saved;
        try
        {
            saved = await _dialogs.SaveDesignAsync(new DesignerSaveRequest(document, suggested, saveAs ? null : FileLocation));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await WarnAsync(Strings.SaveFailed, ex);
            return false;
        }

        if (saved is null) return false;
        SetFile(saved.Name, saved.Location, document, dirty: false);
        Log.Add(Strings.Format(Strings.LogSaved, saved.Name, document.Layout.Berths.Count));
        return true;
    }

    private void SetFile(string? name, string? location, MarinaDocument? document, bool dirty)
    {
        FileName = name;
        FileLocation = location;
        Document = document;
        IsDirty = dirty;
        var title = DesignerText.WindowTitle(IsDirty, FileName ?? Marina.MarinaName);
        Title = title;
        TitleChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- Commands that belong to the design --------------------------------------------------------

    /// <summary>True when Undo would do something: take back a point of the drawing in progress, or undo the last change.</summary>
    public bool CanUndo => Designer.HasDraft || Designer.CanUndo;

    /// <summary>True when Redo would make an undone change again.</summary>
    public bool CanRedo => Designer.CanRedo;

    /// <summary>
    /// Edit ▸ Undo, however it was asked for: the same as Ctrl+Z in the view. While drawing it takes back the last point;
    /// otherwise it undoes the last change, and a refusal is reported through the designer's <c>ActionFailed</c> (and so
    /// the log) rather than thrown.
    /// </summary>
    /// <returns>True when something was taken back.</returns>
    public bool Undo() => Designer.TryUndo();

    /// <summary>Edit ▸ Redo, the same as Ctrl+Y in the view; a refusal is reported rather than thrown.</summary>
    /// <returns>True when a change was made again.</returns>
    public bool Redo() => Designer.TryRedo();

    /// <summary>Shows or hides the berth names, which is saved with the design.</summary>
    public void ToggleBerthLabels()
    {
        Marina.BerthLabelMode = Marina.BerthLabelMode == BerthLabelMode.None ? BerthLabelMode.All : BerthLabelMode.None;
        MarkDirty();
        Log.Add(Marina.BerthLabelMode == BerthLabelMode.None ? Strings.LogBerthLabelsHidden : Strings.LogBerthLabelsShown);
    }

    /// <summary>Takes the mainland away, leaving the marina in open water. Undo brings it back.</summary>
    /// <returns>False when there was none.</returns>
    public bool RemoveShoreline()
    {
        if (!Designer.DeleteShoreline()) return false;
        MarkDirty();
        Log.Add(Strings.LogCoastRemoved);
        return true;
    }

    /// <summary>
    /// Straight down on the whole marina. The automatic view knows how far back that has to be; the designer's own
    /// top-down only turns the camera and leaves it wherever it was.
    /// </summary>
    public void ViewTopDown()
    {
        if (!Marina.ApplyBuiltInCameraPreset(MarinaVisualizer.TopDownPresetName)) Designer.ViewTopDown();
    }

    /// <summary>
    /// Chooses what sits beside the marina. While the look settings are up the scene is drawn with its full haze,
    /// since that is one of the things they set; otherwise it is damped so the shapes being traced stay crisp.
    /// Neither is a change to the design.
    /// </summary>
    /// <param name="panel">What to show under the tool's settings.</param>
    public void ShowSidePanel(DesignerSidePanel panel)
    {
        Designer.FogFactor = panel == DesignerSidePanel.Look ? 1f : DesigningFogFactor;
        if (panel == SidePanel) return;
        SidePanel = panel;
        SidePanelChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Opens a side panel, or goes back to the tool settings alone when it is already open.</summary>
    /// <param name="panel">The panel.</param>
    public void ToggleSidePanel(DesignerSidePanel panel) =>
        ShowSidePanel(SidePanel == panel ? DesignerSidePanel.Tools : panel);

    // ---- Rename -------------------------------------------------------------------------------------

    /// <summary>
    /// Asks for a new name, and keeps asking while the answer is one something else already has, then applies it as
    /// one step for Undo. A berth's name is its id and a pier's id is what its berths are named after, so neither may
    /// collide.
    /// </summary>
    /// <param name="request">What is being renamed.</param>
    /// <returns>True when anything was renamed.</returns>
    public async Task<bool> RenameAsync(RenameRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var busy = Busy();
        var answer = RenamePlanner.Initial(request);
        while (true)
        {
            var typed = RenamePlanner.Read(request, await _dialogs.PromptAsync(RenamePlanner.Prompt(request, answer)));
            if (typed is null) return false;
            answer = typed;

            var problem = RenamePlanner.Check(Designer, request, answer);
            if (problem is null) break;
            Log.Add(problem.LogMessage);
            await _dialogs.AlertAsync(problem.Title, problem.Message, DesignerMessageKind.Warning);
        }

        RenameOutcome outcome;
        try
        {
            outcome = RenamePlanner.Apply(Designer, request, answer);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            Report(Strings.Format(Strings.ActionFailed, request.CurrentName, ex.Message));
            return false;
        }

        foreach (var message in outcome.Messages) Log.Add(message);
        if (outcome.Changed) MarkDirty();
        return outcome.Changed;
    }

    /// <summary>
    /// The designer asks for a name in the middle of handling a click, which is no place for a dialog. The click is
    /// let go and the questions are asked once it has returned; the answer goes in through the public API.
    /// </summary>
    private void OnElementRenaming(object? sender, DesignElementRenamingEventArgs e)
    {
        e.Cancel = true;
        _ = RenameLaterAsync(RenameRequest.From(e, Marina));
    }

    private async Task RenameLaterAsync(RenameRequest request)
    {
        // Off the designer's call stack: the click finishes first, then the dialog opens.
        await Task.Yield();
        if (_disposed) return;
        await RenameAsync(request);
    }

    // ---- Messages -----------------------------------------------------------------------------------

    /// <summary>Logs a failure and tells the user, waiting until the message is dismissed.</summary>
    /// <param name="title">What failed, e.g. "The design could not be opened".</param>
    /// <param name="ex">Why.</param>
    public async Task WarnAsync(string title, Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        Log.Add(Strings.Format(Strings.LogFailed, title, ex.Message));
        await _dialogs.AlertAsync(Strings.AppName, Strings.Format(Strings.WarnBody, title, ex.Message), DesignerMessageKind.Warning);
    }

    /// <summary>Logs a message and raises <see cref="Notice"/> for it.</summary>
    /// <param name="message">What to tell the user.</param>
    public void Report(string message)
    {
        Log.Add(message);
        Notice?.Invoke(this, new DesignerNoticeEventArgs(message));
    }

    // ---- Designer events ----------------------------------------------------------------------------

    private void OnLayoutChanged(object? sender, LayoutChangedEventArgs e) => MarkDirty();

    private void OnElementCreated(object? sender, DesignElementCreatedEventArgs e)
    {
        MarkDirty();
        Log.Add(DesignerLogText.Created(e));
    }

    private void OnElementErased(object? sender, DesignElementErasedEventArgs e)
    {
        MarkDirty();
        Log.Add(DesignerLogText.Erased(e));
    }

    private void OnTreesPlanted(object? sender, DesignTreesPlantedEventArgs e)
    {
        MarkDirty();
        Log.Add(DesignerLogText.TreesPlanted(e));
    }

    private void OnActionUndone(object? sender, DesignActionUndoneEventArgs e)
    {
        MarkDirty();
        Log.Add(DesignerLogText.Undone(e));
    }

    private void OnActionRedone(object? sender, DesignActionRedoneEventArgs e)
    {
        MarkDirty();
        Log.Add(DesignerLogText.Redone(e));
    }

    private void OnActionFailed(object? sender, DesignActionFailedEventArgs e) => Report(DesignerLogText.Failed(e));

    private void OnScaleLineDrawn(object? sender, ScaleLineDrawnEventArgs e) => Log.Add(DesignerLogText.ScaleLine(e));

    private void OnReferenceImageChanged(object? sender, ReferenceImageChangedEventArgs e)
    {
        // The picture, its place, scale and look are all saved with the design.
        MarkDirty();
        Log.Add(DesignerLogText.ReferenceImage(e));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var designer = Marina.Designer;
        designer.ElementCreated -= OnElementCreated;
        designer.ElementErased -= OnElementErased;
        designer.TreesPlanted -= OnTreesPlanted;
        designer.ActionUndone -= OnActionUndone;
        designer.ActionRedone -= OnActionRedone;
        designer.ActionFailed -= OnActionFailed;
        designer.ScaleLineDrawn -= OnScaleLineDrawn;
        designer.ReferenceImageChanged -= OnReferenceImageChanged;
        designer.ElementRenaming -= OnElementRenaming;
        Marina.LayoutChanged -= OnLayoutChanged;
    }

    private BusyScope Busy()
    {
        Interlocked.Increment(ref _busy);
        return new BusyScope(this);
    }

    /// <summary>Counts a question as being asked until it is disposed.</summary>
    private readonly struct BusyScope(DesignerSession session) : IDisposable
    {
        public void Dispose() => Interlocked.Decrement(ref session._busy);
    }
}
