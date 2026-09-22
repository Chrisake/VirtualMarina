using System.Globalization;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.SampleData;

/// <summary>A fake ERP record kept in <see cref="Berth.ExternalData"/>.</summary>
public sealed record SampleContract(string Number, string Holder, decimal Balance, DateOnly ValidUntil);

/// <summary>
/// What a host ERP typically does with the visualizer's interaction events, shared by both test hosts:
/// fills tooltips and actions, runs actions, and keeps its own objects in <see cref="Berth.ExternalData"/>.
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
        _marina.BerthSelected += OnBerthSelected;
        _marina.MultiBerthSelected += OnMultiBerthSelected;
        _marina.BerthActionInvoked += OnBerthActionInvoked;
    }

    public void Dispose()
    {
        _marina.BerthSelected -= OnBerthSelected;
        _marina.MultiBerthSelected -= OnMultiBerthSelected;
        _marina.BerthActionInvoked -= OnBerthActionInvoked;
    }

    // ---- Events -----------------------------------------------------------------------------------

    private void OnBerthSelected(object? sender, BerthSelectedEventArgs e)
    {
        _log($"BerthSelected    {e.BerthId} reason={e.Reason} new={e.IsNewSelection} button={e.Button}");
        var berth = e.Berth;

        // External data: a per-berth counter and a lazily "loaded" contract object.
        if (e.Reason == SelectionReason.Pointer)
        {
            e.ExternalData[ViewCountKey] = e.ExternalData.Get<int>(ViewCountKey) + 1;
        }

        if (berth.Boat is not null && berth.Status != BerthStatus.Reserved)
        {
            var contract = e.ExternalData.GetOrAdd(ContractKey, () => CreateContract(berth));
            e.Tooltip.AddLine("Contract", contract.Number);
            e.Tooltip.AddLine("Balance", contract.Balance.ToString("C", CultureInfo.GetCultureInfo("el-GR")), emphasize: contract.Balance > 0);
        }

        if (berth.Metadata.TryGetValue("DailyRate", out var rate)) e.Tooltip.AddLine("Daily rate", rate);
        if (e.ExternalData.Get<int>(ViewCountKey) is > 1 and var views) e.Tooltip.Footer = $"Opened {views} times this session";

        // Actions depend on the berth's state.
        var a = e.Actions;
        switch (berth.Status)
        {
            case BerthStatus.Free:
                Primary(a.Add("checkin", "Check in walk-in boat", icon: "⚓"));
                a.Add("reserve", "Reserve…", icon: "📅");
                break;
            case BerthStatus.Reserved:
                Primary(a.Add("arrive", "Mark arrived", icon: "⚓"));
                Danger(a.Add("cancel", "Cancel reservation", icon: "✖"));
                break;
            case BerthStatus.Occupied:
                a.Add("tempfree", "Owner away (temporarily free)", icon: "⛵");
                Danger(a.Add("checkout", "Check out", icon: "⇥"));
                break;
            case BerthStatus.TemporarilyFree:
                Primary(a.Add("returned", "Owner returned", icon: "⚓"));
                a.Add("checkout", "End contract (free berth)", icon: "⇥");
                break;
        }

        if (e.MultiBerth is not null) Danger(a.Add("release-berth", $"Release multi-berth ({e.MultiBerth.BerthIds.Count} berths)", icon: "⛓"));

        // Moving boats between the water and the boatyard.
        if (berth.Boat is { } stored && berth.Status == BerthStatus.Occupied && e.MultiBerth is null)
        {
            if (berth.IsOnLand)
            {
                var target = MockMarinaFactory.FindFreeWaterBerth(_marina, stored);
                var launch = a.Add("launch", target is null ? "Launch (no free berth fits)" : $"Launch to {target.DisplayName}", enabled: target is not null, icon: "🌊");
                launch.BeginGroup = true;
            }
            else
            {
                var target = MockMarinaFactory.FindFreeLandBerth(_marina, stored);
                var haulOut = a.Add("haulout", target is null ? "Haul out (boatyard full)" : $"Haul out to {target.DisplayName}", enabled: target is not null, icon: "🏗");
                haulOut.BeginGroup = true;
            }
        }

        var contractAction = a.Add("contract", "Open contract…", enabled: berth.Boat is not null, icon: "📄");
        contractAction.BeginGroup = true;
        contractAction.ShortcutText = "Ctrl+O";
        if (berth.Boat is null) contractAction.Description = "No boat is assigned to this berth.";

        a.Add("focus", "Focus camera (top down)", icon: "🎯");
        a.Add("readonly", "Lock berth (read-only)", icon: "🔒").BeginGroup = true;
        Danger(a.Add("maintenance", "Put under maintenance (disable)", icon: "🛠"));
        a.Add("hide", "Hide berth", icon: "🙈");
    }

    private void OnMultiBerthSelected(object? sender, MultiBerthSelectedEventArgs e)
    {
        _log($"MultiSelected   {string.Join(",", e.BerthIds)} reason={e.Reason} button={e.Button}");

        var actionable = e.ActionableBerths;
        var width = actionable.Sum(s => s.Width);
        e.Tooltip.AddLine("Combined width", string.Format(CultureInfo.CurrentCulture, "{0:0.0} m", width));

        // Any number of berths (two or more) can take one boat alongside.
        var canMoorAlongside = actionable.Count >= 2 && actionable.Count == e.Berths.Count &&
            actionable.All(s => s.Status == BerthStatus.Free && s.MultiBerthId is null) &&
            actionable.All(s => s.PierId is not null) &&
            actionable.Select(s => s.PierId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
        var moor = e.Actions.Add("moor-alongside", $"Moor one yacht alongside {actionable.Count} berths", enabled: canMoorAlongside, icon: "🛥");
        moor.Style = BerthActionStyle.Primary;
        if (!canMoorAlongside) moor.Description = "Select two or more free berths on the same pier (none read-only).";

        var focus = e.Actions.Add("focus-all", $"Focus camera on all {e.Berths.Count} (top down)", icon: "🎯");
        focus.KeepOpen = true;
        e.Actions.Add("tempfree-all", "Mark occupied berths temporarily free", enabled: actionable.Any(s => s.Status == BerthStatus.Occupied), icon: "⛵");
        Danger(e.Actions.Add("free-all", $"Free {actionable.Count} berth(s)", enabled: actionable.Count > 0, icon: "⇥"));
        e.Actions.Add("readonly-all", "Lock all (read-only)", icon: "🔒").BeginGroup = true;
        Danger(e.Actions.Add("maintenance-all", "Put all under maintenance", icon: "🛠"));
    }

    private void OnBerthActionInvoked(object? sender, BerthActionInvokedEventArgs e)
    {
        _log($"ActionInvoked   {e.ActionId} on {string.Join(",", e.Berths.Select(s => s.Id))}");
        var berth = e.Berth;
        var ids = e.ActionableBerths.Select(s => s.Id).ToArray();

        switch (e.ActionId)
        {
            case "checkin":
                _marina.AssignBoat(berth.Id, MockMarinaFactory.CreateBoatForBerth(berth, _rng));
                break;
            case "reserve":
                _marina.ReserveBerth(berth.Id, MockMarinaFactory.CreateBoatForBerth(berth, _rng) with { ExpectedArrival = DateTimeOffset.Now.AddHours(_rng.Next(2, 48)) });
                break;
            case "arrive":
            case "returned":
                _marina.SetBerthStatus(berth.Id, BerthStatus.Occupied, berth.Boat is { } b ? b with { ExpectedArrival = null } : null);
                break;
            case "cancel":
            case "checkout":
                berth.ExternalData.Remove(ContractKey);
                _marina.ReleaseBerth(berth.Id);
                break;
            case "tempfree":
                _marina.MarkTemporarilyFree(berth.Id, berth.Boat is { } away ? away with { ExpectedArrival = DateTimeOffset.Now.AddDays(7) } : null);
                break;
            case "launch":
            case "haulout":
                MoveBoat(berth, e.ActionId == "launch");
                break;
            case "release-berth":
                if (berth.MultiBerthId is { } multiBerthId) _marina.ReleaseMultiBerth(multiBerthId);
                break;
            case "contract":
                var contract = berth.ExternalData.Get<SampleContract>(ContractKey);
                _log($"  -> would open contract {contract?.Number ?? "(none)"} for {berth.Boat?.Name}");
                e.KeepPopupOpen = true;
                break;
            case "focus":
            case "focus-all":
                _marina.FocusBerths(e.Berths.Select(s => s.Id), CameraAngle.TopDown);
                e.KeepPopupOpen = true;
                break;
            case "readonly":
            case "readonly-all":
                _marina.SetBerthFlags(ids, readOnly: true);
                break;
            case "maintenance":
            case "maintenance-all":
                _marina.SetBerthFlags(ids, disabled: true);
                break;
            case "hide":
                _marina.SetBerthVisible(berth.Id, false);
                break;
            case "moor-alongside":
                MoorYachtAlongside(ids);
                break;
            case "tempfree-all":
                _marina.BatchUpdate(e.ActionableBerths.Where(s => s.Status == BerthStatus.Occupied).Select(s => BerthUpdate.TemporarilyFree(s.Id)));
                break;
            case "free-all":
                _marina.BatchUpdate(ids.Select(BerthUpdate.Free));
                break;
        }
    }

    // ---- Helpers for host buttons -----------------------------------------------------------------

    /// <summary>Moors a generated yacht alongside the given berths. Returns the berth, or null when not possible.</summary>
    public MultiBerth? MoorYachtAlongside(IReadOnlyList<string> berthIds)
    {
        var berths = berthIds.Select(_marina.GetBerth).OfType<Berth>().ToList();
        if (berths.Count < 2)
        {
            _log("Select at least two berths (Ctrl+click or Shift+click) to moor a yacht alongside.");
            return null;
        }

        var span = berths.Sum(s => s.Width);
        var depth = berths.Min(s => s.Length);
        var boat = new Boat($"BT-{_rng.Next(10000, 99999)}", "Alongside Guest", BoatType.MotorYacht)
        {
            LengthMeters = MathF.Round(MathF.Max(6f, span - 1.2f), 1),
            BeamMeters = MathF.Round(Math.Clamp((span - 1.2f) * 0.28f, 2.5f, depth - 1.5f), 1),
            OwnerName = "Visiting yacht",
        };

        try
        {
            var berth = _marina.MoorAlongside(berths.Select(s => s.Id), boat);
            _log($"Berth           {berth.Id}: {boat.LengthMeters:0.0} m yacht alongside {string.Join(", ", berth.BerthIds)}");
            return berth;
        }
        catch (Exception ex) when (ex is InvalidOperationException or MarinaLayoutException)
        {
            _log("Cannot moor alongside: " + ex.Message);
            return null;
        }
    }

    /// <summary>Moves a berth's boat into the water (<paramref name="launch"/>) or up onto a land berth, in one batch.</summary>
    public Berth? MoveBoat(Berth from, bool launch)
    {
        if (from.Boat is not { } boat) return null;
        var target = launch ? MockMarinaFactory.FindFreeWaterBerth(_marina, boat) : MockMarinaFactory.FindFreeLandBerth(_marina, boat);
        if (target is null)
        {
            _log(launch ? "No free berth fits this boat." : "The boatyard has no free spot for this boat.");
            return null;
        }

        var result = _marina.BatchUpdate(new[] { BerthUpdate.Free(from.Id), BerthUpdate.Occupy(target.Id, boat) });
        _log($"{(launch ? "Launched" : "Hauled out"),-15} {boat.Name}: {from.DisplayName} -> {target.DisplayName} (applied {result.AppliedCount})");
        _marina.SelectBerth(target.Id, focusCamera: true);
        return _marina.GetBerth(target.Id);
    }

    /// <summary>Clears Disabled / Read-only on every berth and shows hidden ones.</summary>
    public void ResetAllFlags()
    {
        var result = _marina.SetBerthFlags(_marina.GetBerths().Select(s => s.Id), visible: true, disabled: false, readOnly: false);
        _log($"Flags reset on {result.AppliedCount} berths");
    }

    private SampleContract CreateContract(Berth berth)
    {
        var number = $"CT-{DateTime.Today.Year}-{_rng.Next(1000, 9999)}";
        _log($"  -> loaded contract {number} for {berth.Id} into ExternalData");
        return new SampleContract(number, berth.Boat?.OwnerName ?? "-", _rng.Next(0, 4) == 0 ? _rng.Next(50, 900) : 0m, DateOnly.FromDateTime(DateTime.Today.AddMonths(_rng.Next(1, 12))));
    }

    private static void Primary(BerthAction action) => action.Style = BerthActionStyle.Primary;

    private static void Danger(BerthAction action) => action.Style = BerthActionStyle.Danger;
}
