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
            .AddDock("A", "Dock A", Vector2.Zero, 0f, 40f, dock => dock
                .AddSlips(DockSide.Left, 3, 5f, 12f)
                .AddSlips(DockSide.Right, 3, 5f, 12f))
            .Build();
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(layout);
        marina.SetViewportSize(800, 600);
        return marina;
    }

    private static void LookAtSlip(MarinaVisualizer marina, string slipId)
    {
        var slip = marina.GetSlip(slipId)!;
        marina.Camera.SetPose(new CameraPose(new Vector3(slip.Center.X, 0, slip.Center.Y), 0f, 89f, 40f), immediate: true);
    }

    private static void Click(MarinaVisualizer marina, string slipId, PointerButton button = PointerButton.Left, InputModifiers modifiers = InputModifiers.None)
    {
        LookAtSlip(marina, slipId);
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
        SlipSelectedEventArgs? selected = null;
        marina.SlipSelected += (_, e) =>
        {
            selected = e;
            e.Tooltip.AddLine("Contract", "CT-1");
        };

        Click(marina, "A-L02");

        Assert.NotNull(selected);
        Assert.Equal(SelectionReason.Pointer, selected!.Reason);
        Assert.True(selected.IsNewSelection);
        var popup = Assert.IsType<SlipPopup>(marina.ActivePopup);
        Assert.Equal(SlipPopupKind.Tooltip, popup.Kind);
        Assert.Equal("A-L02", popup.Tooltip.Title);
        Assert.Contains(popup.Tooltip.Lines, l => l.Label == "Boat" && l.Value == "Meltemi");
        Assert.Contains(popup.Tooltip.Lines, l => l.Label == "Owner" && l.Value == "A. Owner");
        Assert.Contains(popup.Tooltip.Lines, l => l.Label == "Contract" && l.Value == "CT-1");
        Assert.Empty(popup.Actions);
    }

    [Fact]
    public void RightClick_ShowsActions_AndInvokingRaisesEventWithSlipAndActionId()
    {
        var marina = CreateMarina();
        marina.SlipSelected += (_, e) =>
        {
            e.Actions.Add("checkin", "Check in");
            e.Actions.Add("disabled", "Not now", enabled: false);
            e.Actions.Add("hidden", "Hidden").Visible = false;
        };
        SlipActionInvokedEventArgs? invoked = null;
        marina.SlipActionInvoked += (_, e) => invoked = e;

        Click(marina, "A-R01", PointerButton.Right);

        var popup = marina.ActivePopup!;
        Assert.Equal(SlipPopupKind.Actions, popup.Kind);
        Assert.Equal(new[] { "checkin", "disabled" }, popup.Actions.Select(a => a.ActionId));
        Assert.False(marina.InvokeSlipAction("disabled"));
        Assert.False(marina.InvokeSlipAction("hidden"));

        Assert.True(marina.InvokeSlipAction("checkin"));
        Assert.Equal("checkin", invoked?.ActionId);
        Assert.Equal("A-R01", invoked?.SlipId);
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
        Assert.Equal("A-L01", marina.SelectedSlip?.Id);

        marina.Input.KeyDown(MarinaKey.Escape);
        Assert.Null(marina.SelectedSlip);
    }

    [Fact]
    public void PopupAnchor_ProjectsAboveTheSlip()
    {
        var marina = CreateMarina();
        Click(marina, "A-L03");

        Assert.True(marina.TryGetPopupAnchor(out var anchor));
        // Looking straight down at the slip: the anchor is near the view center.
        Assert.InRange(anchor.X, 300f, 500f);
        Assert.InRange(anchor.Y, 200f, 400f);
    }

    [Fact]
    public void StatusChangeOfSelectedSlip_RefreshesOpenPopup_WithoutLoopingWhenHandlerUpdatesSlip()
    {
        var marina = CreateMarina();
        var reasons = new List<SelectionReason>();
        marina.SlipSelected += (_, e) =>
        {
            reasons.Add(e.Reason);
            e.Actions.Add(e.Status == SlipStatus.Free ? "checkin" : "checkout", "x");
            // Writing back from the handler must not trigger another refresh.
            marina.UpdateSlip(new SlipUpdate(e.SlipId) { Label = $"seen-{reasons.Count}" });
        };

        Click(marina, "A-L01", PointerButton.Right);
        marina.AssignBoat("A-L01", Yacht());

        Assert.Equal(new[] { SelectionReason.Pointer, SelectionReason.Refresh }, reasons);
        Assert.Equal("checkout", marina.ActivePopup!.Actions.Single().ActionId);
    }

    // ---- Visible / Disabled / Read-only --------------------------------------------------------------

    [Fact]
    public void ReadOnlySlip_ShowsTooltipButNotActions()
    {
        var marina = CreateMarina();
        marina.SetSlipReadOnly("A-L02", true);
        marina.SlipSelected += (_, e) => e.Actions.Add("checkin", "Check in");

        Click(marina, "A-L02", PointerButton.Right);

        Assert.Equal("A-L02", marina.SelectedSlip?.Id);
        Assert.Equal(SlipPopupKind.Tooltip, marina.ActivePopup?.Kind);
        Assert.False(marina.InvokeSlipAction("checkin"));
        Assert.False(marina.ShowActions() && marina.ActivePopup?.Kind == SlipPopupKind.Actions);
    }

    [Fact]
    public void DisabledSlip_IsInert_AndDrawnDesaturated()
    {
        var marina = CreateMarina();
        marina.AssignBoat("A-R02", Yacht());
        Assert.True(marina.SelectSlip("A-R02"));

        marina.SetSlipDisabled("A-R02", true);
        Assert.Null(marina.SelectedSlip); // disabling removes it from the selection
        Assert.False(marina.SelectSlip("A-R02"));

        var events = 0;
        marina.SlipClicked += (_, _) => events++;
        marina.SlipSelected += (_, _) => events++;
        Click(marina, "A-R02");
        Click(marina, "A-R02", PointerButton.Right);
        LookAtSlip(marina, "A-R02");
        marina.Input.PointerMove(400, 300);

        Assert.Equal(0, events);
        Assert.Null(marina.SelectedSlip);
        Assert.Null(marina.HoveredSlip);
        Assert.Null(marina.ActivePopup);

        var objects = marina.BuildRenderFrame().Objects;
        Assert.Contains(objects, o => o.MeshId == MeshIds.ForBoat(BoatType.MotorYacht) && o.Desaturation == 1f);
    }

    [Fact]
    public void HiddenSlip_IsNotDrawnOrPickable()
    {
        var marina = CreateMarina();
        var before = marina.BuildRenderFrame().Objects.Count(o => o.MeshId == MeshIds.SlipPad);

        marina.SetSlipVisible("A-L01", false);
        var after = marina.BuildRenderFrame().Objects.Count(o => o.MeshId == MeshIds.SlipPad);
        LookAtSlip(marina, "A-L01");

        Assert.Equal(before - 1, after);
        Assert.False(marina.IsSlipVisible("A-L01"));
        Assert.Null(marina.HitTest(400, 300));
        Assert.False(marina.SelectSlip("A-L01"));
    }

    // ---- Multi-select ----------------------------------------------------------------------------------

    [Fact]
    public void CtrlClick_BuildsMultiSelection_AndRaisesMultiSelectEvents()
    {
        var marina = CreateMarina();
        MultiSlipSelectedEventArgs? multi = null;
        marina.MultiSlipSelected += (_, e) =>
        {
            multi = e;
            e.Actions.Add("free-all", "Free all");
        };
        SlipActionInvokedEventArgs? invoked = null;
        marina.SlipActionInvoked += (_, e) => invoked = e;

        Click(marina, "A-L01");
        Click(marina, "A-L02", modifiers: InputModifiers.Control);

        Assert.Equal(new[] { "A-L01", "A-L02" }, multi?.SlipIds);
        Assert.Equal("A-L02", multi!.PrimarySlip.Id);
        Assert.Equal("2 slips selected", marina.ActivePopup?.Tooltip.Title);

        // Right-click inside the selection keeps it and opens the multi-selection actions.
        Click(marina, "A-L01", PointerButton.Right);
        Assert.Equal(2, marina.SelectedSlips.Count);
        Assert.Equal("A-L01", marina.SelectedSlip?.Id);
        Assert.Equal(SlipPopupKind.Actions, marina.ActivePopup?.Kind);
        Assert.True(marina.InvokeSlipAction("free-all"));
        Assert.True(invoked!.IsMultiSelection);
        Assert.Equal(2, invoked.Slips.Count);

        // Ctrl+click toggles a slip back out.
        Click(marina, "A-L02", modifiers: InputModifiers.Control);
        Assert.Equal(new[] { "A-L01" }, marina.SelectedSlips.Select(s => s.Id));

        // A plain click replaces the selection.
        Click(marina, "A-R03");
        Assert.Equal(new[] { "A-R03" }, marina.SelectedSlips.Select(s => s.Id));
    }

    // ---- Temporarily free --------------------------------------------------------------------------------

    [Fact]
    public void TemporarilyFree_KeepsBoat_CountsInStatistics_AndDrawsGhost()
    {
        var marina = CreateMarina();
        marina.AssignBoat("A-L01", Yacht());

        var slip = marina.MarkTemporarilyFree("A-L01");

        Assert.Equal(SlipStatus.TemporarilyFree, slip.Status);
        Assert.Equal("Meltemi", slip.Boat?.Name);
        Assert.Equal(1, marina.GetStatistics().TemporarilyFree);
        var boat = marina.BuildRenderFrame().Objects.Single(o => o.MeshId == MeshIds.ForBoat(BoatType.MotorYacht));
        Assert.True(boat.IsTransparent);

        marina.SetStatusFilter(SlipStatusFilter.All & ~SlipStatusFilter.TemporarilyFree);
        Assert.False(marina.IsSlipVisible("A-L01"));
    }

    // ---- Multi-slip berths --------------------------------------------------------------------------------

    [Fact]
    public void DockAlongside_SharesBoatAcrossSlips_AndDrawsItOnce()
    {
        var marina = CreateMarina();
        var statusEvents = 0;
        marina.SlipStatusChanged += (_, _) => statusEvents++;

        var berth = marina.DockAlongside(new[] { "a-l01", "A-L02", "A-L03" }, Yacht());

        Assert.Equal(new[] { "A-L01", "A-L02", "A-L03" }, berth.SlipIds); // canonical ids
        Assert.Equal(3, statusEvents);
        Assert.All(berth.SlipIds, id =>
        {
            var slip = marina.GetSlip(id)!;
            Assert.Equal(SlipStatus.Occupied, slip.Status);
            Assert.Equal(berth.Id, slip.BerthId);
            Assert.Equal("Meltemi", slip.Boat?.Name);
        });
        Assert.Same(berth, marina.GetMultiSlipBerthForSlip("A-L02"));
        Assert.Single(marina.BuildRenderFrame().Objects, o => o.MeshId == MeshIds.ForBoat(BoatType.MotorYacht));

        // Alongside: the boat's bow runs along the dock (perpendicular to the slips' heading).
        var world = marina.BuildRenderFrame().Objects.Single(o => o.MeshId == MeshIds.ForBoat(BoatType.MotorYacht)).World;
        var bow = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, world));
        Assert.True(MathF.Abs(bow.Z) > 0.99f, $"expected the bow along the dock (Z), got {bow}");

        Assert.Throws<InvalidOperationException>(() => marina.DockAlongside(new[] { "A-L03", "A-R01" }, Yacht()));
    }

    [Fact]
    public void DockAlongside_HasNoUpperLimitOnSlipCount()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(new MarinaLayoutBuilder()
            .AddDock("Q", "Quay", Vector2.Zero, 0f, 80f, dock => dock.AddSlips(DockSide.Right, 12, 4f, 10f))
            .Build());
        var ids = marina.GetSlips().Select(s => s.Id).ToArray();
        var superyacht = new Boat("SY", "Long One", BoatType.MotorYacht) { LengthMeters = 46f, BeamMeters = 8.5f };

        var berth = marina.DockAlongside(ids, superyacht);

        Assert.Equal(12, berth.SlipIds.Count);
        Assert.All(ids, id => Assert.Equal(berth.Id, marina.GetSlip(id)!.BerthId));
        Assert.Single(marina.BuildRenderFrame().Objects, o => o.MeshId == MeshIds.ForBoat(BoatType.MotorYacht));
    }

    [Fact]
    public void SampleMoorAlongsideAction_IsEnabledForManySlips()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(new MarinaLayoutBuilder()
            .AddDock("Q", "Quay", Vector2.Zero, 0f, 80f, dock => dock.AddSlips(DockSide.Right, 8, 4f, 10f))
            .Build());
        using var erp = new SampleErpIntegration(marina, new Random(1), _ => { });
        SlipAction? moor = null;
        marina.MultiSlipSelected += (_, e) => moor = e.Actions.Find("moor-alongside");

        marina.SelectSlips(marina.GetSlips().Select(s => s.Id));
        Assert.True(moor?.Enabled, "8 free slips on one dock must allow mooring alongside");

        marina.ShowActions();
        Assert.True(marina.InvokeSlipAction("moor-alongside"));
        Assert.Equal(8, marina.GetMultiSlipBerths().Single().SlipIds.Count);
    }

    [Fact]
    public void SingleSlipApi_OnBerthMember_ChangesOrReleasesWholeBerth()
    {
        var marina = CreateMarina();
        var berth = marina.DockAlongside(new[] { "A-L01", "A-L02" }, Yacht());

        marina.MarkTemporarilyFree("A-L02");
        Assert.All(berth.SlipIds, id => Assert.Equal(SlipStatus.TemporarilyFree, marina.GetSlip(id)!.Status));
        Assert.Equal(SlipStatus.TemporarilyFree, marina.GetMultiSlipBerth(berth.Id)!.Status);

        marina.ReleaseSlip("A-L01");
        Assert.Null(marina.GetMultiSlipBerth(berth.Id));
        Assert.All(berth.SlipIds, id =>
        {
            Assert.Equal(SlipStatus.Free, marina.GetSlip(id)!.Status);
            Assert.Null(marina.GetSlip(id)!.BerthId);
        });
    }

    [Fact]
    public void UpdateMultiSlipBerth_ChangesSlipsAndBoat_AndRemovingMembersDissolvesIt()
    {
        var marina = CreateMarina();
        var berth = marina.AssignBoatToSlips(new[] { "A-R01", "A-R02" }, Yacht(), SlipStatus.Reserved, MooringStyle.BowIn);

        var updated = marina.UpdateMultiSlipBerth(berth.Id, slipIds: new[] { "A-R02", "A-R03" }, status: SlipStatus.Occupied);
        Assert.Equal(SlipStatus.Free, marina.GetSlip("A-R01")!.Status);
        Assert.Equal(SlipStatus.Occupied, marina.GetSlip("A-R03")!.Status);
        Assert.Equal(MooringStyle.BowIn, updated.Style);

        marina.RemoveSlip("A-R03");
        Assert.Null(marina.GetMultiSlipBerth(berth.Id));
        var remaining = marina.GetSlip("A-R02")!;
        Assert.Null(remaining.BerthId);
        Assert.Equal("Meltemi", remaining.Boat?.Name); // keeps the boat as a normal assignment
    }

    [Fact]
    public void Berths_SurviveLayoutRoundTrip()
    {
        var marina = CreateMarina();
        marina.DockAlongside(new[] { "A-L01", "A-L02" }, Yacht(), berthId: "BIG");

        var copy = new MarinaVisualizer();
        copy.InitializeLayout(marina.GetLayout());

        Assert.Equal(new[] { "A-L01", "A-L02" }, copy.GetMultiSlipBerth("BIG")?.SlipIds);
        Assert.Equal("BIG", copy.GetSlip("A-L02")?.BerthId);
    }

    [Fact]
    public void BoatClickOnBerth_ResolvesToAMemberSlip()
    {
        var marina = CreateMarina();
        marina.DockAlongside(new[] { "A-L01", "A-L02", "A-L03" }, Yacht());
        var slip = marina.GetSlip("A-L02")!;
        // Low camera looking along the dock, so the ray hits the yacht's hull before any water.
        marina.Camera.SetPose(new CameraPose(new Vector3(slip.Center.X + slip.Length * 0.25f, 2, slip.Center.Y), 90f, 12f, 30f), immediate: true);

        var hit = marina.HitTest(400, 300);

        Assert.True(hit?.HitBoat);
        Assert.StartsWith("A-L0", hit!.Value.SlipId);
    }

    // ---- External data --------------------------------------------------------------------------------

    [Fact]
    public void ExternalData_WrittenInEventHandler_PersistsAcrossUpdates()
    {
        var marina = CreateMarina();
        marina.SlipSelected += (_, e) => e.ExternalData["ContractId"] = 4711;

        Click(marina, "A-L01");
        marina.AssignBoat("A-L01", Yacht());
        marina.UpdateSlip(new Slip("A-L01", "A", new Vector2(-10, 20), 90, 12, 5)); // brand-new snapshot object
        marina.UpdateSlip(new SlipUpdate("A-L01") { ExternalData = new Dictionary<string, object?> { ["Note"] = "VIP" } });

        var data = marina.GetSlip("A-L01")!.ExternalData;
        Assert.Equal(4711, data.Get<int>("ContractId"));
        Assert.Equal("VIP", data.Get<string>("Note"));
    }

    // ---- Docks and dividers -------------------------------------------------------------------------------

    [Fact]
    public void Dock_FromCenter_AndDockUpdate_KeepCenter()
    {
        var dock = Dock.FromCenter("X", "X", new Vector2(10, 20), length: 40, width: 3, headingDegrees: 90, DockType.Concrete);

        Assert.Equal(10f, dock.Center.X, 3);
        Assert.Equal(20f, dock.Center.Y, 3);
        Assert.Equal(1.1f, dock.DeckHeight);

        var marina = new MarinaVisualizer();
        marina.AddDock(dock);
        var updated = marina.UpdateDock(new DockUpdate("X") { Length = 60, HeadingDegrees = 0, Type = DockType.FloatingConcrete });

        Assert.Equal(10f, updated.Center.X, 3);
        Assert.Equal(20f, updated.Center.Y, 3);
        Assert.Equal(60f, updated.Length);
        Assert.Equal(Dock.GetDefaultDeckHeight(DockType.FloatingConcrete), updated.DeckHeight);
    }

    [Fact]
    public void DockTypes_ProduceDifferentGeometry()
    {
        IReadOnlyList<Rendering.RenderObject> Objects(DockType type)
        {
            var marina = new MarinaVisualizer();
            marina.AddDock(new Dock("D", "D", Vector2.Zero, 0, 30, 3, type));
            return marina.BuildRenderFrame().Objects;
        }

        var wooden = Objects(DockType.FloatingWooden);
        var floatingConcrete = Objects(DockType.FloatingConcrete);
        var concrete = Objects(DockType.Concrete);

        // The deck is the first object; each type has its own material.
        Assert.Equal(3, new[] { wooden[0].Tint, floatingConcrete[0].Tint, concrete[0].Tint }.Distinct().Count());
        // Wooden guide piles vs steel piles; the fixed pier stands on columns without guide piles.
        Assert.Contains(wooden, o => o.MeshId == MeshIds.Piling);
        Assert.DoesNotContain(floatingConcrete, o => o.MeshId == MeshIds.Piling);
        Assert.Contains(floatingConcrete, o => o.MeshId == MeshIds.Cylinder && o.World.M22 > 3f);
        Assert.DoesNotContain(concrete, o => o.MeshId == MeshIds.Piling);
        // Floating docks reach below the water line; the fixed pier's deck is higher.
        Assert.True(concrete[0].World.M42 > wooden[0].World.M42);
    }

    [Fact]
    public void Builder_GeneratesDividersAtSlipBoundaries_AndRemoveDockRemovesThem()
    {
        var layout = new MarinaLayoutBuilder()
            .AddDock("A", "A", Vector2.Zero, 0, 40, dock => dock
                .AddSlips(DockSide.Right, 3, 5, 10, dividers: DividerType.Piles)
                .AddSlips(DockSide.Right, 2, 5, 10, dividers: DividerType.Piles))
            .Build();

        Assert.Empty(layout.Validate());
        Assert.Equal(6, layout.Dividers.Count); // 5 slips in a row share boundaries
        Assert.All(layout.Slips, s => Assert.False(s.HasFingerPiers));

        var marina = new MarinaVisualizer();
        marina.InitializeLayout(layout);
        marina.AddDivider(Divider.FromCenter("boom", new Vector2(20, 5), 10, 90, DividerType.Boom) with { DockId = "A" });
        Assert.Equal(7, marina.GetDividersByDock("A").Count);

        marina.RemoveDock("A");
        Assert.Empty(marina.GetDividers());
    }

    [Fact]
    public void SampleMarina_ShowcasesNewFeatures()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());

        Assert.Equal(3, marina.GetDocks().Select(d => d.Type).Distinct().Count());
        Assert.NotEmpty(marina.GetDividers());
        Assert.Equal(3, marina.GetMultiSlipBerth(MockMarinaFactory.SampleBerthId)?.SlipIds.Count);
        Assert.Contains(marina.GetSlips(), s => s.IsDisabled);
        Assert.Contains(marina.GetSlips(), s => s.IsReadOnly);
        Assert.Contains(marina.GetSlips(), s => !s.IsVisible);
        Assert.True(marina.GetStatistics().TemporarilyFree > 0);
    }
}
