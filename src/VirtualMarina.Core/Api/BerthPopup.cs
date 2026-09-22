using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Api;

/// <summary>Which popup is shown above the selection.</summary>
public enum BerthPopupKind
{
    /// <summary>Information about the selection (left-click).</summary>
    Tooltip,

    /// <summary>Tooltip header plus clickable actions (right-click).</summary>
    Actions,
}

/// <summary>
/// The popup currently shown above the selection (content in <see cref="BerthTooltip"/> and <see cref="BerthAction"/>,
/// see SelectionActions.cs). Host views render it and position it at <see cref="MarinaVisualizer.TryGetPopupAnchor"/> every frame.
/// </summary>
public sealed class BerthPopup
{
    internal BerthPopup(BerthPopupKind kind, IReadOnlyList<Berth> berths, BerthTooltip tooltip, IReadOnlyList<BerthAction> actions, int version)
    {
        Kind = kind;
        Berths = berths;
        Tooltip = tooltip;
        Actions = actions;
        Version = version;
    }

    /// <summary>Tooltip (left-click) or actions window (right-click).</summary>
    public BerthPopupKind Kind { get; }

    /// <summary>The berths the popup is about. The last one is the primary berth the popup points at.</summary>
    public IReadOnlyList<Berth> Berths { get; }

    /// <summary>The berth the popup points at.</summary>
    public Berth PrimaryBerth => Berths[^1];

    /// <summary>True when the popup is for two or more berths.</summary>
    public bool IsMultiSelection => Berths.Count > 1;

    /// <summary>Tooltip content produced by the selection event handlers (a copy; editing it has no effect).</summary>
    public BerthTooltip Tooltip { get; }

    /// <summary>Visible actions, in display order. Empty for <see cref="BerthPopupKind.Tooltip"/>.</summary>
    public IReadOnlyList<BerthAction> Actions { get; }

    /// <summary>Changes every time popup content changes; lets views skip re-layout.</summary>
    public int Version { get; }
}
