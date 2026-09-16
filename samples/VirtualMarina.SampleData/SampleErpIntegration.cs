using System.Globalization;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.SampleData;

/// <summary>A fake ERP record kept in <see cref="Slip.ExternalData"/>.</summary>
public sealed record SampleContract(string Number, string Holder, decimal Balance, DateOnly ValidUntil);

/// <summary>
/// What a host ERP typically does with the visualizer's interaction events, shared by both test hosts:
/// fills tooltips and actions, runs actions, and keeps its own objects in <see cref="Slip.ExternalData"/>.
/// </summary>
public sealed class SampleErpIntegration : IDisposable
{
    public const string ContractKey = "Erp.Contract";
    public const string ViewCountKey = "Erp.TooltipViews";

    private readonly MarinaVisualizer _marina;
    private readonly Random _rng;
    private readonly Action<string> _log;

    public SampleErpIntegration(MarinaVisualizer marina, Random rng, Action<string> log)
    {
        _marina = marina ?? throw new ArgumentNullException(nameof(marina));
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        _log = log ?? (_ => { });
        _marina.SlipSelected += OnSlipSelected;
        _marina.MultiSlipSelected += OnMultiSlipSelected;
        _marina.SlipActionInvoked += OnSlipActionInvoked;
    }

    public void Dispose()
    {
        _marina.SlipSelected -= OnSlipSelected;
        _marina.MultiSlipSelected -= OnMultiSlipSelected;
        _marina.SlipActionInvoked -= OnSlipActionInvoked;
    }

    // ---- Events -----------------------------------------------------------------------------------

    private void OnSlipSelected(object? sender, SlipSelectedEventArgs e)
    {
        _log($"SlipSelected    {e.SlipId} reason={e.Reason} new={e.IsNewSelection} button={e.Button}");
        var slip = e.Slip;

        // External data: a per-slip counter and a lazily "loaded" contract object.
        if (e.Reason == SelectionReason.Pointer)
        {
            e.ExternalData[ViewCountKey] = e.ExternalData.Get<int>(ViewCountKey) + 1;
        }

        if (slip.Boat is not null && slip.Status != SlipStatus.Reserved)
        {
            var contract = e.ExternalData.GetOrAdd(ContractKey, () => CreateContract(slip));
            e.Tooltip.AddLine("Contract", contract.Number);
            e.Tooltip.AddLine("Balance", contract.Balance.ToString("C", CultureInfo.GetCultureInfo("el-GR")), emphasize: contract.Balance > 0);
        }

        if (slip.Metadata.TryGetValue("DailyRate", out var rate)) e.Tooltip.AddLine("Daily rate", rate);
        if (e.ExternalData.Get<int>(ViewCountKey) is > 1 and var views) e.Tooltip.Footer = $"Opened {views} times this session";

        // Actions depend on the slip's state.
        var a = e.Actions;
        switch (slip.Status)
        {
            case SlipStatus.Free:
                Primary(a.Add("checkin", "Check in walk-in boat", icon: "⚓"));
                a.Add("reserve", "Reserve…", icon: "📅");
                break;
            case SlipStatus.Reserved:
                Primary(a.Add("arrive", "Mark arrived", icon: "⚓"));
                Danger(a.Add("cancel", "Cancel reservation", icon: "✖"));
                break;
            case SlipStatus.Occupied:
                a.Add("tempfree", "Owner away (temporarily free)", icon: "⛵");
                Danger(a.Add("checkout", "Check out", icon: "⇥"));
                break;
            case SlipStatus.TemporarilyFree:
                Primary(a.Add("returned", "Owner returned", icon: "⚓"));
                a.Add("checkout", "End contract (free slip)", icon: "⇥");
                break;
        }

        if (e.Berth is not null) Danger(a.Add("release-berth", $"Release berth ({e.Berth.SlipIds.Count} slips)", icon: "⛓"));

        var contractAction = a.Add("contract", "Open contract…", enabled: slip.Boat is not null, icon: "📄");
        contractAction.BeginGroup = true;
        contractAction.ShortcutText = "Ctrl+O";
        if (slip.Boat is null) contractAction.Description = "No boat is assigned to this slip.";

        a.Add("focus", "Focus camera (top down)", icon: "🎯");
        a.Add("readonly", "Lock slip (read-only)", icon: "🔒").BeginGroup = true;
        Danger(a.Add("maintenance", "Put under maintenance (disable)", icon: "🛠"));
        a.Add("hide", "Hide slip", icon: "🙈");
    }

