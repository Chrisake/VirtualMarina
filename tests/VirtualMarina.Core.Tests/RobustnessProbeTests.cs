using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// Values that reach the renderer without passing the layout validation on the way: a boat's dimensions, the
/// appearance settings, and the camera pose a host sets itself. A non-finite number here does not throw — it
/// spreads through the matrices until nothing is drawn at all and nothing says why.
/// </summary>
public class RobustnessProbeTests
{
    private static MarinaVisualizer WithOneBerth()
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(800, 600);
        marina.AddPier(new Pier("A", "Pier A", Vector2.Zero, 0f, 40f));
        marina.AddBerth(BerthGenerator.AtPier(marina.GetPier("A")!, "A-L01", PierSide.Left, 0f, 5f, 12f));
        return marina;
    }

    private static void AssertFrameIsFinite(MarinaVisualizer marina, string what)
    {
        var frame = marina.BuildRenderFrame();
        foreach (var o in frame.Objects)
        {
            Assert.True(float.IsFinite(o.World.Translation.X) && float.IsFinite(o.World.Translation.Y)
                && float.IsFinite(o.World.Translation.Z) && float.IsFinite(o.World.M11),
                $"{what} put a non-finite transform into the scene");
            Assert.True(float.IsFinite(o.Tint.X) && float.IsFinite(o.Tint.W), $"{what} put a non-finite tint into the scene");
        }
    }

    // ---- A boat's dimensions -----------------------------------------------------------------

    /// <summary>
    /// Infinity used to be the gap here: it passes a bare "greater than zero" test, so an infinite boat reached
    /// the geometry and turned its transform into NaN. NaN and negatives were always refused.
    /// </summary>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-5f)]
    public void ABoatWithAnImpossibleLength_IsRefused(float length)
    {
        var marina = WithOneBerth();

        var error = Assert.Throws<MarinaLayoutException>(() => marina.AssignBoat("A-L01",
            new Boat("B1", "Odd", BoatType.MotorYacht) { LengthMeters = length, BeamMeters = 3f }));

        Assert.Contains("length", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-2f)]
    public void ABoatWithAnImpossibleBeam_IsRefused(float beam)
    {
        var marina = WithOneBerth();

        var error = Assert.Throws<MarinaLayoutException>(() => marina.AssignBoat("A-L01",
            new Boat("B1", "Odd", BoatType.MotorYacht) { LengthMeters = 10f, BeamMeters = beam }));

        Assert.Contains("beam", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ABoatOfAPlausibleSize_StillGoesIn()
    {
        var marina = WithOneBerth();

        marina.AssignBoat("A-L01", new Boat("B1", "Aurora", BoatType.MotorYacht) { LengthMeters = 10f, BeamMeters = 3f });

        Assert.Equal(BerthStatus.Occupied, marina.GetBerth("A-L01")!.Status);
        AssertFrameIsFinite(marina, "an ordinary boat");
    }

    // ---- The camera pose a host sets itself ---------------------------------------------------

    /// <summary>
    /// <c>Constrain</c> guards Distance, PitchDegrees and YawDegrees against non-finite values, so a non-finite
    /// Target should be guarded the same way rather than reaching the view matrix.
    /// </summary>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void ACameraTargetThatIsNotANumber_IsRejectedLikeTheOtherThreeFields(float bad)
    {
        var camera = new OrbitCamera();

        camera.SetPose(new CameraPose(new Vector3(bad, 0f, 0f), 45f, 40f, 100f), immediate: true);

        Assert.True(float.IsFinite(camera.Pose.Target.X), "the camera target should never be non-finite");
        Assert.True(float.IsFinite(camera.Position.X), "the eye is computed from the target, so it goes too");
    }

    [Fact]
    public void ANonFiniteCameraTarget_WouldOtherwiseBlankTheWholeView()
    {
        var marina = WithOneBerth();

        marina.Camera.SetPose(new CameraPose(new Vector3(float.NaN, 0f, 0f), 45f, 40f, 100f), immediate: true);
        var frame = marina.BuildRenderFrame();

        // Every matrix in the frame is derived from the eye, so one bad number takes the lot.
        Assert.True(float.IsFinite(frame.View.M11), "the view matrix should stay finite");
        Assert.True(float.IsFinite(frame.Projection.M11), "the projection matrix should stay finite");
    }

    // ---- Appearance settings -----------------------------------------------------------------

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-10f)]
    public void ImpossibleWaveSettings_DoNotPoisonTheScene(float value)
    {
        var marina = WithOneBerth();

        marina.Style.Water.WaveAmplitude = value;
        marina.Style.Water.WaveFrequency = value;
        marina.Style.Water.Size = value;

        AssertFrameIsFinite(marina, $"wave settings of {value}");
    }

    [Fact]
    public void ANonFiniteSunDirection_DoesNotPoisonTheScene()
    {
        var marina = WithOneBerth();

        marina.Style.Lighting.SunDirection = new Vector3(float.NaN, float.NaN, float.NaN);

        AssertFrameIsFinite(marina, "a non-finite sun direction");
    }

    [Fact]
    public void AZeroSunDirection_DoesNotNormaliseToNaN()
    {
        var marina = WithOneBerth();

        marina.Style.Lighting.SunDirection = Vector3.Zero;

        var frame = marina.BuildRenderFrame();
        Assert.True(float.IsFinite(frame.Lighting.SunDirection.X), "normalising a zero vector should not produce NaN");
    }

    // ---- Multi-berths ------------------------------------------------------------------------

    private static MarinaVisualizer WithARow(int count = 4)
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(800, 600);
        marina.AddPier(new Pier("A", "Pier A", Vector2.Zero, 0f, 60f));
        var pier = marina.GetPier("A")!;
        for (var i = 0; i < count; i++)
        {
            marina.AddBerth(BerthGenerator.AtPier(pier, $"A-L{i + 1:00}", PierSide.Left, i * 6f, 5f, 12f));
        }

        return marina;
    }

    [Fact]
    public void ABerthCannotBeInTwoMultiBerthsAtOnce()
    {
        var marina = WithARow();
        var boat = new Boat("B1", "Guest", BoatType.MotorYacht) { LengthMeters = 9f, BeamMeters = 3f };
        marina.MoorAlongside(new[] { "A-L01", "A-L02" }, boat);

        // A-L02 is already spoken for.
        Assert.ThrowsAny<Exception>(() => marina.MoorAlongside(new[] { "A-L02", "A-L03" }, boat));
    }

    [Fact]
    public void ReleasingAMultiBerthTwice_IsHarmlessTheSecondTime()
    {
        var marina = WithARow();
        var boat = new Boat("B1", "Guest", BoatType.MotorYacht) { LengthMeters = 9f, BeamMeters = 3f };
        var multi = marina.MoorAlongside(new[] { "A-L01", "A-L02" }, boat);

        marina.ReleaseMultiBerth(multi.Id);

        var second = Record.Exception(() => marina.ReleaseMultiBerth(multi.Id));
        Assert.True(second is null or KeyNotFoundException,
            $"releasing twice should be ignored or report a missing id, not {second?.GetType().Name}");
    }

    [Fact]
    public void RemovingABerthInsideAMultiBerth_LeavesNoDanglingMember()
    {
        var marina = WithARow();
        var boat = new Boat("B1", "Guest", BoatType.MotorYacht) { LengthMeters = 9f, BeamMeters = 3f };
        var multi = marina.MoorAlongside(new[] { "A-L01", "A-L02" }, boat);

        marina.RemoveBerth("A-L01");

        var after = marina.GetMultiBerth(multi.Id);
        if (after is not null)
        {
            Assert.All(after.BerthIds, id => Assert.NotNull(marina.GetBerth(id)));
        }

        Assert.NotNull(marina.BuildRenderFrame());
    }

    // ---- The input controller ----------------------------------------------------------------

    [Fact]
    public void PointerUpWithoutAPointerDown_IsIgnored()
    {
        var marina = WithARow();

        var exception = Record.Exception(() => marina.Input.PointerUp(100, 100, PointerButton.Left));

        Assert.Null(exception);
        Assert.Null(marina.SelectedBerth);
    }

    [Fact]
    public void PointerMoveOutsideTheViewport_IsIgnored()
    {
        var marina = WithARow();

        var exception = Record.Exception(() =>
        {
            marina.Input.PointerMove(-5000, -5000);
            marina.Input.PointerMove(float.NaN, float.NaN);
            marina.Input.PointerMove(1e9f, 1e9f);
        });

        Assert.Null(exception);
        Assert.True(float.IsFinite(marina.Camera.DesiredPose.Distance));
    }

    [Fact]
    public void AWheelOfZeroOrNaN_LeavesTheCameraWhereItWas()
    {
        var marina = WithARow();
        var before = marina.Camera.DesiredPose.Distance;

        marina.Input.Wheel(0f, 400, 300);
        marina.Input.Wheel(float.NaN, 400, 300);

        Assert.Equal(before, marina.Camera.DesiredPose.Distance, 4);
    }

    [Fact]
    public void TwoPointerDownsWithoutAnUp_DoNotLeaveTheControllerStuck()
    {
        var marina = WithARow();

        marina.Input.PointerDown(400, 300, PointerButton.Left);
        marina.Input.PointerDown(410, 310, PointerButton.Left);
        marina.Input.PointerUp(410, 310, PointerButton.Left);

        // A second down should not leave a drag running after the up.
        var before = marina.Camera.DesiredPose.Target;
        marina.Input.PointerMove(600, 500);
        Assert.Equal(before, marina.Camera.DesiredPose.Target);
    }

    // ---- Batch updates -----------------------------------------------------------------------

    [Fact]
    public void ABatchWithTwoUpdatesForTheSameBerth_AppliesTheLastOne()
    {
        var marina = WithARow();

        marina.BatchUpdate(new[]
        {
            BerthUpdate.Occupy("A-L01", new Boat("B1", "First", BoatType.MotorYacht) { LengthMeters = 9f, BeamMeters = 3f }),
            BerthUpdate.Free("A-L01"),
        });

        Assert.Equal(BerthStatus.Free, marina.GetBerth("A-L01")!.Status);
    }

    [Fact]
    public void ABatchCarryingUnknownIds_ReportsThemAndAppliesTheRest()
    {
        var marina = WithARow();

        var result = marina.BatchUpdate(new[]
        {
            BerthUpdate.Free("A-L01"),
            BerthUpdate.Free("does-not-exist"),
        });

        Assert.NotEmpty(result.Errors);
        Assert.Equal(BerthStatus.Free, marina.GetBerth("A-L01")!.Status);
    }

    [Fact]
    public void AnEmptyBatch_IsAcceptedAndChangesNothing()
    {
        var marina = WithARow();

        var result = marina.BatchUpdate(Array.Empty<BerthUpdate>());

        Assert.Empty(result.Errors);
    }
}
