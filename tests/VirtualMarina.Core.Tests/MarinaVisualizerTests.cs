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
            .AddPier("A", "Pier A", Vector2.Zero, 0f, 40f, pier => pier
                .AddBerths(PierSide.Left, 3, 5f, 12f)
                .AddBerths(PierSide.Right, 3, 5f, 12f))
            .Build();

    private static MarinaVisualizer CreateMarina()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(SmallLayout());
        marina.SetViewportSize(800, 600);
        return marina;
    }

    /// <summary>Points the camera straight down at a berth so the view center is over it.</summary>
    private static void LookAtBerth(MarinaVisualizer marina, string berthId)
    {
        var berth = marina.GetBerth(berthId)!;
        marina.Camera.SetPose(new CameraPose(new Vector3(berth.Center.X, 0, berth.Center.Y), 0f, 89f, 40f), immediate: true);
    }

    [Fact]
    public void InitializeLayout_RejectsDuplicateBerthIds()
    {
        var layout = SmallLayout();
        var broken = layout with { Berths = layout.Berths.Append(layout.Berths[0]).ToArray() };

        var ex = Assert.Throws<MarinaLayoutException>(() => new MarinaVisualizer().InitializeLayout(broken));
        Assert.Contains(ex.Errors, e => e.Contains("Duplicate berth id"));
    }

    [Fact]
    public void SetBerthStatus_RaisesEvent_AndFreeClearsBoat()
    {
        var marina = CreateMarina();
        var events = new List<BerthStatusChangedEventArgs>();
        marina.BerthStatusChanged += (_, e) => events.Add(e);
        var boat = new Boat("B1", "Aurora", BoatType.MotorYacht) { LengthMeters = 10, BeamMeters = 3 };

        marina.AssignBoat("A-L01", boat);
        var freed = marina.ReleaseBerth("A-L01");

        Assert.Equal(2, events.Count);
        Assert.Equal(BerthStatus.Free, events[0].OldStatus);
        Assert.Equal(BerthStatus.Occupied, events[0].NewStatus);
        Assert.Equal("Aurora", events[0].NewBoat?.Name);
        Assert.Equal(BerthStatus.Free, freed.Status);
        Assert.Null(freed.Boat);
    }

    [Fact]
    public void SetBerthStatus_ReservedToOccupied_KeepsExpectedBoat()
    {
        var marina = CreateMarina();
        marina.ReserveBerth("A-R02", new Boat("B2", "Zephyr", BoatType.JetSki));

        var arrived = marina.SetBerthStatus("A-R02", BerthStatus.Occupied);

        Assert.Equal("Zephyr", arrived.Boat?.Name);
    }

    [Fact]
    public void BatchUpdate_AppliesValidUpdates_AndReportsMissingBerths()
    {
        var marina = CreateMarina();
        var layoutEvents = 0;
        marina.LayoutChanged += (_, _) => layoutEvents++;

        var result = marina.BatchUpdate(new[]
        {
            BerthUpdate.Reserve("A-L01"),
            BerthUpdate.Occupy("A-L02", new Boat("B3", "Halcyon", BoatType.FishingBoat)),
            BerthUpdate.Free("NOPE"),
        });

        Assert.Equal(2, result.AppliedCount);
        Assert.Single(result.Errors);
        Assert.Equal("NOPE", result.Errors[0].BerthId);
        Assert.Equal(1, layoutEvents); // coalesced into one BatchUpdated notification
        Assert.Equal(new MarinaStatistics(6, 4, 1, 1), marina.GetStatistics());
    }

    [Fact]
    public void AddAndRemoveBerth_UpdatesLayout()
    {
        var marina = CreateMarina();
        marina.AddBerth(new Berth("A-X", "A", new Vector2(0, 50), 180, 10, 4));

        Assert.NotNull(marina.GetBerth("a-x")); // ids are case-insensitive
        Assert.True(marina.RemoveBerth("A-X"));
        Assert.False(marina.RemoveBerth("A-X"));
        Assert.Throws<MarinaLayoutException>(() => marina.AddBerth(new Berth("Z", "UNKNOWN-PIER", Vector2.Zero, 0, 10, 4)));
    }

    [Fact]
    public void HitTest_ViewCenterOverBerth_ReturnsThatBerth()
    {
        var marina = CreateMarina();
        LookAtBerth(marina, "A-R03");

        var hit = marina.HitTest(400, 300);

        Assert.Equal("A-R03", hit?.BerthId);
    }

    [Fact]
    public void HitTest_HitsTallBoatAboveNeighbouringBerth()
    {
        var marina = CreateMarina();
        marina.AssignBoat("A-L01", new Boat("S1", "Wind Dancer", BoatType.MonohullSailboat) { LengthMeters = 10, BeamMeters = 3.3f });
        var berth = marina.GetBerth("A-L01")!;
        // Low camera looking along the pier: the ray passes through the mast region before reaching water.
        marina.Camera.SetPose(new CameraPose(new Vector3(berth.Center.X, 6, berth.Center.Y), 90f, 10f, 30f), immediate: true);

        var hit = marina.HitTest(400, 300);

        Assert.NotNull(hit);
        Assert.True(hit.Value.HitBoat);
        Assert.Equal("A-L01", hit.Value.BerthId);
    }

    [Fact]
    public void StatusFilter_HidesBerthsFromPicking_AndClearsHiddenSelection()
    {
        var marina = CreateMarina();
        Assert.True(marina.SelectBerth("A-R03"));
        var cleared = false;
        marina.SelectionCleared += (_, _) => cleared = true;

        marina.SetStatusFilter(BerthStatusFilter.Occupied | BerthStatusFilter.Reserved);
        LookAtBerth(marina, "A-R03");

        Assert.True(cleared);
        Assert.Null(marina.SelectedBerth);
        Assert.Null(marina.HitTest(400, 300));
        Assert.False(marina.SelectBerth("A-R03"));
    }

    [Fact]
    public void Click_ThroughInputController_SelectsAndRaisesEvents()
    {
        var marina = CreateMarina();
        LookAtBerth(marina, "A-L02");
        BerthEventArgs? clicked = null;
        BerthEventArgs? selected = null;
        marina.BerthClicked += (_, e) => clicked = e;
        marina.BerthSelected += (_, e) => selected = e;

        marina.Input.PointerDown(400, 300, PointerButton.Left);
        marina.Input.PointerUp(401, 301, PointerButton.Left);

        Assert.Equal("A-L02", clicked?.BerthId);
        Assert.Equal(PointerButton.Left, clicked?.Button);
        Assert.Equal("A", clicked?.Pier?.Id);
        Assert.Equal("A-L02", selected?.BerthId);
        Assert.Equal("A-L02", marina.SelectedBerth?.Id);
    }

    [Fact]
    public void Drag_PansCamera_WithoutClicking()
    {
        var marina = CreateMarina();
        LookAtBerth(marina, "A-L02");
        var clicks = 0;
        marina.BerthClicked += (_, _) => clicks++;
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
        marina.ReserveBerth("A-L01");
        var v3 = marina.BuildRenderFrame().SceneVersion;

        Assert.Equal(v1, v2);
        Assert.Equal(v1 + 1, v3);
    }

    [Fact]
    public void Presets_IncludeOverviewAndOnePerPier()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());

        Assert.Contains(marina.CameraPresets, p => p.Name == MarinaVisualizer.OverviewPresetName);
        Assert.Equal(6, marina.CameraPresets.Count(p => p.Name.StartsWith("Pier: ")));
        Assert.True(marina.ApplyCameraPreset("top down", immediate: true));
        Assert.True(marina.Camera.Pose.PitchDegrees > 85f);
    }

    [Fact]
    public void SampleMarina_IsValid_AndUsesEveryBoatType()
    {
        var layout = MockMarinaFactory.CreateSampleMarina();

        Assert.Empty(layout.Validate());

        // Every boat model is exercised somewhere, but not all of them in a berth: a 45 m ferry does not moor in a
        // marina, so it earns its place out in the passing traffic instead.
        var berthed = layout.Berths.Where(s => s.Boat is not null).Select(s => s.Boat!.Type).Distinct().ToList();
        var passing = layout.MarineTraffic?.EffectiveVessels ?? Array.Empty<BoatType>();
        Assert.Equal(BoatTypeCatalog.All, berthed.Concat(passing).Distinct().OrderBy(type => (int)type).ToList());
        Assert.DoesNotContain(BoatType.Ferry, berthed);
        Assert.Contains(BoatType.Ferry, passing);
    }
}
