using System.Text;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.SampleData;

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
    private const string ReleaseBerth = "release-berth";

    // Key under which we keep our own data on each slip (Slip.ExternalData).
    private const string CheckedInAtKey = "BerthDesk.CheckedInAt";

    private readonly Random _random = new(7);

    public MainForm()
    {
        InitializeComponent();

        // The MarinaViewControl owns the visualizer; everything goes through marinaView.Marina.
        var marina = marinaView.Marina;
        marina.DefaultFocusAngle = CameraAngle.TopDown;        // focus (buttons, actions, double-click) looks straight down

        marina.SlipSelected += OnSlipSelected;                  // fill the tooltip and actions of one slip
        marina.MultiSlipSelected += OnMultiSlipSelected;        // ... or of a Ctrl+click multi-selection
        marina.SlipActionInvoked += OnSlipActionInvoked;        // the user clicked an action in the 3D view
        marina.SelectionChanged += OnSelectionChanged;
        marina.SlipStatusChanged += OnSlipStatusChanged;
        marina.LayoutChanged += OnLayoutChanged;
        marina.SlipHoverChanged += OnSlipHoverChanged;

        // In a real application the layout comes from the ERP database.
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());

        cmbLabelMode.SelectedIndex = (int)SlipLabelMode.None;
        ShowBerthDetails();
    }

    // ---- VirtualMarina events ------------------------------------------------------------------------

    /// <summary>A single slip was selected: add our data to the tooltip and offer the commands that make sense for it.</summary>
    private void OnSlipSelected(object? sender, SlipSelectedEventArgs e)
    {
        Log($"Selected {e.SlipId} ({e.Status}) by {e.Reason}");

        if (e.ExternalData.TryGet<DateTime>(CheckedInAtKey, out var checkedInAt))
        {
            e.Tooltip.AddLine("Checked in", checkedInAt.ToString("g"));
        }

        switch (e.Status)
        {
            case SlipStatus.Free:
                e.Actions.Add(CheckIn, "Check in", icon: "⚓").Style = SlipActionStyle.Primary;
                e.Actions.Add(Reserve, "Reserve", icon: "📅");
                break;
            case SlipStatus.Reserved:
                e.Actions.Add(CheckIn, "Boat arrived", icon: "⚓").Style = SlipActionStyle.Primary;
                e.Actions.Add(CheckOut, "Cancel reservation", icon: "✖").Style = SlipActionStyle.Danger;
                break;
            case SlipStatus.Occupied:
                e.Actions.Add(OwnerAway, "Owner away (temporarily free)", icon: "⛵");
                e.Actions.Add(CheckOut, "Check out", icon: "⇥").Style = SlipActionStyle.Danger;
                break;
            case SlipStatus.TemporarilyFree:
                e.Actions.Add(CheckIn, "Owner returned", icon: "⚓").Style = SlipActionStyle.Primary;
                e.Actions.Add(CheckOut, "End contract", icon: "⇥").Style = SlipActionStyle.Danger;
                break;
        }

        if (e.Berth is not null) e.Actions.Add(ReleaseBerth, "Release multi-slip berth", icon: "⛓");

        e.Actions.Add(FocusCamera, "Focus camera", icon: "🎯").BeginGroup = true;
        e.Actions.Add(Maintenance, "Maintenance (disable)", icon: "🛠");
        e.Actions.Add(Lock, "Lock (read-only)", icon: "🔒");
    }

    /// <summary>Several slips are selected (Ctrl+click): offer commands for all of them.</summary>
    private void OnMultiSlipSelected(object? sender, MultiSlipSelectedEventArgs e)
    {
        Log($"Selected {e.Slips.Count} slips: {string.Join(", ", e.SlipIds)}");

        var moor = e.Actions.Add(MoorAlongside, "Moor one yacht alongside", enabled: CanMoorAlongside(e.Slips), icon: "🛥");
        moor.Style = SlipActionStyle.Primary;
        moor.Description = "Needs two or more free slips on the same dock.";
        e.Actions.Add(CheckOut, $"Check out {e.ActionableSlips.Count} slips", icon: "⇥").Style = SlipActionStyle.Danger;
        e.Actions.Add(FocusCamera, "Focus camera on all", icon: "🎯").BeginGroup = true;
        e.Actions.Add(Maintenance, "Maintenance (disable all)", icon: "🛠");
    }

    /// <summary>The user clicked an action in the 3D view: run the same command as the button on the form.</summary>
    private void OnSlipActionInvoked(object? sender, SlipActionInvokedEventArgs e)
    {
        Log($"Action '{e.ActionId}' on {string.Join(", ", e.Slips.Select(s => s.Id))}");
        RunCommand(e.ActionId, e.ActionableSlips);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => ShowBerthDetails();

    private void OnSlipStatusChanged(object? sender, SlipStatusChangedEventArgs e)
    {
        Log($"{e.SlipId}: {e.OldStatus} -> {e.NewStatus}");
        if (marinaView.Marina.IsSlipSelected(e.SlipId)) ShowBerthDetails();
    }

    private void OnLayoutChanged(object? sender, LayoutChangedEventArgs e)
    {
        if (e.Kind == LayoutChangeKind.Initialized) FillCameraPresets();
        ShowStatistics();
    }

    private void OnSlipHoverChanged(object? sender, SlipHoverEventArgs e) =>
        lblHover.Text = e.Slip is null ? "" : $"{e.Slip.DisplayName} – {e.Slip.Status.GetDisplayName()}";

    // ---- Form controls -------------------------------------------------------------------------------

    private void OnCheckInClick(object sender, EventArgs e) => RunCommand(CheckIn, SelectedSlips());

    private void OnReserveClick(object sender, EventArgs e) => RunCommand(Reserve, SelectedSlips());

    private void OnOwnerAwayClick(object sender, EventArgs e) => RunCommand(OwnerAway, SelectedSlips());

    private void OnCheckOutClick(object sender, EventArgs e) => RunCommand(CheckOut, SelectedSlips());

    private void OnFocusClick(object sender, EventArgs e) => RunCommand(FocusCamera, SelectedSlips());

    private void OnMaintenanceClick(object sender, EventArgs e) => RunCommand(Maintenance, SelectedSlips());

    private void OnReadOnlyClick(object sender, EventArgs e) => RunCommand(Lock, SelectedSlips());

    private void OnMoorAlongsideClick(object sender, EventArgs e) => RunCommand(MoorAlongside, SelectedSlips());

    private void OnReleaseBerthClick(object sender, EventArgs e) => RunCommand(ReleaseBerth, SelectedSlips());

    private void OnShowActionsClick(object sender, EventArgs e) => marinaView.Marina.ShowActions();

    /// <summary>Selects every slip of the current dock. Disabled and hidden slips are skipped by SetSelection.</summary>
    private void OnSelectDockClick(object sender, EventArgs e)
    {
        var marina = marinaView.Marina;
        var dockId = marina.SelectedSlip?.DockId ?? marina.GetDocks()[0].Id;

        var result = marina.SetSelection(marina.GetSlipsByDock(dockId).Select(s => s.Id), focusCamera: true);
        foreach (var rejected in result.Rejected) Log($"Not selected: {rejected.SlipId} ({rejected.Reason})");
    }

    /// <summary>Clears Disabled / Read-only on every slip.</summary>
    private void OnResetFlagsClick(object sender, EventArgs e)
    {
        var marina = marinaView.Marina;
        marina.SetSlipFlags(marina.GetSlips().Select(s => s.Id), visible: true, disabled: false, readOnly: false);
    }

    private void OnStatusFilterChanged(object sender, EventArgs e)
    {
        var filter = SlipStatusFilter.None;
        if (chkFree.Checked) filter |= SlipStatusFilter.Free;
        if (chkOccupied.Checked) filter |= SlipStatusFilter.Occupied;
        if (chkReserved.Checked) filter |= SlipStatusFilter.Reserved;
        if (chkTemporarilyFree.Checked) filter |= SlipStatusFilter.TemporarilyFree;
        marinaView.Marina.SetStatusFilter(filter);
    }

    /// <summary>The combo items are in the same order as the <see cref="SlipLabelMode"/> values.</summary>
    private void OnLabelModeChanged(object sender, EventArgs e) =>
        marinaView.Marina.SlipLabelMode = (SlipLabelMode)cmbLabelMode.SelectedIndex;

    private void OnCameraPresetSelected(object sender, EventArgs e) =>
        marinaView.Marina.ApplyCameraPreset((string)cmbCameraPreset.SelectedItem!);

    private void OnResetViewClick(object sender, EventArgs e) => marinaView.Marina.ResetCamera();

    private void OnMarinaViewRenderError(object? sender, ThreadExceptionEventArgs e) =>
        MessageBox.Show(this, "OpenGL 3.3 could not be initialized:\n\n" + e.Exception.Message, "Render error", MessageBoxButtons.OK, MessageBoxIcon.Error);

    private void OnStatusTimerTick(object sender, EventArgs e)
    {
        var pose = marinaView.Marina.Camera.Pose;
        lblCameraPose.Text = $"Camera: yaw {pose.YawDegrees % 360:0}°, pitch {pose.PitchDegrees:0}°, distance {pose.Distance:0} m";
    }

    // ---- Berth commands (what the ERP does) ----------------------------------------------------------

    private void RunCommand(string command, IReadOnlyList<Slip> slips)
    {
        var marina = marinaView.Marina;
        if (slips.Count == 0)
        {
            Log("Select a berth first.");
            return;
        }

        var ids = slips.Select(s => s.Id).ToArray();
        switch (command)
        {
            case CheckIn:
                foreach (var slip in slips)
                {
                    // Set our data first: AssignBoat immediately refreshes the details panel and the open tooltip.
                    slip.ExternalData[CheckedInAtKey] = DateTime.Now;
                    marina.AssignBoat(slip.Id, slip.Boat ?? FindBoatInErp(slip));
                }

                break;

            case Reserve:
                foreach (var slip in slips.Where(s => s.Status == SlipStatus.Free))
                {
                    marina.ReserveSlip(slip.Id, FindBoatInErp(slip) with { ExpectedArrival = DateTimeOffset.Now.AddHours(6) });
                }

                break;

            case OwnerAway:
                foreach (var slip in slips.Where(s => s.Boat is not null)) marina.MarkTemporarilyFree(slip.Id);
                break;

            case CheckOut:
                foreach (var slip in slips)
                {
                    marina.ReleaseSlip(slip.Id);
                    slip.ExternalData.Remove(CheckedInAtKey);
                }

                break;

            case FocusCamera:
                marina.FocusSlips(ids);
                break;

            case Maintenance:
                marina.SetSlipFlags(ids, disabled: true);
                break;

            case Lock:
                marina.SetSlipFlags(ids, readOnly: true);
                break;

            case MoorAlongside:
                if (!CanMoorAlongside(slips))
                {
                    MessageBox.Show(this, "Select two or more free slips on the same dock (Ctrl+click).", "Moor alongside");
                    return;
                }

                var span = slips.Sum(s => s.Width);
                var yacht = new Boat($"YACHT-{_random.Next(1000, 9999)}", "Visiting yacht", BoatType.MotorYacht)
                {
                    LengthMeters = span - 1.5f,
                    BeamMeters = Math.Clamp(span * 0.25f, 3f, slips.Min(s => s.Length) - 2f),
                };
                marina.DockAlongside(ids, yacht);
                break;

            case ReleaseBerth:
                foreach (var berthId in slips.Select(s => s.BerthId).OfType<string>().Distinct()) marina.ReleaseMultiSlipBerth(berthId);
                break;
        }
    }

    private static bool CanMoorAlongside(IReadOnlyList<Slip> slips) =>
        slips.Count >= 2 &&
        slips.All(s => s.Status == SlipStatus.Free && s.AllowsActions && s.BerthId is null) &&
        slips.Select(s => s.DockId).Distinct().Count() == 1;

    /// <summary>Stands in for an ERP lookup of the boat that belongs to this berth.</summary>
    private Boat FindBoatInErp(Slip slip) => MockMarinaFactory.CreateBoatForSlip(slip, _random);

    // ---- Display helpers -----------------------------------------------------------------------------

    private IReadOnlyList<Slip> SelectedSlips() => marinaView.Marina.SelectedSlips;

    private void ShowBerthDetails()
    {
        var slips = SelectedSlips();
        var text = new StringBuilder();

        if (slips.Count == 0)
        {
            text.AppendLine("Click a berth in the 3D view.");
            text.AppendLine("Ctrl+click selects several, right-click shows actions.");
        }
        else if (slips.Count > 1)
        {
            text.AppendLine($"{slips.Count} berths selected:");
            foreach (var s in slips) text.AppendLine($"  {s.DisplayName,-10} {s.Status.GetDisplayName(),-17} {s.Boat?.Name}");
        }
        else
        {
            var slip = slips[0];
            text.AppendLine($"Berth:   {slip.DisplayName}  ({marinaView.Marina.GetDock(slip.DockId)?.Name})");
            text.AppendLine($"Status:  {slip.Status.GetDisplayName()}{(slip.IsReadOnly ? " (locked)" : "")}");
            text.AppendLine($"Size:    {slip.Length:0.0} x {slip.Width:0.0} m");
            if (slip.Boat is { } boat)
            {
                text.AppendLine($"Boat:    {boat.Name} ({boat.TypeDisplayName}, {boat.LengthMeters:0.0} m)");
                text.AppendLine($"Owner:   {boat.OwnerName}");
            }

            if (slip.BerthId is not null) text.AppendLine($"Berth:   part of multi-slip berth {slip.BerthId}");
            if (slip.ExternalData.TryGet<DateTime>(CheckedInAtKey, out var checkedInAt)) text.AppendLine($"Checked in: {checkedInAt:g}");
        }

        txtBerthDetails.Text = text.ToString();
    }

    private void ShowStatistics()
    {
        var s = marinaView.Marina.GetStatistics();
        lblStatistics.Text = $"{s.TotalSlips} berths · {s.Free} free · {s.Occupied} occupied · {s.Reserved} reserved · {s.TemporarilyFree} temp. free · {s.OccupancyRate:P0} occupancy";
    }

    private void FillCameraPresets()
    {
        cmbCameraPreset.Items.Clear();
        foreach (var preset in marinaView.Marina.CameraPresets) cmbCameraPreset.Items.Add(preset.Name);
        cmbCameraPreset.SelectedItem = MarinaVisualizer.OverviewPresetName;
    }

    private void Log(string message)
    {
        lstEvents.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        if (lstEvents.Items.Count > 200) lstEvents.Items.RemoveAt(lstEvents.Items.Count - 1);
    }
}
