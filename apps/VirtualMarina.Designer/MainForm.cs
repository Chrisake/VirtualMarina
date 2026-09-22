using System.Globalization;
using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Serialization;
using VirtualMarina.Designer.Resources;
using VirtualMarina.WinForms;

namespace VirtualMarina.Designer;

/// <summary>
/// The designer window: a fixed toolbar of drawing tools at the top, the 3D marina filling the window, and a panel beside it that
/// only ever shows the settings of the tool in hand. Designs are saved as <c>.marina.json</c> files the host application loads.
/// </summary>
internal sealed class MainForm : Form
{
    private static string AppName => Strings.AppName;

    private readonly MarinaViewControl _view = new() { Dock = DockStyle.Fill };
    private readonly InspectorPanel _inspector;
    private readonly ToolStrip _toolbar = new();
    private readonly Dictionary<DesignTool, ToolStripButton> _toolButtons = new();
    private readonly ToolStripButton _undoButton = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusHint = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _statusPointer = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleRight };
    private readonly ToolStripStatusLabel _statusCamera = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleRight };
    private readonly ListBox _log = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 8.5f), IntegralHeight = false };
    private readonly Panel _logPanel = new() { Dock = DockStyle.Bottom, Height = 150, Visible = false, Padding = new Padding(8, 6, 8, 8), BackColor = Theme.Surface };
    private readonly ToolStripMenuItem _logMenuItem = new(Strings.MenuShowLog) { CheckOnClick = true };

    private string? _filePath;
    private bool _dirty;

    public MainForm()
    {
        Text = AppName;
        MinimumSize = new Size(1080, 720);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        BackColor = Theme.Background;
        Font = Theme.Body;
        KeyPreview = true;

        Marina.Designer.IsActive = true;
        Marina.Designer.Tool = DesignTool.Navigate;
        _inspector = new InspectorPanel(Marina, LoadReferenceImage);

        BuildMenu();
        BuildToolbar();
        BuildStatusBar();
        BuildLayout();
        WireEvents();

        NewMarina(askToSave: false);
    }

    private MarinaVisualizer Marina => _view.Marina;

    private MarinaDesigner Designer => Marina.Designer;

    /// <summary>Narrowest the settings panel may be dragged: below this its rows stop fitting.</summary>
    private const int MinInspectorWidth = 300;

    /// <summary>Widest it may be dragged: past this it takes room from the marina without gaining anything.</summary>
    private const int MaxInspectorWidth = 560;

    /// <summary>Least width left for the marina view.</summary>
    private const int MinViewWidth = 360;

    // ---- Shell ---------------------------------------------------------------------------------

    private void BuildLayout()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 1,
            BackColor = Theme.Border,
            Width = 1200,
            Panel1MinSize = MinViewWidth,
            Panel2MinSize = MinInspectorWidth,
        };
        split.Panel1.Controls.Add(_view);
        split.Panel2.Controls.Add(_inspector);

        var logHeader = new Label
        {
            Text = Strings.LogHeader,
            Dock = DockStyle.Top,
            Font = Theme.Caption,
            ForeColor = Theme.TextSoft,
            Height = 20,
        };
        _logPanel.Controls.Add(_log);
        _logPanel.Controls.Add(logHeader);

        var center = new Panel { Dock = DockStyle.Fill };
        center.Controls.Add(split);
        center.Controls.Add(_logPanel);

        Controls.Add(center);
        Controls.Add(_toolbar);
        Controls.Add(MainMenuStrip);
        Controls.Add(_status);

        // The inspector keeps its width while the window is resized; the splitter can only be set once the form has a size.
        split.SizeChanged += (_, _) => LimitInspectorWidth(split);
        Shown += (_, _) =>
        {
            LimitInspectorWidth(split);
            SetInspectorWidth(split, 316);
        };
    }

    private void BuildMenu()
    {
        var file = new ToolStripMenuItem(Strings.MenuFile);
        file.DropDownItems.Add(Menu(Strings.MenuNew, Keys.Control | Keys.N, () => NewMarina(askToSave: true)));
        file.DropDownItems.Add(Menu(Strings.MenuOpen, Keys.Control | Keys.O, OpenDesign));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Menu(Strings.MenuSave, Keys.Control | Keys.S, () => SaveDesign(saveAs: false)));
        file.DropDownItems.Add(Menu(Strings.MenuSaveAs, Keys.Control | Keys.Shift | Keys.S, () => SaveDesign(saveAs: true)));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Menu(Strings.MenuLoadImage, Keys.Control | Keys.I, LoadReferenceImage));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Menu(Strings.MenuExit, Keys.Alt | Keys.F4, Close));

        var edit = new ToolStripMenuItem(Strings.MenuEdit);
        edit.DropDownItems.Add(Menu(Strings.MenuUndo, Keys.Control | Keys.Z, () => Designer.Undo()));
        edit.DropDownItems.Add(Menu(Strings.MenuCancelDraft, Keys.None, () => Designer.CancelDraft()));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(Menu(Strings.MenuRename, Keys.F2, () => Designer.Tool = DesignTool.Rename));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(Menu(Strings.MenuMarinaProperties, Keys.None, EditMarinaProperties));

        var view = new ToolStripMenuItem(Strings.MenuView);
        view.DropDownItems.Add(Menu(Strings.MenuTopView, Keys.Control | Keys.T, () => Designer.ViewTopDown()));
        view.DropDownItems.Add(Menu(Strings.MenuFitMarina, Keys.Control | Keys.F, () => Marina.ResetCamera()));
        view.DropDownItems.Add(Menu(Strings.MenuFitImage, Keys.None, () => Designer.FocusReferenceImage()));
        view.DropDownItems.Add(new ToolStripSeparator());
        _logMenuItem.CheckedChanged += (_, _) => _logPanel.Visible = _logMenuItem.Checked;
        view.DropDownItems.Add(_logMenuItem);

        var marina = new ToolStripMenuItem(Strings.MenuMarina);
        marina.DropDownItems.Add(Menu(Strings.MenuAppearance, Keys.None, ShowAppearance));
        marina.DropDownItems.Add(Menu(Strings.MenuBerthLabels, Keys.None, ToggleLabels));

        var help = new ToolStripMenuItem(Strings.MenuHelp);
        help.DropDownItems.Add(Menu(Strings.MenuShortcuts, Keys.F1, ShowShortcuts));
        help.DropDownItems.Add(Menu(Strings.MenuAbout, Keys.None, ShowAbout));

        MainMenuStrip = new MenuStrip { BackColor = Theme.Surface, Font = Theme.Body, Padding = new Padding(6, 2, 0, 2) };
        MainMenuStrip.Items.AddRange(new ToolStripItem[] { file, edit, view, marina, help });
    }

    private static ToolStripMenuItem Menu(string text, Keys shortcut, Action action)
    {
        var item = new ToolStripMenuItem(text, null, (_, _) => action());
        if (shortcut != Keys.None)
        {
            item.ShortcutKeys = shortcut;
            item.ShowShortcutKeys = true;
        }

        return item;
    }

    private void BuildToolbar()
    {
        _toolbar.Dock = DockStyle.Top;
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;
        _toolbar.Renderer = new Theme.ToolbarRenderer();
        _toolbar.Padding = new Padding(8, 6, 8, 6);
        _toolbar.ImageScalingSize = new Size(20, 20);
        _toolbar.Font = new Font("Segoe UI", 9.5f);

        AddToolButton(DesignTool.Navigate, Strings.ToolNavigate, Strings.ToolNavigateTip);
        _toolbar.Items.Add(new ToolStripSeparator());
        AddToolButton(DesignTool.DrawLandArea, Strings.ToolLand, Strings.ToolLandTip);
        AddToolButton(DesignTool.DrawPier, Strings.ToolPier, Strings.ToolPierTip);
        AddToolButton(DesignTool.AddBerths, Strings.ToolBerths, Strings.ToolBerthsTip);
        AddToolButton(DesignTool.AddLandBerths, Strings.ToolAshore, Strings.ToolAshoreTip);
        AddToolButton(DesignTool.PlantTrees, Strings.ToolTrees, Strings.ToolTreesTip);
        AddToolButton(DesignTool.Erase, Strings.ToolErase, Strings.ToolEraseTip);
        AddToolButton(DesignTool.Rename, Strings.ToolRename, Strings.ToolRenameTip);
        _toolbar.Items.Add(new ToolStripSeparator());

        _undoButton.Text = Strings.Undo;
        _undoButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _undoButton.Padding = new Padding(10, 4, 10, 4);
        _undoButton.ToolTipText = Strings.UndoTip;
        _undoButton.Click += (_, _) => Designer.Undo();
        _toolbar.Items.Add(_undoButton);

        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(Command(Strings.CommandTopView, Strings.CommandTopViewTip, () => Designer.ViewTopDown()));
        _toolbar.Items.Add(Command(Strings.CommandFitMarina, Strings.CommandFitMarinaTip, () => Marina.ResetCamera()));
        _toolbar.Items.Add(Command(Strings.CommandReferenceImage, Strings.CommandReferenceImageTip, LoadReferenceImage));
    }

    /// <summary>
    /// Holds the settings panel between <see cref="MinInspectorWidth"/> and <see cref="MaxInspectorWidth"/>. The
    /// maximum is expressed as a minimum width for the marina view, so the splitter simply stops there while being
    /// dragged instead of springing back.
    /// </summary>
    private static void LimitInspectorWidth(SplitContainer split)
    {
        var available = split.Width - split.SplitterWidth;
        if (available <= MinInspectorWidth) return; // The window is too narrow to honour anything; leave it alone.

        // Both minimums have to fit, whatever the window size, or SplitContainer throws.
        var viewMinimum = Math.Max(MinViewWidth, available - MaxInspectorWidth);
        split.Panel2MinSize = Math.Min(MinInspectorWidth, available - MinViewWidth);
        split.Panel1MinSize = Math.Min(viewMinimum, available - split.Panel2MinSize);

        // A window that shrank can leave the splitter outside the new bounds.
        SetInspectorWidth(split, available - split.SplitterDistance);
    }

    private static void SetInspectorWidth(SplitContainer split, int width)
    {
        var available = split.Width - split.SplitterWidth;
        var distance = Math.Clamp(available - width, split.Panel1MinSize, Math.Max(split.Panel1MinSize, available - split.Panel2MinSize));
        if (distance != split.SplitterDistance) split.SplitterDistance = distance;
    }

    private void AddToolButton(DesignTool tool, string text, string tip)
    {
        var button = new ToolStripButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            Padding = new Padding(12, 4, 12, 4),
            ToolTipText = tip,
            ForeColor = Theme.Text,
        };
        button.Click += (_, _) =>
        {
            Designer.IsActive = true;
            Designer.Tool = tool;
        };
        _toolButtons[tool] = button;
        _toolbar.Items.Add(button);
    }

    private static ToolStripButton Command(string text, string tip, Action action)
    {
        var button = new ToolStripButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            Padding = new Padding(10, 4, 10, 4),
            ToolTipText = tip,
            ForeColor = Theme.Text,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void BuildStatusBar()
    {
        _status.BackColor = Theme.Surface;
        _status.Font = Theme.Small;
        _status.SizingGrip = false;
        _status.Items.AddRange(new ToolStripItem[] { _statusHint, _statusPointer, _statusCamera });
    }

    private void WireEvents()
    {
        var designer = Designer;
        designer.StateChanged += (_, _) => RefreshUi();
        designer.ElementCreated += (_, e) =>
        {
            MarkDirty();
            Log(e switch
            {
                { LandArea: { } land } => Strings.Format(Strings.LogAddedLand, land.DisplayName, land.Points.Count, land.Area),
                { Pier: { } pier } => Strings.Format(Strings.LogAddedPier, pier.Id, pier.Length, pier.Width),
                { Berths.Count: 1 } => Strings.Format(Strings.LogAddedBerth, e.Berths[0].Id),
                _ => Strings.Format(Strings.LogAddedBerths, e.Berths.Count, string.Join(", ", e.Berths.Select(s => s.Id))),
            });
        };
        designer.ElementErased += (_, e) =>
        {
            MarkDirty();
            Log(Strings.Format(Strings.LogRemoved, e.Element.GetType().Name.ToLowerInvariant(), e.RemovedBerths.Count, e.RemovedDividers.Count));
        };
        designer.TreesPlanted += (_, e) =>
        {
            MarkDirty();
            Log(Strings.Format(Strings.LogTrees, e.LandArea.DisplayName, e.LandArea.Trees.Count, e.PreviousCount));
        };
        designer.ActionUndone += (_, e) =>
        {
            MarkDirty();
            Log(Strings.Format(Strings.LogUndone, e.Description));
        };
        designer.ScaleLineDrawn += (_, e) => Log(Strings.Format(Strings.LogScaleLine, e.MeasuredLength));
        designer.ReferenceImageChanged += (_, e) => Log(Strings.Format(Strings.LogReferenceImage, e.Change.ToString().ToLowerInvariant(), e.MetersPerPixel));

        _view.LayoutChanged += (_, _) => MarkDirty();
        designer.ElementRenaming += (_, e) => AskForName(e);
        _view.RenderError += (_, e) => Log(Strings.Format(Strings.LogRendererError, e.Exception.Message));
        _view.MouseMove += (_, e) => ShowPointer(e.Location);
        _view.MouseLeave += (_, _) => _statusPointer.Text = string.Empty;
        FormClosing += (_, e) => e.Cancel = !ConfirmDiscardChanges();

        // Esc always comes back to navigation, even when the view doesn't have the focus.
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape && !Designer.HasDraft) Designer.Tool = DesignTool.Navigate;
        };
    }

    // ---- State ---------------------------------------------------------------------------------

    private void RefreshUi()
    {
        if (IsDisposed) return;
        foreach (var (tool, button) in _toolButtons) button.Checked = Designer.Tool == tool;
        _undoButton.Enabled = Designer.CanUndo;
        _undoButton.ToolTipText = Designer.CanUndo ? Strings.Format(Strings.UndoTipWith, Designer.UndoDescription) : Strings.NothingToUndo;
        _statusHint.Text = Designer.ToolHint;
        var pose = Marina.Camera.Pose;
        _statusCamera.Text = string.Format(CultureInfo.CurrentCulture, Strings.StatusCamera, pose.Distance, pose.PitchDegrees);
        _inspector.Sync();
    }

    private void ShowPointer(Point location)
    {
        var point = Designer.PointerPosition ?? (Marina.GetWaterPoint(location.X, location.Y) is { } world ? new Vector2(world.X, world.Z) : (Vector2?)null);
        _statusPointer.Text = point is { } p ? string.Format(CultureInfo.CurrentCulture, Strings.StatusPointer, p.X, p.Y) : string.Empty;
        var pose = Marina.Camera.Pose;
        _statusCamera.Text = string.Format(CultureInfo.CurrentCulture, Strings.StatusCamera, pose.Distance, pose.PitchDegrees);
    }

    private void MarkDirty()
    {
        if (_dirty) return;
        _dirty = true;
        UpdateTitle();
    }

    private void UpdateTitle() =>
        Text = Strings.Format(Strings.WindowTitle, _dirty ? Strings.UnsavedMarker : string.Empty, Path.GetFileName(_filePath) ?? Marina.MarinaName, AppName);

    private void Log(string message)
    {
        _log.Items.Insert(0, Strings.Format(Strings.LogEntry, DateTime.Now, message));
        while (_log.Items.Count > 500) _log.Items.RemoveAt(_log.Items.Count - 1);
    }

    // ---- File ----------------------------------------------------------------------------------

    private void NewMarina(bool askToSave)
    {
        if (askToSave && !ConfirmDiscardChanges()) return;

        Marina.Style = new Core.Rendering.MarinaStyle();
        Marina.InitializeLayout(new MarinaLayout { Name = Strings.NewMarinaName });
        Designer.ClearHistory();
        Designer.ClearReferenceImage();
        Designer.IsActive = true;
        Designer.Tool = DesignTool.Navigate;
        Designer.ViewTopDown(immediate: true);
        Marina.Camera.SetPose(Marina.Camera.DesiredPose with { Distance = 220f }, immediate: true);
        _filePath = null;
        _dirty = false;
        UpdateTitle();
        RefreshUi();
        Log(Strings.LogNewMarina);
    }

    private void OpenDesign()
    {
        if (!ConfirmDiscardChanges()) return;
        using var dialog = new OpenFileDialog { Filter = MarinaDocument.FileDialogFilter, Title = Strings.OpenDesignTitle };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var document = MarinaDocument.Load(dialog.FileName);
            document.ApplyTo(Marina);
            DecodeReferenceImage();
            _filePath = dialog.FileName;
            _dirty = false;
            Designer.ClearHistory();
            Designer.IsActive = true;
            Designer.Tool = DesignTool.Navigate;
            UpdateTitle();
            RefreshUi();
            Log(Strings.Format(Strings.LogOpened, Path.GetFileName(dialog.FileName), document.Layout.Berths.Count, document.Layout.Piers.Count) +
                (document.IsFromNewerVersion ? Strings.Format(Strings.LogOpenedNewerVersion, document.Version) : string.Empty));
        }
        catch (Exception ex) when (ex is MarinaFormatException or MarinaLayoutException or IOException or UnauthorizedAccessException)
        {
            Warn(Strings.OpenFailed, ex);
        }
    }

    private void SaveDesign(bool saveAs)
    {
        var path = _filePath;
        if (saveAs || path is null)
        {
            using var dialog = new SaveFileDialog
            {
                Filter = MarinaDocument.FileDialogFilter,
                Title = Strings.SaveDesignTitle,
                FileName = path is null ? Sanitize(Marina.MarinaName) + MarinaDocument.FileExtension : Path.GetFileName(path),
                AddExtension = true,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            path = dialog.FileName;
        }

        try
        {
            var document = MarinaDocument.FromVisualizer(Marina, $"{AppName} {Application.ProductVersion}");
            document.Save(path);
            _filePath = path;
            _dirty = false;
            UpdateTitle();
            Log(Strings.Format(Strings.LogSaved, Path.GetFileName(path), document.Layout.Berths.Count));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warn(Strings.SaveFailed, ex);
        }
    }

    /// <summary>Returns false when the user wants to keep unsaved work.</summary>
    private bool ConfirmDiscardChanges()
    {
        if (!_dirty) return true;
        var answer = MessageBox.Show(
            this,
            Strings.Format(Strings.ConfirmDiscard, Path.GetFileName(_filePath) ?? Marina.MarinaName),
            AppName,
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        if (answer == DialogResult.Cancel) return false;
        if (answer == DialogResult.No) return true;
        SaveDesign(saveAs: false);
        return !_dirty;
    }

    private void LoadReferenceImage()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = ReferenceImageLoader.FileDialogFilter,
            Title = Strings.OpenImageTitle,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _view.LoadReferenceImage(dialog.FileName);
            Designer.FocusReferenceImage();
            Designer.Tool = DesignTool.MeasureScale;
            Log(Strings.Format(Strings.LogImageLoaded, Path.GetFileName(dialog.FileName)));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or OutOfMemoryException or UnauthorizedAccessException)
        {
            Warn(Strings.ImageLoadFailed, ex);
        }
    }

    // ---- Dialogs -------------------------------------------------------------------------------

    private void ShowAppearance()
    {
        using var form = new AppearanceForm(Marina);
        form.ShowDialog(this);
        MarkDirty();
    }

    private void ToggleLabels()
    {
        Marina.BerthLabelMode = Marina.BerthLabelMode == BerthLabelMode.None ? BerthLabelMode.All : BerthLabelMode.None;
        MarkDirty();
        Log(Marina.BerthLabelMode == BerthLabelMode.None ? Strings.LogBerthLabelsHidden : Strings.LogBerthLabelsShown);
    }

    /// <summary>
    /// A design stores its tracing picture as the original PNG or JPEG, which the OpenGL renderer cannot upload.
    /// This decodes it to pixels once, after loading, keeping the file bytes so the next save still carries it.
    /// </summary>
    private void DecodeReferenceImage()
    {
        if (Designer.ReferenceImage is not { Rgba: null } stored) return;

        try
        {
            var center = Designer.ReferenceImageCenter;
            var metersPerPixel = Designer.ReferenceImageMetersPerPixel;
            Designer.SetReferenceImage(ReferenceImageLoader.Decoded(stored), metersPerPixel, center);
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException)
        {
            // The design still opens; only the picture behind it is lost.
            Designer.ClearReferenceImage();
            Log(Strings.Format(Strings.LogFailed, Strings.ImageLoadFailed, ex.Message));
        }
    }

    /// <summary>Answers the designer's rename request with a name typed by the user.</summary>
    private void AskForName(DesignElementRenamingEventArgs e)
    {
        var berth = e.Berth is not null;

        // A pier has a display name and an id its berths point at, so it is asked for both.
        using var form = new TextInputForm(
            berth ? Strings.RenameBerthTitle : Strings.RenamePierTitle,
            berth ? Strings.RenameBerthQuestion : Strings.RenamePierQuestion,
            e.CurrentName,
            berth ? null : Strings.RenamePierIdQuestion,
            e.Pier?.Id);

        if (form.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(form.Value))
        {
            e.Cancel = true;
            return;
        }

        var name = form.Value.Trim();
        if (berth && !string.Equals(name, e.CurrentName, StringComparison.OrdinalIgnoreCase) && Marina.GetBerth(name) is not null)
        {
            e.Cancel = true;
            Log(Strings.Format(Strings.LogRenameRefused, name, e.CurrentName));
            return;
        }

        if (!berth && e.Pier is { } pier)
        {
            var id = form.SecondValue.Trim();
            if (string.IsNullOrEmpty(id)) id = pier.Id;
            if (!string.Equals(id, pier.Id, StringComparison.OrdinalIgnoreCase) && Marina.GetPier(id) is not null)
            {
                e.Cancel = true;
                Log(Strings.Format(Strings.LogRenameRefused, id, pier.Id));
                return;
            }

            e.NewPierId = id;
            if (!string.Equals(id, pier.Id, StringComparison.Ordinal))
            {
                MarkDirty();
                Log(Strings.Format(Strings.LogPierIdChanged, pier.Id, id));
            }
        }

        e.NewName = name;
        if (name != e.CurrentName)
        {
            MarkDirty();
            Log(Strings.Format(Strings.LogRenamed, e.CurrentName, name));
        }
    }

    private void EditMarinaProperties()
    {
        using var form = new TextInputForm(Strings.MarinaNameTitle, Strings.MarinaNameQuestion, Marina.MarinaName);
        if (form.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(form.Value)) return;
        Marina.MarinaName = form.Value.Trim();
        MarkDirty();
        UpdateTitle();
        RefreshUi();
    }

    private void ShowShortcuts() => MessageBox.Show(
        this, Strings.ShortcutsBody, Strings.ShortcutsTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);

    private void ShowAbout() => MessageBox.Show(
        this,
        Strings.Format(Strings.AboutBody, AppName, Application.ProductVersion, _view.RendererDescription),
        Strings.AboutTitle,
        MessageBoxButtons.OK,
        MessageBoxIcon.Information);

    private void Warn(string title, Exception ex)
    {
        Log(Strings.Format(Strings.LogFailed, title, ex.Message));
        MessageBox.Show(this, Strings.Format(Strings.WarnBody, title, ex.Message), AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static string Sanitize(string name)
    {
        var cleaned = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? Strings.DefaultFileName : cleaned;
    }
}
