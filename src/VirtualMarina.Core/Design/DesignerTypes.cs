using System.Numerics;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Design;

/// <summary>What clicks in the 3D view do while <see cref="MarinaDesigner.IsActive"/> is true.</summary>
public enum DesignTool
{
    /// <summary>Clicks do nothing; dragging, the wheel and the keyboard move the camera as usual.</summary>
    Navigate = 0,

    /// <summary>
    /// Click to add outline points of a new <see cref="LandArea"/> (kind and height from the designer settings). Finish with a double-click,
    /// Enter, a right-click or a click on the first point; Backspace removes the last point; Escape cancels.
    /// </summary>
    DrawLandArea = 1,

    /// <summary>Click the shore end, then the far end of a new <see cref="Pier"/> (type, width and berthing sides from the settings). Shift snaps the angle to 15°.</summary>
    DrawPier = 2,

    /// <summary>
    /// Click beside a pier where the first berth should go, then click where the row should end: berths of the configured width,
    /// length and depth fill the space on that side. Clicking twice in the same place adds one berth.
    /// </summary>
    AddBerths = 3,

    /// <summary>Click a berth, pier or land area to remove it (a pier or land area is removed with its berths).</summary>
    Erase = 4,

    /// <summary>Drag with the left button to move the reference image.</summary>
    MoveReferenceImage = 5,

    /// <summary>
    /// Click both ends of the image's scale bar. <see cref="MarinaDesigner.ScaleLineDrawn"/> is raised; then call
    /// <see cref="MarinaDesigner.CalibrateReferenceImage(float)"/> with the length the bar represents.
    /// </summary>
    MeasureScale = 6,

    /// <summary>
    /// Click a lawn (<see cref="LandKind.Grass"/>) to scatter trees on it at <see cref="MarinaDesigner.TreeDensity"/>. Each click
    /// replaces the trees it has with new, randomly placed ones; Ctrl+click (or a right-click) removes them.
    /// </summary>
    PlantTrees = 7,

    /// <summary>
    /// Click a land area to put a land berth (<see cref="Berth.OnLand"/>) there, then click again to aim its bow (Shift snaps to 15°).
    /// Clicking the same spot twice uses <see cref="MarinaDesigner.LandBerthHeading"/>.
    /// </summary>
    AddLandBerths = 8,

    /// <summary>
    /// Click a berth or a pier to give it another name. The designer asks the host for the new name through
    /// <see cref="MarinaDesigner.ElementRenaming"/>, so the application decides how to ask for it.
    /// </summary>
    Rename = 9,

    /// <summary>
    /// Click a berth to give it the pedestals in <see cref="MarinaDesigner.BerthServices"/>; hold Alt or Ctrl to
    /// change every berth down that side of the pier at once. The berths about to change are highlighted.
    /// </summary>
    EditServices = 10,

    /// <summary>
    /// Drag a box over the water to select every berth whose middle falls inside it. Hold Shift or Ctrl to add to
    /// the selection already made instead of replacing it.
    /// </summary>
    SelectArea = 11,

    /// <summary>
    /// Draw the coast of the mainland behind the marina (<see cref="Domain.Shoreline"/>). Click to place points along
    /// it — two are enough for a straight coast — then press Enter, and click the side of the line that is land.
    /// Backspace removes the last point; Escape cancels. Drawing a new one replaces the one already there.
    /// </summary>
    DrawShoreline = 12,

    /// <summary>
    /// Click beside a row of berths to put a <see cref="Divider"/> of <see cref="MarinaDesigner.DividerType"/> on the boundary
    /// nearest the pointer, or to take away the one already there. Hold Alt to fill the whole row instead: a divider on every
    /// <see cref="MarinaDesigner.DividerInterval"/>-th boundary, counting from the one clicked. Ctrl+click (or a right-click)
    /// only removes: the one divider, or with Alt every divider along the row. Berths with a divider between them are no
    /// longer connected (<see cref="Berth.ConnectedBerthIds"/>).
    /// </summary>
    PlaceDividers = 13,
}

