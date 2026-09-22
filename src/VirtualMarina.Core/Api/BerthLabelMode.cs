using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Api;

/// <summary>Which berths get their name written on the water. Set through <see cref="MarinaVisualizer.BerthLabelMode"/>.</summary>
public enum BerthLabelMode
{
    /// <summary>No labels (default).</summary>
    None = 0,

    /// <summary>Only Free berths.</summary>
    OnlyFree = 1,

    /// <summary>Every berth that is not Occupied: Free, Reserved and Temporarily Free.</summary>
    NonOccupied = 2,

    /// <summary>Every berth.</summary>
    All = 3,
}

/// <summary>Helpers for <see cref="BerthLabelMode"/>.</summary>
public static class BerthLabelModeExtensions
{
    /// <summary>True when a berth with <paramref name="status"/> is labeled in this mode.</summary>
    public static bool Includes(this BerthLabelMode mode, BerthStatus status) => mode switch
    {
        BerthLabelMode.OnlyFree => status == BerthStatus.Free,
        BerthLabelMode.NonOccupied => status != BerthStatus.Occupied,
        BerthLabelMode.All => true,
        _ => false,
    };

    /// <summary>Human-readable name, e.g. "Non-occupied", for UI pickers.</summary>
    public static string GetDisplayName(this BerthLabelMode mode) => mode switch
    {
        BerthLabelMode.None => Strings.BerthLabelsNone,
        BerthLabelMode.OnlyFree => Strings.BerthLabelsOnlyFree,
        BerthLabelMode.NonOccupied => Strings.BerthLabelsNonOccupied,
        BerthLabelMode.All => Strings.BerthLabelsAll,
        _ => mode.ToString(),
    };
}
