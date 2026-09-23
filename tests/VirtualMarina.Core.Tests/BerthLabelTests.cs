using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Picking;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Tests;

public class BerthLabelTests
{
    /// <summary>Six 5-character berth ids: two Free, two Occupied, one Reserved, one Temporarily Free.</summary>
    private static MarinaVisualizer CreateMarina()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(new MarinaLayoutBuilder("Labels")
            .AddPier("A", "Pier A", Vector2.Zero, 0f, 40f, pier => pier
                .AddBerths(PierSide.Left, 3, 5f, 12f)
                .AddBerths(PierSide.Right, 3, 5f, 12f))
            .Build());
        marina.SetViewportSize(800, 600);

        var boat = new Boat("B", "Boat", BoatType.FishingBoat) { LengthMeters = 8, BeamMeters = 3 };
        marina.AssignBoat("A-L02", boat);
        marina.ReserveBerth("A-L03", boat);
        marina.MarkTemporarilyFree("A-R01", boat);
        marina.AssignBoat("A-R03", boat);
        return marina;
    }

    private static List<RenderObject> Glyphs(MarinaVisualizer marina) =>
        marina.BuildRenderFrame().Objects.Where(o => o.MeshId >= MeshIds.GlyphBase).ToList();

    [Theory]
    [InlineData(BerthLabelMode.None, 0)]
    [InlineData(BerthLabelMode.OnlyFree, 2)]
    [InlineData(BerthLabelMode.NonOccupied, 4)]
    [InlineData(BerthLabelMode.All, 6)]
    public void Mode_LabelsTheExpectedBerths(BerthLabelMode mode, int labeledBerths)
    {
        var marina = CreateMarina();
        Assert.Equal(BerthLabelMode.None, marina.BerthLabelMode); // default

        marina.BerthLabelMode = mode;

        Assert.Equal(labeledBerths * "A-L01".Length, Glyphs(marina).Count);
    }

    [Fact]
    public void ChangingMode_RebuildsScene()
    {
        var marina = CreateMarina();
        var version = marina.BuildRenderFrame().SceneVersion;

        marina.BerthLabelMode = BerthLabelMode.All;

        Assert.Equal(version + 1, marina.BuildRenderFrame().SceneVersion);
        Assert.Throws<ArgumentOutOfRangeException>(() => marina.BerthLabelMode = (BerthLabelMode)42);
    }

    [Fact]
    public void HiddenAndFilteredBerths_AreNotLabeled_AndLabelFollowsDisplayName()
    {
        var marina = CreateMarina();
        marina.BerthLabelMode = BerthLabelMode.All;

        marina.SetBerthVisible("A-L01", false);
        marina.SetStatusFilter(BerthStatusFilter.All & ~BerthStatusFilter.Occupied);
        marina.UpdateBerth(new BerthUpdate("A-R02") { Label = "12" });

        // Visible, unfiltered: A-L03, A-R01 (5 chars each) and A-R02 relabeled "12".
        Assert.Equal(5 + 5 + 2, Glyphs(marina).Count);
    }

    [Fact]
    public void Label_SitsOnTheWaterPastTheOpenEnd_AndFitsTheBerthWidth()
    {
        var marina = CreateMarina();
        marina.BerthLabelMode = BerthLabelMode.OnlyFree;
        var berth = marina.GetBerth("A-L01")!;

        var (center, height, _, reading) = BerthPlacement.LabelPlacement(berth, berth.DisplayName.Length);
        var width = GlyphFont.MeasureWidth(berth.DisplayName.Length) * height;

        Assert.False(berth.Bounds.Contains(center), "label must be outside the berth");
        Assert.True(Vector2.Dot(center - berth.Center, berth.Forward) < -berth.Length * 0.5f, "label must be past the seaward end");
        Assert.True(width <= berth.Width * BerthPlacement.LabelWidthFraction + 1e-3f, $"label width {width} must leave a gap within berth width {berth.Width}");
        Assert.InRange(height, BerthPlacement.MinLabelHeight, BerthPlacement.MaxLabelHeight);
        Assert.Equal(0f, Vector2.Dot(reading, berth.Forward), 3); // written across the berth

        var glyphs = Glyphs(marina).Where(g => Vector2.Distance(new Vector2(g.World.M41, g.World.M43), center) < berth.Width).ToList();
        Assert.Equal(5, glyphs.Count);
        Assert.All(glyphs, g =>
        {
            Assert.True(g.Animation.HasFlag(RenderAnimation.AboveWaves));
            Assert.False(g.Animation.HasFlag(RenderAnimation.FloatOnWater));
            Assert.True(g.World.M42 > 0f, "text starts above the calm water plane");
        });
    }

    [Fact]
    public void AboveWaves_LiftsTextOverTheHighestPossibleWave()
    {
        // The shader's bound is derived from the same amplitudes that build the waves.
        Assert.Equal(ShaderSources.WaveComponentAmplitudes.Sum(), ShaderSources.MaxWaveHeightFactor, 4);
        foreach (var vertex in new[] { ShaderSources.ModelVertex(ShaderDialect.WebGL2), ShaderSources.InstancedModelVertex(ShaderDialect.WebGL2) })
        {
            Assert.Contains("vmMaxWaveHeight()", vertex);
            Assert.Contains("(animation & 8) != 0", vertex);
            Assert.Contains("float[4](1.0, 0.6, 0.35, 0.22)", vertex);
        }

        // Whatever the wave phases, the surface never reaches the lifted text. Both the camera (MinEyeHeight) and the
        // text are above that level, so the straight line between them is too: no wave can cover a label.
        var water = new WaterSettings { WaveAmplitude = 0.35f };
        var textHeight = SceneBuilder.LabelHeightAboveWater + ShaderSources.MaxWaveHeightFactor * water.WaveAmplitude;
        var rng = new Random(3);
        for (var i = 0; i < 10_000; i++)
        {
            var height = ShaderSources.WaveComponentAmplitudes.Sum(a => a * water.WaveAmplitude * MathF.Sin((float)(rng.NextDouble() * MathF.Tau)));
            Assert.True(height < textHeight);
        }

        Assert.True(new CameraConstraints().MinEyeHeight > textHeight);
    }

    [Fact]
    public void LongNames_ShrinkToFit()
    {
        var berth = new Berth("S", "A", Vector2.Zero, 0f, 10f, 4f);
        var shortLabel = BerthPlacement.LabelPlacement(berth, 3);
        var longLabel = BerthPlacement.LabelPlacement(berth, 12);

        Assert.True(longLabel.Height < shortLabel.Height);
        Assert.Equal(BerthPlacement.MaxLabelHeight, BerthPlacement.LabelPlacement(berth with { Width = 30f }, 2).Height);
    }

    [Fact]
    public void Text_ReadsLeftToRight_AndIsNotMirrored_WhenViewedWithItsTopUp()
    {
        var marina = CreateMarina();
        marina.BerthLabelMode = BerthLabelMode.All;
        marina.UpdateBerth(new BerthUpdate("A-R02") { Label = "LL" });
        var berth = marina.GetBerth("A-R02")!;
        var (center, _, upHeading, _) = BerthPlacement.LabelPlacement(berth, 2);

        // Straight down, screen-up aligned with the text's up direction.
        marina.Camera.SetPose(new CameraPose(new Vector3(center.X, 0, center.Y), upHeading + 180f, 89f, 12f), immediate: true);

        var glyphs = Glyphs(marina).Where(g => Vector2.Distance(new Vector2(g.World.M41, g.World.M43), center) < 3f).ToList();
        Assert.Equal(2, glyphs.Count);

        Vector2 Screen(Vector3 world)
        {
            Assert.True(marina.TryProjectToScreen(world, out var p));
            return p;
        }

        // Characters are emitted in reading order: the first must be left of the second.
        Assert.True(Screen(glyphs[0].World.Translation).X < Screen(glyphs[1].World.Translation).X);

        // Glyph 'L': the top of its vertical stroke is on the left, the end of its foot on the right, the top above the foot.
        var mesh = marina.Meshes.Get(glyphs[0].MeshId);
        var points = Enumerable.Range(0, mesh.VertexCount)
            .Select(i => new Vector3(mesh.Vertices[i * MeshData.VertexStride], mesh.Vertices[i * MeshData.VertexStride + 1], mesh.Vertices[i * MeshData.VertexStride + 2]))
            .ToList();
        var top = points.OrderByDescending(p => p.Z).First();
        var footEnd = points.Where(p => p.Z < -0.3f).OrderBy(p => p.X).First(); // local −X is the reading direction
        var topScreen = Screen(Vector3.Transform(top, glyphs[0].World));
        var footScreen = Screen(Vector3.Transform(footEnd, glyphs[0].World));
        Assert.True(topScreen.X < footScreen.X, "the L's stem must be left of its foot");
        Assert.True(topScreen.Y < footScreen.Y, "the L's top must be above its foot on screen");
    }

    [Fact]
    public void GlyphFont_HasValidMeshes_AndMapsUnknownCharacters()
    {
        var library = MeshLibrary.CreateDefault(100f, 4, Vector2.Zero);
        foreach (var c in GlyphFont.SupportedCharacters)
        {
            Assert.True(GlyphFont.TryGetMeshId(c, out var id));
            var mesh = library.Get(id);
            Assert.True(mesh.TriangleCount >= 2, $"glyph '{c}' has geometry");
            Assert.InRange(mesh.Bounds.Max.Z - mesh.Bounds.Min.Z, 0.05f, 1.4f);
        }

        Assert.True(GlyphFont.TryGetMeshId('a', out var lower));
        Assert.True(GlyphFont.TryGetMeshId('A', out var upper));
        Assert.Equal(upper, lower);
        Assert.True(GlyphFont.TryGetMeshId('€', out var unknown));
        Assert.True(GlyphFont.TryGetMeshId('?', out var question));
        Assert.Equal(question, unknown);
        Assert.False(GlyphFont.TryGetMeshId(' ', out _));
    }

    [Fact]
    public void ABerthAshore_GetsItsLabel_StandingClearOfTheGround()
    {
        const float landHeight = 2.5f;
        var marina = new MarinaVisualizer();
        marina.AddLandArea(new LandArea(
            "yard",
            new[] { new Vector2(-30, -30), new Vector2(30, -30), new Vector2(30, 30), new Vector2(-30, 30) },
            landHeight,
            LandKind.Quay));
        marina.AddBerth(Berth.OnLand("Y-01", "yard", new Vector2(0, 0), 0f, 12f, 5f));
        marina.BerthLabelMode = BerthLabelMode.All;

        var glyphs = marina.BuildRenderFrame().Objects
            .Where(o => o.MeshId >= MeshIds.GlyphBase && o.MeshId < MeshIds.Shoreline)
            .ToList();

        Assert.NotEmpty(glyphs);

        // Every letter floats above the yard, not lying on it where a low camera would never see it.
        foreach (var glyph in glyphs)
        {
            var y = glyph.World.Translation.Y;
            Assert.True(y > landHeight + 0.2f, $"a letter sits {y - landHeight:0.00} m above the ground");
            Assert.True(y < landHeight + 2f, $"a letter floats {y - landHeight:0.00} m above the ground");
        }

        // It is pinned to the ground rather than riding the waves, which only water berths do.
        Assert.All(glyphs, glyph => Assert.Equal(RenderAnimation.None, glyph.Animation));
    }
}
