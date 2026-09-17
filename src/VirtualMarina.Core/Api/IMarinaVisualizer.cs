using System.Numerics;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Picking;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Api;

/// <summary>
/// The API the host application (typically an ERP) uses to drive the 3D marina: layout, slip status, selection,
/// tooltips and actions, camera and appearance. It is UI-framework and graphics-API agnostic: the same instance drives
/// the WinForms/OpenGL view (<c>MarinaViewControl</c>) and the Blazor/WebGL view (<c>&lt;MarinaView&gt;</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Threading.</b> Not thread-safe. Call it from the UI thread that owns the view (marshal with <c>Control.BeginInvoke</c>
/// in WinForms or <c>InvokeAsync</c> in Blazor when data arrives on a background thread).
/// </para>
/// <para>
/// <b>Events.</b> Raised synchronously on the calling thread, after the state change has been applied, so handlers can
/// read the new state and call back into the API.
/// </para>
/// <para>
/// <b>Snapshots.</b> <see cref="Slip"/>, <see cref="Dock"/>, <see cref="Boat"/>, <see cref="Divider"/> and
/// <see cref="MultiSlipBerth"/> are immutable records. Getters and events return snapshots; change state only through
/// this API. The exception is <see cref="Slip.ExternalData"/>, a mutable bag for host data shared by all snapshots of a slip.
/// </para>
/// <para><b>Ids</b> of docks, slips, dividers and berths are compared case-insensitively.</para>
/// </remarks>
/// <example>
/// <code>
/// IMarinaVisualizer marina = view.Marina;
/// marina.InitializeLayout(layout);
/// marina.SlipSelected += (s, e) => e.Actions.Add("checkin", "Check in", enabled: e.Status == SlipStatus.Free);
/// marina.SlipActionInvoked += (s, e) => erp.Execute(e.ActionId, e.Slips);
/// marina.AssignBoat("A-L03", new Boat("B-77", "Aurora", BoatType.MotorYacht));
/// marina.FocusSlips(new[] { "A-L03", "A-L04" }, CameraAngle.TopDown);
/// </code>
/// </example>
public interface IMarinaVisualizer
{
    // ---- Events -----------------------------------------------------------------------------

    /// <summary>
    /// A slip (its water area or its boat) was clicked or double-clicked with any mouse button.
    /// Raised after the selection change the click caused. Never raised for disabled slips.
    /// </summary>
    event EventHandler<SlipEventArgs>? SlipClicked;

    /// <summary>
    /// A single slip was selected: by a left or right click (including on an already selected slip), through the API,
    /// or because the open popup's content needs refreshing (<see cref="SlipSelectedEventArgs.Reason"/>).
    /// Fill <see cref="SlipSelectedEventArgs.Tooltip"/> (pre-filled with slip and boat details) and
    /// <see cref="SlipSelectedEventArgs.Actions"/> (empty) to control the popup: a left click shows the tooltip,
    /// a right click shows the actions window.
    /// </summary>
    event EventHandler<SlipSelectedEventArgs>? SlipSelected;

    /// <summary>
    /// Two or more slips are selected (Ctrl+click or Shift+click, right-click inside a multi-selection, or <see cref="SetSelection(IEnumerable{string}, bool, CameraAngle?)"/>).
    /// Fill the tooltip and actions for the whole selection.
    /// </summary>
    event EventHandler<MultiSlipSelectedEventArgs>? MultiSlipSelected;

    /// <summary>The set of selected slips or the primary slip changed, including the selection being cleared.</summary>
    event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    /// <summary>The selection became empty (raised right after <see cref="SelectionChanged"/>).</summary>
    event EventHandler? SelectionCleared;

    /// <summary>
    /// The user clicked an enabled action in the actions window (or <see cref="InvokeSlipAction"/> was called).
    /// The window closes afterwards unless <see cref="SlipActionInvokedEventArgs.KeepPopupOpen"/> or <see cref="SlipAction.KeepOpen"/> is set.
    /// </summary>
    event EventHandler<SlipActionInvokedEventArgs>? SlipActionInvoked;

