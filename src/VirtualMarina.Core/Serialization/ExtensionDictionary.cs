using System.Collections;
using System.Text.Json;
using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Serialization;

/// <summary>
/// The dictionary behind <see cref="MarinaDocument.Extensions"/>: an ordinary one, except that it refuses the names the format
/// writes at the top of the file itself, which would otherwise be written a second time and read back as the wrong thing.
/// </summary>
internal sealed class ExtensionDictionary : IDictionary<string, JsonElement>
{
    /// <summary>
    /// The top-level names of the format, read from the generated metadata of the document so the list can't fall behind it.
    /// Compared ignoring case, because the reader matches names that way.
    /// </summary>
    private static readonly HashSet<string> Reserved = new(
        MarinaJson.Indented.DocumentDto.Properties.Where(property => !property.IsExtensionData).Select(property => property.Name),
        StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, JsonElement> _items = new(StringComparer.Ordinal);

    public int Count => _items.Count;

    public bool IsReadOnly => false;

    public ICollection<string> Keys => _items.Keys;

    public ICollection<JsonElement> Values => _items.Values;

    public JsonElement this[string key]
    {
        get => _items[key];
        set
        {
            ThrowIfReserved(key);
            _items[key] = value;
        }
    }

    /// <summary>True when <paramref name="key"/> is a name the format uses at the top of the file.</summary>
    public static bool IsReserved(string key) => Reserved.Contains(key);

    /// <summary>Refuses a key the format uses itself.</summary>
    /// <exception cref="ArgumentException">It is one.</exception>
    public static void ThrowIfReserved(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (IsReserved(key))
        {
            throw new ArgumentException(Strings.Format(Strings.ErrorReservedExtensionKey, key), nameof(key));
        }
    }

    public void Add(string key, JsonElement value)
    {
        ThrowIfReserved(key);
        _items.Add(key, value);
    }

    public void Add(KeyValuePair<string, JsonElement> item) => Add(item.Key, item.Value);

    public void Clear() => _items.Clear();

    public bool Contains(KeyValuePair<string, JsonElement> item) => ((ICollection<KeyValuePair<string, JsonElement>>)_items).Contains(item);

    public bool ContainsKey(string key) => _items.ContainsKey(key);

    public void CopyTo(KeyValuePair<string, JsonElement>[] array, int arrayIndex) =>
        ((ICollection<KeyValuePair<string, JsonElement>>)_items).CopyTo(array, arrayIndex);

    public bool Remove(string key) => _items.Remove(key);

    public bool Remove(KeyValuePair<string, JsonElement> item) => ((ICollection<KeyValuePair<string, JsonElement>>)_items).Remove(item);

    public bool TryGetValue(string key, out JsonElement value) => _items.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<string, JsonElement>> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
