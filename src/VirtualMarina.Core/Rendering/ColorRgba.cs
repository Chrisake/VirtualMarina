using System.Globalization;
using System.Numerics;

namespace VirtualMarina.Core.Rendering;

/// <summary>
/// RGBA color with components in 0..1, used by the shaders as they are, with no gamma conversion either way: the
/// components are the familiar sRGB values, so <see cref="FromHex"/> and <see cref="ToHex"/> round-trip byte for byte
/// (<c>#8A4FFF</c> in, <c>#8A4FFF</c> out).
/// </summary>
/// <param name="R">Red.</param>
/// <param name="G">Green.</param>
/// <param name="B">Blue.</param>
/// <param name="A">Alpha (1 = opaque).</param>
/// <example><code>marina.SetStatusColor(BerthStatus.Reserved, ColorRgba.FromHex("#8A4FFF"));</code></example>
public readonly record struct ColorRgba(float R, float G, float B, float A = 1f)
{
    /// <summary>Creates a color from 0–255 components.</summary>
    public static ColorRgba FromBytes(byte r, byte g, byte b, byte a = 255) => new(r / 255f, g / 255f, b / 255f, a / 255f);

    /// <summary>Parses <c>#RGB</c>, <c>#RRGGBB</c> or <c>#RRGGBBAA</c>.</summary>
    public static ColorRgba FromHex(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);
        var s = hex.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length is not (6 or 8)) throw new FormatException($"'{hex}' is not a valid hex color.");

        byte Part(int index) => byte.Parse(s.AsSpan(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return FromBytes(Part(0), Part(2), Part(4), s.Length == 8 ? Part(6) : (byte)255);
    }

    /// <summary>Formats as <c>#RRGGBB</c> (alpha is dropped).</summary>
    public string ToHex() =>
        $"#{ToByte(R):X2}{ToByte(G):X2}{ToByte(B):X2}";

    /// <summary>RGB as a vector.</summary>
    public Vector3 ToVector3() => new(R, G, B);

    /// <summary>RGBA as a vector.</summary>
    public Vector4 ToVector4() => new(R, G, B, A);

    /// <summary>A copy with a different alpha.</summary>
    public ColorRgba WithAlpha(float alpha) => this with { A = alpha };

    /// <summary>Linear blend toward <paramref name="other"/> (t = 0 this color, t = 1 the other).</summary>
    public ColorRgba Lerp(ColorRgba other, float t) =>
        new(R + (other.R - R) * t, G + (other.G - G) * t, B + (other.B - B) * t, A + (other.A - A) * t);

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
}
