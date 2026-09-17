using System.Numerics;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;

namespace VirtualMarina.Core.Api;

/// <summary>
/// Data for <see cref="IMarinaVisualizer.SlipClicked"/> and the base of <see cref="SlipSelectedEventArgs"/>.
/// Carries an immutable snapshot of the slip at the time of the event.
/// </summary>
public class SlipEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    /// <param name="slip">Snapshot of the slip.</param>
    /// <param name="dock">The slip's dock, if it exists.</param>
    /// <param name="button">Button that triggered the event; <see cref="PointerButton.None"/> for API calls.</param>
    /// <param name="isDoubleClick">True for a double-click.</param>
    /// <param name="worldPoint">World-space point under the pointer, when the event came from a click.</param>
    public SlipEventArgs(Slip slip, Dock? dock, PointerButton button = PointerButton.None, bool isDoubleClick = false, Vector3? worldPoint = null)
    {
        Slip = slip;
        Dock = dock;
        Button = button;
        IsDoubleClick = isDoubleClick;
        WorldPoint = worldPoint;
    }

    /// <summary>Snapshot of the slip when the event was raised.</summary>
    public Slip Slip { get; }

    /// <summary>The slip's id (<c>Slip.Id</c>).</summary>
    public string SlipId => Slip.Id;

    /// <summary>The slip's status (<c>Slip.Status</c>).</summary>
    public SlipStatus Status => Slip.Status;

    /// <summary>Boat assigned to the slip (moored, expected or temporarily away), if known.</summary>
    public Boat? Boat => Slip.Boat;

    /// <summary>Host-owned data bag of the slip; values written here persist with the slip (see <see cref="Slip.ExternalData"/>).</summary>
    public SlipDataBag ExternalData => Slip.ExternalData;

    /// <summary>The dock the slip belongs to; null for a land slip.</summary>
    public Dock? Dock { get; }

    /// <summary>The land area a land slip is on (<see cref="Slip.LandAreaId"/>); null for a water slip.</summary>
    public LandArea? LandArea { get; init; }

    /// <summary>Button that triggered the event; <see cref="PointerButton.None"/> for programmatic selection.</summary>
    public PointerButton Button { get; }

    /// <summary>True when the event came from a double-click.</summary>
    public bool IsDoubleClick { get; }

    /// <summary>World-space point under the pointer, when the event came from a click.</summary>
    public Vector3? WorldPoint { get; }
}

/// <summary>Why a selection event (<see cref="IMarinaVisualizer.SlipSelected"/> / <see cref="IMarinaVisualizer.MultiSlipSelected"/>) was raised.</summary>
public enum SelectionReason
{
    /// <summary>The user clicked a slip (left or right button, possibly with Ctrl).</summary>
    Pointer,

    /// <summary>Host code called a selection or popup method (e.g. <c>SetSelection</c>, <c>ShowActions</c>).</summary>
    Api,

    /// <summary>
    /// The selection did not change, but a selected slip's data did (or <see cref="IMarinaVisualizer.RefreshPopup"/>
    /// was called) while the popup was open, so the tooltip and actions are being rebuilt.
    /// </summary>
    Refresh,
}

/// <summary>
/// Data for <see cref="IMarinaVisualizer.SlipSelected"/>: a single slip was selected (or re-clicked, or its popup content
/// needs refreshing). Fill <see cref="Tooltip"/> and <see cref="Actions"/> to control the popup: a left click shows the
/// tooltip, a right click (<see cref="OpensActions"/>) shows the actions window.
/// </summary>
/// <example>
/// <code>
/// marina.SlipSelected += (s, e) =>
/// {
///     e.Tooltip.AddLine("Contract", erp.ContractNumber(e.SlipId));
///     e.Actions.Add("checkin", "Check in", enabled: e.Status == SlipStatus.Free, icon: "⚓").Style = SlipActionStyle.Primary;
///     e.Actions.Add("release", "Release", enabled: e.Boat is not null).Style = SlipActionStyle.Danger;
/// };
/// </code>
/// </example>
public sealed class SlipSelectedEventArgs : SlipEventArgs
{
    /// <summary>Creates the event data (raised by the visualizer; hosts normally don't construct it).</summary>
    /// <param name="slip">Snapshot of the selected slip.</param>
    /// <param name="dock">The slip's dock.</param>
    /// <param name="berth">The multi-slip berth the slip belongs to, if any.</param>
    /// <param name="tooltip">Pre-filled tooltip content.</param>
    /// <param name="actions">Actions collection for the handler to fill.</param>
    /// <param name="reason">Why the event was raised.</param>
    /// <param name="isNewSelection">False when the slip was already the selection.</param>
    /// <param name="button">Button that triggered the event.</param>
    /// <param name="isDoubleClick">True for a double-click.</param>
    /// <param name="worldPoint">World-space point under the pointer.</param>
    public SlipSelectedEventArgs(
        Slip slip, Dock? dock, MultiSlipBerth? berth, SlipTooltip tooltip, SlipActionCollection actions,
        SelectionReason reason, bool isNewSelection,
        PointerButton button = PointerButton.None, bool isDoubleClick = false, Vector3? worldPoint = null)
        : base(slip, dock, button, isDoubleClick, worldPoint)
    {
        Berth = berth;
        Tooltip = tooltip;
        Actions = actions;
        Reason = reason;
        IsNewSelection = isNewSelection;
    }

