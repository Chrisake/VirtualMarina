using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Rendering;
using VirtualMarina.Core.Serialization;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// The shape of the letters on a berth label, which is chosen separately from their weight. They are drawn as
/// strokes rather than set in an installed font, so every backend draws the same thing.
/// </summary>
public class LabelTypefaceTests
{
    private static int Triangles(MeshData mesh) => mesh.Indices.Length / 3;

    private static MeshData Glyph(char c, LabelFont font, LabelTypeface typeface)
    {
        Assert.True(GlyphFont.TryGetMeshId(c, font, typeface, out var id));
        return GlyphFont.CreateAll().Single(mesh => mesh.Id == id);
    }

    [Fact]
    public void EveryTypefaceAndWeight_HasItsOwnGlyphs()
    {
        var meshes = GlyphFont.CreateAll().ToList();
        var faces = Enum.GetValues<LabelFont>().Length;
        var typefaces = Enum.GetValues<LabelTypeface>().Length;

        Assert.Equal(GlyphFont.SupportedCharacters.Count * faces * typefaces, meshes.Count);
        Assert.Equal(meshes.Count, meshes.Select(mesh => mesh.Id).Distinct().Count());
        Assert.All(meshes, mesh => Assert.True(Triangles(mesh) > 0, $"mesh {mesh.Id} has nothing in it"));

        // Every combination is reachable, and no two of them land on the same mesh.
        var ids = new HashSet<int>();
        foreach (var typeface in Enum.GetValues<LabelTypeface>())
        {
            foreach (var font in Enum.GetValues<LabelFont>())
            {
                foreach (var c in GlyphFont.SupportedCharacters)
                {
                    Assert.True(GlyphFont.TryGetMeshId(c, font, typeface, out var id));
                    Assert.True(ids.Add(id), $"'{c}' in {typeface} {font} shares a mesh with something else");
                }
            }
        }
    }

    [Fact]
    public void TheGlyphIdsStayClearOfEverythingElse()
    {
        var ids = GlyphFont.CreateAll().Select(mesh => mesh.Id).ToList();
        Assert.All(ids, id => Assert.InRange(id, MeshIds.GlyphBase, MeshIds.Shoreline - 1));
    }

    [Fact]
    public void ASerifIsActuallyAddedToTheStems()
    {
        // Feet are strokes of their own, so a serif letter is made of more than the plain one.
        foreach (var c in new[] { 'I', 'H', 'L', 'T' })
        {
            var sans = Triangles(Glyph(c, LabelFont.Regular, LabelTypeface.Sans));
            var serif = Triangles(Glyph(c, LabelFont.Regular, LabelTypeface.Serif));
            var slab = Triangles(Glyph(c, LabelFont.Regular, LabelTypeface.Slab));

            Assert.True(serif > sans, $"'{c}' in Serif is no different from Sans");
            Assert.Equal(serif, slab);
        }

        // And a letter with no upright ending at the top or bottom gets none, rather than blots in the middle of it.
        Assert.Equal(
            Triangles(Glyph('O', LabelFont.Regular, LabelTypeface.Sans)),
            Triangles(Glyph('O', LabelFont.Regular, LabelTypeface.Serif)));
    }

    [Fact]
    public void ASerifedLetterIsNoTallerThanAPlainOne()
    {
        // The feet sit inside the line the stem ends on, so a line of Serif sits on the same baseline as Sans.
        foreach (var typeface in Enum.GetValues<LabelTypeface>())
        {
            foreach (var c in new[] { 'I', 'H', 'A', '8' })
            {
                var mesh = Glyph(c, LabelFont.Regular, typeface);
                var heights = Heights(mesh).ToArray();
                Assert.All(heights, z => Assert.InRange(z, -0.62f, 0.62f));
            }
        }
    }

    [Fact]
    public void TheWeightAndTheTypefaceAreChosenSeparately()
    {
        // Bold is heavier than Regular whichever typeface it is in, and Slab is heavier than Serif at either weight.
        foreach (var typeface in Enum.GetValues<LabelTypeface>())
        {
            var regular = Area(Glyph('H', LabelFont.Regular, typeface));
            var bold = Area(Glyph('H', LabelFont.Bold, typeface));
            Assert.True(bold > regular, $"Bold is no heavier than Regular in {typeface}");
        }

        foreach (var font in new[] { LabelFont.Regular, LabelFont.Bold })
        {
            Assert.True(
                Area(Glyph('H', font, LabelTypeface.Slab)) > Area(Glyph('H', font, LabelTypeface.Serif)),
                $"Slab is no heavier than Serif at {font}");
        }
    }

    [Fact]
    public void TheDefaultIsSans_AndAnUnknownValueFallsBackToIt()
    {
        var style = new MarinaStyle();
        Assert.Equal(LabelTypeface.Sans, style.Labels.Typeface);

        style.Labels.Typeface = LabelTypeface.Serif;
        Assert.Equal(LabelTypeface.Serif, style.Labels.Typeface);

        style.Labels.Typeface = (LabelTypeface)99;
        Assert.Equal(LabelTypeface.Sans, style.Labels.Typeface);
    }

    [Fact]
    public void ChangingTheTypeface_RedrawsTheLabelsInIt()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        marina.Designer.IsActive = true;
        var berths = marina.Designer.CreateBerths("A", PierSide.Left, 0f, 60f);
        marina.Designer.IsActive = false;
        Assert.NotEmpty(berths);
        marina.BerthLabelMode = BerthLabelMode.All;

        int[] Drawn()
        {
            var frame = marina.BuildRenderFrame();
            return frame.Objects.Select(o => o.MeshId).Where(id => id >= MeshIds.GlyphBase && id < MeshIds.Shoreline).ToArray();
        }

        var sans = Drawn();
        Assert.NotEmpty(sans);

        marina.Style.Labels.Typeface = LabelTypeface.Slab;
        var slab = Drawn();

        // The same number of letters, drawn from different meshes.
        Assert.Equal(sans.Length, slab.Length);
        Assert.NotEqual(sans, slab);
    }

    [Fact]
    public void TheTypefaceSurvivesAFile()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", Vector2.Zero, 0f, 40f));
        marina.Style.Labels.Typeface = LabelTypeface.Serif;
        marina.Style.Labels.FontFamily = LabelFont.Condensed;

        var reloaded = new MarinaVisualizer();
        MarinaDocument.Parse(MarinaDocument.FromVisualizer(marina, generator: "tests").ToJson()).ApplyTo(reloaded);

        Assert.Equal(LabelTypeface.Serif, reloaded.Style.Labels.Typeface);
        Assert.Equal(LabelFont.Condensed, reloaded.Style.Labels.FontFamily);
    }

    /// <summary>How much ink a glyph uses, which is how its weight shows.</summary>
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

    /// <summary>Every vertex height in a glyph, which is how far up and down the letter reaches.</summary>
    private static IEnumerable<float> Heights(MeshData mesh)
    {
        for (var i = 0; i < mesh.Indices.Length; i++) yield return Vertex(mesh, (int)mesh.Indices[i]).Z;
    }

    private static Vector3 Vertex(MeshData mesh, int index)
    {
        var at = index * 9;
        return new Vector3(mesh.Vertices[at], mesh.Vertices[at + 1], mesh.Vertices[at + 2]);
    }
}
