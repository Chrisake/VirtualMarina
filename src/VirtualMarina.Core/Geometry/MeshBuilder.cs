using System.Numerics;

namespace VirtualMarina.Core.Geometry;

/// <summary>
/// Builds flat-shaded low-poly meshes from convex primitives (boxes, prisms, cylinders, cones, spheres).
/// </summary>
/// <remarks>
/// Every face gets its own vertices so normals stay faceted. Triangle winding is computed so that
/// front faces point away from each primitive's interior; renderers can therefore enable or disable
/// back-face culling freely.
/// </remarks>
public sealed class MeshBuilder
{
    private readonly List<float> _vertices = new();
    private readonly List<uint> _indices = new();

    /// <summary>Vertices added so far.</summary>
    public int VertexCount => _vertices.Count / MeshData.VertexStride;

    /// <summary>Adds a flat-shaded triangle (normal from the winding a → b → c). Degenerate triangles are skipped.</summary>
    public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 color)
    {
        var normal = Vector3.Cross(b - a, c - a);
        var length = normal.Length();
        if (length < 1e-9f) return; // Degenerate (e.g. cone apex); nothing to draw.
        normal /= length;

        var start = (uint)VertexCount;
        AddVertex(a, normal, color);
        AddVertex(b, normal, color);
        AddVertex(c, normal, color);
        _indices.Add(start);
        _indices.Add(start + 1);
        _indices.Add(start + 2);
    }

    /// <summary>Adds a triangle wound so its normal points away from <paramref name="interior"/>.</summary>
    public void AddTriangleFacingAway(Vector3 a, Vector3 b, Vector3 c, Vector3 color, Vector3 interior)
    {
        var normal = Vector3.Cross(b - a, c - a);
        var centroid = (a + b + c) / 3f;
        if (Vector3.Dot(normal, centroid - interior) < 0f)
        {
            AddTriangle(a, c, b, color);
        }
        else
        {
            AddTriangle(a, b, c, color);
        }
    }

    /// <summary>
    /// Connects two closed loops with the same vertex count (a convex "loft") and optionally caps each end.
    /// Covers boxes, prisms, frustums, hulls, cones and cylinders.
    /// </summary>
    public void AddLoft(
        IReadOnlyList<Vector3> loopA, IReadOnlyList<Vector3> loopB,
        Vector3 sideColor, Vector3? capAColor, Vector3? capBColor, Vector3? interior = null)
    {
        if (loopA.Count != loopB.Count || loopA.Count < 3)
        {
            throw new ArgumentException("Loops must have the same number of points (at least 3).");
        }

        var n = loopA.Count;
        var inside = interior ?? (Average(loopA) + Average(loopB)) * 0.5f;

        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            AddTriangleFacingAway(loopA[i], loopA[j], loopB[j], sideColor, inside);
            AddTriangleFacingAway(loopA[i], loopB[j], loopB[i], sideColor, inside);
        }

        if (capAColor.HasValue) AddCap(loopA, capAColor.Value, inside);
        if (capBColor.HasValue) AddCap(loopB, capBColor.Value, inside);
    }

    /// <summary>Axis-aligned box.</summary>
    public void AddBox(Vector3 center, Vector3 size, Vector3 color)
    {
        var h = size * 0.5f;
        var bottom = new[]
        {
            center + new Vector3(-h.X, -h.Y, -h.Z),
            center + new Vector3(h.X, -h.Y, -h.Z),
            center + new Vector3(h.X, -h.Y, h.Z),
            center + new Vector3(-h.X, -h.Y, h.Z),
        };
        var top = bottom.Select(p => p + new Vector3(0f, size.Y, 0f)).ToArray();
        AddLoft(bottom, top, color, color, color, center);
    }

    /// <summary>Extrudes a (y, z) profile polygon across X from <paramref name="x0"/> to <paramref name="x1"/>.</summary>
    public void AddPrismX(float x0, float x1, IReadOnlyList<(float Y, float Z)> profile, Vector3 color)
    {
        var a = profile.Select(p => new Vector3(x0, p.Y, p.Z)).ToArray();
        var b = profile.Select(p => new Vector3(x1, p.Y, p.Z)).ToArray();
        AddLoft(a, b, color, color, color);
    }

    /// <summary>Cylinder or cone frustum between two arbitrary points.</summary>
    public void AddCylinder(Vector3 from, Vector3 to, float radiusFrom, float radiusTo, int sides, Vector3 color, bool caps = true)
    {
        var axis = to - from;
        var length = axis.Length();
        if (length < 1e-6f) return;
        axis /= length;

        var helper = MathF.Abs(axis.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(axis, helper));
        var w = Vector3.Cross(axis, u);

        var a = new Vector3[sides];
        var b = new Vector3[sides];
        for (var i = 0; i < sides; i++)
        {
            var angle = MathF.Tau * i / sides;
            var radial = u * MathF.Cos(angle) + w * MathF.Sin(angle);
            a[i] = from + radial * radiusFrom;
            b[i] = to + radial * radiusTo;
        }

        AddLoft(a, b, color, caps ? color : null, caps ? color : null, (from + to) * 0.5f);
    }

    /// <summary>Low-poly UV sphere.</summary>
    /// <param name="center">Center.</param>
    /// <param name="radius">Radius.</param>
    /// <param name="segments">Divisions around the Y axis.</param>
    /// <param name="rings">Divisions from bottom to top.</param>
    /// <param name="color">Vertex color.</param>
    public void AddSphere(Vector3 center, float radius, int segments, int rings, Vector3 color)
    {
        Vector3[]? previous = null;
        for (var r = 0; r <= rings; r++)
        {
            var latitude = -MathF.PI / 2f + MathF.PI * r / rings;
            var y = MathF.Sin(latitude) * radius;
            var ringRadius = MathF.Cos(latitude) * radius;
            var ring = new Vector3[segments];
            for (var s = 0; s < segments; s++)
            {
                var angle = MathF.Tau * s / segments;
                ring[s] = center + new Vector3(MathF.Cos(angle) * ringRadius, y, MathF.Sin(angle) * ringRadius);
            }

            if (previous is not null) AddLoft(previous, ring, color, null, null, center);
            previous = ring;
        }
    }

    /// <summary>A thin triangular panel (e.g. a sail) with the given thickness.</summary>
    public void AddTriangularPlate(Vector3 a, Vector3 b, Vector3 c, float thickness, Vector3 color)
    {
        var normal = Vector3.Cross(b - a, c - a);
        if (normal.LengthSquared() < 1e-12f) return;
        var offset = Vector3.Normalize(normal) * (thickness * 0.5f);
        AddLoft(new[] { a - offset, b - offset, c - offset }, new[] { a + offset, b + offset, c + offset }, color, color, color);
    }

    /// <summary>Flat horizontal quad facing +Y (for markers laid on the water).</summary>
    public void AddQuadUp(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 color)
    {
        var below = (a + b + c + d) / 4f - Vector3.UnitY;
        AddTriangleFacingAway(a, b, c, color, below);
        AddTriangleFacingAway(a, c, d, color, below);
    }

    /// <summary>Creates the mesh from everything added so far.</summary>
    public MeshData Build(int id, string name, bool isWater = false) =>
        new(id, name, _vertices.ToArray(), _indices.ToArray(), isWater);

    private void AddCap(IReadOnlyList<Vector3> loop, Vector3 color, Vector3 interior)
    {
        var center = Average(loop);
        for (var i = 0; i < loop.Count; i++)
        {
            AddTriangleFacingAway(center, loop[i], loop[(i + 1) % loop.Count], color, interior);
        }
    }

    private void AddVertex(Vector3 p, Vector3 n, Vector3 c)
    {
        _vertices.Add(p.X);
        _vertices.Add(p.Y);
        _vertices.Add(p.Z);
        _vertices.Add(n.X);
        _vertices.Add(n.Y);
        _vertices.Add(n.Z);
        _vertices.Add(c.X);
        _vertices.Add(c.Y);
        _vertices.Add(c.Z);
    }

    private static Vector3 Average(IReadOnlyList<Vector3> points)
    {
        var sum = Vector3.Zero;
        foreach (var p in points) sum += p;
        return sum / points.Count;
    }
}
