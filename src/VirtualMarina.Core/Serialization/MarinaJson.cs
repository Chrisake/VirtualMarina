using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Serialization;

/// <summary>
/// The JSON settings <see cref="MarinaDocument"/> reads and writes with: camelCase names, compact <c>[x, y]</c> vectors, hex colors
/// and forgiving enums. <see cref="CreateOptions"/> gives you the same conventions for marina data of your own.
/// </summary>
/// <remarks>
/// Reading is deliberately tolerant, because a file may come from a newer version of the format: unknown properties are kept aside
/// (see <see cref="MarinaDocument.Extensions"/>), unknown enum names fall back to the default, numbers may be quoted, and comments
/// and trailing commas are allowed. Serialization is source-generated, so it keeps working in trimmed and WebAssembly builds.
/// </remarks>
public static class MarinaJson
{
    internal static MarinaJsonContext Indented { get; } = CreateContext(indented: true);

    internal static MarinaJsonContext Compact { get; } = CreateContext(indented: false);

    /// <summary>The options marina files are written with (indented). Read-only.</summary>
    /// <remarks>
    /// These are bound to the format's own source-generated metadata, so they can only serialize the marina file itself: passing
    /// them to <see cref="JsonSerializer"/> for a type of your own throws <see cref="NotSupportedException"/>. For your own types
    /// call <see cref="CreateOptions"/>, which gives a fresh instance with the same conventions.
    /// </remarks>
    public static JsonSerializerOptions Options => Indented.Options;

    /// <summary>The same options without indentation, as written for compact storage (e.g. a database column). Read-only; see <see cref="Options"/>.</summary>
    public static JsonSerializerOptions CompactOptions => Compact.Options;

    /// <summary>
    /// A new, writable set of options with the marina file's conventions (camelCase names, <c>[x, y]</c> vectors, hex colors,
    /// forgiving enums, comments and trailing commas allowed), for serializing data of your own.
    /// </summary>
    /// <param name="typeInfoResolver">
    /// Where the metadata for your types comes from: typically your own source-generated <see cref="JsonSerializerContext"/>, which
    /// keeps serialization trim- and AOT-safe, or <see cref="DefaultJsonTypeInfoResolver"/> for reflection. Null leaves it unset,
    /// which uses reflection where the application allows it and throws where it does not.
    /// </param>
    /// <param name="indented">Lay the JSON out over several lines (default true).</param>
    /// <example>
    /// <code>
    /// [JsonSerializable(typeof(ErpSettings))]
    /// partial class AcmeJsonContext : JsonSerializerContext { }
    ///
    /// var context = new AcmeJsonContext(MarinaJson.CreateOptions());
    /// document.SetExtension("acmeErp", settings, context.ErpSettings);
    /// </code>
    /// </example>
    public static JsonSerializerOptions CreateOptions(IJsonTypeInfoResolver? typeInfoResolver = null, bool indented = true)
    {
        var options = BuildOptions(indented);
        options.TypeInfoResolver = typeInfoResolver;
        return options;
    }

    /// <summary>The format's own context, on options that are locked so nobody can change how every file is written.</summary>
    private static MarinaJsonContext CreateContext(bool indented)
    {
        var context = new MarinaJsonContext(BuildOptions(indented));
        context.Options.MakeReadOnly();
        return context;
    }

    private static JsonSerializerOptions BuildOptions(bool indented) => new()
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
            new NullableVector3Converter(),
            new ColorRgbaConverter(),
            new NullableColorRgbaConverter(),
            new TolerantBase64Converter(),

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
            // An object has to be stepped over as a whole, or the reader is left inside it and the rest of the file fails.
            if (reader.TokenType == JsonTokenType.StartObject) reader.Skip();
            else values[0] = ReadNumber(ref reader);
            return values;
        }

        var index = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            // Same inside the array: a nested array or object counts as zero, and its end must not be taken for this one's.
            if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject) reader.Skip();
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
    /// <remarks>
    /// The text goes out unchecked, so a value that is not a number (NaN or infinity) is written as 0: JSON has no plain token for
    /// one, and a bare <c>NaN</c> in the array would save a file that could never be opened again.
    /// </remarks>
    internal static void WriteInlineArray(Utf8JsonWriter writer, params float[] values) =>
        writer.WriteRawValue("[" + string.Join(", ", values.Select(Format)) + "]", skipInputValidation: true);

    internal static string FormatPoint(Vector2 point) => "[" + Format(point.X) + ", " + Format(point.Y) + "]";

    private static string Format(float value) =>
        float.IsFinite(value) ? MathF.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture) : "0";
}

/// <summary>Source-generated metadata for the marina file format, so it serializes without reflection.</summary>
[JsonSerializable(typeof(DocumentDto))]
internal sealed partial class MarinaJsonContext : JsonSerializerContext
{
}

