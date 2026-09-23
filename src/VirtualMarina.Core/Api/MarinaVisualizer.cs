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

    private readonly Dictionary<string, Pier> _piers = new(IdComparer);
    private readonly List<string> _pierOrder = [];
    private readonly Dictionary<string, Berth> _berths = new(IdComparer);
    private readonly List<string> _berthOrder = [];
    private readonly Dictionary<string, Divider> _dividers = new(IdComparer);
    private readonly List<string> _dividerOrder = [];
    private readonly Dictionary<string, MultiBerth> _multiBerths = new(IdComparer);
    private readonly List<string> _multiBerthOrder = [];
    private readonly Dictionary<string, LandArea> _landAreas = new(IdComparer);
    private readonly List<string> _landOrder = [];
    private Shoreline? _shoreline;
    private MarineTraffic _traffic = MarineTraffic.None;
    private MarineTrafficField? _trafficField;
    private double _trafficTime;
    private bool _showTrafficLanes;
    private LabelFontDefinition? _registeredLabelFont;
    private bool _trafficDirty = true;
    private readonly List<RenderObject> _frameObjects = [];
    private int _staticObjectCount = -1;
    private readonly Dictionary<string, int> _landMeshSlots = new(IdComparer);
    private int _nextLandMeshSlot;
    private readonly List<CameraPreset> _presets = [];
    private readonly List<RenderObject> _renderObjects = [];
    private MarinaStyle _style;

    private string? _hoveredBerthId;
    private BerthStatusFilter _statusFilter = BerthStatusFilter.All;
    private BerthLabelMode _berthLabelMode = BerthLabelMode.None;
    private bool _sceneDirty = true;
    private int _sceneVersion;
    private int _updateDepth;
    private bool _layoutChangePending;
    private double _time;
    private Vector2 _viewportSize = new(1280f, 720f);
    private Vector2 _waterCenter;
    private Vector2 _marinaCenter;
    private float _waterSize;
    private float _requestedWaterSize;
    private Vector3 _shadowSun = Vector3.UnitY;
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
        Camera = new OrbitCamera();
        AttachStyle(_style);
        Input = new MarinaInputController(this);
        Designer = new MarinaDesigner(this);
        RebuildBuiltInPresets();
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

    /// <inheritdoc/>
    public string MarinaName
    {
        get => _marinaName;
        set => _marinaName = string.IsNullOrWhiteSpace(value) ? "Marina" : value.Trim();
    }

    /// <inheritdoc/>
    public OrbitCamera Camera { get; }

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

        // How wide the view is decides how far back the automatic views have to stand.
        RebuildBuiltInPresets();
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
        // Water.Size can be set at any time; the grid is rebuilt when it actually changes, not when the grid has
        // merely been grown to cover the layout.
        if (MathF.Abs(Water.Size - _requestedWaterSize) > 0.5f) SetWaterGrid(_waterCenter, MathF.Max(50f, Water.Size));

        // Shadows are worked out where the scene is built, so moving the sun has to rebuild it. Lighting otherwise
        // only feeds uniforms, so this is the one thing about it the scene cares about.
        if (_style.Shadows.IsEnabled && Vector3.DistanceSquared(Lighting.SunDirection, _shadowSun) > 1e-8f)
        {
            _shadowSun = Lighting.SunDirection;
            _sceneDirty = true;
        }

        if (Designer.OverlayNeedsRefresh()) _sceneDirty = true;
        if (_sceneDirty)
        {
            SceneBuilder.Build(_renderObjects, new SceneState
            {
                Piers = OrderedPiers(),
                Berths = OrderedBerths().ToList(),
                Dividers = OrderedDividers(),
                Land = OrderedLandAreas(),
                HasShoreline = _shoreline is not null,
                LandMeshId = land => MeshIds.ForLand(_landMeshSlots.TryGetValue(land.Id, out var slot) ? slot : -1),
                LandTreesMeshId = land => MeshIds.ForLandTrees(_landMeshSlots.TryGetValue(land.Id, out var slot) ? slot : -1),
                ShorelineGroundHeight = _shoreline is { } shore ? LandMeshFactory.ShorelineGroundHeight(shore) : 0f,
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
                Overlay = Designer.AppendOverlay,
            });
            _sceneVersion++;
            _sceneDirty = false;
            _staticObjectCount = -1;   // the scene changed under the traffic, so the whole list is rebuilt below
        }

        var objects = AppendTraffic();

        return new RenderFrame
        {
            View = Camera.GetViewMatrix(),
            Projection = Camera.GetProjectionMatrix(_viewportSize.X / _viewportSize.Y),
            CameraPosition = Camera.Position,
            Time = (float)(_time % 3600d),
            Lighting = Designer.IsActive && Designer.FogFactor < 1f ? DesignLighting() : Lighting,
            Water = Water,
            Objects = objects,
            SceneVersion = _sceneVersion,
            MarinaCenter = _marinaCenter,
            WaterCenter = _waterCenter,
            WaterDetailRadius = _waterSize * 0.5f,
            Meshes = Meshes,
            ReferenceImage = Designer.BuildImageLayer(),
        };
    }

    /// <summary>Forces the render object list to be rebuilt on the next frame.</summary>
    public void InvalidateScene() => _sceneDirty = true;

    /// <summary>
    /// The scene with the passing traffic put back on top of it. The still part is copied only when the scene itself
    /// changed; the traffic is a short tail that is rewritten every frame.
    /// </summary>
    private List<RenderObject> AppendTraffic()
    {
        if (_trafficDirty && (_traffic.IsEnabled || _trafficField is not null)) ReplanTraffic();
        AdvanceTraffic();
        if (_trafficField is null || _trafficField.Vessels.Count == 0)
        {
            // Nothing moving: the renderer can have the scene list itself and keep its uploaded instance data.
            _staticObjectCount = -1;
            return _renderObjects;
        }

        if (_staticObjectCount < 0)
        {
            _frameObjects.Clear();
            _frameObjects.AddRange(_renderObjects);
            _staticObjectCount = _renderObjects.Count;
        }
        else
        {
            _frameObjects.RemoveRange(_staticObjectCount, _frameObjects.Count - _staticObjectCount);
        }

        foreach (var vessel in _trafficField.Vessels)
        {
            if (vessel.Opacity <= 0.004f) continue;
            var world = MarinaMath.CreatePlacement(Vector3.One, vessel.HeadingDegrees, MarinaMath.ToWorld(vessel.Position));
            _frameObjects.Add(new RenderObject(
                MeshIds.ForBoat(vessel.Type),
                world,
                new Vector4(1f, 1f, 1f, vessel.Opacity),
                0f,
                RenderAnimation.FloatOnWater,
                vessel.Position.X * 0.11f + vessel.Position.Y * 0.07f));
        }

        // The instance data moved, so the backend has to re-upload it.
        _sceneVersion++;
        return _frameObjects;
    }

    /// <summary>
    /// Hit-tests a point in view pixels (origin top-left) against visible, unfiltered berths and boats.
    /// Disabled berths are hit (so they block what's behind them), but input ignores them.
    /// </summary>
    public BerthHit? HitTest(float x, float y)
    {
        var ray = Camera.ScreenPointToRay(x, y, _viewportSize.X, _viewportSize.Y);
        var berths = OrderedBerths().Where(IsShown).ToList();
        var boats = BerthPlacement.EnumerateBoats(berths, GetBerth, GetMultiBerth, _statusFilter, GroundHeight, Meshes);
        return ScenePicker.Pick(ray, berths, boats, Meshes, GroundHeight);
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
        foreach (var section in style.SceneSections) section.Changed += OnSceneStyleChanged;
        style.Status.Changed += OnStatusStyleChanged;
        style.Land.Changed += OnLandStyleChanged;
        style.Labels.Changed += OnLabelStyleChanged;
        style.View.Changed += OnViewStyleChanged;
        ApplyViewStyle(style.View);
        RegisterLabelFont(style.Labels.Font);
    }

    private void DetachStyle(MarinaStyle style)
    {
        foreach (var section in style.SceneSections) section.Changed -= OnSceneStyleChanged;
        style.Status.Changed -= OnStatusStyleChanged;
        style.Land.Changed -= OnLandStyleChanged;
        style.Labels.Changed -= OnLabelStyleChanged;
        style.View.Changed -= OnViewStyleChanged;
    }

    private void OnSceneStyleChanged(object? sender, EventArgs e) => MarkSceneDirty();

    /// <summary>Status colors also appear in the popup's accent.</summary>
    private void OnStatusStyleChanged(object? sender, EventArgs e) => RequestPopupRefresh();

    private void OnLandStyleChanged(object? sender, EventArgs e)
    {
        foreach (var land in OrderedLandAreas()) RegisterLandMesh(land);
        RegisterShorelineMesh();
        MarkSceneDirty();
    }

    private void OnLabelStyleChanged(object? sender, EventArgs e) => RegisterLabelFont(_style.Labels.Font);

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
        MarkSceneDirty();
    }

    private void OnViewStyleChanged(object? sender, EventArgs e) => ApplyViewStyle(_style.View);

    private void ApplyViewStyle(ViewStyle view)
    {
        Camera.FieldOfViewDegrees = view.FieldOfViewDegrees;
        Camera.Smoothing = view.CameraSmoothing;
    }

    /// <summary>Rebuilds the water grid around <paramref name="center"/>.</summary>
    private void SetWaterGrid(Vector2 center, float size)
    {
        _requestedWaterSize = Water.Size;
        // Keep the cell size of the configured grid as it grows (within limits).
        var resolution = Math.Clamp((int)MathF.Round(Water.GridResolution * size / Water.Size), Water.GridResolution, 400);
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

    private IEnumerable<LandArea> OrderedLandAreas() => _landOrder.Select(id => _landAreas[id]);

    /// <summary>Drops every land mesh and builds one per current land area, in layout order (slots 0, 1, ...).</summary>
    private void RebuildLandMeshes()
    {
        foreach (var slot in _landMeshSlots.Values)
        {
            Meshes.Unregister(MeshIds.ForLand(slot));
            Meshes.Unregister(MeshIds.ForLandTrees(slot));
        }

        _landMeshSlots.Clear();
        _nextLandMeshSlot = 0;
        foreach (var land in OrderedLandAreas()) RegisterLandMesh(land);
        RegisterShorelineMesh();
    }

    /// <summary>Builds the mainland's mesh, or drops it when there is no shoreline.</summary>
    private void RegisterShorelineMesh()
    {
        if (_shoreline is null)
        {
            Meshes.Unregister(MeshIds.Shoreline);
            Meshes.Unregister(MeshIds.ShorelineScenery);
            return;
        }

        Meshes.Register(LandMeshFactory.CreateShorelineGround(MeshIds.Shoreline, _shoreline, _style.Land));
        Meshes.Register(LandMeshFactory.CreateShorelineScenery(MeshIds.ShorelineScenery, _shoreline, _style.Land));
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
        _trafficField = lanes.Count == 0 ? null : new MarineTrafficField(_traffic, lanes);
        _trafficTime = _time;
        _trafficDirty = false;
        _staticObjectCount = -1;
    }

    /// <summary>Builds (or rebuilds) the mesh of one land area, keeping its slot so other land meshes are untouched.</summary>
    private void RegisterLandMesh(LandArea land)
    {
        if (!_landMeshSlots.TryGetValue(land.Id, out var slot))
        {
            slot = _nextLandMeshSlot++;
            _landMeshSlots[land.Id] = slot;
        }

        // Ground and trees are separate meshes: the trees are squashed onto the ground to cast their shadow.
        Meshes.Register(LandMeshFactory.CreateGround(MeshIds.ForLand(slot), land, _style.Land));
        Meshes.Register(LandMeshFactory.CreateTrees(MeshIds.ForLandTrees(slot), land, _style.Land));
    }

    private void UnregisterLandMesh(string landAreaId)
    {
        if (!_landMeshSlots.Remove(landAreaId, out var slot)) return;
        Meshes.Unregister(MeshIds.ForLand(slot));
        Meshes.Unregister(MeshIds.ForLandTrees(slot));
    }

    private IEnumerable<Pier> OrderedPiers() => _pierOrder.Select(id => _piers[id]);

    private IEnumerable<Berth> OrderedBerths() => _berthOrder.Select(id => _berths[id]);

    private IEnumerable<Divider> OrderedDividers() => _dividerOrder.Select(id => _dividers[id]);

    private IEnumerable<MultiBerth> OrderedMultiBerths() => _multiBerthOrder.Select(id => _multiBerths[id]);

    /// <summary>Drawn with its status visuals: visible and not excluded by the status filter.</summary>
    private bool IsShown(Berth berth) => berth.IsVisible && _statusFilter.Includes(berth.Status);

    /// <summary>Can be part of the selection.</summary>
    private bool IsSelectable(Berth berth) => berth.IsInteractive && _statusFilter.Includes(berth.Status);

    internal void MarkSceneDirty() => _sceneDirty = true;

    private void RaiseLayoutChanged(LayoutChangeKind kind, string? pierId = null, string? berthId = null, string? dividerId = null, string? multiBerthId = null, string? landAreaId = null)
    {
        // Anything that moves a pier, a land area or the shore can change where a lane is allowed to run.
        _trafficDirty = true;
        if (_updateDepth > 0)
        {
            _layoutChangePending = true;
            return;
        }

        LayoutChanged?.Invoke(this, new LayoutChangedEventArgs(kind, pierId, berthId, dividerId, multiBerthId, landAreaId));
    }

    private Berth RequireBerth(string berthId)
    {
        ArgumentNullException.ThrowIfNull(berthId);
        return _berths.TryGetValue(berthId, out var berth)
            ? berth
            : throw new KeyNotFoundException($"Berth '{berthId}' does not exist.");
    }

    private BerthEventArgs CreateBerthArgs(Berth berth, PointerButton button = PointerButton.None, bool isDoubleClick = false, Vector3? worldPoint = null) =>
        new(berth, berth.PierId is null ? null : _piers.GetValueOrDefault(berth.PierId), button, isDoubleClick, worldPoint)
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
