using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Mathematics;
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

        // They really do come from four different sides, all looking at the middle of the marina.
        var overview = marina.CameraPresets.Single(preset => preset.Name == MarinaVisualizer.OverviewPresetName);
        var compass = new[] { "North", "East", "South", "West" };
        Assert.All(compass, name => Assert.Equal(overview.Pose.Target, marina.CameraPresets.Single(p => p.Name == name).Pose.Target));
        Assert.Equal(4, compass.Select(name => marina.CameraPresets.Single(p => p.Name == name).Pose.YawDegrees).Distinct().Count());

        // And every one of them actually holds the marina, in a tall window and a wide one alike. Each works out its
        // own distance: a view along the marina needs less room than one across it.
        var (min, max) = marina.GetLayout().ComputeBounds();
        var corners = new[]
        {
            MarinaMath.ToWorld(min),
            MarinaMath.ToWorld(new Vector2(max.X, min.Y)),
            MarinaMath.ToWorld(max),
            MarinaMath.ToWorld(new Vector2(min.X, max.Y)),
        };

        foreach (var (width, height) in new[] { (1080f, 800f), (700f, 900f), (1900f, 600f) })
        {
            marina.SetViewportSize(width, height);
            foreach (var name in compass.Concat(new[] { MarinaVisualizer.OverviewPresetName, MarinaVisualizer.TopDownPresetName }))
            {
                Assert.True(marina.ApplyBuiltInCameraPreset(name, immediate: true), $"no automatic view called {name}");
                foreach (var corner in corners)
                {
                    Assert.True(marina.TryProjectToScreen(corner, out var screen), $"{name} puts a corner behind the camera at {width}x{height}");
                    Assert.InRange(screen.X, 0f, width);
                    Assert.InRange(screen.Y, 0f, height);
                }
            }
        }
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

    [Fact]
    public void ASavedView_MayBeNamedAfterAnAutomaticOne_AndBothStay()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());

        var automatic = marina.CameraPresets.Single(p => p.Name == "North");
        marina.Camera.SetPose(new CameraPose(new Vector3(12, 0, -40), 33f, 41f, 180f), immediate: true);
        var saved = marina.SaveCameraPreset("North", "My own north");

        // Two views called North now: the one the layout made, and the one the user made.
        var both = marina.CameraPresets.Where(p => p.Name == "North").ToList();
        Assert.Equal(2, both.Count);
        Assert.Single(both, p => p.IsBuiltIn);
        Assert.Single(both, p => !p.IsBuiltIn);
        Assert.Equal(automatic.Pose, both.Single(p => p.IsBuiltIn).Pose);

        // Either can be gone to, by handing over the one that is meant.
        marina.ApplyCameraPreset(both.Single(p => p.IsBuiltIn), immediate: true);
        Assert.Equal(automatic.Pose.Distance, marina.Camera.Pose.Distance, 1);

        marina.ApplyCameraPreset(both.Single(p => !p.IsBuiltIn), immediate: true);
        Assert.Equal(180f, marina.Camera.Pose.Distance, 1);

        // Asked for by name alone, the one the user saved wins.
        marina.ApplyCameraPreset(MarinaVisualizer.OverviewPresetName, immediate: true);
        Assert.True(marina.ApplyCameraPreset("North", immediate: true));
        Assert.Equal(180f, marina.Camera.Pose.Distance, 1);

        // Ticking the automatic one off does not touch the saved one.
        Assert.True(marina.SetCameraPresetEnabled("North", false));
        Assert.False(marina.CameraPresets.Single(p => p.Name == "North" && p.IsBuiltIn).IsEnabled);
        Assert.True(marina.CameraPresets.Single(p => p.Name == "North" && !p.IsBuiltIn).IsEnabled);

        // Deleting the saved one leaves the automatic one alone.
        Assert.True(marina.RemoveCameraPreset("North"));
        Assert.Single(marina.CameraPresets, p => p.Name == "North");
        Assert.True(marina.CameraPresets.Single(p => p.Name == "North").IsBuiltIn);
        Assert.Equal("My own north", saved.Description);
    }

    [Fact]
    public void SavedViews_TravelInTheConfigurationFile_ForTheHostToOffer()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());
        marina.Camera.SetPose(new CameraPose(new Vector3(8, 0, -14), 120f, 30f, 95f), immediate: true);
        marina.SaveCameraPreset("Fuel dock", "By the pumps");
        marina.SaveCameraPreset("North");                       // shares its name with an automatic one
        marina.SetCameraPresetEnabled("West", false);

        var json = MarinaDocument.FromVisualizer(marina, generator: "tests").ToJson();
        Assert.Contains("\"Fuel dock\"", json);

        var copy = new MarinaVisualizer();
        MarinaDocument.Parse(json).ApplyTo(copy);

        var fuel = copy.CameraPresets.Single(p => p.Name == "Fuel dock");
        Assert.False(fuel.IsBuiltIn);
        Assert.Equal("By the pumps", fuel.Description);
        Assert.Equal(95f, fuel.Pose.Distance, 1);

        // The automatic North came back from the layout, and the saved North came back from the file.
        Assert.Equal(2, copy.CameraPresets.Count(p => p.Name == "North"));
        Assert.False(copy.CameraPresets.Single(p => p.Name == "West").IsEnabled);
    }
}
