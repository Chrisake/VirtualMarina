using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Serialization;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// The mainland behind the marina. The whole feature rests on turning an open line into a closed shape covering one
/// side of the plan, so that is what these check: which side ends up land, that the shape is a sound polygon, and
/// that a line which cannot divide the plan is refused.
/// </summary>
public class ShorelineTests
{
    /// <summary>A straight coast running east, with the sea to the north (−Y) and the land to the south (+Y).</summary>
    private static Shoreline StraightCoast(bool landOnLeft = true) =>
        new(new[] { new Vector2(-500, -40), new Vector2(500, -40) }, landOnLeft);

    [Fact]
    public void AStraightCoast_PutsTheLandOnTheChosenSideOnly()
    {
        var south = StraightCoast(landOnLeft: true);

        // Left of a line walked east is +Y, and the far side is not land however far out it is tested.
        Assert.True(south.Contains(new Vector2(0, 60)));
        Assert.True(south.Contains(new Vector2(9000, 5000)));
        Assert.False(south.Contains(new Vector2(0, -60)));
        Assert.False(south.Contains(new Vector2(-9000, -5000)));

        // The same line with the other side chosen is the exact mirror.
        var north = StraightCoast(landOnLeft: false);
        Assert.False(north.Contains(new Vector2(0, 60)));
        Assert.True(north.Contains(new Vector2(0, -60)));
        Assert.True(north.Contains(new Vector2(-9000, -5000)));
    }

    [Fact]
    public void TheBuiltShape_IsASoundPolygonThatTriangulates()
    {
        foreach (var shoreline in new[] { StraightCoast(true), StraightCoast(false), BentCoast(), DiagonalCoast() })
        {
            var outline = shoreline.BuildOutline();
            Assert.True(outline.Count >= 3, "the shape needs at least three points");
            Assert.True(PolygonMath.IsSimple(outline), "the shape must not cross itself");
            Assert.NotEmpty(PolygonMath.Triangulate(outline));

            // Cheap enough for a phone: the line's own points plus a corner or two.
            Assert.True(outline.Count <= shoreline.Points.Count + 6, $"{outline.Count} points is more than the shape needs");
        }
    }

    [Fact]
    public void ADiagonalCoast_WhoseEndsLandExactlyOnACorner_StillBuildsASoundShape()
    {
        // The ends run out to the corners of the far square, which is where duplicated points would creep in.
        var outline = DiagonalCoast().BuildOutline();

        Assert.True(PolygonMath.IsSimple(outline));
        for (var i = 0; i < outline.Count; i++)
        {
            var next = outline[(i + 1) % outline.Count];
            Assert.True(Vector2.Distance(outline[i], next) > 0.5f, $"points {i} and {i + 1} sit on top of each other");
        }
    }

    [Fact]
    public void ABentCoast_KeepsABayOnTheWaterSide()
    {
        var bay = BentCoast();

        // The water reaches into the bay, past the line of the open coast either side of it.
        Assert.False(bay.Contains(new Vector2(0, 150)));
        Assert.True(bay.Contains(new Vector2(-400, 150)));
        Assert.True(bay.Contains(new Vector2(400, 150)));

        // Behind the head of the bay it is land again.
        Assert.True(bay.Contains(new Vector2(0, 400)));
    }

