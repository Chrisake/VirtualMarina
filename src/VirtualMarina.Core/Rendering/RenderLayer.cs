namespace VirtualMarina.Core.Rendering;

/// <summary>
/// What a <see cref="RenderLayer"/> holds. The layers of a frame come in this order, which is also the order their
/// transparent instances are drawn in.
/// </summary>
/// <remarks>
/// The scene is split by how often each part changes, so that a change rebuilds, and a backend uploads, only the part
/// it touched: hovering a berth never re-sends the piers, and passing traffic never re-sends the berths.
/// </remarks>
public enum RenderLayerKind
{
    /// <summary>A whole scene in one layer: what a frame built from a flat <see cref="RenderFrame.Objects"/> list has.</summary>
    Scene = 0,

    /// <summary>What only a change to the layout or the style moves: land, trees, piers, pedestals, dividers and fingers.</summary>
    Structure = 1,

    /// <summary>The shadows of <see cref="Structure"/>, which the sun moves on their own.</summary>
    StructureShadows = 2,

    /// <summary>The shadows of the boats.</summary>
    BerthShadows = 3,

    /// <summary>What a berth's status shows: boats, status pads, buoys and labels.</summary>
    Berths = 4,

    /// <summary>The selection markers.</summary>
    Highlight = 5,

    /// <summary>The designer's drawing and measurements, and the traffic lanes while they are shown.</summary>
    Overlay = 6,

    /// <summary>The passing traffic, which moves every frame.</summary>
    Traffic = 7,
}

/// <summary>Which pass of a frame an instance is drawn in.</summary>
public enum RenderPass
{
    /// <summary>Before the water, depth-written, in any order.</summary>
    Opaque = 0,

    /// <summary>
    /// After the water, blended with depth writes off, before any <see cref="Transparent"/> instance. These are the
    /// shadows: every one has the same color and opacity, so the order they are drawn in makes no difference.
    /// </summary>
    Shadow = 1,

    /// <summary>After the shadows, blended with depth writes off, in order (back to front where the backend sorts).</summary>
    Transparent = 2,
}

/// <summary>A run of a layer's instances that share one mesh and one pass, drawn with a single instanced draw call.</summary>
/// <param name="MeshId">The mesh every instance of the run draws (see <see cref="Geometry.MeshLibrary"/>).</param>
/// <param name="Pass">The pass the run belongs to.</param>
/// <param name="Start">Index of the run's first instance in <see cref="RenderLayer.Instances"/>.</param>
/// <param name="Count">How many instances the run has.</param>
public readonly record struct RenderBatch(int MeshId, RenderPass Pass, int Start, int Count);

/// <summary>A stretch of a layer's instances, by index.</summary>
/// <param name="Start">Index of the first instance.</param>
/// <param name="Count">How many instances.</param>
public readonly record struct InstanceRange(int Start, int Count);

/// <summary>
/// One part of the scene: its instances grouped into <see cref="Batches"/> by mesh and pass, with version numbers that
/// tell a backend what, if anything, it has to upload again.
/// </summary>
/// <remarks>
/// <para>
/// A backend keeps, per layer kind, the <see cref="LayoutVersion"/> and <see cref="Version"/> it last uploaded.
/// A different <see cref="LayoutVersion"/> means the instances or batches were laid out afresh: upload everything.
/// The same <see cref="LayoutVersion"/> with a different <see cref="Version"/> means some instances were rewritten in
/// place; <see cref="TryGetChangesSince"/> says which, so only those need to be sent.
/// </para>
/// <para>
/// Versions come from one counter for the whole process, so a layer from another visualizer never looks like one
/// already uploaded. The layer object is reused frame after frame and changes only while the visualizer builds a frame:
/// read it on the thread that builds frames.
/// </para>
/// </remarks>
public sealed class RenderLayer
{
    /// <summary>How many in-place changes are remembered; older ones force a full upload.</summary>
    private const int MaxLoggedChanges = 256;

    private static int s_version;

    private readonly List<(int Version, InstanceRange Range)> _changes = [];
    private readonly List<RenderBatch> _batches = [];
    private RenderObject[] _instances = [];
    private int[] _placement = [];
    private int _count;
    private int _changeFloor;

    internal RenderLayer(RenderLayerKind kind) => Kind = kind;

    /// <summary>What this layer holds.</summary>
    public RenderLayerKind Kind { get; }

