using System.Numerics;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Picking;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Api;

/// <summary>
/// The API the host application (typically an ERP) uses to drive the 3D marina: layout, berth status, selection,
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
/// <b>Snapshots.</b> <see cref="Berth"/>, <see cref="Pier"/>, <see cref="Boat"/>, <see cref="Divider"/> and
/// <see cref="MultiBerth"/> are immutable records. Getters and events return snapshots; change state only through
/// this API. The exception is <see cref="Berth.ExternalData"/>, a mutable bag for host data shared by all snapshots of a berth.
/// </para>
/// <para><b>Ids</b> of piers, berths, dividers and berths are compared case-insensitively.</para>
/// <para>
/// <b>Implementing this interface</b> is not supported: it describes what <see cref="MarinaVisualizer"/> offers, and
/// members are added to it in feature releases, which would break an outside implementation. Depend on it to keep host
/// code testable — a mocking library fills in new members by itself — but let <see cref="MarinaVisualizer"/> be the only
/// real implementation. <see cref="Rendering.ISceneRenderer"/> is the interface meant to be implemented outside the
/// library; anything added to that one comes with a default implementation. See <c>Docs/16-compatibility.md</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// IMarinaVisualizer marina = view.Marina;
/// marina.InitializeLayout(layout);
/// marina.BerthSelected += (s, e) => e.Actions.Add("checkin", "Check in", enabled: e.Status == BerthStatus.Free);
/// marina.BerthActionInvoked += (s, e) => erp.Execute(e.ActionId, e.Berths);
/// marina.AssignBoat("A-L03", new Boat("B-77", "Aurora", BoatType.MotorYacht));
/// marina.FocusBerths(new[] { "A-L03", "A-L04" }, CameraAngle.TopDown);
/// </code>
/// </example>
public interface IMarinaVisualizer
{
    // ---- Events -----------------------------------------------------------------------------

    /// <summary>
    /// A berth (its water area or its boat) was clicked or double-clicked with any mouse button.
    /// Raised after the selection change the click caused. Never raised for disabled berths.
    /// </summary>
    event EventHandler<BerthEventArgs>? BerthClicked;

    /// <summary>
    /// A single berth was selected: by a left or right click (including on an already selected berth), through the API,
    /// or because the open popup's content needs refreshing (<see cref="BerthSelectedEventArgs.Reason"/>).
    /// Fill <see cref="BerthSelectedEventArgs.Tooltip"/> (pre-filled with berth and boat details) and
    /// <see cref="BerthSelectedEventArgs.Actions"/> (empty) to control the popup: a left click shows the tooltip,
    /// a right click shows the actions window.
    /// </summary>
    event EventHandler<BerthSelectedEventArgs>? BerthSelected;

    /// <summary>
    /// Two or more berths are selected (Ctrl+click or Shift+click, right-click inside a multi-selection, or <see cref="SetSelection(IEnumerable{string}, bool, CameraAngle?)"/>).
    /// Fill the tooltip and actions for the whole selection.
    /// </summary>
    event EventHandler<MultiBerthSelectedEventArgs>? MultiBerthSelected;

    /// <summary>The set of selected berths or the primary berth changed, including the selection being cleared.</summary>
    event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    /// <summary>The selection became empty (raised right after <see cref="SelectionChanged"/>).</summary>
    event EventHandler? SelectionCleared;

    /// <summary>
    /// The user clicked an enabled action in the actions window (or <see cref="InvokeBerthAction"/> was called).
    /// The window closes afterwards unless <see cref="BerthActionInvokedEventArgs.KeepPopupOpen"/> or <see cref="BerthAction.KeepOpen"/> is set.
    /// </summary>
    event EventHandler<BerthActionInvokedEventArgs>? BerthActionInvoked;

    /// <summary>The tooltip/actions popup opened, closed or changed content. Host views re-render <see cref="ActivePopup"/>.</summary>
    event EventHandler<BerthPopupChangedEventArgs>? PopupChanged;

    /// <summary>The berth under the pointer changed (null when the pointer left all berths). Disabled berths are never hovered.</summary>
    event EventHandler<BerthHoverEventArgs>? BerthHoverChanged;

    /// <summary>
    /// A berth's status or assigned boat changed, through any API (single updates, batches, berths). Raised once per berth,
    /// with the previous and current snapshots.
    /// </summary>
    event EventHandler<BerthStatusChangedEventArgs>? BerthStatusChanged;

    /// <summary>
    /// Piers, berths, dividers, berths or land areas were added, updated or removed, or the layout was initialized or cleared.
    /// Inside <see cref="BeginUpdate"/> or <see cref="BatchUpdate"/> the notifications are coalesced into one
    /// <see cref="LayoutChangeKind.BatchUpdated"/>.
    /// </summary>
    event EventHandler<LayoutChangedEventArgs>? LayoutChanged;

