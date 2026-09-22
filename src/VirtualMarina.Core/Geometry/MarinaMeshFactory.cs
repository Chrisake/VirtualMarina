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

    /// <summary>Flat 1×1 m quad on Y = 0, facing up, with white vertices (tinted by berth status).</summary>
    public static MeshData CreateBerthPad(int id)
    {
        var b = new MeshBuilder();
        b.AddQuadUp(new(-0.5f, 0f, -0.5f), new(0.5f, 0f, -0.5f), new(0.5f, 0f, 0.5f), new(-0.5f, 0f, 0.5f), White);
        return b.Build(id, "BerthPad");
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

    /// <summary>How far the flat skirt of open sea reaches past the middle of the water, in meters.</summary>
    /// <remarks>
    /// Past the horizon in any usable view, and far enough to sit outside the mainland a
    /// <see cref="Domain.Shoreline"/> builds, so the two never leave a gap between them.
    /// </remarks>
    public const float SeaReach = 30_000f;

    /// <summary>
    /// Square, finely tessellated grid on Y = 0 centered at <paramref name="center"/>, ringed by a flat skirt that
    /// runs out to <see cref="SeaReach"/> so the water never visibly ends.
    /// Vertex positions are displaced in the water vertex shader to animate waves.
    /// </summary>
    /// <remarks>
    /// The skirt is eight triangles. The water shader fades the waves, the sky reflection and the glints out over the
    /// outer part of the grid, so the skirt is plain body colour that the fog carries into the horizon — the open sea
    /// costs almost nothing however far it reaches.
    /// </remarks>
    public static MeshData CreateWaterGrid(int id, float size, int resolution, Vector2 center)
    {
        resolution = Math.Clamp(resolution, 2, 1024);
        var verticesPerSide = resolution + 1;
        const int skirtVertices = 8;         // four corners of the grid, four of the far square
        const int skirtIndices = 8 * 3;      // two triangles per side
        var vertices = new float[(verticesPerSide * verticesPerSide + skirtVertices) * MeshData.VertexStride];
        var indices = new uint[resolution * resolution * 6 + skirtIndices];
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

        // The skirt: a ring between the edge of the grid and a far square, so the sea runs to the horizon.
        var first = (uint)(verticesPerSide * verticesPerSide);
        var inner = new[]
        {
            new Vector2(center.X - half, center.Y - half),
            new Vector2(center.X + half, center.Y - half),
            new Vector2(center.X + half, center.Y + half),
            new Vector2(center.X - half, center.Y + half),
        };

        var reach = MathF.Max(SeaReach, half * 2f);
        var outer = new[]
        {
            new Vector2(center.X - reach, center.Y - reach),
            new Vector2(center.X + reach, center.Y - reach),
            new Vector2(center.X + reach, center.Y + reach),
            new Vector2(center.X - reach, center.Y + reach),
        };

        foreach (var corner in inner.Concat(outer))
        {
            vertices[v++] = corner.X;
            vertices[v++] = 0f;
            vertices[v++] = corner.Y;
            vertices[v++] = 0f;
            vertices[v++] = 1f;
            vertices[v++] = 0f;
            vertices[v++] = 0f;
            vertices[v++] = 0f;
            vertices[v++] = 0f;
        }

        for (var side = 0; side < 4; side++)
        {
            var nextSide = (side + 1) % 4;
            var i0 = first + (uint)side;              // this corner of the grid
            var i1 = first + (uint)nextSide;          // the next one round
            var o0 = first + 4u + (uint)side;         // the matching corners of the far square
            var o1 = first + 4u + (uint)nextSide;

            // Counter-clockwise from above, the same way round as the grid itself.
            indices[k++] = i0;
            indices[k++] = o0;
            indices[k++] = o1;
            indices[k++] = i0;
            indices[k++] = o1;
            indices[k++] = i1;
        }

        return new MeshData(id, "Water", vertices, indices, isWater: true);
    }
}
