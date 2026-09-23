using System.Runtime.InteropServices;
using Microsoft.JSInterop;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Blazor;

/// <summary>
/// <see cref="ISceneRenderer"/> backed by WebGL 2 through synchronous JS interop (Blazor WebAssembly).
/// </summary>
/// <remarks>
/// <para>
/// Everything crosses the boundary as raw bytes (<c>byte[]</c>, which Blazor hands to JavaScript as a
/// <c>Uint8Array</c> without Base64): meshes once per mesh, a layer's instances only when that layer changed, and then
/// only the instances that changed. Every mesh is drawn instanced, one draw call per batch of a layer
/// (see <see cref="RenderLayer.Batches"/>). Per frame, one buffer of 82 floats carries the camera, lighting, water and time
/// uniforms, the reference image's placement and the popup's anchor; waves, bobbing and pulses animate on the GPU.
/// </para>
/// <para>
/// Transparent instances are drawn back to front, as the OpenGL backend draws them: <see cref="TransparentSorter"/> orders
/// them here, and the ordered instances are sent only when the camera or the scene moved and the order came out
/// different from the one the script already has.
/// </para>
/// <para>
/// <c>[JSImport]</c>/<c>[JSExport]</c> would save the JSON wrapper of each call, but it only exists in the browser
/// runtime and would replace the module object the component and its tests talk to, so the calls stay on
/// <see cref="IJSInProcessObjectReference"/>.
/// </para>
/// </remarks>
public sealed class WebGlSceneRenderer : ISceneRenderer
{
    /// <summary>Floats in the per-frame buffer (layout mirrored in marinaWebGL.js).</summary>
    internal const int FrameLength = 82;

    /// <summary>Where the per-frame buffer's slots start (layout mirrored in marinaWebGL.js).</summary>
    internal const int UniformsLength = 70;
    internal const int ImageSlot = 70;
    internal const int AnchorSlot = 79;

    private readonly IJSInProcessObjectReference _module;
    private readonly int _viewId;
    private readonly byte[] _frame = new byte[FrameLength * sizeof(float)];
    private readonly Dictionary<int, Core.Geometry.MeshData> _uploadedMeshes = [];
    private readonly LayerUploadTracker _uploads = new();
    private readonly List<InstanceRange> _changes = [];
    private TransparentSorter _sorter = new();
    private RenderObject[] _sentTransparent = [];
    private bool _transparentSent;
    private float[] _packed = [];
    private (bool Visible, float X, float Y) _anchor;
    private int _uploadedImageKey = -1;
    private int _uploadedLibraryVersion = -1;
    private bool _disposed;

    internal WebGlSceneRenderer(IJSInProcessObjectReference module, int viewId)
    {
        _module = module;
        _viewId = viewId;
    }

    /// <inheritdoc/>
    public string BackendName => "WebGL 2 (Blazor WebAssembly)";

    /// <summary>GPU renderer and WebGL version reported by the browser, available after <see cref="Initialize"/>.</summary>
    public string? DeviceDescription { get; private set; }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">A shader failed to compile or link in the browser.</exception>
    public void Initialize()
    {
        var error = _module.Invoke<string?>(
            "initRenderer",
            _viewId,
            ShaderSources.InstancedModelVertex(ShaderDialect.WebGL2),
            ShaderSources.InstancedModelFragment(ShaderDialect.WebGL2),
            ShaderSources.WaterVertex(ShaderDialect.WebGL2),
            ShaderSources.WaterFragment(ShaderDialect.WebGL2),
            ShaderSources.ImageVertex(ShaderDialect.WebGL2),
            ShaderSources.ImageFragment(ShaderDialect.WebGL2),
            ShaderSources.ImageQuadCorners);
        if (error is not null) throw new InvalidOperationException("WebGL initialization failed: " + error);

        DeviceDescription = _module.Invoke<string?>("getDeviceDescription", _viewId);
    }

