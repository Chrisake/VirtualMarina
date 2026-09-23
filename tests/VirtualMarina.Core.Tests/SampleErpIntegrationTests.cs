using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;
using VirtualMarina.SampleData;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// The sample ERP integration, driven the way a host application drives it: select a berth, read what the handler
/// put in the tooltip and the action list, invoke one, and check the marina changed accordingly.
/// </summary>
/// <remarks>
/// This is the worked example the documentation points integrators at, so it is worth holding to the same standard
/// as the library: a sample that quietly stops working teaches the wrong thing.
/// </remarks>
public sealed class SampleErpIntegrationTests : IDisposable
{
    private readonly MarinaVisualizer _marina = new();
    private readonly SampleErpIntegration _erp;
    private readonly List<string> _log = [];

    public SampleErpIntegrationTests()
    {
        _marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina(seed: 7));
        _marina.SetViewportSize(1200, 800);
        // A fixed seed so a generated boat or contract number is the same on every run.
        _erp = new SampleErpIntegration(_marina, new Random(7), _log.Add);
    }

    public void Dispose()
    {
        _erp.Dispose();
        GC.SuppressFinalize(this);
    }

    private Berth FirstBerthWith(BerthStatus status, bool onLand = false) =>
        _marina.GetBerths().First(berth =>
            berth.Status == status && berth.IsOnLand == onLand && !berth.IsReadOnly && berth.MultiBerthId is null);

    /// <summary>The action the handler offered under this id, or null when it offered none.</summary>
    private static BerthAction? Find(BerthPopup popup, string actionId) =>
        popup.Actions.FirstOrDefault(action => action.ActionId == actionId);

    private BerthPopup SelectAndOpenActions(string berthId)
    {
        Assert.True(_marina.SelectBerth(berthId));
        Assert.True(_marina.ShowActions());
        return Assert.IsType<BerthPopup>(_marina.ActivePopup);
    }

    // ---- The tooltip the handler fills in -----------------------------------------------------

    [Fact]
    public void SelectingAnOccupiedBerth_AddsTheContractLinesAndRemembersTheContract()
    {
        var berth = FirstBerthWith(BerthStatus.Occupied);

        Assert.True(_marina.SelectBerth(berth.Id));

        var contract = _marina.GetBerth(berth.Id)!.ExternalData.Get<SampleContract>(SampleErpIntegration.ContractKey);
        Assert.NotNull(contract);
        Assert.False(string.IsNullOrWhiteSpace(contract.Number));

        // Selecting again reuses the contract rather than "loading" a second one.
        _marina.SelectBerth(berth.Id);
        Assert.Same(contract, _marina.GetBerth(berth.Id)!.ExternalData.Get<SampleContract>(SampleErpIntegration.ContractKey));
    }

    [Fact]
    public void SelectingAReservedBerth_DoesNotLoadAContract()
    {
        var berth = FirstBerthWith(BerthStatus.Reserved);

        _marina.SelectBerth(berth.Id);

        Assert.Null(_marina.GetBerth(berth.Id)!.ExternalData.Get<SampleContract>(SampleErpIntegration.ContractKey));
    }

    [Fact]
    public void TheActionsOfferedFollowTheBerthStatus()
    {
        var free = SelectAndOpenActions(FirstBerthWith(BerthStatus.Free).Id);
        Assert.NotNull(Find(free, "checkin"));
        Assert.NotNull(Find(free, "reserve"));
        Assert.Null(Find(free, "checkout"));

        var occupied = SelectAndOpenActions(FirstBerthWith(BerthStatus.Occupied).Id);
        Assert.NotNull(Find(occupied, "checkout"));
        Assert.NotNull(Find(occupied, "tempfree"));
        Assert.Null(Find(occupied, "checkin"));

        var reserved = SelectAndOpenActions(FirstBerthWith(BerthStatus.Reserved).Id);
        Assert.NotNull(Find(reserved, "arrive"));
        Assert.NotNull(Find(reserved, "cancel"));
    }

    [Fact]
    public void TheContractAction_IsDisabledWhenNoBoatIsAssigned()
    {
        var popup = SelectAndOpenActions(FirstBerthWith(BerthStatus.Free).Id);

        var contract = Find(popup, "contract");

        Assert.NotNull(contract);
        Assert.False(contract.Enabled);
        Assert.False(string.IsNullOrWhiteSpace(contract.Description));
    }

    // ---- Invoking the actions ----------------------------------------------------------------

    [Fact]
    public void CheckIn_PutsABoatInAFreeBerth()
    {
        var berth = FirstBerthWith(BerthStatus.Free);
        SelectAndOpenActions(berth.Id);

        Assert.True(_marina.InvokeBerthAction("checkin"));

        var after = _marina.GetBerth(berth.Id)!;
        Assert.Equal(BerthStatus.Occupied, after.Status);
        Assert.NotNull(after.Boat);
    }

    [Fact]
    public void Reserve_MarksTheBerthReservedWithAnExpectedArrival()
    {
        var berth = FirstBerthWith(BerthStatus.Free);
        SelectAndOpenActions(berth.Id);

        Assert.True(_marina.InvokeBerthAction("reserve"));

        var after = _marina.GetBerth(berth.Id)!;
        Assert.Equal(BerthStatus.Reserved, after.Status);
        Assert.NotNull(after.Boat?.ExpectedArrival);
    }

    [Fact]
    public void Checkout_FreesTheBerthAndForgetsTheContract()
    {
        var berth = FirstBerthWith(BerthStatus.Occupied);
        _marina.SelectBerth(berth.Id);
        Assert.NotNull(_marina.GetBerth(berth.Id)!.ExternalData.Get<SampleContract>(SampleErpIntegration.ContractKey));

        SelectAndOpenActions(berth.Id);
        Assert.True(_marina.InvokeBerthAction("checkout"));

        var after = _marina.GetBerth(berth.Id)!;
        Assert.Equal(BerthStatus.Free, after.Status);
        Assert.Null(after.Boat);
        Assert.Null(after.ExternalData.Get<SampleContract>(SampleErpIntegration.ContractKey));
    }

    [Fact]
    public void TemporarilyFree_KeepsTheBoatButMarksTheOwnerAway()
    {
        var berth = FirstBerthWith(BerthStatus.Occupied);
        SelectAndOpenActions(berth.Id);

        Assert.True(_marina.InvokeBerthAction("tempfree"));

        var after = _marina.GetBerth(berth.Id)!;
        Assert.Equal(BerthStatus.TemporarilyFree, after.Status);
        Assert.NotNull(after.Boat);
    }

    [Fact]
    public void Arrive_TurnsAReservationIntoAnOccupiedBerth()
    {
        var berth = FirstBerthWith(BerthStatus.Reserved);
        SelectAndOpenActions(berth.Id);

        Assert.True(_marina.InvokeBerthAction("arrive"));

        var after = _marina.GetBerth(berth.Id)!;
        Assert.Equal(BerthStatus.Occupied, after.Status);
        Assert.Null(after.Boat?.ExpectedArrival);
    }

    [Fact]
    public void LockAndMaintenance_SetTheFlagsRatherThanChangingTheStatus()
    {
        var berth = FirstBerthWith(BerthStatus.Free);

        SelectAndOpenActions(berth.Id);
        Assert.True(_marina.InvokeBerthAction("readonly"));
        Assert.True(_marina.GetBerth(berth.Id)!.IsReadOnly);

        var another = FirstBerthWith(BerthStatus.Free);
        SelectAndOpenActions(another.Id);
        Assert.True(_marina.InvokeBerthAction("maintenance"));
        Assert.True(_marina.GetBerth(another.Id)!.IsDisabled);
    }

    [Fact]
    public void Hide_TakesTheBerthOutOfTheScene()
    {
        var berth = FirstBerthWith(BerthStatus.Free);
        SelectAndOpenActions(berth.Id);

        Assert.True(_marina.InvokeBerthAction("hide"));

        Assert.False(_marina.GetBerth(berth.Id)!.IsVisible);
    }

    [Fact]
    public void Contract_AndFocus_KeepThePopupOpen()
    {
        var berth = FirstBerthWith(BerthStatus.Occupied);
        SelectAndOpenActions(berth.Id);

        Assert.True(_marina.InvokeBerthAction("contract"));
        Assert.NotNull(_marina.ActivePopup);

        Assert.True(_marina.InvokeBerthAction("focus"));
        Assert.NotNull(_marina.ActivePopup);
    }

    [Fact]
    public void AnActionIdNobodyOffered_IsRefused()
    {
        SelectAndOpenActions(FirstBerthWith(BerthStatus.Free).Id);

        Assert.False(_marina.InvokeBerthAction("not-an-action"));
    }

    // ---- Helpers the host buttons call directly ----------------------------------------------

    [Fact]
    public void MoorYachtAlongside_TakesTwoFreeBerthsOnOnePier()
    {
        var free = _marina.GetBerths()
            .Where(berth => berth.Status == BerthStatus.Free && berth.PierId is not null
                && !berth.IsReadOnly && berth.MultiBerthId is null)
            .GroupBy(berth => berth.PierId!, StringComparer.OrdinalIgnoreCase)
            .First(group => group.Count() >= 2)
            .Take(2).ToArray();

        var multi = _erp.MoorYachtAlongside(free.Select(berth => berth.Id).ToArray());

        Assert.NotNull(multi);
        Assert.Equal(2, multi.BerthIds.Count);
        Assert.All(free, berth => Assert.NotNull(_marina.GetBerth(berth.Id)!.MultiBerthId));
    }

    /// <summary>
    /// The helper itself does not check that the berths are free; it is the multi-selection action list that
    /// decides, by offering "moor alongside" disabled unless every selected berth is free and on one pier. Worth
    /// pinning down, because it says where a new caller has to do the checking.
    /// </summary>
    [Fact]
    public void MoorYachtAlongside_LeavesTheFreeBerthCheckToTheActionList()
    {
        var occupied = _marina.GetBerths().Where(berth => berth.Status == BerthStatus.Occupied).Take(2)
            .Select(berth => berth.Id).ToArray();

        Assert.NotNull(_erp.MoorYachtAlongside(occupied));

        // The action a user would click is the thing that says no.
        Assert.Equal(2, _marina.SetSelection(occupied).Count);
        Assert.True(_marina.ShowActions());
        var moor = Find(Assert.IsType<BerthPopup>(_marina.ActivePopup), "moor-alongside");
        Assert.NotNull(moor);
        Assert.False(moor.Enabled);
        Assert.False(string.IsNullOrWhiteSpace(moor.Description));
    }

    [Fact]
    public void MoorYachtAlongside_ReturnsNullWhenFewerThanTwoBerthsExist()
    {
        Assert.Null(_erp.MoorYachtAlongside(new[] { "no-such-berth", "nor-this-one" }));
    }

    [Fact]
    public void MoorYachtAlongside_RefusesASingleBerth()
    {
        var one = FirstBerthWith(BerthStatus.Free);

        Assert.Null(_erp.MoorYachtAlongside(new[] { one.Id }));
    }

    [Fact]
    public void ResetAllFlags_ClearsTheLocksAndHiding()
    {
        var berth = FirstBerthWith(BerthStatus.Free);
        _marina.SetBerthFlags(new[] { berth.Id }, readOnly: true, disabled: true);
        _marina.SetBerthVisible(berth.Id, false);

        _erp.ResetAllFlags();

        var after = _marina.GetBerth(berth.Id)!;
        Assert.False(after.IsReadOnly);
        Assert.False(after.IsDisabled);
        Assert.True(after.IsVisible);
    }

    [Fact]
    public void Dispose_UnhooksTheHandlers_SoNothingIsAddedAfterwards()
    {
        var berth = FirstBerthWith(BerthStatus.Occupied);

        _erp.Dispose();
        _log.Clear();
        _marina.SelectBerth(berth.Id);

        Assert.Empty(_log);
        Assert.Null(_marina.GetBerth(berth.Id)!.ExternalData.Get<SampleContract>(SampleErpIntegration.ContractKey));
    }

    [Fact]
    public void TheIntegrationRefusesToBeBuiltWithoutAMarinaOrARandom()
    {
        Assert.Throws<ArgumentNullException>(() => new SampleErpIntegration(null!, new Random(1), _ => { }));
        Assert.Throws<ArgumentNullException>(() => new SampleErpIntegration(_marina, null!, _ => { }));

        // A null log is allowed: it stands in for "do not log".
        using var quiet = new SampleErpIntegration(new MarinaVisualizer(), new Random(1), null!);
        Assert.NotNull(quiet);
    }

    [Fact]
    public void EveryHandlerWritesALine_SoAHostCanShowWhatHappened()
    {
        var berth = FirstBerthWith(BerthStatus.Free);
        _log.Clear();

        SelectAndOpenActions(berth.Id);
        _marina.InvokeBerthAction("checkin");

        Assert.Contains(_log, line => line.StartsWith("BerthSelected", StringComparison.Ordinal));
        Assert.Contains(_log, line => line.StartsWith("ActionInvoked", StringComparison.Ordinal));
    }
}
