using System.Globalization;
using System.Numerics;

namespace VirtualMarina.Core.Geometry;

/// <summary>
/// The faces berth labels can be set in. They are stroke fonts baked into meshes, not typefaces from the system, so
/// the choice is between a few built-in weights and widths rather than a font file.
/// </summary>
public enum LabelFont
{
    /// <summary>The default: even strokes, normal width.</summary>
    Regular = 0,

    /// <summary>Heavier strokes, for labels that must read from further away.</summary>
    Bold = 1,

    /// <summary>Narrower glyphs, so longer names fit across a berth.</summary>
    Condensed = 2,

    /// <summary>Wider glyphs, easier to read on big berths.</summary>
    Wide = 3,
}

/// <summary>
/// Minimal stroke font for text laid flat on the water (berth labels). Each character is a mesh of flat,
/// upward-facing strokes, so it renders on every backend without textures.
/// </summary>
/// <remarks>
/// Glyph model space: the glyph is 1 unit tall along local +Z (the text's "up") and centered on the origin.
/// Reading direction runs along local −X, which is correct when the mesh is placed with
/// <c>MarinaMath.CreatePlacement(scale, headingOfTextUp, position)</c> and seen from above.
/// Lowercase letters render as uppercase; unsupported characters render as '?'.
/// </remarks>
public static class GlyphFont
{
    /// <summary>Glyph width of <see cref="LabelFont.Regular"/> as a fraction of its height.</summary>
    public const float GlyphWidth = 4f / 6f;

    /// <summary>Distance between consecutive <see cref="LabelFont.Regular"/> glyph centers, as a fraction of the height.</summary>
    public const float Advance = 5.2f / 6f;

    /// <summary>Ids of one face's glyphs start this far apart.</summary>
    private const int FamilyStride = 100;

    private const float GridHeight = 6f;
    private const float GridWidth = 4f;
    private const float StrokeWidth = 0.75f;

    /// <summary>How each face differs: how heavy its strokes are and how wide its glyphs sit.</summary>
    private static (float Stroke, float Width) Face(LabelFont font) => font switch
    {
        LabelFont.Bold => (1.15f, 1f),
        LabelFont.Condensed => (0.7f, 0.76f),
        LabelFont.Wide => (0.8f, 1.22f),
        _ => (StrokeWidth, 1f),
    };

    // Polylines on a 4 × 6 grid (x right, y up), separated by '|'.
    private static readonly Dictionary<char, string> Strokes = new()
    {
        ['0'] = "1,0 3,0 4,1 4,5 3,6 1,6 0,5 0,1 1,0|0.8,1 3.2,5",
        ['1'] = "1,5 2,6 2,0|1,0 3,0",
        ['2'] = "0,5 1,6 3,6 4,5 4,4 0,0 4,0",
        ['3'] = "0,6 4,6 2,3.5 3,3.5 4,2.5 4,1 3,0 0,0",
        ['4'] = "3,0 3,6 0,2 4,2",
        ['5'] = "4,6 0,6 0,3.5 3,3.5 4,2.5 4,1 3,0 0,0",
        ['6'] = "3.5,6 1,6 0,5 0,1 1,0 3,0 4,1 4,2.5 3,3.5 0,3.5",
        ['7'] = "0,6 4,6 1.5,0",
        ['8'] = "1,3 0,4 0,5 1,6 3,6 4,5 4,4 3,3 1,3 0,2 0,1 1,0 3,0 4,1 4,2 3,3",
        ['9'] = "4,2.5 1,2.5 0,3.5 0,5 1,6 3,6 4,5 4,1 3,0 0.5,0",
        ['A'] = "0,0 0,4 2,6 4,4 4,0|0,2.5 4,2.5",
        ['B'] = "0,0 0,6 3,6 4,5 4,4 3,3 0,3|3,3 4,2 4,1 3,0 0,0",
        ['C'] = "4,5 3,6 1,6 0,5 0,1 1,0 3,0 4,1",
        ['D'] = "0,0 0,6 2.5,6 4,4.5 4,1.5 2.5,0 0,0",
        ['E'] = "4,6 0,6 0,0 4,0|0,3 3,3",
        ['F'] = "4,6 0,6 0,0|0,3 3,3",
        ['G'] = "4,5 3,6 1,6 0,5 0,1 1,0 3,0 4,1 4,3 2,3",
        ['H'] = "0,6 0,0|4,6 4,0|0,3 4,3",
        ['I'] = "1,6 3,6|2,6 2,0|1,0 3,0",
        ['J'] = "1,6 4,6 4,1 3,0 1,0 0,1",
        ['K'] = "0,6 0,0|4,6 0,2.5|1.5,3.5 4,0",
        ['L'] = "0,6 0,0 4,0",
        ['M'] = "0,0 0,6 2,3 4,6 4,0",
        ['N'] = "0,0 0,6 4,0 4,6",
        ['O'] = "1,0 3,0 4,1 4,5 3,6 1,6 0,5 0,1 1,0",
        ['P'] = "0,0 0,6 3,6 4,5 4,4 3,3 0,3",
        ['Q'] = "1,0 3,0 4,1 4,5 3,6 1,6 0,5 0,1 1,0|2.5,1.5 4,0",
        ['R'] = "0,0 0,6 3,6 4,5 4,4 3,3 0,3|2,3 4,0",
        ['S'] = "4,5 3,6 1,6 0,5 0,4 1,3 3,3 4,2 4,1 3,0 1,0 0,1",
        ['T'] = "0,6 4,6|2,6 2,0",
        ['U'] = "0,6 0,1 1,0 3,0 4,1 4,6",
        ['V'] = "0,6 2,0 4,6",
        ['W'] = "0,6 1,0 2,3.5 3,0 4,6",
        ['X'] = "0,6 4,0|0,0 4,6",
        ['Y'] = "0,6 2,3 4,6|2,3 2,0",
        ['Z'] = "0,6 4,6 0,0 4,0",
        ['-'] = "0.8,3 3.2,3",
        ['_'] = "0,0 4,0",
        ['+'] = "2,1.5 2,4.5|0.5,3 3.5,3",
        ['.'] = "2,0 2,0.3",
        [','] = "2,0.5 1.5,-0.8",
        [':'] = "2,1 2,1.3|2,4.2 2,4.5",
        ['/'] = "0.5,0 3.5,6",
        ['('] = "3,6 1.5,4.5 1.5,1.5 3,0",
        [')'] = "1,6 2.5,4.5 2.5,1.5 1,0",
        ['#'] = "1.3,0.5 1.3,5.5|2.7,0.5 2.7,5.5|0,2 4,2|0,4 4,4",
        ['?'] = "0,5 1,6 3,6 4,5 4,4 2,2.8 2,1.8|2,0 2,0.3",
    };

