using System.Numerics;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
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

    /// <summary>
    /// The initial style (colors, waves, opacities, ...). When set, its <see cref="MarinaStyle.Lighting"/> and <see cref="MarinaStyle.Water"/>
    /// are used instead of <see cref="Lighting"/> and <see cref="Water"/>.
    /// </summary>
    public MarinaStyle? Style { get; init; }
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

    /// <summary>Secondary index of <see cref="_berths"/> by pier id, and of <see cref="_dividers"/> by pier id.</summary>
    private const int ByPier = 0;

    /// <summary>Secondary index of <see cref="_berths"/> by land area id.</summary>
    private const int ByLandArea = 1;

    // Every element in the order it was added, with lookups, removals and "the berths of this pier" that do not walk
    // the whole marina: a pier removed from a marina of thousands of berths no longer scans them once per berth.
    private readonly OrderedStore<Pier> _piers = new(IdComparer);
    private readonly OrderedStore<Berth> _berths = new(IdComparer, berth => berth.PierId, berth => berth.LandAreaId);
    private readonly OrderedStore<Divider> _dividers = new(IdComparer, divider => divider.PierId);
    private readonly OrderedStore<MultiBerth> _multiBerths = new(IdComparer);
    private readonly OrderedStore<LandArea> _landAreas = new(IdComparer);
    private Shoreline? _shoreline;
    private MarineTraffic _traffic = MarineTraffic.None;
    private MarineTrafficField? _trafficField;
    private double _trafficTime;
    private bool _showTrafficLanes;
    private LabelFontDefinition? _registeredLabelFont;
    private bool _trafficDirty = true;
    private bool _connectionsDirty;
    private readonly SceneLayers _scene = new();
    private readonly List<RenderObject> _trafficObjects = [];
    private readonly Dictionary<string, int> _landMeshSlots = new(IdComparer);
    private readonly Dictionary<string, RenderObject[]> _landScenery = new(IdComparer);
    private RenderObject[] _shorelineScenery = [];
    private readonly List<CameraPreset> _presets = [];
    private MarinaStyle _style;

    private string? _hoveredBerthId;
    private BerthStatusFilter _statusFilter = BerthStatusFilter.All;
    private BerthLabelMode _berthLabelMode = BerthLabelMode.None;
    private bool _redrawRequested = true;
    private (Matrix4x4 View, Matrix4x4 Projection, FrameLighting Lighting, FrameWater Water) _drawn;
    private int _updateDepth;
    private readonly List<LayoutChange> _pendingChanges = [];
    private double _time;
    private Vector2 _viewportSize = new(1280f, 720f);
    private Vector2 _waterCenter;
    private Vector2 _marinaCenter;
    private float _waterSize;
    private float _requestedWaterSize;
    private string _marinaName = "Marina";

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
        _style = options.Style ?? new MarinaStyle { Lighting = options.Lighting, Water = options.Water };
        Meshes = MeshLibrary.CreateDefault(Water.Size, Water.GridResolution, options.WaterCenter);
        _waterCenter = options.WaterCenter;
        _waterSize = Water.Size;
        _requestedWaterSize = Water.Size;
        _camera = new OrbitCamera();
        AttachStyle(_style);
        Input = new MarinaInputController(this);
        Designer = new MarinaDesigner(this);
    }

    /// <inheritdoc/>
    public event EventHandler<BerthEventArgs>? BerthClicked;

    /// <inheritdoc/>
    public event EventHandler<BerthSelectedEventArgs>? BerthSelected;

    /// <inheritdoc/>
    public event EventHandler<MultiBerthSelectedEventArgs>? MultiBerthSelected;

    /// <inheritdoc/>
    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    /// <inheritdoc/>
    public event EventHandler? SelectionCleared;

    /// <inheritdoc/>
    public event EventHandler<BerthActionInvokedEventArgs>? BerthActionInvoked;

    /// <inheritdoc/>
    public event EventHandler<BerthPopupChangedEventArgs>? PopupChanged;

    /// <inheritdoc/>
    public event EventHandler<BerthHoverEventArgs>? BerthHoverChanged;

    /// <inheritdoc/>
    public event EventHandler<BerthStatusChangedEventArgs>? BerthStatusChanged;

    /// <inheritdoc/>
    public event EventHandler<LayoutChangedEventArgs>? LayoutChanged;

    /// <summary>
    /// Raised when something changed that the view has to draw: the layout, a status, the hover or selection, the style,
    /// the designer's drawing. Raised once until the next <see cref="BuildRenderFrame"/>, however much changes in between,
    /// so a view that stops drawing while nothing changes (see <see cref="NeedsRedraw"/>) knows to start again.
    /// </summary>
    /// <remarks>
    /// Raised on the thread that made the change, which for a WinForms or Blazor view has to be its UI thread: the
    /// visualizer is not thread-safe.
    /// </remarks>
    public event EventHandler? RedrawRequested;

    /// <inheritdoc/>
    public string MarinaName
    {
        get => _marinaName;
        set => _marinaName = string.IsNullOrWhiteSpace(value) ? "Marina" : value.Trim();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Its <see cref="OrbitCamera.Constraints"/> (how far it may wander and pull back) follow the layout; they are
    /// brought up to date whenever the camera is fetched from here, not on every change to the layout.
    /// </remarks>
    public OrbitCamera Camera
    {
        get
        {
            EnsureCameraBounds();
            return _camera;
        }
    }

    private readonly OrbitCamera _camera;

    /// <summary>
    /// Platform-neutral input handling. Host views forward pointer and keyboard events here; configure drag bindings,
    /// zoom step and hover through it.
    /// </summary>
    public MarinaInputController Input { get; }

    /// <inheritdoc/>
    public LightingSettings Lighting => _style.Lighting;

    /// <inheritdoc/>
    public WaterSettings Water => _style.Water;

    /// <inheritdoc/>
    public MarinaStyle Style
    {
        get => _style;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(value, _style)) return;
            DetachStyle(_style);
            _style = value;
            AttachStyle(value);
            RebuildLandMeshes();
            MarkSceneDirty();
            RequestPopupRefresh();
        }
    }

    /// <inheritdoc/>
    public MarinaDesigner Designer { get; }

    /// <summary>
    /// Meshes available to the scene. Register a replacement under an existing id
    /// (e.g. <see cref="MeshIds.ForBoat"/>) to swap in imported models.
    /// </summary>
    public MeshLibrary Meshes { get; }

    /// <summary>
    /// Current status colors and overlay opacities (read-only view). Change them with <see cref="SetStatusColor"/>,
    /// <see cref="SetDisabledColor"/>, <see cref="SetOverlayOpacity"/> and <see cref="ResetStatusColors"/>.
    /// </summary>
    public StatusColorScheme Colors => _style.Status;

    /// <summary>
    /// Which berths have their name (<see cref="Berth.DisplayName"/>) written on the water next to their open end,
    /// sized to fit the berth's width. Hidden and filtered-out berths are never labeled. Default <see cref="BerthLabelMode.None"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="Api.BerthLabelMode"/>.</exception>
    public BerthLabelMode BerthLabelMode
    {
        get => _berthLabelMode;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value), value, null);
            if (value == _berthLabelMode) return;
            _berthLabelMode = value;
            MarkBerthsDirty();
        }
    }

    /// <summary>Seconds of animation time accumulated through <see cref="Update(double, bool)"/>.</summary>
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

        // How wide the view is decides how far back the automatic views have to stand. They are worked out again
        // when next asked for, not on every step of a window being dragged to a new size.
        InvalidatePresets();
    }

    /// <summary>Advances animation time and camera smoothing.</summary>
    public void Update(double deltaSeconds) => Update(deltaSeconds, animate: true);

    /// <summary>
    /// Advances camera smoothing and, when <paramref name="animate"/> is true, animation time. With it false the waves,
    /// floating boats, pulses and traffic stay where they are while the camera still eases to where it was sent: what a
    /// view does when its animation is switched off.
    /// </summary>
    /// <param name="deltaSeconds">Seconds since the last update. Long gaps are capped, so a view left alone does not jump.</param>
    /// <param name="animate">False to leave animation time where it is.</param>
    public void Update(double deltaSeconds, bool animate)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0d) return;
        var dt = Math.Min(deltaSeconds, 0.25d); // Avoid jumps after the window was suspended.
        if (animate) _time += dt;
        Camera.Update((float)dt);
    }

    /// <summary>
    /// True when the view has to draw a frame to be up to date: something changed since the last <see cref="BuildRenderFrame"/>
    /// (the scene, the camera or the view size, the lighting, the water) or the camera is still easing toward where it was sent.
    /// Leaves out what moves by itself; see <see cref="IsAnimating"/>.
    /// </summary>
    /// <remarks>
    /// A view that draws only when this or <see cref="IsAnimating"/> is true costs nothing while the marina sits still. Changes
    /// made through the visualizer, and to <see cref="Lighting"/>, <see cref="Water"/> and the rest of <see cref="Style"/>,
    /// also raise <see cref="RedrawRequested"/>; changes made to the camera directly are only seen here, so poll it now and then.
    /// </remarks>
    public bool NeedsRedraw =>
        _redrawRequested || _scene.IsDirty || Camera.IsMoving || Designer.OverlayNeedsRefresh() ||
        Camera.GetViewMatrix() != _drawn.View ||
        Camera.GetProjectionMatrix(_viewportSize.X / _viewportSize.Y) != _drawn.Projection ||
        FrameLighting.FromLightingSettings(FrameLightingSource()) != _drawn.Lighting ||
        FrameWater.FromWaterSettings(Water) != _drawn.Water;

    /// <summary>
    /// True when the picture moves by itself, so the view has to keep drawing even though nothing changed: waves (and the boats
    /// riding them), a pulsing selection, a spinning selection marker, or passing traffic.
    /// </summary>
    public bool IsAnimating =>
        FrameWater.FromWaterSettings(Water).IsMoving || _scene.HasMarkers || _scene.HasPulse || _trafficField is { Vessels.Count: > 0 };

    /// <summary>Asks the view for a new frame: sets <see cref="NeedsRedraw"/> and raises <see cref="RedrawRequested"/>.</summary>
    public void RequestRedraw()
    {
        if (_redrawRequested) return;
        _redrawRequested = true;
        RedrawRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Builds the frame description for a renderer. Only the parts of the scene that changed are built again, each in a
    /// layer of its own (see <see cref="RenderFrame.Layers"/>), so the renderer uploads only those.
    /// </summary>
    public RenderFrame BuildRenderFrame()
    {
        // Water.Size can be set at any time; the grid is rebuilt when it actually changes, not when the grid has
        // merely been grown to cover the layout. Only looked at after the water section said it changed.
        if (_waterChanged)
        {
            _waterChanged = false;
            if (MathF.Abs(Water.Size - _requestedWaterSize) > 0.5f) SetWaterGrid(_waterCenter, MathF.Max(50f, Water.Size));
        }

        EnsureCameraBounds();

        if (Designer.OverlayNeedsRefresh()) _scene.Invalidate(SceneChanges.Overlay);
        _scene.Update(CreateSceneState, Designer.AppendOverlay);
        UpdateTraffic();

        var view = Camera.GetViewMatrix();
        var projection = Camera.GetProjectionMatrix(_viewportSize.X / _viewportSize.Y);
        var lighting = FrameLighting.FromLightingSettings(FrameLightingSource());
        var water = FrameWater.FromWaterSettings(Water);
        _drawn = (view, projection, lighting, water);
        _redrawRequested = false;

        return new RenderFrame
        {
            View = view,
            Projection = projection,
            CameraPosition = Camera.Position,
            Time = (float)(_time % 3600d),
            Lighting = lighting,
            Water = water,
            Layers = _scene.All,
            Objects = _scene.Flat,
            SceneVersion = _scene.Version,
            MarinaCenter = _marinaCenter,
            WaterCenter = _waterCenter,
            WaterDetailRadius = _waterSize * 0.5f,
            Meshes = Meshes,
            ReferenceImage = Designer.BuildImageLayer(),
        };
    }

    /// <summary>Forces every layer of the scene to be rebuilt on the next frame.</summary>
    public void InvalidateScene() => Invalidate(SceneChanges.All);

    /// <summary>Where the frame's lighting comes from: the style's, with the fog thinned while designing (see <see cref="DesignLighting"/>).</summary>
    private LightingSettings FrameLightingSource() => Designer.IsActive && Designer.FogFactor < 1f ? DesignLighting() : Lighting;

    private SceneState CreateSceneState() => new()
    {
        Piers = OrderedPiers(),
        Berths = OrderedBerths().ToList(),
        Dividers = OrderedDividers(),
        Land = OrderedLandAreas(),
        HasShoreline = _shoreline is not null,
        LandMeshId = land => _landMeshSlots.TryGetValue(land.Id, out var slot) ? MeshIds.ForLand(slot) : -1,
        LandScenery = land => _landScenery.TryGetValue(land.Id, out var scenery) ? scenery : [],
        ShorelineScenery = _shorelineScenery,
        LandLookup = GetLandArea,
        BerthLookup = GetBerth,
        PierLookup = GetPier,
        MultiBerthLookup = GetMultiBerth,
        Filter = _statusFilter,
        Selected = new HashSet<string>(_selection, IdComparer),
        PrimarySelectedId = _selection.Count > 0 ? _selection[^1] : null,
        HoveredBerthId = _hoveredBerthId,
        LabelMode = _berthLabelMode,
        Style = _style,
        Meshes = Meshes,
    };

    /// <summary>Moves the passing traffic on and puts it in its layer, the one part of the scene rewritten every frame.</summary>
    private void UpdateTraffic()
    {
        if (_trafficDirty && (_traffic.IsEnabled || _trafficField is not null)) ReplanTraffic();
        AdvanceTraffic();
        _trafficObjects.Clear();
        foreach (var vessel in _trafficField?.Vessels ?? [])
        {
            if (vessel.Opacity <= 0.004f) continue;
            var world = MarinaMath.CreatePlacement(Vector3.One, vessel.HeadingDegrees, MarinaMath.ToWorld(vessel.Position));
            _trafficObjects.Add(new RenderObject(
                MeshIds.ForBoat(vessel.Type),
                world,
                new Vector4(1f, 1f, 1f, vessel.Opacity),
                0f,
                RenderAnimation.FloatOnWater,
                vessel.Position.X * 0.11f + vessel.Position.Y * 0.07f));
        }

        _scene.SetTraffic(_trafficObjects);
    }

    /// <summary>
    /// Hit-tests a point in view pixels (origin top-left) against visible, unfiltered berths and boats.
    /// Disabled berths are hit (so they block what's behind them), but input ignores them.
    /// </summary>
    /// <remarks>
    /// Answered from a pick set kept until the scene changes — the boats with their placements, the pads in a grid over
    /// the plan — so hovering over a large marina tests only what stands under the pointer.
    /// </remarks>
    public BerthHit? HitTest(float x, float y) => HitTest(Camera.ScreenPointToRay(x, y, _viewportSize.X, _viewportSize.Y));

    private BerthHit? HitTest(Ray ray)
    {
        var version = ((long)_pickVersion << 32) | (uint)Meshes.Version;
        if (_pickSet is null || _pickSet.Version != version)
        {
            var berths = OrderedBerths().Where(IsShown).ToList();
            var boats = BerthPlacement.EnumerateBoats(berths, GetBerth, GetMultiBerth, _statusFilter, GroundHeight, Meshes);
            _pickSet = new PickSet(berths, boats, Meshes, GroundHeight, version);
        }

        return _pickSet.Pick(ray);
    }

    /// <summary>What a pointer can hit changed (a berth, a boat, the filter): the pick set is built again when next needed.</summary>
    private void InvalidatePickSet() => _pickVersion++;

    private PickSet? _pickSet;
    private int _pickVersion;

    /// <summary>Point on the water plane under a view pixel, if the ray hits it.</summary>
    public Vector3? GetWaterPoint(float x, float y)
    {
        var ray = Camera.ScreenPointToRay(x, y, _viewportSize.X, _viewportSize.Y);
        return ray.IntersectHorizontalPlane(0f, out var distance) ? ray.GetPoint(distance) : null;
    }

    /// <summary>
    /// What is under a view pixel: the nearest boat or berth, else the top of a land area or the mainland, else the
    /// water. Null when the pixel shows only sky. What the wheel zooms toward.
    /// </summary>
    internal Vector3? GetGroundPoint(float x, float y)
    {
        var ray = Camera.ScreenPointToRay(x, y, _viewportSize.X, _viewportSize.Y);
        if (HitTest(ray) is { } hit) return hit.WorldPoint;

        float? nearest = null;
        foreach (var land in OrderedLandAreas())
        {
            if (ray.IntersectHorizontalPlane(land.Height, out var distance) && (nearest is null || distance < nearest) &&
                land.Contains(MarinaMath.ToPlan(ray.GetPoint(distance))))
            {
                nearest = distance;
            }
        }

        if (_shoreline is { } shore && ray.IntersectHorizontalPlane(LandMeshFactory.ShorelineGroundHeight(shore), out var ashore) &&
            (nearest is null || ashore < nearest) && shore.Contains(MarinaMath.ToPlan(ray.GetPoint(ashore))))
        {
            nearest = ashore;
        }

        if (nearest is { } found) return ray.GetPoint(found);
        return ray.IntersectHorizontalPlane(0f, out var water) ? ray.GetPoint(water) : null;
    }

    /// <summary>Projects a world point to view pixels (origin top-left). False when the point is behind the camera.</summary>
    public bool TryProjectToScreen(Vector3 worldPoint, out Vector2 screenPoint)
    {
        screenPoint = _camera.WorldToScreen(worldPoint, _viewportSize.X, _viewportSize.Y) ?? default;
        return screenPoint != default && float.IsFinite(screenPoint.X) && float.IsFinite(screenPoint.Y);
    }

    /// <summary>
    /// Where the popup should point, in view pixels: just above the primary selected berth's selection marker.
    /// Call every frame (the camera moves). False when no popup is open or the anchor is behind the camera.
    /// </summary>
    public bool TryGetPopupAnchor(out Vector2 screenPoint)
    {
        screenPoint = default;
        return _popup is { } popup && GetBerth(popup.PrimaryBerth.Id) is { } berth && TryProjectToScreen(GetPopupAnchorWorld(berth), out screenPoint);
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private LightingSettings? _designLighting;

    private StatusColorScheme _colors => _style.Status;

    private void AttachStyle(MarinaStyle style)
    {
        // Each section of the style feeds only some of the layers, so a change to it rebuilds only those.
        style.Piers.Changed += OnPierStyleChanged;
        style.Selection.Changed += OnBerthStyleChanged;
        style.Status.Changed += OnStatusStyleChanged;
        style.Land.Changed += OnLandStyleChanged;
        style.Labels.Changed += OnLabelStyleChanged;
        style.View.Changed += OnViewStyleChanged;
        style.Lighting.Changed += OnLightingChanged;
        style.Water.Changed += OnWaterChanged;
        _waterChanged = true;
        ApplyViewStyle(style.View);
        RegisterLabelFont(style.Labels.Font);
    }

    private void DetachStyle(MarinaStyle style)
    {
        style.Piers.Changed -= OnPierStyleChanged;
        style.Selection.Changed -= OnBerthStyleChanged;
        style.Status.Changed -= OnStatusStyleChanged;
        style.Land.Changed -= OnLandStyleChanged;
        style.Labels.Changed -= OnLabelStyleChanged;
        style.View.Changed -= OnViewStyleChanged;
        style.Lighting.Changed -= OnLightingChanged;
        style.Water.Changed -= OnWaterChanged;
    }

    /// <summary>The lighting goes straight into the frame, so a change needs no rebuilding, only a new frame.</summary>
    private void OnLightingChanged(object? sender, EventArgs e) => RequestRedraw();

    /// <summary>As the lighting; and the next frame checks whether the water's size changed with it.</summary>
    private void OnWaterChanged(object? sender, EventArgs e)
    {
        _waterChanged = true;
        RequestRedraw();
    }

    /// <summary>The water section changed since the last frame (or the style was replaced), so its size is worth comparing.</summary>
    private bool _waterChanged = true;

    private void OnPierStyleChanged(object? sender, EventArgs e) => Invalidate(SceneChanges.Structure);

    private void OnBerthStyleChanged(object? sender, EventArgs e) => MarkBerthsDirty();

    /// <summary>Status colors also appear in the popup's accent.</summary>
    private void OnStatusStyleChanged(object? sender, EventArgs e)
    {
        MarkBerthsDirty();
        RequestPopupRefresh();
    }

    private void OnLandStyleChanged(object? sender, EventArgs e)
    {
        foreach (var land in OrderedLandAreas()) RegisterLandMesh(land);
        RegisterShorelineMesh();
        MarkSceneDirty();
    }

    private void OnLabelStyleChanged(object? sender, EventArgs e)
    {
        RegisterLabelFont(_style.Labels.Font);
        MarkBerthsDirty();
    }

    /// <summary>
    /// Puts the glyphs of a captured font into the mesh library, and takes the previous font's out again. Called
    /// whenever the label style changes, and cheap when the font is the one already registered.
    /// </summary>
    /// <param name="font">The font now in use, or null for the built-in lettering.</param>
    private void RegisterLabelFont(LabelFontDefinition? font)
    {
        if (ReferenceEquals(font, _registeredLabelFont)) return;

        if (_registeredLabelFont is not null)
        {
            foreach (var mesh in _registeredLabelFont.CreateMeshes()) Meshes.Unregister(mesh.Id);
        }

        if (font is not null)
        {
            foreach (var mesh in font.CreateMeshes()) Meshes.Register(mesh);
        }

        _registeredLabelFont = font;
        MarkBerthsDirty();
    }

    private void OnViewStyleChanged(object? sender, EventArgs e) => ApplyViewStyle(_style.View);

    private void ApplyViewStyle(ViewStyle view)
    {
        var fieldOfViewChanged = _camera.FieldOfViewDegrees != view.FieldOfViewDegrees;
        _camera.FieldOfViewDegrees = view.FieldOfViewDegrees;
        _camera.Smoothing = view.CameraSmoothing;

        // A wider or narrower lens changes how far back every automatic view and the camera limit have to be.
        if (!fieldOfViewChanged) return;
        _boundsDirty = true;
        InvalidatePresets();
    }

    /// <summary>Rebuilds the water grid around <paramref name="center"/>.</summary>
    private void SetWaterGrid(Vector2 center, float size)
    {
        _requestedWaterSize = Water.Size;
        // Keep the cell size of the configured grid as it grows (within limits).
        // Clamped as a float first: a grid grown far past the configured size would overflow the int. The configured
        // resolution is itself held within the maximum, so the bounds never cross.
        var grown = MathF.Min(MathF.Round(Water.GridResolution * size / Water.Size), WaterSettings.MaxGridResolution);
        var resolution = Math.Clamp(float.IsFinite(grown) ? (int)grown : Water.GridResolution, Water.GridResolution, Math.Max(Water.GridResolution, WaterSettings.MaxGridResolution));
        Meshes.Register(MarinaMeshFactory.CreateWaterGrid(MeshIds.Water, size, resolution, center));
        _waterCenter = center;
        _waterSize = size;
    }

    /// <summary>Re-centers and, if needed, enlarges the water grid when the layout or the reference image reaches past its edges.</summary>
    private void EnsureWaterCovers(Vector2 min, Vector2 max)
    {
        const float margin = 150f;
        _marinaCenter = (min + max) * 0.5f;
        var half = _waterSize * 0.5f;
        if (min.X - margin >= _waterCenter.X - half && min.Y - margin >= _waterCenter.Y - half &&
            max.X + margin <= _waterCenter.X + half && max.Y + margin <= _waterCenter.Y + half)
        {
            return;
        }

        var extent = MathF.Max(max.X - min.X, max.Y - min.Y);
        SetWaterGrid((min + max) * 0.5f, MathF.Max(Water.Size, extent + 4f * margin));
    }

    /// <summary><see cref="Lighting"/> with the fog thinned by <see cref="MarinaDesigner.FogFactor"/>, so a whole marina seen from high above stays clear.</summary>
    private LightingSettings DesignLighting()
    {
        var copy = _designLighting ??= new LightingSettings();
        copy.SunDirection = Lighting.SunDirection;
        copy.SunColor = Lighting.SunColor;
        copy.AmbientColor = Lighting.AmbientColor;
        copy.SpecularStrength = Lighting.SpecularStrength;
        copy.Shininess = Lighting.Shininess;
        copy.SkyColor = Lighting.SkyColor;
        copy.FogColor = Lighting.FogColor;
        copy.FogDensity = Lighting.FogDensity * Designer.FogFactor;
        return copy;
    }

    private Vector3 GetPopupAnchorWorld(Berth berth)
    {
        var ground = GroundHeight(berth);
        var boatTop = berth.Boat is { } boat && berth.Status.CanHaveBoat() ? BerthPlacement.BoatTopHeight(boat, Meshes, ground) : (ground ?? 0f) + 3f;
        return MarinaMath.ToWorld(berth.Center, SceneBuilder.MarkerBaseHeight(boatTop, ground ?? 0f) + SceneBuilder.MarkerTop);
    }

    /// <summary>Land height under a land berth; null for water berths.</summary>
    private float? GroundHeight(Berth berth) => SceneBuilder.GroundHeight(berth, GetLandArea);

    private IEnumerable<LandArea> OrderedLandAreas() => _landAreas.Values;

    /// <summary>Drops every land mesh and builds one per current land area, in layout order (slots 0, 1, ...).</summary>
    private void RebuildLandMeshes()
    {
        foreach (var slot in _landMeshSlots.Values) Meshes.Unregister(MeshIds.ForLand(slot));
        _landMeshSlots.Clear();
        _landScenery.Clear();
        foreach (var land in OrderedLandAreas()) RegisterLandMesh(land);
        RegisterShorelineMesh();
    }

    /// <summary>
    /// Builds the mainland's ground mesh and the instances of what stands on it, or drops them when there is no shoreline.
    /// </summary>
    private void RegisterShorelineMesh()
    {
        if (_shoreline is null)
        {
            Meshes.Unregister(MeshIds.Shoreline);
            _shorelineScenery = [];
            return;
        }

        Meshes.Register(LandMeshFactory.CreateShorelineGround(MeshIds.Shoreline, _shoreline, _style.Land));
        _shorelineScenery = LandMeshFactory.CreateShorelineSceneryInstances(_shoreline, _style.Land);
    }

    /// <summary>
    /// Lays out the traffic lanes again. Called whenever the traffic settings or anything a lane has to keep clear of
    /// — the piers, the land, the mainland — changes.
    /// </summary>
    private void ReplanTraffic()
    {
        var bounds = MarinaLayout.ComputeBounds(
            OrderedPiers().Select(pier => pier.Bounds)
                .Concat(OrderedBerths().Select(berth => berth.Bounds))
                .Concat(OrderedDividers().Select(divider => divider.Bounds)),
            OrderedLandAreas());

        // Each lane runs from one edge of the map to the other, sweeping past the marina at the clearance asked
        // for, so its ends are far outside the detailed water while its middle runs through the part anyone is
        // watching.
        var lanes = MarineTrafficPlanner.Plan(_traffic, bounds, _shoreline);

        // The same settings on new lanes (a pier moved, land was drawn) keep the vessels already out there where they are:
        // starting them all over from the seed would send every one of them jumping back to where it began. Only new
        // settings start the traffic afresh.
        _trafficField = lanes.Count == 0 ? null
            : _trafficField is { } current && current.Settings == _traffic ? new MarineTrafficField(current, lanes)
            : new MarineTrafficField(_traffic, lanes);
        _trafficTime = _time;
        _trafficDirty = false;
    }

    /// <summary>
    /// Builds (or rebuilds) the ground mesh of one land area, keeping its slot so other land meshes are untouched, and the
    /// instances of the rocks and trees that stand on it.
    /// </summary>
    /// <exception cref="InvalidOperationException">There are already <see cref="MeshIds.MaxLandSlots"/> land areas.</exception>
    /// <param name="land">The land area.</param>
    /// <param name="rebuildGround">False when only what stands on it changed (its trees), so the ground mesh is kept.</param>
    private void RegisterLandMesh(LandArea land, bool rebuildGround = true)
    {
        if (!_landMeshSlots.TryGetValue(land.Id, out var slot))
        {
            // The lowest slot free, so adding and removing land areas over and over never walks the ids into another range.
            var used = new HashSet<int>(_landMeshSlots.Values);
            slot = 0;
            while (used.Contains(slot)) slot++;
            if (slot >= MeshIds.MaxLandSlots) throw new InvalidOperationException($"A marina can have at most {MeshIds.MaxLandSlots} land areas.");
            _landMeshSlots[land.Id] = slot;
        }

        // Rocks and trees are instances of a few shared meshes rather than part of the ground: far less to build and
        // upload.
        if (rebuildGround || !Meshes.TryGet(MeshIds.ForLand(slot), out _))
        {
            Meshes.Register(LandMeshFactory.CreateGroundBase(MeshIds.ForLand(slot), land, _style.Land));
        }

        _landScenery[land.Id] = LandMeshFactory.CreateSceneryInstances(land, _style.Land);
    }

    private void UnregisterLandMesh(string landAreaId)
    {
        _landScenery.Remove(landAreaId);
        if (!_landMeshSlots.Remove(landAreaId, out var slot)) return;
        Meshes.Unregister(MeshIds.ForLand(slot));
    }

    private IEnumerable<Pier> OrderedPiers() => _piers.Values;

    private IEnumerable<Berth> OrderedBerths() => _berths.Values;

    private IEnumerable<Divider> OrderedDividers() => _dividers.Values;

    private IEnumerable<MultiBerth> OrderedMultiBerths() => _multiBerths.Values;

    /// <summary>The berths along a pier, in layout order, from the index rather than a walk over every berth.</summary>
    private IEnumerable<Berth> BerthsOfPier(string pierId) => _berths.WithKey(ByPier, pierId);

    /// <summary>The berths ashore on a land area, in layout order.</summary>
    private IEnumerable<Berth> BerthsOfLandArea(string landAreaId) => _berths.WithKey(ByLandArea, landAreaId);

    /// <summary>Drawn with its status visuals: visible and not excluded by the status filter.</summary>
    private bool IsShown(Berth berth) => berth.IsVisible && _statusFilter.Includes(berth.Status);

    /// <summary>Can be part of the selection.</summary>
    private bool IsSelectable(Berth berth) => berth.IsInteractive && _statusFilter.Includes(berth.Status);

    /// <summary>True when replacing a berth changes the structure it stands in, not just what its status shows.</summary>
    private static bool ShapesStructure(Berth before, Berth after) => SceneBuilder.ShapesStructure(before, after);

    /// <summary>Something about the layout or its style changed: everything is built again.</summary>
    internal void MarkSceneDirty() => Invalidate(SceneChanges.All);

    /// <summary>Only the designer's overlay (or the traffic lanes) changed.</summary>
    internal void MarkOverlayDirty() => Invalidate(SceneChanges.Overlay);

    /// <summary>A berth's status, boat or label, the status filter or the label mode changed; the piers stay as they are.</summary>
    internal void MarkBerthsDirty() => Invalidate(SceneChanges.Berths);

    /// <summary>The hover or the selection changed: only the berths concerned and the markers are drawn again.</summary>
    internal void MarkHighlightDirty() => Invalidate(SceneChanges.Highlight);

    private void Invalidate(SceneChanges changes)
    {
        _scene.Invalidate(changes);
        RequestRedraw();
    }

    private void RaiseLayoutChanged(LayoutChangeKind kind, string? pierId = null, string? berthId = null, string? dividerId = null, string? multiBerthId = null, string? landAreaId = null)
    {
        // Anything that moves a pier, a land area or the shore can change where a lane is allowed to run.
        _trafficDirty = true;

        // What is under the pointer may have changed: a berth moved, a boat arrived or left, one was hidden.
        InvalidatePickSet();

        // A berth or divider coming, going or moving can join or part neighbours. (A berth that was only updated says so
        // itself, since most updates are a new status or boat and change nothing about where it lies.)
        if (ShapesConnections(kind)) _connectionsDirty = true;

        var change = new LayoutChange(kind, pierId, berthId, dividerId, multiBerthId, landAreaId);
        if (_updateDepth > 0)
        {
            // Told once, when the scope ends, as one BatchUpdated listing every change in the order made.
            _pendingChanges.Add(change);
            return;
        }

        RefreshConnections();
        LayoutChanged?.Invoke(this, new LayoutChangedEventArgs(kind, pierId, berthId, dividerId, multiBerthId, landAreaId));
    }

    /// <summary>
    /// True for the changes that can join or part neighbouring berths. <see cref="LayoutChangeKind.BerthUpdated"/> is not
    /// one of them: whoever stores the update marks the connections themselves when the berth moved.
    /// </summary>
    private static bool ShapesConnections(LayoutChangeKind kind) => kind is
        LayoutChangeKind.Initialized or LayoutChangeKind.Cleared or
        LayoutChangeKind.PierAdded or LayoutChangeKind.PierUpdated or LayoutChangeKind.PierRemoved or LayoutChangeKind.PierRenamed or
        LayoutChangeKind.BerthAdded or LayoutChangeKind.BerthRemoved or LayoutChangeKind.BerthRenamed or
        LayoutChangeKind.DividerAdded or LayoutChangeKind.DividerUpdated or LayoutChangeKind.DividerRemoved or
        LayoutChangeKind.LandAreaAdded or LayoutChangeKind.LandAreaRemoved;

    /// <summary>
    /// Works out every berth's <see cref="Berth.ConnectedBerthIds"/> again once something has moved, and stores the berths
    /// whose connections changed. Connections follow from the layout, so this raises no event of its own: the change that
    /// caused it is reported right after, and whoever reads the berths then sees them up to date.
    /// </summary>
    private void RefreshConnections()
    {
        if (!_connectionsDirty) return;
        _connectionsDirty = false;

        var connections = BerthConnections.Compute(_berths.Values, _dividers.Values);
        foreach (var berth in _berths.Values.ToArray())
        {
            var now = connections.TryGetValue(berth.Id, out var found) ? found : [];
            if (berth.ConnectedBerthIds.SequenceEqual(now, StringComparer.Ordinal)) continue;
            _berths[berth.Id] = berth with { ConnectedBerthIds = now };
            if (IsBerthSelected(berth.Id)) _selectedSnapshot = null;
        }
    }

    private Berth RequireBerth(string berthId)
    {
        ArgumentNullException.ThrowIfNull(berthId);
        return _berths.TryGetValue(berthId, out var berth)
            ? berth
            : throw new KeyNotFoundException($"Berth '{berthId}' does not exist.");
    }

    private BerthEventArgs CreateBerthArgs(Berth berth, PointerButton button = PointerButton.None, bool isDoubleClick = false, Vector3? worldPoint = null) =>
        new(berth, berth.PierId is null ? null : GetPier(berth.PierId), button, isDoubleClick, worldPoint)
        {
            LandArea = berth.LandAreaId is null ? null : GetLandArea(berth.LandAreaId),
        };

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
