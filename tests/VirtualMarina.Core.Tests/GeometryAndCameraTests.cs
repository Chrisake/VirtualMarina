using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Rendering;
using VirtualMarina.Core.Serialization;
using VirtualMarina.SampleData;

namespace VirtualMarina.Core.Tests;

public class GeometryAndCameraTests
{
    [Fact]
    public void Camera_ClampsPitchDistanceAndEyeHeight()
    {
        var camera = new OrbitCamera();
        camera.SetPose(new CameraPose(Vector3.Zero, 0f, -40f, 5000f), immediate: true);

        Assert.Equal(camera.Constraints.MinPitchDegrees, camera.Pose.PitchDegrees, 3);
        Assert.Equal(camera.Constraints.MaxDistance, camera.Pose.Distance, 3);

        camera.Orbit(0f, 500f);
        camera.Update(10f);
        Assert.True(camera.Pose.PitchDegrees < 90f, "camera must never flip over the top");

        camera.SetPose(new CameraPose(Vector3.Zero, 0f, 8f, 6f), immediate: true);
        Assert.True(camera.Position.Y >= camera.Constraints.MinEyeHeight - 1e-3f, "eye must stay above the water");
    }

    [Fact]
    public void Camera_SmoothlyApproachesDesiredPose()
    {
        var camera = new OrbitCamera();
        camera.SetPose(new CameraPose(Vector3.Zero, 0f, 45f, 100f), immediate: true);
        camera.SetPose(new CameraPose(new Vector3(50, 0, 0), 0f, 45f, 100f));

        camera.Update(0.05f);
        var partial = camera.Pose.Target.X;
        for (var i = 0; i < 200; i++) camera.Update(0.05f);

        Assert.InRange(partial, 1f, 49f);
        Assert.Equal(50f, camera.Pose.Target.X, 2);
    }

    [Fact]
    public void ScreenCenterRay_PointsAtTarget()
    {
        var camera = new OrbitCamera();
        camera.SetPose(new CameraPose(new Vector3(10, 0, -20), 30f, 50f, 80f), immediate: true);

        var ray = camera.ScreenPointToRay(640, 360, 1280, 720);
        Assert.True(ray.IntersectHorizontalPlane(0f, out var distance));
        var point = ray.GetPoint(distance);

        Assert.Equal(10f, point.X, 2);
        Assert.Equal(-20f, point.Z, 2);
    }

    [Fact]
    public void BerthGenerator_PlacesBerthsBesidePier_BowTowardPier()
    {
        var pier = new Pier("A", "A", Vector2.Zero, 0f, 50f, width: 2f);

        var right = BerthGenerator.AlongPier(pier, PierSide.Right, 2, 5f, 10f, startOffset: 0f);
        var left = BerthGenerator.AlongPier(pier, PierSide.Left, 1, 5f, 10f, startOffset: 0f);

        // Heading 0° runs along +Z: looking from the start toward the end, the right-hand side is −X.
        Assert.Equal("A-R01", right[0].Id);
        Assert.Equal(new Vector2(-6f, 2.5f), right[0].Center);
        Assert.Equal(new Vector2(-6f, 7.5f), right[1].Center);
        Assert.Equal(new Vector2(6f, 2.5f), left[0].Center);
        // Right-side berth bows point +X (toward the pier), left-side berth bows point −X.
        Assert.Equal(1f, right[0].Forward.X, 3);
        Assert.Equal(-1f, left[0].Forward.X, 3);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(37f)]
    [InlineData(180f)]
    [InlineData(-120f)]
    public void PierRight_IsTheRightHandSideLookingFromStartToEnd(float heading)
    {
        var pier = new Pier("A", "A", Vector2.Zero, heading, 10f);
        // In plan coordinates (X right, Y = world Z) seen from above with +Y up the page, a clockwise turn from the direction
        // gives the right-hand side: with world Z pointing down the screen (north up, yaw 0), right = (−dir.Y, dir.X).
        var direction = pier.Direction;
        var expected = new Vector2(-direction.Y, direction.X);
        Assert.Equal(expected.X, pier.Right.X, 4);
        Assert.Equal(expected.Y, pier.Right.Y, 4);

        // Heading 180° runs north (−Z, up the screen when north is up): its right-hand side is east (+X).
        if (heading == 180f) Assert.Equal(1f, pier.Right.X, 4);
    }

    [Fact]
    public void OrientedRect_ContainsRespectsRotation()
    {
        var rect = new OrientedRect(Vector2.Zero, new Vector2(2f, 10f), 90f); // length now runs along X

        Assert.True(rect.Contains(new Vector2(4.5f, 0f)));
        Assert.False(rect.Contains(new Vector2(0f, 4.5f)));
    }

