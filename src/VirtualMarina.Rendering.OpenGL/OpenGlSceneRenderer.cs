using System.Diagnostics;
using OpenTK.Graphics.OpenGL4;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Rendering.OpenGL;

/// <summary>
/// <see cref="ISceneRenderer"/> for desktop OpenGL 3.3 core profile.
/// </summary>
/// <remarks>
/// <para>
/// The host must make its GL context current before calling any member (including <see cref="Dispose"/>)
/// and must have loaded OpenTK's bindings (OpenTK's GLControl and GameWindow do this automatically).
/// The designer's reference image must carry decoded RGBA pixels (<c>ReferenceImage.Rgba</c>); encoded-only images are skipped.
/// </para>
/// <para>
/// Every mesh is drawn instanced: one draw call per batch of a <see cref="RenderLayer"/> (see <see cref="RenderLayer.Batches"/>),
/// with each layer's instances in a buffer of their own that is uploaded again only when the layer changes, and then
/// only the instances that changed. Transparent instances are sorted back to front whenever the camera or the scene moves.
/// </para>
/// </remarks>
public sealed class OpenGlSceneRenderer : ISceneRenderer
{
    private readonly Dictionary<int, GpuMesh> _meshes = [];
    private readonly Dictionary<RenderLayerKind, GpuLayer> _layers = [];
    private readonly LayerUploadTracker _uploads = new();
    private readonly TransparentSorter _sorter = new();
    private readonly List<InstanceRange> _changes = [];
    private float[] _packed = [];
    private GlShaderProgram? _modelProgram;
    private GlShaderProgram? _waterProgram;
    private GlShaderProgram? _imageProgram;
    private FrameUniforms _modelUniforms;
    private FrameUniforms _waterUniforms;
    private (int WaterDeep, int WaterShallow) _waterColors;
    private ImageUniforms _imageUniforms;
    private int _sortedBuffer;
    private int _sortedCapacity;
    private int _imageQuadVao;
    private int _imageQuadVbo;
    private int _imageTexture;
    private int _imageTextureKey = -1;
    private int _maxTextureSize = 4096;
    private int _uploadedLibraryVersion = -1;
    private int _width = 1;
    private int _height = 1;
    private bool _disposed;

    /// <inheritdoc/>
    public string BackendName => "OpenGL 3.3 Core (OpenTK)";

    /// <summary>GL_RENDERER / GL_VERSION of the active context, available after <see cref="Initialize"/>.</summary>
    public string? DeviceDescription { get; private set; }

