using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Design;

/// <summary>
/// Interactive layout designer: draw land areas as polygons, draw piers, add berths along piers, put boats ashore on land berths,
/// plant trees and erase elements directly in the 3D view, with undo and redo, optionally tracing a calibrated aerial image.
/// Available as <see cref="MarinaVisualizer.Designer"/>.
/// </summary>
/// <remarks>
/// <para>
/// Turn it on with <see cref="IsActive"/> and pick a <see cref="Tool"/>. While active, clicks go to the designer instead of selecting
/// berths; dragging, the wheel and the keyboard still move the camera. The host views (and the ready-made designer panels for
/// WinForms and Blazor) forward input as usual, so no extra wiring is needed.
/// </para>
/// <para>
/// Everything the designer creates goes through the normal API (<c>AddLandArea</c>, <c>AddPier</c>, <c>AddBerths</c>, ...), so
/// <c>LayoutChanged</c> is raised as well as the designer's own events, and every change it makes can be taken back with
/// <see cref="Undo"/> (Ctrl+Z in the views) and made again with <see cref="Redo"/> (Ctrl+Shift+Z or Ctrl+Y). Use
/// <see cref="MarinaVisualizer.ExportObjects"/> or <see cref="MarinaVisualizer.GetLayout"/> to save the result.
/// </para>
/// <para>
/// Coordinates: north is −Z (plan −Y) and east is +X, so an image with north at the top lies unmirrored under a camera with yaw 0
/// (see <see cref="ViewTopDown"/>).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var designer = marina.Designer;
/// designer.IsActive = true;
/// designer.SetReferenceImage(new ReferenceImage(width, height, rgba));
/// designer.Tool = DesignTool.MeasureScale;          // user clicks both ends of the map's scale bar
/// designer.ScaleLineDrawn += (s, e) => e.KnownLengthMeters = AskUser("Length of the scale bar (m)?");
/// designer.LandKind = LandKind.Quay;
/// designer.Tool = DesignTool.DrawLandArea;
/// designer.BerthSeparators = BerthSeparator.SinglePile;   // one mooring pile per berth boundary
/// designer.BerthGap = 0.5f;                              // half a meter between berths
/// designer.BerthServices = PierServices.PowerAndWater;   // pedestals beside the berths added to a pier
/// designer.ElementCreated += (s, e) => erp.Save(marina.ExportObjects());
/// </code>
/// </example>
public sealed partial class MarinaDesigner
{
    /// <summary>Shortest pier the <see cref="DesignTool.DrawPier"/> tool creates, in meters.</summary>
    public const float MinimumPierLength = 1f;

    /// <summary>Gap left between berths separated by <see cref="BerthSeparator.None"/> when <see cref="BerthGap"/> is smaller, in meters.</summary>
    public const float MinimumSeparatorGap = BerthPlanner.MinimumSeparatorGap;

    /// <summary>How many changes <see cref="Undo"/> can step back through (older ones are dropped).</summary>
    public const int MaxUndoSteps = 50;

    /// <summary>Shortest distance between the two clicks of <see cref="DesignTool.AddLandBerths"/> that aims the berth.</summary>
    private const float LandBerthAimDistance = 0.5f;

    private readonly DesignToolHandler[] _handlers;
    private readonly DesignHistory _history;
    private bool _active;
    private DesignTool _tool;
    private DesignToolHandler _handler;
    private Vector2? _pointer;
    private InputModifiers _modifiers;
    private Random _random = new();
    private int _updateDepth;
    private bool _stateChangePending;

    internal MarinaDesigner(MarinaVisualizer marina)
    {
        Marina = marina;
        Picker = new DesignPicker(marina);
        Planner = new BerthPlanner(marina);
        Naming = new DesignNaming(marina);
        Image = new ReferenceImageController(OnImageChanged, InvalidateScene);
        _history = new DesignHistory(marina, MaxUndoSteps);
        _history.Changed += (_, _) => RaiseStateChanged();

        _handlers =
        [
            new NavigateTool(this),
            new LandAreaTool(this),
            new PierTool(this),
            new BerthsTool(this),
            new EraseTool(this),
            new MoveImageTool(this),
            new MeasureScaleTool(this),
            new PlantTreesTool(this),
            new LandBerthTool(this),
            new RenameTool(this),
            new ServicesTool(this),
            new SelectAreaTool(this),
            new ShorelineTool(this),
        ];
        _handler = _handlers[(int)DesignTool.Navigate];

        // What the pointer looks things up in is cached until the layout changes.
        marina.LayoutChanged += (_, _) =>
        {
            Picker.Invalidate();
            Planner.Invalidate();
        };
    }

