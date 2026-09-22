using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace VirtualMarina.Core.Domain;

/// <summary>
/// Mutable key/value store (string → object) for host application data attached to a berth,
/// e.g. contract ids, invoice objects, UI state or cached ERP records. Available as <see cref="Berth.ExternalData"/>
/// and on every event that references a berth.
/// </summary>
/// <remarks>
/// One bag exists per berth and is shared by every <see cref="Berth"/> snapshot of that berth, so a value
/// saved from an event handler can be read back later from <c>GetBerth</c>, from other events, or after status
/// and geometry updates. The visualizer never reads, renders or serializes these values. Keys are case-sensitive.
/// Like the rest of the visualizer API it is not thread-safe.
/// </remarks>
/// <example>
/// <code>
/// marina.BerthSelected += (s, e) =>
/// {
///     var contract = e.ExternalData.GetOrAdd("Contract", () => erp.LoadContract(e.BerthId));
///     e.Tooltip.AddLine("Contract", contract.Number);
/// };
/// // Later, anywhere:
/// var cached = marina.GetBerth("A-L03")!.ExternalData.Get&lt;Contract&gt;("Contract");
/// </code>
/// </example>
public sealed class BerthDataBag : IDictionary<string, object?>, IReadOnlyDictionary<string, object?>
{
    private readonly Dictionary<string, object?> _items = new(StringComparer.Ordinal);

    /// <summary>Creates an empty bag. New berths get one automatically.</summary>
    public BerthDataBag()
    {
    }

    /// <summary>Creates a bag pre-filled with <paramref name="items"/> (later duplicates overwrite earlier ones).</summary>
    public BerthDataBag(IEnumerable<KeyValuePair<string, object?>> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var (key, value) in items) this[key] = value;
    }

    /// <summary>Gets or sets a value. Getting a missing key throws; use <see cref="Get{T}"/> or <see cref="TryGetValue"/> to avoid that.</summary>
    /// <exception cref="KeyNotFoundException">Getting a key that isn't present.</exception>
    public object? this[string key]
    {
        get => _items[key];
        set
        {
            ArgumentNullException.ThrowIfNull(key);
            _items[key] = value;
        }
    }

    /// <summary>Number of entries.</summary>
    public int Count => _items.Count;

    /// <summary>All keys.</summary>
    public ICollection<string> Keys => _items.Keys;

    /// <summary>All values.</summary>
    public ICollection<object?> Values => _items.Values;

    IEnumerable<string> IReadOnlyDictionary<string, object?>.Keys => _items.Keys;

    IEnumerable<object?> IReadOnlyDictionary<string, object?>.Values => _items.Values;

    bool ICollection<KeyValuePair<string, object?>>.IsReadOnly => false;

    /// <summary>Returns the value if present and of type <typeparamref name="T"/>; otherwise default.</summary>
    public T? Get<T>(string key) => _items.TryGetValue(key, out var value) && value is T typed ? typed : default;

    /// <summary>Gets the value when it is present and of type <typeparamref name="T"/>.</summary>
    /// <returns>False when the key is missing or holds a different type.</returns>
    public bool TryGet<T>(string key, [MaybeNullWhen(false)] out T value)
    {
        if (_items.TryGetValue(key, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Returns the existing <typeparamref name="T"/> value, or creates it with <paramref name="factory"/> and stores it.</summary>
    public T GetOrAdd<T>(string key, Func<T> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (TryGet<T>(key, out var existing)) return existing;
        var created = factory();
        this[key] = created;
        return created;
    }

    /// <summary>Stores a value, replacing any existing one.</summary>
    public void Set(string key, object? value) => this[key] = value;

    /// <summary>Adds a value.</summary>
    /// <exception cref="ArgumentException">The key already exists.</exception>
    public void Add(string key, object? value) => _items.Add(key, value);

    /// <summary>True when the key is present.</summary>
    public bool ContainsKey(string key) => _items.ContainsKey(key);

    /// <summary>Removes a key. Returns false when it wasn't present.</summary>
    public bool Remove(string key) => _items.Remove(key);

    /// <summary>Gets a value of any type.</summary>
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out object? value) => _items.TryGetValue(key, out value);

    /// <summary>Removes every entry.</summary>
    public void Clear() => _items.Clear();

    /// <summary>Enumerates the entries.</summary>
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    void ICollection<KeyValuePair<string, object?>>.Add(KeyValuePair<string, object?> item) =>
        ((ICollection<KeyValuePair<string, object?>>)_items).Add(item);

    bool ICollection<KeyValuePair<string, object?>>.Contains(KeyValuePair<string, object?> item) =>
        ((ICollection<KeyValuePair<string, object?>>)_items).Contains(item);

    void ICollection<KeyValuePair<string, object?>>.CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex) =>
        ((ICollection<KeyValuePair<string, object?>>)_items).CopyTo(array, arrayIndex);

    bool ICollection<KeyValuePair<string, object?>>.Remove(KeyValuePair<string, object?> item) =>
        ((ICollection<KeyValuePair<string, object?>>)_items).Remove(item);

    /// <summary>Copies entries from another bag, overwriting existing keys.</summary>
    internal void MergeFrom(BerthDataBag other)
    {
        foreach (var (key, value) in other._items) _items[key] = value;
    }
}