    /// <summary>The tooltip/actions popup opened, closed or changed content. Host views re-render <see cref="ActivePopup"/>.</summary>
    event EventHandler<SlipPopupChangedEventArgs>? PopupChanged;

    /// <summary>The slip under the pointer changed (null when the pointer left all slips). Disabled slips are never hovered.</summary>
    event EventHandler<SlipHoverEventArgs>? SlipHoverChanged;

    /// <summary>
    /// A slip's status or assigned boat changed, through any API (single updates, batches, berths). Raised once per slip,
    /// with the previous and current snapshots.
    /// </summary>
    event EventHandler<SlipStatusChangedEventArgs>? SlipStatusChanged;

    /// <summary>
    /// Docks, slips, dividers or berths were added, updated or removed, or the layout was initialized or cleared.
    /// Inside <see cref="BeginUpdate"/> or <see cref="BatchUpdate"/> the notifications are coalesced into one
    /// <see cref="LayoutChangeKind.BatchUpdated"/>.
    /// </summary>
    event EventHandler<LayoutChangedEventArgs>? LayoutChanged;

    // ---- Layout -----------------------------------------------------------------------------

    /// <summary>Name of the loaded layout (<see cref="MarinaLayout.Name"/>).</summary>
    string MarinaName { get; }

    /// <summary>
    /// Replaces the whole marina: docks, slips, dividers, multi-slip berths and land. Clears the selection and popup,
    /// re-centers the water, regenerates the built-in camera presets and resets the camera to the overview.
    /// </summary>
    /// <param name="layout">The complete marina; build one with <see cref="MarinaLayoutBuilder"/> or by hand.</param>
    /// <exception cref="MarinaLayoutException">The layout is invalid (see <see cref="MarinaLayout.Validate"/>); nothing is changed.</exception>
    void InitializeLayout(MarinaLayout layout);

    /// <summary>A snapshot of the current marina, suitable for saving and passing back to <see cref="InitializeLayout"/>.</summary>
    MarinaLayout GetLayout();

    /// <summary>Removes everything (docks, slips, dividers, berths, land) and clears the selection.</summary>
    void ClearLayout();

    // ---- Docks --------------------------------------------------------------------------------

    /// <summary>
    /// Adds a dock. Use the <see cref="Dock"/> constructor (shore-end start point) or <see cref="Dock.FromCenter"/>
    /// (center point) to set position, size, orientation and <see cref="DockType"/>.
    /// </summary>
    /// <exception cref="MarinaLayoutException">The dock definition is invalid.</exception>
    /// <exception cref="InvalidOperationException">A dock with the same id exists.</exception>
    void AddDock(Dock dock);

    /// <summary>Replaces a dock definition (matched by id). Slips and dividers keep their absolute positions.</summary>
    /// <exception cref="MarinaLayoutException">The dock definition is invalid.</exception>
    /// <exception cref="KeyNotFoundException">No dock has this id.</exception>
    void UpdateDock(Dock dock);

    /// <summary>
    /// Changes a dock's position, size, orientation, type, deck height or name, keeping everything else.
    /// Resizing or rotating keeps the dock's center unless a new start point is given.
    /// </summary>
    /// <returns>The updated dock.</returns>
    /// <exception cref="KeyNotFoundException">No dock has this id.</exception>
    /// <exception cref="MarinaLayoutException">The resulting dock is invalid.</exception>
    Dock UpdateDock(DockUpdate update);

    /// <summary>Removes a dock and its dividers.</summary>
    /// <param name="dockId">Dock to remove.</param>
    /// <param name="removeSlips">Also remove the dock's slips. When false and the dock still has slips, an exception is thrown.</param>
    /// <returns>False when no dock has this id.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="removeSlips"/> is false and the dock has slips.</exception>
    bool RemoveDock(string dockId, bool removeSlips = true);

    /// <summary>The dock with this id, or null.</summary>
    Dock? GetDock(string dockId);

    /// <summary>All docks, in the order they were added.</summary>
    IReadOnlyList<Dock> GetDocks();

