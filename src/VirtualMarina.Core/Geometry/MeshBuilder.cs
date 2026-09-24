using System.Numerics;

namespace VirtualMarina.Core.Geometry;

/// <summary>
/// Builds flat-shaded low-poly meshes from convex primitives (boxes, prisms, cylinders, cones, spheres).
/// </summary>
/// <remarks>
/// Every flat face gets its own vertices so normals stay faceted, shared by the triangles of that face: a quad is four
/// vertices, a cap is one per corner. Triangle winding is computed so that front faces point away from each primitive's
/// interior; renderers can therefore enable or disable back-face culling freely.
/// </remarks>
public sealed class MeshBuilder
{
    private readonly List<float> _vertices = [];
    private readonly List<uint> _indices = [];

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

    /// <summary>
    /// Adds a flat convex polygon as a fan sharing one vertex per corner, wound to face away from <paramref name="interior"/>.
    /// A polygon that is not flat is added triangle by triangle instead, each with its own normal, as before.
    /// </summary>
    internal void AddFace(IReadOnlyList<Vector3> corners, Vector3 color, Vector3 interior)
    {
        Span<Vector3> buffer = stackalloc Vector3[corners.Count];
        var n = DistinctCorners(corners, buffer);
        if (n < 3) return;
        ReadOnlySpan<Vector3> points = buffer[..n];

        var (normal, centroid) = NewellNormal(points);
        var length = normal.Length();
        if (length < 1e-9f) return;
        normal /= length;

        if (!IsFlat(points, normal))
        {
            for (var i = 1; i < n - 1; i++) AddTriangleFacingAway(points[0], points[i], points[i + 1], color, interior);
            return;
        }

        var reversed = Vector3.Dot(normal, centroid - interior) < 0f;
        if (reversed) normal = -normal;

        var start = (uint)VertexCount;
        foreach (var p in points) AddVertex(p, normal, color);
        for (var i = 1; i < n - 1; i++)
        {
            // Skip the slivers a collinear corner would make; the rest of the fan still covers the face.
            if (Vector3.Cross(points[i] - points[0], points[i + 1] - points[0]).LengthSquared() < 1e-18f) continue;
            _indices.Add(start);
            _indices.Add(start + (uint)(reversed ? i + 1 : i));
            _indices.Add(start + (uint)(reversed ? i : i + 1));
        }
    }

    /// <summary>
    /// Copies the corners into <paramref name="points"/>, leaving out any that coincide with the one before (the apex of
    /// a cone) or, at the end, with the first. Returns how many were kept.
    /// </summary>
    private static int DistinctCorners(IReadOnlyList<Vector3> corners, Span<Vector3> points)
    {
        var n = 0;
        foreach (var corner in corners)
        {
            if (n > 0 && Vector3.DistanceSquared(points[n - 1], corner) < 1e-18f) continue;
            points[n++] = corner;
        }

        while (n > 1 && Vector3.DistanceSquared(points[n - 1], points[0]) < 1e-18f) n--;
        return n;
    }

    /// <summary>Newell's method: the (unnormalized) normal of the whole polygon, however its corners are spaced, and its centroid.</summary>
    private static (Vector3 Normal, Vector3 Centroid) NewellNormal(ReadOnlySpan<Vector3> points)
    {
        var normal = Vector3.Zero;
        var centroid = Vector3.Zero;
        for (var i = 0; i < points.Length; i++)
        {
            var p = points[i];
            var q = points[(i + 1) % points.Length];
            normal += new Vector3((p.Y - q.Y) * (p.Z + q.Z), (p.Z - q.Z) * (p.X + q.X), (p.X - q.X) * (p.Y + q.Y));
            centroid += p;
        }

        return (normal, centroid / points.Length);
    }

    /// <summary>Flat means every triangle of the fan faces the same way as the whole polygon.</summary>
    private static bool IsFlat(ReadOnlySpan<Vector3> points, Vector3 normal)
    {
        for (var i = 1; i < points.Length - 1; i++)
        {
            var edge = Vector3.Cross(points[i] - points[0], points[i + 1] - points[0]);
            var edgeLength = edge.Length();
            if (edgeLength >= 1e-9f && Vector3.Dot(edge / edgeLength, normal) <= 0.9999f) return false;
        }

        return true;
    }

