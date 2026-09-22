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
/// The shape of the letters themselves, as against <see cref="LabelFont"/>, which is their weight and width. The
/// two are chosen separately, so any typeface can be had bold or condensed.
/// </summary>
/// <remarks>
/// These are drawn as strokes rather than set in a real font: the labels lie flat on the water and are rendered as
/// meshes, with no textures, so that OpenGL and WebGL draw exactly the same thing and the library carries no font
/// files. That rules out naming real faces here, and it is also why there is no monospaced one — every glyph
/// already sits on the same grid and advances by the same step, so it would be the same letters as
/// <see cref="Sans"/>.
/// </remarks>
public enum LabelTypeface
{
    /// <summary>Plain strokes with open ends. The default, and the one to read at a glance.</summary>
    Sans = 0,

    /// <summary>Fine strokes finished with small feet, in the manner of a book face.</summary>
    Serif = 1,

    /// <summary>Heavier strokes with square feet, which hold up at a distance and on a busy background.</summary>
    Slab = 2,
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

    /// <summary>How many weights there are, so a typeface's ids start past all of them.</summary>
    private const int FaceCount = 4;

    /// <summary>How far a serif reaches either side of the stem it finishes, on the 4 x 6 grid.</summary>
    private const float SerifReach = 0.72f;

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

    /// <summary>
    /// How each typeface differs: how much its strokes are scaled, and how heavy a foot they finish with. A foot of
    /// zero means no feet at all.
    /// </summary>
    private static (float Stroke, float Foot) Typeface(LabelTypeface typeface) => typeface switch
    {
        LabelTypeface.Serif => (0.85f, 0.95f),
        LabelTypeface.Slab => (1.12f, 1.1f),
        _ => (1f, 0f),
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
    public static bool TryGetMeshId(char c, LabelFont font, out int meshId) =>
        TryGetMeshId(c, font, LabelTypeface.Sans, out meshId);

    /// <summary>Mesh id for a character in one weight of one typeface; false for whitespace (nothing to draw).</summary>
    /// <param name="c">The character.</param>
    /// <param name="font">Which weight to draw it in.</param>
    /// <param name="typeface">Which typeface to draw it in.</param>
    /// <param name="meshId">The mesh to place.</param>
    public static bool TryGetMeshId(char c, LabelFont font, LabelTypeface typeface, out int meshId)
    {
        meshId = 0;
        if (char.IsWhiteSpace(c)) return false;
        var upper = char.ToUpper(c, CultureInfo.InvariantCulture);
        var index = Array.BinarySearch(Characters, Strokes.ContainsKey(upper) ? upper : '?');
        meshId = MeshIds.GlyphBase + Variant(font, typeface) * FamilyStride + index;
        return true;
    }

    /// <summary>Which of the weight-and-typeface combinations this is, counted from zero.</summary>
    private static int Variant(LabelFont font, LabelTypeface typeface) => (int)typeface * FaceCount + (int)font;

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
        foreach (var typeface in Enum.GetValues<LabelTypeface>())
        {
            foreach (var font in Enum.GetValues<LabelFont>())
            {
                for (var i = 0; i < Characters.Length; i++)
                {
                    var id = MeshIds.GlyphBase + Variant(font, typeface) * FamilyStride + i;
                    yield return CreateGlyph(Characters[i], id, font, typeface);
                }
            }
        }
    }

    private static MeshData CreateGlyph(char c, int meshId, LabelFont font, LabelTypeface typeface)
    {
        var b = new MeshBuilder();
        var white = Vector3.One;
        var (weight, widthScale) = Face(font);
        var (scale, foot) = Typeface(typeface);
        var stroke = weight * scale;
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

            if (foot > 0f) AddSerifs(b, points, stroke * foot, halfStroke, white, ToModel);
        }

        return b.Build(meshId, $"Glyph '{c}' ({typeface} {font})");

        Vector3 ToModel(Vector2 grid) => GlyphFont.ToModel(grid, widthScale);
    }

    /// <summary>
    /// Finishes the ends of an upright stroke with a small crossbar, which is what makes a serif face one.
    /// </summary>
    /// <remarks>
    /// Only where a stroke ends going up or down, and only at the top or bottom of the grid: a serif on the end of
    /// the arm of an E or in the middle of an S would read as a blot rather than a foot. That keeps this to the
    /// stems, which is where the feet belong and where they are worth the triangles.
    /// </remarks>
    private static void AddSerifs(MeshBuilder b, Vector2[] points, float thickness, float halfStroke, Vector3 color, Func<Vector2, Vector3> toModel)
    {
        if (points.Length < 2) return;

        Foot(points[0], points[0] - points[1]);
        Foot(points[^1], points[^1] - points[^2]);

        void Foot(Vector2 end, Vector2 outward)
        {
            if (outward.LengthSquared() < 1e-6f) return;

            // Upright enough to be a stem, and at the head or the foot of the letter.
            var direction = Vector2.Normalize(outward);
            if (MathF.Abs(direction.Y) < 0.8f) return;
            if (end.Y > 0.01f && end.Y < GridHeight - 0.01f) return;

            // The bar sits just inside the line the stem ends on, so the letter keeps its height.
            var half = thickness * 0.5f;
            var reach = SerifReach + halfStroke;
            var middle = new Vector2(end.X, end.Y > GridHeight * 0.5f ? GridHeight - half : half);

            b.AddQuadUp(
                toModel(new Vector2(middle.X - reach, middle.Y - half)),
                toModel(new Vector2(middle.X + reach, middle.Y - half)),
                toModel(new Vector2(middle.X + reach, middle.Y + half)),
                toModel(new Vector2(middle.X - reach, middle.Y + half)),
                color);
        }
    }

    /// <summary>Grid → model space: 1 unit tall, centered, reading direction along −X.</summary>
    private static Vector3 ToModel(Vector2 grid, float widthScale) =>
        new(-(grid.X - GridWidth * 0.5f) / GridHeight * widthScale, 0f, (grid.Y - GridHeight * 0.5f) / GridHeight);
}