/// <summary>
/// A berth or pier is about to be renamed (<see cref="MarinaDesigner.ElementRenaming"/>). Put the new name in
/// <see cref="NewName"/> — or leave it as it is, or set <see cref="Cancel"/>, to leave the element alone.
/// </summary>
public sealed class DesignElementRenamingEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="berth">The berth being renamed, or null for a pier.</param>
    /// <param name="pier">The pier being renamed, or null for a berth.</param>
    /// <param name="currentName">The name it has now.</param>
    public DesignElementRenamingEventArgs(Berth? berth, Pier? pier, string currentName)
        : this(berth, pier, currentName, null)
    {
    }

    /// <summary>Creates the arguments, saying how the element's berths are named.</summary>
    /// <param name="berth">The berth being renamed, or null for a pier.</param>
    /// <param name="pier">The pier being renamed, or null for a berth.</param>
    /// <param name="currentName">The name it has now.</param>
    /// <param name="berthPattern">The pattern the pier's berths follow now, or null when there is none to show.</param>
    public DesignElementRenamingEventArgs(Berth? berth, Pier? pier, string currentName, string? berthPattern)
        : this(berth, pier, currentName, berthPattern, DesignRenameScope.Element)
    {
    }

    /// <summary>Creates the arguments for a rename of a given scope.</summary>
    /// <param name="berth">The berth being renamed, or the one that was clicked for a whole-pier rename.</param>
    /// <param name="pier">The pier being renamed, or the one the clicked berth is on.</param>
    /// <param name="currentName">The name it has now.</param>
    /// <param name="berthPattern">The pattern the pier's berths follow now, or null when there is none to show.</param>
    /// <param name="scope">What the rename is about to change.</param>
    public DesignElementRenamingEventArgs(Berth? berth, Pier? pier, string currentName, string? berthPattern, DesignRenameScope scope)
    {
        Berth = berth;
        Pier = pier;
        CurrentName = currentName;
        NewName = currentName;
        NewPierId = pier?.Id;
        BerthPattern = berthPattern;
        NewBerthPattern = berthPattern;
        Scope = scope;
    }

    /// <summary>The berth being renamed, or null when a pier is.</summary>
    public Berth? Berth { get; }

    /// <summary>The pier being renamed, or null when a berth is.</summary>
    public Pier? Pier { get; }

    /// <summary>The name the element has now: a berth's id, or a pier's display name.</summary>
    public string CurrentName { get; }

    /// <summary>The name to give it. Starts as <see cref="CurrentName"/>; leaving it unchanged does nothing.</summary>
    public string NewName { get; set; }

    /// <summary>
    /// For a pier, the id to give it, which its berths and dividers follow. Starts as the pier's current id;
    /// leaving it unchanged moves nothing. Ignored for a berth, whose name is its id.
    /// </summary>
    public string? NewPierId { get; set; }

    /// <summary>
    /// For a pier, the pattern its berths are named by now, read back out of their names — <c>{pier}-{side}{number}</c>
    /// for berths called <c>A-L01</c>. Null for a berth, and for a pier whose berths were all named by hand.
    /// </summary>
    /// <seealso cref="BerthNamingScheme"/>
    public string? BerthPattern { get; }

    /// <summary>
    /// The pattern to name the pier's berths by. Starts as <see cref="BerthPattern"/>; changing it renames every
    /// numbered berth on the pier to match, keeping the number each one already has. Ignored for a berth.
    /// </summary>
    /// <remarks>
    /// A berth whose new name is already taken is left alone rather than overwritten, so a pattern that would give
    /// two berths the same name renames neither of them.
    /// </remarks>
    public string? NewBerthPattern { get; set; }

    /// <summary>
    /// What this rename is about to change. <see cref="DesignRenameScope.BerthsOfPier"/> means only
    /// <see cref="NewBerthPattern"/> is read: the pier's own name and id are left alone.
    /// </summary>
    public DesignRenameScope Scope { get; }

    /// <summary>Set to true to leave the element alone.</summary>
    public bool Cancel { get; set; }
}

