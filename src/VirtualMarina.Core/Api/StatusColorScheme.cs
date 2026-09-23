using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Api;

/// <summary>
/// Status colors, pad and boat opacities per status, and status markers (<see cref="MarinaStyle.Status"/>). Change the properties
/// directly, or use <see cref="MarinaVisualizer.SetStatusColor"/> and <see cref="MarinaVisualizer.SetOverlayOpacity"/>.
/// </summary>
public sealed class StatusColorScheme : StyleSection
{
    /// <summary>Default Free color (green).</summary>
    public static readonly ColorRgba DefaultFree = new(0.20f, 0.78f, 0.32f);

    /// <summary>Default Occupied color (red).</summary>
    public static readonly ColorRgba DefaultOccupied = new(0.90f, 0.20f, 0.18f);

    /// <summary>Default Reserved color (blue).</summary>
    public static readonly ColorRgba DefaultReserved = new(0.22f, 0.46f, 0.96f);

    /// <summary>Default Temporarily Free color (yellow).</summary>
    public static readonly ColorRgba DefaultTemporarilyFree = new(0.98f, 0.80f, 0.12f);

    /// <summary>Default color of disabled berths (gray).</summary>
    public static readonly ColorRgba DefaultDisabled = new(0.58f, 0.60f, 0.62f);

    private readonly Dictionary<BerthStatus, ColorRgba> _colors = [];
    private float _padOpacity;
    private float _occupiedBoatOpacity;
    private float _reservedBoatOpacity;
    private float _temporarilyFreeBoatOpacity;
    private float _ghostTint;
    private ColorRgba _disabled;
    private bool _showMarkers;
    private float _markerScale;

    /// <summary>Creates a scheme with the default colors and opacities.</summary>
    public StatusColorScheme()
    {
        Reset();
    }

    /// <summary>Color of Free berths.</summary>
    public ColorRgba FreeColor { get => Get(BerthStatus.Free); set => Set(BerthStatus.Free, value); }

    /// <summary>Color of Occupied berths.</summary>
    public ColorRgba OccupiedColor { get => Get(BerthStatus.Occupied); set => Set(BerthStatus.Occupied, value); }

    /// <summary>Color of Reserved berths.</summary>
    public ColorRgba ReservedColor { get => Get(BerthStatus.Reserved); set => Set(BerthStatus.Reserved, value); }

    /// <summary>Color of Temporarily Free berths.</summary>
    public ColorRgba TemporarilyFreeColor { get => Get(BerthStatus.TemporarilyFree); set => Set(BerthStatus.TemporarilyFree, value); }

    /// <summary>Pad and marker color of disabled berths (their boats are drawn in gray).</summary>
    public ColorRgba DisabledColor { get => _disabled; set => SetField(ref _disabled, value); }

    /// <summary>Opacity of the colored berth pads, 0–1 (default 0.45; 0 hides pads except for hover and selection).</summary>
    public float PadOpacity { get => _padOpacity; set => SetField(ref _padOpacity, Clamp(value, 0f, 1f)); }

    /// <summary>Opacity of boats in Occupied berths, 0–1 (default 1; 0 hides them).</summary>
    public float OccupiedBoatOpacity { get => _occupiedBoatOpacity; set => SetField(ref _occupiedBoatOpacity, Clamp(value, 0f, 1f)); }

    /// <summary>Opacity of the expected ("ghost") boat in Reserved berths, 0–1 (default 0.4; 0 hides it).</summary>
    public float ReservedBoatOpacity { get => _reservedBoatOpacity; set => SetField(ref _reservedBoatOpacity, Clamp(value, 0f, 1f)); }

    /// <summary>Opacity of the away ("ghost") boat in Temporarily Free berths, 0–1 (default 0.4; 0 hides it).</summary>
    public float TemporarilyFreeBoatOpacity { get => _temporarilyFreeBoatOpacity; set => SetField(ref _temporarilyFreeBoatOpacity, Clamp(value, 0f, 1f)); }

