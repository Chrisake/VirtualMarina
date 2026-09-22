using System.Numerics;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;

namespace VirtualMarina.Core.Api;

/// <summary>
/// Data for <see cref="IMarinaVisualizer.BerthClicked"/> and the base of <see cref="BerthSelectedEventArgs"/>.
/// Carries an immutable snapshot of the berth at the time of the event.
/// </summary>
public class BerthEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    /// <param name="berth">Snapshot of the berth.</param>
    /// <param name="pier">The berth's pier, if it exists.</param>
    /// <param name="button">Button that triggered the event; <see cref="PointerButton.None"/> for API calls.</param>
    /// <param name="isDoubleClick">True for a double-click.</param>
    /// <param name="worldPoint">World-space point under the pointer, when the event came from a click.</param>
    public BerthEventArgs(Berth berth, Pier? pier, PointerButton button = PointerButton.None, bool isDoubleClick = false, Vector3? worldPoint = null)
    {
        Berth = berth;
        Pier = pier;
        Button = button;
        IsDoubleClick = isDoubleClick;
        WorldPoint = worldPoint;
    }

    /// <summary>Snapshot of the berth when the event was raised.</summary>
    public Berth Berth { get; }

    /// <summary>The berth's id (<c>Berth.Id</c>).</summary>
    public string BerthId => Berth.Id;

    /// <summary>The berth's status (<c>Berth.Status</c>).</summary>
    public BerthStatus Status => Berth.Status;

    /// <summary>Boat assigned to the berth (moored, expected or temporarily away), if known.</summary>
    public Boat? Boat => Berth.Boat;

    /// <summary>Host-owned data bag of the berth; values written here persist with the berth (see <see cref="Berth.ExternalData"/>).</summary>
    public MarinaDataBag ExternalData => Berth.ExternalData;

    /// <summary>The pier the berth belongs to; null for a land berth.</summary>
    public Pier? Pier { get; }

    /// <summary>The land area a land berth is on (<see cref="Berth.LandAreaId"/>); null for a water berth.</summary>
    public LandArea? LandArea { get; init; }

    /// <summary>Button that triggered the event; <see cref="PointerButton.None"/> for programmatic selection.</summary>
    public PointerButton Button { get; }

    /// <summary>True when the event came from a double-click.</summary>
    public bool IsDoubleClick { get; }

    /// <summary>World-space point under the pointer, when the event came from a click.</summary>
    public Vector3? WorldPoint { get; }
}

/// <summary>Why a selection event (<see cref="IMarinaVisualizer.BerthSelected"/> / <see cref="IMarinaVisualizer.MultiBerthSelected"/>) was raised.</summary>
public enum SelectionReason
{
    /// <summary>The user clicked a berth (left or right button, possibly with Ctrl).</summary>
    Pointer = 0,

    /// <summary>Host code called a selection or popup method (e.g. <c>SetSelection</c>, <c>ShowActions</c>).</summary>
    Api = 1,

    /// <summary>
    /// The selection did not change, but a selected berth's data did (or <see cref="IMarinaVisualizer.RefreshPopup"/>
    /// was called) while the popup was open, so the tooltip and actions are being rebuilt.
    /// </summary>
    Refresh = 2,
}

