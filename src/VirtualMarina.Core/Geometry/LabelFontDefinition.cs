using System.Globalization;
using System.Numerics;
using System.Text;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Geometry;

/// <summary>
/// One character of a captured font: how far the pen moves for it, and the outlines that draw it.
/// </summary>
/// <param name="Character">The character.</param>
/// <param name="Advance">
/// How far along the line the next character starts, with the cap height as 1. A space has this and no outlines.
/// </param>
/// <param name="Contours">
/// Closed outlines in a space where the baseline is y = 0, the cap height is y = 1 and the pen starts at x = 0.
/// Counters — the hole in an O, the two in an 8 — are outlines of their own; which are holes is worked out from
/// which lie inside which, so the winding does not have to mean anything.
/// </param>
public sealed record LabelGlyph(char Character, float Advance, IReadOnlyList<IReadOnlyList<Vector2>> Contours);

/// <summary>
/// The outlines of a real font, captured so a design carries its own lettering.
/// </summary>
/// <remarks>
/// <para>
/// Berth labels are meshes lying flat on the water rather than text drawn by the system, so a font has to arrive as
/// shapes. Capturing the outlines once, into the design, means the marina looks the same on a machine that has never
/// heard of the font — which matters, because the machine that draws a marina and the machine that shows it to
/// customers are rarely the same one. It also means the web view gets the same lettering as the desktop one.
/// </para>
/// <para>
/// Only the characters that were captured can be drawn. <see cref="TryGetGlyph"/> says whether one was, and a
/// caller that needs a character the font does not carry should fall back to <see cref="GlyphFont"/>.
/// </para>
/// </remarks>
/// <seealso cref="Rendering.LabelStyle.Font"/>
public sealed class LabelFontDefinition
{
    /// <summary>The most characters a captured font may carry.</summary>
    public const int GlyphLimit = 512;

    private readonly Dictionary<char, LabelGlyph> _glyphs = new();
    private readonly Dictionary<char, int> _meshIds = new();

    /// <summary>Creates a font from its glyphs.</summary>
    /// <param name="name">What the font is called, for showing in a UI and for recognising it again.</param>
    /// <param name="glyphs">The characters it carries.</param>
    /// <param name="isBold">True when the captured face was the bold one.</param>
    /// <exception cref="ArgumentException">More than <see cref="GlyphLimit"/> glyphs were given.</exception>
    public LabelFontDefinition(string name, IEnumerable<LabelGlyph> glyphs, bool isBold = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(glyphs);

        Name = name.Trim();
        IsBold = isBold;

        var slot = 0;
        foreach (var glyph in glyphs)
        {
            if (glyph is null || !_glyphs.TryAdd(glyph.Character, glyph)) continue;
            if (slot >= GlyphLimit) throw new ArgumentException($"A captured font may carry at most {GlyphLimit} characters.", nameof(glyphs));
            _meshIds[glyph.Character] = MeshIds.FontGlyphBase + slot++;
        }

        Glyphs = _glyphs.Values.OrderBy(glyph => glyph.Character).ToArray();
    }

    /// <summary>What the font is called, as it was on the machine it was captured from.</summary>
    public string Name { get; }

    /// <summary>True when the captured face was the bold one.</summary>
    public bool IsBold { get; }

    /// <summary>The characters it carries, in order.</summary>
    public IReadOnlyList<LabelGlyph> Glyphs { get; }

    /// <summary>The glyph for a character, when the font carries one.</summary>
    /// <param name="character">The character.</param>
    /// <param name="glyph">The glyph.</param>
    public bool TryGetGlyph(char character, out LabelGlyph glyph) => _glyphs.TryGetValue(character, out glyph!);

    /// <summary>The mesh drawing a character, when the font carries one.</summary>
    /// <param name="character">The character.</param>
    /// <param name="meshId">Id of the mesh, from <see cref="CreateMeshes"/>.</param>
    public bool TryGetMeshId(char character, out int meshId) => _meshIds.TryGetValue(character, out meshId);

    /// <summary>
    /// How far the pen moves for a character, with the cap height as 1. Falls back to a sensible width for a
    /// character the font does not carry, so a name with an odd character in it still lays out.
    /// </summary>
    /// <param name="character">The character.</param>
    public float AdvanceOf(char character) =>
        _glyphs.TryGetValue(character, out var glyph) ? glyph.Advance : GlyphFont.Advance;

    /// <summary>How wide a line of text is, in multiples of the cap height.</summary>
    /// <param name="text">The text.</param>
    public float MeasureWidth(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var width = 0f;
        foreach (var character in text) width += AdvanceOf(character);
        return width;
    }

    /// <summary>
    /// The meshes for every glyph, in the same model space as <see cref="GlyphFont"/>: one unit tall, centred on
    /// the origin, reading along −X. Register them with a <see cref="MeshLibrary"/> to draw labels in this font.
    /// </summary>
    public IEnumerable<MeshData> CreateMeshes()
    {
        foreach (var glyph in Glyphs)
        {
            if (!_meshIds.TryGetValue(glyph.Character, out var id)) continue;
            yield return CreateMesh(glyph, id);
        }
    }

    /// <summary>Problems that would stop the font being drawn, empty when it is sound.</summary>
    public IEnumerable<string> Validate()
    {
        if (Glyphs.Count == 0) yield return "A captured font must carry at least one character.";

        foreach (var glyph in Glyphs.Where(glyph => !float.IsFinite(glyph.Advance) || glyph.Advance < 0f))
        {
            yield return $"The advance of '{glyph.Character}' is not a width.";
        }
    }