    /// <summary>The multi-slip berth the slip belongs to, if any.</summary>
    public MultiSlipBerth? Berth { get; }

    /// <summary>Pre-filled with slip, dock, status and boat details (<see cref="DefaultPopupContent.ForSlip"/>). Edit freely.</summary>
    public SlipTooltip Tooltip { get; }

    /// <summary>Empty by default. Add the actions available for this slip; they are shown on right-click.</summary>
    public SlipActionCollection Actions { get; }

    /// <summary>Why the event was raised: a click, an API call, or a content refresh.</summary>
    public SelectionReason Reason { get; }

    /// <summary>False when the slip was already the selection (re-click or refresh).</summary>
    public bool IsNewSelection { get; }

    /// <summary>True when the actions window will open (right-click), false for the tooltip.</summary>
    public bool OpensActions => Button == PointerButton.Right;
}

/// <summary>
/// Data for <see cref="IMarinaVisualizer.MultiSlipSelected"/>: two or more slips are selected.
/// Fill <see cref="Tooltip"/> and <see cref="Actions"/> for the whole selection.
/// </summary>
public sealed class MultiSlipSelectedEventArgs : EventArgs
{
    /// <summary>Creates the event data (raised by the visualizer; hosts normally don't construct it).</summary>
    /// <param name="slips">Selected slips in selection order.</param>
    /// <param name="tooltip">Pre-filled tooltip content.</param>
    /// <param name="actions">Actions collection for the handler to fill.</param>
    /// <param name="reason">Why the event was raised.</param>
    /// <param name="isNewSelection">False when the set of selected slips did not change.</param>
    /// <param name="button">Button that triggered the event.</param>
    public MultiSlipSelectedEventArgs(
        IReadOnlyList<Slip> slips, SlipTooltip tooltip, SlipActionCollection actions,
        SelectionReason reason, bool isNewSelection, PointerButton button = PointerButton.None)
    {
        Slips = slips;
        Tooltip = tooltip;
        Actions = actions;
        Reason = reason;
        IsNewSelection = isNewSelection;
        Button = button;
    }

    /// <summary>Selected slips in selection order. The last one is <see cref="PrimarySlip"/>.</summary>
    public IReadOnlyList<Slip> Slips { get; }

    /// <summary>The most recently clicked slip; the popup is drawn above it.</summary>
    public Slip PrimarySlip => Slips[^1];

    /// <summary>Ids of <see cref="Slips"/>, in selection order.</summary>
    public IReadOnlyList<string> SlipIds => Slips.Select(s => s.Id).ToArray();

    /// <summary>Selected slips that are not read-only, i.e. the ones actions may apply to.</summary>
    public IReadOnlyList<Slip> ActionableSlips => Slips.Where(s => s.AllowsActions).ToArray();

    /// <summary>Pre-filled with a summary of the selection (<see cref="DefaultPopupContent.ForSlips"/>). Edit freely.</summary>
    public SlipTooltip Tooltip { get; }

    /// <summary>Empty by default. Add actions that apply to the whole selection.</summary>
    public SlipActionCollection Actions { get; }

    /// <summary>Why the event was raised: a click, an API call, or a content refresh.</summary>
    public SelectionReason Reason { get; }

    /// <summary>False when the set of selected slips did not change (re-click or refresh).</summary>
    public bool IsNewSelection { get; }

    /// <summary>Button that triggered the event; <see cref="PointerButton.None"/> for API calls.</summary>
    public PointerButton Button { get; }

    /// <summary>True when the actions window will open (right-click), false for the tooltip.</summary>
    public bool OpensActions => Button == PointerButton.Right;
}