    /// <summary>The JS side sizes the canvas drawing buffer from its CSS size and devicePixelRatio.</summary>
    public void Resize(int pixelWidth, int pixelHeight)
    {
    }

    /// <inheritdoc/>
    public void Render(RenderFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (frame.MeshLibraryVersion != _uploadedLibraryVersion)
        {
            // Only new or replaced meshes cross the boundary (a large breakwater isn't re-sent when a pier is drawn).
            var current = new HashSet<int>();
            foreach (var mesh in frame.Meshes.All)
            {
                current.Add(mesh.Id);
                if (_uploadedMeshes.TryGetValue(mesh.Id, out var uploaded) && ReferenceEquals(uploaded, mesh)) continue;
                _module.InvokeVoid("uploadMesh", _viewId, mesh.Id, Bytes<float>(mesh.Vertices), Bytes<uint>(mesh.Indices), mesh.IsWater);
                _uploadedMeshes[mesh.Id] = mesh;
            }

            foreach (var stale in _uploadedMeshes.Keys.Where(id => !current.Contains(id)).ToList())
            {
                _module.InvokeVoid("deleteMesh", _viewId, stale);
                _uploadedMeshes.Remove(stale);
            }

            _uploadedLibraryVersion = frame.MeshLibraryVersion;
        }

        SyncLayers(frame.Layers);
        SyncTransparent(frame);
        PackFrame(frame);

        // True when the browser lost the WebGL context and has given it back: the meshes, layers and image uploaded
        // before are gone, so forget them and everything is sent again with the next frame.
        if (_module.Invoke<bool>("renderFrame", _viewId, _frame)) ForgetUploads();
    }

    /// <summary>
    /// Where the selection popup points on the next frame, in CSS pixels relative to the canvas, or null to hide it. The
    /// script positions the popup as part of drawing the frame, so it follows the camera without a call of its own.
    /// </summary>
    internal void SetPopupAnchor(System.Numerics.Vector2? anchor) =>
        _anchor = anchor is { } at ? (true, at.X, at.Y) : (false, 0f, 0f);

    /// <summary>Records that the script already has the texture for this image (decoded when it was loaded), so it is not sent again.</summary>
    internal void ReferenceImageUploaded(int key) => _uploadedImageKey = key;