    /// <summary>Changes whenever any instance changes. Unique across every layer in the process.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// Changes whenever the instances are laid out afresh (their number, meshes or passes changed), which calls for a full
    /// upload. Unique across every layer in the process.
    /// </summary>
    public int LayoutVersion { get; private set; }

    /// <summary>The instances, batch by batch (see <see cref="Batches"/>).</summary>
    public ReadOnlyMemory<RenderObject> Instances => _instances.AsMemory(0, _count);

    /// <summary>How many instances the layer has.</summary>
    public int Count => _count;

    /// <summary>
    /// The runs to draw: opaque batches first, then shadow batches, then the transparent ones in the order they are to be drawn.
    /// </summary>
    public IReadOnlyList<RenderBatch> Batches => _batches;

    /// <summary>
    /// A layer holding the given objects, e.g. for a frame put together by hand. Its versions are both
    /// <paramref name="version"/>, so a backend uploads it again only when that number changes.
    /// </summary>
    /// <param name="kind">What the layer holds.</param>
    /// <param name="objects">The objects, in the order they were meant to be drawn.</param>
    /// <param name="version">The version to report.</param>
    public static RenderLayer FromObjects(RenderLayerKind kind, IEnumerable<RenderObject> objects, int version)
    {
        ArgumentNullException.ThrowIfNull(objects);
        var layer = new RenderLayer(kind);
        layer.Assemble(objects as IReadOnlyList<RenderObject> ?? objects.ToList());
        layer.Version = version;
        layer.LayoutVersion = version;
        layer._changeFloor = version;
        return layer;
    }

    /// <summary>
    /// Adds the stretches of instances rewritten in place since <paramref name="version"/> to <paramref name="changes"/>.
    /// Returns false when that is not known — the layout changed since, or too much changed to keep track of — in which
    /// case the whole layer has to be uploaded again.
    /// </summary>
    /// <param name="version">The <see cref="Version"/> the caller last uploaded.</param>
    /// <param name="changes">Receives the changed stretches, merged and in index order.</param>
    public bool TryGetChangesSince(int version, ICollection<InstanceRange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (version == Version) return true;
        if (version < _changeFloor || version > Version) return false;

        var ranges = new List<InstanceRange>();
        foreach (var (changed, range) in _changes)
        {
            if (changed > version) ranges.Add(range);
        }

        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
        var merged = default(InstanceRange?);
        foreach (var range in ranges)
        {
            if (merged is { } open && range.Start <= open.Start + open.Count)
            {
                merged = open with { Count = Math.Max(open.Count, range.Start + range.Count - open.Start) };
                continue;
            }

            if (merged is { } done) changes.Add(done);
            merged = range;
        }

        if (merged is { } last) changes.Add(last);
        return true;
    }

    /// <summary>The next version number, unique in the process.</summary>
    internal static int NextVersion() => Interlocked.Increment(ref s_version);

    /// <summary>
    /// Replaces the contents with <paramref name="emitted"/>, grouped into batches. Returns false, leaving the versions as
    /// they were, when that is exactly what the layer already holds; when only some instances differ they are logged as
    /// in-place changes, so a backend sends just those.
    /// </summary>
    /// <param name="emitted">The objects in the order they were produced.</param>
    internal bool Rebuild(IReadOnlyList<RenderObject> emitted)
    {
        var previous = _instances.AsSpan(0, _count).ToArray();
        var previousBatches = _batches.ToArray();
        Assemble(emitted);

        if (previousBatches.AsSpan().SequenceEqual(_batches.ToArray()) && Version != 0)
        {
            var version = 0;
            var start = -1;
            for (var i = 0; i <= _count; i++)
            {
                var differs = i < _count && !previous[i].Equals(_instances[i]);
                if (differs && start < 0) start = i;
                if (differs || start < 0) continue;

                if (version == 0) version = NextVersion();
                LogChange(version, new InstanceRange(start, i - start));
                start = -1;
            }

            if (version == 0) return false;
            Version = version;
            return true;
        }

        Version = NextVersion();
        LayoutVersion = Version;
        _changes.Clear();
        _changeFloor = Version;
        return true;
    }

    /// <summary>
    /// Where the object emitted at <paramref name="emittedIndex"/> by the last <see cref="Rebuild"/> ended up in
    /// <see cref="Instances"/>.
    /// </summary>
    internal int PlacementOf(int emittedIndex) => _placement[emittedIndex];