    // ---- Dividers -----------------------------------------------------------------------------

    /// <summary>Adds a divider between slips: a finger pier, a row of piles or a floating boom.</summary>
    /// <exception cref="MarinaLayoutException">The divider is invalid or references an unknown dock.</exception>
    /// <exception cref="InvalidOperationException">A divider with the same id exists.</exception>
    void AddDivider(Divider divider);

    /// <summary>Adds several dividers with a single <see cref="LayoutChanged"/> notification.</summary>
    void AddDividers(IEnumerable<Divider> dividers);

    /// <summary>Replaces a divider definition (matched by id).</summary>
    /// <exception cref="KeyNotFoundException">No divider has this id.</exception>
    void UpdateDivider(Divider divider);

    /// <summary>Removes a divider. Returns false when no divider has this id.</summary>
    bool RemoveDivider(string dividerId);

    /// <summary>The divider with this id, or null.</summary>
    Divider? GetDivider(string dividerId);

    /// <summary>All dividers, in the order they were added.</summary>
    IReadOnlyList<Divider> GetDividers();

    /// <summary>Dividers whose <see cref="Divider.DockId"/> is <paramref name="dockId"/>.</summary>
    IReadOnlyList<Divider> GetDividersByDock(string dockId);

    // ---- Slips --------------------------------------------------------------------------------

    /// <summary>Adds a slip. Its position, heading and size are absolute plan coordinates (see <see cref="Slip"/>).</summary>
    /// <exception cref="MarinaLayoutException">The slip is invalid or references an unknown dock or land area.</exception>
    /// <exception cref="InvalidOperationException">A slip with the same id exists.</exception>
    void AddSlip(Slip slip);

    /// <summary>Adds a Free slip at an explicit position, orientation and size.</summary>
    /// <param name="slipId">Unique id.</param>
    /// <param name="dockId">Existing dock the slip belongs to.</param>
    /// <param name="center">Center of the slip's water area in plan coordinates (X = world X, Y = world Z).</param>
    /// <param name="headingDegrees">Direction a moored boat's bow points, normally toward the dock (0° = +Z, 90° = +X).</param>
    /// <param name="length">Length along the heading, in meters.</param>
    /// <param name="width">Width across the heading, in meters.</param>
    /// <param name="label">Display name; the id is used when null.</param>
    /// <returns>The stored slip.</returns>
    Slip AddSlip(string slipId, string dockId, Vector2 center, float headingDegrees, float length, float width, string? label = null);

    /// <summary>Adds several slips with a single <see cref="LayoutChanged"/> notification.</summary>
    void AddSlips(IEnumerable<Slip> slips);

    /// <summary>
    /// Replaces a slip definition (matched by id). The slip keeps its <see cref="Slip.ExternalData"/> instance (entries from a
    /// different dictionary are merged in). A status or boat change on a member of a multi-slip berth changes the whole berth.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No slip has this id.</exception>
    /// <exception cref="MarinaLayoutException">The new definition is invalid.</exception>
    void UpdateSlip(Slip slip);

    /// <summary>
    /// Applies a partial update (only the members set on <paramref name="update"/>) and returns the resulting slip.
    /// A status or boat change on a member of a multi-slip berth changes the whole berth; setting it Free releases the berth.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No slip has this id.</exception>
    /// <exception cref="MarinaLayoutException">The resulting slip is invalid.</exception>
    Slip UpdateSlip(SlipUpdate update);

    /// <summary>
    /// Removes a slip, dropping it from the selection. If it belongs to a multi-slip berth the berth shrinks, or dissolves
    /// when fewer than two slips remain (the last one keeps the boat). Returns false when no slip has this id.
    /// </summary>
    bool RemoveSlip(string slipId);

    /// <summary>The slip with this id, or null.</summary>
    Slip? GetSlip(string slipId);

    /// <summary>All slips, in the order they were added.</summary>
    IReadOnlyList<Slip> GetSlips();

    /// <summary>Slips belonging to a dock.</summary>
    IReadOnlyList<Slip> GetSlipsByDock(string dockId);

