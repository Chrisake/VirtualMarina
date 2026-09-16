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
/// </remarks>
public sealed class OpenGlSceneRenderer : ISceneRenderer
{
    private readonly Dictionary<int, GpuMesh> _meshes = new();
    private GlShaderProgram? _modelProgram;
    private GlShaderProgram? _waterProgram;
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

        // 3. Transparent overlays (status pads, reserved ghost boats), no depth writes.
        GL.Enable(EnableCap.Blend);
        GL.DepthMask(false);
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
    }

    private void SyncMeshes(MeshLibrary library)
    {
        if (library.Version == _uploadedLibraryVersion) return;

        foreach (var mesh in library.All)
        {
            if (_meshes.Remove(mesh.Id, out var existing)) existing.Dispose();
            _meshes[mesh.Id] = GpuMesh.Upload(mesh);
        }

        _uploadedLibraryVersion = library.Version;
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
    }

    private sealed class GpuMesh : IDisposable
    {
        private int _vao;
        private int _vbo;
        private int _ebo;
        private int _indexCount;

        public static GpuMesh Upload(MeshData mesh)
        {
            var gpu = new GpuMesh { _indexCount = mesh.Indices.Length };
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