    // ---- Layout -----------------------------------------------------------------------------

    /// <summary>Name of the loaded layout (<see cref="MarinaLayout.Name"/>). Set it to rename the marina; it is stored with the layout.</summary>
    string MarinaName { get; set; }

    /// <summary>
    /// The layout designer: draw land areas, piers and berths in the view and trace a calibrated reference image
    /// (see <see cref="Design.MarinaDesigner"/>). Off until <see cref="Design.MarinaDesigner.IsActive"/> is set.
    /// </summary>
    Design.MarinaDesigner Designer { get; }

    /// <summary>
    /// Replaces the whole marina: piers, berths, dividers, multi-berths and land. Clears the selection and popup,
    /// re-centers the water, regenerates the built-in camera presets and resets the camera to the overview.
    /// </summary>
    /// <param name="layout">The complete marina; build one with <see cref="MarinaLayoutBuilder"/> or by hand.</param>
    /// <exception cref="MarinaLayoutException">The layout is invalid (see <see cref="MarinaLayout.Validate"/>); nothing is changed.</exception>
    void InitializeLayout(MarinaLayout layout);

    /// <summary>A snapshot of the current marina, suitable for saving and passing back to <see cref="InitializeLayout"/>.</summary>
    MarinaLayout GetLayout();

    /// <summary>
    /// The whole marina as a flat array of its immutable records, in dependency order: <see cref="LandArea"/>s, <see cref="Pier"/>s,
    /// <see cref="Divider"/>s, <see cref="Berth"/>s, then <see cref="MultiBerth"/>s (see <see cref="MarinaLayout.ToObjects"/>).
    /// Rebuild a layout with <see cref="MarinaLayout.FromObjects"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// foreach (var element in marina.ExportObjects())
    /// {
    ///     switch (element)
    ///     {
    ///         case LandArea land: erp.SaveLand(land.Id, land.Points, land.Height, land.Kind); break;
    ///         case Pier pier: erp.SavePier(pier.Id, pier.Start, pier.End, pier.Width, pier.Type); break;
    ///         case Berth berth: erp.SaveBerth(berth.Id, berth.PierId, berth.Center, berth.Width, berth.Length, berth.MaxDraft); break;
    ///     }
    /// }
    /// </code>
    /// </example>
    object[] ExportObjects();

    /// <summary>Removes everything (piers, berths, dividers, berths, land) and clears the selection.</summary>
    void ClearLayout();

    // ---- Piers --------------------------------------------------------------------------------

    /// <summary>
    /// Adds a pier. Use the <see cref="Pier"/> constructor (shore-end start point) or <see cref="Pier.FromCenter"/>
    /// (center point) to set position, size, orientation and <see cref="PierType"/>.
    /// </summary>
    /// <exception cref="MarinaLayoutException">The pier definition is invalid.</exception>
    /// <exception cref="InvalidOperationException">A pier with the same id exists.</exception>
    void AddPier(Pier pier);

    /// <summary>Replaces a pier definition (matched by id). Berths and dividers keep their absolute positions.</summary>
    /// <exception cref="MarinaLayoutException">The pier definition is invalid.</exception>
    /// <exception cref="KeyNotFoundException">No pier has this id.</exception>
    void UpdatePier(Pier pier);

    /// <summary>
    /// Changes a pier's position, size, orientation, type, deck height or name, keeping everything else.
    /// Resizing or rotating keeps the pier's center unless a new start point is given.
    /// </summary>
    /// <returns>The updated pier.</returns>
    /// <exception cref="KeyNotFoundException">No pier has this id.</exception>
    /// <exception cref="MarinaLayoutException">The resulting pier is invalid.</exception>
    Pier UpdatePier(PierUpdate update);

    /// <summary>Removes a pier and its dividers.</summary>
    /// <param name="pierId">Pier to remove.</param>
    /// <param name="removeBerths">Also remove the pier's berths. When false and the pier still has berths, an exception is thrown.</param>
    /// <returns>False when no pier has this id.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="removeBerths"/> is false and the pier has berths.</exception>
    bool RemovePier(string pierId, bool removeBerths = true);

    /// <summary>The pier with this id, or null.</summary>
    Pier? GetPier(string pierId);

    /// <summary>All piers, in the order they were added.</summary>
    IReadOnlyList<Pier> GetPiers();

    // ---- Dividers -----------------------------------------------------------------------------

    /// <summary>Adds a divider between berths: a finger pier, a row of piles or a floating boom.</summary>
    /// <exception cref="MarinaLayoutException">The divider is invalid or references an unknown pier.</exception>
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

