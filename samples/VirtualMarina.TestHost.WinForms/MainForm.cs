using System.Globalization;
using System.Text;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Serialization;
using VirtualMarina.SampleData;
using VirtualMarina.WinForms;

namespace VirtualMarina.TestHost.WinForms;

/// <summary>
/// A "berth desk" screen, as a marina ERP would build it: the 3D marina on the left, details and commands for the selected
/// berth on the right. The controls are laid out in MainForm.Designer.cs; this file only contains the VirtualMarina integration.
/// </summary>
public partial class MainForm : Form
{
    // Command ids, shared by the buttons on the form and the actions in the 3D view's right-click window.
    private const string CheckIn = "checkin";
    private const string Reserve = "reserve";
    private const string OwnerAway = "owner-away";
    private const string CheckOut = "checkout";
    private const string FocusCamera = "focus";
    private const string Maintenance = "maintenance";
    private const string Lock = "lock";
    private const string MoorAlongside = "moor-alongside";
    private const string ReleaseMultiBerth = "release-berth";

    // Key under which we keep our own data on each berth (Berth.ExternalData).
    private const string CheckedInAtKey = "BerthDesk.CheckedInAt";

    private readonly Random _random = new(7);

    /// <summary>The fonts this form makes itself, let go of with it.</summary>
    private readonly Font _headerFont;
    private readonly Font _exportFont = new("Consolas", 9F);

    /// <summary>What the status bar says about the renderer; filled in once OpenGL has started.</summary>
    private readonly ToolStripStatusLabel _lblRenderer = new() { ForeColor = SystemColors.GrayText };

    // The design file last opened, kept so saving writes back what this host does not understand (another
    // application's sections, a description, newer properties) instead of dropping it. Null for a marina made here.
    private MarinaDocument? _document;

    public MainForm()
    {
        InitializeComponent();
        _headerFont = new Font(Font, FontStyle.Bold);
        Disposed += (_, _) =>
        {
            _headerFont.Dispose();
            _exportFont.Dispose();
        };

        // The MarinaViewControl owns the visualizer; everything goes through marinaView.Marina.
        // Its events (BerthSelected, BerthActionInvoked, ...) are wired in the designer: Properties window → Events → Marina.
        var marina = marinaView.Marina;
        marina.DefaultFocusAngle = CameraAngle.TopDown;        // focus (buttons, actions, double-click) looks straight down

        // The camera list follows the views: a pier added or removed, the view resized, a view switched off.
        // Let go of it with the form, as a host whose visualizer outlives the form has to.
        marina.CameraPresetsChanged += OnCameraPresetsChanged;
        Disposed += (_, _) => marina.CameraPresetsChanged -= OnCameraPresetsChanged;
        cmbCameraPreset.DisplayMember = nameof(CameraPreset.Name);

        // In a real application the layout comes from the ERP database.
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());
        FillCameraPresets();

