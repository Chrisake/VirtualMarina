using System.Numerics;

namespace VirtualMarina.Core.Rendering;

/// <summary>What a backend has to send for one layer of a frame.</summary>
public enum LayerUpload
{
    /// <summary>Already uploaded as it is.</summary>
    None = 0,

    /// <summary>Some instances changed in place: send only the ranges <see cref="LayerUploadTracker.Check"/> listed.</summary>
    Changes = 1,

    /// <summary>New, or laid out afresh: send every instance and the batches.</summary>
    Full = 2,
}

/// <summary>
/// Remembers which version of each <see cref="RenderLayer"/> a backend has uploaded, and says for each layer of a new
/// frame whether to send nothing, some instances, or everything.
/// </summary>
/// <example>
/// <code>
/// foreach (var layer in frame.Layers)
/// {
///     switch (tracker.Check(layer, ranges))
///     {
///         case LayerUpload.Full: UploadAll(layer); break;
///         case LayerUpload.Changes: foreach (var range in ranges) UploadRange(layer, range); break;
///     }
///     tracker.Uploaded(layer);
/// }
/// foreach (var kind in tracker.RemoveMissing(frame.Layers)) DeleteLayer(kind);
/// </code>
/// </example>
public sealed class LayerUploadTracker
{
    private readonly Dictionary<RenderLayerKind, (int LayoutVersion, int Version)> _uploaded = [];

    /// <summary>What to send for <paramref name="layer"/>.</summary>
    /// <param name="layer">A layer of the frame being drawn.</param>
    /// <param name="changes">Cleared, then filled with the instance ranges to send when the answer is <see cref="LayerUpload.Changes"/>.</param>
    public LayerUpload Check(RenderLayer layer, ICollection<InstanceRange> changes)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(changes);
        changes.Clear();
        if (!_uploaded.TryGetValue(layer.Kind, out var uploaded) || uploaded.LayoutVersion != layer.LayoutVersion) return LayerUpload.Full;
        if (uploaded.Version == layer.Version) return LayerUpload.None;
        return layer.TryGetChangesSince(uploaded.Version, changes) ? LayerUpload.Changes : LayerUpload.Full;
    }

    /// <summary>Records that <paramref name="layer"/> is now uploaded as it is.</summary>
    public void Uploaded(RenderLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        _uploaded[layer.Kind] = (layer.LayoutVersion, layer.Version);
    }

    /// <summary>Forgets the layers the frame no longer has, and returns their kinds so their GPU copies can be freed.</summary>
    /// <param name="layers">The layers of the frame being drawn.</param>
    public IReadOnlyList<RenderLayerKind> RemoveMissing(IReadOnlyList<RenderLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var missing = _uploaded.Keys.Where(kind => !layers.Any(layer => layer.Kind == kind)).ToList();
        foreach (var kind in missing) _uploaded.Remove(kind);
        return missing;
    }

    /// <summary>Forgets everything, e.g. after the graphics context was lost: every layer is sent whole again.</summary>
    public void Clear() => _uploaded.Clear();
}

/// <summary>
/// Puts the transparent instances of a frame in back-to-front order, which blending needs where they overlap, and
/// groups consecutive instances of one mesh into runs a backend can draw instanced.
/// </summary>
/// <remarks>
/// Shadows (<see cref="RenderPass.Shadow"/>) are left out: they all have the same color and opacity, so their order
/// does not matter, and they are drawn before everything sorted here.
/// </remarks>
public sealed class TransparentSorter
{
    private readonly List<(float Distance, int Layer, int Index)> _keys = [];
    private RenderObject[] _sorted = [];
    private int _count;
    private readonly List<RenderBatch> _runs = [];
    private Vector3 _sortedFrom = new(float.NaN);
    private int _sortedVersion = int.MinValue;

    /// <summary>The transparent instances, farthest first. Valid until the next <see cref="Sort"/>.</summary>
    public ReadOnlySpan<RenderObject> Sorted => _sorted.AsSpan(0, _count);

    /// <summary>Runs of consecutive <see cref="Sorted"/> instances sharing a mesh, in drawing order.</summary>
    public IReadOnlyList<RenderBatch> Runs => _runs;

    /// <summary>
    /// Sorts the transparent instances of <paramref name="layers"/> by their distance from <paramref name="camera"/>.
    /// Returns false, leaving the order as it was, when neither the camera nor the scene changed since the last call;
    /// true when <see cref="Sorted"/> and <see cref="Runs"/> are new and must be uploaded again.
    /// </summary>
    /// <param name="layers">The frame's layers.</param>
    /// <param name="camera">The eye position.</param>
    /// <param name="sceneVersion">The frame's <see cref="RenderFrame.SceneVersion"/>, which changes with any layer.</param>
    public bool Sort(IReadOnlyList<RenderLayer> layers, Vector3 camera, int sceneVersion)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (sceneVersion == _sortedVersion && camera == _sortedFrom) return false;
        _sortedVersion = sceneVersion;
        _sortedFrom = camera;

        _keys.Clear();
        for (var l = 0; l < layers.Count; l++)
        {
            var instances = layers[l].Instances.Span;
            foreach (var batch in layers[l].Batches)
            {
                if (batch.Pass != RenderPass.Transparent) continue;
                for (var i = batch.Start; i < batch.Start + batch.Count; i++)
                {
                    _keys.Add((Vector3.DistanceSquared(instances[i].World.Translation, camera), l, i));
                }
            }
        }

        // Farthest first; ties keep the order the scene gave them, so the result is the same frame after frame.
        _keys.Sort((a, b) => b.Distance != a.Distance ? b.Distance.CompareTo(a.Distance) : a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer) : a.Index.CompareTo(b.Index));

        if (_sorted.Length < _keys.Count) _sorted = new RenderObject[Math.Max(_keys.Count, _sorted.Length * 2)];
        _count = 0;
        _runs.Clear();
        foreach (var (_, layer, index) in _keys)
        {
            var instance = layers[layer].Instances.Span[index];
            if (_runs.Count > 0 && _runs[^1].MeshId == instance.MeshId)
            {
                _runs[^1] = _runs[^1] with { Count = _runs[^1].Count + 1 };
            }
            else
            {
                _runs.Add(new RenderBatch(instance.MeshId, RenderPass.Transparent, _count, 1));
            }

            _sorted[_count++] = instance;
        }

        return true;
    }
}
