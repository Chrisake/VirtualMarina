namespace VirtualMarina.Core.Rendering;

/// <summary>
/// The only contract a graphics backend must implement. The core library works out <em>what</em>
/// to draw (<see cref="RenderFrame"/>); a backend (desktop OpenGL, WebGL, and later Vulkan,
/// WebGPU, etc.) decides <em>how</em>.
/// </summary>
/// <remarks>
/// Backend responsibilities:
/// <list type="number">
/// <item>Upload every mesh in <see cref="RenderFrame.Meshes"/> (by id) and re-upload when <see cref="RenderFrame.MeshLibraryVersion"/> changes.</item>
/// <item>Draw opaque objects, then the water grid with the water shader, then transparent objects with blending and depth writes off.</item>
/// <item>Apply the frame uniforms (camera, lighting, water, time) to the shaders in <see cref="ShaderSources"/>.</item>
/// </list>
/// All calls happen on the thread that owns the graphics context.
/// </remarks>
public interface ISceneRenderer : IDisposable
{
    /// <summary>Human-readable backend name, e.g. "OpenGL 3.3 Core (OpenTK)".</summary>
    string BackendName { get; }

    /// <summary>Compiles shaders and sets up global state. The graphics context must be current.</summary>
    void Initialize();

    /// <summary>Framebuffer size in device pixels.</summary>
    void Resize(int pixelWidth, int pixelHeight);

    /// <summary>Draws one frame produced by <c>MarinaVisualizer.BuildRenderFrame()</c>.</summary>
    void Render(RenderFrame frame);
}
