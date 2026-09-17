using System.Numerics;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Geometry;

/// <summary>World-space meshes for <see cref="LandArea"/> outlines: solid slabs for quays and lawns, rock piles for breakwaters.</summary>
public static class LandMeshFactory
{
    /// <summary>How far land walls reach below the water surface, in meters.</summary>
    public const float WallDepth = 3f;

    private static readonly Vector3 QuayTop = new(0.74f, 0.72f, 0.67f);
    private static readonly Vector3 QuayWall = new(0.62f, 0.60f, 0.56f);
    private static readonly Vector3 GrassTop = new(0.40f, 0.58f, 0.30f);
    private static readonly Vector3 GrassWall = new(0.47f, 0.40f, 0.30f);
    private static readonly Vector3 RockCore = new(0.26f, 0.26f, 0.25f);
    private static readonly Vector3 RockDark = new(0.42f, 0.41f, 0.39f);
    private static readonly Vector3 RockLight = new(0.64f, 0.62f, 0.58f);

    /// <summary>The mesh for a land area: a rock pile for <see cref="LandKind.Breakwater"/>, otherwise a solid slab.</summary>
    /// <param name="id">Mesh id (see <see cref="MeshIds.ForLand"/>).</param>
    /// <param name="area">The land area. Vertices are in world space, so the mesh is drawn with an identity transform.</param>
    public static MeshData Create(int id, LandArea area)
    {
        ArgumentNullException.ThrowIfNull(area);
        return area.Kind == LandKind.Breakwater ? CreateRockPile(id, area) : CreateSlab(id, area);
    }

    /// <summary>The outline extruded from <see cref="WallDepth"/> below the water up to the land height, with a flat top.</summary>
    public static MeshData CreateSlab(int id, LandArea area)
    {
        ArgumentNullException.ThrowIfNull(area);
        var (top, wall) = area.Kind == LandKind.Grass ? (GrassTop, GrassWall) : (QuayTop, QuayWall);
        var b = new MeshBuilder();
        AddPrism(b, area.Points, -WallDepth, area.Height, top, wall);
        return b.Build(id, $"Land:{area.Id}");
    }

    /// <summary>
    /// A rubble mound: the area filled with irregular rocks (low-poly squashed spheres), reaching the land height in the
    /// middle and sloping down to the water along the outline, over a dark core that hides the gaps between rocks.
    /// </summary>
    public static MeshData CreateRockPile(int id, LandArea area)
    {
        ArgumentNullException.ThrowIfNull(area);
        var points = area.Points;
        var b = new MeshBuilder();
        var random = new Random((int)(MarinaMath.StableHash01(area.Id) * int.MaxValue));

        var height = MathF.Max(area.Height, 0.3f);
        var spacing = Math.Clamp(height * 0.8f, 1.4f, 2.4f);
        var edgeHeight = MathF.Max(0.1f, height * 0.3f);

        // Core, kept below the rocks so it only shows through the gaps.
        AddPrism(b, points, -WallDepth, MathF.Max(-0.2f, edgeHeight - 0.35f), RockCore, RockCore);

        // Deepest point inside the outline sets how far the slope runs in.
        var (min, max) = PolygonMath.GetBounds(points);
        var samples = new List<(Vector2 Position, float Inset, bool Lower)>();
        for (var layer = 0; layer < 2; layer++)
        {
            var offset = layer * spacing * 0.5f;
            for (var y = min.Y + spacing * 0.5f + offset; y < max.Y; y += spacing)
            {
                for (var x = min.X + spacing * 0.5f + offset; x < max.X; x += spacing)
                {
                    var p = new Vector2(x, y) + Jitter(random, spacing * 0.3f);
                    if (!PolygonMath.Contains(points, p)) continue;
                    samples.Add((p, PolygonMath.DistanceToBoundary(points, p), layer == 1));
                }
            }
        }

        var deepest = samples.Count > 0 ? samples.Max(s => s.Inset) : 0f;
        var slope = MathF.Max(0.5f, MathF.Min(3f, deepest));

        foreach (var (position, inset, lower) in samples)
        {
            var t = SmoothStep(MathF.Min(inset / slope, 1f));
            var top = edgeHeight + (height - edgeHeight) * t;
            var radius = spacing * Lerp(0.5f, 0.68f, (float)random.NextDouble());
            // The second (offset) layer sits a little lower, filling the gaps of the first.
            AddRock(b, random, MarinaMath.ToWorld(position, top - radius * (lower ? 0.9f : 0.55f)), radius);
        }

        // A ring of rocks along the outline, at the waterline, covering the core's walls.
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var c = points[(i + 1) % points.Count];
            var length = Vector2.Distance(a, c);
            var count = Math.Max(1, (int)MathF.Ceiling(length / (spacing * 0.85f)));
            for (var k = 0; k < count; k++)
            {
                var along = Vector2.Lerp(a, c, (k + (float)random.NextDouble() * 0.5f) / count);
                var radius = spacing * Lerp(0.5f, 0.7f, (float)random.NextDouble());
                AddRock(b, random, MarinaMath.ToWorld(along, edgeHeight - radius * 0.6f), radius);
            }
        }