        cmbLabelMode.SelectedIndex = (int)BerthLabelMode.None;
        ShowBerthDetails();
        SetUpDesignerTab();
        SetUpMenu();
        statusStrip.Items.Insert(statusStrip.Items.IndexOf(lblHelp), _lblRenderer);
    }

    // ---- Menu ----------------------------------------------------------------------------------------

    /// <summary>
    /// A host's own menu with the usual shortcuts. It shows how they live alongside the 3D view: the view takes arrows,
    /// W/A/S/D and the like, and leaves every Ctrl, Alt and function-key chord to the form, so Ctrl+S, Ctrl+Z and Ctrl+Y
    /// here work whether the view has the focus or not. Undo and Redo are the designer's own.
    /// </summary>
    private void SetUpMenu()
    {
        var designer = marinaView.Marina.Designer;
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(MenuItem("&Open design…", Keys.Control | Keys.O, OpenDesign));
        file.DropDownItems.Add(MenuItem("&Save design…", Keys.Control | Keys.S, SaveDesign));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MenuItem("E&xit", Keys.Alt | Keys.F4, Close));

        var undo = MenuItem("&Undo", Keys.Control | Keys.Z, () => designer.TryUndo());
        var redo = MenuItem("&Redo", Keys.Control | Keys.Y, () => designer.TryRedo());
        var edit = new ToolStripMenuItem("&Edit");
        edit.DropDownItems.Add(undo);
        edit.DropDownItems.Add(redo);

        // Enabled only when there is something to take back or put back, and saying what.
        edit.DropDownOpening += (_, _) =>
        {
            undo.Enabled = designer.HasDraft || designer.CanUndo;
            undo.ToolTipText = designer.UndoDescription;
            redo.Enabled = designer.CanRedo;
            redo.ToolTipText = designer.RedoDescription;
        };

        // A disabled item ignores its key, so they are only disabled while the menu is open, to show there is nothing to
        // take back; closing the menu enables them again and the keys keep coming here.
        edit.DropDownClosed += (_, _) =>
        {
            undo.Enabled = true;
            redo.Enabled = true;
        };

        var view = new ToolStripMenuItem("&View");
        view.DropDownItems.Add(MenuItem("&Reset view", Keys.Control | Keys.R, () => marinaView.Marina.ResetCamera()));

        var menu = new MenuStrip { ShowItemToolTips = true };
        menu.Items.AddRange(new ToolStripItem[] { file, edit, view });
        MainMenuStrip = menu;
        Controls.Add(menu);
    }

    private static ToolStripMenuItem MenuItem(string text, Keys keys, Action action) =>
        new(text, null, (_, _) => action()) { ShortcutKeys = keys, ShowShortcutKeys = true };

    // ---- Designer ------------------------------------------------------------------------------------

    private TabControl _tabs = null!;
    private TabPage _designerPage = null!;

    /// <summary>
    /// Puts the berth desk groups and a layout designer into two tabs. The designer tab holds the library's ready-made
    /// <see cref="MarinaDesignerPanel"/>; opening it turns design mode on.
    /// </summary>
    private void SetUpDesignerTab()
    {
        var marina = marinaView.Marina;
        var panel2 = splitContainer.Panel2;
        panel2.SuspendLayout();
        panel2.Controls.Remove(grpBerth);
        panel2.Controls.Remove(grpView);

        _tabs = new TabControl { Dock = DockStyle.Top, Height = LogicalToDeviceUnits(540) };
        var deskPage = new TabPage("Berth desk") { Padding = new Padding(3), AutoScroll = true };
        deskPage.Controls.Add(grpView);
        deskPage.Controls.Add(grpBerth);
        _designerPage = new TabPage("Designer") { Padding = new Padding(3) };

        var designerPanel = new MarinaDesignerPanel { Dock = DockStyle.Fill, Marina = marina };
        var commands = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 4) };
        commands.Controls.Add(CreateButton("New empty marina", (_, _) => StartEmptyMarina()));
        commands.Controls.Add(CreateButton("Load sample marina", (_, _) =>
        {
            _document = null;
            marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());
        }));
        commands.Controls.Add(CreateButton("Export objects…", (_, _) => ShowExport()));
        commands.Controls.Add(CreateButton("Open design…", (_, _) => OpenDesign()));
        commands.Controls.Add(CreateButton("Save design…", (_, _) => SaveDesign()));
        _designerPage.Controls.Add(designerPanel);
        _designerPage.Controls.Add(commands);

        _tabs.TabPages.Add(deskPage);
        _tabs.TabPages.Add(_designerPage);
        _tabs.TabPages.Add(CreateAppearancePage());
        // The designer tab turns design mode on; leaving it (to any other tab) turns it off.
        _tabs.SelectedIndexChanged += (_, _) => marina.Designer.IsActive = _tabs.SelectedTab == _designerPage;
        panel2.Controls.Add(_tabs);
        grpEvents.BringToFront(); // fills the space under the tabs
        panel2.ResumeLayout(true);

        // Everything the designer does is reported to the host.
        var designer = marina.Designer;
        designer.ActiveChanged += (_, _) =>
        {
            Log($"Designer {(designer.IsActive ? "on" : "off")}");
            if (designer.IsActive && _tabs.SelectedTab != _designerPage) _tabs.SelectedTab = _designerPage;
        };
        designer.ToolChanged += (_, e) => Log($"Tool: {e.Current}");
        designer.DraftChanged += (_, e) => Log($"Drawing {e.Tool}: {e.Change}, {e.Points.Count} point(s)" +
            (e.Points.Count > 0 ? $", last {Format(e.Points[^1])}" : ""));
        designer.ElementCreated += (_, e) => Log(e switch
        {
            { LandArea: { } land } => $"Created land area {land.Id} ({land.Kind}, {land.Points.Count} corners, {land.Area:0} m²)",
            { Pier: { } pier } => $"Created pier {pier.Id} ({Core.Domain.Pier.GetDisplayName(pier.Type)}, {pier.Length:0.0} x {pier.Width:0.0} m, {pier.BerthingSides})",
            _ => $"Created {e.Berths.Count} berth(s): {string.Join(", ", e.Berths.Select(s => s.Id))}",
        });
        designer.ElementErased += (_, e) => Log($"Erased {e.Element.GetType().Name} {IdOf(e.Element)} ({e.RemovedBerths.Count} berth(s), {e.RemovedDividers.Count} divider(s))");
        designer.ActionUndone += (_, e) => Log($"Undone: {e.Description} ({e.RemainingSteps} step(s) left)");
        designer.ScaleLineDrawn += (_, e) => Log($"Scale line drawn: {e.MeasuredLength:0.0} m at the current image scale");
        designer.TreesPlanted += (_, e) => Log($"{e.LandArea.Trees.Count} trees on {e.LandArea.Id} now (had {e.PreviousCount})");
        designer.ReferenceImageChanged += (_, e) => Log($"Reference image {e.Change}: {e.MetersPerPixel:0.####} m/px, center {Format(e.Center)}");
    }

    /// <summary>Clears the marina so a new one can be drawn from scratch.</summary>
    private void StartEmptyMarina()
    {
        var marina = marinaView.Marina;
        _document = null;
        marina.InitializeLayout(new MarinaLayout { Name = "New marina" });
        marina.Designer.IsActive = true;
        marina.Designer.Tool = DesignTool.DrawLandArea;
        marina.Designer.ViewTopDown(immediate: true);
    }

    /// <summary>
    /// Loads a design drawn in the VirtualMarina Designer: layout, water and light settings, berth labels and camera all come out
    /// of the one file, so the host application needs no marina-specific code of its own.
    /// </summary>
    private void OpenDesign()
    {
        using var dialog = new OpenFileDialog { Filter = MarinaDocument.FileDialogFilter, Title = "Open a marina design" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var document = MarinaDocument.Load(dialog.FileName);
            document.ApplyTo(marinaView.Marina);
            _document = document;
            Log($"Loaded {Path.GetFileName(dialog.FileName)}: {document.Layout.Berths.Count} berths, {document.Layout.Piers.Count} piers, " +
                $"written by {document.Generator ?? "an unknown tool"} in format {document.Version}");
        }
        catch (Exception ex) when (ex is MarinaFormatException or MarinaLayoutException or IOException or UnauthorizedAccessException)
        {
            Log("The design could not be loaded: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Open design", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Writes the marina on screen back out as a design file. A design that was opened is brought up to date with
    /// UpdateFrom, so what it carried that this host does not know about survives; a marina started here is written fresh.
    /// </summary>
    private void SaveDesign()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = MarinaDocument.FileDialogFilter,
            FileName = "marina" + MarinaDocument.FileExtension,
            Title = "Save the marina design",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        const string generator = "VirtualMarina WinForms test host";
        try
        {
            if (_document is null) _document = MarinaDocument.FromVisualizer(marinaView.Marina, generator);
            else _document.UpdateFrom(marinaView.Marina, generator);
            _document.Save(dialog.FileName);
            Log($"Saved {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log("The design could not be saved: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Save design", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>What an ERP would store: every element of the marina, as returned by ExportObjects.</summary>
    private void ShowExport()
    {
        var objects = marinaView.Marina.ExportObjects();
        var text = new StringBuilder($"{objects.Length} objects").AppendLine().AppendLine();
        foreach (var element in objects)
        {
            text.AppendLine(element switch
            {
                LandArea l => $"LandArea  {l.Id,-16} {l.Kind,-10} height {l.Height:0.0} m  [{string.Join("; ", l.Points.Select(Format))}]",
                Pier d => $"Pier      {d.Id,-16} {d.Type,-16} start {Format(d.Start)}  heading {d.HeadingDegrees:0.0}°  {d.Length:0.0} x {d.Width:0.0} m  sides {d.BerthingSides}",
                Divider v => $"Divider   {v.Id,-16} {v.Type,-10} pier {v.PierId}  start {Format(v.Start)}  {v.Length:0.0} m",
                Berth s => $"Berth      {s.Id,-16} {(s.PierId is null ? "land " + s.LandAreaId : "pier " + s.PierId),-14} center {Format(s.Center)}  {s.Width:0.0} x {s.Length:0.0} m  depth {s.MaxDraft?.ToString("0.0") ?? "-"} m  {s.Status}",
                MultiBerth b => $"Berth     {b.Id,-16} {string.Join(", ", b.BerthIds)}  {b.Boat.Name}",
                _ => element.ToString(),
            });
        }

        using var dialog = new Form { Text = "ExportObjects()", Size = new Size(1000, 600), StartPosition = FormStartPosition.CenterParent };
        dialog.Controls.Add(new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = _exportFont,
            Text = text.ToString(),
        });
        dialog.ShowDialog(this);
    }

    private static Button CreateButton(string text, EventHandler onClick)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += onClick;
        return button;
    }

    private static string Format(System.Numerics.Vector2 p) => string.Create(CultureInfo.InvariantCulture, $"({p.X:0.0}, {p.Y:0.0})");

    private static string IdOf(object element) => element switch
    {
        Berth s => s.Id,
        Pier d => d.Id,
        LandArea l => l.Id,
        _ => "?",
    };

    // ---- VirtualMarina events ------------------------------------------------------------------------

    /// <summary>A single berth was selected: add our data to the tooltip and offer the commands that make sense for it.</summary>
    private void OnBerthSelected(object? sender, BerthSelectedEventArgs e)
    {
        Log($"Selected {e.BerthId} ({e.Status}) by {e.Reason}");

        if (e.ExternalData.TryGet<DateTime>(CheckedInAtKey, out var checkedInAt))
        {
            e.Tooltip.AddLine("Checked in", checkedInAt.ToString("g"));
        }

        switch (e.Status)
        {
            case BerthStatus.Free:
                e.Actions.Add(CheckIn, "Check in", icon: "⚓").Style = BerthActionStyle.Primary;
                e.Actions.Add(Reserve, "Reserve", icon: "📅");
                break;
            case BerthStatus.Reserved:
                e.Actions.Add(CheckIn, "Boat arrived", icon: "⚓").Style = BerthActionStyle.Primary;
                e.Actions.Add(CheckOut, "Cancel reservation", icon: "✖").Style = BerthActionStyle.Danger;
                break;
            case BerthStatus.Occupied:
                e.Actions.Add(OwnerAway, "Owner away (temporarily free)", icon: "⛵");
                e.Actions.Add(CheckOut, "Check out", icon: "⇥").Style = BerthActionStyle.Danger;
                break;
            case BerthStatus.TemporarilyFree:
                e.Actions.Add(CheckIn, "Owner returned", icon: "⚓").Style = BerthActionStyle.Primary;
                e.Actions.Add(CheckOut, "End contract", icon: "⇥").Style = BerthActionStyle.Danger;
                break;
        }

        if (e.Berth is not null) e.Actions.Add(ReleaseMultiBerth, "Release multi-berth", icon: "⛓");

        e.Actions.Add(FocusCamera, "Focus camera", icon: "🎯").BeginGroup = true;
        e.Actions.Add(Maintenance, "Maintenance (disable)", icon: "🛠");
        e.Actions.Add(Lock, "Lock (read-only)", icon: "🔒");
    }

    /// <summary>Several berths are selected (Ctrl+click or Shift+click): offer commands for all of them.</summary>
    private void OnMultiBerthSelected(object? sender, MultiBerthSelectedEventArgs e)
    {
        Log($"Selected {e.Berths.Count} berths: {string.Join(", ", e.BerthIds)}");

        var moor = e.Actions.Add(MoorAlongside, "Moor one yacht alongside", enabled: CanMoorAlongside(e.Berths), icon: "🛥");
        moor.Style = BerthActionStyle.Primary;
        moor.Description = "Needs two or more free berths on the same pier.";
        e.Actions.Add(CheckOut, $"Check out {e.ActionableBerths.Count} berths", icon: "⇥").Style = BerthActionStyle.Danger;
        e.Actions.Add(FocusCamera, "Focus camera on all", icon: "🎯").BeginGroup = true;
        e.Actions.Add(Maintenance, "Maintenance (disable all)", icon: "🛠");
    }

    /// <summary>The user clicked an action in the 3D view: run the same command as the button on the form.</summary>
    private void OnBerthActionInvoked(object? sender, BerthActionInvokedEventArgs e)
    {
        Log($"Action '{e.ActionId}' on {string.Join(", ", e.Berths.Select(s => s.Id))}");
        RunCommand(e.ActionId, e.ActionableBerths);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => ShowBerthDetails();

    private void OnBerthStatusChanged(object? sender, BerthStatusChangedEventArgs e)
    {
        Log($"{e.BerthId}: {e.OldStatus} -> {e.NewStatus}");
        if (marinaView.Marina.IsBerthSelected(e.BerthId)) ShowBerthDetails();
    }

    private void OnLayoutChanged(object? sender, LayoutChangedEventArgs e)
    {
        if (e.Kind is not (LayoutChangeKind.BerthUpdated or LayoutChangeKind.Initialized)) Log($"LayoutChanged: {e.Kind} {e.LandAreaId ?? e.PierId} {e.BerthId}".TrimEnd());
        ShowStatistics();
    }

    private void OnBerthHoverChanged(object? sender, BerthHoverEventArgs e) =>
        lblHover.Text = e.Berth is null ? "" : $"{e.Berth.DisplayName} – {e.Berth.Status.GetDisplayName()}";

    // ---- Form controls -------------------------------------------------------------------------------

    private void OnCheckInClick(object sender, EventArgs e) => RunCommand(CheckIn, SelectedBerths());

    private void OnReserveClick(object sender, EventArgs e) => RunCommand(Reserve, SelectedBerths());

    private void OnOwnerAwayClick(object sender, EventArgs e) => RunCommand(OwnerAway, SelectedBerths());

    private void OnCheckOutClick(object sender, EventArgs e) => RunCommand(CheckOut, SelectedBerths());

    private void OnFocusClick(object sender, EventArgs e) => RunCommand(FocusCamera, SelectedBerths());

    private void OnMaintenanceClick(object sender, EventArgs e) => RunCommand(Maintenance, SelectedBerths());

    private void OnReadOnlyClick(object sender, EventArgs e) => RunCommand(Lock, SelectedBerths());

    private void OnMoorAlongsideClick(object sender, EventArgs e) => RunCommand(MoorAlongside, SelectedBerths());

    private void OnReleaseBerthClick(object sender, EventArgs e) => RunCommand(ReleaseMultiBerth, SelectedBerths());

    private void OnShowActionsClick(object sender, EventArgs e) => marinaView.Marina.ShowActions();

    /// <summary>
    /// Selects every berth of the current pier, or every land berth of the current land area.
    /// Disabled and hidden berths are skipped by SetSelection.
    /// </summary>
    private void OnSelectPierClick(object sender, EventArgs e)
    {
        var marina = marinaView.Marina;
        var pierId = marina.SelectedBerth?.PierId ?? (marina.GetPiers() is { Count: > 0 } piers ? piers[0].Id : null);
        if (marina.SelectedBerth?.LandAreaId is null && pierId is null)
        {
            Log("There is no pier to select yet.");
            return;
        }

        var berthIds = marina.SelectedBerth?.LandAreaId is { } landAreaId
            ? marina.GetBerthsByLandArea(landAreaId).Select(s => s.Id)
            : marina.GetBerthsByPier(pierId!).Select(s => s.Id);

        var result = marina.SetSelection(berthIds, focusCamera: true);
        foreach (var rejected in result.Rejected) Log($"Not selected: {rejected.BerthId} ({rejected.Reason})");
    }

    /// <summary>Clears Disabled / Read-only on every berth.</summary>
    private void OnResetFlagsClick(object sender, EventArgs e)
    {
        var marina = marinaView.Marina;
        marina.SetBerthFlags(marina.GetBerths().Select(s => s.Id), visible: true, disabled: false, readOnly: false);
    }

    private void OnStatusFilterChanged(object sender, EventArgs e)
    {
        var filter = BerthStatusFilter.None;
        if (chkFree.Checked) filter |= BerthStatusFilter.Free;
        if (chkOccupied.Checked) filter |= BerthStatusFilter.Occupied;
        if (chkReserved.Checked) filter |= BerthStatusFilter.Reserved;
        if (chkTemporarilyFree.Checked) filter |= BerthStatusFilter.TemporarilyFree;
        marinaView.Marina.SetStatusFilter(filter);
    }

    /// <summary>The combo items are in the same order as the <see cref="BerthLabelMode"/> values.</summary>
    private void OnLabelModeChanged(object sender, EventArgs e) =>
        marinaView.Marina.BerthLabelMode = (BerthLabelMode)cmbLabelMode.SelectedIndex;

    /// <summary>
    /// Goes to the chosen view: an automatic one by its key, which stays the same whatever the language or the pier's
    /// name, a saved one by its name. Both are asked for afresh, since the list may hold a view worked out for an
    /// earlier layout.
    /// </summary>
    private void OnCameraPresetSelected(object sender, EventArgs e)
    {
        if (cmbCameraPreset.SelectedItem is not CameraPreset preset) return;
        if (preset.IsBuiltIn && preset.Key is { } key) marinaView.Marina.ApplyBuiltInCameraPreset(key);
        else marinaView.Marina.ApplyCameraPreset(preset.Name);
    }

    private void OnCameraPresetsChanged(object? sender, EventArgs e)
    {
        // Raised on the thread that changed the marina, which in this host is always the UI thread.
        if (InvokeRequired) BeginInvoke(FillCameraPresets);
        else FillCameraPresets();
    }

    private void OnResetViewClick(object sender, EventArgs e) => marinaView.Marina.ResetCamera();

    /// <summary>
    /// The view could not draw: OpenGL would not start, or a frame failed. The view shows a placeholder meanwhile;
    /// this says why, and offers to try again (after a driver update, say, or with the window on another monitor).
    /// </summary>
    private void OnMarinaViewRenderError(object? sender, ThreadExceptionEventArgs e)
    {
        Log("Render error: " + e.Exception.Message);
        var answer = MessageBox.Show(
            this,
            $"The 3D view could not draw the marina:\n\n{e.Exception.Message}\n\nTry again?",
            "Render error",
            MessageBoxButtons.RetryCancel,
            MessageBoxIcon.Error);
        if (answer == DialogResult.Retry) marinaView.RetryRendering();
    }

    private void OnStatusTimerTick(object sender, EventArgs e)
    {
        var pose = marinaView.Marina.Camera.Pose;
        lblCameraPose.Text = $"Camera: yaw {pose.YawDegrees % 360:0}°, pitch {pose.PitchDegrees:0}°, distance {pose.Distance:0} m";
        if (string.IsNullOrEmpty(_lblRenderer.Text) && marinaView.RendererDescription is { Length: > 0 } renderer) _lblRenderer.Text = renderer;
    }

    // ---- Berth commands (what the ERP does) ----------------------------------------------------------

    private void RunCommand(string command, IReadOnlyList<Berth> berths)
    {
        var marina = marinaView.Marina;
        if (berths.Count == 0)
        {
            Log("Select a berth first.");
            return;
        }

        var ids = berths.Select(s => s.Id).ToArray();
        switch (command)
        {
            case CheckIn:
                foreach (var berth in berths)
                {
                    // Set our data first: AssignBoat immediately refreshes the details panel and the open tooltip.
                    berth.ExternalData[CheckedInAtKey] = DateTime.Now;
                    marina.AssignBoat(berth.Id, berth.Boat ?? FindBoatInErp(berth));
                }

                break;

            case Reserve:
                foreach (var berth in berths.Where(s => s.Status == BerthStatus.Free))
                {
                    marina.ReserveBerth(berth.Id, FindBoatInErp(berth) with { ExpectedArrival = DateTimeOffset.Now.AddHours(6) });
                }

                break;

            case OwnerAway:
                foreach (var berth in berths.Where(s => s.Boat is not null)) marina.MarkTemporarilyFree(berth.Id);
                break;

            case CheckOut:
                foreach (var berth in berths)
                {
                    marina.ReleaseBerth(berth.Id);
                    berth.ExternalData.Remove(CheckedInAtKey);
                }

                break;

            case FocusCamera:
                marina.FocusBerths(ids);
                break;

            case Maintenance:
                marina.SetBerthFlags(ids, disabled: true);
                break;

            case Lock:
                marina.SetBerthFlags(ids, readOnly: true);
                break;

            case MoorAlongside:
                if (!CanMoorAlongside(berths))
                {
                    MessageBox.Show(this, "Select two or more free berths on the same pier (Ctrl+click or Shift+click).", "Moor alongside");
                    return;
                }

                var span = berths.Sum(s => s.Width);
                var yacht = new Boat($"YACHT-{_random.Next(1000, 9999)}", "Visiting yacht", BoatType.MotorYacht)
                {
                    LengthMeters = span - 1.5f,
                    BeamMeters = Math.Clamp(span * 0.25f, 3f, berths.Min(s => s.Length) - 2f),
                };
                marina.MoorAlongside(ids, yacht);
                break;

            case ReleaseMultiBerth:
                foreach (var multiBerthId in berths.Select(s => s.MultiBerthId).OfType<string>().Distinct()) marina.ReleaseMultiBerth(multiBerthId);
                break;
        }
    }

    private static bool CanMoorAlongside(IReadOnlyList<Berth> berths) =>
        berths.Count >= 2 &&
        berths.All(s => s.Status == BerthStatus.Free && s.AllowsActions && s.MultiBerthId is null) &&
        berths.All(s => s.PierId is not null) &&
        berths.Select(s => s.PierId).Distinct().Count() == 1;

    /// <summary>Stands in for an ERP lookup of the boat that belongs to this berth.</summary>
    private Boat FindBoatInErp(Berth berth) => MockMarinaFactory.CreateBoatForBerth(berth, _random);

    // ---- Display helpers -----------------------------------------------------------------------------

    private IReadOnlyList<Berth> SelectedBerths() => marinaView.Marina.SelectedBerths;

    private void ShowBerthDetails()
    {
        var berths = SelectedBerths();
        var text = new StringBuilder();

        if (berths.Count == 0)
        {
            text.AppendLine("Click a berth in the 3D view.");
            text.AppendLine("Ctrl+click or Shift+click selects several, right-click shows actions.");
        }
        else if (berths.Count > 1)
        {
            text.AppendLine($"{berths.Count} berths selected:");
            foreach (var s in berths) text.AppendLine($"  {s.DisplayName,-10} {s.Status.GetDisplayName(),-17} {s.Boat?.Name}");
        }
        else
        {
            var berth = berths[0];
            var where = berth.LandAreaId is { } landAreaId
                ? $"on land: {marinaView.Marina.GetLandArea(landAreaId)?.DisplayName}"
                : marinaView.Marina.GetPier(berth.PierId!)?.Name;
            text.AppendLine($"Berth:   {berth.DisplayName}  ({where})");
            text.AppendLine($"Status:  {berth.Status.GetDisplayName()}{(berth.IsReadOnly ? " (locked)" : "")}");
            text.AppendLine($"Size:    {berth.Length:0.0} x {berth.Width:0.0} m");
            if (berth.Boat is { } boat)
            {
                text.AppendLine($"Boat:    {boat.Name} ({boat.TypeDisplayName}, {boat.LengthMeters:0.0} m)");
                text.AppendLine($"Owner:   {boat.OwnerName}");
            }

            if (berth.MultiBerthId is not null) text.AppendLine($"Berth:   part of multi-berth {berth.MultiBerthId}");
            if (berth.ExternalData.TryGet<DateTime>(CheckedInAtKey, out var checkedInAt)) text.AppendLine($"Checked in: {checkedInAt:g}");
        }

        txtBerthDetails.Text = text.ToString();
    }

    private void ShowStatistics()
    {
        var s = marinaView.Marina.GetStatistics();
        lblStatistics.Text = $"{s.TotalBerths} berths · {s.Free} free · {s.Occupied} occupied · {s.Reserved} reserved · {s.TemporarilyFree} temp. free · {s.OccupancyRate:P0} occupancy";
    }

    /// <summary>Lists the views that are switched on, keeping the one chosen (the Overview at first) selected.</summary>
    private void FillCameraPresets()
    {
        var chosen = cmbCameraPreset.SelectedItem as CameraPreset;
        bool Same(CameraPreset preset) => chosen is null
            ? preset.Key == MarinaVisualizer.OverviewPresetName
            : preset.IsBuiltIn == chosen.IsBuiltIn && (preset.IsBuiltIn ? preset.Key == chosen.Key : preset.Name == chosen.Name);

        cmbCameraPreset.BeginUpdate();
        cmbCameraPreset.Items.Clear();
        foreach (var preset in marinaView.Marina.CameraPresets.Where(p => p.IsEnabled))
        {
            var index = cmbCameraPreset.Items.Add(preset);
            if (Same(preset)) cmbCameraPreset.SelectedIndex = index;
        }

        cmbCameraPreset.EndUpdate();
    }

    private void Log(string message)
    {
        lstEvents.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        if (lstEvents.Items.Count > 200) lstEvents.Items.RemoveAt(lstEvents.Items.Count - 1);
    }
}
