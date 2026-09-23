using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// Regressions from a review of the plan-geometry code: holes bridged through other holes, a shoreline whose end
/// segment had no length, a zoom that threw on inverted limits, outlines that only touched themselves, and a sea
/// skirt facing downwards.
/// </summary>
public class GeometryReviewFixTests
{
    private static Vector2[] Rect(float x0, float y0, float x1, float y1) =>
        new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) };

    private static float Area(float x0, float y0, float x1, float y1) => (x1 - x0) * (y1 - y0);

    /// <summary>The area the triangles cover, and the smallest signed area among them.</summary>
    private static (float Total, float Smallest) Measure(IReadOnlyList<Vector2> points, IReadOnlyList<(int A, int B, int C)> triangles)
    {
        var total = 0f;
        var smallest = float.MaxValue;
        foreach (var (a, b, c) in triangles)
        {
            var u = points[b] - points[a];
            var v = points[c] - points[a];
            var signed = (u.X * v.Y - u.Y * v.X) * 0.5f;
            total += MathF.Abs(signed);
            smallest = MathF.Min(smallest, signed);
        }

        return (total, smallest);
    }

    private static void AssertCovers(Vector2[] outer, Vector2[][] holes, float expected)
    {
        var (points, triangles) = PolygonMath.TriangulateWithHoles(outer, holes);
        var (total, smallest) = Measure(points, triangles);
        Assert.Equal(expected, total, tolerance: expected * 1e-4f);
        Assert.True(smallest > -1e-4f, "a triangle came out wound the wrong way, overlapping its neighbours");

        // Nothing is laid over a hole.
        foreach (var (a, b, c) in triangles)
        {
            var centroid = (points[a] + points[b] + points[c]) / 3f;
            Assert.DoesNotContain(holes, hole => PolygonMath.Contains(hole, centroid));
        }
    }

    [Fact]
    public void AHoleIsNotBridgedThroughAnotherHoleStillToCome()
    {
        // B's nearest outline vertex lies past A, which is merged after it; the bridge used to cut straight through A.
        var outer = Rect(0, 0, 20, 20);
        var holes = new[] { Rect(8, 10, 9, 12), Rect(3, 14, 6, 18) };

        AssertCovers(outer, holes, 400f - Area(8, 10, 9, 12) - Area(3, 14, 6, 18));
    }

    [Fact]
    public void AGlyphWithTwoCounters_IsTriangulatedAroundBoth()
    {
        // An "8" or a "B": two counters one above the other, in either order and either winding.
        var outer = Rect(0, 0, 10, 20);
        var lower = Rect(3, 3, 7, 8);
        var upper = Rect(3, 12, 7, 17);
        var expected = 200f - 2f * Area(3, 3, 7, 8);

        AssertCovers(outer, new[] { lower, upper }, expected);
        AssertCovers(outer, new[] { upper, lower }, expected);
        AssertCovers(outer.Reverse().ToArray(), new[] { lower.Reverse().ToArray(), upper }, expected);

        // Counters of different widths, so their rightmost points are not level.
        AssertCovers(outer, new[] { Rect(2, 3, 8, 8), Rect(3, 12, 6, 17) }, 200f - Area(2, 3, 8, 8) - Area(3, 12, 6, 17));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void ManyScatteredHoles_AreAllCutOutExactly(int seed)
    {
        // A grid of cells, each holding one hole of random size and place, so the holes never touch but the nearest
        // way out of one is often past another.
        var random = new Random(seed);
        var outer = Rect(0, 0, 60, 60);
        var holes = new List<Vector2[]>();
        var expected = 3600f;
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                if (random.NextDouble() < 0.25) continue;
                var x0 = 3f + column * 14f + (float)random.NextDouble() * 4f;
                var y0 = 3f + row * 14f + (float)random.NextDouble() * 4f;
                var x1 = x0 + 1f + (float)random.NextDouble() * 6f;
                var y1 = y0 + 1f + (float)random.NextDouble() * 6f;
                holes.Add(Rect(x0, y0, x1, y1));
                expected -= Area(x0, y0, x1, y1);
            }
        }

        AssertCovers(outer, holes.ToArray(), expected);
    }

    [Fact]
    public void AShorelineWithARepeatedEndPoint_IsRefused_AndStillFacesTheRightWay()
    {
        var line = new[] { new Vector2(0, 0), new Vector2(0, 0), new Vector2(100, 0), new Vector2(200, 0) };
        var shoreline = new Shoreline(line, landOnLeft: false);
        Assert.NotEmpty(shoreline.Validate());

        // Nearly the same place is the same place too, at both ends and in the middle.
        Assert.NotEmpty(new Shoreline(new[] { new Vector2(0, 0), new Vector2(1e-5f, 0), new Vector2(100, 0) }, false).Validate());
        Assert.NotEmpty(new Shoreline(new[] { new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 0.005f) }, false).Validate());
        Assert.NotEmpty(new Shoreline(new[] { new Vector2(0, 0), new Vector2(50, 0), new Vector2(50, 0), new Vector2(100, 0) }, false).Validate());

        // Points a few centimeters apart, as the designer can leave them, are fine.
        var close = new Shoreline(new[] { new Vector2(0, 0), new Vector2(0.05f, 0), new Vector2(100, 0), new Vector2(200, 0) }, landOnLeft: false);
        Assert.Empty(close.Validate());

        // Land to the −Z side along the whole line, including far out beyond its start.
        Assert.True(close.Contains(new Vector2(-500, -100)), "half the mainland was lost past the start");
        Assert.True(close.Contains(new Vector2(700, -100)));
        Assert.False(close.Contains(new Vector2(-500, 100)));
        Assert.False(close.Contains(new Vector2(700, 100)));
    }

    [Fact]
    public void LandOnLeft_MeansTowardPlusZ_ForALineRunningAlongPlusX()
    {
        // What the documentation promises: "left" is taken in plan coordinates, so for a line running east (+X) it
        // is south (+Z), and for a line running along +Z it is −X — the same side as Pier.Right.
        var east = new Shoreline(new[] { new Vector2(-100, 0), new Vector2(100, 0) }, landOnLeft: true);
        Assert.True(east.Contains(new Vector2(0, 50)));
        Assert.False(east.Contains(new Vector2(0, -50)));

        var south = new Shoreline(new[] { new Vector2(0, -100), new Vector2(0, 100) }, landOnLeft: true);
        var pier = new Pier("P", "Pier", new Vector2(0, -100), headingDegrees: 0f, length: 200f);
        Assert.True(south.Contains(pier.Center + pier.Right * 50f));
    }

    [Fact]
    public void OrientedRectCorners_RunCounterClockwiseInPlanCoordinates()
    {
        foreach (var heading in new[] { 0f, 37f, 90f, 200f })
        {
            var corners = new OrientedRect(new Vector2(3, 4), new Vector2(2, 5), heading).GetCorners();
            Assert.True(PolygonMath.SignedArea(corners) > 0f);
        }
    }

    [Fact]
    public void ZoomWithTheLimitsTheWrongWayRound_DoesNotThrow()
    {
        var camera = new OrbitCamera();
        camera.Constraints.MinDistance = 100f;
        camera.Constraints.MaxDistance = 50f;

        camera.Zoom(2f);
        camera.Zoom(0.5f, new Vector3(10, 0, 10));

        // Same answer as Constrain gives: the minimum wins.
        Assert.Equal(100f, camera.DesiredPose.Distance, tolerance: 1e-3f);
    }

    [Fact]
    public void IsSimple_RefusesOutlinesThatTouchThemselves()
    {
        // A square is fine, and so is a straight run of three points along one side.
        Assert.True(PolygonMath.IsSimple(Rect(0, 0, 10, 10)));
        Assert.True(PolygonMath.IsSimple(new[] { new Vector2(0, 0), new Vector2(5, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10) }));

        // A point repeated away from its neighbours: two squares meeting at a corner (a bow tie).
        Assert.False(PolygonMath.IsSimple(new[]
        {
            new Vector2(0, 0), new Vector2(5, 0), new Vector2(5, 5), new Vector2(10, 5), new Vector2(10, 10), new Vector2(5, 10), new Vector2(5, 5), new Vector2(0, 5),
        }));

        // A corner resting on another edge (a T-junction).
        Assert.False(PolygonMath.IsSimple(new[]
        {
            new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(5, 0.0f), new Vector2(0, 10),
        }));

        // Two edges overlapping along a line.
        Assert.False(PolygonMath.IsSimple(new[]
        {
            new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 5), new Vector2(4, 5), new Vector2(4, 0), new Vector2(2, 0), new Vector2(2, 8), new Vector2(0, 8),
        }));

        // An edge doubling straight back along the one before it (a spike).
        Assert.False(PolygonMath.IsSimple(new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(15, 0), new Vector2(12, 0), new Vector2(10, 10) }));
    }

    [Fact]
    public void ALandAreaThatRepeatsAPoint_IsRefusedAsTheMessageSays()
    {
        var marina = new MarinaVisualizer();
        var error = Assert.Throws<MarinaLayoutException>(() => marina.AddLandArea(new LandArea("yard", new[]
        {
            new Vector2(0, 0), new Vector2(5, 0), new Vector2(5, 5), new Vector2(10, 5), new Vector2(10, 10), new Vector2(5, 10), new Vector2(5, 5), new Vector2(0, 5),
        }, 1f)));

        Assert.Contains("repeat points", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSeaSkirt_FacesUpLikeTheGrid()
    {
        var mesh = MarinaMeshFactory.CreateWaterGrid(1, 100f, 4, new Vector2(10, -20));
        Assert.Equal(4 * 4 * 2 + 8, mesh.TriangleCount);

        for (var t = 0; t < mesh.TriangleCount; t++)
        {
            var p = new Vector3[3];
            for (var corner = 0; corner < 3; corner++)
            {
                var offset = (int)mesh.Indices[t * 3 + corner] * MeshData.VertexStride;
                p[corner] = new Vector3(mesh.Vertices[offset], mesh.Vertices[offset + 1], mesh.Vertices[offset + 2]);
            }

            var normal = Vector3.Cross(p[1] - p[0], p[2] - p[0]);
            Assert.True(normal.Y > 0f, $"triangle {t} faces down");
        }
    }
}
