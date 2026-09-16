using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;
using VirtualMarina.SampleData;

namespace VirtualMarina.Core.Tests;

public class MarinaVisualizerTests
{
    private static MarinaLayout SmallLayout() =>
        new MarinaLayoutBuilder("Test")
            .AddDock("A", "Dock A", Vector2.Zero, 0f, 40f, dock => dock
                .AddSlips(DockSide.Left, 3, 5f, 12f)
                .AddSlips(DockSide.Right, 3, 5f, 12f))
            .Build();

    private static MarinaVisualizer CreateMarina()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(SmallLayout());
        marina.SetViewportSize(800, 600);
        return marina;
    }

    /// <summary>Points the camera straight down at a slip so the view center is over it.</summary>
    private static void LookAtSlip(MarinaVisualizer marina, string slipId)
    {
        var slip = marina.GetSlip(slipId)!;
        marina.Camera.SetPose(new CameraPose(new Vector3(slip.Center.X, 0, slip.Center.Y), 0f, 89f, 40f), immediate: true);
    }

    [Fact]
    public void InitializeLayout_RejectsDuplicateSlipIds()
    {
        var layout = SmallLayout();
        var broken = layout with { Slips = layout.Slips.Append(layout.Slips[0]).ToArray() };

        var ex = Assert.Throws<MarinaLayoutException>(() => new MarinaVisualizer().InitializeLayout(broken));
        Assert.Contains(ex.Errors, e => e.Contains("Duplicate slip id"));
    }

    [Fact]
    public void SetSlipStatus_RaisesEvent_AndFreeClearsBoat()
    {
        var marina = CreateMarina();
        var events = new List<SlipStatusChangedEventArgs>();
        marina.SlipStatusChanged += (_, e) => events.Add(e);
        var boat = new Boat("B1", "Aurora", BoatType.MotorYacht) { LengthMeters = 10, BeamMeters = 3 };

        marina.AssignBoat("A-L01", boat);
        var freed = marina.ReleaseSlip("A-L01");

        Assert.Equal(2, events.Count);
        Assert.Equal(SlipStatus.Free, events[0].OldStatus);
        Assert.Equal(SlipStatus.Occupied, events[0].NewStatus);
        Assert.Equal("Aurora", events[0].NewBoat?.Name);
        Assert.Equal(SlipStatus.Free, freed.Status);
        Assert.Null(freed.Boat);
    }

    [Fact]
    public void SetSlipStatus_ReservedToOccupied_KeepsExpectedBoat()
    {
        var marina = CreateMarina();
        marina.ReserveSlip("A-R02", new Boat("B2", "Zephyr", BoatType.JetSki));

        var arrived = marina.SetSlipStatus("A-R02", SlipStatus.Occupied);

        Assert.Equal("Zephyr", arrived.Boat?.Name);
    }

    [Fact]
    public void BatchUpdate_AppliesValidUpdates_AndReportsMissingSlips()
    {
        var marina = CreateMarina();
        var layoutEvents = 0;
        marina.LayoutChanged += (_, _) => layoutEvents++;

        var result = marina.BatchUpdate(new[]
        {
            SlipUpdate.Reserve("A-L01"),
            SlipUpdate.Occupy("A-L02", new Boat("B3", "Halcyon", BoatType.FishingBoat)),
            SlipUpdate.Free("NOPE"),
        });

        Assert.Equal(2, result.AppliedCount);
        Assert.Single(result.Errors);
        Assert.Equal("NOPE", result.Errors[0].SlipId);
        Assert.Equal(1, layoutEvents); // coalesced into one BatchUpdated notification
        Assert.Equal(new MarinaStatistics(6, 4, 1, 1), marina.GetStatistics());
    }

    [Fact]
    public void AddAndRemoveSlip_UpdatesLayout()
    {
        var marina = CreateMarina();
        marina.AddSlip(new Slip("A-X", "A", new Vector2(0, 50), 180, 10, 4));

        Assert.NotNull(marina.GetSlip("a-x")); // ids are case-insensitive
        Assert.True(marina.RemoveSlip("A-X"));
        Assert.False(marina.RemoveSlip("A-X"));
        Assert.Throws<MarinaLayoutException>(() => marina.AddSlip(new Slip("Z", "UNKNOWN-DOCK", Vector2.Zero, 0, 10, 4)));
    }

    [Fact]
    public void HitTest_ViewCenterOverSlip_ReturnsThatSlip()
    {
        var marina = CreateMarina();
        LookAtSlip(marina, "A-R03");

        var hit = marina.HitTest(400, 300);

        Assert.Equal("A-R03", hit?.SlipId);
    }

    [Fact]
    public void HitTest_HitsTallBoatAboveNeighbouringSlip()
    {
        var marina = CreateMarina();
        marina.AssignBoat("A-L01", new Boat("S1", "Wind Dancer", BoatType.MonohullSailboat) { LengthMeters = 10, BeamMeters = 3.3f });
        var slip = marina.GetSlip("A-L01")!;
        // Low camera looking along the dock: the ray passes through the mast region before reaching water.
        marina.Camera.SetPose(new CameraPose(new Vector3(slip.Center.X, 6, slip.Center.Y), 90f, 10f, 30f), immediate: true);

        var hit = marina.HitTest(400, 300);

        Assert.NotNull(hit);
        Assert.True(hit!.Value.HitBoat);
        Assert.Equal("A-L01", hit.Value.SlipId);
    }

    [Fact]
    public void StatusFilter_HidesSlipsFromPicking_AndClearsHiddenSelection()
    {
        var marina = CreateMarina();
        Assert.True(marina.SelectSlip("A-R03"));
        var cleared = false;
        marina.SelectionCleared += (_, _) => cleared = true;

        marina.SetStatusFilter(SlipStatusFilter.Occupied | SlipStatusFilter.Reserved);
        LookAtSlip(marina, "A-R03");

        Assert.True(cleared);
        Assert.Null(marina.SelectedSlip);
        Assert.Null(marina.HitTest(400, 300));
        Assert.False(marina.SelectSlip("A-R03"));
    }

    [Fact]
    public void Click_ThroughInputController_SelectsAndRaisesEvents()
    {
        var marina = CreateMarina();
        LookAtSlip(marina, "A-L02");
        SlipEventArgs? clicked = null;
        SlipEventArgs? selected = null;
        marina.SlipClicked += (_, e) => clicked = e;
        marina.SlipSelected += (_, e) => selected = e;

        marina.Input.PointerDown(400, 300, PointerButton.Left);
        marina.Input.PointerUp(401, 301, PointerButton.Left);

        Assert.Equal("A-L02", clicked?.SlipId);
        Assert.Equal(PointerButton.Left, clicked?.Button);
        Assert.Equal("A", clicked?.Dock?.Id);
        Assert.Equal("A-L02", selected?.SlipId);
        Assert.Equal("A-L02", marina.SelectedSlip?.Id);
    }

    [Fact]
    public void Drag_PansCamera_WithoutClicking()
    {
        var marina = CreateMarina();
        LookAtSlip(marina, "A-L02");
        var clicks = 0;
        marina.SlipClicked += (_, _) => clicks++;
        var before = marina.Camera.DesiredPose.Target;

        marina.Input.PointerDown(400, 300, PointerButton.Left);
        marina.Input.PointerMove(500, 300);
        marina.Input.PointerUp(500, 300, PointerButton.Left);

        Assert.Equal(0, clicks);
        Assert.NotEqual(before, marina.Camera.DesiredPose.Target);
    }

    [Fact]
    public void BuildRenderFrame_OnlyBumpsSceneVersionWhenStateChanges()
    {
        var marina = CreateMarina();
        var v1 = marina.BuildRenderFrame().SceneVersion;
        var v2 = marina.BuildRenderFrame().SceneVersion;
        marina.ReserveSlip("A-L01");
        var v3 = marina.BuildRenderFrame().SceneVersion;

        Assert.Equal(v1, v2);
        Assert.Equal(v1 + 1, v3);
    }

    [Fact]
    public void Presets_IncludeOverviewAndOnePerDock()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());

        Assert.Contains(marina.CameraPresets, p => p.Name == MarinaVisualizer.OverviewPresetName);
        Assert.Equal(4, marina.CameraPresets.Count(p => p.Name.StartsWith("Dock: ")));
        Assert.True(marina.ApplyCameraPreset("top down", immediate: true));
        Assert.True(marina.Camera.Pose.PitchDegrees > 85f);
    }

    [Fact]
    public void SampleMarina_IsValid_AndUsesEveryBoatType()
    {
        var layout = MockMarinaFactory.CreateSampleMarina();

        Assert.Empty(layout.Validate());
        var types = layout.Slips.Where(s => s.Boat is not null).Select(s => s.Boat!.Type).Distinct().ToList();
        Assert.Equal(BoatTypeCatalog.All.Count, types.Count);
    }
}
