using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Input;
using VirtualMarina.SampleData;

namespace VirtualMarina.Core.Tests;

public class InteractionAndBerthTests
{
    private static MarinaVisualizer CreateMarina()
    {
        var layout = new MarinaLayoutBuilder("Test")
            .AddPier("A", "Pier A", Vector2.Zero, 0f, 40f, pier => pier
                .AddBerths(PierSide.Left, 3, 5f, 12f)
                .AddBerths(PierSide.Right, 3, 5f, 12f))
            .Build();
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(layout);
        marina.SetViewportSize(800, 600);
        return marina;
    }

    private static void LookAtBerth(MarinaVisualizer marina, string berthId)
    {
        var berth = marina.GetBerth(berthId)!;
        marina.Camera.SetPose(new CameraPose(new Vector3(berth.Center.X, 0, berth.Center.Y), 0f, 89f, 40f), immediate: true);
    }

    private static void Click(MarinaVisualizer marina, string berthId, PointerButton button = PointerButton.Left, InputModifiers modifiers = InputModifiers.None)
    {
        LookAtBerth(marina, berthId);
        marina.Input.PointerDown(400, 300, button, modifiers);
        marina.Input.PointerUp(400, 300, button, modifiers);
    }

    private static Boat Yacht() => new("Y1", "Meltemi", BoatType.MotorYacht) { LengthMeters = 14f, BeamMeters = 4f, OwnerName = "A. Owner" };

    // ---- Tooltip and actions ----------------------------------------------------------------------

    [Fact]
    public void LeftClick_ShowsDefaultTooltip_ExtendedByHandler()
    {
        var marina = CreateMarina();
        marina.AssignBoat("A-L02", Yacht());
        BerthSelectedEventArgs? selected = null;
        marina.BerthSelected += (_, e) =>
        {
            selected = e;
            e.Tooltip.AddLine("Contract", "CT-1");
        };

        Click(marina, "A-L02");

        Assert.NotNull(selected);
        Assert.Equal(SelectionReason.Pointer, selected!.Reason);
        Assert.True(selected.IsNewSelection);
        var popup = Assert.IsType<BerthPopup>(marina.ActivePopup);
        Assert.Equal(BerthPopupKind.Tooltip, popup.Kind);
        Assert.Equal("A-L02", popup.Tooltip.Title);
        Assert.Contains(popup.Tooltip.Lines, l => l.Label == "Boat" && l.Value == "Meltemi");
        Assert.Contains(popup.Tooltip.Lines, l => l.Label == "Owner" && l.Value == "A. Owner");
        Assert.Contains(popup.Tooltip.Lines, l => l.Label == "Contract" && l.Value == "CT-1");
        Assert.Empty(popup.Actions);
    }

    [Fact]
    public void RightClick_ShowsActions_AndInvokingRaisesEventWithBerthAndActionId()
    {
        var marina = CreateMarina();
        marina.BerthSelected += (_, e) =>
        {
            e.Actions.Add("checkin", "Check in");
            e.Actions.Add("disabled", "Not now", enabled: false);
            e.Actions.Add("hidden", "Hidden").Visible = false;
        };
        BerthActionInvokedEventArgs? invoked = null;
        marina.BerthActionInvoked += (_, e) => invoked = e;

        Click(marina, "A-R01", PointerButton.Right);

        var popup = marina.ActivePopup!;
        Assert.Equal(BerthPopupKind.Actions, popup.Kind);
        Assert.Equal(new[] { "checkin", "disabled" }, popup.Actions.Select(a => a.ActionId));
        Assert.False(marina.InvokeBerthAction("disabled"));
        Assert.False(marina.InvokeBerthAction("hidden"));

        Assert.True(marina.InvokeBerthAction("checkin"));
        Assert.Equal("checkin", invoked?.ActionId);
        Assert.Equal("A-R01", invoked?.BerthId);
        Assert.False(invoked!.IsMultiSelection);
        Assert.Null(marina.ActivePopup); // closes by default
    }