/// <summary>
/// Data for <see cref="IMarinaVisualizer.BerthSelected"/>: a single berth was selected (or re-clicked, or its popup content
/// needs refreshing). Fill <see cref="Tooltip"/> and <see cref="Actions"/> to control the popup: a left click shows the
/// tooltip, a right click (<see cref="OpensActions"/>) shows the actions window.
/// </summary>
/// <example>
/// <code>
/// marina.BerthSelected += (s, e) =>
/// {
///     e.Tooltip.AddLine("Contract", erp.ContractNumber(e.BerthId));
///     e.Actions.Add("checkin", "Check in", enabled: e.Status == BerthStatus.Free, icon: "⚓").Style = BerthActionStyle.Primary;
///     e.Actions.Add("release", "Release", enabled: e.Boat is not null).Style = BerthActionStyle.Danger;
/// };
/// </code>
/// </example>
public sealed class BerthSelectedEventArgs : BerthEventArgs
{
    /// <summary>Creates the event data (raised by the visualizer; hosts normally don't construct it).</summary>
    /// <param name="berth">Snapshot of the selected berth.</param>
    /// <param name="pier">The berth's pier.</param>
    /// <param name="multiBerth">The multi-berth the berth belongs to, if any.</param>
    /// <param name="tooltip">Pre-filled tooltip content.</param>
    /// <param name="actions">Actions collection for the handler to fill.</param>
    /// <param name="reason">Why the event was raised.</param>
    /// <param name="isNewSelection">False when the berth was already the selection.</param>
    /// <param name="button">Button that triggered the event.</param>
    /// <param name="isDoubleClick">True for a double-click.</param>
    /// <param name="worldPoint">World-space point under the pointer.</param>
    public BerthSelectedEventArgs(
        Berth berth, Pier? pier, MultiBerth? multiBerth, BerthTooltip tooltip, BerthActionCollection actions,
        SelectionReason reason, bool isNewSelection,
        PointerButton button = PointerButton.None, bool isDoubleClick = false, Vector3? worldPoint = null)
        : base(berth, pier, button, isDoubleClick, worldPoint)
    {
        MultiBerth = multiBerth;
        Tooltip = tooltip;
        Actions = actions;
        Reason = reason;
        IsNewSelection = isNewSelection;
    }

    /// <summary>The multi-berth the berth belongs to, if any.</summary>
    public MultiBerth? MultiBerth { get; }

    /// <summary>Pre-filled with berth, pier, status and boat details (<see cref="DefaultPopupContent.ForBerth"/>). Edit freely.</summary>
    public BerthTooltip Tooltip { get; }

    /// <summary>Empty by default. Add the actions available for this berth; they are shown on right-click.</summary>
    public BerthActionCollection Actions { get; }

    /// <summary>Why the event was raised: a click, an API call, or a content refresh.</summary>
    public SelectionReason Reason { get; }

    /// <summary>False when the berth was already the selection (re-click or refresh).</summary>
    public bool IsNewSelection { get; }

    /// <summary>True when the actions window will open (right-click), false for the tooltip.</summary>
    public bool OpensActions => Button == PointerButton.Right;
}

/// <summary>
/// Data for <see cref="IMarinaVisualizer.MultiBerthSelected"/>: two or more berths are selected.
/// Fill <see cref="Tooltip"/> and <see cref="Actions"/> for the whole selection.
/// </summary>
public sealed class MultiBerthSelectedEventArgs : EventArgs
{
    /// <summary>Creates the event data (raised by the visualizer; hosts normally don't construct it).</summary>
    /// <param name="berths">Selected berths in selection order.</param>
    /// <param name="tooltip">Pre-filled tooltip content.</param>
    /// <param name="actions">Actions collection for the handler to fill.</param>
    /// <param name="reason">Why the event was raised.</param>
    /// <param name="isNewSelection">False when the set of selected berths did not change.</param>
    /// <param name="button">Button that triggered the event.</param>
    public MultiBerthSelectedEventArgs(
        IReadOnlyList<Berth> berths, BerthTooltip tooltip, BerthActionCollection actions,
        SelectionReason reason, bool isNewSelection, PointerButton button = PointerButton.None)
    {
        Berths = berths;
        Tooltip = tooltip;
        Actions = actions;
        Reason = reason;
        IsNewSelection = isNewSelection;
        Button = button;
    }

    /// <summary>Selected berths in selection order. The last one is <see cref="PrimaryBerth"/>.</summary>
    public IReadOnlyList<Berth> Berths { get; }

    /// <summary>The most recently clicked berth; the popup is drawn above it.</summary>
    public Berth PrimaryBerth => Berths[^1];