/// <summary>Plan-view point as <c>[x, y]</c>; also reads <c>{ "x": .., "y": .. }</c>.</summary>
internal sealed class Vector2Converter : JsonConverter<Vector2>
{
    public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ReadValue(ref reader);

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
    public override List<Vector2> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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
    public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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

/// <summary>
/// A light or water color the file may leave out or get wrong: null when it is missing or can't be read, so the setting keeps its
/// default. Black is a color like any other, which is why "missing" can't be told by the value being zero.
/// </summary>
internal sealed class NullableVector3Converter : JsonConverter<Vector3?>
{
    private static readonly Vector3Converter Inner = new();

    public override bool HandleNull => true;

    public override Vector3? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return ColorRgbaConverter.TryParseHex(reader.GetString(), out var color) ? color.ToVector3() : null;
            case JsonTokenType.StartArray or JsonTokenType.StartObject:
                return Inner.Read(ref reader, typeof(Vector3), options);
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, Vector3? value, JsonSerializerOptions options)
    {
        if (value is { } vector) Inner.Write(writer, vector, options);
        else writer.WriteNullValue();
    }
}

/// <summary>Color as <c>"#RRGGBB"</c> (or <c>"#RRGGBBAA"</c> when it is see-through); also reads <c>[r, g, b, a]</c> in 0..1.</summary>
internal sealed class ColorRgbaConverter : JsonConverter<ColorRgba>
{
    public override ColorRgba Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        TryRead(ref reader, out var color) ? color : default;

    /// <summary>
    /// Reads a hex string or an <c>[r, g, b, a]</c> array. Anything else (an object, a number, a string that isn't a color) is
    /// stepped over and reported as unreadable, so the caller can keep its default instead of turning the color black.
    /// </summary>
    internal static bool TryRead(ref Utf8JsonReader reader, out ColorRgba color)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return TryParseHex(reader.GetString(), out color);
            case JsonTokenType.StartArray:
                var values = MarinaJson.ReadNumbers(ref reader, 4);
                color = new ColorRgba(values[0], values[1], values[2], values[3]);
                return true;
            default:
                reader.Skip();
                color = default;
                return false;
        }
    }

    public override void Write(Utf8JsonWriter writer, ColorRgba value, JsonSerializerOptions options) => WriteValue(writer, value);

    internal static void WriteValue(Utf8JsonWriter writer, ColorRgba value)
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

/// <summary>
/// A color the file may leave out or get wrong: null for anything that can't be read, so the setting keeps its default. The
/// style sections of a marina file use this one.
/// </summary>
internal sealed class NullableColorRgbaConverter : JsonConverter<ColorRgba?>
{
    public override bool HandleNull => true;

    public override ColorRgba? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ColorRgbaConverter.TryRead(ref reader, out var color) ? color : null;

    public override void Write(Utf8JsonWriter writer, ColorRgba? value, JsonSerializerOptions options)
    {
        if (value is { } color) ColorRgbaConverter.WriteValue(writer, color);
        else writer.WriteNullValue();
    }
}

/// <summary>
/// Bytes as base64, decoded straight from the UTF-8 text of the file so a large picture is never held as a string. Anything that
/// isn't base64 reads as null rather than refusing the file: a corrupted picture must not stop the rest of a design loading.
/// </summary>
internal sealed class TolerantBase64Converter : JsonConverter<byte[]>
{
    public override byte[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
            return null;
        }

        if (reader.TryGetBytesFromBase64(out var bytes)) return bytes;

        // The fast path takes no whitespace or line breaks, which a hand-edited or wrapped file may have; this one does.
        var text = reader.GetString() ?? string.Empty;
        var buffer = new byte[(text.Length / 4 * 3) + 3];
        return Convert.TryFromBase64String(text, buffer, out var written) ? buffer.AsSpan(0, written).ToArray() : null;
    }

    public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options) => writer.WriteBase64StringValue(value);
}

/// <summary>Writes an enum as its name and reads it case-insensitively, falling back to the default for names this version doesn't know.</summary>
internal sealed class TolerantEnumConverter<T> : JsonConverter<T>
    where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            // Enum.TryParse also takes a number written as a string ("7"), which must meet the same test as a bare one.
            case JsonTokenType.String when Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var parsed):
                return Known(parsed);
            case JsonTokenType.Number when reader.TryGetInt64(out var number):
                return Known((T)Enum.ToObject(typeof(T), number));
            default:
                // A value from a newer version of the format: fall back to the default instead of refusing the file.
                reader.Skip();
                return default;
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());

    /// <summary>The value when this version has a name for it (any mix of the flags, for a flags enum), otherwise the default.</summary>
    private static T Known(T value) => Enum.IsDefined(value) || typeof(T).IsDefined(typeof(FlagsAttribute), false) ? value : default;
}
