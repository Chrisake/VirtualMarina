using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Numerics;
using VirtualMarina.Core.Geometry;

namespace VirtualMarina.Designer;

/// <summary>
/// Reads the outlines of a font installed on this machine, so the design can carry its own lettering.
/// </summary>
/// <remarks>
/// <para>
/// Berth labels are meshes lying flat on the water, not text drawn by the system, so a font has to arrive as
/// shapes. Capturing it here — on the machine drawing the marina, which is the one that has the font — means the
/// design can be opened anywhere and still read the same, including in the web view.
/// </para>
/// <para>
/// This is the only part of the feature that is Windows-only. Everything downstream works from the captured
/// outlines and knows nothing about installed fonts.
/// </para>
/// </remarks>
internal static class FontCapture
{
    /// <summary>The characters captured: everything printable in ASCII, which covers any berth name worth having.</summary>
    private const string Characters =
        "!\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

    /// <summary>Size the outlines are taken at. Big enough that flattening curves loses nothing that shows.</summary>
    private const float CaptureSize = 512f;

    /// <summary>
    /// How finely curves are broken into straight lines, in units of <see cref="CaptureSize"/>. Around a fifth of a
    /// percent of the cap height, which is far below anything a label shows and keeps the design file small.
    /// </summary>
    private const float Flatness = 1.8f;

    /// <summary>Every font family installed on this machine that can actually be drawn, in alphabetical order.</summary>
    internal static IReadOnlyList<string> InstalledFamilies()
    {
        var names = new List<string>();
        foreach (var family in FontFamily.Families)
        {
            if (!family.IsStyleAvailable(FontStyle.Regular) && !family.IsStyleAvailable(FontStyle.Bold)) continue;
            names.Add(family.Name);
        }

        names.Sort(StringComparer.CurrentCultureIgnoreCase);
        return names;
    }

    /// <summary>
    /// Captures a font's outlines, normalised so the cap height is 1 and the baseline is y = 0. Returns null when
    /// the family is not installed or has nothing usable in it.
    /// </summary>
    /// <param name="familyName">The family, from <see cref="InstalledFamilies"/>.</param>
    /// <param name="bold">Capture the bold face.</param>
    internal static LabelFontDefinition? Capture(string familyName, bool bold)
    {
        if (string.IsNullOrWhiteSpace(familyName)) return null;

        FontFamily family;
        try
        {
            family = new FontFamily(familyName);
        }
        catch (ArgumentException)
        {
            return null;   // not installed, or not a real family
        }

        using (family)
        {
            var style = bold && family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold
                : family.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular
                : FontStyle.Bold;

            if (!family.IsStyleAvailable(style)) return null;

            // The cap height is what a label's height means, so everything is measured against this font's own.
            var capHeight = CapHeight(family, style);
            if (capHeight <= 1f) return null;

            var baseline = family.GetCellAscent(style) / (float)family.GetEmHeight(style) * CaptureSize;

            using var bitmap = new Bitmap(1, 1);
            using var graphics = Graphics.FromImage(bitmap);
            using var font = new Font(family, CaptureSize, style, GraphicsUnit.Pixel);

            var glyphs = new List<LabelGlyph>(Characters.Length + 1);
            glyphs.Add(new LabelGlyph(' ', SpaceAdvance(graphics, font) / capHeight, Array.Empty<IReadOnlyList<Vector2>>()));

            foreach (var character in Characters)
            {
                var advance = graphics.MeasureString(
                    character.ToString(), font, PointF.Empty, StringFormat.GenericTypographic).Width;

                var contours = Outlines(character, family, style, baseline, capHeight);
                glyphs.Add(new LabelGlyph(character, advance / capHeight, contours));
            }

            var captured = new LabelFontDefinition(bold ? $"{family.Name} Bold" : family.Name, glyphs, bold);
            return captured.Validate().Any() ? null : captured;
        }
    }

    /// <summary>How tall a capital letter stands above the baseline, in capture pixels.</summary>
    private static float CapHeight(FontFamily family, FontStyle style)
    {
        using var path = new GraphicsPath();
        path.AddString("H", family, (int)style, CaptureSize, PointF.Empty, StringFormat.GenericTypographic);
        if (path.PointCount == 0) return 0f;

        var bounds = path.GetBounds();
        return bounds.Height;
    }

    /// <summary>A space has no outline, so its width is measured from a pair of characters around it.</summary>
    private static float SpaceAdvance(Graphics graphics, Font font)
    {
        var format = StringFormat.GenericTypographic;
        var withSpace = graphics.MeasureString("I I", font, PointF.Empty, format).Width;
        var without = graphics.MeasureString("II", font, PointF.Empty, format).Width;
        return MathF.Max(withSpace - without, 0f);
    }

    /// <summary>
    /// One character's closed outlines, flipped upright and scaled so the cap height is 1 and the pen starts at 0.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<Vector2>> Outlines(
        char character,
        FontFamily family,
        FontStyle style,
        float baseline,
        float capHeight)
    {
        using var path = new GraphicsPath();
        path.AddString(character.ToString(), family, (int)style, CaptureSize, PointF.Empty, StringFormat.GenericTypographic);
        if (path.PointCount == 0) return Array.Empty<IReadOnlyList<Vector2>>();

        // Curves become straight lines here; nothing downstream has to know about beziers.
        path.Flatten(null, Flatness);

        var points = path.PathPoints;
        var types = path.PathTypes;
        var contours = new List<IReadOnlyList<Vector2>>();
        var current = new List<Vector2>();

        for (var i = 0; i < points.Length; i++)
        {
            // Drawing space runs down the page from the top of the em box; glyph space runs up from the baseline.
            current.Add(new Vector2(points[i].X / capHeight, (baseline - points[i].Y) / capHeight));

            const byte closeSubpath = 0x80;
            if ((types[i] & closeSubpath) == 0) continue;

            if (current.Count >= 3) contours.Add(current);
            current = new List<Vector2>();
        }

        if (current.Count >= 3) contours.Add(current);
        return contours;
    }
}
