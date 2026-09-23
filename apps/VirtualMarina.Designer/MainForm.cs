using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Input;
using VirtualMarina.Designer.Resources;
using VirtualMarina.WinForms;

namespace VirtualMarina.Designer;

/// <summary>
/// The designer window: a fixed toolbar of drawing tools at the top, the 3D marina filling the window, and a panel beside it that
/// only ever shows the settings of the tool in hand. Designs are saved as <c>.marina.json</c> files the host application loads.
/// </summary>
/// <remarks>
/// Everything that is not drawing — the file, whether it is saved, the title, the log, the rename questions — is the
/// <see cref="DesignerSession"/> the Blazor designer runs on too; this window supplies its dialogs and its controls. The
/// menus and their keys come from <see cref="DesignerCommands"/>, the one table both designers share.
/// </remarks>
internal sealed class MainForm : Form
{
    /// <summary>Who to credit in the About box.</summary>
    private const string Author = "Christoforos Sakellaris";

    /// <summary>
    /// Narrowest the settings panel may be dragged, at 96 DPI: below this its rows stop fitting. A slider row is the
    /// tightest of them: the row label, the reset button and the value text all take their width before the track
    /// gets what remains.
    /// </summary>
    private const int MinInspectorWidth = 380;

    /// <summary>Widest it may be dragged, at 96 DPI: past this it takes room from the marina without gaining anything.</summary>
    private const int MaxInspectorWidth = 560;

    /// <summary>Least width left for the marina view, at 96 DPI.</summary>
    private const int MinViewWidth = 360;

    /// <summary>How long a notice stays in the status bar.</summary>
    private const int NoticeMilliseconds = 8000;

    private readonly MarinaViewControl _view = new() { Dock = DockStyle.Fill };
    private readonly DesignerSession _session;
    private readonly InspectorPanel _inspector;
    private readonly AppearancePanel _appearance;
    private readonly CamerasPanel _cameras;

    /// <summary>
    /// Holds the tool settings and whichever of Look or Cameras is open, as one column with one scrollbar. Scrolling
    /// carries the tool settings off the top, leaving the whole height to what is underneath.
    /// </summary>
    private readonly Panel _side = new()
    {
        AutoScroll = true,
        Dock = DockStyle.Fill,
        BackColor = Theme.Background,
    };