    // ---- Events ---------------------------------------------------------------------------------

    /// <summary><see cref="IsActive"/> changed.</summary>
    public event EventHandler? ActiveChanged;

    /// <summary><see cref="Tool"/> changed.</summary>
    public event EventHandler<DesignToolChangedEventArgs>? ToolChanged;

    /// <summary>The user placed or removed a point, or finished or abandoned a drawing.</summary>
    public event EventHandler<DesignDraftChangedEventArgs>? DraftChanged;

    /// <summary>A drawing is complete and its element is about to be added; handlers can change or cancel it.</summary>
    public event EventHandler<DesignElementCreatingEventArgs>? ElementCreating;

    /// <summary>A land area, pier or berths were added by the designer.</summary>
    public event EventHandler<DesignElementCreatedEventArgs>? ElementCreated;

    /// <summary>
    /// A berth or pier was clicked with the <see cref="DesignTool.Rename"/> tool: put the new name in
    /// <see cref="DesignElementRenamingEventArgs.NewName"/>. Without a handler the tool does nothing.
    /// </summary>
    /// <example>
    /// <code>
    /// designer.ElementRenaming += (s, e) => e.NewName = Prompt("New name", e.CurrentName) ?? e.CurrentName;
    /// </code>
    /// </example>
    public event EventHandler<DesignElementRenamingEventArgs>? ElementRenaming;

    /// <summary>A berth, pier or land area was removed with the <see cref="DesignTool.Erase"/> tool (or <see cref="Erase"/>).</summary>
    public event EventHandler<DesignElementErasedEventArgs>? ElementErased;

    /// <summary>Trees were scattered on a land area (<see cref="DesignTool.PlantTrees"/>, <see cref="PlantTrees"/>, or a new lawn).</summary>
    public event EventHandler<DesignTreesPlantedEventArgs>? TreesPlanted;

    /// <summary>The last change was reverted by <see cref="Undo"/>.</summary>
    public event EventHandler<DesignActionUndoneEventArgs>? ActionUndone;

    /// <summary>The last change undone was made again by <see cref="Redo"/>.</summary>
    public event EventHandler<DesignActionRedoneEventArgs>? ActionRedone;

    /// <summary>
    /// Something the user asked for in the view — a click, Enter, Ctrl+Z — could not be done: the name typed for a berth is
    /// taken, an outline was invalid, an undo is blocked by a change made since. Nothing was changed. Calls made from code
    /// throw instead, as documented on each method; this is for the view, which has nobody to throw to.
    /// </summary>
    /// <example><code>designer.ActionFailed += (s, e) => statusBar.Text = e.Exception.Message;</code></example>
    public event EventHandler<DesignActionFailedEventArgs>? ActionFailed;

    /// <summary>A scale line was drawn with <see cref="DesignTool.MeasureScale"/>.</summary>
    public event EventHandler<ScaleLineDrawnEventArgs>? ScaleLineDrawn;

    /// <summary>
    /// The reference image was set, cleared, moved, scaled or restyled. Dragging it with <see cref="DesignTool.MoveReferenceImage"/>
    /// raises this once, when the drag ends.
    /// </summary>
    public event EventHandler<ReferenceImageChangedEventArgs>? ReferenceImageChanged;

    /// <summary>
    /// Any designer state changed: activity, tool, a setting, the drawing in progress, the history, the scale line or the
    /// reference image. Designer panels listen to this to refresh their controls. One operation raises it once, however many
    /// things it changed.
    /// </summary>
    public event EventHandler? StateChanged;

    // ---- Mode and tool --------------------------------------------------------------------------