    /// <summary>Land slips on a land area (see <see cref="Slip.OnLand"/>).</summary>
    IReadOnlyList<Slip> GetSlipsByLandArea(string landAreaId);

    /// <summary>The land area with this id, or null.</summary>
    LandArea? GetLandArea(string landAreaId);

    /// <summary>All land areas, in layout order.</summary>
    IReadOnlyList<LandArea> GetLandAreas();

    /// <summary>Slips with a given status.</summary>
    IReadOnlyList<Slip> GetSlipsByStatus(SlipStatus status);

    /// <summary>
    /// Applies many partial updates with a single scene rebuild and one <see cref="LayoutChanged"/>. Failing updates
    /// (unknown slip, invalid values) are collected in the result instead of throwing; the others are applied.
    /// </summary>
    BatchUpdateResult BatchUpdate(IEnumerable<SlipUpdate> updates);

    /// <summary>
    /// Starts a batch: <see cref="LayoutChanged"/> notifications are coalesced and popup refreshes deferred until the
    /// returned scope is disposed. Scopes can be nested.
    /// </summary>
    /// <example><code>using (marina.BeginUpdate()) { foreach (var u in updates) marina.UpdateSlip(u); }</code></example>
    IDisposable BeginUpdate();

    // ---- Status and flags ---------------------------------------------------------------------

    /// <summary>
    /// Sets a slip's status. Free always removes the boat; for other statuses a null <paramref name="boat"/> keeps the current boat.
    /// </summary>
    /// <returns>The updated slip.</returns>
    /// <exception cref="KeyNotFoundException">No slip has this id.</exception>
    Slip SetSlipStatus(string slipId, SlipStatus status, Boat? boat = null);

    /// <summary>Marks the slip Occupied (red) by <paramref name="boat"/>.</summary>
    Slip AssignBoat(string slipId, Boat boat);

    /// <summary>Marks the slip Reserved (blue), optionally for a known incoming boat (drawn as a translucent ghost).</summary>
    Slip ReserveSlip(string slipId, Boat? expectedBoat = null);

    /// <summary>Marks the slip Free (green) and removes its boat. On a berth member this releases the whole berth.</summary>
    Slip ReleaseSlip(string slipId);

    /// <summary>
    /// Marks the slip Temporarily Free (yellow): the berth holder's boat is away. The boat stays assigned and is drawn as a
    /// ghost. Pass <paramref name="boat"/> to set or replace it.
    /// </summary>
    Slip MarkTemporarilyFree(string slipId, Boat? boat = null);

    /// <summary>Shows or hides a slip. Hidden slips draw nothing (not even finger piers) and cannot be interacted with.</summary>
    Slip SetSlipVisible(string slipId, bool visible);

    /// <summary>
    /// Disables or enables a slip. Disabled slips are drawn in gray (boat desaturated) and cannot be hovered, selected,
    /// right-clicked or acted on. Disabling a selected slip removes it from the selection.
    /// </summary>
    Slip SetSlipDisabled(string slipId, bool disabled);

    /// <summary>Makes a slip read-only: it can be selected and shows its tooltip, but its actions window never opens.</summary>
    Slip SetSlipReadOnly(string slipId, bool readOnly);

    /// <summary>Sets interaction flags on many slips with a single scene rebuild. Null leaves a flag unchanged.</summary>
    BatchUpdateResult SetSlipFlags(IEnumerable<string> slipIds, bool? visible = null, bool? disabled = null, bool? readOnly = null);

    /// <summary>Slip counts per status, for dashboards.</summary>
    MarinaStatistics GetStatistics();

    // ---- Multi-slip berths --------------------------------------------------------------------

