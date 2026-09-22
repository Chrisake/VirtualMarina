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
/// <remarks>
/// This interface is meant to be implemented outside the library, so anything added to it in a later version
/// comes with a default implementation that keeps existing backends compiling and working unchanged
/// (see <c>Docs/16-compatibility.md</c>).
/// </remarks>
public interface ISceneRenderer : IDisposable
{
    /// <summary>Human-readable backend name, e.g. "OpenGL 3.3 Core (OpenTK)".</summary>
    string BackendName { get; }

    /// <summary>
    /// The graphics device actually in use once <see cref="Initialize"/> has run, e.g.
    /// "NVIDIA GeForce RTX 3060 — OpenGL 4.6.0", or null when the backend cannot report one.
    /// Hosts show it in a status bar or an About box.
    /// </summary>
    /// <remarks>Defaults to null, so a backend written before this member existed still compiles.</remarks>
    string? DeviceDescription => null;

    /// <summary>Compiles shaders and sets up global state. The graphics context must be current.</summary>
    void Initialize();

    /// <summary>Framebuffer size in device pixels.</summary>
    void Resize(int pixelWidth, int pixelHeight);

    /// <summary>Draws one frame produced by <c>MarinaVisualizer.BuildRenderFrame()</c>.</summary>
    void Render(RenderFrame frame);
}
