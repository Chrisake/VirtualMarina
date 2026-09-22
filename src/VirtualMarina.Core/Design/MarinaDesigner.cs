using System.Globalization;
using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Input;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Picking;
using VirtualMarina.Core.Resources;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Design;

/// <summary>
/// Interactive layout designer: draw land areas as polygons, draw piers, add berths along piers, put boats ashore on land berths,
/// plant trees and erase elements directly in the 3D view, with undo, optionally tracing a calibrated aerial image. Available as
/// <see cref="MarinaVisualizer.Designer"/>.
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
/// <see cref="Undo"/> (Ctrl+Z in the views). Use <see cref="MarinaVisualizer.ExportObjects"/> or
/// <see cref="MarinaVisualizer.GetLayout"/> to save the result.
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
public sealed class MarinaDesigner
{
    /// <summary>How close a pier's direction must be to a square one before it snaps, in degrees.</summary>
    private const float PierAngleSnapDegrees = 6f;

    /// <summary>How far from the pier's shore end a land edge still counts as the quay it springs from, in meters.</summary>
    private const float PierAngleReferenceRange = 40f;

    /// <summary>Shortest pier the <see cref="DesignTool.DrawPier"/> tool creates, in meters.</summary>
    public const float MinimumPierLength = 1f;

    /// <summary>Gap left between berths separated by <see cref="BerthSeparator.None"/> when <see cref="BerthGap"/> is smaller, in meters.</summary>
    public const float MinimumSeparatorGap = 0.3f;

    /// <summary>How many changes <see cref="Undo"/> can step back through (older ones are dropped).</summary>
    public const int MaxUndoSteps = 50;

    /// <summary>How far a divider may sit from a berth's edge and still count as its separator, in meters.</summary>
    private const float SeparatorTolerance = 1f;

    /// <summary>Shortest distance between the two clicks of <see cref="DesignTool.AddLandBerths"/> that aims the berth.</summary>
    private const float LandBerthAimDistance = 0.5f;

    private static readonly Vector4 DraftColor = new(1f, 0.93f, 0.35f, 0.95f);
    private static readonly Vector4 DraftClosingColor = new(1f, 0.93f, 0.35f, 0.5f);
    private static readonly Vector4 InvalidColor = new(0.95f, 0.25f, 0.2f, 0.95f);
    private static readonly Vector4 PointerColor = new(1f, 1f, 1f, 0.95f);
    private static readonly Vector4 SnapColor = new(0.3f, 0.95f, 1f, 0.95f);
    private static readonly Vector4 BerthPreviewColor = new(0.25f, 0.85f, 0.4f, 0.55f);
    private static readonly Vector4 EraseColor = new(0.95f, 0.2f, 0.15f, 0.55f);
    private static readonly Vector4 ImageOutlineColor = new(1f, 0.6f, 0.15f, 0.95f);
    private static readonly Vector4 ScaleLineColor = new(1f, 0.35f, 0.85f, 0.95f);
    private static readonly Vector4 ServicePreviewColor = new(0.99f, 0.78f, 0.15f, 0.6f);
    private static readonly Vector4 SelectionBoxColor = new(0.35f, 0.78f, 1f, 0.95f);
    private static readonly Vector4 TrafficLaneColor = new(1f, 0.85f, 0.25f, 0.85f);
    private static readonly Vector4 TrafficLaneBackColor = new(0.45f, 0.85f, 1f, 0.85f);
    private static readonly Vector4 TextColor = new(1f, 1f, 1f, 0.97f);

    private readonly MarinaVisualizer _marina;
    private readonly List<Vector2> _points = new();
    private List<Vector2>? _shorelineSide;
    private bool _active;
    private DesignTool _tool;
    private Vector2? _pointer;
    private bool _pointerSnapped;
    private bool _headingSnapped;
    private InputModifiers _modifiers;
    private string? _berthPierId;
    private PierSide _berthSide;
    private float _berthAlong;
    private string? _berthLandId;
    private object? _eraseTarget;
    private Vector2? _imageDragLast;
    private Vector2? _selectFrom;
    private Vector2? _selectTo;
    private InputModifiers _selectModifiers;
    private float _selectHeading;
    private float _overlayDistance = -1f;
    private float _overlayYaw;

    private LandKind _landKind = LandKind.Quay;
    private float _landHeight = 1f;
    private PierType _pierType = PierType.FloatingWooden;
    private float _pierWidth = 2.5f;
    private PierSides _pierSides = PierSides.Both;
    private float _berthWidth = 5f;
    private float _berthLength = 12f;
    private float _berthDepth = 3f;
    private BerthSeparator _berthSeparators = BerthSeparator.FingerPiers;
    private float _berthGap;
    private bool _alignBerths = true;
    private PierServices _berthServices = PierServices.None;
    private BerthNamingScheme _berthNaming = BerthNamingScheme.Default;
    private string _pierNamePattern = "Pier {pier}";
    private float _landBerthHeading;
    private float _snapPixels = 12f;
    private float _fogFactor = 0.15f;
    private float _treeDensity = 8f;
    private readonly Random _random = new();
    private HinterlandScenery _scenery = HinterlandScenery.Countryside;
    private readonly List<DesignAction> _history = new();
    private bool _undoing;

    private ReferenceImage? _image;
    private Vector2 _imageCenter;
    private float _imageMetersPerPixel = 0.25f;
    private float _imageOpacity = 0.6f;
    private bool _imageVisible = true;
    private bool _imageAboveScene = true;
    private (Vector2 Start, Vector2 End)? _scaleLine;