    [Fact]
    public void Escape_ClosesPopupFirst_ThenClearsSelection()
    {
        var marina = CreateMarina();
        Click(marina, "A-L01");
        Assert.NotNull(marina.ActivePopup);

        marina.Input.KeyDown(MarinaKey.Escape);
        Assert.Null(marina.ActivePopup);
        Assert.Equal("A-L01", marina.SelectedBerth?.Id);

        marina.Input.KeyDown(MarinaKey.Escape);
        Assert.Null(marina.SelectedBerth);
    }

    [Fact]
    public void PopupAnchor_ProjectsAboveTheBerth()
    {
        var marina = CreateMarina();
        Click(marina, "A-L03");

        Assert.True(marina.TryGetPopupAnchor(out var anchor));
        // Looking straight down at the berth: the anchor is near the view center.
        Assert.InRange(anchor.X, 300f, 500f);
        Assert.InRange(anchor.Y, 200f, 400f);
    }

    [Fact]
    public void StatusChangeOfSelectedBerth_RefreshesOpenPopup_WithoutLoopingWhenHandlerUpdatesBerth()
    {
        var marina = CreateMarina();
        var reasons = new List<SelectionReason>();
        marina.BerthSelected += (_, e) =>
        {
            reasons.Add(e.Reason);
            e.Actions.Add(e.Status == BerthStatus.Free ? "checkin" : "checkout", "x");
            // Writing back from the handler must not trigger another refresh.
            marina.UpdateBerth(new BerthUpdate(e.BerthId) { Label = $"seen-{reasons.Count}" });
        };

        Click(marina, "A-L01", PointerButton.Right);
        marina.AssignBoat("A-L01", Yacht());

        Assert.Equal(new[] { SelectionReason.Pointer, SelectionReason.Refresh }, reasons);
        Assert.Equal("checkout", marina.ActivePopup!.Actions.Single().ActionId);
    }

    // ---- Visible / Disabled / Read-only --------------------------------------------------------------

    [Fact]
    public void ReadOnlyBerth_ShowsTooltipButNotActions()
    {
        var marina = CreateMarina();
        marina.SetBerthReadOnly("A-L02", true);
        marina.BerthSelected += (_, e) => e.Actions.Add("checkin", "Check in");

        Click(marina, "A-L02", PointerButton.Right);

        Assert.Equal("A-L02", marina.SelectedBerth?.Id);
        Assert.Equal(BerthPopupKind.Tooltip, marina.ActivePopup?.Kind);
        Assert.False(marina.InvokeBerthAction("checkin"));
        Assert.False(marina.ShowActions() && marina.ActivePopup?.Kind == BerthPopupKind.Actions);
    }

    [Fact]
    public void DisabledBerth_IsInert_AndDrawnDesaturated()
    {
        var marina = CreateMarina();
        marina.AssignBoat("A-R02", Yacht());
        Assert.True(marina.SelectBerth("A-R02"));

        marina.SetBerthDisabled("A-R02", true);
        Assert.Null(marina.SelectedBerth); // disabling removes it from the selection
        Assert.False(marina.SelectBerth("A-R02"));

        var events = 0;
        marina.BerthClicked += (_, _) => events++;
        marina.BerthSelected += (_, _) => events++;
        Click(marina, "A-R02");
        Click(marina, "A-R02", PointerButton.Right);
        LookAtBerth(marina, "A-R02");
        marina.Input.PointerMove(400, 300);

        Assert.Equal(0, events);
        Assert.Null(marina.SelectedBerth);
        Assert.Null(marina.HoveredBerth);
        Assert.Null(marina.ActivePopup);

        var objects = marina.BuildRenderFrame().Objects;
        Assert.Contains(objects, o => o.MeshId == MeshIds.ForBoat(BoatType.MotorYacht) && o.Desaturation == 1f);
    }

    [Fact]
    public void HiddenBerth_IsNotDrawnOrPickable()
    {
        var marina = CreateMarina();
        var before = marina.BuildRenderFrame().Objects.Count(o => o.MeshId == MeshIds.BerthPad);

        marina.SetBerthVisible("A-L01", false);
        var after = marina.BuildRenderFrame().Objects.Count(o => o.MeshId == MeshIds.BerthPad);
        LookAtBerth(marina, "A-L01");

        Assert.Equal(before - 1, after);
        Assert.False(marina.IsBerthVisible("A-L01"));
        Assert.Null(marina.HitTest(400, 300));
        Assert.False(marina.SelectBerth("A-L01"));
    }

