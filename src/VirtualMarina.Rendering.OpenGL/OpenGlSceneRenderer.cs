using OpenTK.Graphics.OpenGL4;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Rendering.OpenGL;

/// <summary>
/// <see cref="ISceneRenderer"/> for desktop OpenGL 3.3 core profile.
/// </summary>
/// <remarks>
/// The host must make its GL context current before calling any member (including <see cref="Dispose"/>)
/// and must have loaded OpenTK's bindings (OpenTK's GLControl and GameWindow do this automatically).
/// The designer's reference image must carry decoded RGBA pixels (<c>ReferenceImage.Rgba</c>); encoded-only images are skipped.
/// </remarks>
public sealed class OpenGlSceneRenderer : ISceneRenderer
{
    private readonly Dictionary<int, GpuMesh> _meshes = [];
    private GlShaderProgram? _modelProgram;
    private GlShaderProgram? _waterProgram;
    private GlShaderProgram? _imageProgram;
    private int _imageQuadVao;
    private int _imageQuadVbo;
    private int _imageTexture;
    private int _imageTextureKey = -1;
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

        _modelProgram = new GlShaderProgram(
            "model",
            ShaderSources.ModelVertex(ShaderDialect.DesktopGL33),
            ShaderSources.ModelFragment(ShaderDialect.DesktopGL33));
        _waterProgram = new GlShaderProgram(
            "water",
            ShaderSources.WaterVertex(ShaderDialect.DesktopGL33),
            ShaderSources.WaterFragment(ShaderDialect.DesktopGL33));
        _imageProgram = new GlShaderProgram(
            "image",
            ShaderSources.ImageVertex(ShaderDialect.DesktopGL33),
            ShaderSources.ImageFragment(ShaderDialect.DesktopGL33));
        CreateImageQuad();

        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Lequal);
        GL.Disable(EnableCap.CullFace);
        GL.Enable(EnableCap.Multisample);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        DeviceDescription = $"{GL.GetString(StringName.Renderer)} — OpenGL {GL.GetString(StringName.Version)}";
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

        var fog = frame.Lighting.FogColor;
        GL.Viewport(0, 0, _width, _height);
        GL.ClearColor(fog.X, fog.Y, fog.Z, 1f);
        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // 1. Opaque objects.
        var model = _modelProgram!;
        model.Use();
        ApplyFrameUniforms(model, frame);
        foreach (var obj in frame.Objects)
        {
            if (!obj.IsTransparent) DrawObject(model, obj);
        }

        // 2. Animated water.
        if (_meshes.TryGetValue(MeshIds.Water, out var water))
        {
            var waterProgram = _waterProgram!;
            waterProgram.Use();
            ApplyFrameUniforms(waterProgram, frame);
            waterProgram.Set("uWaterDeep", frame.Water.DeepColor);
            waterProgram.Set("uWaterShallow", frame.Water.ShallowColor);
            water.Draw();
        }

        // 3. Reference image (designer), then transparent overlays (status pads, ghost boats, drawing previews), no depth writes.
        GL.Enable(EnableCap.Blend);
        GL.DepthMask(false);
        if (frame.ReferenceImage is { } image) DrawReferenceImage(frame, image);
        model.Use();
        foreach (var obj in frame.Objects)
        {
            if (obj.IsTransparent) DrawObject(model, obj);
        }

        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
        GL.BindVertexArray(0);
    }

    /// <summary>Deletes GPU meshes and shader programs. The GL context must be current.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var mesh in _meshes.Values) mesh.Dispose();
        _meshes.Clear();
        _modelProgram?.Dispose();
        _waterProgram?.Dispose();
        _imageProgram?.Dispose();
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
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, layer.Image.PixelWidth, layer.Image.PixelHeight, 0, PixelFormat.Rgba, PixelType.UnsignedByte, rgba);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            _imageTextureKey = layer.Image.Key;
        }

        if (layer.AboveScene) GL.Disable(EnableCap.DepthTest);
        _imageProgram.Use();
        _imageProgram.Set("uView", frame.View);
        _imageProgram.Set("uProjection", frame.Projection);
        _imageProgram.Set("uImageMin", layer.Min);
        _imageProgram.Set("uImageMax", layer.Max);
        _imageProgram.Set("uImageHeight", layer.Height);
        _imageProgram.Set("uOpacity", layer.Opacity);
        _imageProgram.Set("uImage", 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _imageTexture);
        GL.BindVertexArray(_imageQuadVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        GL.Enable(EnableCap.DepthTest);
    }

    private void DrawObject(GlShaderProgram program, in RenderObject obj)
    {
        if (!_meshes.TryGetValue(obj.MeshId, out var mesh)) return;

        program.Set("uModel", obj.World);
        program.Set("uTint", obj.Tint);
        program.Set("uEmissive", obj.Emissive);
        program.Set("uDesaturation", obj.Desaturation);
        program.Set("uAnimation", (int)obj.Animation);
        program.Set("uPhase", obj.Phase);
        mesh.Draw();
    }

    private static void ApplyFrameUniforms(GlShaderProgram program, RenderFrame frame)
    {
        var lighting = frame.Lighting;
        var water = frame.Water;

        program.Set("uView", frame.View);
        program.Set("uProjection", frame.Projection);
        program.Set("uCameraPos", frame.CameraPosition);
        program.Set("uTime", frame.Time);
        program.Set("uSunDirection", lighting.SunDirection);
        program.Set("uSunColor", lighting.SunColor);
        program.Set("uAmbientColor", lighting.AmbientColor);
        program.Set("uSpecularStrength", lighting.SpecularStrength);
        program.Set("uShininess", lighting.Shininess);
        program.Set("uSkyColor", lighting.SkyColor);
        program.Set("uFogColor", lighting.FogColor);
        program.Set("uFogDensity", lighting.FogDensity);
        program.Set("uWaveAmplitude", water.WaveAmplitude);
        program.Set("uWaveFrequency", water.WaveFrequency);
        program.Set("uWaveSpeed", water.WaveSpeed);
        program.Set("uSkyReflection", Math.Clamp(water.SkyReflection, 0f, 1f));
        program.Set("uRipples", Math.Clamp(water.Ripples, 0f, 2f));
        program.Set("uSunGlints", Math.Clamp(water.SunGlints, 0f, 2f));
        program.Set("uWaterCenter", frame.WaterCenter);
        program.Set("uDetailRadius", MathF.Max(1f, frame.WaterDetailRadius));
        program.Set("uFloatMotion", Math.Clamp(water.BoatMotion, 0f, 3f));
    }

    private sealed class GpuMesh : IDisposable
    {
        private int _vao;
        private int _vbo;
        private int _ebo;
        private int _indexCount;

        public MeshData Source { get; private init; } = null!;

        public static GpuMesh Upload(MeshData mesh)
        {
            var gpu = new GpuMesh { _indexCount = mesh.Indices.Length, Source = mesh };
            gpu._vao = GL.GenVertexArray();
            GL.BindVertexArray(gpu._vao);

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

            GL.BindVertexArray(0);
            return gpu;
        }

        public void Draw()
        {
            GL.BindVertexArray(_vao);
            GL.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt, 0);
        }

        public void Dispose()
        {
            GL.DeleteVertexArray(_vao);
            GL.DeleteBuffer(_vbo);
            GL.DeleteBuffer(_ebo);
        }
    }
}