/// <summary>Data for <see cref="IMarinaVisualizer.SlipActionInvoked"/>: the user clicked an action in the actions window.</summary>
/// <example>
/// <code>
/// marina.SlipActionInvoked += (s, e) =>
/// {
///     switch (e.ActionId)
///     {
///         case "checkin": marina.AssignBoat(e.SlipId, erp.NextArrival(e.SlipId)); break;
///         case "free-all": marina.BatchUpdate(e.ActionableSlips.Select(x => SlipUpdate.Free(x.Id))); break;
///     }
/// };
/// </code>
/// </example>
public sealed class SlipActionInvokedEventArgs : EventArgs
{
    /// <summary>Creates the event data (raised by the visualizer; hosts normally don't construct it).</summary>
    /// <param name="action">The invoked action.</param>
    /// <param name="slips">The slips the actions window was opened for; the last is the primary slip.</param>
    public SlipActionInvokedEventArgs(SlipAction action, IReadOnlyList<Slip> slips)
    {
        Action = action;
        Slips = slips;
    }

    /// <summary>The <see cref="SlipAction.ActionId"/> of the clicked action.</summary>
    public string ActionId => Action.ActionId;

    /// <summary>The clicked action, including its <see cref="SlipAction.Tag"/>.</summary>
    public SlipAction Action { get; }

    /// <summary>The slip the actions window was opened on (the primary slip for a multi-selection).</summary>
    public Slip Slip => Slips[^1];

    /// <summary>Id of <see cref="Slip"/>.</summary>
    public string SlipId => Slip.Id;

    /// <summary>Every slip the actions window was opened for (current snapshots).</summary>
    public IReadOnlyList<Slip> Slips { get; }

    /// <summary>The slips in <see cref="Slips"/> that are not read-only.</summary>
    public IReadOnlyList<Slip> ActionableSlips => Slips.Where(s => s.AllowsActions).ToArray();

    /// <summary>True when the window was opened for two or more slips.</summary>
    public bool IsMultiSelection => Slips.Count > 1;

    /// <summary>Set true to keep the actions window open (it closes by default unless <see cref="SlipAction.KeepOpen"/> is set).</summary>
    public bool KeepPopupOpen { get; set; }
}

/// <summary>Data for <see cref="IMarinaVisualizer.SelectionChanged"/>.</summary>
public sealed class SelectionChangedEventArgs : EventArgs
{
    /// <summary>Creates the event data for a single-slip selection change.</summary>
    /// <param name="previous">The previously selected slip, or null.</param>
    /// <param name="current">The newly selected slip, or null when cleared.</param>
    public SelectionChangedEventArgs(Slip? previous, Slip? current)
        : this(previous is null ? Array.Empty<Slip>() : new[] { previous }, current is null ? Array.Empty<Slip>() : new[] { current })
    {
    }

    /// <summary>Creates the event data.</summary>
    /// <param name="previousSlips">The previous selection, in selection order.</param>
    /// <param name="currentSlips">The new selection, in selection order (empty when cleared).</param>
    public SelectionChangedEventArgs(IReadOnlyList<Slip> previousSlips, IReadOnlyList<Slip> currentSlips)
    {
        PreviousSlips = previousSlips;
        CurrentSlips = currentSlips;
    }

    /// <summary>The previous primary slip.</summary>
    public Slip? Previous => PreviousSlips.Count > 0 ? PreviousSlips[^1] : null;

    /// <summary>The new primary (most recently clicked) slip, or null when the selection was cleared.</summary>
    public Slip? Current => CurrentSlips.Count > 0 ? CurrentSlips[^1] : null;

    /// <summary>The previous selection, in selection order.</summary>
    public IReadOnlyList<Slip> PreviousSlips { get; }

    /// <summary>The new selection, in selection order (empty when cleared).</summary>
    public IReadOnlyList<Slip> CurrentSlips { get; }

    /// <summary>True when two or more slips are now selected.</summary>
    public bool IsMultiSelection => CurrentSlips.Count > 1;
}

/// <summary>Data for <see cref="IMarinaVisualizer.SlipHoverChanged"/>.</summary>
public sealed class SlipHoverEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    /// <param name="slip">Slip under the pointer, or null.</param>
    public SlipHoverEventArgs(Slip? slip)
    {
        Slip = slip;
    }

    /// <summary>Slip under the pointer, or null when the pointer left all slips.</summary>
    public Slip? Slip { get; }
}