    // ---- Multi-select ----------------------------------------------------------------------------------

    [Fact]
    public void CtrlClick_BuildsMultiSelection_AndRaisesMultiSelectEvents()
    {
        var marina = CreateMarina();
        MultiBerthSelectedEventArgs? multi = null;
        marina.MultiBerthSelected += (_, e) =>
        {
            multi = e;
            e.Actions.Add("free-all", "Free all");
        };
        BerthActionInvokedEventArgs? invoked = null;
        marina.BerthActionInvoked += (_, e) => invoked = e;

        Click(marina, "A-L01");
        Click(marina, "A-L02", modifiers: InputModifiers.Control);

        Assert.Equal(new[] { "A-L01", "A-L02" }, multi?.BerthIds);
        Assert.Equal("A-L02", multi!.PrimaryBerth.Id);
        Assert.Equal("2 berths selected", marina.ActivePopup?.Tooltip.Title);

        // Right-click inside the selection keeps it and opens the multi-selection actions.
        Click(marina, "A-L01", PointerButton.Right);
        Assert.Equal(2, marina.SelectedBerths.Count);
        Assert.Equal("A-L01", marina.SelectedBerth?.Id);
        Assert.Equal(BerthPopupKind.Actions, marina.ActivePopup?.Kind);
        Assert.True(marina.InvokeBerthAction("free-all"));
        Assert.True(invoked!.IsMultiSelection);
        Assert.Equal(2, invoked.Berths.Count);

        // Ctrl+click toggles a berth back out.
        Click(marina, "A-L02", modifiers: InputModifiers.Control);
        Assert.Equal(new[] { "A-L01" }, marina.SelectedBerths.Select(s => s.Id));

        // A plain click replaces the selection.
        Click(marina, "A-R03");
        Assert.Equal(new[] { "A-R03" }, marina.SelectedBerths.Select(s => s.Id));
    }

    [Fact]
    public void ShiftClick_BuildsMultiSelection_LikeCtrlClick()
    {
        var marina = CreateMarina();

        Click(marina, "A-L01");
        Click(marina, "A-L02", modifiers: InputModifiers.Shift);
        Click(marina, "A-R01", modifiers: InputModifiers.Control);   // both modifiers can be mixed
        Assert.Equal(new[] { "A-L01", "A-L02", "A-R01" }, marina.SelectedBerths.Select(s => s.Id));

        Click(marina, "A-L02", modifiers: InputModifiers.Shift);      // Shift+click toggles a berth back out
        Assert.Equal(new[] { "A-L01", "A-R01" }, marina.SelectedBerths.Select(s => s.Id));

        marina.MultiSelectEnabled = false;
        Click(marina, "A-L03", modifiers: InputModifiers.Shift);      // disabled: a plain selection
        Assert.Equal(new[] { "A-L03" }, marina.SelectedBerths.Select(s => s.Id));
    }

    [Fact]
    public void ShiftDrag_StillOrbits_WithoutChangingTheSelection()
    {
        var marina = CreateMarina();
        Click(marina, "A-L01");
        var yawBefore = marina.Camera.DesiredPose.YawDegrees;

        marina.Input.PointerDown(400, 300, PointerButton.Left, InputModifiers.Shift);
        marina.Input.PointerMove(500, 300, InputModifiers.Shift);
        marina.Input.PointerUp(500, 300, PointerButton.Left, InputModifiers.Shift);

        Assert.NotEqual(yawBefore, marina.Camera.DesiredPose.YawDegrees);
        Assert.Equal(new[] { "A-L01" }, marina.SelectedBerths.Select(s => s.Id));
    }

    // ---- Temporarily free --------------------------------------------------------------------------------

