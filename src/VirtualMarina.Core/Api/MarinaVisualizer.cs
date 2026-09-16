using System.Numerics;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Input;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Picking;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Api;

/// <summary>Construction options for <see cref="MarinaVisualizer"/>.</summary>
/// <example>
/// <code>
/// var marina = new MarinaVisualizer(new MarinaVisualizerOptions
/// {
///     Water = new WaterSettings { Size = 2000f, GridResolution = 220, WaveAmplitude = 0.05f },
/// });
/// </code>
/// </example>
public sealed class MarinaVisualizerOptions
{
    /// <summary>Water surface settings. <see cref="WaterSettings.Size"/> and <see cref="WaterSettings.GridResolution"/> can only be set here.</summary>
    public WaterSettings Water { get; init; } = new();

    /// <summary>Initial lighting; can also be changed later through <see cref="MarinaVisualizer.Lighting"/>.</summary>
    public LightingSettings Lighting { get; init; } = new();

    /// <summary>Center of the water grid in plan coordinates.</summary>
    public Vector2 WaterCenter { get; init; } = Vector2.Zero;
}

/// <summary>
/// Default <see cref="IMarinaVisualizer"/> implementation. Owns the marina state, selection, popup, filter,
/// camera and input handling, and produces a <see cref="RenderFrame"/> for whichever
/// <see cref="ISceneRenderer"/> the host view uses.
/// </summary>
/// <remarks>
/// Most hosts don't create one directly: <c>MarinaViewControl.Marina</c> (WinForms) owns one, and <c>&lt;MarinaView Marina="..."&gt;</c>
/// (Blazor) takes one. API members are documented on <see cref="IMarinaVisualizer"/>. A custom view drives it once per display frame:
/// <code>
/// marina.SetViewportSize(width, height);
/// marina.Update(deltaSeconds);
/// renderer.Render(marina.BuildRenderFrame());
/// if (marina.TryGetPopupAnchor(out var anchor)) PositionPopup(anchor);
/// </code>
/// and forwards pointer and keyboard input to <see cref="Input"/>.
/// </remarks>
public sealed partial class MarinaVisualizer : IMarinaVisualizer
{
    private static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;

    private readonly Dictionary<string, Dock> _docks = new(IdComparer);
    private readonly List<string> _dockOrder = new();
    private readonly Dictionary<string, Slip> _slips = new(IdComparer);
    private readonly List<string> _slipOrder = new();
    private readonly Dictionary<string, Divider> _dividers = new(IdComparer);
    private readonly List<string> _dividerOrder = new();
    private readonly Dictionary<string, MultiSlipBerth> _berths = new(IdComparer);
    private readonly List<string> _berthOrder = new();
    private readonly List<LandArea> _landAreas = new();
    private readonly List<CameraPreset> _presets = new();
    private readonly List<RenderObject> _renderObjects = new();
    private readonly StatusColorScheme _colors = new();

    private string? _hoveredSlipId;
    private SlipStatusFilter _statusFilter = SlipStatusFilter.All;
    private SlipLabelMode _slipLabelMode = SlipLabelMode.None;
    private bool _sceneDirty = true;
    private int _sceneVersion;
    private int _updateDepth;
    private bool _layoutChangePending;
    private double _time;
    private Vector2 _viewportSize = new(1280f, 720f);

    /// <summary>Creates an empty marina with default water and lighting. Load one with <see cref="InitializeLayout"/>.</summary>
    public MarinaVisualizer()
        : this(new MarinaVisualizerOptions())
    {
    }

    /// <summary>Creates an empty marina with custom water and lighting settings.</summary>
    /// <param name="options">Construction options.</param>
    public MarinaVisualizer(MarinaVisualizerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Water = options.Water;
        Lighting = options.Lighting;
        Meshes = MeshLibrary.CreateDefault(Water.Size, Water.GridResolution, options.WaterCenter);
        Camera = new OrbitCamera();
        Input = new MarinaInputController(this);
        RebuildBuiltInPresets();
    }

    /// <inheritdoc/>
    public event EventHandler<SlipEventArgs>? SlipClicked;

    /// <inheritdoc/>
    public event EventHandler<SlipSelectedEventArgs>? SlipSelected;

    /// <inheritdoc/>
    public event EventHandler<MultiSlipSelectedEventArgs>? MultiSlipSelected;

    /// <inheritdoc/>
    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    /// <inheritdoc/>
    public event EventHandler? SelectionCleared;

    /// <inheritdoc/>
    public event EventHandler<SlipActionInvokedEventArgs>? SlipActionInvoked;

    /// <inheritdoc/>
    public event EventHandler<SlipPopupChangedEventArgs>? PopupChanged;

