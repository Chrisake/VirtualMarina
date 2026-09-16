using System.Text;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.SampleData;
using VirtualMarina.WinForms;

namespace VirtualMarina.TestHost.WinForms;

/// <summary>
/// Simulates a legacy desktop ERP screen hosting the 3D marina control, with buttons that
/// exercise every public API group and a log of the events the library raises.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly MarinaViewControl _view;
    private readonly MarinaVisualizer _marina;
    private readonly Random _rng = new(7);
    private bool _suppressPresetApply;

    private readonly Label _selectionLabel = new() { AutoSize = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9f) };
    private readonly Label _statsLabel = new() { AutoSize = true };
    private readonly ComboBox _presetCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly ComboBox _labelModeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, FormattingEnabled = true };
    private readonly CheckBox _showFree = new() { Text = "Free", Checked = true, AutoSize = true, ForeColor = Color.ForestGreen };
    private readonly CheckBox _showOccupied = new() { Text = "Occupied", Checked = true, AutoSize = true, ForeColor = Color.Firebrick };
    private readonly CheckBox _showReserved = new() { Text = "Reserved", Checked = true, AutoSize = true, ForeColor = Color.RoyalBlue };
    private readonly CheckBox _showTemporarilyFree = new() { Text = "Temp. free", Checked = true, AutoSize = true, ForeColor = Color.DarkGoldenrod };
    private readonly SampleErpIntegration _erp;
    private readonly ListBox _eventLog = new() { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font("Consolas", 8.5f) };
    private readonly ToolStripStatusLabel _statusRenderer = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _statusHover = new() { AutoSize = true };

    /// <param name="args">
    /// Optional: <c>--preset "Dock: Dock A"</c> applies a camera preset, <c>--select A-L03</c> selects a slip.
    /// </param>
    public MainForm(string[] args)
    {
        Text = "VirtualMarina – WinForms Test Host (OpenGL)";
        Width = 1500;
        Height = 900;
        StartPosition = FormStartPosition.CenterScreen;

        // Every focus in the test host (buttons, actions, double-click) looks straight down.
        _marina = new MarinaVisualizer { DefaultFocusAngle = CameraAngle.TopDown };
        _view = new MarinaViewControl(_marina) { Dock = DockStyle.Fill };
        _view.RenderError += (_, e) => MessageBox.Show(this,
            "OpenGL 3.3 could not be initialized:\n\n" + e.Exception.Message, "Render error", MessageBoxButtons.OK, MessageBoxIcon.Error);

        BuildLayout();
        WireMarinaEvents();
        _erp = new SampleErpIntegration(_marina, _rng, Log);

        _marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());
        RefreshPresets();
        RefreshSelection();
        RefreshStatistics();
        ApplyCommandLine(args);
    }

    private void ApplyCommandLine(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--preset":
                    _marina.ApplyCameraPreset(args[i + 1], immediate: true);
                    break;
                case "--select":
                    // Comma-separated ids; disabled/hidden ones are skipped.
                    _marina.SetSelection(args[i + 1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                    break;
                case "--focus":
                    // Comma-separated ids, framed top-down.
                    _marina.FocusSlips(args[i + 1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), CameraAngle.TopDown, immediate: true);
                    break;
                case "--labels":
                    if (Enum.TryParse<SlipLabelMode>(args[i + 1], ignoreCase: true, out var mode)) _labelModeCombo.SelectedItem = mode;
                    break;
            }
        }
    }

    private void BuildLayout()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 5,
        };
        split.Panel1.Controls.Add(_view);

        var side = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(6),
            AutoScroll = true,
        };
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 330));
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // slip labels
        side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Selected slip
        var selectionGroup = Group("Selected slip");
        var selectionPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        selectionPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        selectionPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        selectionPanel.Controls.Add(_selectionLabel, 0, 0);
        selectionPanel.Controls.Add(Flow(
            Button("Free", (_, _) => SetSelectedStatus(SlipStatus.Free)),
            Button("Occupied", (_, _) => SetSelectedStatus(SlipStatus.Occupied)),
            Button("Reserved", (_, _) => SetSelectedStatus(SlipStatus.Reserved)),
            Button("Temp. free", (_, _) => SetSelectedStatus(SlipStatus.TemporarilyFree)),
            Button("Actions…", (_, _) => _marina.ShowActions()),
            Button("Focus (top down)", (_, _) => _marina.FocusSelection(CameraAngle.TopDown)),
            Button("Select whole dock", (_, _) => SelectWholeDock()),
            Button("Clear", (_, _) => _marina.ClearSelection()),
            Button("Read-only", (_, _) => _marina.SetSlipFlags(SelectedIds(), readOnly: true)),
            Button("Disable", (_, _) => _marina.SetSlipFlags(SelectedIds(), disabled: true)),
            Button("Hide", (_, _) => _marina.SetSlipFlags(SelectedIds(), visible: false)),
            Button("Reset all flags", (_, _) => _erp.ResetAllFlags())), 0, 1);
        selectionGroup.Controls.Add(selectionPanel);
        side.Controls.Add(selectionGroup);

        // Filter
        var filterGroup = Group("Status filter");
        foreach (var box in new[] { _showFree, _showOccupied, _showReserved, _showTemporarilyFree }) box.CheckedChanged += (_, _) => ApplyFilter();
        filterGroup.Controls.Add(Flow(_showFree, _showOccupied, _showReserved, _showTemporarilyFree, Button("Show all", (_, _) =>
        {
            _showFree.Checked = _showOccupied.Checked = _showReserved.Checked = _showTemporarilyFree.Checked = true;
        })));
        // Slip labels on the water
        foreach (var mode in Enum.GetValues<SlipLabelMode>()) _labelModeCombo.Items.Add(mode);
        _labelModeCombo.Format += (_, e) => { if (e.ListItem is SlipLabelMode m) e.Value = m.GetDisplayName(); };
        _labelModeCombo.SelectedItem = _marina.SlipLabelMode;
        _labelModeCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_labelModeCombo.SelectedItem is not SlipLabelMode mode) return;
            _marina.SlipLabelMode = mode;
            Log($"SlipLabelMode   {mode}");
        };
        side.Controls.Add(filterGroup);
        var labelGroup = Group("Slip labels on the water");
        labelGroup.Controls.Add(Flow(_labelModeCombo, new Label { Text = "None · Only free · Non-occupied · All", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(6, 7, 0, 0) }));
        side.Controls.Add(labelGroup);

        // Camera
        var cameraGroup = Group("Camera");
        _presetCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!_suppressPresetApply && _presetCombo.SelectedItem is string name) _marina.ApplyCameraPreset(name);
        };
        cameraGroup.Controls.Add(Flow(_presetCombo, Button("Reset view", (_, _) => _marina.ResetCamera())));
        side.Controls.Add(cameraGroup);

        // Space management
        var spaceGroup = Group("Space management");
        spaceGroup.Controls.Add(Flow(
            Button("Add guest slip (D)", (_, _) => AddGuestSlip()),
            Button("Remove selected", (_, _) => RemoveSelected()),
            Button("Batch: 15 random", (_, _) => RunBatch()),
            Button("Moor yacht alongside selection", (_, _) => _erp.MoorYachtAlongside(SelectedIds())),
            Button("Release berth", (_, _) => ReleaseSelectedBerth()),
            Button("Reload layout", (_, _) => { _marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina(_rng.Next())); RefreshPresets(); }),
            Button("Toggle blue/purple", (_, _) => ToggleReservedColor())));
        side.Controls.Add(spaceGroup);

        var statsGroup = Group("Statistics");
        statsGroup.Controls.Add(_statsLabel);
        side.Controls.Add(statsGroup);

        var logGroup = Group("Events raised by the library");
        logGroup.Dock = DockStyle.Fill;
        logGroup.Controls.Add(_eventLog);
        side.Controls.Add(logGroup);

        split.Panel2.Controls.Add(side);

        var help = new ToolStripStatusLabel("Click: tooltip | Ctrl+click: multi-select | Right-click: actions | Left-drag: pan | Right-drag: orbit | Wheel: zoom | Double-click: focus | Esc: close/clear");
        var status = new StatusStrip();
        status.Items.AddRange(new ToolStripItem[] { _statusRenderer, _statusHover, help });

        Controls.Add(split);
        Controls.Add(status);

        Load += (_, _) =>
        {
            split.SplitterDistance = Math.Max(300, ClientSize.Width - 520);
        };

        var statusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        statusTimer.Interval = 250;
        statusTimer.Tick += (_, _) =>
        {
            var pose = _marina.Camera.Pose;
            _statusRenderer.Text = $"Camera ({pose.Target.X:0.0}, {pose.Target.Z:0.0}) yaw {pose.YawDegrees % 360:0}° pitch {pose.PitchDegrees:0}° dist {pose.Distance:0} m | view {_marina.ViewportSize.X:0}×{_marina.ViewportSize.Y:0}";
            _statusRenderer.ToolTipText = _view.RendererDescription;
        };
        statusTimer.Start();
    }

    private void WireMarinaEvents()
    {
        _marina.SlipClicked += (_, e) =>
            Log($"SlipClicked     {e.SlipId} [{e.Status}] button={e.Button}{(e.IsDoubleClick ? " (double)" : "")} boat={e.Boat?.Name ?? "-"}");
        // SlipSelected / MultiSlipSelected / SlipActionInvoked are handled (and logged) by SampleErpIntegration.
        _marina.SelectionChanged += (_, _) => RefreshSelection();
        _marina.SelectionCleared += (_, _) => Log("SelectionCleared");
        _marina.SlipHoverChanged += (_, e) =>
            _statusHover.Text = e.Slip is null ? string.Empty : $"Hover: {e.Slip.DisplayName} ({e.Slip.Status})";
        _marina.SlipStatusChanged += (_, e) =>
        {
            Log($"StatusChanged   {e.SlipId}: {e.OldStatus} -> {e.NewStatus} boat={e.NewBoat?.Name ?? "-"}");
            if (_marina.IsSlipSelected(e.SlipId)) RefreshSelection();
        };
        _marina.LayoutChanged += (_, e) =>
        {
            Log($"LayoutChanged   {e.Kind} {e.DockId} {e.SlipId} {e.BerthId}".TrimEnd());
            RefreshStatistics();
            RefreshSelection();
        };
        _marina.PopupChanged += (_, e) =>
            Log(e.Current is null ? "PopupClosed" : $"Popup           {e.Current.Kind} for {e.Current.Slips.Count} slip(s), {e.Current.Actions.Count} action(s)");
    }

    private string[] SelectedIds() => _marina.SelectedSlips.Select(s => s.Id).ToArray();

    /// <summary>Selects every slip on the selected slip's dock (disabled ones are discarded) and frames them top-down.</summary>
    private void SelectWholeDock()
    {
        var dockId = _marina.SelectedSlip?.DockId ?? _marina.GetDocks().FirstOrDefault()?.Id;
        if (dockId is null) return;

        var result = _marina.SetSelection(_marina.GetSlipsByDock(dockId).Select(s => s.Id), focusCamera: true, CameraAngle.TopDown);
        Log($"SetSelection    {result.Count} selected on dock {dockId}; skipped: " +
            (result.Rejected.Count == 0 ? "none" : string.Join(", ", result.Rejected.Select(r => $"{r.SlipId} ({r.Reason})"))));
    }

    private void SetSelectedStatus(SlipStatus status)
    {
        using (_marina.BeginUpdate())
        {
            foreach (var slip in _marina.SelectedSlips)
            {
                var boat = status == SlipStatus.Free ? null : slip.Boat ?? MockMarinaFactory.CreateBoatForSlip(slip, _rng);
                _marina.SetSlipStatus(slip.Id, status, boat);
            }
        }
    }

    private void ReleaseSelectedBerth()
    {
        var berthIds = _marina.SelectedSlips.Select(s => s.BerthId).OfType<string>().Distinct().ToList();
        if (berthIds.Count == 0) Log("Select a slip of a multi-slip berth first.");
        foreach (var id in berthIds) _marina.ReleaseMultiSlipBerth(id);
    }

    private void ApplyFilter()
    {
        var filter = SlipStatusFilter.None;
        if (_showFree.Checked) filter |= SlipStatusFilter.Free;
        if (_showOccupied.Checked) filter |= SlipStatusFilter.Occupied;
        if (_showReserved.Checked) filter |= SlipStatusFilter.Reserved;
        if (_showTemporarilyFree.Checked) filter |= SlipStatusFilter.TemporarilyFree;
        _marina.SetStatusFilter(filter);
        Log($"Filter          {filter}");
    }

    private void AddGuestSlip()
    {
        var slip = MockMarinaFactory.CreateGuestSlip(_marina, "D");
        if (slip is null)
        {
            Log("Dock D guest berths are full.");
            return;
        }

        _marina.AddSlip(slip);
        _marina.SelectSlip(slip.Id, focusCamera: true);
    }

    private void RemoveSelected()
    {
        if (_marina.SelectedSlip is { } slip) _marina.RemoveSlip(slip.Id);
    }

    private void RunBatch()
    {
        var updates = MockMarinaFactory.CreateRandomActivity(_marina.GetSlips(), _rng, 15);
        var result = _marina.BatchUpdate(updates.Append(SlipUpdate.Free("DOES-NOT-EXIST")));
        Log($"BatchUpdate     applied={result.AppliedCount} errors={result.Errors.Count} ({string.Join("; ", result.Errors.Select(e => e.SlipId))})");
    }

    private void ToggleReservedColor()
    {
        var purple = new Core.Rendering.ColorRgba(0.6f, 0.3f, 0.9f);
        _marina.SetStatusColor(SlipStatus.Reserved,
            _marina.GetStatusColor(SlipStatus.Reserved) == purple ? StatusColorScheme.DefaultReserved : purple);
    }

    private void RefreshPresets()
    {
        _suppressPresetApply = true;
        _presetCombo.BeginUpdate();
        _presetCombo.Items.Clear();
        foreach (var preset in _marina.CameraPresets) _presetCombo.Items.Add(preset.Name);
        _presetCombo.EndUpdate();
        _presetCombo.SelectedItem = MarinaVisualizer.OverviewPresetName;
        _suppressPresetApply = false;
    }

    private void RefreshSelection()
    {
        if (_marina.SelectedSlip is not { } slip)
        {
            _selectionLabel.Text = "Click a slip or boat in the 3D view.\r\nCtrl+click to select several, right-click for actions.";
            return;
        }

        var sb = new StringBuilder();
        if (_marina.IsMultiSelection)
        {
            sb.AppendLine($"{_marina.SelectedSlips.Count} slips selected (primary last):");
            foreach (var s in _marina.SelectedSlips) sb.AppendLine($"  {s.DisplayName,-12} {s.Status,-16} {s.Boat?.Name}");
            _selectionLabel.Text = sb.ToString();
            return;
        }

        sb.AppendLine($"Slip:    {slip.DisplayName} ({slip.Id})");
        sb.AppendLine($"Dock:    {_marina.GetDock(slip.DockId)?.Name}");
        sb.AppendLine($"Status:  {slip.Status}");
        sb.AppendLine($"Size:    {slip.Length:0.0} m x {slip.Width:0.0} m, draft {slip.MaxDraft?.ToString("0.0") ?? "?"} m");
        if (slip.Boat is { } boat)
        {
            sb.AppendLine($"Boat:    {boat.Name} ({boat.TypeDisplayName})");
            sb.AppendLine($"         {boat.LengthMeters:0.0} m x {boat.BeamMeters:0.0} m, {boat.RegistrationNumber}");
            sb.AppendLine($"Owner:   {boat.OwnerName}");
            if (boat.ExpectedArrival is { } eta) sb.AppendLine($"ETA:     {eta:g}");
        }

        if (slip.BerthId is { } berthId) sb.AppendLine($"Berth:   {berthId} ({_marina.GetMultiSlipBerth(berthId)?.Style})");
        var flags = string.Join(", ", new[] { slip.IsReadOnly ? "read-only" : null, slip.IsDisabled ? "disabled" : null }.OfType<string>());
        if (flags.Length > 0) sb.AppendLine($"Flags:   {flags}");
        foreach (var (key, value) in slip.Metadata) sb.AppendLine($"{key,-8} {value}");
        foreach (var (key, value) in slip.ExternalData) sb.AppendLine($"ext:{key} = {value}");
        _selectionLabel.Text = sb.ToString();
    }

    private void RefreshStatistics()
    {
        var s = _marina.GetStatistics();
        _statsLabel.Text = $"{s.TotalSlips} slips   Free {s.Free}   Occupied {s.Occupied}   Reserved {s.Reserved}   Temp. free {s.TemporarilyFree}   Occupancy {s.OccupancyRate:P0}";
    }

    private void Log(string message)
    {
        _eventLog.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (_eventLog.Items.Count > 300) _eventLog.Items.RemoveAt(_eventLog.Items.Count - 1);
    }

    private static GroupBox Group(string title) => new()
    {
        Text = title,
        Dock = DockStyle.Fill,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(6),
        Margin = new Padding(0, 0, 0, 6),
    };

    private static FlowLayoutPanel Flow(params Control[] controls)
    {
        // An auto-sized flow panel only wraps when its width is capped.
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, MaximumSize = new Size(470, 0) };
        panel.Controls.AddRange(controls);
        return panel;
    }

    private static Button Button(string text, EventHandler onClick)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += onClick;
        return button;
    }
}
