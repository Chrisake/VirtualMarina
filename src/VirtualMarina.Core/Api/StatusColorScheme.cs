using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Api;

/// <summary>Color coding for slip statuses. Change it through <see cref="MarinaVisualizer.SetStatusColor"/>.</summary>
public sealed class StatusColorScheme
{
    /// <summary>Default Free color (green).</summary>
    public static readonly ColorRgba DefaultFree = new(0.20f, 0.78f, 0.32f);

    /// <summary>Default Occupied color (red).</summary>
    public static readonly ColorRgba DefaultOccupied = new(0.90f, 0.20f, 0.18f);

    /// <summary>Default Reserved color (blue).</summary>
    public static readonly ColorRgba DefaultReserved = new(0.22f, 0.46f, 0.96f);

    /// <summary>Default Temporarily Free color (yellow).</summary>
    public static readonly ColorRgba DefaultTemporarilyFree = new(0.98f, 0.80f, 0.12f);

    /// <summary>Default color of disabled slips (gray).</summary>
    public static readonly ColorRgba DefaultDisabled = new(0.58f, 0.60f, 0.62f);

    private readonly Dictionary<SlipStatus, ColorRgba> _colors = new();

    /// <summary>Creates a scheme with the default colors and opacities.</summary>
    public StatusColorScheme()
    {
        Reset();
    }

    /// <summary>Opacity of the colored slip pads on the water.</summary>
    public float PadOpacity { get; internal set; } = 0.45f;

    /// <summary>Opacity of the "ghost" boat shown on reserved and temporarily free slips.</summary>
    public float GhostBoatOpacity { get; internal set; } = 0.4f;

    /// <summary>Pad and buoy color of disabled slips.</summary>
    public ColorRgba DisabledColor { get; internal set; } = DefaultDisabled;

    /// <summary>The color of a status (gray for unknown values).</summary>
    public ColorRgba Get(SlipStatus status) => _colors.TryGetValue(status, out var c) ? c : new ColorRgba(0.6f, 0.6f, 0.6f);

    internal void Set(SlipStatus status, ColorRgba color) => _colors[status] = color;

    internal void Reset()
    {
        _colors[SlipStatus.Free] = DefaultFree;
        _colors[SlipStatus.Occupied] = DefaultOccupied;
        _colors[SlipStatus.Reserved] = DefaultReserved;
        _colors[SlipStatus.TemporarilyFree] = DefaultTemporarilyFree;
        DisabledColor = DefaultDisabled;
        PadOpacity = 0.45f;
        GhostBoatOpacity = 0.4f;
    }
}
