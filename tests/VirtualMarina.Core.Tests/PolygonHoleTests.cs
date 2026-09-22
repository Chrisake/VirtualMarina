using System.Numerics;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// Triangulating an outline with holes in it — a letter with a counter, a quay with a pond. The triangles have to
/// cover the ring and stay out of the holes, or an O comes out as a filled blob.
/// </summary>
public class PolygonHoleTests
{
    private static Vector2[] Square(float x, float y, float size) =>
        new[] { new Vector2(x, y), new Vector2(x + size, y), new Vector2(x + size, y + size), new Vector2(x, y + size) };

    /// <summary>The area the triangles actually cover.</summary>
    private static float CoveredArea(IReadOnlyList<Vector2> points, IReadOnlyList<(int A, int B, int C)> triangles)
    {
        var total = 0f;
        foreach (var (a, b, c) in triangles)
        {
            var u = points[b] - points[a];
            var v = points[c] - points[a];
            total += MathF.Abs(u.X * v.Y - u.Y * v.X) * 0.5f;
        }

        return total;
    }

    /// <summary>True when a point lies inside any of the triangles.</summary>
    private static bool Covers(IReadOnlyList<Vector2> points, IReadOnlyList<(int A, int B, int C)> triangles, Vector2 point)
    {
        foreach (var (a, b, c) in triangles)
        {
            var (p, q, r) = (points[a], points[b], points[c]);
            var d1 = (point.X - q.X) * (p.Y - q.Y) - (p.X - q.X) * (point.Y - q.Y);
            var d2 = (point.X - r.X) * (q.Y - r.Y) - (q.X - r.X) * (point.Y - r.Y);
            var d3 = (point.X - p.X) * (r.Y - p.Y) - (r.X - p.X) * (point.Y - p.Y);
            var negative = d1 < 0f || d2 < 0f || d3 < 0f;
            var positive = d1 > 0f || d2 > 0f || d3 > 0f;
            if (!(negative && positive)) return true;
        }

        return false;
    }

    [Fact]
    public void ASquareWithASquareHole_IsTriangulatedAsARing()
    {
        var outer = Square(0, 0, 10);
        var hole = Square(3, 3, 4);

        var (points, triangles) = PolygonMath.TriangulateWithHoles(outer, new[] { hole });

        Assert.NotEmpty(triangles);
        Assert.Equal(100f - 16f, CoveredArea(points, triangles), tolerance: 0.01f);

        // The middle of the hole is not covered, the ring around it is.
        Assert.False(Covers(points, triangles, new Vector2(5, 5)), "the hole was filled in");
        Assert.True(Covers(points, triangles, new Vector2(1, 5)), "the ring was left out");
        Assert.True(Covers(points, triangles, new Vector2(9, 5)), "the ring was left out");
        Assert.True(Covers(points, triangles, new Vector2(5, 1)), "the ring was left out");
        Assert.True(Covers(points, triangles, new Vector2(5, 9)), "the ring was left out");
    }

    [Fact]
    public void TheWindingOfTheInputDoesNotMatter()
    {
        var outer = Square(0, 0, 10);
        var hole = Square(3, 3, 4);

        foreach (var outerRing in new[] { outer, outer.Reverse().ToArray() })
        {
            foreach (var holeRing in new[] { hole, hole.Reverse().ToArray() })
            {
                var (points, triangles) = PolygonMath.TriangulateWithHoles(outerRing, new[] { holeRing });
                Assert.Equal(84f, CoveredArea(points, triangles), tolerance: 0.01f);
                Assert.False(Covers(points, triangles, new Vector2(5, 5)), "the hole was filled in");
            }
        }
    }

    [Fact]
    public void SeveralHolesAreAllKeptOpen()
    {
        // Three holes, as in a percent sign or a heavily counted letter.
        var outer = Square(0, 0, 30);
        var holes = new[] { Square(2, 2, 5), Square(12, 12, 5), Square(22, 22, 5) };

        var (points, triangles) = PolygonMath.TriangulateWithHoles(outer, holes);

        Assert.Equal(900f - (3 * 25f), CoveredArea(points, triangles), tolerance: 0.05f);
        Assert.False(Covers(points, triangles, new Vector2(4.5f, 4.5f)));
        Assert.False(Covers(points, triangles, new Vector2(14.5f, 14.5f)));
        Assert.False(Covers(points, triangles, new Vector2(24.5f, 24.5f)));
        Assert.True(Covers(points, triangles, new Vector2(29f, 1f)));
    }

    [Fact]
    public void AnOutlineWithNoHoles_IsJustTriangulated()
    {
        var outer = Square(0, 0, 10);
        var (points, triangles) = PolygonMath.TriangulateWithHoles(outer, Array.Empty<IReadOnlyList<Vector2>>());

        Assert.Equal(100f, CoveredArea(points, triangles), tolerance: 0.01f);
        Assert.True(Covers(points, triangles, new Vector2(5, 5)));
    }

    [Fact]
    public void AConcaveOutlineWithAHole_StillWorks()
    {
        // An L-shape with a hole in the thick part of it.
        var outer = new[]
        {
            new Vector2(0, 0), new Vector2(20, 0), new Vector2(20, 8), new Vector2(8, 8),
            new Vector2(8, 20), new Vector2(0, 20),
        };

        var hole = Square(2, 2, 4);
        var (points, triangles) = PolygonMath.TriangulateWithHoles(outer, new[] { hole });

        // L area is 20*8 + 8*12 = 256, less the 16 of the hole.
        Assert.Equal(256f - 16f, CoveredArea(points, triangles), tolerance: 0.05f);
        Assert.False(Covers(points, triangles, new Vector2(4, 4)), "the hole was filled in");
        Assert.True(Covers(points, triangles, new Vector2(18, 4)));
        Assert.True(Covers(points, triangles, new Vector2(4, 18)));
        Assert.False(Covers(points, triangles, new Vector2(18, 18)), "the notch of the L was filled in");
    }

    [Fact]
    public void NothingSensibleToDo_ReturnsNothingRatherThanThrowing()
    {
        Assert.Empty(PolygonMath.TriangulateWithHoles(Array.Empty<Vector2>(), Array.Empty<IReadOnlyList<Vector2>>()).Triangles);
        Assert.Empty(PolygonMath.TriangulateWithHoles(new[] { Vector2.Zero, Vector2.One }, Array.Empty<IReadOnlyList<Vector2>>()).Triangles);

        // A hole with too few points is ignored, not fatal.
        var (points, triangles) = PolygonMath.TriangulateWithHoles(Square(0, 0, 10), new IReadOnlyList<Vector2>[] { new[] { Vector2.Zero, Vector2.One } });
        Assert.Equal(100f, CoveredArea(points, triangles), tolerance: 0.01f);
    }
}
