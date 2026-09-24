using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace VirtualMarina;

/// <summary>
/// The static members .NET 8 added to types .NET Framework already has (<c>float.IsFinite</c>,
/// <c>ArgumentNullException.ThrowIfNull</c>, <c>Math.Clamp</c>, ...).
/// </summary>
/// <remarks>
/// C# 12 cannot add a static member to an existing type, so the build points the shared sources' calls here instead: the
/// <c>Net48Rewrite</c> items in the project file list every call it rewrites. Each member behaves as the .NET 8 member it
/// stands in for.
/// </remarks>
internal static class Net48
{
    // ---- float, double, char, string, int, byte ---------------------------------------------------------------------

    public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    public static float Lerp(float value1, float value2, float amount) => value1 + ((value2 - value1) * amount);

    public static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

    public static bool IsAsciiLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    public static string CreateString(IFormatProvider? provider, FormattableString text) => text.ToString(provider);

    public static bool TryParseInt32(string? s, out int result) => int.TryParse(s, out result);

    public static bool TryParseInt32(string? s, NumberStyles style, IFormatProvider? provider, out int result) =>
        int.TryParse(s, style, provider, out result);

    public static bool TryParseInt32(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out int result) =>
        int.TryParse(s.ToString(), style, provider, out result);

    public static byte ParseByte(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider) =>
        byte.Parse(s.ToString(), style, provider);

    /// <summary><c>span.Equals(text, comparison)</c>, which needs .NET Core's conversion of a string to a span.</summary>
    public static bool SpanEquals(ReadOnlySpan<char> span, string text, StringComparison comparison) =>
        span.Equals(text.AsSpan(), comparison);

    // ---- Math, vectors ------------------------------------------------------------------------------------------------

    public static int Clamp(int value, int min, int max) =>
        min > max ? throw MinGreaterThanMax(min, max) : Math.Min(Math.Max(value, min), max);

    public static long Clamp(long value, long min, long max) =>
        min > max ? throw MinGreaterThanMax(min, max) : Math.Min(Math.Max(value, min), max);

    public static float Clamp(float value, float min, float max) =>
        min > max ? throw MinGreaterThanMax(min, max) : Math.Min(Math.Max(value, min), max);

    public static double Clamp(double value, double min, double max) =>
        min > max ? throw MinGreaterThanMax(min, max) : Math.Min(Math.Max(value, min), max);

    public static decimal Clamp(decimal value, decimal min, decimal max) =>
        min > max ? throw MinGreaterThanMax(min, max) : Math.Min(Math.Max(value, min), max);

    /// <summary>A component of a vector by index, as .NET 7's <c>Vector3</c> indexer.</summary>
    public static float Component(Vector3 vector, int index) => index switch
    {
        0 => vector.X,
        1 => vector.Y,
        2 => vector.Z,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    private static ArgumentException MinGreaterThanMax(object min, object max) =>
        new(string.Format(CultureInfo.InvariantCulture, "'{0}' cannot be greater than {1}.", min, max), nameof(min));

    // ---- Argument checks --------------------------------------------------------------------------------------------

    public static void ThrowIfNull([NotNull] object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        if (argument is null) throw new ArgumentNullException(paramName);
    }

    public static void ThrowIfNullOrWhiteSpace([NotNull] string? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        ThrowIfNull(argument, paramName);
        if (string.IsNullOrWhiteSpace(argument))
        {
            throw new ArgumentException("The value cannot be an empty string or composed entirely of whitespace.", paramName);
        }
    }

    public static void ThrowIfNegative<T>(T value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : struct, IComparable<T>
    {
        if (value.CompareTo(default) < 0) throw OutOfRange(value, paramName, "a non-negative value");
    }

    public static void ThrowIfNegativeOrZero<T>(T value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : struct, IComparable<T>
    {
        if (value.CompareTo(default) <= 0) throw OutOfRange(value, paramName, "a non-negative and non-zero value");
    }

    public static void ThrowIfGreaterThanOrEqual<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : IComparable<T>
    {
        if (value.CompareTo(other) >= 0) throw OutOfRange(value, paramName, string.Format(CultureInfo.InvariantCulture, "less than '{0}'", other));
    }

    public static void ThrowIfDisposed([DoesNotReturnIf(true)] bool condition, object instance)
    {
        if (condition) throw new ObjectDisposedException(instance?.GetType().FullName);
    }

    private static ArgumentOutOfRangeException OutOfRange(object value, string? paramName, string expected) =>
        new(paramName, value, string.Format(CultureInfo.InvariantCulture, "'{0}' must be {1}.", paramName, expected));

    // ---- Enum, Array, Convert, File, collections --------------------------------------------------------------------

    public static bool IsDefined<TEnum>(TEnum value)
        where TEnum : struct, Enum => Enum.IsDefined(typeof(TEnum), value);

    public static TEnum[] GetValues<TEnum>()
        where TEnum : struct, Enum => (TEnum[])Enum.GetValues(typeof(TEnum));

    public static void ArrayClear(Array array) => Array.Clear(array, 0, array.Length);

    public static void ArrayClear(Array array, int index, int length) => Array.Clear(array, index, length);

    public static bool TryFromBase64String(string s, Span<byte> bytes, out int bytesWritten)
    {
        bytesWritten = 0;
        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(s);
        }
        catch (FormatException)
        {
            return false;
        }

        if (decoded.Length > bytes.Length) return false;
        decoded.CopyTo(bytes);
        bytesWritten = decoded.Length;
        return true;
    }

    /// <summary><c>File.Move</c> with .NET Core's overwrite flag: an existing target is replaced.</summary>
    public static void MoveFile(string sourceFileName, string destFileName, bool overwrite = false)
    {
        if (overwrite && File.Exists(destFileName))
        {
            // Replace swaps the file in one step, as the overwriting move does on .NET Core.
            File.Replace(sourceFileName, destFileName, destinationBackupFileName: null);
            return;
        }

        File.Move(sourceFileName, destFileName);
    }

    public static ReadOnlyDictionary<TKey, TValue> EmptyReadOnlyDictionary<TKey, TValue>()
        where TKey : notnull => EmptyDictionary<TKey, TValue>.Instance;

    private static class EmptyDictionary<TKey, TValue>
        where TKey : notnull
    {
        public static readonly ReadOnlyDictionary<TKey, TValue> Instance = new(new Dictionary<TKey, TValue>());
    }
}
