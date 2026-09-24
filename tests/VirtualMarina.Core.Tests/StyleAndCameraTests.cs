using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// Settings a host changes at run time, and the camera it changes them through: what is clamped, what raises a
/// change so the scene is rebuilt, and what a label ashore is lettered in.
/// </summary>
public class StyleAndCameraTests
{
    // ---- StatusColorScheme -------------------------------------------------------------------

    [Fact]
    public void StatusColors_StartAtTheirDocumentedDefaults()
    {
        var colors = new StatusColorScheme();

        Assert.Equal(StatusColorScheme.DefaultFree, colors.FreeColor);
        Assert.Equal(StatusColorScheme.DefaultOccupied, colors.OccupiedColor);
        Assert.Equal(StatusColorScheme.DefaultReserved, colors.ReservedColor);
        Assert.Equal(StatusColorScheme.DefaultTemporarilyFree, colors.TemporarilyFreeColor);
        Assert.Equal(StatusColorScheme.DefaultDisabled, colors.DisabledColor);
    }

    [Theory]
    [InlineData(-3f, 0f)]
    [InlineData(0.4f, 0.4f)]
    [InlineData(7f, 1f)]
    public void Opacities_AreClampedToZeroAndOne(float set, float expected)
    {
        var colors = new StatusColorScheme
        {
            PadOpacity = set,
            OccupiedBoatOpacity = set,
            GhostBoatTint = set,
        };

        Assert.Equal(expected, colors.PadOpacity, 4);
        Assert.Equal(expected, colors.OccupiedBoatOpacity, 4);
        Assert.Equal(expected, colors.GhostBoatTint, 4);
    }

    [Fact]
    public void GhostBoatOpacity_SetsBothGhostStatuses_AndReadsBackTheReservedOne()
    {
        var colors = new StatusColorScheme { GhostBoatOpacity = 0.3f };

        Assert.Equal(0.3f, colors.ReservedBoatOpacity, 4);
        Assert.Equal(0.3f, colors.TemporarilyFreeBoatOpacity, 4);
        Assert.Equal(colors.ReservedBoatOpacity, colors.GhostBoatOpacity, 4);
    }

    [Fact]
    public void GhostBoatOpacity_RaisesChangedOnlyWhenSomethingActuallyChanges()
    {
        var colors = new StatusColorScheme { GhostBoatOpacity = 0.3f };
        var changes = 0;
        colors.Changed += (_, _) => changes++;

        colors.GhostBoatOpacity = 0.3f;   // Same value: nothing to rebuild for.
        Assert.Equal(0, changes);

        colors.GhostBoatOpacity = 0.55f;
        Assert.Equal(1, changes);

        // Clamping means these two land on the same stored value, so only the first is a change.
        colors.GhostBoatOpacity = 4f;
        colors.GhostBoatOpacity = 9f;
        Assert.Equal(2, changes);
    }

    [Fact]
    public void StatusMarkerScale_IsClampedToItsUsefulRange()
    {
        var colors = new StatusColorScheme { StatusMarkerScale = 0.01f };
        Assert.Equal(0.2f, colors.StatusMarkerScale, 4);

        colors.StatusMarkerScale = 99f;
        Assert.Equal(5f, colors.StatusMarkerScale, 4);
    }

    [Fact]
    public void GetBoatOpacity_FollowsTheStatus()
    {
        var colors = new StatusColorScheme
        {
            OccupiedBoatOpacity = 0.9f,
            ReservedBoatOpacity = 0.4f,
            TemporarilyFreeBoatOpacity = 0.2f,
        };

        Assert.Equal(0.9f, colors.GetBoatOpacity(BerthStatus.Occupied), 4);
        Assert.Equal(0.4f, colors.GetBoatOpacity(BerthStatus.Reserved), 4);
        Assert.Equal(0.2f, colors.GetBoatOpacity(BerthStatus.TemporarilyFree), 4);
    }

    [Fact]
    public void Reset_PutsEveryStatusColorBack()
    {
        var colors = new StatusColorScheme { FreeColor = new ColorRgba(1f, 0f, 1f), PadOpacity = 0.01f };

        colors.Reset();

        Assert.Equal(StatusColorScheme.DefaultFree, colors.FreeColor);
    }

    [Fact]
    public void Get_ForAStatusOutsideTheEnum_ReturnsAGreyRatherThanThrowing()
    {
        var colors = new StatusColorScheme();

        var grey = colors.Get((BerthStatus)42);

        Assert.Equal(0.6f, grey.R, 4);
        Assert.Equal(0.6f, grey.G, 4);
    }

    // ---- Label colours -----------------------------------------------------------------------

