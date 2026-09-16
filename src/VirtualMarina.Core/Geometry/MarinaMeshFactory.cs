using System.Numerics;

namespace VirtualMarina.Core.Geometry;

/// <summary>Procedural meshes for marina infrastructure, markers and the water surface.</summary>
public static class MarinaMeshFactory
{
    private static readonly Vector3 White = Vector3.One;

    /// <summary>1 m cube centered at the origin with white vertices, tinted per instance.</summary>
    public static MeshData CreateUnitBox(int id)
    {
        var b = new MeshBuilder();
        b.AddBox(Vector3.Zero, Vector3.One, White);
        return b.Build(id, "UnitBox");
    }

    /// <summary>Octagonal piling, 1 m diameter, from Y = 0 to Y = 1, with a lighter top.</summary>
    public static MeshData CreatePiling(int id)
    {
        var b = new MeshBuilder();
        var wood = new Vector3(0.40f, 0.31f, 0.23f);
        b.AddCylinder(Vector3.Zero, new Vector3(0f, 0.97f, 0f), 0.5f, 0.5f, 8, wood);
        b.AddCylinder(new Vector3(0f, 0.97f, 0f), Vector3.UnitY, 0.5f, 0.42f, 8, new Vector3(0.55f, 0.45f, 0.35f));
        return b.Build(id, "Piling");
    }

    /// <summary>12-sided cylinder, 1 m diameter, from Y = 0 to Y = 1, white (tinted per instance: steel piles, bollards).</summary>
    public static MeshData CreateCylinder(int id)
    {
        var b = new MeshBuilder();
        b.AddCylinder(Vector3.Zero, Vector3.UnitY, 0.5f, 0.5f, 12, White);
        return b.Build(id, "Cylinder");
    }

    /// <summary>Flat 1×1 m quad on Y = 0, facing up, with white vertices (tinted by slip status).</summary>
    public static MeshData CreateSlipPad(int id)
    {
        var b = new MeshBuilder();
        b.AddQuadUp(new(-0.5f, 0f, -0.5f), new(0.5f, 0f, -0.5f), new(0.5f, 0f, 0.5f), new(-0.5f, 0f, 0.5f), White);
        return b.Build(id, "SlipPad");
    }

    /// <summary>Inverted pyramid "you are here" marker, tip at Y = 0, about 1.6 m tall.</summary>
    public static MeshData CreateSelectionMarker(int id)
    {
        var b = new MeshBuilder();
        var gold = new Vector3(1f, 0.84f, 0.22f);
        var tip = Vector3.Zero;
        var top = new[]
        {
            new Vector3(-0.6f, 1.2f, -0.6f), new Vector3(0.6f, 1.2f, -0.6f),
            new Vector3(0.6f, 1.2f, 0.6f), new Vector3(-0.6f, 1.2f, 0.6f),
        };
        b.AddLoft(new[] { tip, tip, tip, tip }, top, gold, null, gold, new Vector3(0f, 0.8f, 0f));
        b.AddSphere(new Vector3(0f, 1.6f, 0f), 0.28f, 8, 5, gold);
        return b.Build(id, "SelectionMarker");
    }

    /// <summary>Low-poly sphere, radius 0.5 m, white (status buoy).</summary>
    public static MeshData CreateBuoy(int id)
    {
        var b = new MeshBuilder();
        b.AddSphere(Vector3.Zero, 0.5f, 10, 6, White);
        return b.Build(id, "Buoy");
    }

    /// <summary>
    /// Square, finely tessellated grid on Y = 0 centered at <paramref name="center"/>.
    /// Vertex positions are displaced in the water vertex shader to animate waves.
    /// </summary>
    public static MeshData CreateWaterGrid(int id, float size, int resolution, Vector2 center)
    {
        resolution = Math.Clamp(resolution, 2, 1024);
        var verticesPerSide = resolution + 1;
        var vertices = new float[verticesPerSide * verticesPerSide * MeshData.VertexStride];
        var indices = new uint[resolution * resolution * 6];
        var half = size * 0.5f;
        var step = size / resolution;

        var v = 0;
        for (var zi = 0; zi < verticesPerSide; zi++)
        {
            for (var xi = 0; xi < verticesPerSide; xi++)
            {
                vertices[v++] = center.X - half + xi * step;
                vertices[v++] = 0f;
                vertices[v++] = center.Y - half + zi * step;
                vertices[v++] = 0f;
                vertices[v++] = 1f;
                vertices[v++] = 0f;
                vertices[v++] = 0f;
                vertices[v++] = 0f;
                vertices[v++] = 0f;
            }
        }

        var k = 0;
        for (var zi = 0; zi < resolution; zi++)
        {
            for (var xi = 0; xi < resolution; xi++)
            {
                var i0 = (uint)(zi * verticesPerSide + xi);
                var i1 = i0 + 1;
                var i2 = i0 + (uint)verticesPerSide;
                var i3 = i2 + 1;
                // Counter-clockwise when viewed from above (+Y).
                indices[k++] = i0;
                indices[k++] = i2;
                indices[k++] = i1;
                indices[k++] = i1;
                indices[k++] = i2;
                indices[k++] = i3;
            }
        }

        return new MeshData(id, "Water", vertices, indices, isWater: true);
    }
}