    internal MarinaDesigner(MarinaVisualizer marina)
    {
        _marina = marina;
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

    /// <summary>A scale line was drawn with <see cref="DesignTool.MeasureScale"/>.</summary>
    public event EventHandler<ScaleLineDrawnEventArgs>? ScaleLineDrawn;

    /// <summary>The reference image was set, cleared, moved, scaled or restyled.</summary>
    public event EventHandler<ReferenceImageChangedEventArgs>? ReferenceImageChanged;

    /// <summary>
    /// Any designer state changed: activity, tool, a setting, the drawing in progress, the scale line or the reference image.
    /// Designer panels listen to this to refresh their controls.
    /// </summary>
    public event EventHandler? StateChanged;

    // ---- Mode and tool --------------------------------------------------------------------------

    /// <summary>
    /// Designer mode. Turning it on closes the popup, clears the selection and hover, and routes clicks to <see cref="Tool"/>.
    /// Turning it off abandons any drawing in progress.
    /// </summary>
    public bool IsActive
    {
        get => _active;
        set
        {
            if (value == _active) return;
            if (!value) CancelDraft();
            _active = value;
            if (value)
            {
                _marina.ClosePopup();
                _marina.ClearSelection();
                _marina.HandlePointerLeave();
            }

            _pointer = null;
            _eraseTarget = null;
            _marina.MarkSceneDirty();
            ActiveChanged?.Invoke(this, EventArgs.Empty);
            RaiseStateChanged();
        }
    }

    /// <summary>What clicks do while <see cref="IsActive"/>. Changing it abandons any drawing in progress.</summary>
    public DesignTool Tool
    {
        get => _tool;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value), value, null);
            if (value == _tool) return;
            CancelDraft();
            var previous = _tool;
            _tool = value;
            _eraseTarget = null;
            _imageDragLast = null;
            _marina.MarkSceneDirty();
            ToolChanged?.Invoke(this, new DesignToolChangedEventArgs(previous, value));
            RaiseStateChanged();
        }
    }

    /// <summary>A one-line instruction for the current tool and drawing state, for a status bar or panel.</summary>
    public string ToolHint => _tool switch
    {
        DesignTool.DrawLandArea when _points.Count == 0 => Strings.HintDrawLandAreaFirst,
        DesignTool.DrawLandArea when _points.Count < 3 => Strings.HintDrawLandAreaCorners,
        DesignTool.DrawLandArea => Strings.HintDrawLandAreaFinish,
        DesignTool.DrawPier when _points.Count == 0 => Strings.HintDrawPierStart,
        DesignTool.DrawPier => Strings.HintDrawPierEnd,
        DesignTool.AddBerths when _berthPierId is null => Strings.HintAddBerthsStart,
        DesignTool.AddBerths => Strings.HintAddBerthsEnd,
        DesignTool.AddLandBerths when _berthLandId is null => Strings.HintAddLandBerthsStart,
        DesignTool.AddLandBerths => Strings.HintAddLandBerthsHeading,
        DesignTool.Erase => Strings.HintErase,
        DesignTool.Rename => Strings.HintRename,
        DesignTool.EditServices => Strings.HintEditServices,
        DesignTool.SelectArea => Strings.HintSelectArea,
        DesignTool.DrawShoreline when _shorelineSide is not null => Strings.HintDrawShorelineSide,
        DesignTool.DrawShoreline when _points.Count == 0 => Strings.HintDrawShorelineFirst,
        DesignTool.DrawShoreline when DraftShorelineCrosses() => Strings.HintDrawShorelineCrossing,
        DesignTool.DrawShoreline => Strings.HintDrawShorelineMore,
        DesignTool.PlantTrees => _treeDensity > 0f ? Strings.HintPlantTrees : Strings.HintPlantTreesNone,
        DesignTool.MoveReferenceImage => _image is null ? Strings.HintReferenceImageMissing : Strings.HintMoveReferenceImage,
        DesignTool.MeasureScale when _image is null => Strings.HintReferenceImageMissing,
        DesignTool.MeasureScale when _points.Count == 0 => Strings.HintMeasureScaleFirst,
        DesignTool.MeasureScale => Strings.HintMeasureScaleSecond,
        _ => Strings.HintNavigate,
    };

    // ---- Settings -------------------------------------------------------------------------------

    /// <summary>Kind of land area drawn by <see cref="DesignTool.DrawLandArea"/>. Default <see cref="Domain.LandKind.Quay"/>.</summary>
    public LandKind LandKind
    {
        get => _landKind;
        set => SetSetting(ref _landKind, Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, null));
    }

    /// <summary>Top height of new land areas above the water, 0–50 m (default 1).</summary>
    public float LandHeight
    {
        get => _landHeight;
        set => SetSetting(ref _landHeight, RequireRange(value, 0f, 50f));
    }

    /// <summary>
    /// What is scattered across the mainland drawn by <see cref="DesignTool.DrawShoreline"/>. Default
    /// <see cref="HinterlandScenery.Countryside"/>.
    /// </summary>
    public HinterlandScenery Scenery
    {
        get => _scenery;
        set => SetSetting(ref _scenery, Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, null));
    }

    /// <summary>
    /// Trees per 1000 m² scattered on new lawns (<see cref="Domain.LandKind.Grass"/>) and by <see cref="DesignTool.PlantTrees"/>, 0–100
    /// (default 8; 0 = no trees). Positions are random when drawn and then stored with the land area, so they stay put.
    /// </summary>
    public float TreeDensity
    {
        get => _treeDensity;
        set => SetSetting(ref _treeDensity, RequireRange(value, 0f, 100f));
    }

    /// <summary>Construction of piers drawn by <see cref="DesignTool.DrawPier"/>. Default <see cref="Domain.PierType.FloatingWooden"/>.</summary>
    public PierType PierType
    {
        get => _pierType;
        set => SetSetting(ref _pierType, Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, null));
    }

    /// <summary>Deck width of new piers, 0.5–30 m (default 2.5).</summary>
    public float PierWidth
    {
        get => _pierWidth;
        set => SetSetting(ref _pierWidth, RequireRange(value, 0.5f, 30f));
    }

    /// <summary>Berthing sides of new piers (<see cref="Pier.BerthingSides"/>). Default <see cref="PierSides.Both"/>.</summary>
    public PierSides PierBerthingSides
    {
        get => _pierSides;
        set => SetSetting(ref _pierSides, value is PierSides.Left or PierSides.Right or PierSides.Both ? value : throw new ArgumentOutOfRangeException(nameof(value), value, null));
    }

    /// <summary>Width of new berths (along the pier), 1–50 m (default 5).</summary>
    public float BerthWidth
    {
        get => _berthWidth;
        set => SetSetting(ref _berthWidth, RequireRange(value, 1f, 50f));
    }

    /// <summary>Length of new berths (away from the pier), 1–150 m (default 12).</summary>
    public float BerthLength
    {
        get => _berthLength;
        set => SetSetting(ref _berthLength, RequireRange(value, 1f, 150f));
    }

    /// <summary>Water depth of new berths, stored as <see cref="Berth.MaxDraft"/>, 0.1–50 m (default 3).</summary>
    public float BerthDepth
    {
        get => _berthDepth;
        set => SetSetting(ref _berthDepth, RequireRange(value, 0.1f, 50f));
    }

    /// <summary>
    /// What separates new berths: their own finger piers (default), nothing at all, or generated <see cref="Divider"/> elements
    /// (finger pier, piles, boom or a single pile at the outer end).
    /// </summary>
    public BerthSeparator BerthSeparators
    {
        get => _berthSeparators;
        set => SetSetting(ref _berthSeparators, Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, null));
    }

    /// <summary>
    /// Space left between neighbouring berths, 0–20 m (default 0, berths touching). With <see cref="BerthSeparator.None"/> at least
    /// <see cref="MinimumSeparatorGap"/> is used, so the berths never touch without a separator.
    /// </summary>
    public float BerthGap
    {
        get => _berthGap;
        set => SetSetting(ref _berthGap, RequireRange(value, 0f, 20f));
    }

    /// <summary>
    /// True (default): a row of berths lines up with the nearest existing berth edge on that side, or with the pier's start, in whole
    /// berth pitches. False: the first berth starts exactly where you click (or at <c>fromAlong</c>), at any offset from the pier's start.
    /// </summary>
    public bool AlignBerthsToExisting
    {
        get => _alignBerths;
        set => SetSetting(ref _alignBerths, value);
    }

    /// <summary>
    /// Power/water pedestals switched on for a pier when berths are added to it (default <see cref="PierServices.None"/>). They are
    /// drawn on the berthing sides only, next to the berths that exist (see <see cref="Pier.Services"/>).
    /// </summary>
    public PierServices BerthServices
    {
        get => _berthServices;
        set => SetSetting(ref _berthServices, (value & ~PierServices.PowerAndWater) == 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, null));
    }

    /// <summary>
    /// How the berths drawn from now on are named: the pattern, the first number and the step between them
    /// (default <see cref="BerthNamingScheme.Default"/>, giving <c>A-L01</c>, <c>A-L02</c>, ...).
    /// </summary>
    /// <exception cref="ArgumentException">The scheme could not name anything (see <see cref="BerthNamingScheme.Validate"/>).</exception>
    /// <example><code>designer.BerthNaming = new BerthNamingScheme { Pattern = "{number}", StartNumber = 101, Increment = 2 };</code></example>
    public BerthNamingScheme BerthNaming
    {
        get => _berthNaming;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Validate().FirstOrDefault() is { } problem) throw new ArgumentException(problem, nameof(value));
            SetSetting(ref _berthNaming, value);
        }
    }

    /// <summary>
    /// True when a pier could be given this id: nothing is using it, or the pier using it is the one asking. Ids are
    /// compared without regard to case, and an empty one is never free.
    /// </summary>
    /// <param name="pierId">The id being asked for.</param>
    /// <param name="forPierId">The pier that wants it, so keeping its own id counts as free. Null for a new pier.</param>
    public bool IsPierIdAvailable(string? pierId, string? forPierId = null)
    {
        if (string.IsNullOrWhiteSpace(pierId)) return false;
        var wanted = pierId.Trim();
        if (forPierId is not null && string.Equals(wanted, forPierId, StringComparison.OrdinalIgnoreCase)) return true;
        return _marina.GetPier(wanted) is null;
    }

    /// <summary>
    /// True when a berth could be given this name. A berth's name is also its id, so it has to be free across the
    /// whole marina.
    /// </summary>
    /// <param name="berthName">The name being asked for.</param>
    /// <param name="forBerthId">The berth that wants it, so keeping its own name counts as free.</param>
    public bool IsBerthNameAvailable(string? berthName, string? forBerthId = null)
    {
        if (string.IsNullOrWhiteSpace(berthName)) return false;
        var wanted = berthName.Trim();
        if (forBerthId is not null && string.Equals(wanted, forBerthId, StringComparison.OrdinalIgnoreCase)) return true;
        return _marina.GetBerth(wanted) is null;
    }

    /// <summary>The name <see cref="PierNamePattern"/> gives a pier with this id, e.g. "Pier C".</summary>
    /// <param name="pierId">The pier id to put in the pattern.</param>
    public string GeneratedPierName(string pierId)
    {
        ArgumentNullException.ThrowIfNull(pierId);
        return _pierNamePattern.Replace("{pier}", pierId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Name given to a pier as it is drawn (default <c>Pier {pier}</c>, e.g. "Pier A"). <c>{pier}</c> stands for the
    /// pier's generated id; text without it names every new pier the same, which is allowed — names need not be unique.
    /// </summary>
    /// <exception cref="ArgumentException">The pattern is empty.</exception>
    /// <seealso cref="RenamePier"/>
    public string PierNamePattern
    {
        get => _pierNamePattern;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            SetSetting(ref _pierNamePattern, value);
        }
    }

    /// <summary>
    /// Direction a boat stored on a land berth points, in degrees (0° = +Z, 90° = +X; default 0). Used by
    /// <see cref="DesignTool.AddLandBerths"/> when both clicks land on the same spot, and by <see cref="CreateLandBerth"/> by default.
    /// </summary>
    public float LandBerthHeading
    {
        get => _landBerthHeading;
        set => SetSetting(ref _landBerthHeading, float.IsFinite(value) ? MarinaMath.DeltaAngle(0f, value) : throw new ArgumentOutOfRangeException(nameof(value), value, null));
    }

    /// <summary>
    /// Points within this many pixels of an existing corner, pier end or land edge snap to it (default 12, 0 turns snapping off).
    /// Holding Alt also turns it off.
    /// </summary>
    public float SnapDistancePixels
    {
        get => _snapPixels;
        set => SetSetting(ref _snapPixels, RequireRange(value, 0f, 100f));
    }

    /// <summary>
    /// Multiplier for the distance fog while the designer is active (default 0.15; 1 keeps the normal fog), so the marina and the
    /// reference image stay clear when seen from high above.
    /// </summary>
    public float FogFactor
    {
        get => _fogFactor;
        set => SetSetting(ref _fogFactor, RequireRange(value, 0f, 1f));
    }

    // ---- Drawing state --------------------------------------------------------------------------

    /// <summary>Points placed so far in the current drawing, in plan coordinates.</summary>
    public IReadOnlyList<Vector2> DraftPoints => _points.ToArray();

    /// <summary>True while a drawing is in progress (at least one point placed).</summary>
    public bool HasDraft => _points.Count > 0;

    /// <summary>Plan position under the pointer (after snapping), or null when the pointer isn't over the view.</summary>
    public Vector2? PointerPosition => _pointer;

    /// <summary>
    /// Finishes the drawing in progress: closes a land outline (3+ points), ends a pier at the pointer, or adds the anchored berth.
    /// Returns true when an element was created.
    /// </summary>
    public bool CompleteDraft()
    {
        switch (_tool)
        {
            case DesignTool.DrawLandArea when _points.Count >= 3:
            {
                var outline = RemoveDuplicatePoints(_points);
                if (outline.Count < 3 || !PolygonMath.IsSimple(outline)) return false;
                return TryCreate(() => CreateLandArea(outline) is not null);
            }

            case DesignTool.DrawPier when _points.Count == 1 && _pointer is { } end:
                return TryCreate(() => CreatePier(_points[0], end) is not null);

            case DesignTool.AddBerths when _berthPierId is not null:
                return TryCreate(() => CreateBerths(_berthPierId, _berthSide, _berthAlong, _berthAlong).Count > 0);

            case DesignTool.AddLandBerths when _berthLandId is not null && _points.Count == 1:
                return TryCreate(() => CreateLandBerth(_berthLandId, _points[0], HeadingFor(_points[0], _pointer)) is not null);

            // Enter does not finish a coast; it settles the line, and the next click says which side is land.
            case DesignTool.DrawShoreline when _shorelineSide is null && _points.Count >= 2 && !DraftShorelineCrosses():
            {
                var line = RemoveDuplicatePoints(_points);
                if (line.Count < 2) return false;

                _shorelineSide = line.ToList();
                _points.Clear();
                _marina.MarkSceneDirty();
                DraftChanged?.Invoke(this, new DesignDraftChangedEventArgs(_tool, DesignDraftChange.PointAdded, _shorelineSide.ToArray()));
                RaiseStateChanged();
                return false;
            }

            default:
                return false;
        }
    }

    /// <summary>
    /// The coast that has been drawn and is waiting for a click to say which side of it is land, or null. Enter moves
    /// a <see cref="DesignTool.DrawShoreline"/> drawing into this state (see <see cref="PickShorelineSide"/>).
    /// </summary>
    public IReadOnlyList<Vector2>? ShorelineAwaitingSide => _shorelineSide?.ToArray();

    /// <summary>
    /// Makes the mainland from the coast waiting for a side, putting the land on the side <paramref name="landSide"/>
    /// falls on. Returns null when nothing is waiting, or when a handler cancels.
    /// </summary>
    /// <param name="landSide">A point on the side of the line that should be land.</param>
    public Shoreline? PickShorelineSide(Vector2 landSide)
    {
        if (_shorelineSide is not { Count: >= 2 } line) return null;

        // Both halves of the plan are covered by the two candidates, so testing one of them decides it.
        var onLeft = new Shoreline(line, landOnLeft: true).Contains(landSide);
        return CreateShoreline(line, onLeft);
    }

    /// <summary>True while a coast has been drawn whose endless ends run into each other, so neither side is the land.</summary>
    private bool DraftShorelineCrosses()
    {
        if (_points.Count < 2) return false;
        var line = RemoveDuplicatePoints(_points);
        return line.Count >= 2 && new Shoreline(line, landOnLeft: true).Validate().Any();
    }

    /// <summary>Abandons the drawing in progress. Returns false when there was none.</summary>
    public bool CancelDraft()
    {
        if (_points.Count == 0 && _berthPierId is null && _shorelineSide is null) return false;
        _points.Clear();
        _berthPierId = null;
        _berthLandId = null;
        _shorelineSide = null;
        _marina.MarkSceneDirty();
        DraftChanged?.Invoke(this, new DesignDraftChangedEventArgs(_tool, DesignDraftChange.Canceled, Array.Empty<Vector2>()));
        RaiseStateChanged();
        return true;
    }

    /// <summary>Removes the last placed point. Returns false when there was none.</summary>
    public bool RemoveLastPoint()
    {
        if (_points.Count == 0 && _shorelineSide is { Count: > 0 } waiting)
        {
            _points.AddRange(waiting);
            _shorelineSide = null;
            _marina.MarkSceneDirty();
            DraftChanged?.Invoke(this, new DesignDraftChangedEventArgs(_tool, DesignDraftChange.PointRemoved, _points.ToArray()));
            RaiseStateChanged();
            return true;
        }

        if (_points.Count == 0) return false;
        _points.RemoveAt(_points.Count - 1);
        if (_points.Count == 0)
        {
            _berthPierId = null;
            _berthLandId = null;
        }

        _marina.MarkSceneDirty();
        DraftChanged?.Invoke(this, new DesignDraftChangedEventArgs(_tool, DesignDraftChange.PointRemoved, _points.ToArray()));
        RaiseStateChanged();
        return true;
    }

    // ---- Creating elements (also usable from code) ----------------------------------------------

    /// <summary>
    /// Adds a land area with the given outline and the current <see cref="LandKind"/> and <see cref="LandHeight"/>, raising
    /// <see cref="ElementCreating"/> and <see cref="ElementCreated"/>. Returns null when a handler cancels.
    /// </summary>
    /// <exception cref="MarinaLayoutException">The outline is invalid (e.g. its edges cross).</exception>
    public LandArea? CreateLandArea(IReadOnlyList<Vector2> outline)
    {
        ArgumentNullException.ThrowIfNull(outline);
        var prefix = _landKind switch { LandKind.Grass => "lawn", LandKind.Breakwater => "breakwater", _ => "quay" };
        var id = NextId(prefix + "-", _marina.GetLandAreas().Select(l => l.Id));
        var land = new LandArea(id, outline, _landHeight, _landKind)
        {
            Name = $"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(prefix)} {id[(prefix.Length + 1)..]}",
            Trees = _landKind == LandKind.Grass ? LandArea.GenerateTrees(outline, _treeDensity, _random) : Array.Empty<LandTree>(),
        };

        var args = new DesignElementCreatingEventArgs(DesignTool.DrawLandArea, land, null, Array.Empty<Berth>(), Array.Empty<Divider>());
        ElementCreating?.Invoke(this, args);
        if (args.Cancel || args.LandArea is null)
        {
            FinishDraft(DesignDraftChange.Canceled);
            return null;
        }

        _marina.AddLandArea(args.LandArea);
        var added = _marina.GetLandArea(args.LandArea.Id)!;
        Record(new DesignAction(Strings.Format(Strings.UndoDrawLandArea, added.DisplayName)) { AddedLandAreas = { added } });
        FinishDraft(DesignDraftChange.Completed);
        ElementCreated?.Invoke(this, new DesignElementCreatedEventArgs(DesignTool.DrawLandArea, added, null, Array.Empty<Berth>(), Array.Empty<Divider>()));
        if (added.Trees.Count > 0) TreesPlanted?.Invoke(this, new DesignTreesPlantedEventArgs(added, 0));
        return added;
    }

    /// <summary>
    /// Sets the mainland behind the marina from a drawn coast and the side of it that is land, using the current
    /// <see cref="Scenery"/>. Whatever mainland was there is replaced. Returns null when a handler cancels.
    /// </summary>
    /// <param name="line">The coast, at least two points. Its first and last segments run on without end.</param>
    /// <param name="landOnLeft">True when the land is to the left of the line walked from the first point to the last.</param>
    /// <exception cref="MarinaLayoutException">The line's endless segments cross, so neither side of it is the land.</exception>
    public Shoreline? CreateShoreline(IReadOnlyList<Vector2> line, bool landOnLeft)
    {
        ArgumentNullException.ThrowIfNull(line);
        var previous = _marina.Shoreline;
        var shoreline = new Shoreline(line, landOnLeft)
        {
            // The coast keeps the look of the land already drawn, so the two read as one piece of ground.
            Height = previous?.Height ?? _landHeight,
            Kind = previous?.Kind ?? LandKind.Grass,
            Scenery = _scenery,
            ScenerySeed = _random.Next(1, int.MaxValue),
        };

        var args = new DesignElementCreatingEventArgs(DesignTool.DrawShoreline, null, null, Array.Empty<Berth>(), Array.Empty<Divider>()) { Shoreline = shoreline };
        ElementCreating?.Invoke(this, args);
        if (args.Cancel || args.Shoreline is null)
        {
            FinishDraft(DesignDraftChange.Canceled);
            return null;
        }

        _marina.SetShoreline(args.Shoreline);
        Record(new DesignAction(Strings.UndoDrawShoreline) { PreviousShoreline = previous, TouchedShoreline = true });
        FinishDraft(DesignDraftChange.Completed);
        ElementCreated?.Invoke(this, new DesignElementCreatedEventArgs(DesignTool.DrawShoreline, null, null, Array.Empty<Berth>(), Array.Empty<Divider>()) { Shoreline = args.Shoreline });
        return args.Shoreline;
    }

    /// <summary>Takes the mainland away, leaving the marina in open water. Returns false when there was none.</summary>
    public bool DeleteShoreline()
    {
        var previous = _marina.Shoreline;
        if (previous is null) return false;

        _marina.SetShoreline(null);
        Record(new DesignAction(Strings.UndoRemoveShoreline) { PreviousShoreline = previous, TouchedShoreline = true });
        RaiseStateChanged();
        return true;
    }

    /// <summary>
    /// Adds a pier from <paramref name="start"/> (shore end) to <paramref name="end"/> with the current <see cref="PierType"/>,
    /// <see cref="PierWidth"/> and <see cref="PierBerthingSides"/>. Returns null when a handler cancels.
    /// </summary>
    /// <exception cref="ArgumentException">The ends are closer than <see cref="MinimumPierLength"/>.</exception>
    public Pier? CreatePier(Vector2 start, Vector2 end)
    {
        var length = Vector2.Distance(start, end);
        if (!(length >= MinimumPierLength)) throw new ArgumentException($"A pier must be at least {MinimumPierLength} m long.", nameof(end));

        var id = NextPierId();
        var pier = new Pier(id, GeneratedPierName(id), start, MarinaMath.DirectionToHeading(end - start), length, _pierWidth, _pierType)
        {
            BerthingSides = _pierSides,
            Services = _berthServices,
        };

        var args = new DesignElementCreatingEventArgs(DesignTool.DrawPier, null, pier, Array.Empty<Berth>(), Array.Empty<Divider>());
        ElementCreating?.Invoke(this, args);
        if (args.Cancel || args.Pier is null)
        {
            FinishDraft(DesignDraftChange.Canceled);
            return null;
        }

        _marina.AddPier(args.Pier);
        var added = _marina.GetPier(args.Pier.Id)!;
        Record(new DesignAction(Strings.Format(Strings.UndoDrawPier, added.Id)) { AddedPiers = { added } });
        FinishDraft(DesignDraftChange.Completed);
        ElementCreated?.Invoke(this, new DesignElementCreatedEventArgs(DesignTool.DrawPier, null, added, Array.Empty<Berth>(), Array.Empty<Divider>()));
        return added;
    }

    /// <summary>
    /// Adds a row of berths of the current <see cref="BerthWidth"/>, <see cref="BerthLength"/> and <see cref="BerthDepth"/> on one side of a
    /// pier, covering the stretch between two distances from the pier's start (equal distances add one berth), separated by
    /// <see cref="BerthSeparators"/> and <see cref="BerthGap"/>. With <see cref="AlignBerthsToExisting"/> the row lines up with existing
    /// berths on that side; otherwise it starts exactly at <paramref name="fromAlong"/>. Places already taken are skipped, and
    /// <see cref="BerthServices"/> switches the pier's pedestals on. Returns the berths added (empty when none fit or a handler cancels).
    /// </summary>
    /// <param name="pierId">The pier.</param>
    /// <param name="side">Side of the pier.</param>
    /// <param name="fromAlong">Distance from the pier's start where the row begins.</param>
    /// <param name="toAlong">Distance from the pier's start where the row ends.</param>
    /// <exception cref="KeyNotFoundException">No pier has this id.</exception>
    /// <exception cref="InvalidOperationException">The pier has no berths on <paramref name="side"/>.</exception>
    public IReadOnlyList<Berth> CreateBerths(string pierId, PierSide side, float fromAlong, float toAlong)
    {
        ArgumentNullException.ThrowIfNull(pierId);
        var pier = _marina.GetPier(pierId) ?? throw new KeyNotFoundException($"Pier '{pierId}' does not exist.");
        if (!pier.HasBerthsOn(side)) throw new InvalidOperationException($"Pier '{pier.Id}' has no berths on the {side} side.");

        var (berths, dividers) = PlanBerths(pier, side, fromAlong, toAlong);
        if (berths.Count == 0)
        {
            FinishDraft(DesignDraftChange.Canceled);
            return Array.Empty<Berth>();
        }

        var args = new DesignElementCreatingEventArgs(DesignTool.AddBerths, null, null, berths, dividers);
        ElementCreating?.Invoke(this, args);
        if (args.Cancel || args.Berths.Count == 0)
        {
            FinishDraft(DesignDraftChange.Canceled);
            return Array.Empty<Berth>();
        }

        // Pedestals belong to the pier, so switching them on is part of the same undoable step.
        var withServices = _berthServices != PierServices.None && (pier.Services & _berthServices) != _berthServices
            ? pier with { Services = pier.Services | _berthServices }
            : null;

        using (_marina.BeginUpdate())
        {
            _marina.AddBerths(args.Berths);
            _marina.AddDividers(args.Dividers ?? Array.Empty<Divider>());
            if (withServices is not null) _marina.UpdatePier(withServices);
        }

        var added = args.Berths.Select(s => _marina.GetBerth(s.Id)!).ToArray();
        var addedDividers = (args.Dividers ?? Array.Empty<Divider>()).Select(d => _marina.GetDivider(d.Id)!).ToArray();
        var action = new DesignAction(added.Length == 1
            ? Strings.Format(Strings.UndoAddBerth, added[0].Id)
            : Strings.Format(Strings.UndoAddBerths, added.Length));
        action.AddedBerths.AddRange(added);
        action.AddedDividers.AddRange(addedDividers);
        if (withServices is not null) action.ChangedPiers.Add(pier);
        Record(action);
        FinishDraft(DesignDraftChange.Completed);
        ElementCreated?.Invoke(this, new DesignElementCreatedEventArgs(DesignTool.AddBerths, null, null, added, addedDividers));
        return added;
    }

    /// <summary>
    /// Adds a land berth (<see cref="Berth.OnLand"/>) of the current <see cref="BerthWidth"/> and <see cref="BerthLength"/> to a land area,
    /// where a boat is stored or worked on ashore. Raises <see cref="ElementCreating"/> and <see cref="ElementCreated"/>; returns null
    /// when a handler cancels.
    /// </summary>
    /// <param name="landAreaId">The land area the berth stands on.</param>
    /// <param name="position">Center of the spot, in plan coordinates.</param>
    /// <param name="headingDegrees">Direction the stored boat's bow points; null uses <see cref="LandBerthHeading"/>.</param>
    /// <exception cref="KeyNotFoundException">No land area has this id.</exception>
    public Berth? CreateLandBerth(string landAreaId, Vector2 position, float? headingDegrees = null)
    {
        ArgumentNullException.ThrowIfNull(landAreaId);
        var land = _marina.GetLandArea(landAreaId) ?? throw new KeyNotFoundException($"Land area '{landAreaId}' does not exist.");

        var ashore = _berthNaming.AshoreNumbering;
        var id = new BerthNames(_berthNaming, _marina.GetBerths().Select(existing => existing.Id), ashore.Start, ashore.Step)
            .Next(number => _berthNaming.Format(land, number));

        var berth = Berth.OnLand(id, land.Id, position, headingDegrees ?? _landBerthHeading, _berthLength, _berthWidth);
        var args = new DesignElementCreatingEventArgs(DesignTool.AddLandBerths, null, null, new[] { berth }, Array.Empty<Divider>());
        ElementCreating?.Invoke(this, args);
        if (args.Cancel || args.Berths.Count == 0)
        {
            FinishDraft(DesignDraftChange.Canceled);
            return null;
        }

        _marina.AddBerths(args.Berths);
        var added = args.Berths.Select(created => _marina.GetBerth(created.Id)!).ToArray();
        var action = new DesignAction(Strings.Format(Strings.UndoAddLandBerth, added[0].Id));
        action.AddedBerths.AddRange(added);
        Record(action);
        FinishDraft(DesignDraftChange.Completed);
        ElementCreated?.Invoke(this, new DesignElementCreatedEventArgs(DesignTool.AddLandBerths, null, null, added, Array.Empty<Divider>()));
        return added[0];
    }

    /// <summary>
    /// Replaces the trees of a lawn with new, randomly placed ones (kept clear of its land berths) and raises <see cref="TreesPlanted"/>.
    /// Returns the updated land area, or null when it doesn't exist or is not a <see cref="LandKind.Grass"/> area.
    /// </summary>
    /// <param name="landAreaId">The lawn.</param>
    /// <param name="treesPer1000SquareMeters">Coverage; null uses <see cref="TreeDensity"/>, 0 removes the trees.</param>
    public LandArea? PlantTrees(string landAreaId, float? treesPer1000SquareMeters = null)
    {
        ArgumentNullException.ThrowIfNull(landAreaId);
        if (_marina.GetLandArea(landAreaId) is not { Kind: LandKind.Grass } land) return null;

        var density = treesPer1000SquareMeters is { } d ? RequireRange(d, 0f, 100f) : _treeDensity;
        var keepClear = _marina.GetBerthsByLandArea(land.Id).Select(landBerth => landBerth.Bounds);
        return SetTrees(land, LandArea.GenerateTrees(land.Points, density, _random, keepClear), density > 0f ? Strings.UndoPlantTrees : Strings.UndoRemoveTrees);
    }

    /// <summary>
    /// Removes every tree from a land area (of any kind) and raises <see cref="TreesPlanted"/> with an empty
    /// <see cref="LandArea.Trees"/>. Returns the updated land area, or null when it doesn't exist or has no trees.
    /// </summary>
    /// <param name="landAreaId">The land area.</param>
    public LandArea? RemoveTrees(string landAreaId)
    {
        ArgumentNullException.ThrowIfNull(landAreaId);
        return _marina.GetLandArea(landAreaId) is { Trees.Count: > 0 } land ? SetTrees(land, Array.Empty<LandTree>(), Strings.UndoRemoveTrees) : null;
    }

    private LandArea SetTrees(LandArea land, IReadOnlyList<LandTree> trees, string description)
    {
        var previous = land.Trees.Count;
        _marina.UpdateLandArea(land with { Trees = trees });
        var updated = _marina.GetLandArea(land.Id)!;
        Record(new DesignAction(Strings.Format(Strings.UndoTreesOn, description, land.DisplayName)) { ChangedLandAreas = { land } });
        TreesPlanted?.Invoke(this, new DesignTreesPlantedEventArgs(updated, previous));
        RaiseStateChanged();
        return updated;
    }

    /// <summary>
    /// Gives berths the pedestals in <see cref="BerthServices"/>, and records one step for <see cref="Undo"/>.
    /// This is what <see cref="DesignTool.EditServices"/> does when a berth is clicked.
    /// </summary>
    /// <param name="berthId">The berth clicked.</param>
    /// <param name="wholeSide">
    /// True to change every berth down that side of the pier, false for the one berth.
    /// </param>
    /// <returns>The berths that changed; empty when the id is unknown or they already had these pedestals.</returns>
    public IReadOnlyList<Berth> SetBerthServices(string berthId, bool wholeSide = false)
    {
        ArgumentNullException.ThrowIfNull(berthId);
        return _marina.GetBerth(berthId) is { } berth ? SetBerthServices(berth, wholeSide) : Array.Empty<Berth>();
    }

    private IReadOnlyList<Berth> SetBerthServices(Berth berth, bool wholeSide)
    {
        var targets = ServiceTargets(berth, wholeSide).Where(b => b.Services != _berthServices).ToArray();
        if (targets.Length == 0) return Array.Empty<Berth>();

        var action = new DesignAction(Strings.Format(Strings.UndoSetServices, _berthServices.GetDisplayName(), targets.Length));
        using (_marina.BeginUpdate())
        {
            foreach (var target in targets)
            {
                action.ChangedBerths.Add(target);
                _marina.UpdateBerth(target with { Services = _berthServices });
            }
        }

        Record(action);
        _marina.MarkSceneDirty();
        RaiseStateChanged();
        return targets;
    }

    /// <summary>The berths a pedestal change would touch: the one clicked, or its whole side of the pier.</summary>
    private IReadOnlyList<Berth> ServiceTargets(Berth berth, bool wholeSide)
    {
        if (!wholeSide || berth.PierId is not { } pierId || _marina.GetPier(pierId) is not { } pier) return new[] { berth };

        var side = MathF.Sign(Vector2.Dot(berth.Center - pier.Center, pier.Right));
        return _marina.GetBerthsByPier(pier.Id)
            .Where(other => MathF.Sign(Vector2.Dot(other.Center - pier.Center, pier.Right)) == side)
            .ToArray();
    }

    /// <summary>Alt or Ctrl widens a pedestal change from one berth to the whole side.</summary>
    private static bool WholeSideWanted(InputModifiers modifiers) =>
        (modifiers & (InputModifiers.Alt | InputModifiers.Control)) != 0;

    /// <summary>
    /// Gives a pier another id, which its berths and dividers follow, and records the change for <see cref="Undo"/>.
    /// Returns the pier under its new id.
    /// </summary>
    /// <param name="pierId">The pier to move.</param>
    /// <param name="newPierId">Its new id. Leading and trailing spaces are dropped.</param>
    /// <exception cref="KeyNotFoundException">No pier has this id.</exception>
    /// <exception cref="InvalidOperationException">Another pier already has the new id.</exception>
    /// <remarks>
    /// The berths named after the pier come too: a berth called <c>A-L01</c> on pier <c>A</c> becomes <c>B-L01</c>
    /// when the pier becomes <c>B</c>, which is what the names are for. A berth whose name was typed by hand, and so
    /// does not start with the pier id, is left alone, as is one whose new name is already taken. The whole move is
    /// one step for <see cref="Undo"/>.
    /// </remarks>
    public Pier ChangePierId(string pierId, string newPierId)
    {
        ArgumentNullException.ThrowIfNull(pierId);
        var before = _marina.GetPier(pierId)?.Id ?? throw new KeyNotFoundException($"Pier '{pierId}' does not exist.");

        // Not batched: a host tracking berths by id needs to hear about every one that moved, and there is one
        // notification per rename either way. Undo still puts the whole move back in a single step.
        var moved = _marina.ChangePierId(before, newPierId);
        if (string.Equals(before, moved.Id, StringComparison.Ordinal)) return moved;

        var action = new DesignAction(Strings.Format(Strings.UndoChangePierId, before, moved.Id));
        action.RenamedPiers.Add((before, moved.Id));
        foreach (var (from, to) in RenameBerthsAfterPier(moved.Id, before)) action.RenamedBerths.Add((from, to));

        // A pier still carrying the name generated for its old id gets the one for its new id.
        if (string.Equals(moved.Name, GeneratedPierName(before), StringComparison.Ordinal))
        {
            _marina.UpdatePier(moved with { Name = GeneratedPierName(moved.Id) });
            action.ChangedPiers.Add(moved);
            moved = _marina.GetPier(moved.Id)!;
        }

        Record(action);
        RaiseStateChanged();
        return moved;
    }

    /// <summary>
    /// Names a pier's berths again from <see cref="BerthNaming"/>, keeping the number each one already has, and
    /// records it for <see cref="Undo"/>. Returns the berths that were renamed, as (old name, new name).
    /// </summary>
    /// <param name="pierId">The pier whose berths to put right.</param>
    /// <remarks>
    /// This is the repair for berths whose names no longer match the pier: one that used to take boats on both sides
    /// and now takes them on one still has the side letter in its berth names, and this takes it out. A berth named
    /// by hand keeps its name.
    /// </remarks>
    /// <exception cref="KeyNotFoundException">No pier has this id.</exception>
    /// <example><code>designer.RenumberBerths("E");   // E-R01 becomes E-01 on a pier that berths to one side</code></example>
    public IReadOnlyList<(string From, string To)> RenumberBerths(string pierId) => RenumberBerths(pierId, null);

    /// <summary>
    /// Names a pier's berths again from a pattern of your own, keeping the number each one already has, and records
    /// it for <see cref="Undo"/>. Returns the berths that were renamed, as (old name, new name).
    /// </summary>
    /// <param name="pierId">The pier whose berths to rename.</param>
    /// <param name="pattern">
    /// The pattern to name them by, in the form <see cref="BerthNamingScheme.Pattern"/> takes; null uses
    /// <see cref="BerthNaming"/> as it stands. The padding follows the names the berths already have.
    /// </param>
    /// <remarks>
    /// A berth whose new name is already taken is left alone rather than overwritten, so a pattern that would give
    /// two berths the same name — dropping <c>{side}</c> from a pier that berths on both — renames neither.
    /// </remarks>
    /// <exception cref="KeyNotFoundException">No pier has this id.</exception>
    /// <example><code>designer.RenumberBerths("E", "{pier}.{number}");   // E-R01 becomes E.01</code></example>
    public IReadOnlyList<(string From, string To)> RenumberBerths(string pierId, string? pattern)
    {
        ArgumentNullException.ThrowIfNull(pierId);
        var pier = _marina.GetPier(pierId) ?? throw new KeyNotFoundException($"Pier '{pierId}' does not exist.");

        // Asked for outright, this puts every numbered berth right, including ones still carrying a prefix from an
        // id the pier had long ago. Only a berth named without a number on the end is taken to be named by hand.
        var renamed = RenameBerthsAfterPier(pier.Id, namedFrom: null, SchemeFor(pier, pattern));
        if (renamed.Count == 0) return renamed;

        var action = new DesignAction(Strings.Format(Strings.UndoRenumberBerths, renamed.Count, pier.Name));
        foreach (var entry in renamed) action.RenamedBerths.Add(entry);
        Record(action);
        RaiseStateChanged();
        return renamed;
    }

    /// <summary>
    /// Builds the names of a pier's berths again from <see cref="BerthNaming"/>, keeping the number each one already
    /// has. Returns every rename that happened, as (old name, new name).
    /// </summary>
    /// <param name="pierId">The pier whose berths are being renamed.</param>
    /// <param name="namedFrom">
    /// The pier id the current names were built from, so berths named after something else are left alone. Null takes
    /// every berth with a running number, which is what an outright renumber wants.
    /// </param>
    /// <param name="scheme">
    /// The scheme to name them by; null uses <see cref="BerthNaming"/>. Giving one also takes every berth on the
    /// pier rather than only those named after <paramref name="namedFrom"/>, since a pattern asked for by name is
    /// meant for the whole pier.
    /// </param>
    /// <remarks>
    /// Going through the scheme rather than swapping the prefix is what drops the side letter from a pier that berths
    /// on one side only: under pier K, <c>K-R07</c> becomes <c>T-07</c> when the pier becomes T. A berth whose name
    /// was not built from the pier id, or has no running number on the end, was named by hand and is left alone.
    /// </remarks>
    private List<(string From, string To)> RenameBerthsAfterPier(string pierId, string? namedFrom, BerthNamingScheme? scheme = null)
    {
        var renamed = new List<(string From, string To)>();
        if (_marina.GetPier(pierId) is not { } pier) return renamed;

        // A pattern asked for by name means the whole pier, whatever its berths happen to be called now.
        var naming = scheme ?? _berthNaming;
        foreach (var berth in _marina.GetBerthsByPier(pierId))
        {
            if (scheme is null && namedFrom is not null && !berth.Id.StartsWith(namedFrom, StringComparison.OrdinalIgnoreCase)) continue;
            if (NumberIn(berth.Id) is not { } number) continue;

            var wanted = naming.Format(pier, SideOf(pier, berth), number);
            if (string.Equals(wanted, berth.Id, StringComparison.Ordinal)) continue;
            if (_marina.GetBerth(wanted) is not null) continue;   // already taken; leave this one as it was

            var repeatedItsId = berth.Label is null || string.Equals(berth.Label, berth.Id, StringComparison.Ordinal);
            var moved = _marina.RenameBerth(berth.Id, wanted);
            renamed.Add((berth.Id, moved.Id));

            // A label that merely repeated the old name follows it; one the user wrote is left alone.
            if (repeatedItsId && moved.Label is not null) _marina.UpdateBerth(moved with { Label = wanted });
        }

        return renamed;
    }

    /// <summary>
    /// The pattern a pier's berths are named by when nothing else is asked for: <c>{pier}-{side}{number}</c>, or
    /// <c>{pier}-{number}</c> on a pier that takes boats on one side only, where there is no other side to tell a
    /// berth apart from.
    /// </summary>
    /// <param name="pierId">The pier.</param>
    /// <exception cref="KeyNotFoundException">No pier has this id.</exception>
    public string DefaultBerthPattern(string pierId)
    {
        ArgumentNullException.ThrowIfNull(pierId);
        var pier = _marina.GetPier(pierId) ?? throw new KeyNotFoundException($"Pier '{pierId}' does not exist.");
        return pier.BerthingSides is PierSides.Left or PierSides.Right ? "{pier}-{number}" : "{pier}-{side}{number}";
    }

    /// <summary>
    /// Works out what naming a pier's berths by a pattern would call each of them, and what would go wrong, without
    /// changing anything.
    /// </summary>
    /// <param name="pierId">The pier whose berths to name.</param>
    /// <param name="pattern">
    /// The pattern, in the form <see cref="BerthNamingScheme.Pattern"/> takes. Null or blank uses
    /// <see cref="DefaultBerthPattern"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// Unlike <see cref="RenumberBerths(string, string?)"/>, which keeps the number each berth already has, this
    /// throws the old names away and counts from <see cref="BerthNamingScheme.StartNumber"/> along the pier. A berth
    /// named by hand is renamed with the rest: asking for a whole pier to be named by a pattern means all of it.
    /// </para>
    /// <para>
    /// Berths are numbered down one side and then the other when the pattern tells the sides apart, and straight
    /// through when it does not, so <c>{pier}-{number}</c> on a pier that berths both sides gives one run of
    /// numbers rather than two sets of the same ones.
    /// </para>
    /// </remarks>
    /// <exception cref="KeyNotFoundException">No pier has this id.</exception>
    /// <example><code>
    /// var plan = designer.PlanBerthNames("A", "{pier}.{side}{number}");
    /// if (!plan.IsClear) Warn(string.Join(", ", plan.Clashes));
    /// else designer.ApplyBerthNames(plan);
    /// </code></example>
    public BerthNamePlan PlanBerthNames(string pierId, string? pattern)
    {
        ArgumentNullException.ThrowIfNull(pierId);
        var pier = _marina.GetPier(pierId) ?? throw new KeyNotFoundException($"Pier '{pierId}' does not exist.");
        var wanted = string.IsNullOrWhiteSpace(pattern) ? DefaultBerthPattern(pier.Id) : pattern.Trim();
        var scheme = _berthNaming with { Pattern = wanted };

        // Down one side and then the other, each side in the order the berths lie along the pier.
        var ordered = _marina.GetBerthsByPier(pier.Id)
            .Select(berth => (Berth: berth, Side: SideOf(pier, berth), Along: Vector2.Dot(berth.Center - pier.Start, pier.Direction)))
            .OrderBy(entry => entry.Side == PierSide.Left ? 0 : 1)
            .ThenBy(entry => entry.Along)
            .ToArray();

        // One run of numbers when the pattern gives both sides the same name, two when it tells them apart.
        var perSide = !string.Equals(
            scheme.Format(pier, PierSide.Left, scheme.StartNumber),
            scheme.Format(pier, PierSide.Right, scheme.StartNumber),
            StringComparison.Ordinal);

        var renames = new List<(string From, string To)>(ordered.Length);
        var counters = new Dictionary<PierSide, int> { [PierSide.Left] = 0, [PierSide.Right] = 0 };
        var running = 0;

        foreach (var entry in ordered)
        {
            var index = perSide ? counters[entry.Side]++ : running++;
            renames.Add((entry.Berth.Id, scheme.Format(pier, entry.Side, scheme.StartNumber + index * scheme.Increment)));
        }

        // A name is a clash when two of these berths want it, or when a berth that is not one of them already has it.
        var mine = new HashSet<string>(renames.Select(rename => rename.From), StringComparer.OrdinalIgnoreCase);
        var clashes = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, to) in renames)
        {
            var taken = _marina.GetBerth(to) is { } other && !mine.Contains(other.Id);
            if ((!seen.Add(to) || taken) && !clashes.Contains(to, StringComparer.OrdinalIgnoreCase)) clashes.Add(to);
        }

        return new BerthNamePlan(pier.Id, wanted, renames, clashes);
    }

    /// <summary>
    /// Applies a plan from <see cref="PlanBerthNames"/>, renaming every berth on the pier in one undoable step.
    /// </summary>
    /// <param name="plan">The plan. It must be clear of clashes.</param>
    /// <remarks>
    /// The berths go to temporary names first and then to the ones asked for, so a pattern that shuffles names
    /// around a pier — every berth moving up one — does not collide with itself half way through.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The plan has clashes, or the pier has gone.</exception>
    /// <returns>Every berth that actually changed name, as (old name, new name).</returns>
    public IReadOnlyList<(string From, string To)> ApplyBerthNames(BerthNamePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.IsClear) throw new InvalidOperationException($"The names {string.Join(", ", plan.Clashes)} are already taken.");
        if (_marina.GetPier(plan.PierId) is null) throw new InvalidOperationException($"Pier '{plan.PierId}' does not exist.");

        var moving = plan.Renames.Where(rename => !string.Equals(rename.From, rename.To, StringComparison.Ordinal)).ToArray();
        if (moving.Length == 0) return Array.Empty<(string, string)>();

        var action = new DesignAction(Strings.Format(Strings.UndoRenumberBerths, moving.Length, plan.PierId));
        var parked = new List<(string Temporary, string To)>(moving.Length);

        // Out of the way first, so nothing is renamed onto a name another of them is still using.
        var stamp = Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < moving.Length; i++)
        {
            var temporary = $"~{stamp}-{i}";
            _marina.RenameBerth(moving[i].From, temporary);
            parked.Add((temporary, moving[i].To));
        }

        foreach (var (temporary, to) in parked) _marina.RenameBerth(temporary, to);
        foreach (var (from, to) in moving) action.RenamedBerths.Add((from, to));

        Record(action);
        RaiseStateChanged();
        return moving;
    }

    /// <summary>
    /// The naming scheme a pattern asked for by the host stands for: the one in use, with that pattern and with the
    /// padding the pier's berths already have, so putting the pattern back unchanged renames nothing.
    /// </summary>
    /// <param name="pier">The pier being renamed.</param>
    /// <param name="pattern">The pattern wanted, or null to use the scheme as it stands.</param>
    private BerthNamingScheme? SchemeFor(Pier pier, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        var digits = InferBerthPattern(pier)?.Digits ?? _berthNaming.NumberDigits;
        return _berthNaming with { Pattern = pattern, NumberDigits = digits };
    }

    /// <summary>
    /// The pattern a pier's berths are named by now, read back out of the first one that carries a running number.
    /// Null when the pier has no berths, or none of them was named from a pattern.
    /// </summary>
    /// <param name="pier">The pier to look at.</param>
    private (string Pattern, int Digits)? InferBerthPattern(Pier pier)
    {
        foreach (var berth in _marina.GetBerthsByPier(pier.Id))
        {
            if (_berthNaming.Infer(pier, SideOf(pier, berth), berth.Id) is { } found) return found;
        }

        return null;
    }

    /// <summary>Which side of its pier a berth lies on, from where it actually is.</summary>
    private static PierSide SideOf(Pier pier, Berth berth) =>
        Vector2.Dot(berth.Center - pier.Start, pier.Right) < 0f ? PierSide.Left : PierSide.Right;

    /// <summary>The running number at the end of a generated berth name, or null when there is none.</summary>
    private static int? NumberIn(string berthId)
    {
        var digits = berthId.Length;
        while (digits > 0 && char.IsAsciiDigit(berthId[digits - 1])) digits--;
        return digits < berthId.Length && int.TryParse(berthId[digits..], out var number) ? number : null;
    }

    /// <summary>
    /// Removes every berth on a pier, and the separators that only served them, leaving the pier itself in place.
    /// This is what the eraser does when Alt is held over one of the pier's berths. Records one step for
    /// <see cref="Undo"/> and raises <see cref="ElementErased"/> with the pier as the element.
    /// </summary>
    /// <param name="pierId">The pier to clear.</param>
    /// <returns>False when no pier has this id, or it had no berths.</returns>
    public bool EraseBerthsOfPier(string pierId)
    {
        ArgumentNullException.ThrowIfNull(pierId);
        if (_marina.GetPier(pierId) is not { } pier) return false;

        var berths = _marina.GetBerthsByPier(pier.Id);
        if (berths.Count == 0) return false;

        var dividers = OrphanedDividers(berths);
        using (_marina.BeginUpdate())
        {
            foreach (var berth in berths) _marina.RemoveBerth(berth.Id);
            foreach (var divider in dividers) _marina.RemoveDivider(divider.Id);
        }

        var action = new DesignAction(Strings.Format(Strings.UndoEraseBerthsOfPier, berths.Count, pier.Name));
        action.RemovedBerths.AddRange(berths);
        action.RemovedDividers.AddRange(dividers);
        Record(action);

        _eraseTarget = null;
        _marina.MarkSceneDirty();
        ElementErased?.Invoke(this, new DesignElementErasedEventArgs(pier, berths, dividers));
        RaiseStateChanged();
        return true;
    }

    /// <summary>
    /// Gives one berth another name, keeping everything else about it, and records the change for <see cref="Undo"/>.
    /// Returns the renamed berth.
    /// </summary>
    /// <param name="berthId">The berth to rename.</param>
    /// <param name="newBerthId">Its new name. Leading and trailing spaces are dropped.</param>
    /// <exception cref="KeyNotFoundException">No berth has this id.</exception>
    /// <exception cref="InvalidOperationException">Another berth already has the new name.</exception>
    /// <remarks>
    /// The name is also the berth's id, which a host application may store against a contract; see
    /// <see cref="IMarinaVisualizer.RenameBerth"/>.
    /// </remarks>
    public Berth RenameBerth(string berthId, string newBerthId)
    {
        ArgumentNullException.ThrowIfNull(berthId);
        var before = _marina.GetBerth(berthId)?.Id ?? throw new KeyNotFoundException($"Berth '{berthId}' does not exist.");
        var renamed = _marina.RenameBerth(before, newBerthId);
        if (!string.Equals(before, renamed.Id, StringComparison.Ordinal))
        {
            var action = new DesignAction(Strings.Format(Strings.UndoRenameBerth, before, renamed.Id));
            action.RenamedBerths.Add((before, renamed.Id));
            Record(action);
            RaiseStateChanged();
        }

        return renamed;
    }

    /// <summary>
    /// Gives a pier a display name (<see cref="Pier.Name"/>), the one shown in tooltips and the camera preset, and records
    /// the change for <see cref="Undo"/>. The pier's id, and the berth names built from it, stay as they are.
    /// Returns the renamed pier.
    /// </summary>
    /// <param name="pierId">The pier to name.</param>
    /// <param name="name">Its new name; names need not be unique. Leading and trailing spaces are dropped.</param>
    /// <exception cref="KeyNotFoundException">No pier has this id.</exception>
    /// <example><code>designer.RenamePier("A", "West pontoon");</code></example>
    public Pier RenamePier(string pierId, string name)
    {
        ArgumentNullException.ThrowIfNull(pierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var existing = _marina.GetPier(pierId) ?? throw new KeyNotFoundException($"Pier '{pierId}' does not exist.");
        name = name.Trim();
        if (string.Equals(existing.Name, name, StringComparison.Ordinal)) return existing;

        _marina.UpdatePier(existing with { Name = name });
        var renamed = _marina.GetPier(existing.Id)!;
        Record(new DesignAction(Strings.Format(Strings.UndoRenamePier, existing.Name, renamed.Name)) { ChangedPiers = { existing } });
        RaiseStateChanged();
        return renamed;
    }

    /// <summary>True when the modifiers ask for the whole row rather than the one thing under the pointer.</summary>
    /// <param name="modifiers">The modifier keys held.</param>
    private static bool WholeRowWanted(InputModifiers modifiers) => (modifiers & InputModifiers.Alt) != 0;

    /// <summary>
    /// Asks the host for a new name through <see cref="ElementRenaming"/> and applies it. Used by
    /// <see cref="DesignTool.Rename"/>; does nothing without a handler, or when the handler cancels or leaves
    /// everything as it was.
    /// </summary>
    /// <param name="element">A <see cref="Domain.Berth"/> or a <see cref="Domain.Pier"/>; anything else is ignored.</param>
    /// <param name="wholeRow">
    /// True to rename every berth on the clicked berth's pier by a pattern, rather than the one berth. Ignored when
    /// a pier was clicked, which always asks about the pier.
    /// </param>
    private void AskToRename(object element, bool wholeRow)
    {
        if (ElementRenaming is null) return;

        // A berth with Alt held means its whole row: one pattern for the pier rather than one name for one berth.
        if (wholeRow && element is Berth swept && swept.PierId is { } sweptPier && _marina.GetPier(sweptPier) is { } sweptOwner)
        {
            element = sweptOwner;
        }
        else
        {
            wholeRow = false;
        }

        var args = element switch
        {
            Berth berth => new DesignElementRenamingEventArgs(berth, null, berth.Id),
            Pier pier => new DesignElementRenamingEventArgs(
                wholeRow ? _marina.GetBerthsByPier(pier.Id).FirstOrDefault() : null,
                pier,
                pier.Name,
                wholeRow ? DefaultBerthPattern(pier.Id) : InferBerthPattern(pier)?.Pattern ?? _berthNaming.Pattern,
                wholeRow ? DesignRenameScope.BerthsOfPier : DesignRenameScope.Element),
            _ => null,
        };

        if (args is null) return;
        ElementRenaming(this, args);

        // A whole-row rename touches nothing but the berth names, so the pier keeps its own name and id.
        if (args.Scope == DesignRenameScope.BerthsOfPier)
        {
            var pattern = args.NewBerthPattern?.Trim();
            if (args.Cancel || args.Pier is not { } target || string.IsNullOrEmpty(pattern)) return;

            // The host is expected to have checked the plan; one that still clashes is left alone rather than
            // half-applied.
            var plan = PlanBerthNames(target.Id, pattern);
            if (plan.IsClear) ApplyBerthNames(plan);
            return;
        }

        var nameChanged = !string.IsNullOrWhiteSpace(args.NewName) && !string.Equals(args.NewName.Trim(), args.CurrentName, StringComparison.Ordinal);
        var idChanged = args.Pier is { } owner && args.NewPierId is { } wanted && !string.Equals(wanted.Trim(), owner.Id, StringComparison.Ordinal);
        var patternChanged = args.Pier is not null && !string.IsNullOrWhiteSpace(args.NewBerthPattern)
            && !string.Equals(args.NewBerthPattern.Trim(), args.BerthPattern, StringComparison.Ordinal);
        if (args.Cancel || (!nameChanged && !idChanged && !patternChanged)) return;
        if (!nameChanged) args.NewName = args.CurrentName;

        try
        {
            if (args.Berth is { } target)
            {
                RenameBerth(target.Id, args.NewName);
            }
            else if (args.Pier is { } pier)
            {
                // The id moves first, so the display name is applied to the pier under its new id.
                var wantedId = args.NewPierId?.Trim();
                var moving = !string.IsNullOrEmpty(wantedId) && !string.Equals(wantedId, pier.Id, StringComparison.Ordinal);

                // The id moves first, so the display name is applied to the pier under its new id.
                var current = moving ? ChangePierId(pier.Id, wantedId!).Id : pier.Id;
                RenamePier(current, args.NewName);

                // A pattern of the host's own goes over the whole pier, including the berths the id move just
                // renamed under the old one. Otherwise moving the id has already put the names right.
                if (patternChanged) RenumberBerths(current, args.NewBerthPattern!.Trim());
                else if (!moving) RenumberBerths(current);
            }
        }
        catch (InvalidOperationException)
        {
            // The name or id is taken; the host can offer another one on the next click.
        }
        catch (ArgumentException)
        {
            // An empty id: same story.
        }
    }

    /// <summary>
    /// Removes a berth, or a pier or land area with its berths, and raises <see cref="ElementErased"/>. Dividers left without a berth on
    /// either side go too (a pier takes all of its dividers). Returns false when the element doesn't exist.
    /// </summary>
    /// <param name="element">A <see cref="Berth"/>, <see cref="Pier"/> or <see cref="LandArea"/> (matched by id).</param>
    public bool Erase(object element)
    {
        ArgumentNullException.ThrowIfNull(element);
        IReadOnlyList<Berth> removedBerths;
        IReadOnlyList<Divider> removedDividers = Array.Empty<Divider>();
        Pier? removedPier = null;
        LandArea? removedLand = null;
        switch (element)
        {
            case Berth berth when _marina.GetBerth(berth.Id) is { } current:
                removedBerths = new[] { current };
                removedDividers = OrphanedDividers(removedBerths);
                using (_marina.BeginUpdate())
                {
                    _marina.RemoveBerth(current.Id);
                    foreach (var divider in removedDividers) _marina.RemoveDivider(divider.Id);
                }

                element = current;
                break;
            case Pier pier when _marina.GetPier(pier.Id) is { } current:
                removedBerths = _marina.GetBerthsByPier(current.Id);
                // The pier takes its own dividers with it; separators that belonged to no pier but only served its berths go too.
                var strays = OrphanedDividers(removedBerths)
                    .Where(divider => !string.Equals(divider.PierId, current.Id, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                removedDividers = _marina.GetDividersByPier(current.Id).Concat(strays).ToArray();
                removedPier = current;
                using (_marina.BeginUpdate())
                {
                    _marina.RemovePier(current.Id); // Removes its berths and dividers too.
                    foreach (var divider in strays) _marina.RemoveDivider(divider.Id);
                }

                element = current;
                break;
            case LandArea land when _marina.GetLandArea(land.Id) is { } current:
                removedBerths = _marina.GetBerthsByLandArea(current.Id);
                removedLand = current;
                _marina.RemoveLandArea(current.Id);
                element = current;
                break;
            default:
                return false;
        }

        var action = new DesignAction(Strings.Format(Strings.UndoErase, DescribeElement(element)));
        if (removedPier is not null) action.RemovedPiers.Add(removedPier);
        if (removedLand is not null) action.RemovedLandAreas.Add(removedLand);
        action.RemovedBerths.AddRange(removedBerths);
        action.RemovedDividers.AddRange(removedDividers);
        Record(action);

        _eraseTarget = null;
        _marina.MarkSceneDirty();
        ElementErased?.Invoke(this, new DesignElementErasedEventArgs(element, removedBerths, removedDividers));
        RaiseStateChanged();
        return true;
    }

    /// <summary>
    /// The dividers along <paramref name="doomedBerths"/> that no other berth uses: the separators of the berths about to go. A divider
    /// shared with a berth that stays is kept, so erasing one of two neighbours leaves the pier between them standing.
    /// </summary>
    private IReadOnlyList<Divider> OrphanedDividers(IReadOnlyList<Berth> doomedBerths)
    {
        var doomedIds = new HashSet<string>(doomedBerths.Select(berth => berth.Id), StringComparer.OrdinalIgnoreCase);
        var survivors = _marina.GetBerths().Where(berth => !doomedIds.Contains(berth.Id)).ToList();
        var result = new List<Divider>();
        foreach (var divider in _marina.GetDividers())
        {
            if (!doomedBerths.Any(berth => Separates(divider, berth))) continue;
            if (survivors.Any(berth => Separates(divider, berth))) continue;
            result.Add(divider);
        }

        return result;
    }

    /// <summary>True when the divider runs along one of the berth's long sides, as the generated separators do.</summary>
    private static bool Separates(Divider divider, Berth berth)
    {
        if (berth.IsOnLand || (divider.PierId is { } pierId && !string.Equals(pierId, berth.PierId, StringComparison.OrdinalIgnoreCase))) return false;
        var offset = divider.Center - berth.Center;
        var across = MathF.Abs(Vector2.Dot(offset, berth.Right));
        var along = MathF.Abs(Vector2.Dot(offset, berth.Forward));
        return MathF.Abs(across - berth.Width * 0.5f) <= SeparatorTolerance && along <= berth.Length * 0.6f &&
            MathF.Abs(Vector2.Dot(divider.Direction, berth.Right)) < 0.35f;
    }

    // ---- History --------------------------------------------------------------------------------

    /// <summary>True when <see cref="Undo"/> would revert something.</summary>
    public bool CanUndo => _history.Count > 0;

    /// <summary>What the next <see cref="Undo"/> would revert (e.g. "Add 4 berths"), or null when there is nothing to undo.</summary>
    public string? UndoDescription => _history.Count > 0 ? _history[^1].Description : null;

    /// <summary>How many changes can still be undone (at most <see cref="MaxUndoSteps"/>).</summary>
    public int UndoCount => _history.Count;

    /// <summary>
    /// Reverts the last change the designer made: a drawn land area, pier, berth row or land berth is removed again, erased elements
    /// come back with their dividers, and trees are restored. Raises <see cref="ActionUndone"/> (and the usual <c>LayoutChanged</c>
    /// notifications). Returns false when there is nothing to undo.
    /// </summary>
    /// <remarks>
    /// Only the designer's own changes are recorded, and everything the host changed through the normal API in between stays as it
    /// is. Loading or clearing a layout empties the history. A restored berth is no longer part of a multi-berth.
    /// </remarks>
    public bool Undo()
    {
        if (_history.Count == 0) return false;

        var action = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        _undoing = true;
        try
        {
            using (_marina.BeginUpdate())
            {
                foreach (var berth in action.AddedBerths) _marina.RemoveBerth(berth.Id);
                foreach (var divider in action.AddedDividers) _marina.RemoveDivider(divider.Id);
                foreach (var pier in action.AddedPiers) _marina.RemovePier(pier.Id);
                foreach (var land in action.AddedLandAreas) _marina.RemoveLandArea(land.Id);

                // Containers first: berths and dividers reference them.
                foreach (var land in action.RemovedLandAreas.Where(land => _marina.GetLandArea(land.Id) is null)) _marina.AddLandArea(land);
                foreach (var pier in action.RemovedPiers.Where(pier => _marina.GetPier(pier.Id) is null)) _marina.AddPier(pier);
                foreach (var divider in action.RemovedDividers.Where(divider => _marina.GetDivider(divider.Id) is null)) _marina.AddDivider(divider);
                foreach (var berth in action.RemovedBerths.Where(berth => _marina.GetBerth(berth.Id) is null)) _marina.AddBerth(berth);

                foreach (var land in action.ChangedLandAreas.Where(land => _marina.GetLandArea(land.Id) is not null)) _marina.UpdateLandArea(land);
                foreach (var pier in action.ChangedPiers.Where(pier => _marina.GetPier(pier.Id) is not null)) _marina.UpdatePier(pier);
                foreach (var berth in action.ChangedBerths.Where(berth => _marina.GetBerth(berth.Id) is not null)) _marina.UpdateBerth(berth);

                foreach (var (from, to) in action.RenamedBerths.Where(r => _marina.GetBerth(r.To) is not null)) _marina.RenameBerth(to, from);
                foreach (var (from, to) in action.RenamedPiers.Where(r => _marina.GetPier(r.To) is not null)) _marina.ChangePierId(to, from);

                if (action.TouchedShoreline) _marina.SetShoreline(action.PreviousShoreline);
            }
        }
        finally
        {
            _undoing = false;
        }

        _eraseTarget = null;
        _marina.MarkSceneDirty();
        ActionUndone?.Invoke(this, new DesignActionUndoneEventArgs(action.Description, _history.Count));
        RaiseStateChanged();
        return true;
    }

    /// <summary>Forgets every recorded change, so <see cref="Undo"/> does nothing until the designer changes something again.</summary>
    public void ClearHistory()
    {
        if (_history.Count == 0) return;
        _history.Clear();
        RaiseStateChanged();
    }

    /// <summary>Pushes a change onto the undo stack (not while undoing, and not for changes that touched nothing).</summary>
    private void Record(DesignAction action)
    {
        if (_undoing || action.IsEmpty) return;
        _history.Add(action);
        if (_history.Count > MaxUndoSteps) _history.RemoveAt(0);
    }

    // ---- Reference image ------------------------------------------------------------------------

    /// <summary>The image shown to trace the marina, or null.</summary>
    public ReferenceImage? ReferenceImage => _image;

    /// <summary>Center of the reference image in plan coordinates.</summary>
    public Vector2 ReferenceImageCenter
    {
        get => _imageCenter;
        set
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y)) throw new ArgumentOutOfRangeException(nameof(value), value, "The center must be finite.");
            if (value == _imageCenter) return;

            // The line was measured on the picture, so it goes where the picture goes.
            var moved = value - _imageCenter;
            if (_scaleLine is { } line) _scaleLine = (line.Start + moved, line.End + moved);

            _imageCenter = value;
            OnImageChanged(ReferenceImageChange.Moved);
        }
    }

    /// <summary>Ground size of one image pixel, in meters. Set it directly or with <see cref="CalibrateReferenceImage(float)"/>.</summary>
    public float ReferenceImageMetersPerPixel
    {
        get => _imageMetersPerPixel;
        set
        {
            var checkedValue = RequireRange(value, 1e-4f, 1000f);
            if (checkedValue == _imageMetersPerPixel) return;
            _imageMetersPerPixel = checkedValue;
            OnImageChanged(ReferenceImageChange.Scaled);
        }
    }

    /// <summary>Opacity of the reference image, 0–1 (default 0.6).</summary>
    public float ReferenceImageOpacity
    {
        get => _imageOpacity;
        set
        {
            var clamped = Math.Clamp(float.IsFinite(value) ? value : 0.6f, 0f, 1f);
            if (clamped == _imageOpacity) return;
            _imageOpacity = clamped;
            OnImageChanged(ReferenceImageChange.AppearanceChanged);
        }
    }

    /// <summary>Show the reference image (default true). It is shown whether or not the designer is active.</summary>
    public bool ReferenceImageVisible
    {
        get => _imageVisible;
        set
        {
            if (value == _imageVisible) return;
            _imageVisible = value;
            OnImageChanged(ReferenceImageChange.AppearanceChanged);
        }
    }

    /// <summary>
    /// True (default): the image is drawn over land, piers and boats, so it stays visible while tracing. False: it lies on the water
    /// and land and structures hide it. Drawing previews are always on top.
    /// </summary>
    public bool ReferenceImageAboveScene
    {
        get => _imageAboveScene;
        set
        {
            if (value == _imageAboveScene) return;
            _imageAboveScene = value;
            OnImageChanged(ReferenceImageChange.AppearanceChanged);
        }
    }

    /// <summary>Ground size of the reference image in meters (X = east–west, Y = north–south), or zero without an image.</summary>
    public Vector2 ReferenceImageSize => _image is null ? Vector2.Zero : new Vector2(_image.PixelWidth, _image.PixelHeight) * _imageMetersPerPixel;

    /// <summary>The last line drawn with <see cref="DesignTool.MeasureScale"/>, or null.</summary>
    /// <remarks>It is measured on the picture, so it moves and scales with it, and is not saved to a marina file.</remarks>
    public (Vector2 Start, Vector2 End)? ScaleLine => _scaleLine;

    /// <summary>
    /// Forgets the measuring line, once the image has been scaled by it and the line is only in the way.
    /// Returns false when there was none.
    /// </summary>
    public bool ClearScaleLine()
    {
        if (_scaleLine is null) return false;
        _scaleLine = null;
        _marina.MarkSceneDirty();
        RaiseStateChanged();
        return true;
    }

    /// <summary>
    /// Shows an image to trace (north at the top). Without <paramref name="metersPerPixel"/> it is sized to cover the current layout
    /// (at least 300 m wide) until calibrated; without <paramref name="center"/> it is centered on the camera target.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="metersPerPixel">Known ground size of a pixel, e.g. from map metadata.</param>
    /// <param name="center">Plan position of the image center.</param>
    public void SetReferenceImage(ReferenceImage image, float? metersPerPixel = null, Vector2? center = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        var layout = _marina.GetLayout();
        var hasContent = layout.Piers.Count + layout.LandAreas.Count + layout.Berths.Count > 0;
        var (min, max) = layout.ComputeBounds();
        var extent = hasContent ? MathF.Max(max.X - min.X, max.Y - min.Y) : 0f;

        _image = image;
        _imageMetersPerPixel = metersPerPixel is { } mpp ? RequireRange(mpp, 1e-4f, 1000f) : MathF.Max(extent * 1.2f, 300f) / MathF.Max(image.PixelWidth, image.PixelHeight);
        var target = _marina.Camera.DesiredPose.Target;
        _imageCenter = center ?? new Vector2(target.X, target.Z);
        _scaleLine = null;
        OnImageChanged(ReferenceImageChange.Set);
    }

    /// <summary>Removes the reference image and the scale line.</summary>
    public void ClearReferenceImage()
    {
        if (_image is null) return;
        _image = null;
        _scaleLine = null;
        _imageDragLast = null;
        OnImageChanged(ReferenceImageChange.Cleared);
    }

    /// <summary>
    /// Rescales the reference image so the last <see cref="ScaleLine"/> becomes <paramref name="knownLengthMeters"/> long. The image scales
    /// about the line's first end, which stays put. Returns false without an image or scale line.
    /// </summary>
    /// <param name="knownLengthMeters">The real length of the scale bar the line was drawn over.</param>
    /// <exception cref="ArgumentOutOfRangeException">The length is not positive.</exception>
    public bool CalibrateReferenceImage(float knownLengthMeters) =>
        _scaleLine is { } line && CalibrateReferenceImage(line.Start, line.End, knownLengthMeters);

    /// <summary>
    /// Rescales the reference image so the segment <paramref name="start"/>–<paramref name="end"/> (in plan coordinates, drawn over the
    /// image at its current scale) becomes <paramref name="knownLengthMeters"/> long. <paramref name="start"/> stays put.
    /// </summary>
    /// <returns>False without an image or when the segment has no length.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The length is not positive.</exception>
    public bool CalibrateReferenceImage(Vector2 start, Vector2 end, float knownLengthMeters)
    {
        if (!(knownLengthMeters > 0f) || !float.IsFinite(knownLengthMeters))
        {
            throw new ArgumentOutOfRangeException(nameof(knownLengthMeters), knownLengthMeters, "The length must be positive.");
        }

        var measured = Vector2.Distance(start, end);
        if (_image is null || measured < 1e-4f) return false;

        var factor = knownLengthMeters / measured;
        _imageMetersPerPixel = Math.Clamp(_imageMetersPerPixel * factor, 1e-4f, 1000f);
        _imageCenter = start + (_imageCenter - start) * factor;
        _scaleLine = (start, start + (end - start) * factor);
        OnImageChanged(ReferenceImageChange.Scaled);
        return true;
    }

    /// <summary>Plan-view bounds of the reference image (north-west and south-east corners), or null without an image.</summary>
    public (Vector2 Min, Vector2 Max)? ReferenceImageBounds
    {
        get
        {
            if (_image is null) return null;
            var half = ReferenceImageSize * 0.5f;
            return (_imageCenter - half, _imageCenter + half);
        }
    }

    // ---- Camera ---------------------------------------------------------------------------------

    /// <summary>
    /// Looks straight down with north (−Z) at the top of the view, as aerial images are shown: the best angle to trace an image.
    /// Keeps the camera target and distance.
    /// </summary>
    public void ViewTopDown(bool immediate = false)
    {
        var pose = _marina.Camera.DesiredPose;
        _marina.Camera.SetPose(pose with { YawDegrees = 0f, PitchDegrees = 89f }, immediate);
    }

    /// <summary>Looks straight down, north up, at the whole reference image. Returns false without an image.</summary>
    public bool FocusReferenceImage(bool immediate = false)
    {
        if (ReferenceImageBounds is not { } bounds) return false;
        var size = bounds.Max - bounds.Min;
        var aspect = _marina.ViewportSize.X / MathF.Max(1f, _marina.ViewportSize.Y);
        var tan = MathF.Tan(_marina.Camera.FieldOfViewDegrees * MarinaMath.DegToRad * 0.5f);
        var distance = MathF.Max(size.Y * 0.5f / tan, size.X * 0.5f / (tan * aspect)) * 1.08f;
        _marina.Camera.SetPose(new CameraPose(MarinaMath.ToWorld(_imageCenter), 0f, 89f, MathF.Max(10f, distance)), immediate);
        return true;
    }

    // ---- Input (called by MarinaInputController) ------------------------------------------------

    /// <summary>
    /// The smallest north-up box around what is being dragged with <see cref="DesignTool.SelectArea"/>, or null.
    /// </summary>
    /// <remarks>
    /// The box the user drags follows the camera rather than the compass, so this is only its extent.
    /// <see cref="SelectionQuad"/> has the corners to draw.
    /// </remarks>
    public (Vector2 Min, Vector2 Max)? SelectionBox =>
        SelectionQuad is { } quad
            ? (quad.Aggregate(Vector2.Min), quad.Aggregate(Vector2.Max))
            : null;

    /// <summary>
    /// The four corners of the box being dragged with <see cref="DesignTool.SelectArea"/>, in plan coordinates, or
    /// null when nothing is being dragged.
    /// </summary>
    /// <remarks>
    /// Its sides run with the camera, so dragging across the screen selects what the box appeared to cover, whichever
    /// way the view happens to be turned. The heading is taken when the drag starts, so turning the camera part way
    /// through does not reshape what has already been swept.
    /// </remarks>
    public IReadOnlyList<Vector2>? SelectionQuad
    {
        get
        {
            if (_selectFrom is not { } from || _selectTo is not { } to) return null;
            var (right, forward) = ViewAxes(_selectHeading);
            var (minU, maxU) = Extent(Vector2.Dot(from, right), Vector2.Dot(to, right));
            var (minV, maxV) = Extent(Vector2.Dot(from, forward), Vector2.Dot(to, forward));
            return new[]
            {
                right * minU + forward * minV,
                right * maxU + forward * minV,
                right * maxU + forward * maxV,
                right * minU + forward * maxV,
            };
        }
    }

    /// <summary>
    /// Selects every berth whose middle lies inside a north-up box in plan coordinates. Kept for code that wants a
    /// box in compass terms; the tool itself uses the overload that takes a heading.
    /// </summary>
    /// <param name="from">One corner of the box.</param>
    /// <param name="to">The opposite corner.</param>
    /// <param name="add">True to add to the selection already made, false to replace it.</param>
    /// <returns>The berths now selected.</returns>
    public IReadOnlyList<string> SelectBerthsInArea(Vector2 from, Vector2 to, bool add = false) =>
        SelectBerthsInArea(from, to, 0f, add);

    /// <summary>
    /// Selects every berth whose middle lies inside a box whose sides run along <paramref name="headingDegrees"/>.
    /// This is what <see cref="DesignTool.SelectArea"/> does when the drag ends, using the camera heading.
    /// </summary>
    /// <param name="from">One corner of the box.</param>
    /// <param name="to">The opposite corner.</param>
    /// <param name="headingDegrees">Which way the top of the box points; 0 is north.</param>
    /// <param name="add">True to add to the selection already made, false to replace it.</param>
    /// <returns>The berths now selected.</returns>
    public IReadOnlyList<string> SelectBerthsInArea(Vector2 from, Vector2 to, float headingDegrees, bool add = false)
    {
        var (right, forward) = ViewAxes(headingDegrees);
        var (minU, maxU) = Extent(Vector2.Dot(from, right), Vector2.Dot(to, right));
        var (minV, maxV) = Extent(Vector2.Dot(from, forward), Vector2.Dot(to, forward));

        var inside = _marina.GetBerths()
            .Where(berth => berth.IsInteractive)
            .Where(berth =>
            {
                var u = Vector2.Dot(berth.Center, right);
                var v = Vector2.Dot(berth.Center, forward);
                return u >= minU && u <= maxU && v >= minV && v <= maxV;
            })
            .Select(berth => berth.Id);

        var ids = add ? _marina.SelectedBerths.Select(berth => berth.Id).Concat(inside).Distinct(StringComparer.OrdinalIgnoreCase) : inside;
        return _marina.SetSelection(ids.ToArray()).SelectedBerthIds;
    }

    /// <summary>The plan directions the sides of a box run along, for a box turned to the given heading.</summary>
    private static (Vector2 Right, Vector2 Forward) ViewAxes(float headingDegrees) =>
        (MarinaMath.HeadingToRight(headingDegrees), MarinaMath.HeadingToDirection(headingDegrees));

    private static (float Min, float Max) Extent(float a, float b) => a <= b ? (a, b) : (b, a);

    /// <summary>True when a left drag belongs to a tool — moving the picture or dragging a selection box — not the camera.</summary>
    internal bool CapturesDrag(PointerButton button) =>
        _active && button == PointerButton.Left &&
        (_tool == DesignTool.SelectArea || (_tool == DesignTool.MoveReferenceImage && _image is not null));

    internal void BeginImageDrag(float x, float y)
    {
        if (_tool == DesignTool.SelectArea)
        {
            _selectFrom = GroundPoint(x, y);
            _selectTo = _selectFrom;
            _selectModifiers = _modifiers;
            _selectHeading = _marina.Camera.Pose.YawDegrees;
            return;
        }

        _imageDragLast = GroundPoint(x, y);
    }

    internal void DragImage(float x, float y)
    {
        if (_tool == DesignTool.SelectArea)
        {
            if (GroundPoint(x, y) is { } corner) _selectTo = corner;
            _marina.MarkSceneDirty();
            return;
        }

        if (_image is null || _imageDragLast is not { } last || GroundPoint(x, y) is not { } current) return;
        _imageDragLast = current;
        ReferenceImageCenter = _imageCenter + (current - last);
    }

    internal void EndImageDrag()
    {
        _imageDragLast = null;
        if (_selectFrom is { } from && _selectTo is { } to)
        {
            var dragged = Vector2.Distance(from, to) > 0.5f;
            if (dragged) SelectBerthsInArea(from, to, _selectHeading, (_selectModifiers & (InputModifiers.Shift | InputModifiers.Control)) != 0);
        }

        _selectFrom = null;
        _selectTo = null;
        _marina.MarkSceneDirty();
    }

    /// <summary>
    /// Tells the designer which modifier keys are held, without the pointer having moved. Returns true when it
    /// redrew because of it.
    /// </summary>
    /// <remarks>
    /// Alt changes what the tools are about to do — the eraser takes the whole row rather than one berth, a point
    /// stops snapping — and the preview has to say so the moment the key goes down, not the next time the pointer
    /// happens to move. Without this the marina sits there showing the wrong thing while the user holds the key
    /// still and wonders whether it worked.
    /// </remarks>
    /// <param name="modifiers">The modifier keys now held.</param>
    internal bool SetModifiers(InputModifiers modifiers)
    {
        if (_modifiers == modifiers) return false;

        var before = _modifiers;
        _modifiers = modifiers;
        if (!_active || !ShowsModifiers(before ^ modifiers)) return false;

        _marina.MarkSceneDirty();
        return true;
    }

    /// <summary>Whether a change in these modifiers would show in the preview of the tool in hand.</summary>
    private bool ShowsModifiers(InputModifiers changed) => _tool switch
    {
        // Alt sweeps the whole row, or the whole side of the pier.
        DesignTool.Erase or DesignTool.Rename or DesignTool.EditServices => (changed & InputModifiers.Alt) != 0,

        // Alt turns snapping off and Shift squares the angle up, both of which move the preview.
        DesignTool.DrawLandArea or DesignTool.DrawShoreline or DesignTool.DrawPier or DesignTool.AddBerths
            or DesignTool.AddLandBerths or DesignTool.MoveReferenceImage or DesignTool.MeasureScale =>
            (changed & (InputModifiers.Alt | InputModifiers.Shift)) != 0,

        _ => false,
    };

    internal void HandlePointerMove(float x, float y, InputModifiers modifiers)
    {
        _modifiers = modifiers;
        UpdatePointer(x, y);
        if (_tool is DesignTool.Erase or DesignTool.Rename or DesignTool.EditServices) _eraseTarget = FindEraseTarget(x, y);
        if (_tool == DesignTool.PlantTrees) _eraseTarget = FindLandUnderPointer(x, y, LandKind.Grass);
        if (_tool == DesignTool.AddLandBerths) _eraseTarget = _berthLandId is null ? FindLandUnderPointer(x, y) : _marina.GetLandArea(_berthLandId);
        _marina.MarkSceneDirty();
    }

    internal void HandlePointerLeave()
    {
        _pointer = null;
        _eraseTarget = null;
        _marina.MarkSceneDirty();
    }

    internal void HandleClick(float x, float y, PointerButton button, InputModifiers modifiers)
    {
        HandlePointerMove(x, y, modifiers);

        if (button == PointerButton.Right)
        {
            // The tree tool has no drawing to finish, so a right-click clears the trees instead.
            if (_tool == DesignTool.PlantTrees) RemoveTreesUnderPointer(x, y);
            else if (!CompleteDraft()) CancelDraft();
            return;
        }

        if (button != PointerButton.Left) return;

        switch (_tool)
        {
            case DesignTool.DrawLandArea:
                ClickLandArea(x, y);
                break;

            case DesignTool.DrawPier when _pointer is { } p:
                if (_points.Count == 0) AddPoint(p);
                else if (Vector2.Distance(_points[0], p) >= MinimumPierLength) TryCreate(() => CreatePier(_points[0], p) is not null);
                break;

            case DesignTool.AddBerths when _pointer is { } p:
                ClickBerths(p);
                break;

            case DesignTool.DrawShoreline when _pointer is { } p:
                // Points first; once Enter has settled the line, the click says which side is the land.
                if (_shorelineSide is null) AddPoint(p);
                else TryCreate(() => PickShorelineSide(p) is not null);
                break;

            case DesignTool.AddLandBerths when _pointer is { } p:
                ClickLandBerth(x, y, p);
                break;

            case DesignTool.Erase:
                // Alt over a berth clears the pier it belongs to, rather than that one berth.
                if (FindEraseTarget(x, y) is { } target)
                {
                    if ((modifiers & InputModifiers.Alt) != 0 && target is Berth { PierId: { } owner }) EraseBerthsOfPier(owner);
                    else Erase(target);
                }

                break;

            case DesignTool.Rename:
                if (FindEraseTarget(x, y) is { } toRename) AskToRename(toRename, WholeRowWanted(modifiers));
                break;

            case DesignTool.EditServices:
                if (FindEraseTarget(x, y) is Berth berthToServe)
                {
                    SetBerthServices(berthToServe, WholeSideWanted(modifiers));
                }

                break;

            case DesignTool.PlantTrees when (modifiers & InputModifiers.Control) != 0:
                RemoveTreesUnderPointer(x, y);
                break;

            case DesignTool.PlantTrees:
                if (FindLandUnderPointer(x, y, LandKind.Grass) is { } lawn) PlantTrees(lawn.Id);
                break;

            case DesignTool.MeasureScale when _pointer is { } p && _image is not null:
                if (_points.Count == 0)
                {
                    AddPoint(p);
                }
                else if (Vector2.Distance(_points[0], p) > 1e-3f)
                {
                    FinishScaleLine(_points[0], p);
                }

                break;
        }
    }

    internal void HandleDoubleClick(float x, float y, PointerButton button, InputModifiers modifiers)
    {
        if (button == PointerButton.Left && _tool == DesignTool.DrawLandArea) CompleteDraft();
    }

    internal bool HandleKey(MarinaKey key)
    {
        switch (key)
        {
            case MarinaKey.Enter:
                return CompleteDraft();
            case MarinaKey.Backspace:
                return RemoveLastPoint();
            case MarinaKey.Delete:
                return _tool == DesignTool.Erase && _eraseTarget is { } target && Erase(target);
            case MarinaKey.Undo:
                return Undo();
            case MarinaKey.Escape:
                if (CancelDraft()) return true;
                if (_tool == DesignTool.Navigate) return false;
                Tool = DesignTool.Navigate;
                return true;
            default:
                return false;
        }
    }

    // ---- Rendering (called by MarinaVisualizer) -------------------------------------------------

    internal ReferenceImageLayer? BuildImageLayer()
    {
        if (_image is null || !_imageVisible || _imageOpacity <= 0f || ReferenceImageBounds is not { } bounds) return null;
        var height = ShaderSources.MaxWaveHeightFactor * MathF.Abs(_marina.Water.WaveAmplitude) + 0.03f;
        return new ReferenceImageLayer(_image, bounds.Min, bounds.Max, height, _imageOpacity, _imageAboveScene);
    }

    /// <summary>True when the camera moved enough that line widths and text orientation of the overlay should be rebuilt.</summary>
    internal bool OverlayNeedsRefresh()
    {
        if (!_active && !_marina.ShowTrafficLanes) return false;
        var pose = _marina.Camera.Pose;
        return _overlayDistance < 0f
            || MathF.Abs(pose.Distance - _overlayDistance) > _overlayDistance * 0.08f
            || MathF.Abs(MarinaMath.DeltaAngle(pose.YawDegrees, _overlayYaw)) > 20f;
    }

    internal void AppendOverlay(List<RenderObject> output)
    {
        var pose = _marina.Camera.Pose;
        _overlayDistance = pose.Distance;
        _overlayYaw = pose.YawDegrees;
        if (!_active && _scaleLine is null && !_marina.ShowTrafficLanes) return;

        var line = Math.Clamp(pose.Distance * 0.0035f, 0.08f, 4f);
        var textUp = MarinaMath.DirectionToHeading(new Vector2(-MathF.Sin(pose.YawDegrees * MarinaMath.DegToRad), -MathF.Cos(pose.YawDegrees * MarinaMath.DegToRad)));
        // Measurements float above the highest land area (and the land being drawn), so a surface never hides them.
        var textFloor = _marina.GetLandAreas().Select(l => l.Height).Append(_landHeight).Append(Pier.GetDefaultDeckHeight(PierType.Concrete)).Max() + 0.5f;
        var overlay = new Overlay(output, line, textUp, textFloor);

        if (_scaleLine is { } scale && _image is not null && _imageVisible && _imageOpacity > 0.01f && (_active || _tool == DesignTool.MeasureScale))
        {
            var y = ImageDrawHeight() + 0.05f;
            // The line is drawn on the picture, so it fades with it.
            var color = ScaleLineColor with { W = ScaleLineColor.W * _imageOpacity };
            overlay.Line(scale.Start, scale.End, y, color);
            overlay.Dot(scale.Start, y, color);
            overlay.Dot(scale.End, y, color);
        }

        // Where the passing traffic will run. Shown on demand while the traffic settings are being adjusted, and
        // not tied to a tool, so the clearance and the spacing can be set from any view.
        if (_marina.ShowTrafficLanes)
        {
            var y = TrafficLaneHeight();
            foreach (var lane in _marina.TrafficLanes)
            {
                // The two directions are tinted apart, so which way a lane runs can be seen at a glance.
                var color = lane.Reversed ? TrafficLaneBackColor : TrafficLaneColor;
                for (var i = 0; i < lane.Points.Count - 1; i++)
                {
                    overlay.Line(lane.Points[i], lane.Points[i + 1], y, color);
                }
            }
        }

        if (!_active) return;

        switch (_tool)
        {
            case DesignTool.DrawLandArea:
                AppendLandDraft(overlay);
                break;
            case DesignTool.DrawPier:
                AppendPierDraft(overlay);
                break;
            case DesignTool.AddBerths:
                AppendBerthDraft(overlay);
                break;
            case DesignTool.AddLandBerths:
                AppendLandBerthDraft(overlay);
                break;
            case DesignTool.Erase:
            case DesignTool.Rename:
                AppendEraseTarget(overlay);
                break;
            case DesignTool.EditServices when _eraseTarget is Berth serving:
                foreach (var berth in ServiceTargets(serving, WholeSideWanted(_modifiers)))
                {
                    overlay.Pad(berth, BerthPlacement.PadHeightFor(SceneBuilder.GroundHeight(berth, _marina.GetLandArea)) + 0.04f, ServicePreviewColor);
                }

                break;
            case DesignTool.PlantTrees when _eraseTarget is LandArea target:
                var outlineY = target.Height + 0.1f;
                for (var i = 0; i < target.Points.Count; i++) overlay.Line(target.Points[i], target.Points[(i + 1) % target.Points.Count], outlineY, BerthPreviewColor with { W = 0.95f });
                overlay.Text(Strings.Format(Strings.OverlayTreeCount, target.Trees.Count), _pointer ?? target.Points[0], outlineY, TextColor);
                break;
            case DesignTool.SelectArea when SelectionQuad is { } corners:
                var boxY = textFloor - 0.4f;
                for (var i = 0; i < 4; i++) overlay.Line(corners[i], corners[(i + 1) % 4], boxY, SelectionBoxColor);
                break;
            case DesignTool.DrawShoreline:
                AppendShorelineDraft(overlay);
                break;
            case DesignTool.MoveReferenceImage:
            case DesignTool.MeasureScale:
                AppendImageTools(overlay);
                break;
        }
    }

    /// <summary>Just clear of the wave crests, so the traffic lines are not swallowed by the water they lie on.</summary>
    private float TrafficLaneHeight() =>
        ShaderSources.MaxWaveHeightFactor * MathF.Abs(_marina.Water.WaveAmplitude) + 0.2f;

    private void AppendLandDraft(Overlay overlay)
    {
        var y = _landHeight + 0.08f;
        var preview = _pointer is { } p && (_points.Count == 0 || Vector2.DistanceSquared(_points[^1], p) > 1e-6f)
            ? _points.Append(p).ToList()
            : _points.ToList();
        var valid = preview.Count < 3 || PolygonMath.IsSimple(preview);
        var color = valid ? DraftColor : InvalidColor;

        for (var i = 0; i + 1 < preview.Count; i++) overlay.Line(preview[i], preview[i + 1], y, color);
        if (preview.Count >= 3) overlay.Line(preview[^1], preview[0], y, valid ? DraftClosingColor : InvalidColor);
        foreach (var point in _points) overlay.Dot(point, y, color);
        if (_points.Count >= 3) overlay.Dot(_points[0], y, SnapColor, 1.6f);

        if (_pointer is { } pointer)
        {
            overlay.Dot(pointer, y, _pointerSnapped ? SnapColor : PointerColor, _pointerSnapped ? 1.5f : 1f);
            if (_points.Count > 0) overlay.Text(FormatMeters(Vector2.Distance(_points[^1], pointer)), pointer, y, TextColor);
        }
    }

    /// <summary>
    /// The coast being drawn: the line itself, its two ends carried on to show where the land is cut, and — once the
    /// line is settled — which side the pointer is choosing.
    /// </summary>
    private void AppendShorelineDraft(Overlay overlay)
    {
        const float endless = 300f;   // enough of the endless ends to read which way they go
        var y = (_marina.Shoreline?.Height ?? _landHeight) + 0.08f;
        var settled = _shorelineSide is not null;
        var line = _shorelineSide ?? (_pointer is { } p && (_points.Count == 0 || Vector2.DistanceSquared(_points[^1], p) > 1e-6f)
            ? _points.Append(p).ToList()
            : _points.ToList());

        var crosses = line.Count >= 2 && new Shoreline(line, landOnLeft: true).Validate().Any();
        var color = crosses ? InvalidColor : settled ? SnapColor : DraftColor;

        for (var i = 0; i + 1 < line.Count; i++) overlay.Line(line[i], line[i + 1], y, color);
        foreach (var point in _points) overlay.Dot(point, y, color);

        if (line.Count >= 2)
        {
            // The ends run on without end; showing a few hundred meters of them says which way the land is cut.
            overlay.Line(line[0], line[0] + Vector2.Normalize(line[0] - line[1]) * endless, y, color with { W = color.W * 0.45f });
            overlay.Line(line[^1], line[^1] + Vector2.Normalize(line[^1] - line[^2]) * endless, y, color with { W = color.W * 0.45f });
        }

        if (_pointer is not { } pointer) return;
        overlay.Dot(pointer, y, _pointerSnapped ? SnapColor : PointerColor, _pointerSnapped ? 1.5f : 1f);

        if (settled && line.Count >= 2)
        {
            // A line from the coast out to the pointer, showing the half of the plan the click would make land.
            var nearest = line.OrderBy(point => Vector2.DistanceSquared(point, pointer)).First();
            overlay.Line(nearest, pointer, y, BerthPreviewColor with { W = 0.9f });
            overlay.Text(Strings.OverlayLandThisSide, pointer, y, TextColor);
        }
        else if (_points.Count > 0)
        {
            overlay.Text(FormatMeters(Vector2.Distance(_points[^1], pointer)), pointer, y, TextColor);
        }
    }

    private void AppendPierDraft(Overlay overlay)
    {
        var deck = Pier.GetDefaultDeckHeight(_pierType);
        if (_pointer is not { } pointer) return;
        overlay.Dot(pointer, deck + 0.2f, _pointerSnapped ? SnapColor : PointerColor, _pointerSnapped ? 1.5f : 1f);
        if (_points.Count == 0) return;

        var start = _points[0];
        var length = Vector2.Distance(start, pointer);
        overlay.Dot(start, deck + 0.2f, DraftColor);
        if (length < 1e-3f) return;

        var heading = MarinaMath.DirectionToHeading(pointer - start);
        var center = (start + pointer) * 0.5f;
        var color = length >= MinimumPierLength ? new Vector4(PierPreviewColor(_pierType), 0.7f) : InvalidColor;
        // A line back along the pier marks the direction as squared up rather than freehand.
        if (_headingSnapped) overlay.Line(start - MarinaMath.HeadingToDirection(heading) * 4f, start, deck + 0.1f, SnapColor);
        overlay.Box(center, heading, new Vector3(_pierWidth, 0.3f, length), deck - 0.15f, color);
        overlay.Line(start, pointer, deck + 0.1f, DraftColor);

        if (_pierSides != PierSides.Both)
        {
            // Mark the open side with a strip along its edge.
            var right = -MarinaMath.HeadingToRight(heading) * (_pierSides == PierSides.Right ? 1f : -1f); // Pier.Right
            var edge = right * (_pierWidth * 0.5f + overlay.LineWidth);
            overlay.Line(start + edge, pointer + edge, deck + 0.1f, BerthPreviewColor with { W = 0.95f });
        }

        overlay.Text(FormatMeters(length), pointer, deck + 0.2f, TextColor);
    }

    private void AppendBerthDraft(Overlay overlay)
    {
        if (_pointer is not { } pointer) return;

        var target = _berthPierId is { } anchoredPier && _marina.GetPier(anchoredPier) is { } anchored
            ? (Pier: anchored, Side: _berthSide, Along: Along(anchored, pointer))
            : FindBerthTarget(pointer);
        if (target is not { } t)
        {
            overlay.Dot(pointer, 0.4f, PointerColor);
            return;
        }

        if (!t.Pier.HasBerthsOn(t.Side))
        {
            var edge = t.Pier.Right * (t.Side == PierSide.Right ? 1f : -1f) * (t.Pier.Width * 0.5f + overlay.LineWidth);
            overlay.Line(t.Pier.Start + edge, t.Pier.End + edge, t.Pier.DeckHeight + 0.1f, InvalidColor);
            overlay.Dot(pointer, 0.4f, InvalidColor);
            return;
        }

        var from = _berthPierId is null ? t.Along : _berthAlong;
        var (berths, _) = PlanBerths(t.Pier, t.Side, from, t.Along);
        foreach (var berth in berths)
        {
            overlay.Pad(berth, BerthPlacement.PadHeight + 0.03f, BerthPreviewColor);
        }

        if (berths.Count > 0)
        {
            var label = berths.Count == 1 ? $"1 BERTH {FormatNumber(_berthWidth)} X {FormatNumber(_berthLength)} M" : $"{berths.Count} BERTHS";
            overlay.Text(label, pointer, 0.5f, TextColor);
        }
        else
        {
            overlay.Dot(pointer, 0.4f, InvalidColor);
        }
    }

    private void AppendLandBerthDraft(Overlay overlay)
    {
        if (_pointer is not { } pointer) return;

        var land = _berthLandId is { } anchored ? _marina.GetLandArea(anchored) : _eraseTarget as LandArea;
        if (land is null)
        {
            overlay.Dot(pointer, 0.4f, InvalidColor);
            return;
        }

        var y = land.Height + 0.12f;
        var center = _points.Count > 0 ? _points[0] : pointer;
        var heading = _points.Count > 0 ? HeadingFor(center, pointer) : _landBerthHeading;
        var preview = Berth.OnLand("preview", land.Id, center, heading, _berthLength, _berthWidth);
        var inside = land.Contains(center);
        overlay.Pad(preview, y, inside ? BerthPreviewColor : InvalidColor with { W = 0.5f });

        // An arrow out of the spot, the way the bow points.
        var nose = center + preview.Forward * (_berthLength * 0.5f + 1.5f);
        overlay.Line(center, nose, y, DraftColor);
        overlay.Dot(nose, y, DraftColor, 1.4f);
        overlay.Text($"{FormatNumber(_berthWidth)} X {FormatNumber(_berthLength)} M AT {FormatNumber(heading)}", pointer, y, TextColor);
    }

    private void AppendEraseTarget(Overlay overlay)
    {
        switch (_eraseTarget)
        {
            case Berth berth:
                // With Alt down the eraser takes the whole row, so show the whole row.
                var sweeping = (_modifiers & InputModifiers.Alt) != 0 && berth.PierId is not null;
                foreach (var doomed in sweeping ? _marina.GetBerthsByPier(berth.PierId!) : new[] { berth })
                {
                    var height = SceneBuilder.GroundHeight(doomed, _marina.GetLandArea);
                    overlay.Pad(doomed, BerthPlacement.PadHeightFor(height) + 0.04f, EraseColor);
                }

                break;
            case Pier pier:
                overlay.Box(pier.Center, pier.HeadingDegrees, new Vector3(pier.Width + 0.4f, 0.5f, pier.Length + 0.4f), pier.DeckHeight + 0.1f, EraseColor);
                foreach (var berth in _marina.GetBerthsByPier(pier.Id)) overlay.Pad(berth, BerthPlacement.PadHeight + 0.04f, EraseColor);
                break;
            case LandArea land:
                var y = land.Height + 0.1f;
                for (var i = 0; i < land.Points.Count; i++) overlay.Line(land.Points[i], land.Points[(i + 1) % land.Points.Count], y, InvalidColor);
                foreach (var berth in _marina.GetBerthsByLandArea(land.Id)) overlay.Pad(berth, land.Height + 0.1f, EraseColor);
                break;
        }
    }

    private void AppendImageTools(Overlay overlay)
    {
        if (ReferenceImageBounds is not { } b) return;
        var y = ImageDrawHeight() + 0.05f;
        var corners = new[] { b.Min, new Vector2(b.Max.X, b.Min.Y), b.Max, new Vector2(b.Min.X, b.Max.Y) };
        for (var i = 0; i < 4; i++) overlay.Line(corners[i], corners[(i + 1) % 4], y, ImageOutlineColor);
        overlay.Text("N", new Vector2((b.Min.X + b.Max.X) * 0.5f, b.Min.Y - overlay.LineWidth * 6f), y, ImageOutlineColor);

        if (_tool == DesignTool.MeasureScale && _points.Count == 1 && _pointer is { } pointer)
        {
            overlay.Line(_points[0], pointer, y, ScaleLineColor);
            overlay.Dot(_points[0], y, ScaleLineColor);
            overlay.Text(FormatMeters(Vector2.Distance(_points[0], pointer)), pointer, y, TextColor);
        }
        else if (_tool == DesignTool.MeasureScale && _pointer is { } p)
        {
            overlay.Dot(p, y, PointerColor);
        }

        if (_tool == DesignTool.MeasureScale && _scaleLine is { } scale)
        {
            overlay.Text(FormatMeters(Vector2.Distance(scale.Start, scale.End)), (scale.Start + scale.End) * 0.5f, y, ScaleLineColor);
        }
    }

    // ---- Tool helpers ---------------------------------------------------------------------------

    private void ClickLandArea(float x, float y)
    {
        if (_pointer is not { } p) return;

        // Clicking the first corner closes the outline.
        if (_points.Count >= 3 && _marina.TryProjectToScreen(MarinaMath.ToWorld(_points[0]), out var first) &&
            Vector2.Distance(first, new Vector2(x, y)) <= MathF.Max(_snapPixels, 6f))
        {
            CompleteDraft();
            return;
        }

        // The second click of a double-click lands on the same spot; the double-click then finishes the outline.
        if (_points.Count > 0 && Vector2.Distance(_points[^1], p) < 1e-3f) return;
        AddPoint(p);
    }

    private void ClickBerths(Vector2 pointer)
    {
        if (_berthPierId is null)
        {
            if (FindBerthTarget(pointer) is not { } target || !target.Pier.HasBerthsOn(target.Side)) return;
            _berthPierId = target.Pier.Id;
            _berthSide = target.Side;
            _berthAlong = target.Along;
            AddPoint(pointer);
            return;
        }

        if (_marina.GetPier(_berthPierId) is not { } pier)
        {
            CancelDraft();
            return;
        }

        var pierId = _berthPierId;
        TryCreate(() => CreateBerths(pierId, _berthSide, _berthAlong, Along(pier, pointer)).Count > 0);
        if (_berthPierId is not null) FinishDraft(DesignDraftChange.Canceled);
    }

    private void ClickLandBerth(float x, float y, Vector2 pointer)
    {
        if (_berthLandId is null)
        {
            if (FindLandUnderPointer(x, y) is not { } land) return;
            _berthLandId = land.Id;
            AddPoint(pointer);
            return;
        }

        if (_marina.GetLandArea(_berthLandId) is null)
        {
            CancelDraft();
            return;
        }

        var landAreaId = _berthLandId;
        var center = _points[0];
        TryCreate(() => CreateLandBerth(landAreaId, center, HeadingFor(center, pointer)) is not null);
        if (_berthLandId is not null) FinishDraft(DesignDraftChange.Canceled);
    }

    private void RemoveTreesUnderPointer(float x, float y)
    {
        if (FindLandUnderPointer(x, y) is { } land) RemoveTrees(land.Id);
    }

    private void FinishScaleLine(Vector2 start, Vector2 end)
    {
        _scaleLine = (start, end);
        FinishDraft(DesignDraftChange.Completed);
        var args = new ScaleLineDrawnEventArgs(start, end, Vector2.Distance(start, end));
        ScaleLineDrawn?.Invoke(this, args);
        if (args.KnownLengthMeters is > 0f and var known) CalibrateReferenceImage(start, end, known);
        RaiseStateChanged();
    }

    private void AddPoint(Vector2 point)
    {
        _points.Add(point);
        _marina.MarkSceneDirty();
        DraftChanged?.Invoke(this, new DesignDraftChangedEventArgs(_tool, DesignDraftChange.PointAdded, _points.ToArray()));
        RaiseStateChanged();
    }

    private void FinishDraft(DesignDraftChange change)
    {
        var points = _points.ToArray();
        var hadDraft = points.Length > 0 || _berthPierId is not null || _shorelineSide is not null;
        _points.Clear();
        _berthPierId = null;
        _berthLandId = null;
        _shorelineSide = null;
        _marina.MarkSceneDirty();
        if (hadDraft) DraftChanged?.Invoke(this, new DesignDraftChangedEventArgs(_tool, change, change == DesignDraftChange.Canceled ? Array.Empty<Vector2>() : points));
        RaiseStateChanged();
    }

    /// <summary>Runs a creation triggered by the user; invalid geometry abandons the drawing instead of throwing into the input loop.</summary>
    private bool TryCreate(Func<bool> create)
    {
        try
        {
            return create();
        }
        catch (Exception ex) when (ex is MarinaLayoutException or ArgumentException or InvalidOperationException)
        {
            FinishDraft(DesignDraftChange.Canceled);
            return false;
        }
    }

    private void UpdatePointer(float x, float y)
    {
        _pointerSnapped = false;
        _headingSnapped = false;
        if (GroundPoint(x, y) is not { } ground)
        {
            _pointer = null;
            return;
        }

        var point = ground;
        if (_tool is DesignTool.DrawLandArea or DesignTool.DrawPier) point = Snap(point, new Vector2(x, y), out _pointerSnapped);

        // A pier lines up square with what is already there; Shift asks for 15° steps instead.
        if (_tool == DesignTool.DrawPier && _points.Count == 1 && !_pointerSnapped)
        {
            var offset = point - _points[0];
            if (offset.LengthSquared() > 1e-6f)
            {
                var heading = MarinaMath.DirectionToHeading(offset);
                var aligned = (_modifiers & InputModifiers.Shift) != 0
                    ? MathF.Round(heading / 15f) * 15f
                    : SquareWithSurroundings(heading, _points[0]);

                _headingSnapped = aligned is not null;
                if (aligned is { } snapped) point = _points[0] + MarinaMath.HeadingToDirection(snapped) * offset.Length();
            }
        }

        _pointer = point;
    }

    /// <summary>
    /// The direction a pier should really take: square (a multiple of 90°) with the piers already in the marina and
    /// with the shore it springs from, when <paramref name="heading"/> is within <see cref="PierAngleSnapDegrees"/> of
    /// one. Null when nothing is close enough, or when Alt asks for the exact direction drawn.
    /// </summary>
    /// <param name="heading">The direction the pointer is actually indicating.</param>
    /// <param name="start">The pier's shore end, which decides which land edges count as its quay.</param>
    private float? SquareWithSurroundings(float heading, Vector2 start)
    {
        if ((_modifiers & InputModifiers.Alt) != 0) return null;

        float? best = null;
        var bestDelta = PierAngleSnapDegrees;

        void Consider(float reference)
        {
            // Along the reference, across it, and both the other ways round: the four square directions.
            for (var quarter = 0; quarter < 4; quarter++)
            {
                var candidate = MarinaMath.DeltaAngle(0f, reference + quarter * 90f);
                var delta = MathF.Abs(MarinaMath.DeltaAngle(candidate, heading));
                if (delta >= bestDelta) continue;
                bestDelta = delta;
                best = candidate;
            }
        }

        foreach (var pier in _marina.GetPiers()) Consider(pier.HeadingDegrees);

        foreach (var land in _marina.GetLandAreas())
        {
            for (var i = 0; i < land.Points.Count; i++)
            {
                var from = land.Points[i];
                var to = land.Points[(i + 1) % land.Points.Count];
                // Only the edges near the shore end: the far side of a big quay says nothing about this pier.
                if (Vector2.Distance(ClosestOnSegment(start, from, to), start) > PierAngleReferenceRange) continue;
                Consider(MarinaMath.DirectionToHeading(to - from));
            }
        }

        return best;
    }

    /// <summary>Snaps to land corners, pier ends and draft points, then to land edges, within <see cref="SnapDistancePixels"/>.</summary>
    private Vector2 Snap(Vector2 point, Vector2 screen, out bool snapped)
    {
        snapped = false;
        if (_snapPixels <= 0f || (_modifiers & InputModifiers.Alt) != 0) return point;

        var best = _snapPixels;
        var result = point;
        var found = false;
        void Try(Vector2 candidate)
        {
            if (!_marina.TryProjectToScreen(MarinaMath.ToWorld(candidate), out var projected)) return;
            var distance = Vector2.Distance(projected, screen);
            if (distance >= best) return;
            best = distance;
            result = candidate;
            found = true;
        }

        var lands = _marina.GetLandAreas();
        foreach (var land in lands) foreach (var corner in land.Points) Try(corner);
        foreach (var pier in _marina.GetPiers())
        {
            Try(pier.Start);
            Try(pier.End);
        }

        foreach (var draft in _points) Try(draft);

        if (!found)
        {
            foreach (var land in lands)
            {
                for (var i = 0; i < land.Points.Count; i++) Try(ClosestOnSegment(point, land.Points[i], land.Points[(i + 1) % land.Points.Count]));
            }
        }

        snapped = found;
        return result;
    }

    private (Pier Pier, PierSide Side, float Along)? FindBerthTarget(Vector2 point)
    {
        (Pier Pier, PierSide Side, float Along)? best = null;
        var bestLateral = float.MaxValue;
        foreach (var pier in _marina.GetPiers())
        {
            var along = Vector2.Dot(point - pier.Start, pier.Direction);
            if (along < -_berthWidth || along > pier.Length + _berthWidth) continue;
            var lateral = Vector2.Dot(point - pier.Center, pier.Right);
            if (MathF.Abs(lateral) > pier.Width * 0.5f + _berthLength + 2f || MathF.Abs(lateral) >= bestLateral) continue;
            bestLateral = MathF.Abs(lateral);
            best = (pier, lateral >= 0f ? PierSide.Right : PierSide.Left, Math.Clamp(along, 0f, pier.Length));
        }

        return best;
    }

    /// <summary>Bow direction of the land berth being placed: from its spot toward the pointer (Shift: 15° steps), or the set heading.</summary>
    private float HeadingFor(Vector2 center, Vector2? pointer)
    {
        if (pointer is not { } target || Vector2.Distance(center, target) < LandBerthAimDistance) return _landBerthHeading;
        var heading = MarinaMath.DirectionToHeading(target - center);
        return (_modifiers & InputModifiers.Shift) != 0 ? MathF.Round(heading / 15f) * 15f : heading;
    }

    private static string DescribeElement(object element) => element switch
    {
        Berth berth => Strings.Format(Strings.ElementBerth, berth.DisplayName),
        Pier pier => Strings.Format(Strings.ElementPier, pier.Name),
        LandArea land => land.DisplayName,
        _ => element.GetType().Name,
    };

    private static float Along(Pier pier, Vector2 point) => Math.Clamp(Vector2.Dot(point - pier.Start, pier.Direction), 0f, pier.Length);

    /// <summary>
    /// The berths (and dividers) a row between two distances along a pier would add: one every <see cref="BerthWidth"/> +
    /// <see cref="BerthGap"/> meters, lined up with the existing berths on that side unless <see cref="AlignBerthsToExisting"/> is off,
    /// skipping the places already taken.
    /// </summary>
    internal (IReadOnlyList<Berth> Berths, IReadOnlyList<Divider> Dividers) PlanBerths(Pier pier, PierSide side, float fromAlong, float toAlong)
    {
        var width = _berthWidth;
        var gap = SeparatorGap;
        var pitch = width + gap;
        var sign = side == PierSide.Right ? 1f : -1f;
        var occupied = _marina.GetBerthsByPier(pier.Id)
            .Where(s => Vector2.Dot(s.Center - pier.Center, pier.Right) * sign > 0f)
            .Select(s =>
            {
                var along = Vector2.Dot(s.Center - pier.Start, pier.Direction);
                var extent = MathF.Abs(Vector2.Dot(s.Right, pier.Direction)) * s.Width * 0.5f + MathF.Abs(Vector2.Dot(s.Forward, pier.Direction)) * s.Length * 0.5f;
                return (Min: along - extent, Max: along + extent);
            })
            .ToList();

        var lo = MathF.Min(fromAlong, toAlong);
        var hi = MathF.Max(fromAlong, toAlong);
        float first, last;
        if (_alignBerths)
        {
            // Line up with the nearest existing berth edge on this side, or with the pier's start.
            var origin = 0f;
            var nearest = float.MaxValue;
            foreach (var (min, max) in occupied)
            {
                // Slot edges of the existing row: its own near edge, and the next slot one gap past its far edge.
                foreach (var edge in new[] { min, max + gap })
                {
                    if (MathF.Abs(edge - fromAlong) < nearest)
                    {
                        nearest = MathF.Abs(edge - fromAlong);
                        origin = edge;
                    }
                }
            }

            first = origin + MathF.Floor((lo - origin) / pitch + 1e-4f) * pitch;
            last = MathF.Max(origin + MathF.Ceiling((hi - origin) / pitch - 1e-4f) * pitch, first + pitch);
        }
        else
        {
            // The row starts exactly where the user clicked, at any offset from the pier's start.
            first = lo;
            last = MathF.Max(hi, first + pitch);
        }

        var names = new BerthNames(_berthNaming, _marina.GetBerths().Select(s => s.Id));
        var berths = new List<Berth>();
        var offsets = new List<float>();
        for (var offset = first; offset < last - 1e-3f && berths.Count < 500; offset += pitch)
        {
            var center = offset + width * 0.5f;
            if (center < -1e-3f || center > pier.Length + 1e-3f) continue;
            if (occupied.Any(o => MathF.Min(o.Max, offset + width) - MathF.Max(o.Min, offset) > 0.05f)) continue;

            var id = names.Next(name => _berthNaming.Format(pier, side, name));
            berths.Add(BerthGenerator.AtPier(pier, id, side, offset, width, _berthLength) with
            {
                MaxDraft = _berthDepth,
                HasFingerPiers = _berthSeparators == BerthSeparator.FingerPiers,
            });
            offsets.Add(offset);
        }

        var dividers = new List<Divider>();
        if (DividerTypeOf(_berthSeparators) is { } type && berths.Count > 0)
        {
            var existing = _marina.GetDividersByPier(pier.Id);
            var dividerPrefix = BerthGenerator.DividerPrefix(pier, side);
            var usedDividerIds = new HashSet<string>(_marina.GetDividers().Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
            var dividerNumber = _marina.GetDividers().Select(d => ParseNumber(d.Id, dividerPrefix)).DefaultIfEmpty(0).Max();
            foreach (var edge in SeparatorEdges(offsets, occupied, width))
            {
                var divider = BerthGenerator.DividerAtPier(pier, "-", side, edge, _berthLength, type);
                if (existing.Concat(dividers).Any(d => d.Type == divider.Type && Vector2.DistanceSquared(d.Start, divider.Start) < 0.01f)) continue;
                string id;
                do id = $"{dividerPrefix}{++dividerNumber:00}"; while (!usedDividerIds.Add(id));
                dividers.Add(divider with { Id = id });
            }
        }

        return (berths, dividers);
    }

    /// <summary>
    /// Distances along the pier where the new berths get a separator: both edges of every new berth, or — for
    /// <see cref="BerthSeparator.PairedFingerPiers"/> — every other boundary of the whole row, so the berths end up in pairs with one
    /// pier each and a pier at both ends of the row.
    /// </summary>
    /// <param name="newOffsets">Near edge of each berth about to be added, in order along the pier.</param>
    /// <param name="occupied">Stretches the berths already on this side cover.</param>
    /// <param name="width">Width of the new berths.</param>
    private IReadOnlyList<float> SeparatorEdges(IReadOnlyList<float> newOffsets, IReadOnlyList<(float Min, float Max)> occupied, float width)
    {
        var mine = new List<float>();
        foreach (var offset in newOffsets)
        {
            mine.Add(offset);
            mine.Add(offset + width);
        }

        if (_berthSeparators != BerthSeparator.PairedFingerPiers) return mine;

        // Pair up the whole row, not just the berths being added, so a row built in several goes keeps its rhythm.
        var row = newOffsets.Select(offset => (Min: offset, Max: offset + width)).Concat(occupied).OrderBy(span => span.Min).ToList();
        var edges = new List<float>();
        for (var i = 0; i < row.Count; i += 2) edges.Add(row[i].Min);
        if (row.Count > 0) edges.Add(row[^1].Max);

        // Only the boundaries of the berths being added are ours to build.
        return edges.Where(edge => mine.Any(m => MathF.Abs(m - edge) < 0.05f)).ToList();
    }

    /// <summary>The divider generated between the berths, or null when they have their own finger piers or nothing at all.</summary>
    private static DividerType? DividerTypeOf(BerthSeparator separator) => separator switch
    {
        BerthSeparator.FingerPier or BerthSeparator.PairedFingerPiers => DividerType.FingerPier,
        BerthSeparator.Piles => DividerType.Piles,
        BerthSeparator.Boom => DividerType.Boom,
        BerthSeparator.SinglePile => DividerType.SinglePile,
        _ => null,
    };

    /// <summary>The space left between neighbouring berths: <see cref="BerthGap"/>, but never less than a hand's width without a separator.</summary>
    private float SeparatorGap => _berthSeparators == BerthSeparator.None ? MathF.Max(_berthGap, MinimumSeparatorGap) : _berthGap;

    /// <summary>The land area under a view pixel, optionally only of one kind (trees go on lawns only).</summary>
    private LandArea? FindLandUnderPointer(float x, float y, LandKind? kind = null)
    {
        var ray = _marina.Camera.ScreenPointToRay(x, y, _marina.ViewportSize.X, _marina.ViewportSize.Y);
        LandArea? best = null;
        var bestDistance = float.MaxValue;
        foreach (var land in _marina.GetLandAreas())
        {
            if (kind is { } required && land.Kind != required) continue;
            if (ray.IntersectHorizontalPlane(land.Height, out var distance) && distance < bestDistance && land.Contains(MarinaMath.ToPlan(ray.GetPoint(distance))))
            {
                best = land;
                bestDistance = distance;
            }
        }

        return best;
    }

    private object? FindEraseTarget(float x, float y)
    {
        if (_marina.HitTest(x, y) is { } hit && _marina.GetBerth(hit.BerthId) is { } berth) return berth;

        var ray = _marina.Camera.ScreenPointToRay(x, y, _marina.ViewportSize.X, _marina.ViewportSize.Y);
        object? best = null;
        var bestDistance = float.MaxValue;
        foreach (var pier in _marina.GetPiers())
        {
            if (ray.IntersectHorizontalPlane(pier.DeckHeight, out var distance) && distance < bestDistance &&
                new OrientedRect(pier.Center, new Vector2(pier.Width + 0.6f, pier.Length), pier.HeadingDegrees).Contains(MarinaMath.ToPlan(ray.GetPoint(distance))))
            {
                best = pier;
                bestDistance = distance;
            }
        }

        foreach (var land in _marina.GetLandAreas())
        {
            if (ray.IntersectHorizontalPlane(land.Height, out var distance) && distance < bestDistance && land.Contains(MarinaMath.ToPlan(ray.GetPoint(distance))))
            {
                best = land;
                bestDistance = distance;
            }
        }

        return best;
    }

    private Vector2? GroundPoint(float x, float y)
    {
        var ray = _marina.Camera.ScreenPointToRay(x, y, _marina.ViewportSize.X, _marina.ViewportSize.Y);
        return ray.IntersectHorizontalPlane(0f, out var distance) ? MarinaMath.ToPlan(ray.GetPoint(distance)) : null;
    }

    private float ImageDrawHeight() => ShaderSources.MaxWaveHeightFactor * MathF.Abs(_marina.Water.WaveAmplitude) + 0.03f;

    private string NextPierId()
    {
        var used = new HashSet<string>(_marina.GetPiers().Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
        for (var c = 'A'; c <= 'Z'; c++)
        {
            if (!used.Contains(c.ToString())) return c.ToString();
        }

        return NextId("D", used);
    }

    private static string NextId(string prefix, IEnumerable<string> existing)
    {
        var used = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var number = used.Select(id => ParseNumber(id, prefix)).DefaultIfEmpty(0).Max();
        string id;
        do id = $"{prefix}{++number}"; while (used.Contains(id));
        return id;
    }

    /// <summary>Hands out the next free name a <see cref="BerthNamingScheme"/> offers, skipping the ones already taken.</summary>
    private sealed class BerthNames
    {
        private readonly HashSet<string> _used;
        private readonly int _increment;
        private int _number;

        /// <param name="scheme">The naming scheme in force.</param>
        /// <param name="existingIds">Names already taken, which are skipped.</param>
        /// <param name="start">First number to offer; null counts from the scheme's own start.</param>
        /// <param name="step">Step between names; null uses the scheme's own increment.</param>
        public BerthNames(BerthNamingScheme scheme, IEnumerable<string> existingIds, int? start = null, int? step = null)
        {
            _used = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);
            var increment = step ?? scheme.Increment;
            _increment = increment == 0 ? 1 : increment;
            _number = start ?? scheme.StartNumber;
        }

        public string Next(Func<int, string> format)
        {
            // A pattern without {number} gives the same name every time; after a few tries fall back to a numbered one.
            for (var attempt = 0; attempt < 10000; attempt++)
            {
                var name = format(_number);
                _number += _increment;
                if (_used.Add(name)) return name;
            }

            string unique;
            var suffix = 1;
            do unique = $"B-{suffix++}"; while (!_used.Add(unique));
            return unique;
        }
    }

    private static int ParseNumber(string id, string prefix) =>
        id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && int.TryParse(id.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static List<Vector2> RemoveDuplicatePoints(IReadOnlyList<Vector2> points)
    {
        var result = new List<Vector2>();
        foreach (var point in points)
        {
            if (result.Count == 0 || Vector2.Distance(result[^1], point) > 0.01f) result.Add(point);
        }

        while (result.Count > 1 && Vector2.Distance(result[0], result[^1]) <= 0.01f) result.RemoveAt(result.Count - 1);
        return result;
    }

    private static Vector2 ClosestOnSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        return lengthSquared < 1e-12f ? a : a + ab * Math.Clamp(Vector2.Dot(p - a, ab) / lengthSquared, 0f, 1f);
    }

    private static Vector3 PierPreviewColor(PierType type) => type switch
    {
        PierType.Concrete => new Vector3(0.74f, 0.73f, 0.70f),
        PierType.FloatingConcrete => new Vector3(0.68f, 0.67f, 0.64f),
        _ => new Vector3(0.66f, 0.50f, 0.33f),
    };

    private static string FormatMeters(float meters) => FormatNumber(meters) + " M";

    private static string FormatNumber(float value) => value.ToString(value >= 100f ? "0" : "0.0", CultureInfo.InvariantCulture);

    private static float RequireRange(float value, float min, float max) =>
        float.IsFinite(value) && value >= min && value <= max
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, $"Must be between {min.ToString(CultureInfo.InvariantCulture)} and {max.ToString(CultureInfo.InvariantCulture)}.");

    private void SetSetting<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        _marina.MarkSceneDirty();
        RaiseStateChanged();
    }

    private void OnImageChanged(ReferenceImageChange change)
    {
        _marina.RefreshCameraBounds();
        _marina.MarkSceneDirty();
        ReferenceImageChanged?.Invoke(this, new ReferenceImageChangedEventArgs(change, _image, _imageCenter, _imageMetersPerPixel));
        RaiseStateChanged();
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>One undoable change: the elements it added, the ones it removed and the state of the ones it changed.</summary>
    private sealed class DesignAction
    {
        public DesignAction(string description) => Description = description;

        public string Description { get; }

        public List<LandArea> AddedLandAreas { get; } = new();

        public List<Pier> AddedPiers { get; } = new();

        public List<Divider> AddedDividers { get; } = new();

        public List<Berth> AddedBerths { get; } = new();

        public List<LandArea> RemovedLandAreas { get; } = new();

        public List<Pier> RemovedPiers { get; } = new();

        public List<Divider> RemovedDividers { get; } = new();

        public List<Berth> RemovedBerths { get; } = new();

        /// <summary>The land areas as they were before the change (trees, for now).</summary>
        public List<LandArea> ChangedLandAreas { get; } = new();

        /// <summary>The piers as they were before the change (service pedestals and names).</summary>
        public List<Pier> ChangedPiers { get; } = new();

        /// <summary>The berths as they were before the change (pedestals, for now).</summary>
        public List<Berth> ChangedBerths { get; } = new();

        /// <summary>Berths that were renamed, as (old name, new name); undoing names them back.</summary>
        public List<(string From, string To)> RenamedBerths { get; } = new();

        /// <summary>Piers that were given another id, as (old id, new id); undoing moves them back.</summary>
        public List<(string From, string To)> RenamedPiers { get; } = new();

        /// <summary>True when this action set or removed the mainland, so undoing puts <see cref="PreviousShoreline"/> back.</summary>
        public bool TouchedShoreline { get; init; }

        /// <summary>The mainland as it was before the change. Null is a real value here: there was none.</summary>
        public Shoreline? PreviousShoreline { get; init; }

        public bool IsEmpty =>
            !TouchedShoreline &&
            AddedLandAreas.Count + AddedPiers.Count + AddedDividers.Count + AddedBerths.Count +
            RemovedLandAreas.Count + RemovedPiers.Count + RemovedDividers.Count + RemovedBerths.Count +
            ChangedLandAreas.Count + ChangedPiers.Count + ChangedBerths.Count + RenamedBerths.Count + RenamedPiers.Count == 0;
    }

    /// <summary>Emits preview geometry (thin boxes, dots, pads and text) into the transparent pass, so it shows above the reference image.</summary>
    private readonly struct Overlay
    {
        private readonly List<RenderObject> _output;
        private readonly float _textUpHeading;
        private readonly float _textFloor;

        public Overlay(List<RenderObject> output, float lineWidth, float textUpHeading, float textFloor)
        {
            _output = output;
            LineWidth = lineWidth;
            _textUpHeading = textUpHeading;
            _textFloor = textFloor;
        }

        public float LineWidth { get; }

        public void Line(Vector2 a, Vector2 b, float y, Vector4 color)
        {
            var length = Vector2.Distance(a, b);
            if (length < 1e-4f) return;
            Box((a + b) * 0.5f, MarinaMath.DirectionToHeading(b - a), new Vector3(LineWidth, LineWidth * 0.4f, length + LineWidth), y, color);
        }

        public void Box(Vector2 center, float heading, Vector3 size, float y, Vector4 color) =>
            _output.Add(new RenderObject(MeshIds.UnitBox, MarinaMath.CreatePlacement(size, heading, MarinaMath.ToWorld(center, y)), color, 0.6f));

        public void Dot(Vector2 point, float y, Vector4 color, float scale = 1f) =>
            _output.Add(new RenderObject(
                MeshIds.Buoy, Matrix4x4.CreateScale(LineWidth * 3f * scale) * Matrix4x4.CreateTranslation(MarinaMath.ToWorld(point, y)), color, 0.6f));

        public void Pad(Berth berth, float y, Vector4 color) =>
            _output.Add(new RenderObject(
                MeshIds.BerthPad,
                MarinaMath.CreatePlacement(new Vector3(MathF.Max(0.2f, berth.Width - 0.3f), 1f, MathF.Max(0.2f, berth.Length - 0.3f)), berth.HeadingDegrees, MarinaMath.ToWorld(berth.Center, y)),
                color, 0.4f));

        /// <summary>Text laid flat next to <paramref name="anchor"/>, upright for the current camera yaw, never below the highest land.</summary>
        public void Text(string text, Vector2 anchor, float y, Vector4 color)
        {
            if (text.Length == 0) return;
            y = MathF.Max(y, _textFloor);
            var height = LineWidth * 5f;
            var up = MarinaMath.HeadingToDirection(_textUpHeading);
            var reading = -MarinaMath.HeadingToRight(_textUpHeading);
            // Beside the pointer: to the right and a little above it on screen.
            var start = anchor + reading * (LineWidth * 5f) + up * (height * 0.9f);
            var scale = new Vector3(height, 1f, height);
            for (var i = 0; i < text.Length; i++)
            {
                if (!GlyphFont.TryGetMeshId(text[i], out var meshId)) continue;
                var position = start + reading * (i * GlyphFont.Advance * height + GlyphFont.GlyphWidth * height * 0.5f);
                _output.Add(new RenderObject(meshId, MarinaMath.CreatePlacement(scale, _textUpHeading, MarinaMath.ToWorld(position, y + 0.05f)), color, 0.8f));
            }
        }
    }
}
