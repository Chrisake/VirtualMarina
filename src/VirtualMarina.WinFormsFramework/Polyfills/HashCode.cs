using System.Runtime.CompilerServices;

namespace VirtualMarina;

/// <summary>
/// Combines hash codes, as .NET Core's <c>System.HashCode</c>, which .NET Framework does not have. Declared in
/// <c>VirtualMarina</c>, the namespace around all the shared code, so that code finds it without a using; it holds the
/// part of <c>HashCode</c> that code uses.
/// </summary>
/// <remarks>
/// Kept here rather than taken from the Microsoft.Bcl.HashCode package: a host application that already loads another
/// version of that assembly (the ones shipped for .NET Core 3.1 and for .NET 6 have different assembly versions) could
/// not load ours beside it, and the first hash of a berth would fail. The values differ from .NET's, which is allowed:
/// hash codes are never stored or compared across processes.
/// </remarks>
internal struct HashCode
{
    private const uint Prime1 = 2654435761U;
    private const uint Prime2 = 2246822519U;
    private const uint Prime3 = 3266489917U;
    private const uint Prime4 = 668265263U;

    private uint _hash;
    private int _count;

    public static int Combine<T1, T2>(T1 value1, T2 value2)
    {
        var hash = default(HashCode);
        hash.Add(value1);
        hash.Add(value2);
        return hash.ToHashCode();
    }

    public void Add<T>(T value)
    {
        var item = (uint)(value is null ? 0 : EqualityComparer<T>.Default.GetHashCode(value));
        _hash = RotateLeft((_count == 0 ? Prime4 : _hash) + (item * Prime2), 13) * Prime1;
        _count++;
    }

    public readonly int ToHashCode()
    {
        // The finish of xxHash32: every input bit reaches every output bit.
        var hash = _hash + (uint)(_count * 4);
        hash ^= hash >> 15;
        hash *= Prime2;
        hash ^= hash >> 13;
        hash *= Prime3;
        hash ^= hash >> 16;
        return (int)hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint RotateLeft(uint value, int offset) => (value << offset) | (value >> (32 - offset));
}