/// <summary>Data for <see cref="IMarinaVisualizer.PopupChanged"/>: the popup above the selection opened, closed, or changed content.</summary>
public sealed class SlipPopupChangedEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    /// <param name="previous">The popup shown before, or null.</param>
    /// <param name="current">The popup shown now, or null when it closed.</param>
    public SlipPopupChangedEventArgs(SlipPopup? previous, SlipPopup? current)
    {
        Previous = previous;
        Current = current;
    }

    /// <summary>The popup shown before the change, or null.</summary>
    public SlipPopup? Previous { get; }

    /// <summary>The popup now shown, or null when it closed.</summary>
    public SlipPopup? Current { get; }
}

/// <summary>Data for <see cref="IMarinaVisualizer.SlipStatusChanged"/>: a slip's status or assigned boat changed.</summary>
public sealed class SlipStatusChangedEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    /// <param name="previous">Snapshot before the change.</param>
    /// <param name="current">Snapshot after the change.</param>
    public SlipStatusChangedEventArgs(Slip previous, Slip current)
    {
        Previous = previous;
        Current = current;
    }

    /// <summary>Snapshot of the slip before the change.</summary>
    public Slip Previous { get; }

    /// <summary>Snapshot of the slip after the change.</summary>
    public Slip Current { get; }

    /// <summary>The slip's id.</summary>
    public string SlipId => Current.Id;

    /// <summary>Status before the change.</summary>
    public SlipStatus OldStatus => Previous.Status;

    /// <summary>Status after the change.</summary>
    public SlipStatus NewStatus => Current.Status;

    /// <summary>Boat before the change.</summary>
    public Boat? OldBoat => Previous.Boat;

    /// <summary>Boat after the change.</summary>
    public Boat? NewBoat => Current.Boat;
}

/// <summary>What changed in a <see cref="IMarinaVisualizer.LayoutChanged"/> notification.</summary>
public enum LayoutChangeKind
{
    /// <summary><see cref="IMarinaVisualizer.InitializeLayout"/> loaded a new marina.</summary>
    Initialized,

    /// <summary><see cref="IMarinaVisualizer.ClearLayout"/> removed everything.</summary>
    Cleared,

    /// <summary>A dock was added (<see cref="LayoutChangedEventArgs.DockId"/>).</summary>
    DockAdded,

    /// <summary>A dock was updated.</summary>
    DockUpdated,

    /// <summary>A dock was removed.</summary>
    DockRemoved,

    /// <summary>A slip was added (<see cref="LayoutChangedEventArgs.SlipId"/>).</summary>
    SlipAdded,

    /// <summary>A slip was updated (geometry, status, boat, flags, ...).</summary>
    SlipUpdated,

    /// <summary>A slip was removed.</summary>
    SlipRemoved,

    /// <summary>Several changes made inside <see cref="IMarinaVisualizer.BeginUpdate"/> or a batch, coalesced into one notification.</summary>
    BatchUpdated,

    /// <summary>A divider was added (<see cref="LayoutChangedEventArgs.DividerId"/>).</summary>
    DividerAdded,

    /// <summary>A divider was updated.</summary>
    DividerUpdated,

    /// <summary>A divider was removed.</summary>
    DividerRemoved,

    /// <summary>A multi-slip berth was created (<see cref="LayoutChangedEventArgs.BerthId"/>).</summary>
    BerthAdded,

    /// <summary>A multi-slip berth changed boat, status, style or member slips.</summary>
    BerthUpdated,

    /// <summary>A multi-slip berth was released or dissolved.</summary>
    BerthRemoved,
}

/// <summary>Data for <see cref="IMarinaVisualizer.LayoutChanged"/>. The id properties that apply to <see cref="Kind"/> are set.</summary>
public sealed class LayoutChangedEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    /// <param name="kind">What changed.</param>
    /// <param name="dockId">Affected dock, if any.</param>
    /// <param name="slipId">Affected slip, if any.</param>
    /// <param name="dividerId">Affected divider, if any.</param>
    /// <param name="berthId">Affected multi-slip berth, if any.</param>
    public LayoutChangedEventArgs(LayoutChangeKind kind, string? dockId = null, string? slipId = null, string? dividerId = null, string? berthId = null)
    {
        Kind = kind;
        DockId = dockId;
        SlipId = slipId;
        DividerId = dividerId;
        BerthId = berthId;
    }

    /// <summary>What changed.</summary>
    public LayoutChangeKind Kind { get; }

    /// <summary>The affected dock (for dock, slip and divider changes), or null.</summary>
    public string? DockId { get; }

    /// <summary>The affected slip, or null.</summary>
    public string? SlipId { get; }

    /// <summary>The affected divider, or null.</summary>
    public string? DividerId { get; }

    /// <summary>The affected multi-slip berth, or null.</summary>
    public string? BerthId { get; }
}
