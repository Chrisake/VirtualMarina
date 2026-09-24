using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Picking;
using VirtualMarina.Core.Rendering;
using VirtualMarina.SampleData;

namespace VirtualMarina.Core.Tests;

/// <summary>Polygon land areas, rock breakwaters, land berths and single-sided piers.</summary>
public class LandAndSingleSidedPierTests
{
    private static readonly Vector2[] LShape =
    {
        new(0, 0), new(40, 0), new(40, 10), new(10, 10), new(10, 30), new(0, 30),
    };

    private static MarinaVisualizer CreateMarinaWithBoatyard()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(new MarinaLayoutBuilder("Yard")
            .AddLandArea(new LandArea("yard", LShape, 1.5f) { Name = "Boatyard" }, yard => yard
                .AddBerth("Y-1", new Vector2(25, 5), headingDegrees: 90, length: 12, width: 5)
                .AddBerth("Y-2", new Vector2(5, 20), headingDegrees: 0, length: 12, width: 5))
            .AddLandArea(new LandArea("rocks", new OrientedRect(new Vector2(20, 60), new Vector2(60, 8), 0), 2.2f, LandKind.Breakwater))
            .AddPier("A", "Pier A", new Vector2(20, 12), 0f, 30f, pier => pier.AddBerths(PierSide.Right, 2, 5f, 10f))
            .Build());
        marina.SetViewportSize(800, 600);
        return marina;
    }

    [Fact]
    public void Triangulation_OfAConcavePolygon_CoversItsArea()
    {
        var triangles = PolygonMath.Triangulate(LShape);
        var area = triangles.Sum(t => PolygonMath.SignedArea(new[] { LShape[t.A], LShape[t.B], LShape[t.C] }));

        Assert.Equal(LShape.Length - 2, triangles.Count);
        Assert.Equal(40f * 10f + 10f * 20f, area, 3);
        Assert.All(triangles, t => Assert.True(PolygonMath.SignedArea(new[] { LShape[t.A], LShape[t.B], LShape[t.C] }) > 0f));
        Assert.True(PolygonMath.Contains(LShape, new Vector2(5, 25)));
        Assert.False(PolygonMath.Contains(LShape, new Vector2(25, 25)));
    }

    [Fact]
    public void LandArea_FromRectangle_KeepsItsFootprint()
    {
        var land = new LandArea("quay", new OrientedRect(new Vector2(0, -19), new Vector2(260, 26), 0), 1f);

        Assert.Equal(4, land.Points.Count);
        Assert.Equal(260f * 26f, land.Area, 1);
        Assert.Equal((new Vector2(-130, -32), new Vector2(130, -6)), land.GetAxisAlignedBounds());
    }

    [Fact]
    public void Validation_RejectsBadOutlinesAndUnknownLandAreas()
    {
        var bowTie = new LandArea("bow-tie", new[] { new Vector2(0, 0), new Vector2(10, 10), new Vector2(10, 0), new Vector2(0, 10) }, 1f);
        var layout = new MarinaLayout
        {
            LandAreas = new[] { bowTie, new LandArea("line", new[] { new Vector2(0, 0), new Vector2(1, 0) }, 1f), new LandArea("line", LShape, 1f) },
            Berths = new[] { Berth.OnLand("X-1", "nowhere", Vector2.Zero) },
        };

        var errors = layout.Validate();
        Assert.Contains(errors, e => e.Contains("bow-tie") && e.Contains("cross"));
        Assert.Contains(errors, e => e.Contains("at least three points"));
        Assert.Contains(errors, e => e.Contains("Duplicate land area id 'line'"));
        Assert.Contains(errors, e => e.Contains("unknown land area 'nowhere'"));

        var marina = CreateMarinaWithBoatyard();
        Assert.Throws<MarinaLayoutException>(() => marina.AddBerth(Berth.OnLand("X-2", "nowhere", Vector2.Zero)));
        Assert.Throws<MarinaLayoutException>(() => marina.AddBerth(Berth.OnLand("X-3", "yard", Vector2.Zero) with { PierId = "A" }));
    }

    [Fact]
    public void LandAreas_GetOneWorldSpaceMeshEach_AndBreakwatersAreRocks()
    {
        var marina = CreateMarinaWithBoatyard();
        var yard = marina.Meshes.Get(MeshIds.ForLand(0));

        Assert.Equal(1.5f, yard.Bounds.Max.Y, 3);
        Assert.Equal(-LandMeshFactory.WallDepth, yard.Bounds.Min.Y, 3);

        // Dozens of rocks rather than a box, reaching about the crest height: instances of a few shared rock meshes
        // standing on the breakwater's core, rather than one mesh with every rock baked in.
        var objects = marina.BuildRenderFrame().Objects;
        var rockIds = Enumerable.Range(0, MeshIds.RockVariants).Select(MeshIds.Rock).ToHashSet();
        var rocks = objects.Where(o => rockIds.Contains(o.MeshId) && !o.IsTransparent).ToList();
        Assert.True(rocks.Count > 50, $"only {rocks.Count} rocks");
        var crest = rocks.Max(rock => marina.Meshes.Get(rock.MeshId).Bounds.Max.Y * rock.World.M22 + rock.World.M42);
        Assert.InRange(crest, 1.8f, 3.2f);

        // Baked into one mesh, as LandMeshFactory.CreateGround still offers, it is the same pile.
        var baked = LandMeshFactory.CreateGround(1, marina.GetLandArea("rocks")!);
        Assert.True(baked.TriangleCount > 2000, $"only {baked.TriangleCount} triangles");
        Assert.InRange(baked.Bounds.Max.Y, 1.8f, 3.2f);

        Assert.Contains(objects, o => o.MeshId == MeshIds.ForLand(0) && o.World == Matrix4x4.Identity);
        Assert.Contains(objects, o => o.MeshId == MeshIds.ForLand(1));

        // A smaller layout drops the meshes it no longer needs.
        marina.InitializeLayout(new MarinaLayout { LandAreas = new[] { new LandArea("only", LShape, 1f) } });
        Assert.True(marina.Meshes.TryGet(MeshIds.ForLand(0), out _));
        Assert.False(marina.Meshes.TryGet(MeshIds.ForLand(1), out _));
    }

    [Fact]
    public void LandBerths_AreQueryable_AndCanBeSelectedAndUpdatedLikeWaterBerths()
    {
        var marina = CreateMarinaWithBoatyard();

        Assert.Equal(new[] { "Y-1", "Y-2" }, marina.GetBerthsByLandArea("YARD").Select(s => s.Id));
        Assert.Equal("Boatyard", marina.GetLandArea("yard")?.DisplayName);
        Assert.Null(marina.GetBerth("Y-1")!.PierId);

        BerthSelectedEventArgs? selected = null;
        marina.BerthSelected += (_, e) => selected = e;
        Assert.True(marina.SelectBerth("Y-1"));
        Assert.Equal("yard", selected!.LandArea?.Id);
        Assert.Null(selected.Pier);
        Assert.Contains("Boatyard", selected.Tooltip.Subtitle);

        var boat = new Boat("B1", "Hauled", BoatType.MonohullSailboat) { LengthMeters = 10f, BeamMeters = 3.4f };
        foreach (var status in new[] { BerthStatus.Occupied, BerthStatus.Reserved, BerthStatus.TemporarilyFree, BerthStatus.Free })
        {
            Assert.Equal(status, marina.SetBerthStatus("Y-1", status, boat).Status);
        }
    }

    [Fact]
    public void BoatOnLand_RestsOnItsCradle_AndIsPickedFromAbove()
    {
        var marina = CreateMarinaWithBoatyard();
        var boat = new Boat("B1", "Hauled", BoatType.MonohullSailboat) { LengthMeters = 10f, BeamMeters = 3.4f };
        marina.AssignBoat("Y-1", boat);

        var instance = BerthPlacement.EnumerateBoats(
            marina.GetBerths(), marina.GetBerth, marina.GetMultiBerth, BerthStatusFilter.All,
            berth => berth.IsOnLand ? 1.5f : null, marina.Meshes).Single();
        var mesh = marina.Meshes.Get(MeshIds.ForBoat(boat.Type));
        var keelY = Vector3.Transform(new Vector3(0f, mesh.Bounds.Min.Y, 0f), instance.World).Y;
        Assert.True(instance.OnLand);
        Assert.Equal(1.5f + BerthPlacement.CradleHeight, keelY, 2);

        var objects = marina.BuildRenderFrame().Objects;
        Assert.Contains(objects, o => o.MeshId == MeshIds.ForBoat(boat.Type) && (o.Animation & RenderAnimation.FloatOnWater) == 0);

        // Straight down onto the empty land berth: the pad sits on the land, not on the water.
        var hit = ScenePicker.Pick(
            new Ray(new Vector3(5, 50, 20), -Vector3.UnitY), marina.GetBerths(), Array.Empty<BoatInstance>(), marina.Meshes,
            berth => berth.IsOnLand ? 1.5f : null);
        Assert.Equal("Y-2", hit?.BerthId);
        Assert.Equal(1.5f + BerthPlacement.LandPadLift, hit!.Value.WorldPoint.Y, 3);
    }

    [Fact]
    public void SingleSidedPier_RejectsBerthsOnItsClosedSide()
    {
        var pier = new Pier("Q", "Quay pontoon", Vector2.Zero, 90f, 30f) { BerthingSides = PierSides.Left };

        Assert.True(pier.HasBerthsOn(PierSide.Left));
        Assert.False(pier.HasBerthsOn(PierSide.Right));
        Assert.Throws<InvalidOperationException>(() => BerthGenerator.AtPier(pier, "Q-R01", PierSide.Right, 0f, 5f, 10f));
        Assert.Throws<InvalidOperationException>(() => new MarinaLayoutBuilder().AddPier(pier, d => d.AddBerths(PierSide.Right, 2, 5f, 10f)));
        Assert.Equal(3, BerthGenerator.AlongPier(pier, PierSide.Left, 3, 5f, 10f).Count);
        Assert.Contains(new MarinaLayout { Piers = new[] { pier with { BerthingSides = 0 } } }.Validate(), e => e.Contains("berthing sides"));
    }

    [Theory]
    [InlineData(PierType.Concrete)]
    [InlineData(PierType.FloatingConcrete)]
    public void SingleSidedPier_DrawsMooringPointsOnlyOnItsOpenSide(PierType type)
    {
        var marina = new MarinaVisualizer();
        // Heading 0 (along +Z): looking from the start, the left-hand side is +X.
        marina.InitializeLayout(new MarinaLayout
        {
            Piers = new[] { new Pier("Q", "Quay pontoon", Vector2.Zero, 0f, 40f, 3f, type) { BerthingSides = PierSides.Left } },
        });

        var mooringPoints = marina.BuildRenderFrame().Objects
            .Where(o => o.MeshId is MeshIds.Cylinder or MeshIds.Piling)
            .Select(o => o.World.Translation.X)
            .ToList();

        Assert.NotEmpty(mooringPoints);
        Assert.All(mooringPoints, x => Assert.True(x > 0f, $"mooring point at x = {x} on the closed side"));
    }

    [Theory]
    [InlineData(PierType.Concrete)]
    [InlineData(PierType.FloatingWooden)]
    public void SingleSidedPier_RaisesItsEdgeOnlyOnTheSideBoatsComeTo(PierType type)
    {
        // Heading 0 (along +Z): looking from the start, the left-hand side is +X.
        var single = Drawn(type, PierSides.Left);
        var edges = RaisedEdges(single, type);

        Assert.NotEmpty(edges);
        Assert.All(edges, x => Assert.True(x > 0f, $"a raised edge at x = {x} on the side with no berths"));

        // A pier that berths on both sides still gets both of its edges.
        var both = RaisedEdges(Drawn(type, PierSides.Both), type);
        Assert.Contains(both, x => x > 0f);
        Assert.Contains(both, x => x < 0f);
        Assert.Equal(edges.Count * 2, both.Count);
    }

    /// <summary>One pier of the given kind, drawn on its own.</summary>
    private static IReadOnlyList<RenderObject> Drawn(PierType type, PierSides sides)
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(new MarinaLayout
        {
            Piers = new[] { new Pier("Q", "Quay pontoon", Vector2.Zero, 0f, 40f, 3f, type) { BerthingSides = sides } },
        });

        return marina.BuildRenderFrame().Objects;
    }

    /// <summary>
    /// Where the long raised strips along a pier's edges are: the kerb of a fixed pier, the waler of a wooden one.
    /// Both run the whole length, which is what tells them from the deck, the seams and the fittings.
    /// </summary>
    private static List<float> RaisedEdges(IReadOnlyList<RenderObject> objects, PierType type) =>
        objects
            .Where(o => o.MeshId == MeshIds.UnitBox)
            .Where(o => o.World.M33 >= 39f)                                  // runs the length of the pier
            .Where(o => o.World.M11 <= 0.4f)                                 // and is a narrow strip, not the deck
            .Where(o => type != PierType.Concrete || o.World.Translation.Y > 0.5f)   // a kerb stands on the deck
            .Select(o => o.World.Translation.X)
            .ToList();

    [Theory]
    [InlineData(PierType.FloatingWooden)]
    [InlineData(PierType.FloatingConcrete)]
    public void FloatingPiers_HaveNoGuidePiles(PierType type)
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(new MarinaLayout { Piers = new[] { new Pier("F", "Floating", Vector2.Zero, 0f, 60f, 3f, type) } });

        var objects = marina.BuildRenderFrame().Objects;
        Assert.DoesNotContain(objects, o => o.MeshId == MeshIds.Piling);
        // Only short deck fittings (cleats) may use the cylinder mesh; nothing reaches down into the water.
        Assert.DoesNotContain(objects, o => o.MeshId == MeshIds.Cylinder && o.World.Translation.Y < 0f);
    }

    [Fact]
    public void SampleMarina_LandBerthsLieOnTheirLand_AndWaterBerthsStayOffLand()
    {
        var layout = MockMarinaFactory.CreateSampleMarina();
        Assert.Empty(layout.Validate());

        var land = layout.LandAreas.ToDictionary(l => l.Id, StringComparer.OrdinalIgnoreCase);
        var landBerths = layout.Berths.Where(s => s.IsOnLand).ToList();
        Assert.True(landBerths.Count >= 15);
        Assert.Contains(layout.Piers, d => d.BerthingSides != PierSides.Both);
        Assert.Contains(layout.LandAreas, l => l.Kind == LandKind.Breakwater);

        foreach (var berth in landBerths)
        {
            Assert.All(berth.Bounds.GetCorners(), c => Assert.True(land[berth.LandAreaId!].Contains(c), $"{berth.Id} corner {c} is off its land area"));
        }

        foreach (var berth in layout.Berths.Where(s => !s.IsOnLand))
        {
            Assert.All(layout.LandAreas, l => Assert.False(l.Contains(berth.Center), $"{berth.Id} is on land area {l.Id}"));
        }

        // Land berths don't overlap each other.
        foreach (var berth in landBerths)
        {
            Assert.DoesNotContain(landBerths, other => other.Id != berth.Id && other.Bounds.Contains(berth.Center));
        }

        var marina = new MarinaVisualizer();
        marina.InitializeLayout(layout);
        Assert.NotEmpty(marina.BuildRenderFrame().Objects);
    }
}
