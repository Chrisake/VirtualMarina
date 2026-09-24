using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace VirtualMarina;

/// <summary>
/// The instance members .NET Core added to types .NET Framework already has, as extension methods. Declared in
/// <c>VirtualMarina</c>, the namespace around all the shared code, so that code finds them without a using; where .NET
/// Framework has a member of the same name the call fits, that member is used and these are not.
/// </summary>
internal static class Net48Extensions
{
    // ---- Collections ------------------------------------------------------------------------------------------------

    public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
    {
        key = pair.Key;
        value = pair.Value;
    }

    public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        where TKey : notnull
    {
        if (dictionary.ContainsKey(key)) return false;
        dictionary.Add(key, value);
        return true;
    }

    public static TValue GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue)
        where TKey : notnull => dictionary.TryGetValue(key, out var value) ? value : defaultValue;

    public static bool Remove<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, [MaybeNullWhen(false)] out TValue value)
        where TKey : notnull => dictionary.TryGetValue(key, out value) && dictionary.Remove(key);

    public static IEnumerable<(TFirst First, TSecond Second)> Zip<TFirst, TSecond>(this IEnumerable<TFirst> first, IEnumerable<TSecond> second) =>
        first.Zip(second, (a, b) => (a, b));

    public static IEnumerable<TSource> DistinctBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey>? comparer = null)
    {
        var seen = new HashSet<TKey>(comparer);
        foreach (var item in source)
        {
            if (seen.Add(keySelector(item))) yield return item;
        }
    }

    public static TSource? MinBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector) =>
        By(source, keySelector, sign: -1);

    public static TSource? MaxBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector) =>
        By(source, keySelector, sign: 1);

    public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate, TSource defaultValue)
    {
        foreach (var item in source)
        {
            if (predicate(item)) return item;
        }

        return defaultValue;
    }

    public static bool SequenceEqual<T>(this Span<T> span, ReadOnlySpan<T> other, IEqualityComparer<T>? comparer)
    {
        if (span.Length != other.Length) return false;
        comparer ??= EqualityComparer<T>.Default;
        for (var i = 0; i < span.Length; i++)
        {
            if (!comparer.Equals(span[i], other[i])) return false;
        }

        return true;
    }

    /// <summary>The first element with the smallest (<paramref name="sign"/> -1) or largest (1) key; the first one wins a tie.</summary>
    private static TSource? By<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector, int sign)
    {
        var comparer = Comparer<TKey>.Default;
        using var e = source.GetEnumerator();
        if (!e.MoveNext()) return default(TSource) is null ? default : throw new InvalidOperationException("Sequence contains no elements");

        var best = e.Current;
        var bestKey = keySelector(best);
        while (e.MoveNext())
        {
            var key = keySelector(e.Current);
            if (Math.Sign(comparer.Compare(key, bestKey)) == sign)
            {
                best = e.Current;
                bestKey = key;
            }
        }

        return best;
    }

    // ---- Strings and streams ----------------------------------------------------------------------------------------

    public static string[] Split(this string text, char separator, StringSplitOptions options = StringSplitOptions.None) =>
        text.Split([separator], options);

    public static int IndexOf(this string text, char value, StringComparison comparison) =>
        text.IndexOf(value.ToString(), comparison);

    public static string Replace(this string text, string oldValue, string? newValue, StringComparison comparison)
    {
        var result = new StringBuilder(text.Length);
        var start = 0;
        for (var at = text.IndexOf(oldValue, comparison); at >= 0; at = text.IndexOf(oldValue, start, comparison))
        {
            result.Append(text, start, at - start).Append(newValue);
            start = at + oldValue.Length;
        }

        return result.Append(text, start, text.Length - start).ToString();
    }

    public static StringBuilder Append(this StringBuilder builder, ReadOnlySpan<char> value) => builder.Append(value.ToString());

    public static Task CopyToAsync(this Stream source, Stream destination, CancellationToken cancellationToken) =>
        source.CopyToAsync(destination, 81920, cancellationToken);
}