/// <summary>What a rename is about to change (<see cref="DesignElementRenamingEventArgs.Scope"/>).</summary>
public enum DesignRenameScope
{
    /// <summary>The one element that was clicked: a berth's name, or a pier's name and id.</summary>
    Element = 0,

    /// <summary>
    /// Every berth on the pier that was clicked, renamed to a pattern rather than one at a time. Raised when a
    /// berth is clicked with Alt held, the same modifier that sweeps a whole row with the eraser.
    /// </summary>
    BerthsOfPier = 1,
}

/// <summary>What happened to the drawing in progress (<see cref="MarinaDesigner.DraftChanged"/>).</summary>
public enum DesignDraftChange
{
    /// <summary>A point was added (a land outline point, a pier's start, the first end of a berth row or scale line).</summary>
    PointAdded = 0,

    /// <summary>The last point was removed (Backspace).</summary>
    PointRemoved = 1,

    /// <summary>The drawing was abandoned (Escape, tool change, designer turned off).</summary>
    Canceled = 2,

    /// <summary>The drawing was finished and turned into an element (or a scale line).</summary>
    Completed = 3,
}

/// <summary>What changed about the reference image (<see cref="MarinaDesigner.ReferenceImageChanged"/>).</summary>
public enum ReferenceImageChange
{
    /// <summary>A new image was set.</summary>
    Set = 0,

    /// <summary>The image was removed.</summary>
    Cleared = 1,

    /// <summary>The image was moved (<see cref="MarinaDesigner.ReferenceImageCenter"/>).</summary>
    Moved = 2,

    /// <summary>The image scale changed (<see cref="MarinaDesigner.ReferenceImageMetersPerPixel"/>), e.g. by calibration.</summary>
    Scaled = 3,

    /// <summary>Opacity, visibility or layering changed.</summary>
    AppearanceChanged = 4,
}

/// <summary>Data for <see cref="MarinaDesigner.ToolChanged"/>.</summary>
public sealed class DesignToolChangedEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    public DesignToolChangedEventArgs(DesignTool previous, DesignTool current)
    {
        Previous = previous;
        Current = current;
    }

    /// <summary>The tool before the change.</summary>
    public DesignTool Previous { get; }

    /// <summary>The tool now active.</summary>
    public DesignTool Current { get; }
}

/// <summary>Data for <see cref="MarinaDesigner.DraftChanged"/>: the user is drawing.</summary>
public sealed class DesignDraftChangedEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    public DesignDraftChangedEventArgs(DesignTool tool, DesignDraftChange change, IReadOnlyList<Vector2> points)
    {
        Tool = tool;
        Change = change;
        Points = points;
    }

    /// <summary>The tool being used.</summary>
    public DesignTool Tool { get; }

    /// <summary>What happened.</summary>
    public DesignDraftChange Change { get; }

    /// <summary>The points placed so far, in plan coordinates (a snapshot; empty after <see cref="DesignDraftChange.Canceled"/>).</summary>
    public IReadOnlyList<Vector2> Points { get; }
}