    /// <inheritdoc/>
    public event EventHandler<SlipHoverEventArgs>? SlipHoverChanged;

    /// <inheritdoc/>
    public event EventHandler<SlipStatusChangedEventArgs>? SlipStatusChanged;

    /// <inheritdoc/>
    public event EventHandler<LayoutChangedEventArgs>? LayoutChanged;

    /// <inheritdoc/>
    public string MarinaName { get; private set; } = "Marina";

    /// <inheritdoc/>
    public OrbitCamera Camera { get; }

    /// <summary>
    /// Platform-neutral input handling. Host views forward pointer and keyboard events here; configure drag bindings,
    /// zoom step and hover through it.
    /// </summary>
    public MarinaInputController Input { get; }

    /// <inheritdoc/>
    public LightingSettings Lighting { get; }

    /// <inheritdoc/>
    public WaterSettings Water { get; }

    /// <summary>
    /// Meshes available to the scene. Register a replacement under an existing id
    /// (e.g. <see cref="MeshIds.ForBoat"/>) to swap in imported models.
    /// </summary>
    public MeshLibrary Meshes { get; }

    /// <summary>
    /// Current status colors and overlay opacities (read-only view). Change them with <see cref="SetStatusColor"/>,
    /// <see cref="SetDisabledColor"/>, <see cref="SetOverlayOpacity"/> and <see cref="ResetStatusColors"/>.
    /// </summary>
    public StatusColorScheme Colors => _colors;

