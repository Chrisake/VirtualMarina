using System.Numerics;

namespace VirtualMarina.Core.Geometry;

/// <summary>
/// CPU-side triangle mesh in a backend-neutral format. Each renderer uploads it to its own GPU API.
/// </summary>
/// <remarks>
/// Interleaved vertex layout (<see cref="VertexStride"/> floats per vertex):
/// position (xyz), normal (xyz), color (rgb). Indices are 32-bit triangle lists.
/// </remarks>
public sealed class MeshData
{
    /// <summary>Floats per vertex: position (3), normal (3), color (3).</summary>
    public const int VertexStride = 9;

    /// <summary>Offset of the position within a vertex.</summary>
    public const int PositionOffset = 0;

    /// <summary>Offset of the normal within a vertex.</summary>
    public const int NormalOffset = 3;

    /// <summary>Offset of the RGB color within a vertex.</summary>
    public const int ColorOffset = 6;

    /// <summary>Creates a mesh. Register it with <see cref="MeshLibrary.Register"/> to use or replace a model.</summary>
    /// <param name="id">Mesh id (see <see cref="MeshIds"/>).</param>
    /// <param name="name">Name for diagnostics.</param>
    /// <param name="vertices">Interleaved vertex data, <see cref="VertexStride"/> floats per vertex.</param>
    /// <param name="indices">Triangle list indices.</param>
    /// <param name="isWater">True only for the water grid.</param>
    /// <exception cref="ArgumentException">The vertex buffer length is not a multiple of <see cref="VertexStride"/>.</exception>
    public MeshData(int id, string name, float[] vertices, uint[] indices, bool isWater = false)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        if (vertices.Length % VertexStride != 0)
        {
            throw new ArgumentException($"Vertex buffer length must be a multiple of {VertexStride}.", nameof(vertices));
        }

        Id = id;
        Name = name;
        Vertices = vertices;
        Indices = indices;
        IsWater = isWater;
        Bounds = BoundingBox.FromVertices(vertices, VertexStride);
    }

    /// <summary>Mesh id referenced by <see cref="Rendering.RenderObject.MeshId"/>.</summary>
    public int Id { get; }

    /// <summary>Name for diagnostics.</summary>
    public string Name { get; }

    /// <summary>Interleaved vertex data (position, normal, color).</summary>
    public float[] Vertices { get; }

    /// <summary>Triangle list indices into <see cref="Vertices"/>.</summary>
    public uint[] Indices { get; }

    /// <summary>The water grid is drawn by the water pass instead of the object pass.</summary>
    public bool IsWater { get; }

    /// <summary>Number of vertices.</summary>
    public int VertexCount => Vertices.Length / VertexStride;

    /// <summary>Number of triangles.</summary>
    public int TriangleCount => Indices.Length / 3;

    /// <summary>Local-space bounds, used for picking and placement.</summary>
    public BoundingBox Bounds { get; }
}

/// <summary>Axis-aligned box.</summary>
/// <param name="Min">Minimum corner.</param>
/// <param name="Max">Maximum corner.</param>
public readonly record struct BoundingBox(Vector3 Min, Vector3 Max)
{
    /// <summary>Extent along each axis.</summary>
    public Vector3 Size => Max - Min;

    /// <summary>Middle point.</summary>
    public Vector3 Center => (Min + Max) * 0.5f;

    /// <summary>Bounds of the positions in an interleaved vertex buffer (zero box when empty).</summary>
    /// <param name="vertices">Vertex data with the position in the first three floats of each vertex.</param>
    /// <param name="stride">Floats per vertex.</param>
    public static BoundingBox FromVertices(float[] vertices, int stride)
    {
        if (vertices.Length < 3) return new BoundingBox(Vector3.Zero, Vector3.Zero);

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var i = 0; i < vertices.Length; i += stride)
        {
            var p = new Vector3(vertices[i], vertices[i + 1], vertices[i + 2]);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        return new BoundingBox(min, max);
    }

    /// <summary>Slab test. Returns the entry distance along the (not necessarily normalized) direction.</summary>
    public bool IntersectRay(Vector3 origin, Vector3 direction, out float distance)
    {
        var tMin = 0f;
        var tMax = float.MaxValue;
        distance = 0f;

        for (var axis = 0; axis < 3; axis++)
        {
            var o = origin[axis];
            var d = direction[axis];
            var min = Min[axis];
            var max = Max[axis];

            if (MathF.Abs(d) < 1e-8f)
            {
                if (o < min || o > max) return false;
                continue;
            }

            var inv = 1f / d;
            var t1 = (min - o) * inv;
            var t2 = (max - o) * inv;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = MathF.Max(tMin, t1);
            tMax = MathF.Min(tMax, t2);
            if (tMin > tMax) return false;
        }

        distance = tMin;
        return true;
    }
}
