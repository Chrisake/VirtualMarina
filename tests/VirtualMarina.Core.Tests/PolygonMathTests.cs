using System.Numerics;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// Ear clipping on random outlines, near and far from the origin, and the small helpers the designer and the file reader
/// share.
/// </summary>
public class PolygonMathTests
{
    /// <summary>
    /// A random simple outline: points at increasing angles round a center, each at its own distance, so the outline is
    /// star-shaped (never crosses itself) but has plenty of reflex corners.
    /// </summary>
    private static Vector2[] RandomOutline(Random random, int count, Vector2 center, float radius)
    {
        var angles = Enumerable.Range(0, count).Select(_ => random.NextDouble() * Math.PI * 2).Order().ToArray();
        return angles
            .Select(angle => center + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius * (0.25f + 0.75f * (float)random.NextDouble()))
            .ToArray();
    }

    private static double TriangleArea(Vector2 a, Vector2 b, Vector2 c) =>
        (((double)b.X - a.X) * ((double)c.Y - a.Y) - ((double)c.X - a.X) * ((double)b.Y - a.Y)) * 0.5;

    public static TheoryData<float> Origins => [0f, 1e4f, 1e5f];

    [Theory]
    [MemberData(nameof(Origins))]
    public void RandomOutlines_AreCoveredExactly_ByCounterClockwiseTriangles(float origin)
    {
        var random = new Random(1234);
        for (var run = 0; run < 200; run++)
        {
            var outline = RandomOutline(random, random.Next(3, 60), new Vector2(origin, -origin * 0.5f), 50f);
            if (!PolygonMath.IsSimple(outline)) continue;
            if (random.Next(2) == 0) Array.Reverse(outline);

            var triangles = PolygonMath.Triangulate(outline, out var dropped);

            Assert.Equal(0, dropped);
            Assert.Equal(outline.Length - 2, triangles.Count);
            var covered = 0d;
            foreach (var (a, b, c) in triangles)
            {
                var area = TriangleArea(outline[a], outline[b], outline[c]);
                Assert.True(area > -1e-3, $"run {run}: triangle ({a}, {b}, {c}) is wound clockwise");
                covered += area;
            }

            var expected = Math.Abs(PolygonMath.SignedArea(outline));
            Assert.True(Math.Abs(covered - expected) <= expected * 1e-3 + 1e-2, $"run {run}: covered {covered}, outline {expected}");
        }
    }

    [Fact]
    public void SignedArea_FarFromTheOrigin_KeepsItsPrecision()
    {
        // A 10 m square a thousand kilometers out: the products of the coordinates alone are 10^12.
        var square = new[] { new Vector2(1e6f, 1e6f), new Vector2(1e6f + 10f, 1e6f), new Vector2(1e6f + 10f, 1e6f + 10f), new Vector2(1e6f, 1e6f + 10f) };
        Assert.Equal(100f, PolygonMath.SignedArea(square), 3);
        Assert.Equal(-100f, PolygonMath.SignedArea(square.Reverse().ToArray()), 3);
    }

    [Fact]
    public void AnOutlineThatCrossesItself_EndsWithoutHanging_AndAccountsForEveryCorner()
    {
        var bowTie = new[] { new Vector2(0, 0), new Vector2(10, 10), new Vector2(10, 0), new Vector2(0, 10), new Vector2(5, 20), new Vector2(-5, 5) };
        var triangles = PolygonMath.Triangulate(bowTie, out var dropped);
        Assert.True(triangles.Count + dropped == bowTie.Length - 2, "every corner is either clipped or given up on");
    }

    [Fact]
    public void AFlatCorner_IsTheOneGivenUp()
    {
        // All in a line but one: no ear anywhere once the triangle is gone, and the lost corners cover nothing.
        var line = new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(20, 0), new Vector2(30, 0), new Vector2(15, 10) };
        var triangles = PolygonMath.Triangulate(line, out _);
        var covered = triangles.Sum(t => TriangleArea(line[t.A], line[t.B], line[t.C]));
        Assert.Equal(150d, covered, 3);
    }

    [Fact]
    public void RemoveRepeatedPoints_DropsDoubleClicks_AndTheClosingPointOfAClosedOutline()
    {
        var points = new[] { new Vector2(0, 0), new Vector2(0.004f, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0.002f, 0.001f) };

        Assert.Equal(new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10) }, PolygonMath.RemoveRepeatedPoints(points, closed: true));
        Assert.Equal(4, PolygonMath.RemoveRepeatedPoints(points, closed: false).Count);
        Assert.Equal(2, PolygonMath.RemoveRepeatedPoints(new[] { new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(1, 0) }, closed: false, tolerance: 0.6f).Count);
    }

    [Fact]
    public void ClosestPointOnSegment_StaysOnTheSegment()
    {
        var a = new Vector2(0, 0);
        var b = new Vector2(10, 0);
        Assert.Equal(new Vector2(4, 0), PolygonMath.ClosestPointOnSegment(new Vector2(4, 3), a, b));
        Assert.Equal(a, PolygonMath.ClosestPointOnSegment(new Vector2(-5, 3), a, b));
        Assert.Equal(b, PolygonMath.ClosestPointOnSegment(new Vector2(15, -3), a, b));
        Assert.Equal(a, PolygonMath.ClosestPointOnSegment(new Vector2(15, -3), a, a));
    }
}
