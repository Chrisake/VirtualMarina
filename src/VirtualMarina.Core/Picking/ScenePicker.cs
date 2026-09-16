using System.Numerics;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Picking;

/// <summary>
/// CPU ray casting against slip footprints and boat bounding boxes. Works the same on every backend
/// and needs no GPU read-back.
/// </summary>
internal static class ScenePicker
{
    /// <param name="ray">World-space pick ray.</param>
    /// <param name="slips">Slips whose pads can be hit (visible and not filtered out).</param>
    /// <param name="boats">Boats that can be hit.</param>
    /// <param name="meshes">Mesh bounds used for boat hit boxes.</param>
    public static SlipHit? Pick(Ray ray, IEnumerable<Slip> slips, IEnumerable<BoatInstance> boats, MeshLibrary meshes)
    {
        SlipHit? best = null;

        // 1. Slip water areas (the colored pads).
        if (ray.IntersectHorizontalPlane(SlipPlacement.PadHeight, out var padDistance))
        {
            var point = ray.GetPoint(padDistance);
            var plan = MarinaMath.ToPlan(point);
            foreach (var slip in slips)
            {
                if (slip.Bounds.Contains(plan) && (best is null || padDistance < best.Value.Distance))
                {
                    best = new SlipHit(slip.Id, padDistance, point, HitBoat: false);
                }
            }
        }

        // 2. Boats, which may rise far above the pads and overhang neighbouring slips.
        foreach (var boat in boats)
        {
            if (!meshes.TryGet(MeshIds.ForBoat(boat.Boat.Type), out var mesh) ||
                !Matrix4x4.Invert(boat.World, out var toLocal))
            {
                continue;
            }

            var localOrigin = Vector3.Transform(ray.Origin, toLocal);
            var localDirection = Vector3.TransformNormal(ray.Direction, toLocal);
            if (mesh.Bounds.IntersectRay(localOrigin, localDirection, out var boatDistance) &&
                (best is null || boatDistance < best.Value.Distance))
            {
                var hitPoint = ray.GetPoint(boatDistance);
                best = new SlipHit(ResolveSlip(boat, MarinaMath.ToPlan(hitPoint)), boatDistance, hitPoint, HitBoat: true);
            }
        }

        return best;
    }

    /// <summary>For a boat spanning several slips, the visible member slip nearest the hit point (interactive ones first).</summary>
    private static string ResolveSlip(BoatInstance boat, Vector2 plan)
    {
        if (boat.Slips.Count == 1) return boat.PrimarySlip.Id;

        return boat.Slips
            .Where(s => s.IsVisible)
            .OrderBy(s => s.IsInteractive ? 0 : 1)
            .ThenBy(s => s.Bounds.Contains(plan) ? 0 : 1)
            .ThenBy(s => Vector2.DistanceSquared(s.Center, plan))
            .Select(s => s.Id)
            .DefaultIfEmpty(boat.PrimarySlip.Id)
            .First();
    }
}