    /// <summary>
    /// Moors one boat alongside (parallel to the dock) across two or more slips, e.g. a superyacht taking several small slips.
    /// Shortcut for <see cref="AssignBoatToSlips"/> with <see cref="MooringStyle.Alongside"/>.
    /// </summary>
    /// <param name="slipIds">Member slips (any number, at least two). The first slip's orientation places the boat.</param>
    /// <param name="boat">The boat.</param>
    /// <param name="status">Occupied, Reserved or TemporarilyFree.</param>
    /// <param name="berthId">Id for the berth; generated from the first slip id when null.</param>
    /// <exception cref="InvalidOperationException">A slip already belongs to another berth, or the berth id is taken.</exception>
    /// <exception cref="MarinaLayoutException">Fewer than two slips, unknown slips, or an invalid boat or status.</exception>
    MultiSlipBerth DockAlongside(IEnumerable<string> slipIds, Boat boat, SlipStatus status = SlipStatus.Occupied, string? berthId = null);

    /// <summary>
    /// Puts a single boat in several slips at once. Every member slip takes <paramref name="status"/> and <paramref name="boat"/>
    /// and gets <see cref="Slip.BerthId"/>; the boat is drawn once across them and finger piers between them are hidden.
    /// </summary>
    /// <param name="slipIds">Member slips (at least two).</param>
    /// <param name="boat">The boat.</param>
    /// <param name="status">Occupied, Reserved or TemporarilyFree.</param>
    /// <param name="style">Alongside (parallel to the dock) or bow-in (centered across the slips).</param>
    /// <param name="berthId">Id for the berth; generated when null.</param>
    /// <exception cref="InvalidOperationException">A slip already belongs to another berth, or the berth id is taken.</exception>
    /// <exception cref="MarinaLayoutException">Fewer than two slips, unknown slips, or an invalid boat or status.</exception>
    MultiSlipBerth AssignBoatToSlips(IEnumerable<string> slipIds, Boat boat, SlipStatus status = SlipStatus.Occupied, MooringStyle style = MooringStyle.Alongside, string? berthId = null);

    /// <summary>Replaces a berth's boat, status, style and/or member slips. Slips no longer listed become Free.</summary>
    /// <exception cref="KeyNotFoundException">No berth has this id.</exception>
    MultiSlipBerth UpdateMultiSlipBerth(MultiSlipBerth berth);

    /// <summary>Partial update of a berth; null arguments leave values unchanged. Slips no longer listed become Free.</summary>
    /// <exception cref="KeyNotFoundException">No berth has this id.</exception>
    MultiSlipBerth UpdateMultiSlipBerth(string berthId, Boat? boat = null, SlipStatus? status = null, MooringStyle? style = null, IEnumerable<string>? slipIds = null);

    /// <summary>Removes the berth and sets all its slips Free. Returns false when no berth has this id.</summary>
    bool ReleaseMultiSlipBerth(string berthId);

    /// <summary>The berth with this id, or null.</summary>
    MultiSlipBerth? GetMultiSlipBerth(string berthId);

    /// <summary>All multi-slip berths.</summary>
    IReadOnlyList<MultiSlipBerth> GetMultiSlipBerths();

    /// <summary>The berth a slip belongs to, or null.</summary>
    MultiSlipBerth? GetMultiSlipBerthForSlip(string slipId);

    // ---- Selection ----------------------------------------------------------------------------

    /// <summary>The primary (most recently clicked or last listed) selected slip, or null.</summary>
    Slip? SelectedSlip { get; }

    /// <summary>All selected slips in selection order; the last one is <see cref="SelectedSlip"/>.</summary>
    IReadOnlyList<Slip> SelectedSlips { get; }

    /// <summary>The slip under the pointer, or null.</summary>
    Slip? HoveredSlip { get; }

    /// <summary>
    /// Makes one slip the only selected slip (raising <see cref="SlipSelected"/>), optionally focusing the camera on it.
    /// Returns false, changing nothing, when the slip doesn't exist or is hidden, disabled or filtered out.
    /// </summary>
    bool SelectSlip(string slipId, bool focusCamera = false);

    /// <summary>Replaces the selection with the selectable slips among <paramref name="slipIds"/>. Returns how many were selected.</summary>
    int SelectSlips(IEnumerable<string> slipIds);

