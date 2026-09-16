using System.Numerics;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Rendering;

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
    public void SlipGenerator_PlacesSlipsBesideDock_BowTowardDock()
    {
        var dock = new Dock("A", "A", Vector2.Zero, 0f, 50f, width: 2f);

        var right = SlipGenerator.AlongDock(dock, DockSide.Right, 2, 5f, 10f, startOffset: 0f);
        var left = SlipGenerator.AlongDock(dock, DockSide.Left, 1, 5f, 10f, startOffset: 0f);

        Assert.Equal("A-R01", right[0].Id);
        Assert.Equal(new Vector2(6f, 2.5f), right[0].Center);
        Assert.Equal(new Vector2(6f, 7.5f), right[1].Center);
        Assert.Equal(new Vector2(-6f, 2.5f), left[0].Center);
        // Right-side slip bows point -X (toward the dock), left-side slip bows point +X.
        Assert.Equal(-1f, right[0].Forward.X, 3);
        Assert.Equal(1f, left[0].Forward.X, 3);
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
}
