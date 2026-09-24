using System.Collections;

namespace VirtualMarina;

/// <summary>
/// A read-only view of a set, standing in for .NET 5's <c>System.Collections.Generic.IReadOnlySet&lt;T&gt;</c>, which
/// .NET Framework does not have. A class rather than an interface, so a <see cref="HashSet{T}"/> converts to it the way it
/// converts to the real interface on .NET 8, and the shared core compiles unchanged.
/// </summary>
/// <typeparam name="T">The type of the elements.</typeparam>
#pragma warning disable CA1715, S101 // Named as the .NET type it stands in for, so the shared source finds it by that name.
public sealed class IReadOnlySet<T> : IReadOnlyCollection<T>
#pragma warning restore CA1715, S101
{
    private readonly HashSet<T> _set;

    private IReadOnlySet(HashSet<T> set) => _set = set ?? throw new ArgumentNullException(nameof(set));

    /// <inheritdoc/>
    public int Count => _set.Count;

    /// <summary>Wraps a set, without copying it.</summary>
    /// <param name="set">The set to wrap.</param>
#pragma warning disable CA2225 // The conversion stands in for HashSet<T> implementing the interface; there is nothing to name.
    public static implicit operator IReadOnlySet<T>(HashSet<T> set) => new(set);
#pragma warning restore CA2225

    /// <summary>Whether the set contains <paramref name="item"/>.</summary>
    /// <param name="item">The element to look for.</param>
    public bool Contains(T item) => _set.Contains(item);

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator() => _set.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