    [Fact]
    public void ALineWhoseEndlessSegmentsCross_IsRefused()
    {
        // The two ends splay outward and meet, so neither side of the line is "the land".
        var crossing = new Shoreline(
            new[] { new Vector2(0, 0), new Vector2(-100, 300), new Vector2(500, 300), new Vector2(400, 0) },
            landOnLeft: true);

        Assert.Contains(crossing.Validate(), problem => problem.Contains("cross", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(crossing.BuildOutline());
        Assert.False(crossing.Contains(new Vector2(0, 500)));
    }

    [Fact]
    public void TwoPointsAreEnough_ButTheyMustBeApart()
    {
        Assert.Empty(StraightCoast().Validate());
        Assert.Throws<ArgumentException>(() => new Shoreline(new[] { Vector2.Zero }, landOnLeft: true));

        var samePlace = new Shoreline(new[] { new Vector2(5, 5), new Vector2(5, 5) }, landOnLeft: true);
        Assert.NotEmpty(samePlace.Validate());
    }

    [Fact]
    public void DistanceToShore_MeasuresToTheLineNotTheFarEdge()
    {
        var coast = StraightCoast();

        Assert.Equal(60f, coast.DistanceToShore(new Vector2(0, 20)), 2);
        Assert.Equal(0f, coast.DistanceToShore(new Vector2(0, -40)), 2);

        // Past the end of the drawn line it measures to the nearest end point, not to the endless part.
        Assert.Equal(100f, coast.DistanceToShore(new Vector2(600, -40)), 2);
    }

    [Fact]
    public void TheMainland_IsCheapToDraw_HowevermuchOfItThereIs()
    {
        var bare = LandMeshFactory.CreateShoreline(MeshIds.Shoreline, StraightCoast() with { Scenery = HinterlandScenery.None });

        // The ground itself is the outline triangulated plus its walls, and nothing else.
        Assert.InRange(Triangles(bare), 1, 40);

        // A coast a hundred times longer is the same handful of triangles: the shape does not grow with the land.
        var huge = LandMeshFactory.CreateShoreline(
            MeshIds.Shoreline,
            new Shoreline(new[] { new Vector2(-50_000, -40), new Vector2(50_000, -40) }, landOnLeft: true) { Scenery = HinterlandScenery.None });
        Assert.Equal(Triangles(bare), Triangles(huge));

        // Scenery is capped, so even a town stays within a mobile budget.
        foreach (var scenery in new[] { HinterlandScenery.Countryside, HinterlandScenery.Fields, HinterlandScenery.Town })
        {
            var mesh = LandMeshFactory.CreateShoreline(MeshIds.Shoreline, StraightCoast() with { Scenery = scenery });
            Assert.True(Triangles(mesh) > Triangles(bare), $"{scenery} drew nothing");
            Assert.True(Triangles(mesh) < 30_000, $"{scenery} costs {Triangles(mesh)} triangles");
        }
    }

    [Fact]
    public void TheSameSeed_DrawsTheSameScenery()
    {
        var coast = StraightCoast() with { Scenery = HinterlandScenery.Town, ScenerySeed = 7 };

        var drawn = LandMeshFactory.CreateShoreline(MeshIds.Shoreline, coast);
        Assert.Equal(drawn.Vertices, LandMeshFactory.CreateShoreline(MeshIds.Shoreline, coast).Vertices);

        // A different seed rearranges the town, so the two are not the same drawing.
        var other = LandMeshFactory.CreateShoreline(MeshIds.Shoreline, coast with { ScenerySeed = 8 });
        Assert.NotEqual(drawn.Vertices, other.Vertices);
    }

    [Fact]
    public void AMarinaWithAMainland_DrawsItBeneathTheLandAreas_AndKeepsItThroughAFile()
    {
        var marina = new MarinaVisualizer();
        marina.AddLandArea(new LandArea("quay", new[] { new Vector2(-20, -10), new Vector2(20, -10), new Vector2(20, 10), new Vector2(-20, 10) }, 1.4f, LandKind.Quay));
        Assert.Null(marina.Shoreline);

        marina.SetShoreline(StraightCoast() with { Scenery = HinterlandScenery.Fields, ScenerySeed = 3 });
        Assert.NotNull(marina.Shoreline);

        // The mainland is the first thing drawn, so the quay traced along the shore sits on top of it.
        var frame = marina.BuildRenderFrame();
        Assert.Equal(MeshIds.Shoreline, frame.Objects[0].MeshId);

        var ground = frame.Objects.Select((o, i) => (o.MeshId, i)).ToList();
        var mainland = ground.First(e => e.MeshId == MeshIds.Shoreline).i;
        var quay = ground.First(e => e.MeshId == MeshIds.ForLand(0)).i;
        Assert.True(mainland < quay, "the quay is drawn under the mainland instead of on it");

        var reloaded = new MarinaVisualizer();
        MarinaDocument.Parse(MarinaDocument.FromVisualizer(marina, generator: "tests").ToJson()).ApplyTo(reloaded);

        var copy = reloaded.Shoreline;
        Assert.NotNull(copy);
        Assert.Equal(marina.Shoreline.Points, copy.Points);
        Assert.Equal(marina.Shoreline.LandOnLeft, copy.LandOnLeft);
        Assert.Equal(HinterlandScenery.Fields, copy.Scenery);
        Assert.Equal(3, copy.ScenerySeed);

        // Taking it away leaves open water, and the land areas keep their place in the scene.
        Assert.True(marina.RemoveShoreline());
        Assert.False(marina.RemoveShoreline());
        Assert.Equal(MeshIds.ForLand(0), marina.BuildRenderFrame().Objects[0].MeshId);
        Assert.DoesNotContain(marina.BuildRenderFrame().Objects, o => o.MeshId == MeshIds.ShorelineScenery);
    }

    [Fact]
    public void ALayoutWrittenBeforeMainlandsExisted_StillLoads_AsOpenWater()
    {
        var marina = new MarinaVisualizer();
        marina.AddLandArea(new LandArea("quay", new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10) }, 1f, LandKind.Quay));

        var json = MarinaDocument.FromVisualizer(marina, generator: "tests").ToJson();
        Assert.DoesNotContain("\"shoreline\"", json);

        var reloaded = new MarinaVisualizer();
        MarinaDocument.Parse(json).ApplyTo(reloaded);
        Assert.Null(reloaded.Shoreline);
    }

    private static int Triangles(MeshData mesh) => mesh.Indices.Length / 3;


    /// <summary>A bay: the coast runs east, then bites back into the land and out again.</summary>
    private static Shoreline BentCoast() =>
        new(
            new[]
            {
                new Vector2(-900, 40),
                new Vector2(-300, 40),
                new Vector2(-250, 300),
                new Vector2(250, 300),
                new Vector2(300, 40),
                new Vector2(900, 40),
            },
            landOnLeft: true);

    /// <summary>A coast at 45°, whose ends run out to the corners of the far square.</summary>
    private static Shoreline DiagonalCoast() =>
        new(new[] { new Vector2(-100, -100), new Vector2(100, 100) }, landOnLeft: true);
}
