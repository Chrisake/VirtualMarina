using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Rendering;
using VirtualMarina.Core.Serialization;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// A real font captured into a design. The outlines travel with the file, so the marina reads the same on a machine
/// that has never had the font installed — which is the whole point of storing shapes rather than a font name.
/// </summary>
public class LabelFontTests
{
    /// <summary>A square ring: an outer box with a hole in it, like an O.</summary>
    private static LabelGlyph Ring(char c, float advance = 1f) => new(
        c,
        advance,
        new IReadOnlyList<Vector2>[]
        {
            new[] { new Vector2(0.1f, 0f), new Vector2(0.9f, 0f), new Vector2(0.9f, 1f), new Vector2(0.1f, 1f) },
            new[] { new Vector2(0.3f, 0.2f), new Vector2(0.7f, 0.2f), new Vector2(0.7f, 0.8f), new Vector2(0.3f, 0.8f) },
        });

    /// <summary>A solid box, like an I with no counter.</summary>
    private static LabelGlyph Solid(char c, float advance = 0.5f) => new(
        c,
        advance,
        new IReadOnlyList<Vector2>[]
        {
            new[] { new Vector2(0.1f, 0f), new Vector2(0.4f, 0f), new Vector2(0.4f, 1f), new Vector2(0.1f, 1f) },
        });

    private static LabelFontDefinition SampleFont() =>
        new("Test Sans", new[] { Ring('O'), Solid('I'), new LabelGlyph(' ', 0.3f, Array.Empty<IReadOnlyList<Vector2>>()) });

    private static float Area(MeshData mesh)
    {
        var total = 0f;
        for (var i = 0; i < mesh.Indices.Length; i += 3)
        {
            var a = Vertex(mesh, (int)mesh.Indices[i]);
            var b = Vertex(mesh, (int)mesh.Indices[i + 1]);
            var c = Vertex(mesh, (int)mesh.Indices[i + 2]);
            total += MathF.Abs(((b.X - a.X) * (c.Z - a.Z)) - ((c.X - a.X) * (b.Z - a.Z))) * 0.5f;
        }

        return total;
    }

    private static Vector3 Vertex(MeshData mesh, int index)
    {
        var at = index * 9;
        return new Vector3(mesh.Vertices[at], mesh.Vertices[at + 1], mesh.Vertices[at + 2]);
    }

    [Fact]
    public void ACounterIsLeftOpen()
    {
        var font = SampleFont();
        var meshes = font.CreateMeshes().ToDictionary(mesh => mesh.Id);

        Assert.True(font.TryGetMeshId('O', out var ring));
        Assert.True(font.TryGetMeshId('I', out var solid));

        // The O covers its outline less its counter: 0.8 x 1 minus 0.4 x 0.6.
        Assert.Equal(0.8f - 0.24f, Area(meshes[ring]), tolerance: 0.001f);

        // The I is solid: 0.3 x 1.
        Assert.Equal(0.3f, Area(meshes[solid]), tolerance: 0.001f);
    }

    [Fact]
    public void AGlyphIsCentredOnItsOwnAdvance_AndOnTheCapHeight()
    {
        var font = SampleFont();
        var mesh = font.CreateMeshes().Single(m => font.TryGetMeshId('O', out var id) && m.Id == id);

        var xs = new List<float>();
        var zs = new List<float>();
        for (var i = 0; i < mesh.Indices.Length; i++)
        {
            var v = Vertex(mesh, (int)mesh.Indices[i]);
            xs.Add(v.X);
            zs.Add(v.Z);
        }

        // Reading runs along −X, and the glyph spans 0.1..0.9 of a 1.0 advance, so it sits −0.4..+0.4 about the middle.
        Assert.Equal(-0.4f, xs.Min(), tolerance: 0.001f);
        Assert.Equal(0.4f, xs.Max(), tolerance: 0.001f);

        // Baseline at 0 and cap height at 1 become −0.5..+0.5, matching the built-in lettering.
        Assert.Equal(-0.5f, zs.Min(), tolerance: 0.001f);
        Assert.Equal(0.5f, zs.Max(), tolerance: 0.001f);
    }

    [Fact]
    public void TextIsMeasuredByTheAdvanceOfEachCharacter()
    {
        var font = SampleFont();

        Assert.Equal(1f, font.AdvanceOf('O'));
        Assert.Equal(0.5f, font.AdvanceOf('I'));
        Assert.Equal(0.3f, font.AdvanceOf(' '));

        Assert.Equal(2.5f, font.MeasureWidth("OOI"), tolerance: 0.001f);
        Assert.Equal(1.8f, font.MeasureWidth("O I"), tolerance: 0.001f);
        Assert.Equal(0f, font.MeasureWidth(string.Empty));
    }

    [Fact]
    public void ACharacterTheFontDoesNotCarry_FallsBackRatherThanVanishing()
    {
        var font = SampleFont();

        Assert.False(font.TryGetGlyph('Z', out _));
        Assert.False(font.TryGetMeshId('Z', out _));

        // It still takes up room, so a name with an odd character in it lays out instead of collapsing.
        Assert.True(font.AdvanceOf('Z') > 0f);
    }