    /// <summary>Dividers whose <see cref="Divider.PierId"/> is <paramref name="pierId"/>.</summary>
    IReadOnlyList<Divider> GetDividersByPier(string pierId);

    // ---- Berths --------------------------------------------------------------------------------

    /// <summary>Adds a berth. Its position, heading and size are absolute plan coordinates (see <see cref="Berth"/>).</summary>
    /// <exception cref="MarinaLayoutException">The berth is invalid or references an unknown pier or land area.</exception>
    /// <exception cref="InvalidOperationException">A berth with the same id exists.</exception>
    void AddBerth(Berth berth);

    /// <summary>Adds a Free berth at an explicit position, orientation and size.</summary>
    /// <param name="berthId">Unique id.</param>
    /// <param name="pierId">Existing pier the berth belongs to.</param>
    /// <param name="center">Center of the berth's water area in plan coordinates (X = world X, Y = world Z).</param>
    /// <param name="headingDegrees">Direction a moored boat's bow points, normally toward the pier (0° = +Z, 90° = +X).</param>
    /// <param name="length">Length along the heading, in meters.</param>
    /// <param name="width">Width across the heading, in meters.</param>
    /// <param name="label">Display name; the id is used when null.</param>
    /// <returns>The stored berth.</returns>
    Berth AddBerth(string berthId, string pierId, Vector2 center, float headingDegrees, float length, float width, string? label = null);

    /// <summary>Adds several berths with a single <see cref="LayoutChanged"/> notification.</summary>
    void AddBerths(IEnumerable<Berth> berths);

    /// <summary>
    /// Replaces a berth definition (matched by id). The berth keeps its <see cref="Berth.ExternalData"/> instance (entries from a
    /// different dictionary are merged in). A status or boat change on a member of a multi-berth changes the whole berth.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No berth has this id.</exception>
    /// <exception cref="MarinaLayoutException">The new definition is invalid.</exception>
    void UpdateBerth(Berth berth);

    /// <summary>
    /// Applies a partial update (only the members set on <paramref name="update"/>) and returns the resulting berth.
    /// A status or boat change on a member of a multi-berth changes the whole berth; setting it Free releases the berth.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No berth has this id.</exception>
    /// <exception cref="MarinaLayoutException">The resulting berth is invalid.</exception>
    Berth UpdateBerth(BerthUpdate update);

    /// <summary>
    /// Removes a berth, dropping it from the selection. If it belongs to a multi-berth the berth shrinks, or dissolves
    /// when fewer than two berths remain (the last one keeps the boat). Returns false when no berth has this id.
    /// </summary>
    bool RemoveBerth(string berthId);

    /// <summary>
    /// Gives a berth another name. The berth keeps everything else — its place in the marina, its boat, its
    /// <see cref="Berth.ExternalData"/> and its place in the selection — and its multi-berth follows it.
    /// Returns the renamed berth and raises <see cref="LayoutChangeKind.BerthRenamed"/>.
    /// </summary>
    /// <param name="berthId">The berth to rename.</param>
    /// <param name="newBerthId">Its new name; also its new id, so it must not be taken (ids are case-insensitive).</param>
    /// <exception cref="KeyNotFoundException">No berth has this id.</exception>
    /// <exception cref="InvalidOperationException">Another berth already has the new name.</exception>
    /// <remarks>
    /// The id is what a host application stores against a contract, so renaming a berth that is already in use means
    /// updating that reference too. <see cref="Berth.Label"/> is the alternative: a display name shown on the water and
    /// in tooltips, which leaves the id alone.
    /// </remarks>
    /// <example><code>marina.RenameBerth("A-L01", "A-1");</code></example>
    Berth RenameBerth(string berthId, string newBerthId);

    /// <summary>The berth with this id, or null.</summary>
    Berth? GetBerth(string berthId);

    /// <summary>
    /// Gives a pier another id. Its berths and dividers are re-pointed at the new id, and the pier keeps everything
    /// else, including its place in the order and its display name. Returns the renamed pier and raises
    /// <see cref="LayoutChangeKind.PierRenamed"/>.
    /// </summary>
    /// <param name="pierId">The pier to move.</param>
    /// <param name="newPierId">Its new id; it must not be taken (ids are case-insensitive).</param>
    /// <exception cref="KeyNotFoundException">No pier has this id.</exception>
    /// <exception cref="InvalidOperationException">Another pier already has the new id.</exception>
    /// <remarks>
    /// Berth ids are not rebuilt from the new pier id: a berth called <c>A-L01</c> stays <c>A-L01</c>, because that
    /// name may already be printed on a finger pier and stored against a contract. Rename the berths as well if the
    /// old pier letter should disappear.
    /// </remarks>
    Pier ChangePierId(string pierId, string newPierId);