    /// <summary>
    /// Replaces the selection with one or more slips. Disabled slips are discarded, as are hidden, filtered-out and unknown
    /// ids and duplicates; <see cref="SelectionResult.Rejected"/> lists them with the reason. The rest are selected in the
    /// given order (the last becomes primary) and <see cref="SlipSelected"/> or <see cref="MultiSlipSelected"/> is raised.
    /// When nothing remains the selection is cleared.
    /// </summary>
    /// <param name="slipIds">Slips to select.</param>
    /// <param name="focusCamera">Also frame all selected slips (see <see cref="FocusSlips"/>).</param>
    /// <param name="focusAngle">Angle for the focus; null uses <see cref="DefaultFocusAngle"/>.</param>
    /// <example><code>
    /// var result = marina.SetSelection(ids, focusCamera: true, CameraAngle.TopDown);
    /// foreach (var r in result.Rejected) log($"{r.SlipId}: {r.Reason}");
    /// </code></example>
    SelectionResult SetSelection(IEnumerable<string> slipIds, bool focusCamera = false, CameraAngle? focusAngle = null);

    /// <summary>Replaces the selection with the given slips (disabled ones are discarded), without moving the camera.</summary>
    SelectionResult SetSelection(params string[] slipIds);

    /// <summary>Adds a slip to the selection, making it primary. Returns false when it can't be selected.</summary>
    bool AddToSelection(string slipId);

    /// <summary>Removes a slip from the selection. Returns false when it wasn't selected.</summary>
    bool RemoveFromSelection(string slipId);

    /// <summary>True when the slip is part of the selection.</summary>
    bool IsSlipSelected(string slipId);

    /// <summary>Clears the selection and closes the popup.</summary>
    void ClearSelection();

    // ---- Tooltip and actions popup ------------------------------------------------------------

    /// <summary>The tooltip or actions window currently shown above the selection, or null.</summary>
    SlipPopup? ActivePopup { get; }

    /// <summary>Shows the tooltip for the current selection, raising the selection event again to collect content. False when nothing is shown.</summary>
    bool ShowTooltip();

    /// <summary>
    /// Opens the actions window for the current selection (raising the selection event with a right-click button).
    /// Falls back to the tooltip when every selected slip is read-only or there are no visible actions.
    /// </summary>
    bool ShowActions();

    /// <summary>Rebuilds the open popup's content by raising the selection event with <see cref="SelectionReason.Refresh"/>.</summary>
    void RefreshPopup();

    /// <summary>Closes the tooltip or actions window (the selection is kept).</summary>
    void ClosePopup();

    /// <summary>
    /// Invokes an action of the open actions window: raises <see cref="SlipActionInvoked"/> and closes the window unless kept open.
    /// Returns false when no actions window is open or the action doesn't exist, is disabled or hidden.
    /// </summary>
    bool InvokeSlipAction(string actionId);

    /// <summary>Show a tooltip on left-click (default true).</summary>
    bool TooltipsEnabled { get; set; }

    /// <summary>Open the actions window on right-click (default true).</summary>
    bool ActionsEnabled { get; set; }

    /// <summary>Allow Ctrl+click or Shift+click multi-selection (default true).</summary>
    bool MultiSelectEnabled { get; set; }

    // ---- Filtering and appearance -------------------------------------------------------------

    /// <summary>Which slips have their name written on the water: None (default), OnlyFree, NonOccupied or All.</summary>
    SlipLabelMode SlipLabelMode { get; set; }

    /// <summary>Statuses currently shown. Filtered-out slips keep their structure but lose status visuals, boats, labels and interaction.</summary>
    SlipStatusFilter StatusFilter { get; }

    /// <summary>Shows only slips whose status is in <paramref name="filter"/>; filtered-out slips leave the selection.</summary>
    void SetStatusFilter(SlipStatusFilter filter);

    /// <summary>Shows every status (<see cref="SlipStatusFilter.All"/>).</summary>
    void ShowAllStatuses();

    /// <summary>True when the slip exists, is not hidden and passes the status filter.</summary>
    bool IsSlipVisible(string slipId);