    /// <summary>Ids of <see cref="Berths"/>, in selection order.</summary>
    public IReadOnlyList<string> BerthIds => Berths.Select(s => s.Id).ToArray();

    /// <summary>Selected berths that are not read-only, i.e. the ones actions may apply to.</summary>
    public IReadOnlyList<Berth> ActionableBerths => Berths.Where(s => s.AllowsActions).ToArray();

    /// <summary>Pre-filled with a summary of the selection (<see cref="DefaultPopupContent.ForBerths"/>). Edit freely.</summary>
    public BerthTooltip Tooltip { get; }

    /// <summary>Empty by default. Add actions that apply to the whole selection.</summary>
    public BerthActionCollection Actions { get; }

    /// <summary>Why the event was raised: a click, an API call, or a content refresh.</summary>
    public SelectionReason Reason { get; }

    /// <summary>False when the set of selected berths did not change (re-click or refresh).</summary>
    public bool IsNewSelection { get; }

    /// <summary>Button that triggered the event; <see cref="PointerButton.None"/> for API calls.</summary>
    public PointerButton Button { get; }

    /// <summary>True when the actions window will open (right-click), false for the tooltip.</summary>
    public bool OpensActions => Button == PointerButton.Right;
}

/// <summary>Data for <see cref="IMarinaVisualizer.BerthActionInvoked"/>: the user clicked an action in the actions window.</summary>
/// <example>
/// <code>
/// marina.BerthActionInvoked += (s, e) =>
/// {
///     switch (e.ActionId)
///     {
///         case "checkin": marina.AssignBoat(e.BerthId, erp.NextArrival(e.BerthId)); break;
///         case "free-all": marina.BatchUpdate(e.ActionableBerths.Select(x => BerthUpdate.Free(x.Id))); break;
///     }
/// };
/// </code>
/// </example>
public sealed class BerthActionInvokedEventArgs : EventArgs
{
    /// <summary>Creates the event data (raised by the visualizer; hosts normally don't construct it).</summary>
    /// <param name="action">The invoked action.</param>
    /// <param name="berths">The berths the actions window was opened for; the last is the primary berth.</param>
    public BerthActionInvokedEventArgs(BerthAction action, IReadOnlyList<Berth> berths)
    {
        Action = action;
        Berths = berths;
    }

    /// <summary>The <see cref="BerthAction.ActionId"/> of the clicked action.</summary>
    public string ActionId => Action.ActionId;

    /// <summary>The clicked action, including its <see cref="BerthAction.Tag"/>.</summary>
    public BerthAction Action { get; }

    /// <summary>The berth the actions window was opened on (the primary berth for a multi-selection).</summary>
    public Berth Berth => Berths[^1];

    /// <summary>Id of <see cref="Berth"/>.</summary>
    public string BerthId => Berth.Id;

    /// <summary>Every berth the actions window was opened for (current snapshots).</summary>
    public IReadOnlyList<Berth> Berths { get; }

    /// <summary>The berths in <see cref="Berths"/> that are not read-only.</summary>
    public IReadOnlyList<Berth> ActionableBerths => Berths.Where(s => s.AllowsActions).ToArray();

    /// <summary>True when the window was opened for two or more berths.</summary>
    public bool IsMultiSelection => Berths.Count > 1;

    /// <summary>Set true to keep the actions window open (it closes by default unless <see cref="BerthAction.KeepOpen"/> is set).</summary>
    public bool KeepPopupOpen { get; set; }
}

/// <summary>Data for <see cref="IMarinaVisualizer.SelectionChanged"/>.</summary>
public sealed class SelectionChangedEventArgs : EventArgs
{
    /// <summary>Creates the event data for a single-berth selection change.</summary>
    /// <param name="previous">The previously selected berth, or null.</param>
    /// <param name="current">The newly selected berth, or null when cleared.</param>
    public SelectionChangedEventArgs(Berth? previous, Berth? current)
        : this(previous is null ? Array.Empty<Berth>() : new[] { previous }, current is null ? Array.Empty<Berth>() : new[] { current })
    {
    }