    /// <summary>All berths, in the order they were added.</summary>
    IReadOnlyList<Berth> GetBerths();

    /// <summary>Berths belonging to a pier.</summary>
    IReadOnlyList<Berth> GetBerthsByPier(string pierId);

    /// <summary>Land berths on a land area (see <see cref="Berth.OnLand"/>).</summary>
    IReadOnlyList<Berth> GetBerthsByLandArea(string landAreaId);

    /// <summary>Adds a land area (quay, lawn or breakwater) and builds its mesh.</summary>
    /// <exception cref="MarinaLayoutException">The land area is invalid (see <see cref="LandArea"/>).</exception>
    /// <exception cref="InvalidOperationException">A land area with the same id exists.</exception>
    void AddLandArea(LandArea landArea);

    /// <summary>Replaces a land area definition (matched by id) and rebuilds its mesh. Its land berths keep their positions.</summary>
    /// <exception cref="KeyNotFoundException">No land area has this id.</exception>
    /// <exception cref="MarinaLayoutException">The new definition is invalid.</exception>
    void UpdateLandArea(LandArea landArea);

    /// <summary>Removes a land area. Returns false when no land area has this id.</summary>
    /// <param name="landAreaId">Land area to remove.</param>
    /// <param name="removeBerths">Also remove its land berths. When false and it still has land berths, an exception is thrown.</param>
    /// <exception cref="InvalidOperationException"><paramref name="removeBerths"/> is false and the land area has land berths.</exception>
    bool RemoveLandArea(string landAreaId, bool removeBerths = true);

    /// <summary>The land area with this id, or null.</summary>
    LandArea? GetLandArea(string landAreaId);

    /// <summary>All land areas, in layout order.</summary>
    IReadOnlyList<LandArea> GetLandAreas();

    /// <summary>
    /// The mainland behind the marina, or null when the marina stands in open water. Set it with
    /// <see cref="SetShoreline"/>.
    /// </summary>
    Shoreline? Shoreline { get; }

    /// <summary>
    /// Sets (or replaces) the mainland behind the marina and builds its mesh. It is drawn beneath the land areas, so
    /// a quay traced along the shore sits on top of it and the two read as one piece of ground.
    /// </summary>
    /// <param name="shoreline">The shoreline, or null to go back to open water.</param>
    /// <exception cref="MarinaLayoutException">The shoreline is invalid (see <see cref="Domain.Shoreline.Validate"/>).</exception>
    /// <example>
    /// <code>
    /// // A straight coast running east-west a hundred meters north of the marina.
    /// marina.SetShoreline(new Shoreline(new[] { new Vector2(-800, -100), new Vector2(800, -100) }, landOnLeft: false));
    /// </code>
    /// </example>
    void SetShoreline(Shoreline? shoreline);

    /// <summary>Takes the mainland away, leaving open water. Returns false when there was none.</summary>
    bool RemoveShoreline();

    /// <summary>
    /// The passing traffic out at sea. <see cref="Domain.MarineTraffic.None"/> until it is switched on with
    /// <see cref="SetMarineTraffic"/>.
    /// </summary>
    MarineTraffic MarineTraffic { get; }

    /// <summary>
    /// Sets the passing traffic and lays out the lanes it runs along. Lanes are kept clear of the marina, the land
    /// areas and the mainland by <see cref="Domain.MarineTraffic.Clearance"/>, so nothing sails over a quay.
    /// </summary>
    /// <param name="traffic">The traffic settings, or null for empty sea.</param>
    /// <exception cref="MarinaLayoutException">The settings are unsound (see <see cref="Domain.MarineTraffic.Validate"/>).</exception>
    /// <example>
    /// <code>
    /// marina.SetMarineTraffic(MarineTraffic.None with { IsEnabled = true, Clearance = 400f, LaneCount = 3 });
    /// </code>
    /// </example>
    void SetMarineTraffic(MarineTraffic? traffic);

    /// <summary>
    /// Where every passing vessel is right now, for a host that wants to draw its own marker or label. The list is a
    /// snapshot: the vessels have moved on by the next frame.
    /// </summary>
    IReadOnlyList<TrafficVessel> GetTrafficVessels();

    /// <summary>
    /// The lanes the traffic runs along, nearest the marina first, or empty when there are none — the traffic is off,
    /// or its settings are unsound. Useful for showing where the shipping will pass while the settings are adjusted.
    /// </summary>
    IReadOnlyList<TrafficLane> TrafficLanes { get; }

    /// <summary>
    /// Draws the traffic's lanes on the water, so it can be seen where the shipping will pass while the clearance and
    /// the spacing are being set. Default false. It is a working aid and is not saved with the design.
    /// </summary>
    bool ShowTrafficLanes { get; set; }

