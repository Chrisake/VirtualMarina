using System.Numerics;
using VirtualMarina.Core.Design;
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

    /// <summary>
    /// The designer's reference image, or null when none is shown. Draw it after the water and before the transparent objects
    /// (so drawing previews stay on top), with the <see cref="ShaderSources.ImageVertex"/> / <see cref="ShaderSources.ImageFragment"/> program.
    /// </summary>
    public ReferenceImageLayer? ReferenceImage { get; init; }
}

/// <summary>A reference image laid flat in the scene, north (the top row) toward −Z.</summary>
/// <param name="Image">The picture; upload a texture once per <see cref="Design.ReferenceImage.Key"/>.</param>
/// <param name="Min">Plan-view corner at the image's top-left pixel (north-west): smallest X and Z.</param>
/// <param name="Max">Plan-view corner at the image's bottom-right pixel (south-east): largest X and Z.</param>
/// <param name="Height">World Y of the image plane (just above the highest wave).</param>
/// <param name="Opacity">0 = invisible, 1 = opaque (uniform <c>uOpacity</c>).</param>
/// <param name="AboveScene">
/// True: draw without depth testing, over piers, land and boats. False: depth-tested, so land and structures hide it.
/// </param>
public sealed record ReferenceImageLayer(ReferenceImage Image, Vector2 Min, Vector2 Max, float Height, float Opacity, bool AboveScene);

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
/// <param name="Desaturation">0 = full color, 1 = grayscale (used for disabled berths and their boats).</param>
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