    [Fact]
    public void LabelStyle_LettersBerthsAshoreDarkAndBerthsAfloatLight()
    {
        var labels = new MarinaStyle().Labels;

        // The water label has to carry on dark water and the one ashore on light quay or grass, so the two
        // defaults sit on opposite sides of mid grey.
        var afloat = (labels.Color.R + labels.Color.G + labels.Color.B) / 3f;
        var ashore = (labels.AshoreColor.R + labels.AshoreColor.G + labels.AshoreColor.B) / 3f;

        Assert.True(afloat > 0.5f, "a label on the water should be light");
        Assert.True(ashore < 0.5f, "a label ashore should be dark");
    }

    [Fact]
    public void AshoreColor_RaisesChanged_SoTheSceneIsRebuilt()
    {
        var labels = new MarinaStyle().Labels;
        var changes = 0;
        labels.Changed += (_, _) => changes++;

        labels.AshoreColor = new ColorRgba(0.5f, 0.1f, 0.1f);
        Assert.Equal(1, changes);

        labels.AshoreColor = new ColorRgba(0.5f, 0.1f, 0.1f);
        Assert.Equal(1, changes);
    }

    // ---- OrbitCamera -------------------------------------------------------------------------

    private static OrbitCamera LookingAtTheOrigin()
    {
        var camera = new OrbitCamera();
        camera.SetPose(new CameraPose(Vector3.Zero, 45f, 40f, 100f), immediate: true);
        return camera;
    }

    [Fact]
    public void SetPose_Immediate_ArrivesAtOnce_AndOtherwiseEasesIn()
    {
        var camera = LookingAtTheOrigin();
        Assert.False(camera.IsMoving);

        camera.SetPose(new CameraPose(new Vector3(50f, 0f, 0f), 10f, 30f, 200f));
        Assert.True(camera.IsMoving);
        Assert.NotEqual(camera.DesiredPose.Distance, camera.Pose.Distance, 3);

        // Enough frames that the easing has settled.
        for (var i = 0; i < 200; i++) camera.Update(1f / 60f);

        Assert.False(camera.IsMoving);
        Assert.Equal(200f, camera.Pose.Distance, 2);
    }

    [Fact]
    public void Orbit_TurnsTheCamera_AndPitchStaysInsideItsConstraints()
    {
        var camera = LookingAtTheOrigin();

        camera.Orbit(90f, 0f);
        Assert.Equal(135f, camera.DesiredPose.YawDegrees, 3);

        camera.Orbit(0f, 1000f);
        Assert.InRange(camera.DesiredPose.PitchDegrees,
            camera.Constraints.MinPitchDegrees, camera.Constraints.MaxPitchDegrees);

        camera.Orbit(0f, -1000f);
        Assert.InRange(camera.DesiredPose.PitchDegrees,
            camera.Constraints.MinPitchDegrees, camera.Constraints.MaxPitchDegrees);
    }

    /// <summary>
    /// The distance is divided by the factor, so a factor above one moves in and one below moves out. Worth
    /// pinning down: the name alone does not say which way round it is.
    /// </summary>
    [Fact]
    public void Zoom_DividesTheDistanceByItsFactor()
    {
        var camera = LookingAtTheOrigin();

        camera.Zoom(2f);
        Assert.Equal(50f, camera.DesiredPose.Distance, 3);

        camera.Zoom(0.5f);
        Assert.Equal(100f, camera.DesiredPose.Distance, 3);
    }