    /// <summary>Berths with a given status.</summary>
    IReadOnlyList<Berth> GetBerthsByStatus(BerthStatus status);

    /// <summary>
    /// Applies many partial updates with a single scene rebuild and one <see cref="LayoutChanged"/>. Failing updates
    /// (unknown berth, invalid values) are collected in the result instead of throwing; the others are applied.
    /// </summary>
    BatchUpdateResult BatchUpdate(IEnumerable<BerthUpdate> updates);

    /// <summary>
    /// Starts a batch: <see cref="LayoutChanged"/> notifications are coalesced and popup refreshes deferred until the
    /// returned scope is disposed. Scopes can be nested.
    /// </summary>
    /// <example><code>using (marina.BeginUpdate()) { foreach (var u in updates) marina.UpdateBerth(u); }</code></example>
    IDisposable BeginUpdate();

    // ---- Status and flags ---------------------------------------------------------------------

    /// <summary>
    /// Sets a berth's status. Free always removes the boat; for other statuses a null <paramref name="boat"/> keeps the current boat.
    /// </summary>
    /// <returns>The updated berth.</returns>
    /// <exception cref="KeyNotFoundException">No berth has this id.</exception>
    Berth SetBerthStatus(string berthId, BerthStatus status, Boat? boat = null);

    /// <summary>Marks the berth Occupied (red) by <paramref name="boat"/>.</summary>
    Berth AssignBoat(string berthId, Boat boat);

    /// <summary>Marks the berth Reserved (blue), optionally for a known incoming boat (drawn as a translucent ghost).</summary>
    Berth ReserveBerth(string berthId, Boat? expectedBoat = null);

    /// <summary>Marks the berth Free (green) and removes its boat. On a berth member this releases the whole berth.</summary>
    Berth ReleaseBerth(string berthId);

    /// <summary>
    /// Marks the berth Temporarily Free (yellow): the berth holder's boat is away. The boat stays assigned and is drawn as a
    /// ghost. Pass <paramref name="boat"/> to set or replace it.
    /// </summary>
    Berth MarkTemporarilyFree(string berthId, Boat? boat = null);

    /// <summary>Shows or hides a berth. Hidden berths draw nothing (not even finger piers) and cannot be interacted with.</summary>
    Berth SetBerthVisible(string berthId, bool visible);

    /// <summary>
    /// Disables or enables a berth. Disabled berths are drawn in gray (boat desaturated) and cannot be hovered, selected,
    /// right-clicked or acted on. Disabling a selected berth removes it from the selection.
    /// </summary>
    Berth SetBerthDisabled(string berthId, bool disabled);

    /// <summary>Makes a berth read-only: it can be selected and shows its tooltip, but its actions window never opens.</summary>
    Berth SetBerthReadOnly(string berthId, bool readOnly);

    /// <summary>Sets interaction flags on many berths with a single scene rebuild. Null leaves a flag unchanged.</summary>
    BatchUpdateResult SetBerthFlags(IEnumerable<string> berthIds, bool? visible = null, bool? disabled = null, bool? readOnly = null);

    /// <summary>Berth counts per status, for dashboards.</summary>
    MarinaStatistics GetStatistics();

    // ---- Multi-berths --------------------------------------------------------------------

    /// <summary>
    /// Moors one boat alongside (parallel to the pier) across two or more berths, e.g. a superyacht taking several small berths.
    /// Shortcut for <see cref="AssignBoatToBerths"/> with <see cref="MooringStyle.Alongside"/>.
    /// </summary>
    /// <param name="berthIds">Member berths (any number, at least two). The first berth's orientation places the boat.</param>
    /// <param name="boat">The boat.</param>
    /// <param name="status">Occupied, Reserved or TemporarilyFree.</param>
    /// <param name="multiBerthId">Id for the berth; generated from the first berth id when null.</param>
    /// <exception cref="InvalidOperationException">A berth already belongs to another berth, or the berth id is taken.</exception>
    /// <exception cref="MarinaLayoutException">Fewer than two berths, unknown berths, or an invalid boat or status.</exception>
    MultiBerth MoorAlongside(IEnumerable<string> berthIds, Boat boat, BerthStatus status = BerthStatus.Occupied, string? multiBerthId = null);

