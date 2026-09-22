using System.Runtime.InteropServices;
using Microsoft.JSInterop;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Blazor;

/// <summary>
/// <see cref="ISceneRenderer"/> backed by WebGL 2 through synchronous JS interop (Blazor WebAssembly).
/// </summary>
/// <remarks>
/// Binary data goes to JavaScript as base64 little-endian float/uint32 buffers:
/// meshes once per mesh-library version, instance data only when <see cref="RenderFrame.SceneVersion"/>
/// changes. Per frame, only ~67 floats of camera/lighting/time uniforms cross the boundary;
/// waves, bobbing and pulses animate on the GPU.
/// </remarks>
public sealed class WebGlSceneRenderer : ISceneRenderer
{
    /// <summary>Floats per render object: world matrix (16), tint (4), emissive, animation, phase, mesh id, desaturation.</summary>
    private const int ObjectStride = 25;

    /// <summary>Length of the per-frame uniform array (layout mirrored in marinaWebGL.js).</summary>
    private const int FrameLength = 70;

    private readonly IJSInProcessObjectReference _module;
    private readonly int _viewId;
    private readonly float[] _frame = new float[FrameLength];
    private readonly Dictionary<int, Core.Geometry.MeshData> _uploadedMeshes = new();
    private readonly double[] _image = new double[8];
    private float[] _objects = Array.Empty<float>();
    private int _uploadedImageKey = -1;
    private int _uploadedLibraryVersion = -1;
    private int _uploadedSceneVersion = -1;
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
            ShaderSources.ModelVertex(ShaderDialect.WebGL2),
            ShaderSources.ModelFragment(ShaderDialect.WebGL2),
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
                _module.InvokeVoid("uploadMesh", _viewId, mesh.Id, ToBase64(mesh.Vertices), ToBase64(mesh.Indices), mesh.IsWater);
                _uploadedMeshes[mesh.Id] = mesh;
            }

            foreach (var stale in _uploadedMeshes.Keys.Where(id => !current.Contains(id)).ToList())
            {
                _module.InvokeVoid("deleteMesh", _viewId, stale);
                _uploadedMeshes.Remove(stale);
            }

            _uploadedLibraryVersion = frame.MeshLibraryVersion;
        }

        var imageValues = PackReferenceImage(frame.ReferenceImage);

        if (frame.SceneVersion != _uploadedSceneVersion)
        {
            var count = PackObjects(frame.Objects);
            _module.InvokeVoid("setObjects", _viewId, ToBase64<float>(_objects.AsSpan(0, count * ObjectStride)), count);
            _uploadedSceneVersion = frame.SceneVersion;
        }

        PackFrame(frame, _frame);
        _module.InvokeVoid("renderFrame", _viewId, _frame, imageValues);
    }

    /// <summary>Uploads the reference image once per image and returns its per-frame placement, or null when there is none.</summary>
    private double[]? PackReferenceImage(ReferenceImageLayer? layer)
    {
        if (layer is null) return null;

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

        _image[0] = image.Key;
        _image[1] = layer.Min.X;
        _image[2] = layer.Min.Y;
        _image[3] = layer.Max.X;
        _image[4] = layer.Max.Y;
        _image[5] = layer.Height;
        _image[6] = layer.Opacity;
        _image[7] = layer.AboveScene ? 1d : 0d;
        return _image;
    }

    /// <summary>Stops rendering. GPU resources are released when the view is destroyed on the JS side.</summary>
    public void Dispose() => _disposed = true;

    private int PackObjects(IReadOnlyList<RenderObject> objects)
    {
        var required = objects.Count * ObjectStride;
        if (_objects.Length < required) _objects = new float[Math.Max(required, _objects.Length * 2)];

        var o = 0;
        foreach (var obj in objects)
        {
            var m = obj.World;
            _objects[o++] = m.M11; _objects[o++] = m.M12; _objects[o++] = m.M13; _objects[o++] = m.M14;
            _objects[o++] = m.M21; _objects[o++] = m.M22; _objects[o++] = m.M23; _objects[o++] = m.M24;
            _objects[o++] = m.M31; _objects[o++] = m.M32; _objects[o++] = m.M33; _objects[o++] = m.M34;
            _objects[o++] = m.M41; _objects[o++] = m.M42; _objects[o++] = m.M43; _objects[o++] = m.M44;
            _objects[o++] = obj.Tint.X; _objects[o++] = obj.Tint.Y; _objects[o++] = obj.Tint.Z; _objects[o++] = obj.Tint.W;
            _objects[o++] = obj.Emissive;
            _objects[o++] = (int)obj.Animation;
            _objects[o++] = obj.Phase;
            _objects[o++] = obj.MeshId;
            _objects[o++] = obj.Desaturation;
        }

        return objects.Count;
    }

    private static void PackFrame(RenderFrame frame, float[] f)
    {
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
        f[i++] = Math.Clamp(w.Whitecaps, 0f, 1f);
        f[i++] = Math.Clamp(w.WhitecapDistance, 20f, 5000f);
        f[i++] = frame.MarinaCenter.X;
        f[i++] = frame.MarinaCenter.Y;
    }

    private static void WriteMatrix(float[] f, ref int i, System.Numerics.Matrix4x4 m)
    {
        f[i++] = m.M11; f[i++] = m.M12; f[i++] = m.M13; f[i++] = m.M14;
        f[i++] = m.M21; f[i++] = m.M22; f[i++] = m.M23; f[i++] = m.M24;
        f[i++] = m.M31; f[i++] = m.M32; f[i++] = m.M33; f[i++] = m.M34;
        f[i++] = m.M41; f[i++] = m.M42; f[i++] = m.M43; f[i++] = m.M44;
    }

    private static string ToBase64<T>(ReadOnlySpan<T> data) where T : unmanaged =>
        Convert.ToBase64String(MemoryMarshal.AsBytes(data));

    private static string ToBase64<T>(T[] data) where T : unmanaged => ToBase64<T>(data.AsSpan());
}
