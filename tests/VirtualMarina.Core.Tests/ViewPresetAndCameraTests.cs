using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Serialization;
using VirtualMarina.SampleData;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// The automatic views are worked out when they are asked for, not on every resize or edit, and yet always match the
/// layout; they carry keys that survive a language or a pier's name; and the camera and its input hold up to limits
/// set any way round, to an oblique drag and to a press whose release never came.
/// </summary>
public class ViewPresetAndCameraTests
{
    private static MarinaVisualizer Sample(float width = 1280f, float height = 720f)
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(width, height);
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());
        return marina;
    }

    private static Pier PierA => new("A", "Pier A", new Vector2(0, -30), 0f, 60f);

    // ---- Worked out when needed ---------------------------------------------------------------

    [Fact]
    public void ResizingTheView_DoesNotWorkTheViewsOut_UntilTheyAreAskedFor()
    {
        var marina = Sample();
        _ = marina.CameraPresets;
        var rebuilds = marina.PresetRebuildCount;

        // A window being dragged to a new size: dozens of sizes, and nobody looks at the views meanwhile.
        for (var i = 0; i < 50; i++) marina.SetViewportSize(800f + i * 10f, 600f + i * 5f);
        Assert.Equal(rebuilds, marina.PresetRebuildCount);

        _ = marina.CameraPresets;
        _ = marina.CameraPresets;
        Assert.Equal(rebuilds + 1, marina.PresetRebuildCount);
    }

    [Fact]
    public void ABatchOfEdits_WorksTheViewsOutOnce_WhenTheyAreNextUsed()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(PierA);
        _ = marina.CameraPresets;
        var rebuilds = marina.PresetRebuildCount;

        var pier = marina.GetPier("A")!;
        marina.AddBerths(Enumerable.Range(0, 10).Select(i => BerthGenerator.AtPier(pier, $"A-{i}", PierSide.Left, i * 5f, 4.5f, 12f)));
        marina.AddDivider(new Divider("D1", new Vector2(10, 10), 0f, 20f) { PierId = "A" });
        Assert.Equal(rebuilds, marina.PresetRebuildCount);

        marina.ResetCamera(immediate: true);
        Assert.Equal(rebuilds + 1, marina.PresetRebuildCount);
    }

    [Fact]
    public void AfterEveryKindOfEdit_TheViewsAndCameraLimits_MatchAFreshlyLoadedCopy()
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(1200f, 800f);
        _ = marina.CameraPresets;

        // Built up one piece at a time, with the views looked at in between, as a designer does it.
        marina.AddPier(PierA);
        _ = marina.CameraPresets;
        var pier = marina.GetPier("A")!;
        marina.AddBerths(Enumerable.Range(0, 8).Select(i => BerthGenerator.AtPier(pier, $"A-{i}", PierSide.Right, i * 6f, 5f, 14f)));
        _ = marina.CameraPresets;
        marina.UpdateBerth(BerthUpdate.Geometry("A-0", center: new Vector2(80f, 40f)));
        marina.RemoveBerth("A-7");
        marina.AddDivider(new Divider("D1", new Vector2(-40, 10), 90f, 30f) { PierId = "A" });
        marina.AddLandArea(new LandArea("L", new[] { new Vector2(-60, -60), new Vector2(-20, -60), new Vector2(-20, -40) }, 2f));

        var fresh = new MarinaVisualizer();
        fresh.SetViewportSize(1200f, 800f);
        fresh.InitializeLayout(marina.GetLayout());

        var ours = marina.CameraPresets.Where(p => p.IsBuiltIn).ToList();
        var theirs = fresh.CameraPresets.Where(p => p.IsBuiltIn).ToList();
        Assert.Equal(theirs.Select(p => p.Key), ours.Select(p => p.Key));
        for (var i = 0; i < ours.Count; i++)
        {
            Assert.True(Vector3.Distance(ours[i].Pose.Target, theirs[i].Pose.Target) < 0.01f, $"{ours[i].Key} aims elsewhere");
            Assert.Equal(theirs[i].Pose.Distance, ours[i].Pose.Distance, 2);
        }

        Assert.Equal(fresh.Camera.Constraints.TargetBoundsMin, marina.Camera.Constraints.TargetBoundsMin);
        Assert.Equal(fresh.Camera.Constraints.TargetBoundsMax, marina.Camera.Constraints.TargetBoundsMax);
        Assert.Equal(fresh.Camera.Constraints.MaxDistance, marina.Camera.Constraints.MaxDistance);
    }

    [Fact]
    public void APierAddedBeforeItsBerths_GetsAViewOfThePierWithTheBerths()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(PierA);
        var bare = marina.CameraPresets.Single(p => p.Key == MarinaVisualizer.PierPresetKey("A")).Pose;

        var pier = marina.GetPier("A")!;
        marina.AddBerths(Enumerable.Range(0, 10).Select(i => BerthGenerator.AtPier(pier, $"A-{i}", PierSide.Left, i * 6f, 5f, 30f)));

        var berthed = marina.CameraPresets.Single(p => p.Key == MarinaVisualizer.PierPresetKey("A")).Pose;
        Assert.NotEqual(bare, berthed);
    }

    // ---- The list handed out -------------------------------------------------------------------

    [Fact]
    public void CameraPresets_IsAReadOnlySnapshot()
    {
        var marina = Sample();
        var presets = marina.CameraPresets;

        Assert.IsNotType<List<CameraPreset>>(presets);
        Assert.True(((ICollection<CameraPreset>)presets).IsReadOnly);

        var count = presets.Count;
        marina.SaveCameraPreset("Mine");
        Assert.Equal(count, presets.Count);
        Assert.Equal(count + 1, marina.CameraPresets.Count);
    }

    [Fact]
    public void CameraPresetsChanged_IsRaisedOnceUntilTheListIsReadAgain()
    {
        var marina = Sample();
        _ = marina.CameraPresets;
        var raised = 0;
        marina.CameraPresetsChanged += (_, _) => raised++;

        marina.SaveCameraPreset("One");
        marina.SetViewportSize(900f, 900f);
        marina.AddPier(new Pier("Z", "Pier Z", new Vector2(300, 300), 0f, 40f));
        Assert.Equal(1, raised);

        _ = marina.CameraPresets;
        marina.RemoveCameraPreset("One");
        Assert.Equal(2, raised);
    }

    [Fact]
    public void CameraPresetsChanged_WaitsForTheEndOfAnUpdateScope()
    {
        var marina = Sample();
        _ = marina.CameraPresets;
        var raised = 0;
        marina.CameraPresetsChanged += (_, _) => raised++;

        using (marina.BeginUpdate())
        {
            marina.AddPier(new Pier("Z", "Pier Z", new Vector2(300, 300), 0f, 40f));
            Assert.Equal(0, raised);
        }

        Assert.Equal(1, raised);
    }

    // ---- Keys ----------------------------------------------------------------------------------

    [Fact]
    public void EveryAutomaticView_HasAKey_AndPierViewsAreKeyedByPierId()
    {
        var marina = Sample();
        var builtIn = marina.CameraPresets.Where(p => p.IsBuiltIn).ToList();

        Assert.All(builtIn, preset => Assert.False(string.IsNullOrEmpty(preset.Key)));
        Assert.Equal(builtIn.Count, builtIn.Select(p => p.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var pier in marina.GetPiers()) Assert.Contains(builtIn, p => p.Key == MarinaVisualizer.PierPresetKey(pier.Id));
        Assert.All(marina.CameraPresets.Where(p => !p.IsBuiltIn), preset => Assert.Null(preset.Key));
    }

    [Fact]
    public void TwoPiersWithTheSameName_GetViewsWithDifferentNames()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Guest pier", new Vector2(0, 0), 0f, 40f));
        marina.AddPier(new Pier("B", "Guest pier", new Vector2(60, 0), 0f, 40f));

        var names = marina.CameraPresets.Where(p => p.IsBuiltIn).Select(p => p.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(marina.ApplyBuiltInCameraPreset(MarinaVisualizer.PierPresetKey("B"), immediate: true));
    }

    [Fact]
    public void AViewSwitchedOffByKey_StaysOffThroughASaveAndAPierRename()
    {
        var marina = Sample();
        var pier = marina.GetPiers()[0];
        Assert.True(marina.SetCameraPresetEnabled(MarinaVisualizer.PierPresetKey(pier.Id), enabled: false));
        Assert.True(marina.SetCameraPresetEnabled("North", enabled: false));

        marina.ChangePierId(pier.Id, "RENAMED");
        Assert.False(marina.CameraPresets.Single(p => p.Key == MarinaVisualizer.PierPresetKey("RENAMED")).IsEnabled);

        var copy = new MarinaVisualizer();
        MarinaDocument.Parse(MarinaDocument.FromVisualizer(marina).ToJson()).ApplyTo(copy);
        Assert.False(copy.CameraPresets.Single(p => p.Key == MarinaVisualizer.PierPresetKey("RENAMED")).IsEnabled);
        Assert.False(copy.CameraPresets.Single(p => p.Key == "North").IsEnabled);
        Assert.Equal(2, copy.CameraPresets.Count(p => p.IsBuiltIn && !p.IsEnabled));
    }

    [Fact]
    public void AFileThatSwitchedAPierViewOffByName_IsStillHonoured()
    {
        var marina = Sample();
        var pier = marina.GetPiers()[0];
        var document = MarinaDocument.FromVisualizer(marina);
        document.DisabledCameraPresets = new[] { $"Pier: {pier.Name}" };

        var copy = new MarinaVisualizer();
        document.ApplyTo(copy);

        Assert.False(copy.CameraPresets.Single(p => p.Key == MarinaVisualizer.PierPresetKey(pier.Id)).IsEnabled);
        Assert.Contains(MarinaVisualizer.PierPresetKey(pier.Id), MarinaDocument.FromVisualizer(copy).DisabledCameraPresets);
    }

    // ---- Pier close-up -------------------------------------------------------------------------

    [Fact]
    public void ShowPierCloseUp_GoesToThePiersOwnView()
    {
        var marina = Sample();
        var pier = marina.GetPiers()[0];

        Assert.True(marina.ShowPierCloseUp(pier.Id, immediate: true));
        var preset = marina.CameraPresets.Single(p => p.Key == MarinaVisualizer.PierPresetKey(pier.Id));
        Assert.Equal(marina.Camera.Constrain(preset.Pose).Target, marina.Camera.DesiredPose.Target);
        Assert.False(marina.ShowPierCloseUp("no such pier"));

#pragma warning disable CS0618 // The old name still does the same thing.
        var before = marina.Camera.DesiredPose;
        Assert.True(marina.FocusPier(pier.Id, immediate: true));
#pragma warning restore CS0618
        Assert.Equal(before, marina.Camera.DesiredPose);
    }

    // ---- Focus ---------------------------------------------------------------------------------

    [Fact]
    public void FocusingOnAWholePiersBerths_KeepsEveryCornerAndMastInView()
    {
        var marina = Sample();
        var berths = marina.GetBerthsByPier(marina.GetPiers()[0].Id);
        Assert.True(marina.FocusBerths(berths.Select(b => b.Id), new CameraAngle(30f, 40f), immediate: true));

        foreach (var berth in berths)
        {
            foreach (var corner in berth.Bounds.GetCorners())
            {
                Assert.True(marina.TryProjectToScreen(MarinaMath.ToWorld(corner), out var screen));
                Assert.InRange(screen.X, -1f, marina.ViewportSize.X + 1f);
                Assert.InRange(screen.Y, -1f, marina.ViewportSize.Y + 1f);
            }
        }
    }

    // ---- Camera limits -------------------------------------------------------------------------

    [Fact]
    public void LimitsSetTheWrongWayRound_OrToNothing_NeverMakeTheCameraThrowOrGoNaN()
    {
        var camera = new OrbitCamera();
        camera.Constraints.TargetBoundsMin = new Vector2(100f, 100f);
        camera.Constraints.TargetBoundsMax = new Vector2(-100f, -100f);
        camera.Constraints.MinDistance = 0f;
        camera.Constraints.MaxDistance = float.NaN;
        camera.Constraints.MinTargetHeight = 20f;
        camera.Constraints.MaxTargetHeight = 5f;

        var exception = Record.Exception(() =>
        {
            camera.SetPose(new CameraPose(new Vector3(500f, 60f, -500f), 0f, 45f, 0f));
            camera.Zoom(1000f);
            camera.Update(0.1f);
        });

        Assert.Null(exception);
        var pose = camera.Pose;
        Assert.True(float.IsFinite(pose.Distance) && pose.Distance > 0f);
        Assert.InRange(pose.Target.X, -100f, 100f);
        Assert.InRange(pose.Target.Y, 5f, 20f);
    }

    [Fact]
    public void TheTargetHeightRange_IsAConstraint()
    {
        var camera = new OrbitCamera();
        camera.Constraints.MaxTargetHeight = 2f;
        camera.SetPose(new CameraPose(new Vector3(0f, 30f, 0f), 0f, 45f, 100f), immediate: true);
        Assert.Equal(2f, camera.Pose.Target.Y);
    }

    // ---- Panning -------------------------------------------------------------------------------

    [Theory]
    [InlineData(20f)]
    [InlineData(45f)]
    [InlineData(80f)]
    public void AGrabPan_KeepsTheGroundUnderThePointer(float pitch)
    {
        var camera = new OrbitCamera { Smoothing = 0f };
        camera.Constraints.TargetBoundsMin = new Vector2(-10_000f);
        camera.Constraints.TargetBoundsMax = new Vector2(10_000f);
        camera.SetPose(new CameraPose(Vector3.Zero, 30f, pitch, 150f), immediate: true);

        var from = new Vector2(300f, 500f);
        var to = new Vector2(420f, 380f);
        var grabbed = GroundUnder(camera, from);

        camera.Pan(from, to, 1000f, 800f);
        camera.Update(1f);

        Assert.True(Vector3.Distance(grabbed, GroundUnder(camera, to)) < 0.05f, $"the ground slid from under the pointer at {pitch}°");
    }

    [Fact]
    public void ThePixelPan_MovesTheGroundAtTheMiddleOfAnObliqueViewWithThePointer()
    {
        var camera = new OrbitCamera { Smoothing = 0f };
        camera.SetPose(new CameraPose(Vector3.Zero, 0f, 30f, 100f), immediate: true);
        var middle = new Vector2(500f, 400f);
        var grabbed = GroundUnder(camera, middle);

        camera.Pan(0f, 2f, 800f);
        camera.Update(1f);

        // Two pixels down at the middle of the view: the ground that was there is now two pixels lower, near enough.
        var screen = camera.WorldToScreen(grabbed, 1000f, 800f)!.Value;
        Assert.Equal(402f, screen.Y, 0);
    }

    [Fact]
    public void AnArrowKey_PansFurtherWhenZoomedOut()
    {
        static float MoveAt(float distance)
        {
            var marina = new MarinaVisualizer();
            marina.Camera.SetPose(new CameraPose(Vector3.Zero, 0f, 45f, distance), immediate: true);
            marina.Input.KeyDown(MarinaKey.Left);
            return marina.Camera.DesiredPose.Target.Length();
        }

        Assert.Equal(10f, MoveAt(MarinaInputController.KeyboardPanReferenceDistance), 2);
        Assert.True(MoveAt(400f) > MoveAt(40f) * 5f);
    }

    [Fact]
    public void AFixedArrowStep_CanStillBeHad()
    {
        var marina = new MarinaVisualizer();
        marina.Camera.SetPose(new CameraPose(Vector3.Zero, 0f, 45f, 400f), immediate: true);
        marina.Input.KeyboardPanScalesWithDistance = false;
        marina.Input.KeyDown(MarinaKey.Left);
        Assert.Equal(10f, marina.Camera.DesiredPose.Target.Length(), 2);
    }

    // ---- Wheel ---------------------------------------------------------------------------------

    [Fact]
    public void TheWheel_ZoomsTowardRaisedLand_NotTheWaterBelowIt()
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(1000f, 800f);
        marina.AddLandArea(new LandArea("Hill", new[] { new Vector2(-200, -200), new Vector2(200, -200), new Vector2(200, 200), new Vector2(-200, 200) }, 12f));
        marina.Camera.SetPose(new CameraPose(new Vector3(0f, 12f, 0f), 0f, 50f, 150f), immediate: true);

        var point = marina.GetGroundPoint(500f, 400f);
        Assert.NotNull(point);
        Assert.Equal(12f, point.Value.Y, 2);
    }

    // ---- A lost release ------------------------------------------------------------------------

    [Fact]
    public void APressWhileAnotherIsRecorded_StartsAfresh()
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(1000f, 800f);

        // The release of this press went to another window.
        marina.Input.PointerDown(500, 400, PointerButton.Left);
        marina.Input.PointerMove(600, 450);
        Assert.True(marina.Input.IsDragging);

        marina.Input.PointerDown(200, 200, PointerButton.Right);
        Assert.False(marina.Input.IsDragging);

        var before = marina.Camera.DesiredPose;
        marina.Input.PointerMove(260, 200);
        Assert.NotEqual(before.YawDegrees, marina.Camera.DesiredPose.YawDegrees); // the right button's orbit
    }

    [Fact]
    public void CancelPointer_EndsTheDragWithoutAClick()
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(1000f, 800f);
        var clicks = 0;
        marina.SelectionCleared += (_, _) => clicks++;

        marina.Input.PointerDown(500, 400, PointerButton.Left);
        marina.Input.CancelPointer();
        marina.Input.PointerUp(500, 400, PointerButton.Left);
        var before = marina.Camera.DesiredPose;
        marina.Input.PointerMove(700, 600);

        Assert.False(marina.Input.IsDragging);
        Assert.Equal(before, marina.Camera.DesiredPose);
        Assert.Equal(0, clicks);
    }

    private static Vector3 GroundUnder(OrbitCamera camera, Vector2 pixel)
    {
        var ray = camera.ScreenPointToRay(pixel.X, pixel.Y, 1000f, 800f);
        Assert.True(ray.IntersectHorizontalPlane(camera.Pose.Target.Y, out var distance));
        return ray.GetPoint(distance);
    }
}
