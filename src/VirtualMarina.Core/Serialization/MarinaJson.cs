using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Serialization;

/// <summary>
/// The JSON settings <see cref="MarinaDocument"/> reads and writes with: camelCase names, compact <c>[x, y]</c> vectors, hex colors
/// and forgiving enums. Reuse them when you store marina data of your own.
/// </summary>
/// <remarks>
/// Reading is deliberately tolerant, because a file may come from a newer version of the format: unknown properties are kept aside
/// (see <see cref="MarinaDocument.Extensions"/>), unknown enum names fall back to the default, numbers may be quoted, and comments
/// and trailing commas are allowed. Serialization is source-generated, so it keeps working in trimmed and WebAssembly builds.
/// </remarks>
public static class MarinaJson
{
    internal static MarinaJsonContext Indented { get; } = new(CreateOptions(indented: true));

    internal static MarinaJsonContext Compact { get; } = new(CreateOptions(indented: false));

    /// <summary>The options marina files are written with (indented).</summary>
    public static JsonSerializerOptions Options => Indented.Options;

    /// <summary>The same options without indentation, for compact storage (e.g. a database column).</summary>
    public static JsonSerializerOptions CompactOptions => Compact.Options;

    private static JsonSerializerOptions CreateOptions(bool indented) => new()
    {
        WriteIndented = indented,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters =
        {
            new Vector2Converter(),
            new Vector2ListConverter(),
            new Vector3Converter(),
            new ColorRgbaConverter(),

            // Listed one by one rather than through a converter factory, so nothing is built by reflection and the format keeps
            // working in trimmed and ahead-of-time compiled builds. Add a line here when the format gains an enum.
            new TolerantEnumConverter<LandKind>(),
            new TolerantEnumConverter<HinterlandScenery>(),
            new TolerantEnumConverter<TreeShape>(),
            new TolerantEnumConverter<PierType>(),
            new TolerantEnumConverter<PierSides>(),
            new TolerantEnumConverter<PierServices>(),
            new TolerantEnumConverter<DividerType>(),
            new TolerantEnumConverter<BerthStatus>(),
            new TolerantEnumConverter<BoatType>(),
            new TolerantEnumConverter<MooringStyle>(),
            new TolerantEnumConverter<BerthSeparator>(),
            new TolerantEnumConverter<BerthLabelMode>(),
            new TolerantEnumConverter<LabelFont>(),
            new TolerantEnumConverter<LabelTypeface>(),
        },
    };

    internal static float ReadNumber(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.Number => reader.GetSingle(),
        JsonTokenType.String when float.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => 0f,
    };

    internal static float[] ReadNumbers(ref Utf8JsonReader reader, int count)
    {
        var values = new float[count];
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            values[0] = ReadNumber(ref reader);
            return values;
        }

        var index = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var value = ReadNumber(ref reader);
            if (index < count) values[index] = value;
            index++;
        }

        // A shorter array leaves the rest at zero; a color without alpha is opaque.
        if (count == 4 && index < 4) values[3] = 1f;
        return values;
    }

    /// <summary>
    /// Writes a point as <c>[x, y]</c> on one line, so an outline of 40 corners stays readable in an indented file. Coordinates are
    /// rounded to a tenth of a millimeter, which is plenty for a marina and keeps the noise digits out of the file.
    /// </summary>
    internal static void WriteInlineArray(Utf8JsonWriter writer, params float[] values) =>
        writer.WriteRawValue("[" + string.Join(", ", values.Select(Format)) + "]", skipInputValidation: true);

    internal static string FormatPoint(Vector2 point) => "[" + Format(point.X) + ", " + Format(point.Y) + "]";

    private static string Format(float value) => MathF.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);
}

/// <summary>Source-generated metadata for the marina file format, so it serializes without reflection.</summary>
[JsonSerializable(typeof(DocumentDto))]
[JsonSerializable(typeof(List<PierDto>))]
[JsonSerializable(typeof(List<BerthDto>))]
internal sealed partial class MarinaJsonContext : JsonSerializerContext
{
}

/// <summary>Plan-view point as <c>[x, y]</c>; also reads <c>{ "x": .., "y": .. }</c>.</summary>
internal sealed class Vector2Converter : JsonConverter<Vector2>
{
    public override Vector2 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => ReadValue(ref reader);