    /// <summary>
    /// Rewrites instances in place, as long as each keeps its mesh and pass. Returns false, changing nothing, when one
    /// would not: the caller rebuilds the layer instead.
    /// </summary>
    /// <param name="emittedStart">Index, in the order of the last <see cref="Rebuild"/>, of the first object to replace.</param>
    /// <param name="replacement">The new objects, as many as they replace.</param>
    /// <param name="version">The version the change is logged under (one per set of patches).</param>
    internal bool TryPatch(int emittedStart, IReadOnlyList<RenderObject> replacement, int version)
    {
        if (emittedStart < 0 || emittedStart + replacement.Count > _placement.Length) return false;
        for (var i = 0; i < replacement.Count; i++)
        {
            var current = _instances[_placement[emittedStart + i]];
            if (current.MeshId != replacement[i].MeshId || PassOf(current) != PassOf(replacement[i])) return false;
        }

        for (var i = 0; i < replacement.Count; i++)
        {
            var at = _placement[emittedStart + i];
            if (_instances[at].Equals(replacement[i])) continue;
            _instances[at] = replacement[i];
            LogChange(version, new InstanceRange(at, 1));
            Version = version;
        }

        return true;
    }

    /// <summary>The pass an object is drawn in.</summary>
    internal static RenderPass PassOf(in RenderObject obj) =>
        !obj.IsTransparent ? RenderPass.Opaque
        : (obj.Animation & RenderAnimation.Unlit) != 0 ? RenderPass.Shadow
        : RenderPass.Transparent;

    private void LogChange(int version, InstanceRange range)
    {
        if (_changes.Count > 0 && _changes[^1] is var (lastVersion, last) && lastVersion == version && last.Start + last.Count == range.Start)
        {
            _changes[^1] = (version, last with { Count = last.Count + range.Count });
            return;
        }

        _changes.Add((version, range));
        if (_changes.Count <= MaxLoggedChanges) return;

        // Too much to track: anyone further back than the oldest change still remembered uploads the whole layer.
        _changeFloor = _changes[_changes.Count / 2].Version;
        _changes.RemoveAll(change => change.Version <= _changeFloor);
    }

    /// <summary>
    /// Lays the objects out batch by batch. Opaque and shadow objects are grouped by mesh, in the order each mesh first
    /// appears, since their order does not change the picture; transparent ones keep their order exactly, split into a
    /// new batch wherever the mesh changes.
    /// </summary>
    private void Assemble(IReadOnlyList<RenderObject> emitted)
    {
        var count = emitted.Count;
        if (_instances.Length < count) _instances = new RenderObject[Math.Max(count, _instances.Length * 2)];
        if (_placement.Length != count) _placement = new int[count];
        _count = count;
        _batches.Clear();

        var batchOf = new int[count];
        var sizes = new List<int>();
        var keys = new List<(int MeshId, RenderPass Pass)>();
        var grouped = new Dictionary<(int, RenderPass), int>();
        var lastTransparent = -1;
        for (var i = 0; i < count; i++)
        {
            var obj = emitted[i];
            var pass = PassOf(obj);
            int batch;
            if (pass == RenderPass.Transparent)
            {
                if (lastTransparent >= 0 && keys[lastTransparent].MeshId == obj.MeshId)
                {
                    batch = lastTransparent;
                }
                else
                {
                    batch = keys.Count;
                    keys.Add((obj.MeshId, pass));
                    sizes.Add(0);
                    lastTransparent = batch;
                }
            }
            else if (!grouped.TryGetValue((obj.MeshId, pass), out batch))
            {
                batch = keys.Count;
                grouped[(obj.MeshId, pass)] = batch;
                keys.Add((obj.MeshId, pass));
                sizes.Add(0);
            }

            batchOf[i] = batch;
            sizes[batch]++;
        }

        // Opaque batches, then shadows, then the transparent runs, each in the order they were first met.
        var order = Enumerable.Range(0, keys.Count).OrderBy(b => (int)keys[b].Pass).ThenBy(b => b).ToArray();
        var starts = new int[keys.Count];
        var next = 0;
        foreach (var b in order)
        {
            starts[b] = next;
            _batches.Add(new RenderBatch(keys[b].MeshId, keys[b].Pass, next, sizes[b]));
            next += sizes[b];
        }

        var filled = new int[keys.Count];
        for (var i = 0; i < count; i++)
        {
            var at = starts[batchOf[i]] + filled[batchOf[i]]++;
            _instances[at] = emitted[i];
            _placement[i] = at;
        }

        Array.Clear(_instances, count, _instances.Length - count);
    }
}
