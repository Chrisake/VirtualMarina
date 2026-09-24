using System.Runtime.CompilerServices;

namespace VirtualMarina;

/// <summary>Compares objects by reference, as .NET 5's <c>System.Collections.Generic.ReferenceEqualityComparer</c>.</summary>
internal sealed class ReferenceEqualityComparer : IEqualityComparer<object?>
{
    private ReferenceEqualityComparer()
    {
    }

    public static ReferenceEqualityComparer Instance { get; } = new();

    public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

    public int GetHashCode(object? obj) => RuntimeHelpers.GetHashCode(obj);
}