        return b.Build(id, $"Breakwater:{area.Id}");
    }

    /// <summary>Flat top and vertical walls of a (possibly concave) outline.</summary>
    private static void AddPrism(MeshBuilder b, IReadOnlyList<Vector2> points, float bottom, float top, Vector3 topColor, Vector3 wallColor)
    {
        foreach (var (i0, i1, i2) in PolygonMath.Triangulate(points))
        {
            var a = MarinaMath.ToWorld(points[i0], top);
            var c = MarinaMath.ToWorld(points[i1], top);
            var d = MarinaMath.ToWorld(points[i2], top);
            b.AddTriangleFacingAway(a, c, d, topColor, (a + c + d) / 3f - Vector3.UnitY);
        }

        // Outward normal of an edge a→c: to the right of the edge for counter-clockwise (positive area) outlines.
        var sign = PolygonMath.SignedArea(points) >= 0f ? 1f : -1f;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var c = points[(i + 1) % points.Count];
            var edge = c - a;
            if (edge.LengthSquared() < 1e-8f) continue;
            var outward = Vector2.Normalize(new Vector2(edge.Y, -edge.X)) * sign;
            var inside = MarinaMath.ToWorld((a + c) * 0.5f - outward, (top + bottom) * 0.5f);

            var a0 = MarinaMath.ToWorld(a, bottom);
            var a1 = MarinaMath.ToWorld(a, top);
            var c0 = MarinaMath.ToWorld(c, bottom);
            var c1 = MarinaMath.ToWorld(c, top);
            b.AddTriangleFacingAway(a0, c0, c1, wallColor, inside);
            b.AddTriangleFacingAway(a0, c1, a1, wallColor, inside);
        }
    }

    /// <summary>An irregular, flattened low-poly ball.</summary>
    private static void AddRock(MeshBuilder b, Random random, Vector3 center, float radius)
    {
        const int segments = 6;
        const int rings = 3;
        var radii = new Vector3(
            radius * Lerp(0.9f, 1.25f, (float)random.NextDouble()),
            radius * Lerp(0.6f, 0.85f, (float)random.NextDouble()),
            radius * Lerp(0.9f, 1.25f, (float)random.NextDouble()));
        var yaw = (float)random.NextDouble() * MathF.Tau;
        var color = Vector3.Lerp(RockDark, RockLight, (float)random.NextDouble()) *
            new Vector3(1f, 1f, Lerp(0.94f, 1.02f, (float)random.NextDouble()));

        Vector3[]? previous = null;
        for (var r = 0; r <= rings; r++)
        {
            var latitude = -MathF.PI / 2f + MathF.PI * r / rings;
            var ring = new Vector3[segments];
            for (var s = 0; s < segments; s++)
            {
                var angle = yaw + MathF.Tau * s / segments;
                var bump = r == 0 || r == rings ? 1f : Lerp(0.82f, 1.12f, (float)random.NextDouble());
                ring[s] = center + new Vector3(
                    MathF.Cos(latitude) * MathF.Cos(angle) * radii.X * bump,
                    MathF.Sin(latitude) * radii.Y,
                    MathF.Cos(latitude) * MathF.Sin(angle) * radii.Z * bump);
            }

            if (previous is not null) b.AddLoft(previous, ring, color, null, null, center);
            previous = ring;
        }
    }

    private static Vector2 Jitter(Random random, float amount) =>
        new(((float)random.NextDouble() * 2f - 1f) * amount, ((float)random.NextDouble() * 2f - 1f) * amount);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float SmoothStep(float t) => t * t * (3f - 2f * t);
}