    [Fact]
    public void Zoom_StopsAtTheDistanceLimits()
    {
        var camera = LookingAtTheOrigin();

        for (var i = 0; i < 50; i++) camera.Zoom(2f);
        Assert.Equal(camera.Constraints.MinDistance, camera.DesiredPose.Distance, 3);

        for (var i = 0; i < 100; i++) camera.Zoom(0.5f);
        Assert.Equal(camera.Constraints.MaxDistance, camera.DesiredPose.Distance, 3);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-2f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Zoom_IgnoresAFactorThatWouldNotMakeSense(float factor)
    {
        var camera = LookingAtTheOrigin();

        camera.Zoom(factor);

        Assert.Equal(100f, camera.DesiredPose.Distance, 3);
    }

    [Fact]
    public void PanWorld_SlidesTheTargetWithoutChangingTheAngleOrDistance()
    {
        var camera = LookingAtTheOrigin();
        var before = camera.DesiredPose;

        camera.PanWorld(10f, 5f);

        Assert.NotEqual(before.Target, camera.DesiredPose.Target);
        Assert.Equal(before.YawDegrees, camera.DesiredPose.YawDegrees, 4);
        Assert.Equal(before.Distance, camera.DesiredPose.Distance, 4);
    }

    [Fact]
    public void ScreenPointToRay_StartsOnTheNearPlane_AndPointsWhereTheCameraLooks()
    {
        var camera = LookingAtTheOrigin();

        var ray = camera.ScreenPointToRay(500f, 400f, 1000f, 800f);

        Assert.Equal(1f, ray.Direction.Length(), 3);

        // The ray starts on the near plane rather than at the eye, so it is the near plane's distance in front of it.
        Assert.Equal(camera.NearPlaneFor(camera.Pose), Vector3.Distance(ray.Origin, camera.Position), 2);

        // A ray through the middle of the viewport heads towards what the camera is looking at.
        Assert.True(Vector3.Dot(Vector3.Normalize(camera.Pose.Target - camera.Position), ray.Direction) > 0.99f);
    }

    [Fact]
    public void TheNearPlane_StaysPut_CloseIn()
    {
        var camera = new OrbitCamera();
        camera.SetPose(new CameraPose(Vector3.Zero, 0f, 30f, 20f), immediate: true);

        Assert.Equal(camera.NearPlane, camera.NearPlaneFor(camera.Pose));
    }

    [Fact]
    public void TheNearPlane_FollowsTheCameraOut_ButNeverCutsAwayWhatIsBelowIt()
    {
        var camera = new OrbitCamera();
        camera.Constraints.MaxDistance = 2000f;
        camera.SetPose(new CameraPose(Vector3.Zero, 0f, 8f, 2000f), immediate: true);

        var near = camera.NearPlaneFor(camera.Pose);

        Assert.True(near > camera.NearPlane * 10f, $"near plane {near}");
        Assert.True(near <= camera.Position.Y * 0.25f + 1e-3f, $"near plane {near} against an eye {camera.Position.Y} m up");
    }

    /// <summary>
    /// Zoomed all the way out, a quay a meter above the water lands many steps of a 24-bit depth buffer away from the
    /// water beneath it — at the marina, and on the mainland far behind it — so the two cannot fight. With the near plane
    /// fixed at half a meter they were barely a step apart, and the water showed through the land in stripes.
    /// </summary>
    /// <remarks>
    /// Looking lower still, towards the horizon, a meter of height is a sliver of depth whatever the planes; there the
    /// renderers' depth bias on the water pass, which grows with the slope of the surface, keeps the land in front.
    /// </remarks>
    [Theory]
    [InlineData(0f)]        // the marina itself, where the camera looks
    [InlineData(-3000f)]    // the mainland, far off behind it
    public void ZoomedAllTheWayOut_TheLandAndTheWaterStayApartInDepth(float along)
    {
        var camera = new OrbitCamera { FarPlane = 8000f };
        camera.Constraints.MaxDistance = 2000f;
        camera.SetPose(new CameraPose(Vector3.Zero, 0f, 30f, 2000f), immediate: true);
        var view = camera.GetViewMatrix();

        // The eye is on +Z, so it looks towards -Z.
        var water = new Vector3(0f, 0f, along);
        var quay = new Vector3(0f, 1f, along);

        Assert.True(DepthStepsApart(view, water, quay, 0.5f, camera.FarPlane) < 2.0, "the old fixed near plane already kept them apart");
        var apart = DepthStepsApart(view, water, quay, camera.NearPlaneFor(camera.Pose), camera.FarPlane);
        Assert.True(apart > 8.0, $"only {apart:0.00} depth steps apart");
    }

    /// <summary>
    /// How many steps of a 24-bit depth buffer lie between two points, for a perspective projection with these planes.
    /// Worked out in double precision: in single precision the depth near the far end is no finer than the buffer.
    /// </summary>
    private static double DepthStepsApart(Matrix4x4 view, Vector3 a, Vector3 b, double near, double far)
    {
        double Depth(Vector3 point)
        {
            var distance = -(double)Vector3.Transform(point, view).Z;   // the camera looks down -Z in view space
            return (far + near) / (far - near) - 2.0 * far * near / ((far - near) * distance);
        }

        const double step = 2.0 / (1 << 24);   // NDC depth runs -1..1 across 2^24 steps
        return Math.Abs(Depth(a) - Depth(b)) / step;
    }

    [Fact]
    public void WorldToScreen_AndBack_AgreeAtTheCentreOfTheViewport()
    {
        var camera = LookingAtTheOrigin();

        var screen = camera.WorldToScreen(camera.Pose.Target, 1000f, 800f);

        Assert.NotNull(screen);
        Assert.Equal(500f, screen.Value.X, 0);
        Assert.Equal(400f, screen.Value.Y, 0);
    }

    [Fact]
    public void WorldToScreen_ReturnsNull_ForAPointBehindTheCamera()
    {
        var camera = LookingAtTheOrigin();

        var behind = camera.Position + (camera.Position - camera.Pose.Target) * 2f;

        Assert.Null(camera.WorldToScreen(behind, 1000f, 800f));
    }
}