    /// <summary>
    /// Puts a single boat in several berths at once. Every member berth takes <paramref name="status"/> and <paramref name="boat"/>
    /// and gets <see cref="Berth.MultiBerthId"/>; the boat is drawn once across them and finger piers between them are hidden.
    /// </summary>
    /// <param name="berthIds">Member berths (at least two).</param>
    /// <param name="boat">The boat.</param>
    /// <param name="status">Occupied, Reserved or TemporarilyFree.</param>
    /// <param name="style">Alongside (parallel to the pier) or bow-in (centered across the berths).</param>
    /// <param name="multiBerthId">Id for the berth; generated when null.</param>
    /// <exception cref="InvalidOperationException">A berth already belongs to another berth, or the berth id is taken.</exception>
    /// <exception cref="MarinaLayoutException">Fewer than two berths, unknown berths, or an invalid boat or status.</exception>
    MultiBerth AssignBoatToBerths(IEnumerable<string> berthIds, Boat boat, BerthStatus status = BerthStatus.Occupied, MooringStyle style = MooringStyle.Alongside, string? multiBerthId = null);

    /// <summary>Replaces a berth's boat, status, style and/or member berths. Berths no longer listed become Free.</summary>
    /// <exception cref="KeyNotFoundException">No berth has this id.</exception>
    MultiBerth UpdateMultiBerth(MultiBerth berth);

    /// <summary>Partial update of a berth; null arguments leave values unchanged. Berths no longer listed become Free.</summary>
    /// <exception cref="KeyNotFoundException">No berth has this id.</exception>
    MultiBerth UpdateMultiBerth(string multiBerthId, Boat? boat = null, BerthStatus? status = null, MooringStyle? style = null, IEnumerable<string>? berthIds = null);

    /// <summary>Removes the berth and sets all its berths Free. Returns false when no berth has this id.</summary>
    bool ReleaseMultiBerth(string multiBerthId);

    /// <summary>The berth with this id, or null.</summary>
    MultiBerth? GetMultiBerth(string multiBerthId);

    /// <summary>All multi-berths.</summary>
    IReadOnlyList<MultiBerth> GetMultiBerths();

    /// <summary>The berth a berth belongs to, or null.</summary>
    MultiBerth? GetMultiBerthFor(string berthId);

    // ---- Selection ----------------------------------------------------------------------------

    /// <summary>The primary (most recently clicked or last listed) selected berth, or null.</summary>
    Berth? SelectedBerth { get; }

    /// <summary>All selected berths in selection order; the last one is <see cref="SelectedBerth"/>.</summary>
    IReadOnlyList<Berth> SelectedBerths { get; }

    /// <summary>The berth under the pointer, or null.</summary>
    Berth? HoveredBerth { get; }

    /// <summary>
    /// Makes one berth the only selected berth (raising <see cref="BerthSelected"/>), optionally focusing the camera on it.
    /// Returns false, changing nothing, when the berth doesn't exist or is hidden, disabled or filtered out.
    /// </summary>
    bool SelectBerth(string berthId, bool focusCamera = false);

    /// <summary>Replaces the selection with the selectable berths among <paramref name="berthIds"/>. Returns how many were selected.</summary>
    int SelectBerths(IEnumerable<string> berthIds);

    /// <summary>
    /// Replaces the selection with one or more berths. Disabled berths are discarded, as are hidden, filtered-out and unknown
    /// ids and duplicates; <see cref="SelectionResult.Rejected"/> lists them with the reason. The rest are selected in the
    /// given order (the last becomes primary) and <see cref="BerthSelected"/> or <see cref="MultiBerthSelected"/> is raised.
    /// When nothing remains the selection is cleared.
    /// </summary>
    /// <param name="berthIds">Berths to select.</param>
    /// <param name="focusCamera">Also frame all selected berths (see <see cref="FocusBerths"/>).</param>
    /// <param name="focusAngle">Angle for the focus; null uses <see cref="DefaultFocusAngle"/>.</param>
    /// <example><code>
    /// var result = marina.SetSelection(ids, focusCamera: true, CameraAngle.TopDown);
    /// foreach (var r in result.Rejected) log($"{r.BerthId}: {r.Reason}");
    /// </code></example>
    SelectionResult SetSelection(IEnumerable<string> berthIds, bool focusCamera = false, CameraAngle? focusAngle = null);

    /// <summary>Replaces the selection with the given berths (disabled ones are discarded), without moving the camera.</summary>
    SelectionResult SetSelection(params string[] berthIds);

    /// <summary>Adds a berth to the selection, making it primary. Returns false when it can't be selected.</summary>
    bool AddToSelection(string berthId);

    /// <summary>Removes a berth from the selection. Returns false when it wasn't selected.</summary>
    bool RemoveFromSelection(string berthId);

    /// <summary>True when the berth is part of the selection.</summary>
    bool IsBerthSelected(string berthId);

    /// <summary>Clears the selection and closes the popup.</summary>
    void ClearSelection();

    // ---- Tooltip and actions popup ------------------------------------------------------------

    /// <summary>The tooltip or actions window currently shown above the selection, or null.</summary>
    BerthPopup? ActivePopup { get; }