    /// <summary>Changes the pad, buoy and ghost-boat color of a status.</summary>
    void SetStatusColor(SlipStatus status, ColorRgba color);

    /// <summary>The current color of a status.</summary>
    ColorRgba GetStatusColor(SlipStatus status);

    /// <summary>Restores the default status colors and overlay opacities.</summary>
    void ResetStatusColors();

    // ---- Camera -------------------------------------------------------------------------------

    /// <summary>The orbit camera. Use it for low-level control (poses, constraints, field of view).</summary>
    OrbitCamera Camera { get; }

    /// <summary>Built-in presets (Overview, Top Down, Sea Side, East, West, Low Angle, one per dock) followed by custom presets.</summary>
    IReadOnlyList<CameraPreset> CameraPresets { get; }

    /// <summary>Moves the camera to the Overview preset.</summary>
    /// <param name="immediate">Jump instead of animating.</param>
    void ResetCamera(bool immediate = false);

    /// <summary>Moves the camera to a preset by name (case-insensitive). Returns false when no preset has this name.</summary>
    bool ApplyCameraPreset(string presetName, bool immediate = false);

    /// <summary>Adds a custom preset, replacing one with the same name. Custom presets survive layout changes.</summary>
    void AddCameraPreset(CameraPreset preset);

    /// <summary>
    /// Angle used by focus calls that don't pass one, by <c>SelectSlip(id, focusCamera: true)</c> and by double-click.
    /// Null (default) keeps the current yaw and looks down at least 35°. Set <see cref="CameraAngle.TopDown"/> for a plan view.
    /// </summary>
    CameraAngle? DefaultFocusAngle { get; set; }

    /// <summary>Centers the camera on a slip at <see cref="DefaultFocusAngle"/>, zoomed to show it whole. False when the slip doesn't exist.</summary>
    bool FocusSlip(string slipId, bool immediate = false);

    /// <summary>Centers the camera on a slip from a specific angle, zoomed to show it whole. False when the slip doesn't exist.</summary>
    bool FocusSlip(string slipId, CameraAngle angle, bool immediate = false);

    /// <summary>
    /// Moves the camera so every listed slip (and its boat) is in view: the target is the middle of the slips and the distance
    /// is the closest that fits them all, with a margin. Unknown ids are ignored; disabled and hidden slips are included.
    /// </summary>
    /// <param name="slipIds">Slips to frame.</param>
    /// <param name="angle">Viewing angle, e.g. <see cref="CameraAngle.TopDown"/> or <c>new CameraAngle(200, 45)</c>. Null uses <see cref="DefaultFocusAngle"/>.</param>
    /// <param name="immediate">Jump instead of animating.</param>
    /// <returns>False when none of the ids exist.</returns>
    bool FocusSlips(IEnumerable<string> slipIds, CameraAngle? angle = null, bool immediate = false);

    /// <summary>Frames the selected slips (see <see cref="FocusSlips"/>). False when nothing is selected.</summary>
    bool FocusSelection(CameraAngle? angle = null, bool immediate = false);

    /// <summary>Moves the camera to the dock's close-up view. False when the dock doesn't exist.</summary>
    bool FocusDock(string dockId, bool immediate = false);

    /// <summary>Sun, ambient light, specular and fog. Changes apply on the next frame.</summary>
    LightingSettings Lighting { get; }

    /// <summary>Water colors and wave animation. Changes apply on the next frame.</summary>
    WaterSettings Water { get; }

    // ---- View helpers -------------------------------------------------------------------------

    /// <summary>
    /// Hit-tests a point in view pixels (origin top-left) against visible, unfiltered slips and boats.
    /// Returns the nearest hit, or null. Disabled slips are hit (they block what's behind them) but input ignores them.
    /// </summary>
    SlipHit? HitTest(float x, float y);

    /// <summary>
    /// Where the popup should point, in view pixels: just above the primary selected slip. Call every frame (the camera moves).
    /// False when no popup is open or the point is behind the camera.
    /// </summary>
    bool TryGetPopupAnchor(out Vector2 screenPoint);
}
