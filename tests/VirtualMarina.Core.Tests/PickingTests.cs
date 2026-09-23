using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Picking;

namespace VirtualMarina.Core.Tests;

/// <summary>Picking uses the boats' actual triangles, not their bounding boxes.</summary>
public class PickingTests
{
    /// <summary>Two sailboats side by side in A-L01 (front, z ≈ 4.5) and A-L02 (behind it along +Z, z ≈ 9.5).</summary>
    private static MarinaVisualizer CreateMarinaWithTwoSailboats()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(new MarinaLayoutBuilder("Picking")
            .AddPier("A", "Pier A", Vector2.Zero, 0f, 40f, pier => pier.AddBerths(PierSide.Left, 2, 5f, 12f))
            .Build());
        marina.SetViewportSize(800, 600);
        foreach (var id in new[] { "A-L01", "A-L02" })
        {
            marina.AssignBoat(id, new Boat(id + "-boat", "Sail " + id, BoatType.MonohullSailboat) { LengthMeters = 11f, BeamMeters = 3.6f });
        }

        return marina;
    }

    private static List<BoatInstance> Boats(MarinaVisualizer marina) =>
        BerthPlacement.EnumerateBoats(marina.GetBerths(), marina.GetBerth, marina.GetMultiBerth, BerthStatusFilter.All).ToList();

    private static bool HitsBox(BoatInstance boat, MeshLibrary meshes, Ray ray, out MeshData mesh, out Vector3 origin, out Vector3 direction)
    {
        mesh = meshes.Get(MeshIds.ForBoat(boat.Boat.Type));
        Matrix4x4.Invert(boat.World, out var toLocal);
        origin = Vector3.Transform(ray.Origin, toLocal);
        direction = Vector3.TransformNormal(ray.Direction, toLocal);
        return mesh.Bounds.IntersectRay(origin, direction, out _);
    }

    /// <summary>
    /// Rays toward +Z across a grid of positions, looking slightly down like a camera above the water: they pass the
    /// front boat first, then the rear one (which appears higher on screen, e.g. its sail beside the front boat's mast).
    /// </summary>
    private static IEnumerable<Ray> RaysAlongTheRow(Berth front)
    {
        foreach (var slope in new[] { -0.15f, -0.3f, -0.5f })
        {
            var direction = Vector3.Normalize(new Vector3(0f, slope, 1f));
            for (var x = front.Center.X - front.Length * 0.5f; x <= front.Center.X + front.Length * 0.5f; x += 0.25f)
            {
                for (var y = 0.5f; y <= 16f; y += 0.25f)
                {
                    // Start the ray so that it is at height y where it reaches the front berth.
                    var start = new Vector3(x, y, front.Center.Y) - direction * 30f;
                    yield return new Ray(start, direction);
                }
            }
        }
    }

    [Fact]
    public void RayThroughTheFrontBoatsBoundingBox_ButNotItsShape_PicksTheBoatBehind()
    {
        var marina = CreateMarinaWithTwoSailboats();
        var boats = Boats(marina);
        var front = boats.Single(b => b.PrimaryBerth.Id == "A-L01");
        var rear = boats.Single(b => b.PrimaryBerth.Id == "A-L02");
        var cases = 0;

        foreach (var ray in RaysAlongTheRow(front.PrimaryBerth))
        {
            // The old behavior would pick the front boat here: its box is hit...
            if (!HitsBox(front, marina.Meshes, ray, out var frontMesh, out var fo, out var fd)) continue;
            // ... but the ray passes beside the mast and sails, and does hit the rear boat's shape.
            if (ScenePicker.TryIntersectMesh(frontMesh, fo, fd, out _)) continue;
            if (!HitsBox(rear, marina.Meshes, ray, out var rearMesh, out var ro, out var rd) || !ScenePicker.TryIntersectMesh(rearMesh, ro, rd, out _)) continue;

            // The rear berth must win: through its boat, or its water area when that is nearer than the hull under the waterline.
            var hit = ScenePicker.Pick(ray, marina.GetBerths(), boats, marina.Meshes);
            Assert.True(hit?.BerthId == "A-L02", $"Ray from ({ray.Origin.X}, {ray.Origin.Y}) should pick the rear berth, got {hit?.BerthId ?? "nothing"}.");
            cases++;
        }

        Assert.True(cases > 20, $"Expected many rays that miss the front boat's shape but cross its bounding box; found {cases}.");
    }

    [Fact]
    public void RayThroughTheFrontBoatsShape_StillPicksTheFrontBoat()
    {
        var marina = CreateMarinaWithTwoSailboats();
        var boats = Boats(marina);
        var front = boats.Single(b => b.PrimaryBerth.Id == "A-L01");
        var cases = 0;

        foreach (var ray in RaysAlongTheRow(front.PrimaryBerth))
        {
            if (!HitsBox(front, marina.Meshes, ray, out var mesh, out var o, out var d) || !ScenePicker.TryIntersectMesh(mesh, o, d, out _)) continue;

            Assert.Equal("A-L01", ScenePicker.Pick(ray, marina.GetBerths(), boats, marina.Meshes)?.BerthId);
            cases++;
        }

        Assert.True(cases > 20);
    }

    [Fact]
    public void Hover_OnlyTargetsBoatShapes()
    {
        var marina = CreateMarinaWithTwoSailboats();
        var berth = marina.GetBerth("A-L01")!;

        // Low camera from the side, looking along the boat at the height of the empty space beside the mast.
        marina.Camera.SetPose(new CameraPose(new Vector3(berth.Center.X, 9f, berth.Center.Y), 180f, 8f, 25f), immediate: true);
        var ray = marina.Camera.ScreenPointToRay(400, 300, 800, 600);
        var expected = ScenePicker.Pick(ray, marina.GetBerths().Where(s => s.IsVisible), Boats(marina), marina.Meshes)?.BerthId;

        marina.Input.PointerMove(400, 300);

        Assert.Equal(expected, marina.HoveredBerth?.Id);
        Assert.Equal(expected, marina.HitTest(400, 300)?.BerthId);
    }

    [Fact]
    public void TryIntersectMesh_ReportsTheNearestTriangle()
    {
        var builder = new MeshBuilder();
        builder.AddBox(new Vector3(0, 0, 0), Vector3.One, Vector3.One);        // front face at z = -0.5
        builder.AddBox(new Vector3(0, 0, 5), Vector3.One, Vector3.One);        // behind
        var mesh = builder.Build(999, "boxes");

        Assert.True(ScenePicker.TryIntersectMesh(mesh, new Vector3(0, 0, -10), Vector3.UnitZ, out var distance));
        Assert.Equal(9.5f, distance, 3);
        Assert.False(ScenePicker.TryIntersectMesh(mesh, new Vector3(3, 0, -10), Vector3.UnitZ, out _));
    }
}