/// <summary>
/// Data for <see cref="MarinaDesigner.ElementCreating"/>: the user finished drawing and the element is about to be added. Handlers can
/// change it (e.g. assign ERP ids or names) or cancel.
/// </summary>
/// <example>
/// <code>
/// marina.Designer.ElementCreating += (s, e) =>
/// {
///     if (e.Pier is { } pier) e.Pier = pier with { Id = erp.NextPierCode(), Name = "Pier " + erp.NextPierCode() };
///     e.Berths = e.Berths.Select(berth => berth with { Metadata = new Dictionary&lt;string, string&gt; { ["Source"] = "Designer" } }).ToArray();
/// };
/// </code>
/// </example>
public sealed class DesignElementCreatingEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    public DesignElementCreatingEventArgs(DesignTool tool, LandArea? landArea, Pier? pier, IReadOnlyList<Berth> berths, IReadOnlyList<Divider> dividers)
    {
        Tool = tool;
        LandArea = landArea;
        Pier = pier;
        Berths = berths;
        Dividers = dividers;
    }

    /// <summary>The tool that produced the element.</summary>
    public DesignTool Tool { get; }

    /// <summary>The land area to add (<see cref="DesignTool.DrawLandArea"/>). Replace it to change the id, name or outline.</summary>
    public LandArea? LandArea { get; set; }

    /// <summary>The pier to add (<see cref="DesignTool.DrawPier"/>). If you change its id, berths created later reference the new id.</summary>
    public Pier? Pier { get; set; }

    /// <summary>The berths to add (<see cref="DesignTool.AddBerths"/>, or the one land berth of <see cref="DesignTool.AddLandBerths"/>); empty for other tools.</summary>
    public IReadOnlyList<Berth> Berths { get; set; }

    /// <summary>
    /// The dividers to add (<see cref="DesignTool.PlaceDividers"/>); empty for other tools, since <see cref="DesignTool.AddBerths"/>
    /// places no dividers of its own.
    /// </summary>
    public IReadOnlyList<Divider> Dividers { get; set; }

    /// <summary>
    /// The mainland to set (<see cref="DesignTool.DrawShoreline"/>); null for other tools. Replace it to change its
    /// height, surface or scenery before it is drawn.
    /// </summary>
    public Shoreline? Shoreline { get; set; }

    /// <summary>Set to true to discard the drawing.</summary>
    public bool Cancel { get; set; }
}

/// <summary>Data for <see cref="MarinaDesigner.ElementCreated"/>: the element was added to the marina (a <c>LayoutChanged</c> was raised too).</summary>
public sealed class DesignElementCreatedEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    public DesignElementCreatedEventArgs(DesignTool tool, LandArea? landArea, Pier? pier, IReadOnlyList<Berth> berths, IReadOnlyList<Divider> dividers)
    {
        Tool = tool;
        LandArea = landArea;
        Pier = pier;
        Berths = berths;
        Dividers = dividers;
    }

    /// <summary>The tool that produced the element.</summary>
    public DesignTool Tool { get; }

    /// <summary>The land area added, if any.</summary>
    public LandArea? LandArea { get; }

    /// <summary>The pier added, if any.</summary>
    public Pier? Pier { get; }

    /// <summary>The berths added (empty unless berths were added).</summary>
    public IReadOnlyList<Berth> Berths { get; }

    /// <summary>The dividers added (<see cref="DesignTool.PlaceDividers"/>).</summary>
    public IReadOnlyList<Divider> Dividers { get; }

    /// <summary>The mainland that was set (<see cref="DesignTool.DrawShoreline"/>), if any.</summary>
    public Shoreline? Shoreline { get; init; }
}

/// <summary>Data for <see cref="MarinaDesigner.TreesPlanted"/> (raised when trees are scattered or removed).</summary>
public sealed class DesignTreesPlantedEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    public DesignTreesPlantedEventArgs(LandArea landArea, int previousCount)
    {
        LandArea = landArea;
        PreviousCount = previousCount;
    }

    /// <summary>The land area with its new <see cref="LandArea.Trees"/> (empty after a removal).</summary>
    public LandArea LandArea { get; }

    /// <summary>How many trees it had before.</summary>
    public int PreviousCount { get; }
}

/// <summary>Data for <see cref="MarinaDesigner.ActionUndone"/>: the last designer change was reverted.</summary>
public sealed class DesignActionUndoneEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    public DesignActionUndoneEventArgs(string description, int remainingSteps)
    {
        Description = description;
        RemainingSteps = remainingSteps;
    }

    /// <summary>What was undone, e.g. "Add 4 berths".</summary>
    public string Description { get; }

    /// <summary>How many steps can still be undone.</summary>
    public int RemainingSteps { get; }
}