    /// <summary>One mesh for one glyph: its outlines triangulated, with the counters left open.</summary>
    private static MeshData CreateMesh(LabelGlyph glyph, int meshId)
    {
        var builder = new MeshBuilder();
        var white = Vector3.One;
        var rings = glyph.Contours.Where(contour => contour is { Count: >= 3 }).ToArray();

        // Which outlines are the shape and which are counters, from how deeply each is nested in the others. A
        // dot over an i is a second outline rather than a hole, so counting containment is the only way to tell.
        foreach (var (outer, holes) in Nest(rings))
        {
            var (points, triangles) = PolygonMath.TriangulateWithHoles(outer, holes);
            foreach (var (a, b, c) in triangles)
            {
                builder.AddTriangleUp(ToModel(points[a], glyph), ToModel(points[b], glyph), ToModel(points[c], glyph), white);
            }
        }

        return builder.Build(meshId, $"Glyph '{glyph.Character}'");
    }

    /// <summary>Groups outlines into shapes and the counters inside them, by how many outlines each one sits in.</summary>
    private static IEnumerable<(IReadOnlyList<Vector2> Outer, IReadOnlyList<IReadOnlyList<Vector2>> Holes)> Nest(
        IReadOnlyList<Vector2>[] rings)
    {
        var depth = new int[rings.Length];
        var parent = new int[rings.Length];
        for (var i = 0; i < rings.Length; i++)
        {
            parent[i] = -1;
            var smallest = float.MaxValue;
            for (var j = 0; j < rings.Length; j++)
            {
                if (i == j || !PolygonMath.Contains(rings[j], rings[i][0])) continue;

                depth[i]++;
                var area = MathF.Abs(PolygonMath.SignedArea(rings[j]));
                if (area >= smallest) continue;

                smallest = area;
                parent[i] = j;
            }
        }

        for (var i = 0; i < rings.Length; i++)
        {
            if (depth[i] % 2 != 0) continue;   // odd means it sits inside something: a counter, not a shape

            var holes = new List<IReadOnlyList<Vector2>>();
            for (var j = 0; j < rings.Length; j++)
            {
                if (depth[j] % 2 == 1 && parent[j] == i) holes.Add(rings[j]);
            }

            yield return (rings[i], holes);
        }
    }

    /// <summary>
    /// Glyph space to model space: centred on its own advance and on the cap height, reading along −X, so it drops
    /// straight into the placement the stroke font already uses.
    /// </summary>
    private static Vector3 ToModel(Vector2 point, LabelGlyph glyph) =>
        new(-(point.X - (glyph.Advance * 0.5f)), 0f, point.Y - 0.5f);

    // ---- Storing it in a design ------------------------------------------------------------------

    /// <summary>
    /// The glyphs as lines of text, for writing into a design file. One line per character: the character, its
    /// advance, then its outlines as <c>x,y</c> points, spaces between points and <c>|</c> between outlines.
    /// </summary>
    public IReadOnlyList<string> Encode()
    {
        var lines = new List<string>(Glyphs.Count);
        foreach (var glyph in Glyphs)
        {
            var text = new StringBuilder();
            text.Append(glyph.Character).Append(' ').Append(Round(glyph.Advance));

            foreach (var contour in glyph.Contours.Where(contour => contour is { Count: >= 3 }))
            {
                text.Append(' ');
                for (var i = 0; i < contour.Count; i++)
                {
                    if (i > 0) text.Append(' ');
                    text.Append(Round(contour[i].X)).Append(',').Append(Round(contour[i].Y));
                }

                text.Append('|');
            }

            lines.Add(text.ToString().TrimEnd('|'));
        }

        return lines;
    }

    /// <summary>Reads back what <see cref="Encode"/> wrote. Returns null when there is nothing usable in it.</summary>
    /// <param name="name">What the font is called.</param>
    /// <param name="lines">The encoded glyphs.</param>
    /// <param name="isBold">True when the captured face was the bold one.</param>
    public static LabelFontDefinition? Decode(string? name, IEnumerable<string>? lines, bool isBold = false)
    {
        if (string.IsNullOrWhiteSpace(name) || lines is null) return null;

        var glyphs = new List<LabelGlyph>();
        foreach (var line in lines)
        {
            if (ParseGlyph(line) is { } glyph) glyphs.Add(glyph);
        }

        if (glyphs.Count == 0 || glyphs.Count > GlyphLimit) return null;

        var font = new LabelFontDefinition(name, glyphs, isBold);
        return font.Validate().Any() ? null : font;
    }

    private static LabelGlyph? ParseGlyph(string? line)
    {
        if (string.IsNullOrEmpty(line) || line.Length < 3 || line[1] != ' ') return null;

        var character = line[0];
        var rest = line[2..];
        var space = rest.IndexOf(' ', StringComparison.Ordinal);
        var advanceText = space < 0 ? rest : rest[..space];
        if (!float.TryParse(advanceText, NumberStyles.Float, CultureInfo.InvariantCulture, out var advance)) return null;

        var contours = new List<IReadOnlyList<Vector2>>();
        if (space >= 0)
        {
            foreach (var part in rest[(space + 1)..].Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var points = new List<Vector2>();
                foreach (var pair in part.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var comma = pair.IndexOf(',', StringComparison.Ordinal);
                    if (comma <= 0) continue;
                    if (!float.TryParse(pair[..comma], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) continue;
                    if (!float.TryParse(pair[(comma + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) continue;
                    points.Add(new Vector2(x, y));
                }

                if (points.Count >= 3) contours.Add(points);
            }
        }

        return new LabelGlyph(character, advance, contours);
    }

    /// <summary>Three decimals of the cap height is finer than a label is ever drawn, and keeps the file small.</summary>
    private static string Round(float value) => MathF.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
}
