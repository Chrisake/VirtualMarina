using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Picking;
using VirtualMarina.Core.Rendering;
using VirtualMarina.SampleData;

namespace VirtualMarina.Core.Tests;

/// <summary>Polygon land areas, rock breakwaters, land slips and single-sided docks.</summary>
public class LandAndSingleSidedDockTests
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
                .AddSlip("Y-1", new Vector2(25, 5), headingDegrees: 90, length: 12, width: 5)
                .AddSlip("Y-2", new Vector2(5, 20), headingDegrees: 0, length: 12, width: 5))
            .AddLandArea(new LandArea("rocks", new OrientedRect(new Vector2(20, 60), new Vector2(60, 8), 0), 2.2f, LandKind.Breakwater))
            .AddDock("A", "Dock A", new Vector2(20, 12), 0f, 30f, dock => dock.AddSlips(DockSide.Right, 2, 5f, 10f))
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
            Slips = new[] { Slip.OnLand("X-1", "nowhere", Vector2.Zero) },
        };

        var errors = layout.Validate();
        Assert.Contains(errors, e => e.Contains("bow-tie") && e.Contains("cross"));
        Assert.Contains(errors, e => e.Contains("at least three points"));
        Assert.Contains(errors, e => e.Contains("Duplicate land area id 'line'"));
        Assert.Contains(errors, e => e.Contains("unknown land area 'nowhere'"));

        var marina = CreateMarinaWithBoatyard();
        Assert.Throws<MarinaLayoutException>(() => marina.AddSlip(Slip.OnLand("X-2", "nowhere", Vector2.Zero)));
        Assert.Throws<MarinaLayoutException>(() => marina.AddSlip(Slip.OnLand("X-3", "yard", Vector2.Zero) with { DockId = "A" }));
    }

    [Fact]
    public void LandAreas_GetOneWorldSpaceMeshEach_AndBreakwatersAreRocks()
    {
        var marina = CreateMarinaWithBoatyard();
        var yard = marina.Meshes.Get(MeshIds.ForLand(0));
        var rocks = marina.Meshes.Get(MeshIds.ForLand(1));

        Assert.Equal(1.5f, yard.Bounds.Max.Y, 3);
        Assert.Equal(-LandMeshFactory.WallDepth, yard.Bounds.Min.Y, 3);
        // Hundreds of rocks rather than a box, reaching about the crest height.
        Assert.True(rocks.TriangleCount > 2000, $"only {rocks.TriangleCount} triangles");
        Assert.InRange(rocks.Bounds.Max.Y, 1.8f, 3.2f);

        var objects = marina.BuildRenderFrame().Objects;
        Assert.Contains(objects, o => o.MeshId == MeshIds.ForLand(0) && o.World == Matrix4x4.Identity);
        Assert.Contains(objects, o => o.MeshId == MeshIds.ForLand(1));

        // A smaller layout drops the meshes it no longer needs.
        marina.InitializeLayout(new MarinaLayout { LandAreas = new[] { new LandArea("only", LShape, 1f) } });
        Assert.True(marina.Meshes.TryGet(MeshIds.ForLand(0), out _));
        Assert.False(marina.Meshes.TryGet(MeshIds.ForLand(1), out _));
    }

    [Fact]
    public void LandSlips_AreQueryable_AndCanBeSelectedAndUpdatedLikeWaterSlips()
    {
        var marina = CreateMarinaWithBoatyard();

        Assert.Equal(new[] { "Y-1", "Y-2" }, marina.GetSlipsByLandArea("YARD").Select(s => s.Id));
        Assert.Equal("Boatyard", marina.GetLandArea("yard")?.DisplayName);
        Assert.Null(marina.GetSlip("Y-1")!.DockId);

        SlipSelectedEventArgs? selected = null;
        marina.SlipSelected += (_, e) => selected = e;
        Assert.True(marina.SelectSlip("Y-1"));
        Assert.Equal("yard", selected!.LandArea?.Id);
        Assert.Null(selected.Dock);
        Assert.Contains("Boatyard", selected.Tooltip.Subtitle);

        var boat = new Boat("B1", "Hauled", BoatType.MonohullSailboat) { LengthMeters = 10f, BeamMeters = 3.4f };
        foreach (var status in new[] { SlipStatus.Occupied, SlipStatus.Reserved, SlipStatus.TemporarilyFree, SlipStatus.Free })
        {
            Assert.Equal(status, marina.SetSlipStatus("Y-1", status, boat).Status);
        }
    }

    [Fact]
    public void BoatOnLand_RestsOnItsCradle_AndIsPickedFromAbove()
    {
        var marina = CreateMarinaWithBoatyard();
        var boat = new Boat("B1", "Hauled", BoatType.MonohullSailboat) { LengthMeters = 10f, BeamMeters = 3.4f };
        marina.AssignBoat("Y-1", boat);

        var instance = SlipPlacement.EnumerateBoats(
            marina.GetSlips(), marina.GetSlip, marina.GetMultiSlipBerth, SlipStatusFilter.All,
            slip => slip.IsOnLand ? 1.5f : null, marina.Meshes).Single();
        var mesh = marina.Meshes.Get(MeshIds.ForBoat(boat.Type));
        var keelY = Vector3.Transform(new Vector3(0f, mesh.Bounds.Min.Y, 0f), instance.World).Y;
        Assert.True(instance.OnLand);
        Assert.Equal(1.5f + SlipPlacement.CradleHeight, keelY, 2);

        var objects = marina.BuildRenderFrame().Objects;
        Assert.Contains(objects, o => o.MeshId == MeshIds.ForBoat(boat.Type) && (o.Animation & RenderAnimation.FloatOnWater) == 0);

        // Straight down onto the empty land slip: the pad sits on the land, not on the water.
        var hit = ScenePicker.Pick(
            new Ray(new Vector3(5, 50, 20), -Vector3.UnitY), marina.GetSlips(), Array.Empty<BoatInstance>(), marina.Meshes,
            slip => slip.IsOnLand ? 1.5f : null);
        Assert.Equal("Y-2", hit?.SlipId);
        Assert.Equal(1.5f + SlipPlacement.LandPadLift, hit!.Value.WorldPoint.Y, 3);
    }

    [Fact]
    public void SingleSidedDock_RejectsSlipsOnItsClosedSide()
    {
        var dock = new Dock("Q", "Quay pontoon", Vector2.Zero, 90f, 30f) { BerthingSides = DockSides.Left };

        Assert.True(dock.HasBerthsOn(DockSide.Left));
        Assert.False(dock.HasBerthsOn(DockSide.Right));
        Assert.Throws<InvalidOperationException>(() => SlipGenerator.AtDock(dock, "Q-R01", DockSide.Right, 0f, 5f, 10f));
        Assert.Throws<InvalidOperationException>(() => new MarinaLayoutBuilder().AddDock(dock, d => d.AddSlips(DockSide.Right, 2, 5f, 10f)));
        Assert.Equal(3, SlipGenerator.AlongDock(dock, DockSide.Left, 3, 5f, 10f).Count);
        Assert.Contains(new MarinaLayout { Docks = new[] { dock with { BerthingSides = 0 } } }.Validate(), e => e.Contains("berthing sides"));
    }

    [Theory]
    [InlineData(DockType.Concrete)]
    [InlineData(DockType.FloatingConcrete)]
    public void SingleSidedDock_DrawsMooringPointsOnlyOnItsOpenSide(DockType type)
    {
        var marina = new MarinaVisualizer();
        // Heading 0: the Left side is −X.
        marina.InitializeLayout(new MarinaLayout
        {
            Docks = new[] { new Dock("Q", "Quay pontoon", Vector2.Zero, 0f, 40f, 3f, type) { BerthingSides = DockSides.Left } },
        });

        var mooringPoints = marina.BuildRenderFrame().Objects
            .Where(o => o.MeshId is MeshIds.Cylinder or MeshIds.Piling)
            .Select(o => o.World.Translation.X)
            .ToList();

        Assert.NotEmpty(mooringPoints);
        Assert.All(mooringPoints, x => Assert.True(x < 0f, $"mooring point at x = {x} on the closed side"));
    }

    [Theory]
    [InlineData(DockType.FloatingWooden)]
    [InlineData(DockType.FloatingConcrete)]
    public void FloatingDocks_HaveNoGuidePiles(DockType type)
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(new MarinaLayout { Docks = new[] { new Dock("F", "Floating", Vector2.Zero, 0f, 60f, 3f, type) } });

        var objects = marina.BuildRenderFrame().Objects;
        Assert.DoesNotContain(objects, o => o.MeshId == MeshIds.Piling);
        // Only short deck fittings (cleats) may use the cylinder mesh; nothing reaches down into the water.
        Assert.DoesNotContain(objects, o => o.MeshId == MeshIds.Cylinder && o.World.Translation.Y < 0f);
    }

    [Fact]
    public void SampleMarina_LandSlipsLieOnTheirLand_AndWaterSlipsStayOffLand()
    {
        var layout = MockMarinaFactory.CreateSampleMarina();
        Assert.Empty(layout.Validate());

        var land = layout.LandAreas.ToDictionary(l => l.Id, StringComparer.OrdinalIgnoreCase);
        var landSlips = layout.Slips.Where(s => s.IsOnLand).ToList();
        Assert.True(landSlips.Count >= 15);
        Assert.Contains(layout.Docks, d => d.BerthingSides != DockSides.Both);
        Assert.Contains(layout.LandAreas, l => l.Kind == LandKind.Breakwater);

        foreach (var slip in landSlips)
        {
            Assert.All(slip.Bounds.GetCorners(), c => Assert.True(land[slip.LandAreaId!].Contains(c), $"{slip.Id} corner {c} is off its land area"));
        }

        foreach (var slip in layout.Slips.Where(s => !s.IsOnLand))
        {
            Assert.All(layout.LandAreas, l => Assert.False(l.Contains(slip.Center), $"{slip.Id} is on land area {l.Id}"));
        }

        // Land slips don't overlap each other.
        foreach (var slip in landSlips)
        {
            Assert.DoesNotContain(landSlips, other => other.Id != slip.Id && other.Bounds.Contains(slip.Center));
        }

        var marina = new MarinaVisualizer();
        marina.InitializeLayout(layout);
        Assert.NotEmpty(marina.BuildRenderFrame().Objects);
    }
}