    /// <summary>
    /// Opacity of the "ghost" boats of reserved and temporarily free berths. Reading returns <see cref="ReservedBoatOpacity"/>;
    /// setting changes both ghost opacities.
    /// </summary>
    public float GhostBoatOpacity
    {
        get => _reservedBoatOpacity;
        set
        {
            var clamped = Clamp(value, 0f, 1f);
            if (clamped == _reservedBoatOpacity && clamped == _temporarilyFreeBoatOpacity) return;
            _reservedBoatOpacity = clamped;
            _temporarilyFreeBoatOpacity = clamped;
            OnChanged();
        }
    }

    /// <summary>How strongly ghost boats take on their status color, 0 (white) to 1 (status color); default 0.55.</summary>
    public float GhostBoatTint { get => _ghostTint; set => SetField(ref _ghostTint, Clamp(value, 0f, 1f)); }

    /// <summary>Show the colored status buoys on the water (posts for land berths). Default true.</summary>
    public bool ShowStatusMarkers { get => _showMarkers; set => SetField(ref _showMarkers, value); }

    /// <summary>Size multiplier of status buoys and posts, 0.2–5 (default 1).</summary>
    public float StatusMarkerScale { get => _markerScale; set => SetField(ref _markerScale, Clamp(value, 0.2f, 5f)); }

    /// <summary>The color of a status (gray for unknown values).</summary>
    public ColorRgba Get(BerthStatus status) => _colors.TryGetValue(status, out var c) ? c : new ColorRgba(0.6f, 0.6f, 0.6f);

    /// <summary>Changes the color of a status.</summary>
    public void Set(BerthStatus status, ColorRgba color)
    {
        if (_colors.TryGetValue(status, out var current) && current == color) return;
        _colors[status] = color;
        OnChanged();
    }

    /// <summary>Opacity of the boat drawn for a status (0 for Free).</summary>
    public float GetBoatOpacity(BerthStatus status) => status switch
    {
        BerthStatus.Occupied => _occupiedBoatOpacity,
        BerthStatus.Reserved => _reservedBoatOpacity,
        BerthStatus.TemporarilyFree => _temporarilyFreeBoatOpacity,
        _ => 0f,
    };

    /// <summary>Takes every color, opacity and marker setting from <paramref name="other"/> (a <see cref="MarinaStyle.Clone"/> step).</summary>
    /// <remarks>Kept next to the fields, like the other sections' copies, so a setting added here is copied too.</remarks>
    internal void CopyFrom(StatusColorScheme other)
    {
        foreach (var status in Enum.GetValues<BerthStatus>()) Set(status, other.Get(status));
        DisabledColor = other.DisabledColor;
        PadOpacity = other.PadOpacity;
        OccupiedBoatOpacity = other.OccupiedBoatOpacity;
        ReservedBoatOpacity = other.ReservedBoatOpacity;
        TemporarilyFreeBoatOpacity = other.TemporarilyFreeBoatOpacity;
        GhostBoatTint = other.GhostBoatTint;
        ShowStatusMarkers = other.ShowStatusMarkers;
        StatusMarkerScale = other.StatusMarkerScale;
    }

    /// <summary>Restores the default colors, opacities and markers.</summary>
    public void Reset()
    {
        _colors[BerthStatus.Free] = DefaultFree;
        _colors[BerthStatus.Occupied] = DefaultOccupied;
        _colors[BerthStatus.Reserved] = DefaultReserved;
        _colors[BerthStatus.TemporarilyFree] = DefaultTemporarilyFree;
        _disabled = DefaultDisabled;
        _padOpacity = 0.45f;
        _occupiedBoatOpacity = 1f;
        _reservedBoatOpacity = 0.4f;
        _temporarilyFreeBoatOpacity = 0.4f;
        _ghostTint = 0.55f;
        _showMarkers = true;
        _markerScale = 1f;
        OnChanged();
    }
}