    private void OnMultiSlipSelected(object? sender, MultiSlipSelectedEventArgs e)
    {
        _log($"MultiSelected   {string.Join(",", e.SlipIds)} reason={e.Reason} button={e.Button}");

        var actionable = e.ActionableSlips;
        var width = actionable.Sum(s => s.Width);
        e.Tooltip.AddLine("Combined width", string.Format(CultureInfo.CurrentCulture, "{0:0.0} m", width));

        // Any number of slips (two or more) can take one boat alongside.
        var canMoorAlongside = actionable.Count >= 2 && actionable.Count == e.Slips.Count &&
            actionable.All(s => s.Status == SlipStatus.Free && s.BerthId is null) &&
            actionable.Select(s => s.DockId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
        var moor = e.Actions.Add("moor-alongside", $"Moor one yacht alongside {actionable.Count} slips", enabled: canMoorAlongside, icon: "🛥");
        moor.Style = SlipActionStyle.Primary;
        if (!canMoorAlongside) moor.Description = "Select two or more free slips on the same dock (none read-only).";

        var focus = e.Actions.Add("focus-all", $"Focus camera on all {e.Slips.Count} (top down)", icon: "🎯");
        focus.KeepOpen = true;
        e.Actions.Add("tempfree-all", "Mark occupied slips temporarily free", enabled: actionable.Any(s => s.Status == SlipStatus.Occupied), icon: "⛵");
        Danger(e.Actions.Add("free-all", $"Free {actionable.Count} slip(s)", enabled: actionable.Count > 0, icon: "⇥"));
        e.Actions.Add("readonly-all", "Lock all (read-only)", icon: "🔒").BeginGroup = true;
        Danger(e.Actions.Add("maintenance-all", "Put all under maintenance", icon: "🛠"));
    }

    private void OnSlipActionInvoked(object? sender, SlipActionInvokedEventArgs e)
    {
        _log($"ActionInvoked   {e.ActionId} on {string.Join(",", e.Slips.Select(s => s.Id))}");
        var slip = e.Slip;
        var ids = e.ActionableSlips.Select(s => s.Id).ToArray();

        switch (e.ActionId)
        {
            case "checkin":
                _marina.AssignBoat(slip.Id, MockMarinaFactory.CreateBoatForSlip(slip, _rng));
                break;
            case "reserve":
                _marina.ReserveSlip(slip.Id, MockMarinaFactory.CreateBoatForSlip(slip, _rng) with { ExpectedArrival = DateTimeOffset.Now.AddHours(_rng.Next(2, 48)) });
                break;
            case "arrive":
            case "returned":
                _marina.SetSlipStatus(slip.Id, SlipStatus.Occupied, slip.Boat is { } b ? b with { ExpectedArrival = null } : null);
                break;
            case "cancel":
            case "checkout":
                slip.ExternalData.Remove(ContractKey);
                _marina.ReleaseSlip(slip.Id);
                break;
            case "tempfree":
                _marina.MarkTemporarilyFree(slip.Id, slip.Boat is { } away ? away with { ExpectedArrival = DateTimeOffset.Now.AddDays(7) } : null);
                break;
            case "release-berth":
                if (slip.BerthId is { } berthId) _marina.ReleaseMultiSlipBerth(berthId);
                break;
            case "contract":
                var contract = slip.ExternalData.Get<SampleContract>(ContractKey);
                _log($"  -> would open contract {contract?.Number ?? "(none)"} for {slip.Boat?.Name}");
                e.KeepPopupOpen = true;
                break;
            case "focus":
            case "focus-all":
                _marina.FocusSlips(e.Slips.Select(s => s.Id), CameraAngle.TopDown);
                e.KeepPopupOpen = true;
                break;
            case "readonly":
            case "readonly-all":
                _marina.SetSlipFlags(ids, readOnly: true);
                break;
            case "maintenance":
            case "maintenance-all":
                _marina.SetSlipFlags(ids, disabled: true);
                break;
            case "hide":
                _marina.SetSlipVisible(slip.Id, false);
                break;
            case "moor-alongside":
                MoorYachtAlongside(ids);
                break;
            case "tempfree-all":
                _marina.BatchUpdate(e.ActionableSlips.Where(s => s.Status == SlipStatus.Occupied).Select(s => SlipUpdate.TemporarilyFree(s.Id)));
                break;
            case "free-all":
                _marina.BatchUpdate(ids.Select(SlipUpdate.Free));
                break;
        }
    }

    // ---- Helpers for host buttons -----------------------------------------------------------------

    /// <summary>Moors a generated yacht alongside the given slips. Returns the berth, or null when not possible.</summary>
    public MultiSlipBerth? MoorYachtAlongside(IReadOnlyList<string> slipIds)
    {
        var slips = slipIds.Select(_marina.GetSlip).OfType<Slip>().ToList();
        if (slips.Count < 2)
        {
            _log("Select at least two slips (Ctrl+click) to moor a yacht alongside.");
            return null;
        }

        var span = slips.Sum(s => s.Width);
        var depth = slips.Min(s => s.Length);
        var boat = new Boat($"BT-{_rng.Next(10000, 99999)}", "Alongside Guest", BoatType.MotorYacht)
        {
            LengthMeters = MathF.Round(MathF.Max(6f, span - 1.2f), 1),
            BeamMeters = MathF.Round(Math.Clamp((span - 1.2f) * 0.28f, 2.5f, depth - 1.5f), 1),
            OwnerName = "Visiting yacht",
        };

        try
        {
            var berth = _marina.DockAlongside(slips.Select(s => s.Id), boat);
            _log($"Berth           {berth.Id}: {boat.LengthMeters:0.0} m yacht alongside {string.Join(", ", berth.SlipIds)}");
            return berth;
        }
        catch (Exception ex) when (ex is InvalidOperationException or MarinaLayoutException)
        {
            _log("Cannot moor alongside: " + ex.Message);
            return null;
        }
    }

    /// <summary>Clears Disabled / Read-only on every slip and shows hidden ones.</summary>
    public void ResetAllFlags()
    {
        var result = _marina.SetSlipFlags(_marina.GetSlips().Select(s => s.Id), visible: true, disabled: false, readOnly: false);
        _log($"Flags reset on {result.AppliedCount} slips");
    }

    private SampleContract CreateContract(Slip slip)
    {
        var number = $"CT-{DateTime.Today.Year}-{_rng.Next(1000, 9999)}";
        _log($"  -> loaded contract {number} for {slip.Id} into ExternalData");
        return new SampleContract(number, slip.Boat?.OwnerName ?? "-", _rng.Next(0, 4) == 0 ? _rng.Next(50, 900) : 0m, DateOnly.FromDateTime(DateTime.Today.AddMonths(_rng.Next(1, 12))));
    }

    private static void Primary(SlipAction action) => action.Style = SlipActionStyle.Primary;

    private static void Danger(SlipAction action) => action.Style = SlipActionStyle.Danger;
}