    /// <summary>True once shaders are compiled.</summary>
    public bool IsInitialized => _modelProgram is not null;

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">A shader failed to compile or link (the message contains the GL log).</exception>
    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsInitialized) return;

        // All or nothing: IsInitialized keys off the model program, so a later program failing must not leave it behind,
        // or Render would go on to use the missing ones.
        GlShaderProgram? model = null;
        GlShaderProgram? water = null;
        try
        {
            model = new GlShaderProgram(
                "model",
                ShaderSources.InstancedModelVertex(ShaderDialect.DesktopGL33),
                ShaderSources.InstancedModelFragment(ShaderDialect.DesktopGL33));
            water = new GlShaderProgram(
                "water",
                ShaderSources.WaterVertex(ShaderDialect.DesktopGL33),
                ShaderSources.WaterFragment(ShaderDialect.DesktopGL33));
            _imageProgram = new GlShaderProgram(
                "image",
                ShaderSources.ImageVertex(ShaderDialect.DesktopGL33),
                ShaderSources.ImageFragment(ShaderDialect.DesktopGL33));
        }
        catch (InvalidOperationException)
        {
            model?.Dispose();
            water?.Dispose();
            throw;
        }

        _modelProgram = model;
        _waterProgram = water;
        _modelUniforms = FrameUniforms.Of(model);
        _waterUniforms = FrameUniforms.Of(water);
        _waterColors = (water.Location("uWaterDeep"), water.Location("uWaterShallow"));
        _imageUniforms = ImageUniforms.Of(_imageProgram);
        CreateImageQuad();
        _sortedBuffer = GL.GenBuffer();

        GL.GetInteger(GetPName.MaxTextureSize, out _maxTextureSize);
        DeviceDescription = $"{GL.GetString(StringName.Renderer)} — OpenGL {GL.GetString(StringName.Version)}";
        CheckError("initialize");
    }

    /// <inheritdoc/>
    public void Resize(int pixelWidth, int pixelHeight)
    {
        _width = Math.Max(1, pixelWidth);
        _height = Math.Max(1, pixelHeight);
    }

    /// <inheritdoc/>
    public void Render(RenderFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_modelProgram is null || _waterProgram is null) Initialize();

        SyncMeshes(frame.Meshes);
        SyncLayers(frame.Layers);

        // The host may share the context with other drawing, so the state this needs is set every frame rather than
        // trusted to have stayed as Initialize left it.
        var fog = frame.Lighting.FogColor;
        GL.Viewport(0, 0, _width, _height);
        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Lequal);
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.ScissorTest);
        GL.Enable(EnableCap.Multisample);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.ColorMask(true, true, true, true);
        GL.ClearColor(fog.X, fog.Y, fog.Z, 1f);
        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // 1. Opaque instances, layer by layer, one draw call per batch.
        var model = _modelProgram!;
        model.Use();
        ApplyFrameUniforms(model, _modelUniforms, frame);
        DrawPass(frame.Layers, RenderPass.Opaque);

        // 2. Animated water.
        if (_meshes.TryGetValue(MeshIds.Water, out var water))
        {
            var waterProgram = _waterProgram!;
            waterProgram.Use();
            ApplyFrameUniforms(waterProgram, _waterUniforms, frame);
            GlShaderProgram.Set(_waterColors.WaterDeep, frame.Water.DeepColor);
            GlShaderProgram.Set(_waterColors.WaterShallow, frame.Water.ShallowColor);
            water.Draw();
        }

        // 3. Reference image (designer), then shadows, then the other transparent instances (status pads, ghost boats,
        // drawing previews) back to front, all blended without depth writes.
        GL.Enable(EnableCap.Blend);
        GL.DepthMask(false);
        if (frame.ReferenceImage is { } image) DrawReferenceImage(frame, image);
        else ReleaseReferenceTexture();

        model.Use();
        DrawPass(frame.Layers, RenderPass.Shadow);
        DrawSortedTransparent(frame);

        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
        GL.BindVertexArray(0);
        CheckError("render");
    }

    /// <summary>Deletes GPU meshes and shader programs. The GL context must be current.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var mesh in _meshes.Values) mesh.Dispose();
        _meshes.Clear();
        foreach (var layer in _layers.Values) GL.DeleteBuffer(layer.Buffer);
        _layers.Clear();
        _modelProgram?.Dispose();
        _waterProgram?.Dispose();
        _imageProgram?.Dispose();
        if (_sortedBuffer != 0) GL.DeleteBuffer(_sortedBuffer);
        if (_imageTexture != 0) GL.DeleteTexture(_imageTexture);
        if (_imageQuadVao != 0) GL.DeleteVertexArray(_imageQuadVao);
        if (_imageQuadVbo != 0) GL.DeleteBuffer(_imageQuadVbo);
    }

    /// <summary>Uploads meshes that are new or were replaced, and frees meshes no longer in the library.</summary>
    private void SyncMeshes(MeshLibrary library)
    {
        if (library.Version == _uploadedLibraryVersion) return;

        var current = new HashSet<int>();
        foreach (var mesh in library.All)
        {
            current.Add(mesh.Id);
            if (_meshes.TryGetValue(mesh.Id, out var existing))
            {
                if (ReferenceEquals(existing.Source, mesh)) continue;
                existing.Dispose();
            }

            _meshes[mesh.Id] = GpuMesh.Upload(mesh);
        }

        foreach (var stale in _meshes.Keys.Where(id => !current.Contains(id)).ToList())
        {
            _meshes[stale].Dispose();
            _meshes.Remove(stale);
        }

        _uploadedLibraryVersion = library.Version;
    }

    /// <summary>
    /// Brings each layer's instance buffer up to date: nothing when the layer is unchanged, the changed instances when it was
    /// rewritten in place, everything when it was laid out afresh.
    /// </summary>
    private void SyncLayers(IReadOnlyList<RenderLayer> layers)
    {
        foreach (var kind in _uploads.RemoveMissing(layers))
        {
            GL.DeleteBuffer(_layers[kind].Buffer);
            _layers.Remove(kind);
        }

        foreach (var layer in layers)
        {
            var upload = _uploads.Check(layer, _changes);
            if (upload == LayerUpload.None) continue;

            if (!_layers.TryGetValue(layer.Kind, out var gpu))
            {
                gpu = new GpuLayer { Buffer = GL.GenBuffer() };
                _layers[layer.Kind] = gpu;
            }

            var instances = layer.Instances.Span;
            GL.BindBuffer(BufferTarget.ArrayBuffer, gpu.Buffer);
            if (upload == LayerUpload.Full)
            {
                var floats = Pack(instances);
                var bytes = floats * sizeof(float);
                if (bytes > gpu.Capacity)
                {
                    // Room to grow, so a layer that gains a few instances does not reallocate every time.
                    gpu.Capacity = Math.Max(bytes, gpu.Capacity * 3 / 2);
                    GL.BufferData(BufferTarget.ArrayBuffer, gpu.Capacity, IntPtr.Zero, BufferUsageHint.DynamicDraw);
                }

                if (bytes > 0) GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, bytes, _packed);
            }
            else
            {
                foreach (var range in _changes)
                {
                    var floats = Pack(instances.Slice(range.Start, range.Count));
                    GL.BufferSubData(BufferTarget.ArrayBuffer, range.Start * InstanceData.StrideBytes, floats * sizeof(float), _packed);
                }
            }

            _uploads.Uploaded(layer);
        }

        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    }

    /// <summary>Packs instances into the scratch buffer (growing it when needed) and returns how many floats that took.</summary>
    private int Pack(ReadOnlySpan<RenderObject> instances)
    {
        var floats = instances.Length * InstanceData.Stride;
        if (_packed.Length < floats) _packed = new float[Math.Max(floats, _packed.Length * 2)];
        InstanceData.Pack(instances, _packed);
        return floats;
    }

    /// <summary>Draws every batch of one pass, layer by layer.</summary>
    private void DrawPass(IReadOnlyList<RenderLayer> layers, RenderPass pass)
    {
        foreach (var layer in layers)
        {
            if (!_layers.TryGetValue(layer.Kind, out var gpu)) continue;
            foreach (var batch in layer.Batches)
            {
                if (batch.Pass == pass) DrawInstances(gpu.Buffer, batch);
            }
        }
    }

    /// <summary>The transparent instances of every layer, farthest first, re-sorted when the camera or the scene moved.</summary>
    private void DrawSortedTransparent(RenderFrame frame)
    {
        if (_sorter.Sort(frame.Layers, frame.CameraPosition, frame.SceneVersion) && _sorter.Sorted.Length > 0)
        {
            var floats = Pack(_sorter.Sorted);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _sortedBuffer);
            var bytes = floats * sizeof(float);
            if (bytes > _sortedCapacity)
            {
                _sortedCapacity = Math.Max(bytes, _sortedCapacity * 3 / 2);
                GL.BufferData(BufferTarget.ArrayBuffer, _sortedCapacity, IntPtr.Zero, BufferUsageHint.StreamDraw);
            }

            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, bytes, _packed);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }

        foreach (var run in _sorter.Runs) DrawInstances(_sortedBuffer, run);
    }

    /// <summary>One instanced draw: the batch's mesh, with its instances read from <paramref name="buffer"/>.</summary>
    private void DrawInstances(int buffer, RenderBatch batch)
    {
        if (batch.Count == 0 || !_meshes.TryGetValue(batch.MeshId, out var mesh) || mesh.IsWater) return;

        GL.BindVertexArray(mesh.Vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, buffer);
        var offset = batch.Start * InstanceData.StrideBytes;
        for (var i = 0; i < InstanceData.AttributeCount; i++)
        {
            GL.VertexAttribPointer(InstanceData.FirstAttributeLocation + i, 4, VertexAttribPointerType.Float, false, InstanceData.StrideBytes, offset + (i * 4 * sizeof(float)));
        }

        GL.DrawElementsInstanced(PrimitiveType.Triangles, mesh.IndexCount, DrawElementsType.UnsignedInt, IntPtr.Zero, batch.Count);
    }

    private void CreateImageQuad()
    {
        _imageQuadVao = GL.GenVertexArray();
        GL.BindVertexArray(_imageQuadVao);
        _imageQuadVbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _imageQuadVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, ShaderSources.ImageQuadCorners.Length * sizeof(float), ShaderSources.ImageQuadCorners, BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.BindVertexArray(0);
    }

    /// <summary>Draws the designer's reference image as a textured quad (texture uploaded once per image).</summary>
    private void DrawReferenceImage(RenderFrame frame, ReferenceImageLayer layer)
    {
        if (_imageProgram is null || layer.Image.Rgba is not { } rgba) return; // encoded-only images need a browser to decode

        if (_imageTextureKey != layer.Image.Key)
        {
            if (_imageTexture == 0) _imageTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _imageTexture);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

            // A picture larger than the GPU takes is shrunk to fit rather than refused.
            var (width, height, pixels) = FitTexture(layer.Image.PixelWidth, layer.Image.PixelHeight, rgba, _maxTextureSize);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            _imageTextureKey = layer.Image.Key;
        }

        if (layer.AboveScene) GL.Disable(EnableCap.DepthTest);
        _imageProgram.Use();
        _imageProgram.Set(_imageUniforms.View, frame.View);
        _imageProgram.Set(_imageUniforms.Projection, frame.Projection);
        GlShaderProgram.Set(_imageUniforms.Min, layer.Min);
        GlShaderProgram.Set(_imageUniforms.Max, layer.Max);
        GlShaderProgram.Set(_imageUniforms.Height, layer.Height);
        GlShaderProgram.Set(_imageUniforms.Opacity, layer.Opacity);
        GlShaderProgram.Set(_imageUniforms.Image, 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _imageTexture);
        GL.BindVertexArray(_imageQuadVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.Enable(EnableCap.DepthTest);
    }

    /// <summary>Frees the reference image's texture once no image is shown: a large one holds a lot of video memory.</summary>
    private void ReleaseReferenceTexture()
    {
        if (_imageTexture == 0) return;
        GL.DeleteTexture(_imageTexture);
        _imageTexture = 0;
        _imageTextureKey = -1;
    }

    /// <summary>
    /// The pixels as they are when they fit in <paramref name="maxSize"/>, otherwise scaled down (nearest pixel) until they do.
    /// </summary>
    internal static (int Width, int Height, byte[] Pixels) FitTexture(int width, int height, byte[] rgba, int maxSize)
    {
        if (maxSize <= 0 || (width <= maxSize && height <= maxSize)) return (width, height, rgba);

        var scale = (double)maxSize / Math.Max(width, height);
        var fitWidth = Math.Clamp((int)(width * scale), 1, maxSize);
        var fitHeight = Math.Clamp((int)(height * scale), 1, maxSize);
        var pixels = new byte[fitWidth * fitHeight * 4];
        for (var y = 0; y < fitHeight; y++)
        {
            var sourceRow = Math.Min(height - 1, (int)((y + 0.5) / scale));
            for (var x = 0; x < fitWidth; x++)
            {
                var sourceColumn = Math.Min(width - 1, (int)((x + 0.5) / scale));
                System.Buffer.BlockCopy(rgba, ((sourceRow * width) + sourceColumn) * 4, pixels, ((y * fitWidth) + x) * 4, 4);
            }
        }

        return (fitWidth, fitHeight, pixels);
    }

    private static void ApplyFrameUniforms(GlShaderProgram program, in FrameUniforms u, RenderFrame frame)
    {
        var lighting = frame.Lighting;
        var water = frame.Water;

        program.Set(u.View, frame.View);
        program.Set(u.Projection, frame.Projection);
        GlShaderProgram.Set(u.CameraPos, frame.CameraPosition);
        GlShaderProgram.Set(u.Time, frame.Time);
        GlShaderProgram.Set(u.SunDirection, lighting.SunDirection);
        GlShaderProgram.Set(u.SunColor, lighting.SunColor);
        GlShaderProgram.Set(u.AmbientColor, lighting.AmbientColor);
        GlShaderProgram.Set(u.SpecularStrength, lighting.SpecularStrength);
        GlShaderProgram.Set(u.Shininess, lighting.Shininess);
        GlShaderProgram.Set(u.SkyColor, lighting.SkyColor);
        GlShaderProgram.Set(u.FogColor, lighting.FogColor);
        GlShaderProgram.Set(u.FogDensity, lighting.FogDensity);
        GlShaderProgram.Set(u.WaveAmplitude, water.WaveAmplitude);
        GlShaderProgram.Set(u.WaveFrequency, water.WaveFrequency);
        GlShaderProgram.Set(u.WaveSpeed, water.WaveSpeed);
        GlShaderProgram.Set(u.SkyReflection, Math.Clamp(water.SkyReflection, 0f, 1f));
        GlShaderProgram.Set(u.Ripples, Math.Clamp(water.Ripples, 0f, 2f));
        GlShaderProgram.Set(u.SunGlints, Math.Clamp(water.SunGlints, 0f, 2f));
        GlShaderProgram.Set(u.WaterCenter, frame.WaterCenter);
        GlShaderProgram.Set(u.DetailRadius, MathF.Max(1f, frame.WaterDetailRadius));
        GlShaderProgram.Set(u.FloatMotion, Math.Clamp(water.BoatMotion, 0f, 3f));
    }

    /// <summary>Reports GL errors in debug builds of the library; compiled out of release builds.</summary>
    [Conditional("DEBUG")]
    private static void CheckError(string stage)
    {
        for (var error = GL.GetError(); error != ErrorCode.NoError; error = GL.GetError())
        {
            Debug.Fail($"OpenGL error during {stage}: {error}");
        }
    }

    /// <summary>The frame uniforms' locations in one program, looked up once.</summary>
    private readonly record struct FrameUniforms(
        int View, int Projection, int CameraPos, int Time, int SunDirection, int SunColor, int AmbientColor, int SpecularStrength,
        int Shininess, int SkyColor, int FogColor, int FogDensity, int WaveAmplitude, int WaveFrequency, int WaveSpeed,
        int SkyReflection, int Ripples, int SunGlints, int WaterCenter, int DetailRadius, int FloatMotion)
    {
        public static FrameUniforms Of(GlShaderProgram p) => new(
            p.Location("uView"), p.Location("uProjection"), p.Location("uCameraPos"), p.Location("uTime"), p.Location("uSunDirection"),
            p.Location("uSunColor"), p.Location("uAmbientColor"), p.Location("uSpecularStrength"), p.Location("uShininess"),
            p.Location("uSkyColor"), p.Location("uFogColor"), p.Location("uFogDensity"), p.Location("uWaveAmplitude"),
            p.Location("uWaveFrequency"), p.Location("uWaveSpeed"), p.Location("uSkyReflection"), p.Location("uRipples"),
            p.Location("uSunGlints"), p.Location("uWaterCenter"), p.Location("uDetailRadius"), p.Location("uFloatMotion"));
    }

    /// <summary>The reference image program's uniform locations.</summary>
    private readonly record struct ImageUniforms(int View, int Projection, int Min, int Max, int Height, int Opacity, int Image)
    {
        public static ImageUniforms Of(GlShaderProgram p) => new(
            p.Location("uView"), p.Location("uProjection"), p.Location("uImageMin"), p.Location("uImageMax"),
            p.Location("uImageHeight"), p.Location("uOpacity"), p.Location("uImage"));
    }

    /// <summary>A layer's instance buffer on the GPU.</summary>
    private sealed class GpuLayer
    {
        public required int Buffer { get; init; }

        public int Capacity { get; set; }
    }

    private sealed class GpuMesh : IDisposable
    {
        private int _vbo;
        private int _ebo;

        public MeshData Source { get; private init; } = null!;

        public int Vao { get; private set; }

        public int IndexCount { get; private init; }

        public bool IsWater => Source.IsWater;

        public static GpuMesh Upload(MeshData mesh)
        {
            var gpu = new GpuMesh { IndexCount = mesh.Indices.Length, Source = mesh };
            gpu.Vao = GL.GenVertexArray();
            GL.BindVertexArray(gpu.Vao);

            gpu._vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, gpu._vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, mesh.Vertices.Length * sizeof(float), mesh.Vertices, BufferUsageHint.StaticDraw);

            gpu._ebo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, gpu._ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, mesh.Indices.Length * sizeof(uint), mesh.Indices, BufferUsageHint.StaticDraw);

            var stride = MeshData.VertexStride * sizeof(float);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, MeshData.PositionOffset * sizeof(float));
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, MeshData.NormalOffset * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, MeshData.ColorOffset * sizeof(float));
            GL.EnableVertexAttribArray(2);

            // The instance attributes advance once per instance. Where they read from is set at each draw, since the same
            // mesh is drawn from several layers' buffers.
            if (!mesh.IsWater)
            {
                for (var i = 0; i < InstanceData.AttributeCount; i++)
                {
                    GL.EnableVertexAttribArray(InstanceData.FirstAttributeLocation + i);
                    GL.VertexAttribDivisor(InstanceData.FirstAttributeLocation + i, 1);
                }
            }

            GL.BindVertexArray(0);
            return gpu;
        }

        public void Draw()
        {
            GL.BindVertexArray(Vao);
            GL.DrawElements(PrimitiveType.Triangles, IndexCount, DrawElementsType.UnsignedInt, 0);
        }

        public void Dispose()
        {
            GL.DeleteVertexArray(Vao);
            GL.DeleteBuffer(_vbo);
            GL.DeleteBuffer(_ebo);
        }
    }
}
