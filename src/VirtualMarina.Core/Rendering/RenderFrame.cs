using System.Numerics;
using VirtualMarina.Core.Geometry;

namespace VirtualMarina.Core.Rendering;

/// <summary>
/// Everything a backend needs to draw one frame. Matrices use the System.Numerics row-vector
/// convention; uploading the fields in M11..M44 order produces the column-major matrices GLSL expects.
/// </summary>
public sealed class RenderFrame
{
    /// <summary>Camera view matrix (uniform <c>uView</c>).</summary>
    public required Matrix4x4 View { get; init; }

    /// <summary>OpenGL-style projection matrix (uniform <c>uProjection</c>).</summary>
    public required Matrix4x4 Projection { get; init; }

    /// <summary>Eye position in world space (uniform <c>uCameraPos</c>).</summary>
    public required Vector3 CameraPosition { get; init; }

    /// <summary>Seconds since the visualizer was created. Drives waves, bobbing and pulses in the shaders.</summary>
    public required float Time { get; init; }

    /// <summary>Sun, ambient, specular and fog uniforms.</summary>
    public required LightingSettings Lighting { get; init; }

    /// <summary>Water color and wave uniforms.</summary>
    public required WaterSettings Water { get; init; }

    /// <summary>Scene objects. Draw the opaque ones, then the water, then the transparent ones (see <see cref="RenderObject.IsTransparent"/>).</summary>
    public required IReadOnlyList<RenderObject> Objects { get; init; }

    /// <summary>Incremented whenever <see cref="Objects"/> changes; lets backends skip re-uploading instance data.</summary>
    public required int SceneVersion { get; init; }

    /// <summary>Meshes referenced by <see cref="Objects"/>.</summary>
    public required MeshLibrary Meshes { get; init; }

    /// <summary><see cref="MeshLibrary.Version"/>; re-upload meshes when it changes.</summary>
    public int MeshLibraryVersion => Meshes.Version;
}

/// <summary>Per-object animations evaluated on the GPU (uniform <c>uAnimation</c>), so an animated scene needs no per-frame CPU updates.</summary>
[Flags]
public enum RenderAnimation
{
    /// <summary>Static.</summary>
    None = 0,

    /// <summary>Rise, fall, roll and pitch with the water surface at the object's origin.</summary>
    FloatOnWater = 1,

    /// <summary>Spin about Y and bob up and down (selection marker).</summary>
    SpinAndBob = 2,

    /// <summary>Pulse the emissive highlight over time.</summary>
    Pulse = 4,

    /// <summary>
    /// Lift the object by the highest height the waves can reach (see <see cref="ShaderSources.MaxWaveHeightFactor"/>),
    /// so the water never covers it. Used for text on the water.
    /// </summary>
    AboveWaves = 8,
}

/// <summary>One mesh instance.</summary>
/// <param name="MeshId">Id in <see cref="MeshLibrary"/>.</param>
/// <param name="World">Model-to-world transform.</param>
/// <param name="Tint">Multiplied with vertex colors. Alpha below 1 renders in the transparent pass.</param>
/// <param name="Emissive">0..1 highlight blend toward a brightened base color.</param>
/// <param name="Animation">GPU-side animation flags.</param>
/// <param name="Phase">Per-object animation phase offset in radians, so objects don't move in lockstep.</param>
/// <param name="Desaturation">0 = full color, 1 = grayscale (used for disabled slips and their boats).</param>
public readonly record struct RenderObject(
    int MeshId,
    Matrix4x4 World,
    Vector4 Tint,
    float Emissive = 0f,
    RenderAnimation Animation = RenderAnimation.None,
    float Phase = 0f,
    float Desaturation = 0f)
{
    /// <summary>True when the tint alpha is below 1: draw in the transparent pass (after the water, blending on, depth writes off).</summary>
    public bool IsTransparent => Tint.W < 0.999f;
}