    [Fact]
    public void TemporarilyFree_KeepsBoat_CountsInStatistics_AndDrawsGhost()
    {
        var marina = CreateMarina();
        marina.AssignBoat("A-L01", Yacht());

        var berth = marina.MarkTemporarilyFree("A-L01");

        Assert.Equal(BerthStatus.TemporarilyFree, berth.Status);
        Assert.Equal("Meltemi", berth.Boat?.Name);
        Assert.Equal(1, marina.GetStatistics().TemporarilyFree);
        var boat = marina.BuildRenderFrame().Objects.Single(o => o.MeshId == MeshIds.ForBoat(BoatType.MotorYacht));
        Assert.True(boat.IsTransparent);

        marina.SetStatusFilter(BerthStatusFilter.All & ~BerthStatusFilter.TemporarilyFree);
        Assert.False(marina.IsBerthVisible("A-L01"));
    }

    // ---- Multi-berths --------------------------------------------------------------------------------

    [Fact]
    public void MoorAlongside_SharesBoatAcrossBerths_AndDrawsItOnce()
    {
        var marina = CreateMarina();
        var statusEvents = 0;
        marina.BerthStatusChanged += (_, _) => statusEvents++;

        var group = marina.MoorAlongside(new[] { "a-l01", "A-L02", "A-L03" }, Yacht());

        Assert.Equal(new[] { "A-L01", "A-L02", "A-L03" }, group.BerthIds); // canonical ids
        Assert.Equal(3, statusEvents);
        Assert.All(group.BerthIds, id =>
        {
            var berth = marina.GetBerth(id)!;
            Assert.Equal(BerthStatus.Occupied, berth.Status);
            Assert.Equal(group.Id, berth.MultiBerthId);
            Assert.Equal("Meltemi", berth.Boat?.Name);
        });
        Assert.Same(group, marina.GetMultiBerthFor("A-L02"));
        Assert.Single(marina.BuildRenderFrame().Objects, o => o.MeshId == MeshIds.ForBoat(BoatType.MotorYacht));

        // Alongside: the boat's bow runs along the pier (perpendicular to the berths' heading).
        var world = marina.BuildRenderFrame().Objects.Single(o => o.MeshId == MeshIds.ForBoat(BoatType.MotorYacht)).World;
        var bow = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, world));
        Assert.True(MathF.Abs(bow.Z) > 0.99f, $"expected the bow along the pier (Z), got {bow}");

        Assert.Throws<InvalidOperationException>(() => marina.MoorAlongside(new[] { "A-L03", "A-R01" }, Yacht()));
    }

    [Fact]
    public void MoorAlongside_HasNoUpperLimitOnBerthCount()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(new MarinaLayoutBuilder()
            .AddPier("Q", "Quay", Vector2.Zero, 0f, 80f, pier => pier.AddBerths(PierSide.Right, 12, 4f, 10f))
            .Build());
        var ids = marina.GetBerths().Select(s => s.Id).ToArray();
        var superyacht = new Boat("SY", "Long One", BoatType.MotorYacht) { LengthMeters = 46f, BeamMeters = 8.5f };

        var berth = marina.MoorAlongside(ids, superyacht);

        Assert.Equal(12, berth.BerthIds.Count);
        Assert.All(ids, id => Assert.Equal(berth.Id, marina.GetBerth(id)!.MultiBerthId));
        Assert.Single(marina.BuildRenderFrame().Objects, o => o.MeshId == MeshIds.ForBoat(BoatType.MotorYacht));
    }

    [Fact]
    public void SampleMoorAlongsideAction_IsEnabledForManyBerths()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(new MarinaLayoutBuilder()
            .AddPier("Q", "Quay", Vector2.Zero, 0f, 80f, pier => pier.AddBerths(PierSide.Right, 8, 4f, 10f))
            .Build());
        using var erp = new SampleErpIntegration(marina, new Random(1), _ => { });
        BerthAction? moor = null;
        marina.MultiBerthSelected += (_, e) => moor = e.Actions.Find("moor-alongside");

        marina.SelectBerths(marina.GetBerths().Select(s => s.Id));
        Assert.True(moor?.Enabled, "8 free berths on one pier must allow mooring alongside");

        marina.ShowActions();
        Assert.True(marina.InvokeBerthAction("moor-alongside"));
        Assert.Equal(8, marina.GetMultiBerths().Single().BerthIds.Count);
    }

    [Fact]
    public void SingleBerthApi_OnBerthMember_ChangesOrReleasesWholeBerth()
    {
        var marina = CreateMarina();
        var berth = marina.MoorAlongside(new[] { "A-L01", "A-L02" }, Yacht());

        marina.MarkTemporarilyFree("A-L02");
        Assert.All(berth.BerthIds, id => Assert.Equal(BerthStatus.TemporarilyFree, marina.GetBerth(id)!.Status));
        Assert.Equal(BerthStatus.TemporarilyFree, marina.GetMultiBerth(berth.Id)!.Status);

        marina.ReleaseBerth("A-L01");
        Assert.Null(marina.GetMultiBerth(berth.Id));
        Assert.All(berth.BerthIds, id =>
        {
            Assert.Equal(BerthStatus.Free, marina.GetBerth(id)!.Status);
            Assert.Null(marina.GetBerth(id)!.MultiBerthId);
        });
    }

    [Fact]
    public void UpdateMultiBerth_ChangesBerthsAndBoat_AndRemovingMembersDissolvesIt()
    {
        var marina = CreateMarina();
        var berth = marina.AssignBoatToBerths(new[] { "A-R01", "A-R02" }, Yacht(), BerthStatus.Reserved, MooringStyle.BowIn);

        var updated = marina.UpdateMultiBerth(berth.Id, berthIds: new[] { "A-R02", "A-R03" }, status: BerthStatus.Occupied);
        Assert.Equal(BerthStatus.Free, marina.GetBerth("A-R01")!.Status);
        Assert.Equal(BerthStatus.Occupied, marina.GetBerth("A-R03")!.Status);
        Assert.Equal(MooringStyle.BowIn, updated.Style);

        marina.RemoveBerth("A-R03");
        Assert.Null(marina.GetMultiBerth(berth.Id));
        var remaining = marina.GetBerth("A-R02")!;
        Assert.Null(remaining.MultiBerthId);
        Assert.Equal("Meltemi", remaining.Boat?.Name); // keeps the boat as a normal assignment
    }

    [Fact]
    public void Berths_SurviveLayoutRoundTrip()
    {
        var marina = CreateMarina();
        marina.MoorAlongside(new[] { "A-L01", "A-L02" }, Yacht(), multiBerthId: "BIG");

        var copy = new MarinaVisualizer();
        copy.InitializeLayout(marina.GetLayout());

        Assert.Equal(new[] { "A-L01", "A-L02" }, copy.GetMultiBerth("BIG")?.BerthIds);
        Assert.Equal("BIG", copy.GetBerth("A-L02")?.MultiBerthId);
    }

    [Fact]
    public void BoatClickOnBerth_ResolvesToAMemberBerth()
    {
        var marina = CreateMarina();
        marina.MoorAlongside(new[] { "A-L01", "A-L02", "A-L03" }, Yacht());
        var berth = marina.GetBerth("A-L02")!;
        // Low camera looking along the pier, so the ray hits the yacht's hull before any water.
        marina.Camera.SetPose(new CameraPose(new Vector3(berth.Center.X + berth.Length * 0.25f, 2, berth.Center.Y), 90f, 12f, 30f), immediate: true);

        var hit = marina.HitTest(400, 300);

        Assert.True(hit?.HitBoat);
        Assert.StartsWith("A-L0", hit!.Value.BerthId);
    }

    // ---- External data --------------------------------------------------------------------------------

    [Fact]
    public void ExternalData_WrittenInEventHandler_PersistsAcrossUpdates()
    {
        var marina = CreateMarina();
        marina.BerthSelected += (_, e) => e.ExternalData["ContractId"] = 4711;

        Click(marina, "A-L01");
        marina.AssignBoat("A-L01", Yacht());
        marina.UpdateBerth(new Berth("A-L01", "A", new Vector2(-10, 20), 90, 12, 5)); // brand-new snapshot object
        marina.UpdateBerth(new BerthUpdate("A-L01") { ExternalData = new Dictionary<string, object?> { ["Note"] = "VIP" } });

        var data = marina.GetBerth("A-L01")!.ExternalData;
        Assert.Equal(4711, data.Get<int>("ContractId"));
        Assert.Equal("VIP", data.Get<string>("Note"));
    }

    // ---- Piers and dividers -------------------------------------------------------------------------------

    [Fact]
    public void Pier_FromCenter_AndPierUpdate_KeepCenter()
    {
        var pier = Pier.FromCenter("X", "X", new Vector2(10, 20), length: 40, width: 3, headingDegrees: 90, PierType.Concrete);

        Assert.Equal(10f, pier.Center.X, 3);
        Assert.Equal(20f, pier.Center.Y, 3);
        Assert.Equal(1.1f, pier.DeckHeight);

        var marina = new MarinaVisualizer();
        marina.AddPier(pier);
        var updated = marina.UpdatePier(new PierUpdate("X") { Length = 60, HeadingDegrees = 0, Type = PierType.FloatingConcrete });

        Assert.Equal(10f, updated.Center.X, 3);
        Assert.Equal(20f, updated.Center.Y, 3);
        Assert.Equal(60f, updated.Length);
        Assert.Equal(Pier.GetDefaultDeckHeight(PierType.FloatingConcrete), updated.DeckHeight);
    }

    [Fact]
    public void PierTypes_ProduceDifferentGeometry()
    {
        IReadOnlyList<Rendering.RenderObject> Objects(PierType type)
        {
            var marina = new MarinaVisualizer();
            marina.AddPier(new Pier("D", "D", Vector2.Zero, 0, 30, 3, type));
            return marina.BuildRenderFrame().Objects;
        }

        var wooden = Objects(PierType.FloatingWooden);
        var floatingConcrete = Objects(PierType.FloatingConcrete);
        var concrete = Objects(PierType.Concrete);

        // The deck is the first object; each type has its own material.
        Assert.Equal(3, new[] { wooden[0].Tint, floatingConcrete[0].Tint, concrete[0].Tint }.Distinct().Count());
        // No pier type draws standalone piles; the floating concrete pontoon has cleats, the fixed pier bollards.
        Assert.All(new[] { wooden, floatingConcrete, concrete }, objects => Assert.DoesNotContain(objects, o => o.MeshId == MeshIds.Piling));
        Assert.Contains(floatingConcrete, o => o.MeshId == MeshIds.Cylinder);
        Assert.Contains(concrete, o => o.MeshId == MeshIds.Cylinder);
        // Floating piers reach below the water line; the fixed pier's deck is higher.
        Assert.True(concrete[0].World.M42 > wooden[0].World.M42);
    }

    [Fact]
    public void Builder_GeneratesDividersAtBerthBoundaries_AndRemovePierRemovesThem()
    {
        var layout = new MarinaLayoutBuilder()
            .AddPier("A", "A", Vector2.Zero, 0, 40, pier => pier
                .AddBerths(PierSide.Right, 3, 5, 10, dividers: DividerType.Piles)
                .AddBerths(PierSide.Right, 2, 5, 10, dividers: DividerType.Piles))
            .Build();

        Assert.Empty(layout.Validate());
        Assert.Equal(6, layout.Dividers.Count); // 5 berths in a row share boundaries
        Assert.All(layout.Berths, s => Assert.False(s.HasFingerPiers));

        var marina = new MarinaVisualizer();
        marina.InitializeLayout(layout);
        marina.AddDivider(Divider.FromCenter("boom", new Vector2(20, 5), 10, 90, DividerType.Boom) with { PierId = "A" });
        Assert.Equal(7, marina.GetDividersByPier("A").Count);

        marina.RemovePier("A");
        Assert.Empty(marina.GetDividers());
    }

    [Fact]
    public void SampleMarina_ShowcasesNewFeatures()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());

        Assert.Equal(3, marina.GetPiers().Select(d => d.Type).Distinct().Count());
        Assert.NotEmpty(marina.GetDividers());
        Assert.Equal(3, marina.GetMultiBerth(MockMarinaFactory.SampleMultiBerthId)?.BerthIds.Count);
        Assert.Contains(marina.GetBerths(), s => s.IsDisabled);
        Assert.Contains(marina.GetBerths(), s => s.IsReadOnly);
        Assert.Contains(marina.GetBerths(), s => !s.IsVisible);
        Assert.True(marina.GetStatistics().TemporarilyFree > 0);
    }
}
