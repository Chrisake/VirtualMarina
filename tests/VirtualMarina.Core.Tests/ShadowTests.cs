using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Rendering;
using VirtualMarina.Core.Serialization;
using VirtualMarina.SampleData;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// Shadows: the boats and the piers squashed onto the ground along the sun's rays. What matters is that a shadow
/// lands where the sun says it should, on the right ground, and that it can be switched off entirely.
/// </summary>
public class ShadowTests
{
    /// <summary>A marina with one pier, its berths, and a boat in the first of them.</summary>
    private static MarinaVisualizer Harbour()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, 0), 0f, 40f));
        marina.AddBerth(new Berth("A-L01", "A", new Vector2(-6, 10), 90f, 12f, 5f));
        marina.AssignBoat("A-L01", new Boat("B1", "Aurora", BoatType.MotorYacht));
        marina.Lighting.SetSunAngles(azimuthDegrees: 0f, elevationDegrees: 45f);
        return marina;
    }

    private static List<RenderObject> Shadows(MarinaVisualizer marina) =>
        marina.BuildRenderFrame().Objects.Where(o => o.Tint.X == 0f && o.Tint.Y == 0f && o.Tint.Z == 0f && o.Tint.W is > 0f and < 1f).ToList();

    [Fact]
    public void ThePointUnderAThing_LandsWhereTheSunPutsIt()
    {
        // Sun due north at 45°: its direction is (0, sin45, cos45), so a shadow slides the other way, toward −Z.
        var sun = new Vector3(0f, MathF.Sqrt(0.5f), MathF.Sqrt(0.5f));
        var flatten = ShadowProjection.OntoPlane(sun, 0f);

        // A point 10 m up slides 10 m along −Z at 45°, and lands on the plane.
        var shadow = Vector3.Transform(new Vector3(3f, 10f, 4f), flatten);
        Assert.Equal(3f, shadow.X, 3);
        Assert.Equal(0f, shadow.Y, 3);
        Assert.Equal(4f - 10f, shadow.Z, 3);

        // A point already on the plane does not move.
        var onGround = Vector3.Transform(new Vector3(-2f, 0f, 7f), flatten);
        Assert.Equal(new Vector3(-2f, 0f, 7f), onGround);

        // The higher the sun, the shorter the shadow; straight overhead it is directly underneath.
        var noon = ShadowProjection.OntoPlane(Vector3.UnitY, 0f);
        Assert.Equal(new Vector3(3f, 0f, 4f), Vector3.Transform(new Vector3(3f, 10f, 4f), noon));
    }

    [Fact]
    public void AShadowLandsOnTheGroundItIsGiven_NotAlwaysOnTheWater()
    {
        var flatten = ShadowProjection.OntoPlane(Vector3.UnitY, 2.5f);
        Assert.Equal(2.5f, Vector3.Transform(new Vector3(1f, 9f, 1f), flatten).Y, 3);
    }

    [Fact]
    public void ASunOnTheHorizon_CastsNothingRatherThanAShadowToInfinity()
    {
        Assert.False(ShadowProjection.CanCast(new Vector3(0.99f, 0.01f, 0f)));
        Assert.False(ShadowProjection.CanCast(new Vector3(0f, -0.5f, 0.86f)));
        Assert.True(ShadowProjection.CanCast(new Vector3(0f, 0.7f, 0.7f)));

        // Even asked directly, it refuses to run the shadow off to the horizon.
        var grazing = ShadowProjection.OntoPlane(new Vector3(0f, 0.0001f, 1f), 0f);
        var shadow = Vector3.Transform(new Vector3(0f, 10f, 0f), grazing);
        Assert.True(MathF.Abs(shadow.Z) < 1000f, $"a grazing sun threw the shadow {shadow.Z:0} m away");

        var marina = Harbour();
        marina.Lighting.SetSunAngles(0f, 1f);
        Assert.Empty(Shadows(marina));
    }

    [Fact]
    public void TheMarinaCastsShadows_AndTheToggleTakesThemAllAway()
    {
        var marina = Harbour();

        var withShadows = marina.BuildRenderFrame().Objects.Count;
        Assert.NotEmpty(Shadows(marina));

        marina.Style.Shadows.IsEnabled = false;
        var without = marina.BuildRenderFrame().Objects.Count;
        Assert.Empty(Shadows(marina));
        Assert.True(without < withShadows, "switching shadows off left the scene the same size");

        // Back on again, and the scene is what it was.
        marina.Style.Shadows.IsEnabled = true;
        Assert.Equal(withShadows, marina.BuildRenderFrame().Objects.Count);

        // Strength 0 is the same as off, without having to reach for the toggle.
        marina.Style.Shadows.Strength = 0f;
        Assert.Empty(Shadows(marina));
    }

    [Fact]
    public void AShadowIsFlat_SitsJustAboveItsGround_AndIsDrawnAfterIt()
    {
        var marina = Harbour();
        var objects = marina.BuildRenderFrame().Objects;
        var shadows = Shadows(marina);
        Assert.NotEmpty(shadows);

        foreach (var shadow in shadows)
        {
            // Flattened: the second row of the transform contributes nothing upward any more.
            Assert.Equal(0f, shadow.World.M22, 4);
            Assert.InRange(shadow.World.Translation.Y, 0f, 0.5f);
        }

        // Every shadow is drawn after the water and the land it lies on, so it blends over them.
        var lastGround = objects.Select((o, i) => (o, i)).Where(e => e.o.MeshId >= MeshIds.LandBase || e.o.MeshId == MeshIds.Shoreline)
            .Select(e => e.i).DefaultIfEmpty(-1).Max();
        var firstShadow = objects.Select((o, i) => (o, i)).First(e => shadows.Contains(e.o)).i;
        Assert.True(firstShadow > lastGround, "a shadow is drawn before the ground it falls on");
    }

    [Fact]
    public void ABoatAshore_ThrowsItsShadowOnTheYard_NotTheWaterBelow()
    {
        const float yardHeight = 3f;
        var marina = new MarinaVisualizer();
        marina.AddLandArea(new LandArea(
            "yard",
            new[] { new Vector2(-30, -30), new Vector2(30, -30), new Vector2(30, 30), new Vector2(-30, 30) },
            yardHeight,
            LandKind.Quay));
        marina.AddBerth(Berth.OnLand("Y-01", "yard", Vector2.Zero, 0f, 12f, 5f));
        marina.AssignBoat("Y-01", new Boat("B2", "Ashore", BoatType.FishingBoat));
        marina.Lighting.SetSunAngles(0f, 50f);

        var shadows = Shadows(marina);
        Assert.NotEmpty(shadows);
        Assert.All(shadows, shadow => Assert.InRange(shadow.World.Translation.Y, yardHeight, yardHeight + 0.5f));
    }

    [Fact]
    public void TheSettings_SurviveASaveAndLoad()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());
        marina.Style.Shadows.IsEnabled = false;
        marina.Style.Shadows.Strength = 0.4f;

        var copy = new MarinaVisualizer();
        MarinaDocument.Parse(MarinaDocument.FromVisualizer(marina, generator: "tests").ToJson()).ApplyTo(copy);

        Assert.False(copy.Style.Shadows.IsEnabled);
        Assert.Equal(0.4f, copy.Style.Shadows.Strength, 3);
    }

    [Fact]
    public void MovingTheSun_MovesTheShadows()
    {
        var marina = Harbour();
        var north = Shadows(marina).Select(s => s.World.Translation).ToList();

        marina.Lighting.SetSunAngles(azimuthDegrees: 180f, elevationDegrees: 45f);
        var south = Shadows(marina).Select(s => s.World.Translation).ToList();

        Assert.Equal(north.Count, south.Count);
        Assert.NotEqual(north, south);
    }

    [Fact]
    public void TreesOnALandArea_CastShadowsOnIt()
    {
        const float lawnHeight = 1.2f;
        var marina = new MarinaVisualizer();
        var lawn = new LandArea(
            "lawn",
            new[] { new Vector2(-40, -40), new Vector2(40, -40), new Vector2(40, 40), new Vector2(-40, 40) },
            lawnHeight,
            LandKind.Grass);

        marina.AddLandArea(lawn with { Trees = LandArea.GenerateTrees(lawn.Points, 8f, new Random(4)) });
        marina.Lighting.SetSunAngles(0f, 40f);
        Assert.NotEmpty(marina.GetLandArea("lawn")!.Trees);

        // The trees are a mesh of their own, drawn over the ground rather than baked into it.
        var objects = marina.BuildRenderFrame().Objects;
        Assert.Contains(objects, o => o.MeshId == MeshIds.ForLandTrees(0));

        var treeShadow = Shadows(marina).SingleOrDefault(o => o.MeshId == MeshIds.ForLandTrees(0));
        Assert.True(treeShadow != default, "the trees cast no shadow");
        Assert.Equal(0f, treeShadow.World.M22, 4);
        Assert.InRange(treeShadow.World.Translation.Y, lawnHeight, lawnHeight + 0.5f);

        // Hiding the trees takes their shadow with them.
        marina.Style.Land.ShowTrees = false;
        Assert.DoesNotContain(marina.BuildRenderFrame().Objects, o => o.MeshId == MeshIds.ForLandTrees(0));
    }

    [Fact]
    public void EverythingStandingOnTheMarinaCastsAShadow()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());
        marina.Lighting.SetSunAngles(30f, 45f);

        var cast = Shadows(marina).Select(o => o.MeshId).ToHashSet();

        // The piers and their fittings, the boats, and the trees ashore.
        Assert.Contains(MeshIds.UnitBox, cast);                       // decks, kerbs, pedestals, cradles
        Assert.Contains(MeshIds.Piling, cast);                        // piles
        Assert.Contains(MeshIds.Cylinder, cast);                      // bollards and cleats
        Assert.Contains(MeshIds.ForBoat(BoatType.MotorYacht), cast);  // boats
        Assert.Contains(cast, id => id >= MeshIds.LandTreesBase);     // trees on the land areas

        // The ground itself does not shadow itself, and neither do the labels or the markers.
        Assert.DoesNotContain(MeshIds.Water, cast);
        Assert.DoesNotContain(MeshIds.Shoreline, cast);
        Assert.DoesNotContain(MeshIds.SelectionMarker, cast);
        Assert.DoesNotContain(cast, id => id >= MeshIds.GlyphBase && id < MeshIds.Shoreline);
        Assert.DoesNotContain(cast, id => id >= MeshIds.LandBase && id < MeshIds.LandTreesBase);
    }
}
