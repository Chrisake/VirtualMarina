using System.Numerics;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Picking;

/// <summary>
/// CPU ray casting against berth footprints and the actual triangles of boat models. Works the same on every backend
/// and needs no GPU read-back.
/// </summary>
/// <remarks>
/// A boat is hit only where the ray touches its geometry (hull, cabin, mast, sails), not its bounding box, so a tall
/// boat in front doesn't steal clicks meant for the boat visible behind it. The bounding box is only used to skip
/// boats the ray can't touch.
/// </remarks>
internal static class ScenePicker
{
    /// <param name="ray">World-space pick ray.</param>
    /// <param name="berths">Berths whose pads can be hit (visible and not filtered out).</param>
    /// <param name="boats">Boats that can be hit.</param>
    /// <param name="meshes">Boat meshes, tested triangle by triangle.</param>
    /// <param name="groundHeight">Land height of a land berth (null for water berths); when null every pad is on the water.</param>
    public static BerthHit? Pick(Ray ray, IEnumerable<Berth> berths, IEnumerable<BoatInstance> boats, MeshLibrary meshes, Func<Berth, float?>? groundHeight = null)
    {
        BerthHit? best = null;

        // 1. Berth areas (the colored pads), on the water or on land.
        foreach (var berth in berths)
        {
            var padHeight = BerthPlacement.PadHeightFor(groundHeight?.Invoke(berth));
            if (!ray.IntersectHorizontalPlane(padHeight, out var padDistance) || (best is not null && padDistance >= best.Value.Distance)) continue;

            var point = ray.GetPoint(padDistance);
            if (berth.Bounds.Contains(MarinaMath.ToPlan(point)))
            {
                best = new BerthHit(berth.Id, padDistance, point, HitBoat: false);
            }
        }

        // 2. Boats, which may rise far above the pads and overhang neighbouring berths.
        foreach (var boat in boats)
        {
            if (!meshes.TryGet(MeshIds.ForBoat(boat.Boat.Type), out var mesh) ||
                !Matrix4x4.Invert(boat.World, out var toLocal))
            {
                continue;
            }

            // The local direction isn't normalized, so distances along it equal world distances along the world ray.
            var localOrigin = Vector3.Transform(ray.Origin, toLocal);
            var localDirection = Vector3.TransformNormal(ray.Direction, toLocal);

            // Broad phase: skip boats whose bounding box is missed or can't beat the current best hit.
            if (!mesh.Bounds.IntersectRay(localOrigin, localDirection, out var boxDistance) ||
                (best is not null && boxDistance >= best.Value.Distance))
            {
                continue;
            }

            if (TryIntersectMesh(mesh, localOrigin, localDirection, out var boatDistance) &&
                (best is null || boatDistance < best.Value.Distance))
            {
                var hitPoint = ray.GetPoint(boatDistance);
                best = new BerthHit(ResolveBerth(boat, MarinaMath.ToPlan(hitPoint)), boatDistance, hitPoint, HitBoat: true);
            }
        }

        return best;
    }

    /// <summary>Nearest intersection of a ray with any triangle of the mesh (both faces count; sails are thin plates).</summary>
    internal static bool TryIntersectMesh(MeshData mesh, Vector3 origin, Vector3 direction, out float distance)
    {
        distance = float.MaxValue;
        var vertices = mesh.Vertices;
        var indices = mesh.Indices;
        var stride = MeshData.VertexStride;

        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            var a = Position(vertices, (int)indices[i] * stride);
            var b = Position(vertices, (int)indices[i + 1] * stride);
            var c = Position(vertices, (int)indices[i + 2] * stride);
            if (IntersectTriangle(origin, direction, a, b, c, out var t) && t < distance) distance = t;
        }

        return distance < float.MaxValue;
    }

    /// <summary>Möller–Trumbore ray/triangle intersection, no back-face culling. <paramref name="t"/> is in units of <paramref name="direction"/>.</summary>
    private static bool IntersectTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        const float epsilon = 1e-7f;
        t = 0f;
        var edge1 = b - a;
        var edge2 = c - a;
        var p = Vector3.Cross(direction, edge2);
        var determinant = Vector3.Dot(edge1, p);
        if (MathF.Abs(determinant) < epsilon) return false; // parallel to the triangle

        var inverse = 1f / determinant;
        var s = origin - a;
        var u = Vector3.Dot(s, p) * inverse;
        if (u < 0f || u > 1f) return false;

        var q = Vector3.Cross(s, edge1);
        var v = Vector3.Dot(direction, q) * inverse;
        if (v < 0f || u + v > 1f) return false;

        t = Vector3.Dot(edge2, q) * inverse;
        return t > epsilon;
    }

    private static Vector3 Position(float[] vertices, int offset) => new(vertices[offset], vertices[offset + 1], vertices[offset + 2]);

    /// <summary>For a boat spanning several berths, the visible member berth nearest the hit point (interactive ones first).</summary>
    private static string ResolveBerth(BoatInstance boat, Vector2 plan)
    {
        if (boat.Berths.Count == 1) return boat.PrimaryBerth.Id;

        return boat.Berths
            .Where(s => s.IsVisible)
            .OrderBy(s => s.IsInteractive ? 0 : 1)
            .ThenBy(s => s.Bounds.Contains(plan) ? 0 : 1)
            .ThenBy(s => Vector2.DistanceSquared(s.Center, plan))
            .Select(s => s.Id)
            .DefaultIfEmpty(boat.PrimaryBerth.Id)
            .First();
    }
}