    [Fact]
    public void TheOutlinesSurviveAFileEvenWhereTheFontIsUnknown()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", Vector2.Zero, 0f, 40f));
        marina.Style.Labels.Font = SampleFont();

        var json = MarinaDocument.FromVisualizer(marina, generator: "tests").ToJson();

        // The shapes are in the file, not just the name of a font the other machine may not have.
        Assert.Contains("Test Sans", json, StringComparison.Ordinal);

        var reloaded = new MarinaVisualizer();
        MarinaDocument.Parse(json).ApplyTo(reloaded);

        var font = reloaded.Style.Labels.Font;
        Assert.NotNull(font);
        Assert.Equal("Test Sans", font!.Name);
        Assert.Equal(3, font.Glyphs.Count);
        Assert.Equal(1f, font.AdvanceOf('O'), tolerance: 0.001f);

        // And it draws the same thing after the round trip.
        var before = SampleFont().CreateMeshes().Sum(Area);
        Assert.Equal(before, font.CreateMeshes().Sum(Area), tolerance: 0.001f);
    }

    [Fact]
    public void ADesignWithNoCapturedFont_UsesTheBuiltInLettering()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", Vector2.Zero, 0f, 40f));
        Assert.Null(marina.Style.Labels.Font);

        var json = MarinaDocument.FromVisualizer(marina, generator: "tests").ToJson();
        var reloaded = new MarinaVisualizer();
        MarinaDocument.Parse(json).ApplyTo(reloaded);

        Assert.Null(reloaded.Style.Labels.Font);
    }

    [Fact]
    public void SettingAFont_PutsItsGlyphsInTheMeshLibrary_AndTakesThemOutAgain()
    {
        var marina = new MarinaVisualizer();
        var font = SampleFont();
        Assert.True(font.TryGetMeshId('O', out var id));
        Assert.False(marina.Meshes.TryGet(id, out _));

        marina.Style.Labels.Font = font;
        Assert.True(marina.Meshes.TryGet(id, out _), "the glyphs were not registered");

        var version = marina.Meshes.Version;
        marina.Style.Labels.Font = null;
        Assert.False(marina.Meshes.TryGet(id, out _), "the glyphs were left behind");
        Assert.True(marina.Meshes.Version > version, "the renderers were not told to re-upload");
    }

    [Fact]
    public void LabelsAreDrawnFromTheCapturedFont_WhenThereIsOne()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        marina.Designer.IsActive = true;
        marina.Designer.CreateBerths("A", PierSide.Left, 0f, 60f);
        marina.Designer.IsActive = false;
        marina.BerthLabelMode = BerthLabelMode.All;

        int[] Glyphs(int from, int to) => marina.BuildRenderFrame().Objects
            .Select(o => o.MeshId)
            .Where(id => id >= from && id < to)
            .ToArray();

        // The built-in lettering to begin with.
        Assert.NotEmpty(Glyphs(MeshIds.GlyphBase, MeshIds.FontGlyphBase));
        Assert.Empty(Glyphs(MeshIds.FontGlyphBase, MeshIds.Shoreline));

        // A font that carries every character of the names takes over entirely.
        var names = marina.GetBerths().SelectMany(berth => berth.DisplayName).Distinct();
        marina.Style.Labels.Font = new LabelFontDefinition("Full", names.Select(c => Solid(c)));

        Assert.Empty(Glyphs(MeshIds.GlyphBase, MeshIds.FontGlyphBase));
        Assert.NotEmpty(Glyphs(MeshIds.FontGlyphBase, MeshIds.Shoreline));
    }

    [Fact]
    public void AFontMissingSomeCharacters_DrawsThoseWithTheBuiltInLettering()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        marina.Designer.IsActive = true;
        marina.Designer.CreateBerths("A", PierSide.Left, 0f, 60f);
        marina.Designer.IsActive = false;
        marina.BerthLabelMode = BerthLabelMode.All;

        // Only the digits, so the letters and the dash have to come from somewhere else.
        marina.Style.Labels.Font = new LabelFontDefinition("Digits", "0123456789".Select(c => Solid(c)));

        var ids = marina.BuildRenderFrame().Objects.Select(o => o.MeshId).ToArray();
        Assert.Contains(ids, id => id >= MeshIds.FontGlyphBase && id < MeshIds.Shoreline);
        Assert.Contains(ids, id => id >= MeshIds.GlyphBase && id < MeshIds.FontGlyphBase);
    }

    [Fact]
    public void AnEmptyOrBrokenFont_IsRefusedRatherThanDrawnAsNothing()
    {
        Assert.NotEmpty(new LabelFontDefinition("Empty", Array.Empty<LabelGlyph>()).Validate());
        Assert.NotEmpty(new LabelFontDefinition("Bad", new[] { new LabelGlyph('A', float.NaN, Array.Empty<IReadOnlyList<Vector2>>()) }).Validate());

        Assert.Null(LabelFontDefinition.Decode(null, new[] { "A 1" }));
        Assert.Null(LabelFontDefinition.Decode("Name", null));
        Assert.Null(LabelFontDefinition.Decode("Name", Array.Empty<string>()));
        Assert.Null(LabelFontDefinition.Decode("Name", new[] { "nonsense" }));
    }
}