    /// <summary>Creates the event data.</summary>
    /// <param name="previousBerths">The previous selection, in selection order.</param>
    /// <param name="currentBerths">The new selection, in selection order (empty when cleared).</param>
    public SelectionChangedEventArgs(IReadOnlyList<Berth> previousBerths, IReadOnlyList<Berth> currentBerths)
    {
        PreviousBerths = previousBerths;
        CurrentBerths = currentBerths;
    }

    /// <summary>The previous primary berth.</summary>
    public Berth? Previous => PreviousBerths.Count > 0 ? PreviousBerths[^1] : null;

    /// <summary>The new primary (most recently clicked) berth, or null when the selection was cleared.</summary>
    public Berth? Current => CurrentBerths.Count > 0 ? CurrentBerths[^1] : null;

    /// <summary>The previous selection, in selection order.</summary>
    public IReadOnlyList<Berth> PreviousBerths { get; }

    /// <summary>The new selection, in selection order (empty when cleared).</summary>
    public IReadOnlyList<Berth> CurrentBerths { get; }

    /// <summary>True when two or more berths are now selected.</summary>
    public bool IsMultiSelection => CurrentBerths.Count > 1;
}

/// <summary>Data for <see cref="IMarinaVisualizer.BerthHoverChanged"/>.</summary>
public sealed class BerthHoverEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    /// <param name="berth">Berth under the pointer, or null.</param>
    public BerthHoverEventArgs(Berth? berth)
    {
        Berth = berth;
    }

    /// <summary>Berth under the pointer, or null when the pointer left all berths.</summary>
    public Berth? Berth { get; }
}

/// <summary>Data for <see cref="IMarinaVisualizer.PopupChanged"/>: the popup above the selection opened, closed, or changed content.</summary>
public sealed class BerthPopupChangedEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    /// <param name="previous">The popup shown before, or null.</param>
    /// <param name="current">The popup shown now, or null when it closed.</param>
    public BerthPopupChangedEventArgs(BerthPopup? previous, BerthPopup? current)
    {
        Previous = previous;
        Current = current;
    }

    /// <summary>The popup shown before the change, or null.</summary>
    public BerthPopup? Previous { get; }

    /// <summary>The popup now shown, or null when it closed.</summary>
    public BerthPopup? Current { get; }
}

/// <summary>Data for <see cref="IMarinaVisualizer.BerthStatusChanged"/>: a berth's status or assigned boat changed.</summary>
public sealed class BerthStatusChangedEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    /// <param name="previous">Snapshot before the change.</param>
    /// <param name="current">Snapshot after the change.</param>
    public BerthStatusChangedEventArgs(Berth previous, Berth current)
    {
        Previous = previous;
        Current = current;
    }

    /// <summary>Snapshot of the berth before the change.</summary>
    public Berth Previous { get; }

    /// <summary>Snapshot of the berth after the change.</summary>
    public Berth Current { get; }

    /// <summary>The berth's id.</summary>
    public string BerthId => Current.Id;

    /// <summary>Status before the change.</summary>
    public BerthStatus OldStatus => Previous.Status;

    /// <summary>Status after the change.</summary>
    public BerthStatus NewStatus => Current.Status;

    /// <summary>Boat before the change.</summary>
    public Boat? OldBoat => Previous.Boat;

    /// <summary>Boat after the change.</summary>
    public Boat? NewBoat => Current.Boat;
}

/// <summary>What changed in a <see cref="IMarinaVisualizer.LayoutChanged"/> notification.</summary>
public enum LayoutChangeKind
{
    /// <summary><see cref="IMarinaVisualizer.InitializeLayout"/> loaded a new marina.</summary>
    Initialized = 0,

    /// <summary><see cref="IMarinaVisualizer.ClearLayout"/> removed everything.</summary>
    Cleared = 1,