    private static readonly char[] Characters = Strokes.Keys.OrderBy(c => c).ToArray();

    /// <summary>Characters that have a glyph (besides space).</summary>
    public static IReadOnlyList<char> SupportedCharacters => Characters;

    /// <summary>Mesh id for a character in <see cref="LabelFont.Regular"/>; false for whitespace (nothing to draw).</summary>
    public static bool TryGetMeshId(char c, out int meshId) => TryGetMeshId(c, LabelFont.Regular, out meshId);

    /// <summary>Mesh id for a character in one face; false for whitespace (nothing to draw).</summary>
    /// <param name="c">The character.</param>
    /// <param name="font">Which face to draw it in.</param>
    /// <param name="meshId">The mesh to place.</param>
    public static bool TryGetMeshId(char c, LabelFont font, out int meshId)
    {
        meshId = 0;
        if (char.IsWhiteSpace(c)) return false;
        var upper = char.ToUpper(c, CultureInfo.InvariantCulture);
        var index = Array.BinarySearch(Characters, Strokes.ContainsKey(upper) ? upper : '?');
        meshId = MeshIds.GlyphBase + (int)font * FamilyStride + index;
        return true;
    }

    /// <summary>Distance between consecutive glyph centers in a face, as a fraction of the height.</summary>
    /// <param name="font">The face.</param>
    public static float AdvanceOf(LabelFont font) => Advance * Face(font).Width;

    /// <summary>Glyph width in a face, as a fraction of the height.</summary>
    /// <param name="font">The face.</param>
    public static float GlyphWidthOf(LabelFont font) => GlyphWidth * Face(font).Width;

    /// <summary>Width of a line of <see cref="LabelFont.Regular"/> text in units of the glyph height.</summary>
    public static float MeasureWidth(int characterCount) => MeasureWidth(characterCount, LabelFont.Regular);

    /// <summary>Width of a line of text in units of the glyph height.</summary>
    /// <param name="characterCount">How many characters.</param>
    /// <param name="font">The face it is set in.</param>
    public static float MeasureWidth(int characterCount, LabelFont font) =>
        characterCount <= 0 ? 0f : (characterCount - 1) * AdvanceOf(font) + GlyphWidthOf(font);

    /// <summary>
    /// Every supported character in every face, with ids from <see cref="MeshIds.GlyphBase"/>.
    /// Registered by <see cref="MeshLibrary.CreateDefault"/>.
    /// </summary>
    public static IEnumerable<MeshData> CreateAll()
    {
        foreach (var font in Enum.GetValues<LabelFont>())
        {
            for (var i = 0; i < Characters.Length; i++)
            {
                yield return CreateGlyph(Characters[i], MeshIds.GlyphBase + (int)font * FamilyStride + i, font);
            }
        }
    }

    private static MeshData CreateGlyph(char c, int meshId, LabelFont font)
    {
        var b = new MeshBuilder();
        var white = Vector3.One;
        var (stroke, widthScale) = Face(font);
        var halfStroke = stroke * 0.5f;

        foreach (var polyline in Strokes[c].Split('|'))
        {
            var points = polyline.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split(','))
                .Select(p => new Vector2(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture)))
                .ToArray();

            for (var i = 0; i < points.Length - 1; i++)
            {
                var a = points[i];
                var e = points[i + 1];
                var dir = e - a;
                dir = dir.LengthSquared() < 1e-6f ? Vector2.UnitX : Vector2.Normalize(dir);
                var normal = new Vector2(-dir.Y, dir.X) * halfStroke;
                // Extend each end by half a stroke so joints and dots are filled.
                a -= dir * halfStroke;
                e += dir * halfStroke;
                b.AddQuadUp(ToModel(a - normal), ToModel(e - normal), ToModel(e + normal), ToModel(a + normal), white);
            }
        }

        return b.Build(meshId, $"Glyph '{c}' ({font})");

        Vector3 ToModel(Vector2 grid) => GlyphFont.ToModel(grid, widthScale);
    }

    /// <summary>Grid → model space: 1 unit tall, centered, reading direction along −X.</summary>
    private static Vector3 ToModel(Vector2 grid, float widthScale) =>
        new(-(grid.X - GridWidth * 0.5f) / GridHeight * widthScale, 0f, (grid.Y - GridHeight * 0.5f) / GridHeight);
}
