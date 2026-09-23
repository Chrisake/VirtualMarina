using System.Collections;
using System.Numerics;

namespace VirtualMarina.Core.Rendering;

/// <summary>Which parts of the scene have to be built again.</summary>
[Flags]
internal enum SceneChanges
{
    None = 0,

    /// <summary>The layout or its style: land, piers, dividers, fingers. Their shadows follow.</summary>
    Structure = 1,

    /// <summary>Berth statuses, boats, labels, the status filter or the status colors. Boat shadows and markers follow.</summary>
    Berths = 2,

    /// <summary>Hover or selection: rewrites the affected berths in place and redraws the markers.</summary>
    Highlight = 4,

    /// <summary>The designer's overlay or the traffic lanes.</summary>
    Overlay = 8,

    /// <summary>The sun or the shadow style.</summary>
    Shadows = 16,

    All = Structure | Berths | Highlight | Overlay | Shadows,
}

/// <summary>
/// The scene as the layers of <see cref="RenderLayerKind"/>, each rebuilt only when what it shows has changed. A hover
/// rewrites a handful of instances in place; a status update rebuilds the berths but not the piers; traffic moving
/// touches nothing but itself.
/// </summary>
internal sealed class SceneLayers
{
    private static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;

    private readonly RenderLayer _structure = new(RenderLayerKind.Structure);
    private readonly RenderLayer _structureShadows = new(RenderLayerKind.StructureShadows);
    private readonly RenderLayer _berthShadows = new(RenderLayerKind.BerthShadows);
    private readonly RenderLayer _berths = new(RenderLayerKind.Berths);
    private readonly RenderLayer _highlight = new(RenderLayerKind.Highlight);
    private readonly RenderLayer _overlay = new(RenderLayerKind.Overlay);
    private readonly RenderLayer _traffic = new(RenderLayerKind.Traffic);
    private readonly RenderLayer[] _all;

    private readonly List<RenderObject> _scratch = [];
    private readonly List<ShadowCaster> _structureCasters = [];
    private readonly List<ShadowCaster> _berthCasters = [];
    private readonly BerthLayerContent _berthContent = new();
    private readonly HashSet<string> _drawnSelection = new(IdComparer);
    private string? _drawnHover;
    private (bool Cast, Vector3 Sun, float Strength) _drawnShadows;
    private SceneChanges _dirty = SceneChanges.All;
    private FlatObjects? _flat;

    public SceneLayers() => _all = [_structure, _structureShadows, _berthShadows, _berths, _highlight, _overlay, _traffic];

    /// <summary>Every layer, in drawing order.</summary>
    public IReadOnlyList<RenderLayer> All => _all;

    /// <summary>True while something still has to be built.</summary>
    public bool IsDirty => _dirty != SceneChanges.None;

    /// <summary>The highest version of any layer: changes whenever anything in the scene does.</summary>
    public int Version => _all.Max(layer => layer.Version);

    /// <summary>True when a selection marker is drawn, which spins and bobs.</summary>
    public bool HasMarkers => _highlight.Count > 0;

    /// <summary>True when a highlighted berth or boat pulses.</summary>
    public bool HasPulse { get; private set; }

    /// <summary>World Y of the top of each berth's boat, as last drawn.</summary>
    public IReadOnlyDictionary<string, float> BoatTops => _berthContent.BoatTops;

    /// <summary>The whole scene as one list, worked out only if someone asks for it.</summary>
    public IReadOnlyList<RenderObject> Flat
    {
        get
        {
            var version = Version;
            if (_flat is null || _flat.Version != version) _flat = new FlatObjects(_all, version);
            return _flat;
        }
    }

    public void Invalidate(SceneChanges changes) => _dirty |= changes;