    /// <summary>
    /// Which slips have their name (<see cref="Slip.DisplayName"/>) written on the water next to their open end,
    /// sized to fit the slip's width. Hidden and filtered-out slips are never labeled. Default <see cref="SlipLabelMode.None"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="Api.SlipLabelMode"/>.</exception>
    public SlipLabelMode SlipLabelMode
    {
        get => _slipLabelMode;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value), value, null);
            if (value == _slipLabelMode) return;
            _slipLabelMode = value;
            MarkSceneDirty();
        }
    }

    /// <summary>Seconds of animation time accumulated through <see cref="Update"/>.</summary>
    public double Time => _time;

    /// <summary>View size in pointer units (usually pixels), as last set with <see cref="SetViewportSize"/>. Defaults to 1280 × 720.</summary>
    public Vector2 ViewportSize => _viewportSize;

    // ---- Frame loop -----------------------------------------------------------------------------

    /// <summary>Sets the view size in the same units as pointer coordinates (usually pixels).</summary>
    /// <remarks>If the camera is still where the last focus call put it, the focus is re-fitted to the new size.</remarks>
    public void SetViewportSize(float width, float height)
    {
        if (!(width > 0f && height > 0f)) return;
        var size = new Vector2(width, height);
        if (size == _viewportSize) return;

        _viewportSize = size;
        RefitFocusAfterResize();
    }

    /// <summary>Advances animation time and camera smoothing.</summary>
    public void Update(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0d) return;
        var dt = Math.Min(deltaSeconds, 0.25d); // Avoid jumps after the window was suspended.
        _time += dt;
        Camera.Update((float)dt);
    }

    /// <summary>Builds the frame description for a renderer. Rebuilds instance data only when the scene changed.</summary>
    public RenderFrame BuildRenderFrame()
    {
        if (_sceneDirty)
        {
            SceneBuilder.Build(_renderObjects, new SceneState
            {
                Docks = OrderedDocks(),
                Slips = OrderedSlips().ToList(),
                Dividers = OrderedDividers(),
                Land = _landAreas,
                SlipLookup = GetSlip,
                DockLookup = GetDock,
                BerthLookup = GetMultiSlipBerth,
                Filter = _statusFilter,
                Selected = new HashSet<string>(_selection, IdComparer),
                PrimarySelectedId = _selection.Count > 0 ? _selection[^1] : null,
                HoveredSlipId = _hoveredSlipId,
                LabelMode = _slipLabelMode,
                Colors = _colors,
                Meshes = Meshes,
            });
            _sceneVersion++;
            _sceneDirty = false;
        }

        return new RenderFrame
        {
            View = Camera.GetViewMatrix(),
            Projection = Camera.GetProjectionMatrix(_viewportSize.X / _viewportSize.Y),
            CameraPosition = Camera.Position,
            Time = (float)(_time % 3600d),
            Lighting = Lighting,
            Water = Water,
            Objects = _renderObjects,
            SceneVersion = _sceneVersion,
            Meshes = Meshes,
        };
    }

    /// <summary>Forces the render object list to be rebuilt on the next frame.</summary>
    public void InvalidateScene() => _sceneDirty = true;

    /// <summary>
    /// Hit-tests a point in view pixels (origin top-left) against visible, unfiltered slips and boats.
    /// Disabled slips are hit (so they block what's behind them), but input ignores them.
    /// </summary>
    public SlipHit? HitTest(float x, float y)
    {
        var ray = Camera.ScreenPointToRay(x, y, _viewportSize.X, _viewportSize.Y);
        var slips = OrderedSlips().Where(IsShown).ToList();
        var boats = SlipPlacement.EnumerateBoats(slips, GetSlip, GetMultiSlipBerth, _statusFilter);
        return ScenePicker.Pick(ray, slips, boats, Meshes);
    }

    /// <summary>Point on the water plane under a view pixel, if the ray hits it.</summary>
    public Vector3? GetWaterPoint(float x, float y)
    {
        var ray = Camera.ScreenPointToRay(x, y, _viewportSize.X, _viewportSize.Y);
        return ray.IntersectHorizontalPlane(0f, out var distance) ? ray.GetPoint(distance) : null;
    }

    /// <summary>Projects a world point to view pixels (origin top-left). False when the point is behind the camera.</summary>
    public bool TryProjectToScreen(Vector3 worldPoint, out Vector2 screenPoint)
    {
        var viewProjection = Camera.GetViewMatrix() * Camera.GetProjectionMatrix(_viewportSize.X / _viewportSize.Y);
        var clip = Vector4.Transform(new Vector4(worldPoint, 1f), viewProjection);
        if (clip.W <= 1e-4f)
        {
            screenPoint = default;
            return false;
        }

        var ndc = new Vector2(clip.X, clip.Y) / clip.W;
        screenPoint = new Vector2((ndc.X + 1f) * 0.5f * _viewportSize.X, (1f - ndc.Y) * 0.5f * _viewportSize.Y);
        return float.IsFinite(screenPoint.X) && float.IsFinite(screenPoint.Y);
    }

    /// <summary>
    /// Where the popup should point, in view pixels: just above the primary selected slip's selection marker.
    /// Call every frame (the camera moves). False when no popup is open or the anchor is behind the camera.
    /// </summary>
    public bool TryGetPopupAnchor(out Vector2 screenPoint)
    {
        screenPoint = default;
        return _popup is { } popup && GetSlip(popup.PrimarySlip.Id) is { } slip && TryProjectToScreen(GetPopupAnchorWorld(slip), out screenPoint);
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private Vector3 GetPopupAnchorWorld(Slip slip)
    {
        var boatTop = slip.Boat is { } boat && slip.Status.CanHaveBoat() ? SlipPlacement.BoatTopHeight(boat, Meshes) : 3f;
        return MarinaMath.ToWorld(slip.Center, SceneBuilder.MarkerBaseHeight(boatTop) + SceneBuilder.MarkerTop);
    }

    private IEnumerable<Dock> OrderedDocks() => _dockOrder.Select(id => _docks[id]);

    private IEnumerable<Slip> OrderedSlips() => _slipOrder.Select(id => _slips[id]);

    private IEnumerable<Divider> OrderedDividers() => _dividerOrder.Select(id => _dividers[id]);

    private IEnumerable<MultiSlipBerth> OrderedBerths() => _berthOrder.Select(id => _berths[id]);

    /// <summary>Drawn with its status visuals: visible and not excluded by the status filter.</summary>
    private bool IsShown(Slip slip) => slip.IsVisible && _statusFilter.Includes(slip.Status);

    /// <summary>Can be part of the selection.</summary>
    private bool IsSelectable(Slip slip) => slip.IsInteractive && _statusFilter.Includes(slip.Status);

    private void MarkSceneDirty() => _sceneDirty = true;

    private void RaiseLayoutChanged(LayoutChangeKind kind, string? dockId = null, string? slipId = null, string? dividerId = null, string? berthId = null)
    {
        if (_updateDepth > 0)
        {
            _layoutChangePending = true;
            return;
        }

        LayoutChanged?.Invoke(this, new LayoutChangedEventArgs(kind, dockId, slipId, dividerId, berthId));
    }

    private Slip RequireSlip(string slipId)
    {
        ArgumentNullException.ThrowIfNull(slipId);
        return _slips.TryGetValue(slipId, out var slip)
            ? slip
            : throw new KeyNotFoundException($"Slip '{slipId}' does not exist.");
    }

    private SlipEventArgs CreateSlipArgs(Slip slip, PointerButton button = PointerButton.None, bool isDoubleClick = false, Vector3? worldPoint = null) =>
        new(slip, _docks.GetValueOrDefault(slip.DockId), button, isDoubleClick, worldPoint);

    private sealed class UpdateScope : IDisposable
    {
        private MarinaVisualizer? _owner;

        public UpdateScope(MarinaVisualizer owner) => _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.EndUpdate();
        }
    }
}