    /// <summary>
    /// Designer mode. Turning it on closes the popup, clears the selection and hover, and routes clicks to <see cref="Tool"/>.
    /// Turning it off abandons any drawing, and any drag, in progress.
    /// </summary>
    public bool IsActive
    {
        get => _active;
        set
        {
            if (value == _active) return;
            using (BeginUpdate())
            {
                if (!value) CancelDraft();
                _handler.Reset();
                _active = value;
                if (value)
                {
                    Marina.ClosePopup();
                    Marina.ClearSelection();
                    Marina.HandlePointerLeave();
                }

                _pointer = null;
                InvalidateScene();
                ActiveChanged?.Invoke(this, EventArgs.Empty);
                RaiseStateChanged();
            }
        }
    }

    /// <summary>What clicks do while <see cref="IsActive"/>. Changing it abandons any drawing, and any drag, in progress.</summary>
    public DesignTool Tool
    {
        get => _tool;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value), value, null);
            if (value == _tool) return;
            using (BeginUpdate())
            {
                CancelDraft();
                _handler.Reset();
                var previous = _tool;
                _tool = value;
                _handler = _handlers[(int)value];
                InvalidateOverlay();
                ToolChanged?.Invoke(this, new DesignToolChangedEventArgs(previous, value));
                RaiseStateChanged();
            }
        }
    }

    /// <summary>A one-line instruction for the current tool and drawing state, for a status bar or panel.</summary>
    public string ToolHint => _handler.Hint;

    /// <summary>
    /// True (default): Escape with no drawing to abandon puts the tool down, back to <see cref="DesignTool.Navigate"/>, and the view
    /// counts the key as used. False: that Escape is left to the host, so its own Escape (closing a panel, leaving design mode)
    /// works on the first press; Escape still abandons a drawing or a drag first.
    /// </summary>
    public bool EscapeReturnsToNavigate { get; set; } = true;

    // ---- Drawing state --------------------------------------------------------------------------

    /// <summary>Points placed so far in the current drawing, in plan coordinates.</summary>
    public IReadOnlyList<Vector2> DraftPoints => _handler.DraftPoints;

    /// <summary>True while a drawing is in progress: at least one point placed, or a coast waiting for its side.</summary>
    public bool HasDraft => _handler.HasDraft;

    /// <summary>
    /// Plan position under the pointer (after snapping), or null when the pointer isn't over the view. It lies on the plane the
    /// drawing is shown on — the new land's height for a land outline, the deck for a pier — so it stays under the pointer.
    /// </summary>
    public Vector2? PointerPosition => _pointer;

    /// <summary>
    /// The coast that has been drawn and is waiting for a click to say which side of it is land, or null. Enter moves
    /// a <see cref="DesignTool.DrawShoreline"/> drawing into this state (see <see cref="PickShorelineSide"/>).
    /// </summary>
    public IReadOnlyList<Vector2>? ShorelineAwaitingSide => Shoreline.AwaitingSide;

    /// <summary>
    /// Finishes the drawing in progress: closes a land outline (3+ points), ends a pier at the pointer, or adds the anchored berth.
    /// Returns true when an element was created.
    /// </summary>
    public bool CompleteDraft() => _handler.Complete() == DraftOutcome.Created;

    /// <summary>
    /// Makes the mainland from the coast waiting for a side, putting the land on the side <paramref name="landSide"/>
    /// falls on. Returns null when nothing is waiting, or when a handler cancels.
    /// </summary>
    /// <param name="landSide">A point on the side of the line that should be land.</param>
    public Domain.Shoreline? PickShorelineSide(Vector2 landSide) => Shoreline.PickSide(landSide);

    /// <summary>Abandons the drawing in progress. Returns false when there was none.</summary>
    public bool CancelDraft()
    {
        using (BeginUpdate())
        {
            if (!_handler.EndDraft(DesignDraftChange.Canceled)) return false;
            InvalidateOverlay();
            RaiseStateChanged();
            return true;
        }
    }

    /// <summary>
    /// Removes the last placed point (or takes a settled coast back to drawing). Returns false when there was none. Ctrl+Z in the
    /// views does this too while a drawing is in progress, rather than undoing the last element.
    /// </summary>
    public bool RemoveLastPoint()
    {
        if (!_handler.RemoveLastPoint()) return false;
        InvalidateOverlay();
        return true;
    }

    // ---- Internals shared with the tools --------------------------------------------------------

    internal MarinaVisualizer Marina { get; }

    internal DesignPicker Picker { get; }

    internal BerthPlanner Planner { get; }

    internal DesignNaming Naming { get; }

    internal ReferenceImageController Image { get; }

    internal InputModifiers Modifiers => _modifiers;

    /// <summary>True when the pointer snapped to a corner or edge.</summary>
    internal bool PointerSnapped { get; private set; }

    /// <summary>True when the pier being drawn was squared up with its surroundings.</summary>
    internal bool HeadingSnapped { get; set; }

    /// <summary>The settings a row of berths is laid out by, as they stand.</summary>
    internal BerthRowSettings RowSettings => new(_berthWidth, _berthLength, _berthDepth, _berthSeparators, _berthGap, _alignBerths, _berthNaming);

    /// <summary>
    /// The seed of the random numbers behind tree positions and the mainland's scenery, so a test (or a host that wants a
    /// design to come out the same every time) can have them repeat.
    /// </summary>
    /// <param name="seed">The seed.</param>
    internal void SetRandomSeed(int seed) => _random = new Random(seed);

    private ShorelineTool Shoreline => (ShorelineTool)_handlers[(int)DesignTool.DrawShoreline];

    /// <summary>
    /// Holds <see cref="StateChanged"/> back until the returned scope is disposed, then raises it once if anything changed.
    /// Scopes nest.
    /// </summary>
    internal IDisposable BeginUpdate()
    {
        _updateDepth++;
        return new UpdateScope(this);
    }

    internal void RaiseStateChanged()
    {
        if (_updateDepth > 0)
        {
            _stateChangePending = true;
            return;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The one place the designer asks for its preview to be drawn again: the pointer, the drawing, a hover target or a setting
    /// that only shows in the preview changed. Nothing in the scene itself changed.
    /// </summary>
    internal void InvalidateOverlay() => Marina.MarkOverlayDirty();

    /// <summary>Asks for the scene to be drawn again: the reference image, or the design lighting, changed.</summary>
    internal void InvalidateScene() => Marina.MarkSceneDirty();

    internal void RaiseDraftChanged(DesignDraftChange change, IReadOnlyList<Vector2> points)
    {
        InvalidateOverlay();
        DraftChanged?.Invoke(this, new DesignDraftChangedEventArgs(_tool, change, points));
        RaiseStateChanged();
    }

    /// <summary>Ends the drawing of the tool in hand, however it ended; creating an element from code ends it too.</summary>
    internal void FinishDraft(DesignDraftChange change)
    {
        using (BeginUpdate())
        {
            _handler.EndDraft(change);
            InvalidateOverlay();
            RaiseStateChanged();
        }
    }

    /// <summary>
    /// Runs a creation the user started from the view. Invalid geometry, or a name already taken, abandons the drawing and is
    /// reported through <see cref="ActionFailed"/> instead of throwing into the input loop.
    /// </summary>
    internal bool TryCreate(Func<bool> create)
    {
        try
        {
            return create();
        }
        catch (Exception ex) when (ex is MarinaLayoutException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            FinishDraft(DesignDraftChange.Canceled);
            ReportFailure(_tool.GetDisplayName(), ex);
            return false;
        }
    }

    internal void ReportFailure(string description, Exception exception)
    {
        ActionFailed?.Invoke(this, new DesignActionFailedEventArgs(description, exception));
        RaiseStateChanged();
    }

    /// <summary>Bow direction of the land berth being placed: from its spot toward the pointer (Shift: 15° steps), or the set heading.</summary>
    internal float HeadingFor(Vector2 center, Vector2? pointer)
    {
        if (pointer is not { } target || Vector2.Distance(center, target) < LandBerthAimDistance) return _landBerthHeading;
        var heading = MarinaMath.DirectionToHeading(target - center);
        return (_modifiers & InputModifiers.Shift) != 0 ? MathF.Round(heading / 15f) * 15f : heading;
    }

    private void EndUpdate()
    {
        if (_updateDepth == 0) return;
        _updateDepth--;
        if (_updateDepth > 0 || !_stateChangePending) return;
        _stateChangePending = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class UpdateScope : IDisposable
    {
        private MarinaDesigner? _designer;

        public UpdateScope(MarinaDesigner designer) => _designer = designer;

        public void Dispose()
        {
            _designer?.EndUpdate();
            _designer = null;
        }
    }
}