/// <summary>Data for <see cref="MarinaDesigner.ElementErased"/>.</summary>
public sealed class DesignElementErasedEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    public DesignElementErasedEventArgs(object element, IReadOnlyList<Berth> removedBerths, IReadOnlyList<Divider> removedDividers)
    {
        Element = element;
        RemovedBerths = removedBerths;
        RemovedDividers = removedDividers;
    }

    /// <summary>
    /// The removed <see cref="Berth"/>, <see cref="Pier"/>, <see cref="LandArea"/> or <see cref="Divider"/>. When
    /// <see cref="DesignTool.PlaceDividers"/> clears a whole row of dividers, the pier they stood along.
    /// </summary>
    public object Element { get; }

    /// <summary>Every berth removed (the berth itself, or the pier's or land area's berths).</summary>
    public IReadOnlyList<Berth> RemovedBerths { get; }

    /// <summary>
    /// Dividers removed with them: a pier's dividers, or the ones left without a berth on either side. A divider still shared with a
    /// remaining berth stays.
    /// </summary>
    public IReadOnlyList<Divider> RemovedDividers { get; }
}

/// <summary>
/// Data for <see cref="MarinaDesigner.ScaleLineDrawn"/>: the user drew a line over the reference image's scale bar. Set
/// <see cref="KnownLengthMeters"/> to calibrate immediately, or call <see cref="MarinaDesigner.CalibrateReferenceImage(float)"/> later
/// (e.g. after asking the user).
/// </summary>
public sealed class ScaleLineDrawnEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    public ScaleLineDrawnEventArgs(Vector2 start, Vector2 end, float measuredLength)
    {
        Start = start;
        End = end;
        MeasuredLength = measuredLength;
    }

    /// <summary>First end, in plan coordinates.</summary>
    public Vector2 Start { get; }

    /// <summary>Second end, in plan coordinates.</summary>
    public Vector2 End { get; }

    /// <summary>Length of the line in meters at the image's current scale.</summary>
    public float MeasuredLength { get; }

    /// <summary>The real length the scale bar represents, in meters. When set by a handler, the image is calibrated right away.</summary>
    public float? KnownLengthMeters { get; set; }
}

/// <summary>Data for <see cref="MarinaDesigner.ReferenceImageChanged"/>.</summary>
public sealed class ReferenceImageChangedEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    public ReferenceImageChangedEventArgs(ReferenceImageChange change, ReferenceImage? image, Vector2 center, float metersPerPixel)
    {
        Change = change;
        Image = image;
        Center = center;
        MetersPerPixel = metersPerPixel;
    }

    /// <summary>What changed.</summary>
    public ReferenceImageChange Change { get; }

    /// <summary>The current image, or null after <see cref="ReferenceImageChange.Cleared"/>.</summary>
    public ReferenceImage? Image { get; }

    /// <summary>Center of the image in plan coordinates.</summary>
    public Vector2 Center { get; }

    /// <summary>Meters per image pixel.</summary>
    public float MetersPerPixel { get; }
}

/// <summary>Data for <see cref="MarinaDesigner.ActionRedone"/>: the last change undone was made again.</summary>
public sealed class DesignActionRedoneEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    /// <param name="description">What was made again.</param>
    /// <param name="remainingSteps">How many undone steps can still be redone.</param>
    public DesignActionRedoneEventArgs(string description, int remainingSteps)
    {
        Description = description;
        RemainingSteps = remainingSteps;
    }

    /// <summary>What was made again, e.g. "Add 4 berths".</summary>
    public string Description { get; }

    /// <summary>How many undone steps can still be redone.</summary>
    public int RemainingSteps { get; }
}

/// <summary>Data for <see cref="MarinaDesigner.ActionFailed"/>: something asked for in the view could not be done.</summary>
public sealed class DesignActionFailedEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    /// <param name="description">What was being done, e.g. "Undo Add 4 berths" or the tool in hand.</param>
    /// <param name="exception">Why it could not be done.</param>
    public DesignActionFailedEventArgs(string description, Exception exception)
    {
        Description = description;
        Exception = exception;
    }

    /// <summary>What was being done.</summary>
    public string Description { get; }

    /// <summary>Why it could not be done; its message is fit to show the user.</summary>
    public Exception Exception { get; }
}