    public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options) =>
        MarinaJson.WriteInlineArray(writer, value.X, value.Y);

    internal static Vector2 ReadValue(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            float x = 0f, y = 0f;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) continue;
                var name = reader.GetString();
                reader.Read();
                if (string.Equals(name, "x", StringComparison.OrdinalIgnoreCase)) x = MarinaJson.ReadNumber(ref reader);
                else if (string.Equals(name, "y", StringComparison.OrdinalIgnoreCase)) y = MarinaJson.ReadNumber(ref reader);
                else reader.Skip();
            }

            return new Vector2(x, y);
        }

        var values = MarinaJson.ReadNumbers(ref reader, 2);
        return new Vector2(values[0], values[1]);
    }
}

/// <summary>An outline as one line of points, <c>[[x, y], [x, y], ...]</c>, so a 40-corner quay stays readable.</summary>
internal sealed class Vector2ListConverter : JsonConverter<List<Vector2>>
{
    public override List<Vector2> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var points = new List<Vector2>();
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return points;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) points.Add(Vector2Converter.ReadValue(ref reader));
        return points;
    }

    public override void Write(Utf8JsonWriter writer, List<Vector2> value, JsonSerializerOptions options) =>
        writer.WriteRawValue("[" + string.Join(", ", value.Select(MarinaJson.FormatPoint)) + "]", skipInputValidation: true);
}

/// <summary>World point or light color as <c>[x, y, z]</c>; also reads <c>{ "x": .., "y": .., "z": .. }</c> and a hex color.</summary>
internal sealed class Vector3Converter : JsonConverter<Vector3>
{
    public override Vector3 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return ColorRgbaConverter.TryParseHex(reader.GetString(), out var color) ? color.ToVector3() : Vector3.Zero;
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            float x = 0f, y = 0f, z = 0f;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) continue;
                var name = reader.GetString();
                reader.Read();
                if (string.Equals(name, "x", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "r", StringComparison.OrdinalIgnoreCase)) x = MarinaJson.ReadNumber(ref reader);
                else if (string.Equals(name, "y", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "g", StringComparison.OrdinalIgnoreCase)) y = MarinaJson.ReadNumber(ref reader);
                else if (string.Equals(name, "z", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "b", StringComparison.OrdinalIgnoreCase)) z = MarinaJson.ReadNumber(ref reader);
                else reader.Skip();
            }

            return new Vector3(x, y, z);
        }

        var values = MarinaJson.ReadNumbers(ref reader, 3);
        return new Vector3(values[0], values[1], values[2]);
    }

    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options) =>
        MarinaJson.WriteInlineArray(writer, value.X, value.Y, value.Z);
}

/// <summary>Color as <c>"#RRGGBB"</c> (or <c>"#RRGGBBAA"</c> when it is see-through); also reads <c>[r, g, b, a]</c> in 0..1.</summary>
internal sealed class ColorRgbaConverter : JsonConverter<ColorRgba>
{
    public override ColorRgba Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return TryParseHex(reader.GetString(), out var color) ? color : default;
        }

        var values = MarinaJson.ReadNumbers(ref reader, 4);
        return new ColorRgba(values[0], values[1], values[2], values[3]);
    }

    public override void Write(Utf8JsonWriter writer, ColorRgba value, JsonSerializerOptions options)
    {
        var hex = value.ToHex();
        if (value.A < 0.999f) hex += ((byte)Math.Clamp((int)MathF.Round(value.A * 255f), 0, 255)).ToString("X2", CultureInfo.InvariantCulture);
        writer.WriteStringValue(hex);
    }

    internal static bool TryParseHex(string? text, out ColorRgba color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            color = ColorRgba.FromHex(text);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>Writes an enum as its name and reads it case-insensitively, falling back to the default for names this version doesn't know.</summary>
internal sealed class TolerantEnumConverter<T> : JsonConverter<T>
    where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String when Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var parsed):
                return parsed;
            case JsonTokenType.Number when reader.TryGetInt64(out var number):
                var value = (T)Enum.ToObject(typeof(T), number);
                return Enum.IsDefined(value) || typeof(T).IsDefined(typeof(FlagsAttribute), false) ? value : default;
            default:
                // A value from a newer version of the format: fall back to the default instead of refusing the file.
                reader.Skip();
                return default;
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