    /// <summary>Adds a triangle wound so its normal points away from <paramref name="interior"/>.</summary>
    public void AddTriangleFacingAway(Vector3 a, Vector3 b, Vector3 c, Vector3 color, Vector3 interior)
    {
        var normal = Vector3.Cross(b - a, c - a);
        var centroid = (a + b + c) / 3f;
        if (Vector3.Dot(normal, centroid - interior) < 0f)
        {
#pragma warning disable S2234 // b and c are passed the other way round on purpose: that reverses
            AddTriangle(a, c, b, color);   // the winding, which is how the normal gets flipped.
#pragma warning restore S2234
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
        ArgumentNullException.ThrowIfNull(loopA);
        ArgumentNullException.ThrowIfNull(loopB);
        if (loopA.Count != loopB.Count || loopA.Count < 3)
        {
            throw new ArgumentException("Loops must have the same number of points (at least 3).");
        }

        var n = loopA.Count;
        var inside = interior ?? (Average(loopA) + Average(loopB)) * 0.5f;

        var quad = new Vector3[4];
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            quad[0] = loopA[i];
            quad[1] = loopA[j];
            quad[2] = loopB[j];
            quad[3] = loopB[i];
            AddFace(quad, sideColor, inside);
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

    /// <summary>Flat horizontal triangle facing +Y (for glyph outlines laid on the water).</summary>
    public void AddTriangleUp(Vector3 a, Vector3 b, Vector3 c, Vector3 color) =>
        AddTriangleFacingAway(a, b, c, color, ((a + b + c) / 3f) - Vector3.UnitY);

    /// <summary>Flat horizontal quad facing +Y (for markers laid on the water).</summary>
    public void AddQuadUp(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 color) =>
        AddFace(new[] { a, b, c, d }, color, (a + b + c + d) / 4f - Vector3.UnitY);

    /// <summary>
    /// Adds a copy of another mesh, moved by <paramref name="world"/> (normals turned to match) with its colors multiplied
    /// by <paramref name="colorScale"/>: how instances of a shared mesh are baked into one.
    /// </summary>
    internal void AddTransformed(MeshData mesh, Matrix4x4 world, Vector3 colorScale)
    {
        // Normals go through the inverse transpose, so a stretched shape still gets normals square to its faces.
        var normalMatrix = Matrix4x4.Invert(world, out var inverse) ? Matrix4x4.Transpose(inverse) : world;
        var start = (uint)VertexCount;
        var v = mesh.Vertices;
        for (var i = 0; i < v.Length; i += MeshData.VertexStride)
        {
            var position = Vector3.Transform(new Vector3(v[i], v[i + 1], v[i + 2]), world);
            var normal = Vector3.TransformNormal(new Vector3(v[i + 3], v[i + 4], v[i + 5]), normalMatrix);
            normal = normal.LengthSquared() > 1e-20f ? Vector3.Normalize(normal) : Vector3.UnitY;
            AddVertex(position, normal, new Vector3(v[i + 6], v[i + 7], v[i + 8]) * colorScale);
        }

        // A mirroring transform turns every triangle inside out; wind them the other way round to face out again.
        var mirrored = world.GetDeterminant() < 0f;
        var indices = mesh.Indices;
        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            _indices.Add(start + indices[i]);
            _indices.Add(start + indices[mirrored ? i + 2 : i + 1]);
            _indices.Add(start + indices[mirrored ? i + 1 : i + 2]);
        }
    }

    /// <summary>Creates the mesh from everything added so far.</summary>
    public MeshData Build(int id, string name, bool isWater = false) =>
        new(id, name, _vertices.ToArray(), _indices.ToArray(), isWater);

    private void AddCap(IReadOnlyList<Vector3> loop, Vector3 color, Vector3 interior) => AddFace(loop, color, interior);

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
