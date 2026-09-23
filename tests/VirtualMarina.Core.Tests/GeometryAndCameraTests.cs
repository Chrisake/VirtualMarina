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


    /// <summary>The middle of the marina in plan coordinates, and how far across it is.</summary>
    private static (Vector2 Middle, float Span) MarinaMiddle(MarinaVisualizer marina)
    {
        var (min, max) = marina.GetLayout().ComputeBounds();
        return ((min + max) * 0.5f, Vector2.Distance(min, max) * 0.5f);
    }

    /// <summary>Where a set of world points lands on screen, or false when one is behind the camera.</summary>
    private static bool TryScreenBounds(MarinaVisualizer marina, IEnumerable<Vector3> points, out (Vector2 Min, Vector2 Max) bounds)
    {
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var point in points)
        {
            if (!marina.TryProjectToScreen(point, out var screen))
            {
                bounds = default;
                return false;
            }

            min = Vector2.Min(min, screen);
            max = Vector2.Max(max, screen);
        }

        bounds = (min, max);
        return true;
    }

    [Fact]
    public void TheCompassViews_StandOnTheSideTheyAreNamedAfter()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());
        marina.SetViewportSize(1400f, 900f);

        var (min, max) = marina.GetLayout().ComputeBounds();
        var middle = (min + max) * 0.5f;
        var across = MathF.Max(max.X - min.X, max.Y - min.Y) * 0.25f;

        // North is −Y in plan, so a view "from the north" puts the camera at a smaller Y than the marina. Getting
        // this backwards is not visible in a screenshot of the framing, which is why it went unnoticed: the marina
        // fills the view either way, seen from the wrong side.
        var expected = new (string Name, float X, float Y)[]
        {
            ("North", 0f, -1f),
            ("East", 1f, 0f),
            ("South", 0f, 1f),
            ("West", -1f, 0f),
        };

        foreach (var (name, x, y) in expected)
        {
            Assert.True(marina.ApplyBuiltInCameraPreset(name, immediate: true), $"no automatic view called {name}");

            var eye = MarinaMath.ToPlan(marina.Camera.Position);
            var offset = eye - middle;

            if (x != 0f) Assert.True(MathF.Sign(offset.X) == MathF.Sign(x) && MathF.Abs(offset.X) > across, $"{name} does not stand to the {(x > 0 ? "east" : "west")}");
            else Assert.True(MathF.Abs(offset.X) < across, $"{name} is off to one side rather than due {name.ToLowerInvariant()}");

            if (y != 0f) Assert.True(MathF.Sign(offset.Y) == MathF.Sign(y) && MathF.Abs(offset.Y) > across, $"{name} does not stand to the {(y > 0 ? "south" : "north")}");
            else Assert.True(MathF.Abs(offset.Y) < across, $"{name} is off to one side rather than due {name.ToLowerInvariant()}");
        }
    }

    [Fact]
    public void TheTopDownView_HasNorthAtTheTopOfTheScreen()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());
        marina.SetViewportSize(1400f, 900f);
        Assert.True(marina.ApplyBuiltInCameraPreset(MarinaVisualizer.TopDownPresetName, immediate: true));

        var (min, max) = marina.GetLayout().ComputeBounds();
        var middle = (min + max) * 0.5f;
        var step = MathF.Max(max.X - min.X, max.Y - min.Y) * 0.25f;

        Assert.True(marina.TryProjectToScreen(MarinaMath.ToWorld(middle), out var centre));
        Assert.True(marina.TryProjectToScreen(MarinaMath.ToWorld(middle - new Vector2(0f, step)), out var north));
        Assert.True(marina.TryProjectToScreen(MarinaMath.ToWorld(middle + new Vector2(step, 0f)), out var east));

        // Screen Y grows downward, so north being up means a smaller Y than the middle.
        Assert.True(north.Y < centre.Y, "north is not at the top of the screen");
        Assert.True(east.X > centre.X, "east is not to the right of the screen");
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

        // They really do come from four different sides, and every one of them looks at the marina. Not at exactly
        // the same point: seen from an angle the middle of the marina does not land in the middle of the picture, so
        // each view aims a little off it to put the marina in the frame, by its own amount.
        var compass = new[] { "North", "East", "South", "West" };
        var (middle, span) = MarinaMiddle(marina);
        Assert.All(compass, name =>
        {
            var aim = marina.CameraPresets.Single(p => p.Name == name).Pose.Target;
            Assert.True(Vector2.Distance(MarinaMath.ToPlan(aim), middle) < span, $"{name} does not look at the marina");
        });

        Assert.Equal(4, compass.Select(name => marina.CameraPresets.Single(p => p.Name == name).Pose.YawDegrees).Distinct().Count());

        // And every one of them holds the *whole* marina, in a tall window and a wide one alike: the berths, the
        // piers, and every corner of every quay and breakwater. Framing a box around all of that is not the same
        // thing — a marina that bends leaves the box corners out in open water, and centring those pushes the
        // marina itself off to one side.
        var marinaPoints = marina.GetBerths().Select(berth => MarinaMath.ToWorld(berth.Center))
            .Concat(marina.GetPiers().SelectMany(pier => new[] { MarinaMath.ToWorld(pier.Start), MarinaMath.ToWorld(pier.End) }))
            .Concat(marina.GetLandAreas().SelectMany(land => land.Points).Select(point => MarinaMath.ToWorld(point)))
            .ToArray();

        var views = compass.Concat(new[] { MarinaVisualizer.OverviewPresetName, MarinaVisualizer.TopDownPresetName }).ToArray();

        foreach (var (width, height) in new[] { (1400f, 900f), (1080f, 800f), (700f, 900f), (1900f, 600f) })
        {
            marina.SetViewportSize(width, height);
            foreach (var name in views)
            {
                Assert.True(marina.ApplyBuiltInCameraPreset(name, immediate: true), $"no automatic view called {name}");
                Assert.True(TryScreenBounds(marina, marinaPoints, out var seen), $"{name} puts the marina behind the camera at {width}x{height}");

                Assert.InRange(seen.Min.X, 0f, width);
                Assert.InRange(seen.Min.Y, 0f, height);
                Assert.InRange(seen.Max.X, 0f, width);
                Assert.InRange(seen.Max.Y, 0f, height);

                // Worth looking at: the marina fills the view rather than sitting small in the middle of it. A view
                // across the long side of a marina in a narrow window cannot fill both directions, so this is the better
                // of the two. Framing the box instead of the outline drops this to under 60% on a diagonal marina.
                var fill = MathF.Max((seen.Max.X - seen.Min.X) / width, (seen.Max.Y - seen.Min.Y) / height);
                Assert.True(fill > 0.75f, $"{name} fills only {fill:P0} of a {width}x{height} view");

                // And it is roughly in the middle, not pushed into a corner.
                var offset = ((seen.Min + seen.Max) * 0.5f) - new Vector2(width * 0.5f, height * 0.5f);
                Assert.True(MathF.Abs(offset.X) < width * 0.1f, $"{name} pushes the marina {offset.X:0} px off centre sideways");
                Assert.True(MathF.Abs(offset.Y) < height * 0.1f, $"{name} pushes the marina {offset.Y:0} px off centre vertically");
            }
        }
    }

    [Fact]
    public void TheAutomaticViews_AreRefittedWhenTheMarinaGrows()
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(1400f, 900f);
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());

        // A preset is a snapshot of where the camera has to stand for the marina as it is now. Anything that holds
        // on to one past a layout change is holding a view of a marina that no longer exists, which is how the
        // designer's camera list ended up sending every automatic view to the middle of a marina many times smaller.
        var held = marina.CameraPresets.Where(preset => preset.IsBuiltIn).ToDictionary(preset => preset.Name);

        var (min, max) = marina.GetLayout().ComputeBounds();
        var far = new Vector2(max.X + 900f, max.Y + 900f);
        marina.AddLandArea(new LandArea("far", new[]
        {
            far,
            far + new Vector2(300f, 0f),
            far + new Vector2(300f, 300f),
            far + new Vector2(0f, 300f),
        }, 3f));

        var reach = marina.GetLandAreas().SelectMany(land => land.Points).Select(point => MarinaMath.ToWorld(point))
            .Concat(marina.GetBerths().Select(berth => MarinaMath.ToWorld(berth.Center)))
            .ToArray();

        foreach (var name in new[] { MarinaVisualizer.OverviewPresetName, MarinaVisualizer.TopDownPresetName, "North", "East", "South", "West" })
        {
            var now = marina.CameraPresets.Single(preset => preset.IsBuiltIn && preset.Name == name);
            Assert.True(now.Pose.Distance > held[name].Pose.Distance * 1.5f, $"{name} was not pulled back for the bigger marina");

            // Applying the one the marina offers now holds the lot; applying the one from before does not.
            Assert.True(marina.ApplyBuiltInCameraPreset(name, immediate: true));
            Assert.True(TryScreenBounds(marina, reach, out var seen), $"{name} puts the marina behind the camera");
            Assert.InRange(seen.Min.X, 0f, 1400f);
            Assert.InRange(seen.Max.X, 0f, 1400f);
            Assert.InRange(seen.Min.Y, 0f, 900f);
            Assert.InRange(seen.Max.Y, 0f, 900f);
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
