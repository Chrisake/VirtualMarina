using System.Numerics;
using System.Text.Json;
using Microsoft.JSInterop;
using VirtualMarina.Core.Geometry;

namespace VirtualMarina.Designer.Blazor;

/// <summary>
/// Reads the fonts on this machine and captures their glyph outlines, so a design can carry its own lettering
/// exactly as the desktop designer's <c>FontCapture</c> does — only through a canvas in the browser rather than
/// through GDI+. The heavy work (probing families, tracing outlines) is in <c>wwwroot/designer.js</c>; this turns
/// what it returns into the <see cref="LabelFontDefinition"/> the renderer draws labels from.
/// </summary>
internal static class BrowserFonts
{
    /// <summary>Every font family the browser could find, in alphabetical order.</summary>
    /// <param name="js">The in-process JS runtime (Blazor WebAssembly).</param>
    public static IReadOnlyList<string> InstalledFamilies(IJSInProcessRuntime js)
    {
        ArgumentNullException.ThrowIfNull(js);
        return js.Invoke<string[]>("vmDesigner.fontFamilies") ?? Array.Empty<string>();
    }

    /// <summary>
    /// Captures a family's outlines, normalised so the cap height is 1 and the baseline is y = 0. Returns null
    /// when the family is not present or nothing usable came back.
    /// </summary>
    /// <param name="js">The in-process JS runtime (Blazor WebAssembly).</param>
    /// <param name="familyName">The family, from <see cref="InstalledFamilies"/>.</param>
    /// <param name="bold">Capture the bold face.</param>
    public static LabelFontDefinition? Capture(IJSInProcessRuntime js, string familyName, bool bold)
    {
        ArgumentNullException.ThrowIfNull(js);
        if (string.IsNullOrWhiteSpace(familyName)) return null;

        var captured = js.Invoke<JsonElement>("vmDesigner.captureFont", familyName, bold);
        if (captured.ValueKind != JsonValueKind.Object || !captured.TryGetProperty("glyphs", out var glyphArray)) return null;

        var glyphs = new List<LabelGlyph>(glyphArray.GetArrayLength());
        foreach (var glyph in glyphArray.EnumerateArray())
        {
            var character = glyph.GetProperty("c").GetString();
            if (string.IsNullOrEmpty(character)) continue;

            var contours = new List<IReadOnlyList<Vector2>>();
            if (glyph.TryGetProperty("o", out var outlines))
            {
                foreach (var contour in outlines.EnumerateArray())
                {
                    var flat = contour.EnumerateArray().Select(v => v.GetSingle()).ToArray();
                    if (flat.Length < 6) continue;   // fewer than three points is not an outline

                    var points = new List<Vector2>(flat.Length / 2);
                    for (var i = 0; i + 1 < flat.Length; i += 2) points.Add(new Vector2(flat[i], flat[i + 1]));
                    if (points.Count >= 3) contours.Add(points);
                }
            }

            glyphs.Add(new LabelGlyph(character[0], glyph.GetProperty("a").GetSingle(), contours));
        }

        if (glyphs.Count == 0) return null;
        var name = captured.TryGetProperty("name", out var n) ? n.GetString() ?? familyName : familyName;
        var isBold = captured.TryGetProperty("isBold", out var b) && b.GetBoolean();
        var font = new LabelFontDefinition(name, glyphs, isBold);
        return font.Validate().Any() ? null : font;
    }
}