    [Fact]
    public void MeshLibrary_ContainsValidMeshForEveryBoatType()
    {
        var library = MeshLibrary.CreateDefault(200f, 16, Vector2.Zero);

        foreach (var type in BoatTypeCatalog.All)
        {
            var mesh = library.Get(MeshIds.ForBoat(type));
            var nominal = BoatTypeCatalog.GetNominalDimensions(type);
            Assert.True(mesh.TriangleCount > 10, $"{type} should have geometry");
            Assert.InRange(mesh.Bounds.Size.Z, nominal.Length * 0.9f, nominal.Length * 1.15f);
        }

        foreach (var mesh in library.All)
        {
            Assert.Equal(0, mesh.Indices.Length % 3);
            Assert.All(mesh.Indices, i => Assert.True(i < mesh.VertexCount));
            Assert.DoesNotContain(mesh.Vertices, float.IsNaN);
        }
    }

    [Fact]
    public void ShaderSources_StartWithDialectVersion()
    {
        Assert.StartsWith("#version 330 core", ShaderSources.ModelVertex(ShaderDialect.DesktopGL33));
        Assert.StartsWith("#version 300 es", ShaderSources.WaterFragment(ShaderDialect.WebGL2));
    }

    [Fact]
    public void ColorRgba_ParsesHex()
    {
        var c = ColorRgba.FromHex("#3366FF");
        Assert.Equal("#3366FF", c.ToHex());
        Assert.Equal(1f, c.A);
    }

    [Fact]
    public void TheBuiltInViews_AreTheMarina_StraightDown_AndTheFourCompassPoints()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());

        var builtIn = marina.CameraPresets.Where(preset => preset.IsBuiltIn).Select(preset => preset.Name).ToList();
        Assert.Contains(MarinaVisualizer.OverviewPresetName, builtIn);
        Assert.Contains(MarinaVisualizer.TopDownPresetName, builtIn);
        foreach (var point in new[] { "North", "East", "South", "West" }) Assert.Contains(point, builtIn);

        // One per pier, on top of those.
        foreach (var pier in marina.GetPiers()) Assert.Contains($"Pier: {pier.Name}", builtIn);

        // Every compass view looks at the middle of the marina from far enough back to hold it.
        var overview = marina.CameraPresets.Single(preset => preset.Name == MarinaVisualizer.OverviewPresetName);
        foreach (var name in new[] { "North", "East", "South", "West" })
        {
            var preset = marina.CameraPresets.Single(p => p.Name == name);
            Assert.Equal(overview.Pose.Target, preset.Pose.Target);
            Assert.Equal(overview.Pose.Distance, preset.Pose.Distance, 1);
        }

        // They really do come from four different sides.
        var yaws = new[] { "North", "East", "South", "West" }
            .Select(name => marina.CameraPresets.Single(p => p.Name == name).Pose.YawDegrees)
            .ToList();
        Assert.Equal(4, yaws.Distinct().Count());
    }

    [Fact]
    public void AViewCanBeSwitchedOff_AndStaysOffThroughALayoutChangeAndAFile()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());

        Assert.All(marina.CameraPresets, preset => Assert.True(preset.IsEnabled));
        Assert.True(marina.SetCameraPresetEnabled("South", false));
        Assert.False(marina.SetCameraPresetEnabled("Nowhere", false));
        Assert.False(marina.CameraPresets.Single(p => p.Name == "South").IsEnabled);

        // Switched off is not the same as gone: it can still be applied by name on purpose.
        Assert.True(marina.ApplyCameraPreset("South", immediate: true));

        // Adding a pier rebuilds the built-in views; the choice survives.
        marina.AddPier(new Pier("Z", "Pier Z", new Vector2(200, -6), 0f, 30f));
        Assert.False(marina.CameraPresets.Single(p => p.Name == "South").IsEnabled);
        Assert.True(marina.CameraPresets.Single(p => p.Name == "North").IsEnabled);

        var copy = new MarinaVisualizer();
        MarinaDocument.Parse(MarinaDocument.FromVisualizer(marina, generator: "tests").ToJson()).ApplyTo(copy);
        Assert.False(copy.CameraPresets.Single(p => p.Name == "South").IsEnabled);
        Assert.True(copy.CameraPresets.Single(p => p.Name == "West").IsEnabled);
    }

    [Fact]
    public void TheDetailedWater_CanBeWidenedWhileTheMarinaIsOnScreen()
    {
        var marina = new MarinaVisualizer();
        Assert.Equal(4200f, marina.Water.Size, 1);

        var before = marina.BuildRenderFrame().WaterDetailRadius;
        marina.Water.Size = 9000f;

        var after = marina.BuildRenderFrame().WaterDetailRadius;
        Assert.Equal(4500f, after, 1);
        Assert.True(after > before);

        // Reading the frame again does not keep rebuilding the grid.
        Assert.Equal(after, marina.BuildRenderFrame().WaterDetailRadius, 1);
    }
}