    /// <summary>
    /// Drops every mesh, layer and reference image on both sides so the next frame uploads everything again. Needed when the
    /// view is given a different visualizer.
    /// </summary>
    internal void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _module.InvokeVoid("resetScene", _viewId);
        ForgetUploads();
    }

    private void ForgetUploads()
    {
        _uploadedMeshes.Clear();
        _uploads.Clear();
        _uploadedImageKey = -1;
        _uploadedLibraryVersion = -1;
        _sorter = new TransparentSorter();
        _sentTransparent = [];
        _transparentSent = false;
    }

    /// <summary>
    /// Sends the transparent instances of every layer, farthest first, with their runs of one mesh as [meshId, start, count]
    /// triples. Sorted again only when the camera or the scene moved, and sent only when that changed the order (or what is
    /// drawn): a small turn of the camera usually leaves it as it was.
    /// </summary>
    private void SyncTransparent(RenderFrame frame)
    {
        if (!_sorter.Sort(frame.Layers, frame.CameraPosition, frame.SceneVersion)) return;
        var sorted = _sorter.Sorted;
        if (_transparentSent && sorted.SequenceEqual(_sentTransparent)) return;

        var runs = new int[_sorter.Runs.Count * 3];
        for (var i = 0; i < _sorter.Runs.Count; i++)
        {
            var run = _sorter.Runs[i];
            runs[i * 3] = run.MeshId;
            runs[(i * 3) + 1] = run.Start;
            runs[(i * 3) + 2] = run.Count;
        }

        _module.InvokeVoid("setTransparent", _viewId, PackInstances(sorted), Bytes<int>(runs));
        _sentTransparent = sorted.ToArray();
        _transparentSent = true;
    }

    /// <summary>
    /// Sends each layer that changed: its instances and batches when it was laid out afresh, just the instances that changed
    /// when it was rewritten in place, nothing at all when it is as uploaded.
    /// </summary>
    private void SyncLayers(IReadOnlyList<RenderLayer> layers)
    {
        foreach (var kind in _uploads.RemoveMissing(layers)) _module.InvokeVoid("deleteLayer", _viewId, (int)kind);

        foreach (var layer in layers)
        {
            var upload = _uploads.Check(layer, _changes);
            var instances = layer.Instances.Span;
            if (upload == LayerUpload.Full)
            {
                var batches = new int[layer.Batches.Count * 4];
                for (var i = 0; i < layer.Batches.Count; i++)
                {
                    var batch = layer.Batches[i];
                    batches[i * 4] = batch.MeshId;
                    batches[(i * 4) + 1] = (int)batch.Pass;
                    batches[(i * 4) + 2] = batch.Start;
                    batches[(i * 4) + 3] = batch.Count;
                }

                _module.InvokeVoid("setLayer", _viewId, (int)layer.Kind, PackInstances(instances), Bytes<int>(batches));
            }
            else if (upload == LayerUpload.Changes)
            {
                // All the changed stretches in one call: their bounds, then their instances end to end.
                var ranges = new int[_changes.Count * 2];
                var total = 0;
                for (var i = 0; i < _changes.Count; i++)
                {
                    ranges[i * 2] = _changes[i].Start;
                    ranges[(i * 2) + 1] = _changes[i].Count;
                    total += _changes[i].Count;
                }

                var changed = new RenderObject[total];
                var at = 0;
                foreach (var range in _changes)
                {
                    instances.Slice(range.Start, range.Count).CopyTo(changed.AsSpan(at));
                    at += range.Count;
                }

                _module.InvokeVoid("patchLayer", _viewId, (int)layer.Kind, Bytes<int>(ranges), PackInstances(changed));
            }

            _uploads.Uploaded(layer);
        }
    }

    /// <summary>The instances as <see cref="InstanceData"/> floats, in bytes.</summary>
    private byte[] PackInstances(ReadOnlySpan<RenderObject> instances)
    {
        var floats = instances.Length * InstanceData.Stride;
        if (_packed.Length < floats) _packed = new float[Math.Max(floats, _packed.Length * 2)];
        InstanceData.Pack(instances, _packed);
        return MemoryMarshal.AsBytes(_packed.AsSpan(0, floats)).ToArray();
    }

    /// <summary>Uploads the reference image once per image and packs its per-frame placement into the frame buffer.</summary>
    private void PackReferenceImage(ReferenceImageLayer? layer, Span<float> f)
    {
        f[ImageSlot] = 0f;
        if (layer is null)
        {
            // The script frees the texture of an image no longer shown, so showing it again has to send it again.
            _uploadedImageKey = -1;
            return;
        }

        var image = layer.Image;
        if (image.Key != _uploadedImageKey)
        {
            if (image.EncodedData is { } encoded)
            {
                _module.InvokeVoid("setReferenceImageEncoded", _viewId, image.Key, encoded, image.ContentType);
            }
            else if (image.Rgba is { } rgba)
            {
                _module.InvokeVoid("setReferenceImageRgba", _viewId, image.Key, rgba, image.PixelWidth, image.PixelHeight);
            }

            _uploadedImageKey = image.Key;
        }

        f[ImageSlot] = 1f;
        MemoryMarshal.Cast<float, int>(f)[ImageSlot + 1] = image.Key;
        f[ImageSlot + 2] = layer.Min.X;
        f[ImageSlot + 3] = layer.Min.Y;
        f[ImageSlot + 4] = layer.Max.X;
        f[ImageSlot + 5] = layer.Max.Y;
        f[ImageSlot + 6] = layer.Height;
        f[ImageSlot + 7] = layer.Opacity;
        f[ImageSlot + 8] = layer.AboveScene ? 1f : 0f;
    }

    /// <summary>Stops rendering. GPU resources are released when the view is destroyed on the JS side.</summary>
    public void Dispose() => _disposed = true;

    /// <summary>The uniforms, the reference image's placement and the popup anchor, in the reused frame buffer.</summary>
    private void PackFrame(RenderFrame frame)
    {
        var f = MemoryMarshal.Cast<byte, float>(_frame.AsSpan());
        var i = 0;
        WriteMatrix(f, ref i, frame.View);
        WriteMatrix(f, ref i, frame.Projection);
        f[i++] = frame.CameraPosition.X; f[i++] = frame.CameraPosition.Y; f[i++] = frame.CameraPosition.Z;
        f[i++] = frame.Time;

        var l = frame.Lighting;
        f[i++] = l.SunDirection.X; f[i++] = l.SunDirection.Y; f[i++] = l.SunDirection.Z;
        f[i++] = l.SunColor.X; f[i++] = l.SunColor.Y; f[i++] = l.SunColor.Z;
        f[i++] = l.AmbientColor.X; f[i++] = l.AmbientColor.Y; f[i++] = l.AmbientColor.Z;
        f[i++] = l.SpecularStrength;
        f[i++] = l.Shininess;
        f[i++] = l.SkyColor.X; f[i++] = l.SkyColor.Y; f[i++] = l.SkyColor.Z;
        f[i++] = l.FogColor.X; f[i++] = l.FogColor.Y; f[i++] = l.FogColor.Z;
        f[i++] = l.FogDensity;

        var w = frame.Water;
        f[i++] = w.DeepColor.X; f[i++] = w.DeepColor.Y; f[i++] = w.DeepColor.Z;
        f[i++] = w.ShallowColor.X; f[i++] = w.ShallowColor.Y; f[i++] = w.ShallowColor.Z;
        f[i++] = w.WaveAmplitude;
        f[i++] = w.WaveFrequency;
        f[i++] = w.WaveSpeed;
        f[i++] = Math.Clamp(w.SkyReflection, 0f, 1f);
        f[i++] = Math.Clamp(w.Ripples, 0f, 2f);
        f[i++] = Math.Clamp(w.SunGlints, 0f, 2f);
        f[i++] = Math.Clamp(w.BoatMotion, 0f, 3f);
        f[i++] = frame.WaterCenter.X;
        f[i++] = frame.WaterCenter.Y;
        f[i++] = MathF.Max(1f, frame.WaterDetailRadius);

        // The uniforms have to end exactly where the image slots begin: one float too few leaves stale data in the
        // shader, one too many overwrites the image. Neither shows up as anything obvious on screen, hence the check.
        if (i != UniformsLength) throw new InvalidOperationException($"The frame packs {i} uniform floats but UniformsLength is {UniformsLength}.");

        PackReferenceImage(frame.ReferenceImage, f);
        f[AnchorSlot] = _anchor.Visible ? 1f : 0f;
        f[AnchorSlot + 1] = _anchor.X;
        f[AnchorSlot + 2] = _anchor.Y;
    }

    private static void WriteMatrix(Span<float> f, ref int i, System.Numerics.Matrix4x4 m)
    {
        f[i++] = m.M11; f[i++] = m.M12; f[i++] = m.M13; f[i++] = m.M14;
        f[i++] = m.M21; f[i++] = m.M22; f[i++] = m.M23; f[i++] = m.M24;
        f[i++] = m.M31; f[i++] = m.M32; f[i++] = m.M33; f[i++] = m.M34;
        f[i++] = m.M41; f[i++] = m.M42; f[i++] = m.M43; f[i++] = m.M44;
    }

    private static byte[] Bytes<T>(T[] data) where T : unmanaged => MemoryMarshal.AsBytes(data.AsSpan()).ToArray();
}