    private readonly ToolStrip _toolbar = new();
    private readonly Dictionary<DesignTool, ToolStripButton> _toolButtons = [];
    private readonly Dictionary<DesignerCommandId, Action> _commands = [];
    private readonly Dictionary<DesignerCommandId, ToolStripMenuItem> _menuItems = [];
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusHint = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _statusNotice = new() { AutoSize = true, ForeColor = Theme.Danger, Visible = false };
    private readonly ToolStripStatusLabel _statusPointer = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleRight };
    private readonly ToolStripStatusLabel _statusCamera = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleRight };
    private readonly ListBox _log = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = Theme.Mono, IntegralHeight = false };
    private readonly Panel _logPanel = new() { Dock = DockStyle.Bottom, Height = 150, Visible = false, Padding = new Padding(8, 6, 8, 8), BackColor = Theme.Surface };
    private readonly SplitContainer _split = new();

    /// <summary>Refreshes the camera readout ten times a second, and only when the camera has actually moved.</summary>
    private readonly System.Windows.Forms.Timer _cameraTimer = new() { Interval = 100 };

    /// <summary>Clears a notice from the status bar once it has been there long enough to read.</summary>
    private readonly System.Windows.Forms.Timer _noticeTimer = new() { Interval = NoticeMilliseconds };

    private ToolStripButton _lookButton = null!;
    private ToolStripButton _camerasButton = null!;
    private ToolStripButton _undoButton = null!;
    private ToolStripButton _redoButton = null!;
    private CameraPose _shownPose;
    private bool _refreshPending;
    private bool _closeConfirmed;

    public MainForm()
    {
        // Every size below is written for 96 DPI and scaled from there, when the window opens and whenever it moves to
        // a monitor with another scale.
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = Strings.AppName;
        MinimumSize = new Size(1080, 720);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        BackColor = Theme.Background;
        Font = Theme.Body;

        var dialogs = new WinFormsDialogs(this);
        _session = new DesignerSession(Marina, dialogs, DesignerText.Generator(typeof(MainForm)));
        _inspector = new InspectorPanel(_session, () => Run(DesignerCommandId.LoadImage));
        _appearance = new AppearancePanel(Marina, _session.Log.Add, _session.MarkDirty, Confirm);
        _cameras = new CamerasPanel(Marina, _session.Log.Add, _session.MarkDirty, Confirm);

        RegisterCommands();
        BuildMenu();
        BuildToolbar();
        BuildStatusBar();
        BuildLayout();
        WireEvents();

        _ = _session.NewAsync(askToSave: false);
    }

    private MarinaVisualizer Marina => _view.Marina;

    private MarinaDesigner Designer => Marina.Designer;

    // ---- Commands ------------------------------------------------------------------------------

    /// <summary>What each command in the table does in this window.</summary>
    private void RegisterCommands()
    {
        _commands[DesignerCommandId.New] = () => _ = _session.NewAsync();
        _commands[DesignerCommandId.Open] = () => _ = _session.OpenAsync();
        _commands[DesignerCommandId.Save] = () => _ = _session.SaveAsync();
        _commands[DesignerCommandId.SaveAs] = () => _ = _session.SaveAsync(saveAs: true);
        _commands[DesignerCommandId.LoadImage] = LoadReferenceImage;
        _commands[DesignerCommandId.Exit] = Close;
        _commands[DesignerCommandId.Undo] = () => _session.Undo();
        _commands[DesignerCommandId.Redo] = () => _session.Redo();
        _commands[DesignerCommandId.CancelDraft] = () => Designer.CancelDraft();
        _commands[DesignerCommandId.Rename] = () => SelectTool(DesignTool.Rename);
        _commands[DesignerCommandId.MarinaProperties] = () => _ = _session.EditMarinaPropertiesAsync();
        _commands[DesignerCommandId.TopView] = _session.ViewTopDown;
        _commands[DesignerCommandId.FitMarina] = () => Marina.ResetCamera();
        _commands[DesignerCommandId.FitImage] = () => Designer.FocusReferenceImage();
        _commands[DesignerCommandId.ShowLog] = () => _logPanel.Visible = !_logPanel.Visible;
        _commands[DesignerCommandId.Appearance] = () => _session.ToggleSidePanel(DesignerSidePanel.Look);
        _commands[DesignerCommandId.Cameras] = () => _session.ToggleSidePanel(DesignerSidePanel.Cameras);
        _commands[DesignerCommandId.BerthLabels] = _session.ToggleBerthLabels;
        _commands[DesignerCommandId.Shortcuts] = ShowShortcuts;
        _commands[DesignerCommandId.About] = ShowAbout;
        _commands[DesignerCommandId.Escape] = () => Marina.Input.KeyDown(MarinaKey.Escape);
    }

    /// <summary>
    /// Runs a command, after a number field being typed in has handed its value over: Ctrl+S straight after typing a
    /// width saves the new width, not the old one.
    /// </summary>
    private void Run(DesignerCommandId id)
    {
        if (_session.IsBusy) return;
        CommitPendingEdit();
        if (_commands.TryGetValue(id, out var command)) command();
        RequestRefresh();
    }

    private void RunCommand(DesignerCommand command)
    {
        if (command.Tool is { } tool)
        {
            CommitPendingEdit();
            SelectTool(tool);
        }
        else
        {
            Run(command.Id);
        }
    }

    /// <summary>
    /// A number field only takes what was typed into it when it loses the focus; reading its value takes it now. The
    /// field's ValueChanged then reaches the designer before the command runs.
    /// </summary>
    private void CommitPendingEdit()
    {
        if (FocusedControl() is not { } focused) return;
        var upDown = focused as UpDownBase ?? focused.Parent as UpDownBase;
        if (upDown is NumericUpDown number) _ = number.Value;
        Validate();
    }

    private void SelectTool(DesignTool tool)
    {
        Designer.IsActive = true;
        Designer.Tool = tool;
    }

    private bool Confirm(string title, string message) =>
        MessageBox.Show(this, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    // ---- Shell ---------------------------------------------------------------------------------

    private void BuildLayout()
    {
        _split.Dock = DockStyle.Fill;
        _split.FixedPanel = FixedPanel.Panel2;
        _split.SplitterWidth = 1;
        _split.BackColor = Theme.Border;
        _split.Width = 1200;
        _split.Panel1MinSize = MinViewWidth;
        _split.Panel2MinSize = MinInspectorWidth;
        _split.Panel1.Controls.Add(_view);

        // One scrolling column. Docked Top and added in this order, the last one added ends up at the very top, so
        // the tool settings lead and the open side panel follows underneath.
        _appearance.UseOuterScrolling();
        _cameras.UseOuterScrolling();
        _inspector.UseOuterScrolling();
        _appearance.Collapsed = true;
        _cameras.Collapsed = true;
        _side.Controls.Add(_appearance);
        _side.Controls.Add(_cameras);
        _side.Controls.Add(_inspector);
        _split.Panel2.Controls.Add(_side);
        WheelForwarding.Attach(_side);

        var logHeader = new Label
        {
            Text = Strings.LogHeader,
            Dock = DockStyle.Top,
            Font = Theme.Caption,
            ForeColor = Theme.TextSoft,
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 4),
        };
        _logPanel.Controls.Add(_log);
        _logPanel.Controls.Add(logHeader);

        var center = new Panel { Dock = DockStyle.Fill };
        center.Controls.Add(_split);
        center.Controls.Add(_logPanel);

        Controls.Add(center);
        Controls.Add(_toolbar);
        Controls.Add(MainMenuStrip);
        Controls.Add(_status);

        // The inspector keeps its width while the window is resized; the splitter can only be set once the form has a size.
        _split.SizeChanged += (_, _) => LimitInspectorWidth();
        Shown += (_, _) =>
        {
            LimitInspectorWidth();
            SetInspectorWidth(LogicalToDeviceUnits(MinInspectorWidth));
        };
        DpiChanged += (_, _) => LimitInspectorWidth();
    }

    /// <summary>
    /// Holds the settings panel between <see cref="MinInspectorWidth"/> and <see cref="MaxInspectorWidth"/>, scaled to
    /// the monitor the window is on. The maximum is expressed as a minimum width for the marina view, so the splitter
    /// simply stops there while being dragged instead of springing back.
    /// </summary>
    private void LimitInspectorWidth()
    {
        var available = _split.Width - _split.SplitterWidth;
        var minimum = LogicalToDeviceUnits(MinInspectorWidth);
        var maximum = LogicalToDeviceUnits(MaxInspectorWidth);
        var minimumView = LogicalToDeviceUnits(MinViewWidth);
        if (available <= minimum) return; // The window is too narrow to honour anything; leave it alone.

        // Both minimums have to fit, whatever the window size, or SplitContainer throws.
        var viewMinimum = Math.Max(minimumView, available - maximum);
        _split.Panel2MinSize = Math.Min(minimum, available - minimumView);
        _split.Panel1MinSize = Math.Min(viewMinimum, available - _split.Panel2MinSize);

        // A window that shrank can leave the splitter outside the new bounds.
        SetInspectorWidth(available - _split.SplitterDistance);
    }

    private void SetInspectorWidth(int width)
    {
        var available = _split.Width - _split.SplitterWidth;
        var distance = Math.Clamp(available - width, _split.Panel1MinSize, Math.Max(_split.Panel1MinSize, available - _split.Panel2MinSize));
        if (distance != _split.SplitterDistance) _split.SplitterDistance = distance;
    }

    private void BuildMenu()
    {
        var file = Menu(Strings.MenuFile, DesignerCommandId.New, DesignerCommandId.Open, null, DesignerCommandId.Save, DesignerCommandId.SaveAs, null, DesignerCommandId.LoadImage, null, DesignerCommandId.Exit);
        var edit = Menu(Strings.MenuEdit, DesignerCommandId.Undo, DesignerCommandId.Redo, DesignerCommandId.CancelDraft, null, DesignerCommandId.Rename, null, DesignerCommandId.MarinaProperties);
        var view = Menu(Strings.MenuView, DesignerCommandId.TopView, DesignerCommandId.FitMarina, DesignerCommandId.FitImage, null, DesignerCommandId.ShowLog);

        // Ticked when on: they show or hide something beside the marina, rather than open a window.
        var marina = Menu(Strings.MenuMarina, DesignerCommandId.Appearance, DesignerCommandId.Cameras, DesignerCommandId.BerthLabels);
        var help = Menu(Strings.MenuHelp, DesignerCommandId.Shortcuts, DesignerCommandId.About);

        // The history's items say what they would take back or put back, and the ticks what is shown, every time a
        // menu opens.
        edit.DropDownOpening += (_, _) => UpdateHistoryCommands();
        view.DropDownOpening += (_, _) => UpdateToggles();
        marina.DropDownOpening += (_, _) => UpdateToggles();

        MainMenuStrip = new MenuStrip { BackColor = Theme.Surface, Font = Theme.Body, Padding = new Padding(6, 2, 0, 2), ShowItemToolTips = true };
        MainMenuStrip.Items.AddRange(new ToolStripItem[] { file, edit, view, marina, help });
    }

    /// <summary>A top-level menu of commands from the table; a null entry is a separator.</summary>
    private ToolStripMenuItem Menu(string title, params DesignerCommandId?[] commands)
    {
        var menu = new ToolStripMenuItem(title);
        foreach (var id in commands)
        {
            if (id is { } command) menu.DropDownItems.Add(MenuItem(command));
            else menu.DropDownItems.Add(new ToolStripSeparator());
        }

        return menu;
    }

    /// <summary>A menu item for a command of the table, showing the key the table gives it.</summary>
    private ToolStripMenuItem MenuItem(DesignerCommandId id)
    {
        var command = DesignerCommands.Get(id);
        var item = new ToolStripMenuItem(command.Label, null, (_, _) => Run(id));
        if (command.PrimaryGesture(DesignerPlatform.Desktop) is { } gesture)
        {
            // The keys themselves are dispatched from ProcessCmdKey before the menu sees them, which knows about the
            // second key of a command (Ctrl+Shift+Z for Redo) and about fields that keep a key for themselves.
            item.ShortcutKeys = ToKeys(gesture);
            item.ShortcutKeyDisplayString = gesture.DisplayText;
            item.ShowShortcutKeys = true;
        }

        _menuItems[id] = item;
        return item;
    }

    private void BuildToolbar()
    {
        _toolbar.Dock = DockStyle.Top;
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;
        _toolbar.Renderer = new Theme.ToolbarRenderer();
        _toolbar.Padding = new Padding(8, 6, 8, 6);
        _toolbar.ImageScalingSize = new Size(20, 20);
        _toolbar.Font = Theme.Toolbar;
        _toolbar.AccessibleName = Strings.ToolbarLabel;

        // Getting around, then what you place, then what you dress it with, then what you change.
        AddToolButton(DesignTool.Navigate, Strings.ToolNavigate, Strings.ToolNavigateTip);
        AddToolButton(DesignTool.SelectArea, Strings.ToolSelect, Strings.ToolSelectTip);
        _toolbar.Items.Add(new ToolStripSeparator());
        AddToolButton(DesignTool.DrawShoreline, Strings.ToolCoast, Strings.ToolCoastTip);
        AddToolButton(DesignTool.DrawLandArea, Strings.ToolLand, Strings.ToolLandTip);
        AddToolButton(DesignTool.DrawPier, Strings.ToolPier, Strings.ToolPierTip);
        AddToolButton(DesignTool.AddBerths, Strings.ToolBerths, Strings.ToolBerthsTip);
        AddToolButton(DesignTool.AddLandBerths, Strings.ToolAshore, Strings.ToolAshoreTip);
        _toolbar.Items.Add(new ToolStripSeparator());
        AddToolButton(DesignTool.PlantTrees, Strings.ToolTrees, Strings.ToolTreesTip);
        AddToolButton(DesignTool.EditServices, Strings.ToolServices, Strings.ToolServicesTip);
        _toolbar.Items.Add(new ToolStripSeparator());
        AddToolButton(DesignTool.Rename, Strings.ToolRename, Strings.ToolRenameTip);
        AddToolButton(DesignTool.Erase, Strings.ToolErase, Strings.ToolEraseTip);
        _toolbar.Items.Add(new ToolStripSeparator());

        // Not drawing tools: they add the look or camera settings under the tool's own.
        _lookButton = Command(Strings.ToolLook, Strings.ToolLookTip, () => Run(DesignerCommandId.Appearance));
        _camerasButton = Command(Strings.ToolCameras, Strings.ToolCamerasTip, () => Run(DesignerCommandId.Cameras));
        _toolbar.Items.Add(_lookButton);
        _toolbar.Items.Add(_camerasButton);

        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(Command(Strings.CommandTopView, Strings.Format(Strings.CommandTopViewTip, ShortcutOf(DesignerCommandId.TopView)), () => Run(DesignerCommandId.TopView)));
        _toolbar.Items.Add(Command(Strings.CommandFitMarina, Strings.CommandFitMarinaTip, () => Run(DesignerCommandId.FitMarina)));

        _toolbar.Items.Add(new ToolStripSeparator());
        _undoButton = Command(Strings.ToolUndo, Strings.NothingToUndo, () => Run(DesignerCommandId.Undo));
        _redoButton = Command(Strings.ToolRedo, Strings.NothingToRedo, () => Run(DesignerCommandId.Redo));
        _toolbar.Items.Add(_undoButton);
        _toolbar.Items.Add(_redoButton);
    }

    private static string ShortcutOf(DesignerCommandId id) => DesignerCommands.Get(id).ShortcutText(DesignerPlatform.Desktop) ?? string.Empty;

    private void AddToolButton(DesignTool tool, string text, string tip)
    {
        var key = DesignerCommands.ForTool(tool)?.ShortcutText(DesignerPlatform.Desktop);
        var button = new ToolStripButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            Padding = new Padding(12, 4, 12, 4),
            ToolTipText = key is null ? tip : $"{tip} ({key})",
            ForeColor = Theme.Text,
        };
        button.Click += (_, _) =>
        {
            CommitPendingEdit();
            SelectTool(tool);
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
        _status.Items.AddRange(new ToolStripItem[] { _statusHint, _statusNotice, _statusPointer, _statusCamera });
    }

    private void WireEvents()
    {
        Designer.StateChanged += (_, _) => RequestRefresh();

        // The box-select tool changes the selection without touching the designer's own state.
        Marina.SelectionChanged += (_, _) => RequestRefresh();

        // Undo and redo can change a text being typed; the field should show what the design now holds.
        Designer.ActionUndone += (_, _) => _inspector.RequestSync(overwriteFocused: true);
        Designer.ActionRedone += (_, _) => _inspector.RequestSync(overwriteFocused: true);

        // The automatic views follow the layout and the size of the view. A collapsed panel is synced when it opens.
        Marina.CameraPresetsChanged += (_, _) =>
        {
            if (!_cameras.Collapsed) _cameras.Sync();
        };

        _session.TitleChanged += (_, _) => Text = _session.Title;
        _session.SidePanelChanged += (_, _) => ShowSidePanel(_session.SidePanel);
        _session.DocumentReplaced += (_, _) =>
        {
            // A new or opened design brings its own style, traffic and views; the panels still show the old ones.
            _appearance.Sync();
            _cameras.Sync();
            _inspector.RequestSync(overwriteFocused: true);
        };
        _session.Log.Added += (_, _) => AddLogLine();
        _session.Notice += (_, e) => ShowNotice(e.Message);

        _view.RenderError += (_, e) => _session.Log.Add(Strings.Format(Strings.LogRendererError, e.Exception.Message));
        _view.MouseMove += (_, e) => ShowPointer(e.Location);
        _view.MouseLeave += (_, _) => _statusPointer.Text = string.Empty;

        _cameraTimer.Tick += (_, _) => ShowCamera();
        _cameraTimer.Start();
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            _statusNotice.Visible = false;
        };

        FormClosing += OnFormClosing;
    }

    /// <summary>
    /// Asks about unsaved changes before the window closes. The close is held back while the question is asked, and
    /// made again once the answer allows it.
    /// </summary>
    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closeConfirmed) return;
        e.Cancel = true;
        if (_session.IsBusy) return;
        CommitPendingEdit();
        if (!await _session.ConfirmDiscardChangesAsync()) return;
        _closeConfirmed = true;

        // Closed again once this round of closing (held back above) is over: a Close made from inside FormClosing
        // would be ignored.
        BeginInvoke(Close);
    }

    // ---- Keyboard ------------------------------------------------------------------------------

    /// <summary>
    /// Every key of the command table, dispatched here rather than by the menus so the table's rules apply: a field being
    /// typed in keeps Ctrl+Z, Ctrl+Y, F2 and Esc; a tool's letter only counts while the 3D view has the focus; and a
    /// command's second key (Ctrl+Shift+Z for Redo) works as well as the one the menu shows.
    /// </summary>
    /// <param name="msg">The key message.</param>
    /// <param name="keyData">The key, with its modifiers.</param>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var command = DesignerCommands.Match(DesignerPlatform.Desktop, KeyName(keyData & Keys.KeyCode), ToModifiers(keyData));
        if (command is null) return base.ProcessCmdKey(ref msg, keyData);

        var target = FromChildHandle(msg.HWnd) ?? FocusedControl();
        var inView = _view.ContainsFocus;
        if (command.YieldsToTextFields && IsEditing(target)) return false;
        if (command.ViewOnly && !inView) return base.ProcessCmdKey(ref msg, keyData);

        // Esc in the view is the view's own: it drops the drawing and goes back to Navigate there.
        if (command.Id == DesignerCommandId.Escape && inView) return base.ProcessCmdKey(ref msg, keyData);

        RunCommand(command);
        return true;
    }

    /// <summary>A gesture of the command table as WinForms writes it.</summary>
    private static Keys ToKeys(KeyGesture gesture)
    {
        var keys = Enum.Parse<Keys>(gesture.Key);
        if (gesture.Modifiers.HasFlag(KeyModifiers.Control)) keys |= Keys.Control;
        if (gesture.Modifiers.HasFlag(KeyModifiers.Shift)) keys |= Keys.Shift;
        if (gesture.Modifiers.HasFlag(KeyModifiers.Alt)) keys |= Keys.Alt;
        return keys;
    }

    /// <summary>The key's name as the command table writes it: a letter, "F2", "Escape".</summary>
    private static string KeyName(Keys key) => key == Keys.Escape ? "Escape" : key.ToString();

    private static KeyModifiers ToModifiers(Keys keyData)
    {
        var modifiers = KeyModifiers.None;
        if ((keyData & Keys.Control) != 0) modifiers |= KeyModifiers.Control;
        if ((keyData & Keys.Shift) != 0) modifiers |= KeyModifiers.Shift;
        if ((keyData & Keys.Alt) != 0) modifiers |= KeyModifiers.Alt;
        return modifiers;
    }

    /// <summary>The control that has the focus, found through every container on the way down (a NumericUpDown included).</summary>
    private Control? FocusedControl()
    {
        Control? control = this;
        while (control is ContainerControl { ActiveControl: { } inner }) control = inner;
        return control;
    }

    /// <summary>True for a control that edits text of its own: a text box, the box of a number field, an editable list.</summary>
    private static bool IsEditing(Control? control) => control switch
    {
        TextBoxBase { ReadOnly: false } => true,
        UpDownBase => true,
        ComboBox combo => combo.DropDownStyle != ComboBoxStyle.DropDownList || combo.DroppedDown,
        { Parent: UpDownBase } => true,
        _ => false,
    };

    // ---- State ---------------------------------------------------------------------------------

    /// <summary>
    /// Brings the toolbar, menus and panels in line with the designer once the current burst of changes is over. A
    /// dragged slider or an undo that changes a dozen things costs one refresh, not one per change.
    /// </summary>
    private void RequestRefresh()
    {
        _inspector.RequestSync();
        if (_refreshPending || IsDisposed) return;
        _refreshPending = true;
        if (IsHandleCreated) BeginInvoke(RefreshUi);
        else RefreshUi();
    }

    private void RefreshUi()
    {
        _refreshPending = false;
        if (IsDisposed) return;
        foreach (var (tool, button) in _toolButtons) button.Checked = Designer.IsActive && Designer.Tool == tool;
        _statusHint.Text = Designer.ToolHint;
        UpdateHistoryCommands();
        UpdateToggles();
    }

    private void UpdateHistoryCommands()
    {
        var undoTip = DesignerText.UndoTip(Designer);
        var redoTip = DesignerText.RedoTip(Designer);
        _undoButton.Enabled = _session.CanUndo;
        _undoButton.ToolTipText = $"{undoTip} ({ShortcutOf(DesignerCommandId.Undo)})";
        _redoButton.Enabled = Designer.CanRedo;
        _redoButton.ToolTipText = $"{redoTip} ({ShortcutOf(DesignerCommandId.Redo)})";
        _menuItems[DesignerCommandId.Undo].Enabled = _session.CanUndo;
        _menuItems[DesignerCommandId.Undo].ToolTipText = undoTip;
        _menuItems[DesignerCommandId.Redo].Enabled = Designer.CanRedo;
        _menuItems[DesignerCommandId.Redo].ToolTipText = redoTip;
        _menuItems[DesignerCommandId.CancelDraft].Enabled = Designer.HasDraft;
    }

    /// <summary>The ticks on the items that show or hide something, read from what is shown.</summary>
    private void UpdateToggles()
    {
        _menuItems[DesignerCommandId.ShowLog].Checked = _logPanel.Visible;
        _menuItems[DesignerCommandId.Appearance].Checked = _session.SidePanel == DesignerSidePanel.Look;
        _menuItems[DesignerCommandId.Cameras].Checked = _session.SidePanel == DesignerSidePanel.Cameras;
        _menuItems[DesignerCommandId.BerthLabels].Checked = Marina.BerthLabelMode != BerthLabelMode.None;
        _lookButton.Checked = _session.SidePanel == DesignerSidePanel.Look;
        _camerasButton.Checked = _session.SidePanel == DesignerSidePanel.Cameras;
    }

    private void ShowPointer(Point location)
    {
        var point = Designer.PointerPosition ?? (Marina.GetWaterPoint(location.X, location.Y) is { } world ? new Vector2(world.X, world.Z) : (Vector2?)null);
        _statusPointer.Text = DesignerText.PointerText(point);
    }

    /// <summary>The camera readout, redrawn only when the camera has moved since it was last shown.</summary>
    private void ShowCamera()
    {
        var pose = Marina.Camera.Pose;
        if (pose == _shownPose && !string.IsNullOrEmpty(_statusCamera.Text)) return;
        _shownPose = pose;
        _statusCamera.Text = DesignerText.CameraText(pose);
    }

    private void AddLogLine()
    {
        if (_session.Log.Latest is not { } entry || IsDisposed) return;
        _log.Items.Insert(0, entry.Text);
        while (_log.Items.Count > ActivityLog.Capacity) _log.Items.RemoveAt(_log.Items.Count - 1);
    }

    /// <summary>A change the designer refused, shown in the status bar for a few seconds without stopping the work.</summary>
    private void ShowNotice(string message)
    {
        _statusNotice.Text = message;
        _statusNotice.ToolTipText = message;
        _statusNotice.Visible = true;
        _noticeTimer.Stop();
        _noticeTimer.Start();
        System.Media.SystemSounds.Asterisk.Play();
    }

    // ---- Side panel ----------------------------------------------------------------------------

    /// <summary>
    /// Shows what sits beside the marina. The current tool's settings stay on top whatever is open underneath, so
    /// switching to the look or camera settings does not mean losing sight of what the tool in hand is doing.
    /// </summary>
    /// <param name="which">Which settings to show under the tool's own.</param>
    private void ShowSidePanel(DesignerSidePanel which)
    {
        // Collapsed rather than hidden: a hidden panel loses its layout and costs most of a second to show again.
        if (which == DesignerSidePanel.Cameras) _cameras.Sync();

        _side.SuspendLayout();
        _appearance.Collapsed = which != DesignerSidePanel.Look;
        _cameras.Collapsed = which != DesignerSidePanel.Cameras;
        _side.ResumeLayout(performLayout: true);

        // The tool settings are always there, at the top of the column, however far it has been scrolled.
        _side.AutoScrollPosition = Point.Empty;
        UpdateToggles();
    }

    // ---- File ----------------------------------------------------------------------------------

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
            Designer.IsActive = true;
            Designer.FocusReferenceImage();
            Designer.Tool = DesignTool.MeasureScale;
            _session.Log.Add(Strings.Format(Strings.LogImageLoaded, Path.GetFileName(dialog.FileName)));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or OutOfMemoryException or UnauthorizedAccessException)
        {
            _ = _session.WarnAsync(Strings.ImageLoadFailed, ex);
        }
    }

    // ---- Help ----------------------------------------------------------------------------------

    private void ShowShortcuts() => MessageBox.Show(
        this, ShortcutHelp.Build(DesignerPlatform.Desktop), Strings.ShortcutsTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);

    private void ShowAbout() => MessageBox.Show(
        this,
        DesignerText.About(typeof(MainForm), _view.RendererDescription, Author, DateTime.Now.Year),
        Strings.AboutTitle,
        MessageBoxButtons.OK,
        MessageBoxIcon.Information);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cameraTimer.Dispose();
            _noticeTimer.Dispose();
            _session.Dispose();
        }

        base.Dispose(disposing);
    }
}