    /// <summary>Shows the tooltip for the current selection, raising the selection event again to collect content. False when nothing is shown.</summary>
    bool ShowTooltip();

    /// <summary>
    /// Opens the actions window for the current selection (raising the selection event with a right-click button).
    /// Falls back to the tooltip when every selected berth is read-only or there are no visible actions.
    /// </summary>
    bool ShowActions();

    /// <summary>Rebuilds the open popup's content by raising the selection event with <see cref="SelectionReason.Refresh"/>.</summary>
    void RefreshPopup();

    /// <summary>Closes the tooltip or actions window (the selection is kept).</summary>
    void ClosePopup();

    /// <summary>
    /// Invokes an action of the open actions window: raises <see cref="BerthActionInvoked"/> and closes the window unless kept open.
    /// Returns false when no actions window is open or the action doesn't exist, is disabled or hidden.
    /// </summary>
    bool InvokeBerthAction(string actionId);

    /// <summary>Show a tooltip on left-click (default true).</summary>
    bool TooltipsEnabled { get; set; }

    /// <summary>Open the actions window on right-click (default true).</summary>
    bool ActionsEnabled { get; set; }

    /// <summary>Allow Ctrl+click or Shift+click multi-selection (default true).</summary>
    bool MultiSelectEnabled { get; set; }

    // ---- Filtering and appearance -------------------------------------------------------------

    /// <summary>Which berths have their name written on the water: None (default), OnlyFree, NonOccupied or All.</summary>
    BerthLabelMode BerthLabelMode { get; set; }

    /// <summary>Statuses currently shown. Filtered-out berths keep their structure but lose status visuals, boats, labels and interaction.</summary>
    BerthStatusFilter StatusFilter { get; }

    /// <summary>Shows only berths whose status is in <paramref name="filter"/>; filtered-out berths leave the selection.</summary>
    void SetStatusFilter(BerthStatusFilter filter);

    /// <summary>Shows every status (<see cref="BerthStatusFilter.All"/>).</summary>
    void ShowAllStatuses();

    /// <summary>True when the berth exists, is not hidden and passes the status filter.</summary>
    bool IsBerthVisible(string berthId);

    /// <summary>Changes the pad, buoy and ghost-boat color of a status.</summary>
    void SetStatusColor(BerthStatus status, ColorRgba color);

    /// <summary>The current color of a status.</summary>
    ColorRgba GetStatusColor(BerthStatus status);

    /// <summary>Restores the default status colors and overlay opacities.</summary>
    void ResetStatusColors();

    // ---- Camera -------------------------------------------------------------------------------

    /// <summary>The orbit camera. Use it for low-level control (poses, constraints, field of view).</summary>
    OrbitCamera Camera { get; }

    /// <summary>
    /// Built-in presets (Overview, Top Down, North, East, South, West, and one per pier) followed by custom ones.
    /// Every built-in view is centred on the marina and pulled back far enough to hold all of it.
    /// </summary>
    /// <remarks>
    /// The list includes views that have been switched off; <see cref="CameraPreset.IsEnabled"/> says which. Offer the
    /// enabled ones and leave the rest out.
    /// </remarks>
    IReadOnlyList<CameraPreset> CameraPresets { get; }

    /// <summary>
    /// Switches a view on or off. A switched-off view stays in <see cref="CameraPresets"/> marked
    /// <see cref="CameraPreset.IsEnabled"/> false, and can still be applied by name; it is simply not one a host
    /// should offer. Which built-in views are off is saved with the design.
    /// </summary>
    /// <param name="presetName">Name of an automatic view (case-insensitive).</param>
    /// <param name="enabled">True to offer it again.</param>
    /// <returns>False when no automatic view has this name.</returns>
    bool SetCameraPresetEnabled(string presetName, bool enabled);

    /// <summary>Moves the camera to the Overview preset.</summary>
    /// <param name="immediate">Jump instead of animating.</param>
    void ResetCamera(bool immediate = false);

    /// <summary>Moves the camera to a preset by name (case-insensitive). Returns false when no preset has this name.</summary>
    /// <remarks>
    /// A saved view and an automatic one may share a name. This picks the saved one, since someone chose it
    /// deliberately; use <see cref="ApplyCameraPreset(CameraPreset, bool)"/> to say exactly which.
    /// </remarks>
    bool ApplyCameraPreset(string presetName, bool immediate = false);

    /// <summary>
    /// Moves the camera to a preset taken from <see cref="CameraPresets"/>, which says exactly which one even when a
    /// saved view and an automatic one share a name.
    /// </summary>
    /// <param name="preset">The preset to go to.</param>
    /// <param name="immediate">Jump instead of animating.</param>
    void ApplyCameraPreset(CameraPreset preset, bool immediate = false);