    /// <summary>Builds whatever is out of date.</summary>
    /// <param name="style">The style, which says whether and where shadows fall.</param>
    /// <param name="createState">Gathers the scene; called only when a layer that needs it is out of date.</param>
    /// <param name="overlay">Adds the designer's overlay.</param>
    public void Update(MarinaStyle style, Func<SceneState> createState, Action<List<RenderObject>> overlay)
    {
        var shadows = (Cast: SceneBuilder.CastsShadows(style), Sun: style.Lighting.SunDirection, Strength: style.Shadows.Strength);
        if (shadows != _drawnShadows) _dirty |= SceneChanges.Shadows;

        var dirty = _dirty;
        _dirty = SceneChanges.None;
        if ((dirty & (SceneChanges.Structure | SceneChanges.Berths | SceneChanges.Highlight)) == 0)
        {
            UpdateShadowsAndOverlay(dirty, shadows, overlay);
            return;
        }

        var state = createState();

        if ((dirty & SceneChanges.Structure) != 0)
        {
            SceneBuilder.BuildStructure(_scratch, _structureCasters, state);
            _structure.Rebuild(_scratch);
            dirty |= SceneChanges.Shadows;
        }

        if ((dirty & SceneChanges.Berths) != 0 || ((dirty & SceneChanges.Highlight) != 0 && !TryPatchHighlight(state)))
        {
            SceneBuilder.BuildBerths(_berthContent, _berthCasters, state);
            _berths.Rebuild(_berthContent.Objects);
            dirty |= SceneChanges.Highlight | SceneChanges.Shadows;
        }

        if ((dirty & SceneChanges.Highlight) != 0)
        {
            _drawnSelection.Clear();
            _drawnSelection.UnionWith(state.Selected);
            _drawnHover = state.HoveredBerthId;
            HasPulse = state.Style.Selection.Pulse && _drawnSelection.Count > 0;
            SceneBuilder.BuildHighlight(_scratch, state, _berthContent.BoatTops);
            _highlight.Rebuild(_scratch);
        }

        UpdateShadowsAndOverlay(dirty, shadows, overlay);
    }

    private void UpdateShadowsAndOverlay(SceneChanges dirty, (bool Cast, Vector3 Sun, float Strength) shadows, Action<List<RenderObject>> overlay)
    {
        if ((dirty & SceneChanges.Shadows) != 0)
        {
            _drawnShadows = shadows;
            CastShadows(_structureShadows, _structureCasters, shadows);
            CastShadows(_berthShadows, _berthCasters, shadows);
        }

        if ((dirty & SceneChanges.Overlay) != 0)
        {
            _scratch.Clear();
            overlay(_scratch);
            _overlay.Rebuild(_scratch);
        }
    }

    /// <summary>Replaces the passing traffic.</summary>
    public void SetTraffic(IReadOnlyList<RenderObject> vessels) => _traffic.Rebuild(vessels);

    private void CastShadows(RenderLayer layer, List<ShadowCaster> casters, (bool Cast, Vector3 Sun, float Strength) shadows)
    {
        if (shadows.Cast) SceneBuilder.CastShadows(_scratch, casters, shadows.Sun, shadows.Strength);
        else _scratch.Clear();
        layer.Rebuild(_scratch);
    }

    /// <summary>
    /// Rewrites, in place, the berths and boats whose hover or selection changed. False when one of them would come out
    /// with a different number of objects, or in another pass: the berths layer is then rebuilt instead.
    /// </summary>
    private bool TryPatchHighlight(SceneState state)
    {
        var affected = new HashSet<string>(_drawnSelection, IdComparer);
        affected.SymmetricExceptWith(state.Selected);
        if (!IdComparer.Equals(_drawnHover, state.HoveredBerthId))
        {
            if (_drawnHover is not null) affected.Add(_drawnHover);
            if (state.HoveredBerthId is not null) affected.Add(state.HoveredBerthId);
        }

        var units = new SortedSet<int>();
        foreach (var id in affected)
        {
            if (_berthContent.UnitsByBerth.TryGetValue(id, out var list)) units.UnionWith(list);
        }

        if (units.Count == 0) return true;

        var version = RenderLayer.NextVersion();
        foreach (var index in units)
        {
            var (start, count, berth, boat) = _berthContent.Units[index];
            SceneBuilder.RebuildUnit(_scratch, berth, boat, state);
            if (_scratch.Count != count || !_berths.TryPatch(start, _scratch, version)) return false;
        }

        return true;
    }

    /// <summary>The layers' instances end to end, copied out on first use.</summary>
    private sealed class FlatObjects(IReadOnlyList<RenderLayer> layers, int version) : IReadOnlyList<RenderObject>
    {
        private RenderObject[]? _objects;

        public int Version { get; } = version;

        public int Count => Objects.Length;

        private RenderObject[] Objects
        {
            get
            {
                if (_objects is not null) return _objects;
                var all = new RenderObject[layers.Sum(layer => layer.Count)];
                var at = 0;
                foreach (var layer in layers)
                {
                    layer.Instances.Span.CopyTo(all.AsSpan(at));
                    at += layer.Count;
                }

                return _objects = all;
            }
        }

        public RenderObject this[int index] => Objects[index];

        public IEnumerator<RenderObject> GetEnumerator() => ((IEnumerable<RenderObject>)Objects).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
