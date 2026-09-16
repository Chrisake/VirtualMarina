using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Api;

/// <summary>Which slips get their name written on the water. Set through <see cref="MarinaVisualizer.SlipLabelMode"/>.</summary>
public enum SlipLabelMode
{
    /// <summary>No labels (default).</summary>
    None = 0,

    /// <summary>Only Free slips.</summary>
    OnlyFree = 1,

    /// <summary>Every slip that is not Occupied: Free, Reserved and Temporarily Free.</summary>
    NonOccupied = 2,

    /// <summary>Every slip.</summary>
    All = 3,
}

/// <summary>Helpers for <see cref="SlipLabelMode"/>.</summary>
public static class SlipLabelModeExtensions
{
    /// <summary>True when a slip with <paramref name="status"/> is labeled in this mode.</summary>
    public static bool Includes(this SlipLabelMode mode, SlipStatus status) => mode switch
    {
        SlipLabelMode.OnlyFree => status == SlipStatus.Free,
        SlipLabelMode.NonOccupied => status != SlipStatus.Occupied,
        SlipLabelMode.All => true,
        _ => false,
    };

    /// <summary>Human-readable name, e.g. "Non-occupied", for UI pickers.</summary>
    public static string GetDisplayName(this SlipLabelMode mode) => mode switch
    {
        SlipLabelMode.None => "None",
        SlipLabelMode.OnlyFree => "Only free",
        SlipLabelMode.NonOccupied => "Non-occupied",
        SlipLabelMode.All => "All",
        _ => mode.ToString(),
    };
}