    /// <summary>
    /// Adds a custom preset, replacing a custom preset of the same name. An automatic view of that name is left
    /// alone, so the two live side by side. Custom presets survive layout changes and are saved with the design.
    /// </summary>
    void AddCameraPreset(CameraPreset preset);

    /// <summary>
    /// Saves where the camera is now as a custom preset, so a host can offer "go back to this view" later.
    /// Replaces a custom preset of the same name.
    /// </summary>
    /// <param name="name">Name to save it under.</param>
    /// <param name="description">Optional line describing the view.</param>
    /// <returns>The preset that was stored.</returns>
    CameraPreset SaveCameraPreset(string name, string? description = null);

    /// <summary>
    /// Angle used by focus calls that don't pass one, by <c>SelectBerth(id, focusCamera: true)</c> and by double-click.
    /// Null (default) keeps the current yaw and looks down at least 35°. Set <see cref="CameraAngle.TopDown"/> for a plan view.
    /// </summary>
    CameraAngle? DefaultFocusAngle { get; set; }

    /// <summary>Centers the camera on a berth at <see cref="DefaultFocusAngle"/>, zoomed to show it whole. False when the berth doesn't exist.</summary>
    bool FocusBerth(string berthId, bool immediate = false);

    /// <summary>Centers the camera on a berth from a specific angle, zoomed to show it whole. False when the berth doesn't exist.</summary>
    bool FocusBerth(string berthId, CameraAngle angle, bool immediate = false);

    /// <summary>
    /// Moves the camera so every listed berth (and its boat) is in view: the target is the middle of the berths and the distance
    /// is the closest that fits them all, with a margin. Unknown ids are ignored; disabled and hidden berths are included.
    /// </summary>
    /// <param name="berthIds">Berths to frame.</param>
    /// <param name="angle">Viewing angle, e.g. <see cref="CameraAngle.TopDown"/> or <c>new CameraAngle(200, 45)</c>. Null uses <see cref="DefaultFocusAngle"/>.</param>
    /// <param name="immediate">Jump instead of animating.</param>
    /// <returns>False when none of the ids exist.</returns>
    bool FocusBerths(IEnumerable<string> berthIds, CameraAngle? angle = null, bool immediate = false);

    /// <summary>Frames the selected berths (see <see cref="FocusBerths"/>). False when nothing is selected.</summary>
    bool FocusSelection(CameraAngle? angle = null, bool immediate = false);

    /// <summary>
    /// Moves the camera so the whole pier is in view, with every berth along it, at
    /// <paramref name="angle"/> (or <see cref="DefaultFocusAngle"/> when null — pass
    /// <see cref="CameraAngle.TopDown"/> for a plan view of the pier).
    /// </summary>
    /// <param name="pierId">The pier to frame.</param>
    /// <param name="angle">Viewing angle; null uses <see cref="DefaultFocusAngle"/>.</param>
    /// <param name="immediate">Jump instead of animating.</param>
    /// <returns>False when no pier has this id.</returns>
    /// <remarks>
    /// This fits the pier and its berths in the view. The parameterless <see cref="FocusPier(string, bool)"/> is
    /// the tighter close-up from the pier's shore end instead.
    /// </remarks>
    bool FocusPier(string pierId, CameraAngle? angle, bool immediate = false);

    /// <summary>Moves the camera to the pier's close-up view. False when the pier doesn't exist.</summary>
    bool FocusPier(string pierId, bool immediate = false);

    /// <summary>Sun, ambient light, specular and fog. Changes apply on the next frame.</summary>
    LightingSettings Lighting { get; }

    /// <summary>Water colors and wave animation. Changes apply on the next frame. The same object as <c>Style.Water</c>.</summary>
    WaterSettings Water { get; }

    /// <summary>
    /// Everything about how the marina is drawn and animated: lighting, water and waves, status colors and boat opacities, land and trees,
    /// piers, labels, selection and camera (see <see cref="MarinaStyle"/>). Change its properties or assign a new instance.
    /// </summary>
    MarinaStyle Style { get; set; }

    // ---- View helpers -------------------------------------------------------------------------

    /// <summary>
    /// Hit-tests a point in view pixels (origin top-left) against visible, unfiltered berths and boats.
    /// Returns the nearest hit, or null. Disabled berths are hit (they block what's behind them) but input ignores them.
    /// </summary>
    BerthHit? HitTest(float x, float y);

    /// <summary>
    /// Where the popup should point, in view pixels: just above the primary selected berth. Call every frame (the camera moves).
    /// False when no popup is open or the point is behind the camera.
    /// </summary>
    bool TryGetPopupAnchor(out Vector2 screenPoint);
}
