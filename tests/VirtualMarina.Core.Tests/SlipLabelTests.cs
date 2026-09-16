using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Picking;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Tests;

public class SlipLabelTests
{
    /// <summary>Six 5-character slip ids: two Free, two Occupied, one Reserved, one Temporarily Free.</summary>
    private static MarinaVisualizer CreateMarina()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(new MarinaLayoutBuilder("Labels")
            .AddDock("A", "Dock A", Vector2.Zero, 0f, 40f, dock => dock
                .AddSlips(DockSide.Left, 3, 5f, 12f)
                .AddSlips(DockSide.Right, 3, 5f, 12f))
            .Build());
        marina.SetViewportSize(800, 600);

        var boat = new Boat("B", "Boat", BoatType.FishingBoat) { LengthMeters = 8, BeamMeters = 3 };
        marina.AssignBoat("A-L02", boat);
        marina.ReserveSlip("A-L03", boat);
        marina.MarkTemporarilyFree("A-R01", boat);
        marina.AssignBoat("A-R03", boat);
        return marina;
    }

    private static List<RenderObject> Glyphs(MarinaVisualizer marina) =>
        marina.BuildRenderFrame().Objects.Where(o => o.MeshId >= MeshIds.GlyphBase).ToList();

    [Theory]
    [InlineData(SlipLabelMode.None, 0)]
    [InlineData(SlipLabelMode.OnlyFree, 2)]
    [InlineData(SlipLabelMode.NonOccupied, 4)]
    [InlineData(SlipLabelMode.All, 6)]
    public void Mode_LabelsTheExpectedSlips(SlipLabelMode mode, int labeledSlips)
    {
        var marina = CreateMarina();
        Assert.Equal(SlipLabelMode.None, marina.SlipLabelMode); // default

        marina.SlipLabelMode = mode;

        Assert.Equal(labeledSlips * "A-L01".Length, Glyphs(marina).Count);
    }

    [Fact]
    public void ChangingMode_RebuildsScene()
    {
        var marina = CreateMarina();
        var version = marina.BuildRenderFrame().SceneVersion;

        marina.SlipLabelMode = SlipLabelMode.All;

        Assert.Equal(version + 1, marina.BuildRenderFrame().SceneVersion);
        Assert.Throws<ArgumentOutOfRangeException>(() => marina.SlipLabelMode = (SlipLabelMode)42);
    }

    [Fact]
    public void HiddenAndFilteredSlips_AreNotLabeled_AndLabelFollowsDisplayName()
    {
        var marina = CreateMarina();
        marina.SlipLabelMode = SlipLabelMode.All;

        marina.SetSlipVisible("A-L01", false);
        marina.SetStatusFilter(SlipStatusFilter.All & ~SlipStatusFilter.Occupied);
        marina.UpdateSlip(new SlipUpdate("A-R02") { Label = "12" });

        // Visible, unfiltered: A-L03, A-R01 (5 chars each) and A-R02 relabeled "12".
        Assert.Equal(5 + 5 + 2, Glyphs(marina).Count);
    }

    [Fact]
    public void Label_SitsOnTheWaterPastTheOpenEnd_AndFitsTheSlipWidth()
    {
        var marina = CreateMarina();
        marina.SlipLabelMode = SlipLabelMode.OnlyFree;
        var slip = marina.GetSlip("A-L01")!;

        var (center, height, _, reading) = SlipPlacement.LabelPlacement(slip, slip.DisplayName.Length);
        var width = GlyphFont.MeasureWidth(slip.DisplayName.Length) * height;

        Assert.False(slip.Bounds.Contains(center), "label must be outside the slip");
        Assert.True(Vector2.Dot(center - slip.Center, slip.Forward) < -slip.Length * 0.5f, "label must be past the seaward end");
        Assert.True(width <= slip.Width * SlipPlacement.LabelWidthFraction + 1e-3f, $"label width {width} must leave a gap within slip width {slip.Width}");
        Assert.InRange(height, SlipPlacement.MinLabelHeight, SlipPlacement.MaxLabelHeight);
        Assert.Equal(0f, Vector2.Dot(reading, slip.Forward), 3); // written across the slip

        var glyphs = Glyphs(marina).Where(g => Vector2.Distance(new Vector2(g.World.M41, g.World.M43), center) < slip.Width).ToList();
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
        var vertex = ShaderSources.ModelVertex(ShaderDialect.WebGL2);
        Assert.Contains("vmMaxWaveHeight()", vertex);
        Assert.Contains("(uAnimation & 8) != 0", vertex);
        Assert.Contains("float[4](1.0, 0.6, 0.35, 0.22)", vertex);

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
        var slip = new Slip("S", "A", Vector2.Zero, 0f, 10f, 4f);
        var shortLabel = SlipPlacement.LabelPlacement(slip, 3);
        var longLabel = SlipPlacement.LabelPlacement(slip, 12);

        Assert.True(longLabel.Height < shortLabel.Height);
        Assert.Equal(SlipPlacement.MaxLabelHeight, SlipPlacement.LabelPlacement(slip with { Width = 30f }, 2).Height);
    }

    [Fact]
    public void Text_ReadsLeftToRight_AndIsNotMirrored_WhenViewedWithItsTopUp()
    {
        var marina = CreateMarina();
        marina.SlipLabelMode = SlipLabelMode.All;
        marina.UpdateSlip(new SlipUpdate("A-R02") { Label = "LL" });
        var slip = marina.GetSlip("A-R02")!;
        var (center, _, upHeading, _) = SlipPlacement.LabelPlacement(slip, 2);

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
}
