using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace VirtualMarina.Core.Domain;

// The collections the domain records hold. A record compares its fields one by one, so a record holding a plain array or
// dictionary compares that part by reference: two lawns with the same outline were not Equal, and every rebuilt Boat looked
// changed. Holding these instead gives the records value equality over their contents, and because each one is a private copy
// that is never written to, `land with { Points = myList }` can no longer alias a list the host goes on changing.

/// <summary>
/// A read-only copy of a sequence that compares by its elements, in order. What the domain records store behind their
/// <see cref="IReadOnlyList{T}"/> properties.
/// </summary>
/// <typeparam name="T">Element type.</typeparam>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "It is a list.")]
internal sealed class ValueList<T> : IReadOnlyList<T>, IEquatable<ValueList<T>>
{
    private readonly T[] _items;

    private ValueList(T[] items)
    {
        _items = items;
    }

    /// <summary>The empty list.</summary>
    public static ValueList<T> Empty { get; } = new(Array.Empty<T>());

    public int Count => _items.Length;

    public T this[int index] => _items[index];

    /// <summary>
    /// A private copy of <paramref name="source"/>; null reads as empty. One of these is already immutable, so it is kept as
    /// it is rather than copied again.
    /// </summary>
    public static ValueList<T> From(IEnumerable<T>? source) => source switch
    {
        null => Empty,
        ValueList<T> list => list,
        _ => source.ToArray() is { Length: > 0 } items ? new ValueList<T>(items) : Empty,
    };

    public bool Equals(ValueList<T>? other) =>
        other is not null && (ReferenceEquals(this, other) || _items.AsSpan().SequenceEqual(other._items, EqualityComparer<T>.Default));

    public override bool Equals(object? obj) => Equals(obj as ValueList<T>);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_items.Length);

        // Enough of the list to tell most lists apart without hashing a 5000-tree lawn in full.
        for (var i = 0; i < _items.Length && i < 16; i++) hash.Add(_items[i]);
        return hash.ToHashCode();
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    public override string ToString() => $"[{_items.Length}]";
}

/// <summary>
/// A read-only copy of a dictionary that compares by its entries, in any order. What the records store behind their
/// <c>Metadata</c> properties.
/// </summary>
/// <remarks>Keys are looked up with the comparer of the dictionary it was copied from, when that was a <see cref="Dictionary{TKey, TValue}"/>.</remarks>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "It is a dictionary.")]
internal sealed class ValueDictionary : IReadOnlyDictionary<string, string>, IEquatable<ValueDictionary>
{
    private readonly Dictionary<string, string> _items;

    private ValueDictionary(Dictionary<string, string> items)
    {
        _items = items;
    }

    /// <summary>The empty dictionary.</summary>
    public static ValueDictionary Empty { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal));

    public int Count => _items.Count;

    public IEnumerable<string> Keys => _items.Keys;

    public IEnumerable<string> Values => _items.Values;

    public string this[string key] => _items[key];

    /// <summary>A private copy of <paramref name="source"/>; null reads as empty.</summary>
    public static ValueDictionary From(IReadOnlyDictionary<string, string>? source)
    {
        switch (source)
        {
            case null:
                return Empty;
            case ValueDictionary value:
                return value;
            case { Count: 0 }:
                return Empty;
        }

        var comparer = source is Dictionary<string, string> dictionary ? dictionary.Comparer : StringComparer.Ordinal;
        var copy = new Dictionary<string, string>(source.Count, comparer);
        foreach (var (key, value) in source) copy[key] = value;
        return new ValueDictionary(copy);
    }

    public bool ContainsKey(string key) => _items.ContainsKey(key);

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value) => _items.TryGetValue(key, out value);

    public bool Equals(ValueDictionary? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (other._items.Count != _items.Count) return false;
        foreach (var (key, value) in _items)
        {
            if (!other._items.TryGetValue(key, out var theirs) || !string.Equals(value, theirs, StringComparison.Ordinal)) return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as ValueDictionary);

    // Order-independent, and only over the values: the keys may be compared ignoring case, which a key hash would not respect.
    public override int GetHashCode()
    {
        var hash = _items.Count;
        foreach (var value in _items.Values) hash ^= StringComparer.Ordinal.GetHashCode(value ?? string.Empty);
        return hash;
    }

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"{{{_items.Count}}}";
}