    /// <summary>A pier was added (<see cref="LayoutChangedEventArgs.PierId"/>).</summary>
    PierAdded = 2,

    /// <summary>A pier was updated.</summary>
    PierUpdated = 3,

    /// <summary>A pier was removed.</summary>
    PierRemoved = 4,

    /// <summary>A pier was given another id (<see cref="LayoutChangedEventArgs.PierId"/> is the new one).</summary>
    PierRenamed = 19,

    /// <summary>A berth was added (<see cref="LayoutChangedEventArgs.BerthId"/>).</summary>
    BerthAdded = 5,

    /// <summary>A berth was updated (geometry, status, boat, flags, ...).</summary>
    BerthUpdated = 6,

    /// <summary>A berth was removed.</summary>
    BerthRemoved = 7,

    /// <summary>A berth was given another name (<see cref="LayoutChangedEventArgs.BerthId"/> is the new one).</summary>
    BerthRenamed = 8,

    /// <summary>Several changes made inside <see cref="IMarinaVisualizer.BeginUpdate"/> or a batch, coalesced into one notification.</summary>
    BatchUpdated = 9,

    /// <summary>A divider was added (<see cref="LayoutChangedEventArgs.DividerId"/>).</summary>
    DividerAdded = 10,

    /// <summary>A divider was updated.</summary>
    DividerUpdated = 11,

    /// <summary>A divider was removed.</summary>
    DividerRemoved = 12,

    /// <summary>A multi-berth was created (<see cref="LayoutChangedEventArgs.MultiBerthId"/>).</summary>
    MultiBerthAdded = 13,

    /// <summary>A multi-berth changed boat, status, style or member berths.</summary>
    MultiBerthUpdated = 14,

    /// <summary>A multi-berth was released or dissolved.</summary>
    MultiBerthRemoved = 15,

    /// <summary>A land area was added (<see cref="LayoutChangedEventArgs.LandAreaId"/>).</summary>
    LandAreaAdded = 16,

    /// <summary>A land area was updated (outline, height, kind or name).</summary>
    LandAreaUpdated = 17,

    /// <summary>A land area was removed.</summary>
    LandAreaRemoved = 18,

    /// <summary>The mainland behind the marina was set, redrawn or taken away (<c>IMarinaVisualizer.SetShoreline</c>).</summary>
    ShorelineChanged = 19,
}

/// <summary>Data for <see cref="IMarinaVisualizer.LayoutChanged"/>. The id properties that apply to <see cref="Kind"/> are set.</summary>
public sealed class LayoutChangedEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    /// <param name="kind">What changed.</param>
    /// <param name="pierId">Affected pier, if any.</param>
    /// <param name="berthId">Affected berth, if any.</param>
    /// <param name="dividerId">Affected divider, if any.</param>
    /// <param name="multiBerthId">Affected multi-berth, if any.</param>
    /// <param name="landAreaId">Affected land area, if any.</param>
    public LayoutChangedEventArgs(LayoutChangeKind kind, string? pierId = null, string? berthId = null, string? dividerId = null, string? multiBerthId = null, string? landAreaId = null)
    {
        Kind = kind;
        PierId = pierId;
        BerthId = berthId;
        DividerId = dividerId;
        MultiBerthId = multiBerthId;
        LandAreaId = landAreaId;
    }

    /// <summary>What changed.</summary>
    public LayoutChangeKind Kind { get; }

    /// <summary>The affected pier (for pier, berth and divider changes), or null.</summary>
    public string? PierId { get; }

    /// <summary>The affected berth, or null.</summary>
    public string? BerthId { get; }

    /// <summary>The affected divider, or null.</summary>
    public string? DividerId { get; }

    /// <summary>The affected multi-berth, or null.</summary>
    public string? MultiBerthId { get; }

    /// <summary>The affected land area (for land area changes and land berth changes), or null.</summary>
    public string? LandAreaId { get; }
}
