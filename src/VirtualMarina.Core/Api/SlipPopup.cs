using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Api;

/// <summary>Which popup is shown above the selection.</summary>
public enum SlipPopupKind
{
    /// <summary>Information about the selection (left-click).</summary>
    Tooltip,

    /// <summary>Tooltip header plus clickable actions (right-click).</summary>
    Actions,
}

/// <summary>
/// The popup currently shown above the selection (content in <see cref="SlipTooltip"/> and <see cref="SlipAction"/>,
/// see SelectionActions.cs). Host views render it and position it at <see cref="MarinaVisualizer.TryGetPopupAnchor"/> every frame.
/// </summary>
public sealed class SlipPopup
{
    internal SlipPopup(SlipPopupKind kind, IReadOnlyList<Slip> slips, SlipTooltip tooltip, IReadOnlyList<SlipAction> actions, int version)
    {
        Kind = kind;
        Slips = slips;
        Tooltip = tooltip;
        Actions = actions;
        Version = version;
    }

    /// <summary>Tooltip (left-click) or actions window (right-click).</summary>
    public SlipPopupKind Kind { get; }

    /// <summary>The slips the popup is about. The last one is the primary slip the popup points at.</summary>
    public IReadOnlyList<Slip> Slips { get; }

    /// <summary>The slip the popup points at.</summary>
    public Slip PrimarySlip => Slips[^1];

    /// <summary>True when the popup is for two or more slips.</summary>
    public bool IsMultiSelection => Slips.Count > 1;

    /// <summary>Tooltip content produced by the selection event handlers (a copy; editing it has no effect).</summary>
    public SlipTooltip Tooltip { get; }

    /// <summary>Visible actions, in display order. Empty for <see cref="SlipPopupKind.Tooltip"/>.</summary>
    public IReadOnlyList<SlipAction> Actions { get; }

    /// <summary>Changes every time popup content changes; lets views skip re-layout.</summary>
    public int Version { get; }
}
