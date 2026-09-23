using System.Diagnostics.CodeAnalysis;

namespace VirtualMarina.Core.Api;

/// <summary>
/// Elements by id, kept in the order they were added, with lookups, additions and removals that do not walk the
/// whole collection, and optional secondary indexes (a berth's pier, its land area) kept in step with every change.
/// </summary>
/// <remarks>
/// Each element carries the sequence number it was added with. The order is a sorted map on that number, so
/// removing one element is a logarithmic step rather than a scan of a list, and an element replaced or given a new
/// id keeps its place. The secondary indexes are sorted on the same number, so the berths of one pier come out in
/// the same order as they do among all berths.
/// </remarks>
/// <typeparam name="T">The element type.</typeparam>
internal sealed class OrderedStore<T>
    where T : class
{
    private readonly Dictionary<string, Entry> _byId;
    private readonly SortedDictionary<long, Entry> _order = [];
    private readonly Func<T, string?>[] _indexKeys;
    private readonly Dictionary<string, SortedDictionary<long, Entry>>[] _indexes;
    private long _next;

    /// <summary>Creates an empty store.</summary>
    /// <param name="comparer">How ids (and the keys of the secondary indexes) compare.</param>
    /// <param name="indexKeys">One function per secondary index, giving an element's key in it (null leaves it out).</param>
    public OrderedStore(StringComparer comparer, params Func<T, string?>[] indexKeys)
    {
        _byId = new Dictionary<string, Entry>(comparer);
        _indexKeys = indexKeys;
        _indexes = new Dictionary<string, SortedDictionary<long, Entry>>[indexKeys.Length];
        for (var i = 0; i < indexKeys.Length; i++) _indexes[i] = new Dictionary<string, SortedDictionary<long, Entry>>(comparer);
    }

    /// <summary>How many elements there are.</summary>
    public int Count => _byId.Count;

    /// <summary>Every element, in the order they were added.</summary>
    public IEnumerable<T> Values
    {
        get
        {
            foreach (var entry in _order.Values) yield return entry.Item;
        }
    }

    /// <summary>The element with this id; setting one adds it at the end, or replaces it where it stands.</summary>
    /// <param name="id">The element's id.</param>
    /// <exception cref="KeyNotFoundException">Reading an id that is not there.</exception>
    public T this[string id]
    {
        get => _byId[id].Item;
        set => Put(id, value);
    }

    /// <summary>True when an element has this id.</summary>
    public bool ContainsKey(string id) => _byId.ContainsKey(id);

    /// <summary>The element with this id, if there is one.</summary>
    public bool TryGetValue(string id, [MaybeNullWhen(false)] out T item)
    {
        if (_byId.TryGetValue(id, out var entry))
        {
            item = entry.Item;
            return true;
        }

        item = null;
        return false;
    }

    /// <summary>Adds an element at the end, or replaces the one with the same id without moving it.</summary>
    public void Put(string id, T item)
    {
        if (_byId.TryGetValue(id, out var entry))
        {
            var previous = entry.Item;
            entry.Item = item;
            for (var i = 0; i < _indexKeys.Length; i++)
            {
                var before = _indexKeys[i](previous);
                var after = _indexKeys[i](item);
                if (string.Equals(before, after, StringComparison.Ordinal)) continue;
                Unindex(i, before, entry);
                Index(i, after, entry);
            }

            return;
        }

        Insert(id, item, _next++);
    }

    /// <summary>Removes the element with this id. False when there was none.</summary>
    public bool Remove(string id)
    {
        if (!_byId.Remove(id, out var entry)) return false;
        _order.Remove(entry.Sequence);
        for (var i = 0; i < _indexKeys.Length; i++) Unindex(i, _indexKeys[i](entry.Item), entry);
        return true;
    }

    /// <summary>Removes everything.</summary>
    public void Clear()
    {
        _byId.Clear();
        _order.Clear();
        foreach (var index in _indexes) index.Clear();
    }

    /// <summary>The elements whose key in a secondary index is <paramref name="key"/>, in the order they were added.</summary>
    /// <param name="index">Which secondary index, in the order they were given to the constructor.</param>
    /// <param name="key">The key looked for.</param>
    public IEnumerable<T> WithKey(int index, string? key)
    {
        if (key is null || !_indexes[index].TryGetValue(key, out var entries)) yield break;
        foreach (var entry in entries.Values) yield return entry.Item;
    }

    /// <summary>How many elements have <paramref name="key"/> in a secondary index.</summary>
    public int CountWithKey(int index, string? key) =>
        key is not null && _indexes[index].TryGetValue(key, out var entries) ? entries.Count : 0;

    /// <summary>
    /// Moves elements to new ids all at once, each keeping its place: every old id is let go before any new one is
    /// taken, so ids can be shuffled among the elements moving.
    /// </summary>
    /// <param name="moves">The id each element leaves, and the element under its new id.</param>
    public void Rekey(IReadOnlyList<(string OldId, string NewId, T Item)> moves)
    {
        var sequences = new long[moves.Count];
        for (var i = 0; i < moves.Count; i++)
        {
            var entry = _byId[moves[i].OldId];
            sequences[i] = entry.Sequence;
            Remove(moves[i].OldId);
        }

        for (var i = 0; i < moves.Count; i++) Insert(moves[i].NewId, moves[i].Item, sequences[i]);
    }

    private void Insert(string id, T item, long sequence)
    {
        var entry = new Entry(item, sequence);
        _byId.Add(id, entry);
        _order.Add(sequence, entry);
        for (var i = 0; i < _indexKeys.Length; i++) Index(i, _indexKeys[i](item), entry);
    }

    private void Index(int index, string? key, Entry entry)
    {
        if (key is null) return;
        if (!_indexes[index].TryGetValue(key, out var entries))
        {
            entries = [];
            _indexes[index].Add(key, entries);
        }

        entries.Add(entry.Sequence, entry);
    }

    private void Unindex(int index, string? key, Entry entry)
    {
        if (key is null || !_indexes[index].TryGetValue(key, out var entries)) return;
        entries.Remove(entry.Sequence);
        if (entries.Count == 0) _indexes[index].Remove(key);
    }

    /// <summary>One element and the place it was given when it was added.</summary>
    private sealed class Entry(T item, long sequence)
    {
        public T Item { get; set; } = item;

        public long Sequence { get; } = sequence;
    }
}
