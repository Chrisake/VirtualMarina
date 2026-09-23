using System.Numerics;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Geometry;

namespace VirtualMarina.Core.Rendering;

/// <summary>
/// Everything a backend needs to draw one frame. Matrices use the System.Numerics row-vector
/// convention; uploading the fields in M11..M44 order produces the column-major matrices GLSL expects.
/// </summary>
/// <remarks>
/// The scene comes in <see cref="Layers"/>, each with versions that say what changed since the last frame, so a backend
/// uploads only that. <see cref="Objects"/> is the same scene as one flat list, for a backend that draws object by object.
/// A frame put together by hand may give either; the other is worked out from it.
/// </remarks>
public sealed class RenderFrame
{
    private IReadOnlyList<RenderObject>? _objects;
    private IReadOnlyList<RenderLayer>? _layers;
    private int? _sceneVersion;

    /// <summary>Camera view matrix (uniform <c>uView</c>).</summary>
    public required Matrix4x4 View { get; init; }

    /// <summary>OpenGL-style projection matrix (uniform <c>uProjection</c>).</summary>
    public required Matrix4x4 Projection { get; init; }

    /// <summary>Eye position in world space (uniform <c>uCameraPos</c>).</summary>
    public required Vector3 CameraPosition { get; init; }

    /// <summary>Seconds since the visualizer was created. Drives waves, bobbing and pulses in the shaders.</summary>
    public required float Time { get; init; }

    /// <summary>Sun, ambient, specular and fog uniforms, as they were when the frame was built.</summary>
    public required FrameLighting Lighting { get; init; }

    /// <summary>Water color and wave uniforms, as they were when the frame was built.</summary>
    public required FrameWater Water { get; init; }

    /// <summary>
    /// Scene objects as one list. Draw the opaque ones, then the water, then the transparent ones (see
    /// <see cref="RenderObject.IsTransparent"/>). Worked out from <see cref="Layers"/> when the frame was built from those.
    /// </summary>
    public IReadOnlyList<RenderObject> Objects
    {
        get => _objects ??= Flatten(Layers);
        init => _objects = value;
    }

    /// <summary>
    /// The scene in layers, in drawing order (see <see cref="RenderLayerKind"/>). A frame built from <see cref="Objects"/>
    /// alone has a single <see cref="RenderLayerKind.Scene"/> layer versioned by <see cref="SceneVersion"/>.
    /// </summary>
    public IReadOnlyList<RenderLayer> Layers
    {
        get => _layers ??= [RenderLayer.FromObjects(RenderLayerKind.Scene, _objects ?? [], SceneVersion)];
        init => _layers = value;
    }

    /// <summary>
    /// Changes whenever anything in the scene changes; lets a backend that uploads the whole of <see cref="Objects"/> skip
    /// frames where nothing did. Defaults to the highest version among <see cref="Layers"/>.
    /// </summary>
    public int SceneVersion
    {
        get => _sceneVersion ??= _layers is { Count: > 0 } layers ? layers.Max(layer => layer.Version) : 0;
        init => _sceneVersion = value;
    }

    /// <summary>
    /// Middle of the marina in plan coordinates: the center of the box around the layout (and the reference image, when
    /// one is shown), as last worked out when the water grid was fitted to it. It is there for a custom backend that wants
    /// to place something relative to the marina; neither built-in backend reads it, and the water is drawn around
    /// <see cref="WaterCenter"/> instead.
    /// </summary>
    public Vector2 MarinaCenter { get; init; }

    /// <summary>Middle of the water grid in plan coordinates, which the waves and the detail fade are centered on.</summary>
    public Vector2 WaterCenter { get; init; }

    /// <summary>
    /// How far from <see cref="WaterCenter"/> the water is drawn in detail, in meters. Past it the sea flattens into
    /// a plain skirt with no waves, reflections or glints, which is what lets the water run to the horizon cheaply.
    /// </summary>
    public float WaterDetailRadius { get; init; } = 700f;

    /// <summary>Meshes referenced by the scene.</summary>
    public required MeshLibrary Meshes { get; init; }

    /// <summary><see cref="MeshLibrary.Version"/>; re-upload meshes when it changes.</summary>
    public int MeshLibraryVersion => Meshes.Version;

    /// <summary>
    /// The designer's reference image, or null when none is shown. Draw it after the water and before the transparent objects
    /// (so drawing previews stay on top), with the <see cref="ShaderSources.ImageVertex"/> / <see cref="ShaderSources.ImageFragment"/> program.
    /// </summary>
    public ReferenceImageLayer? ReferenceImage { get; init; }

    private static RenderObject[] Flatten(IReadOnlyList<RenderLayer> layers)
    {
        var all = new RenderObject[layers.Sum(layer => layer.Count)];
        var at = 0;
        foreach (var layer in layers)
        {
            layer.Instances.Span.CopyTo(all.AsSpan(at));
            at += layer.Count;
        }

        return all;
    }
}

/// <summary>The lighting uniforms of one frame: a copy, so changing the settings later does not change a frame already built.</summary>
/// <param name="SunDirection">Direction toward the sun (uniform <c>uSunDirection</c>).</param>
/// <param name="SunColor">Sun color (<c>uSunColor</c>).</param>
/// <param name="AmbientColor">Ambient color (<c>uAmbientColor</c>).</param>
/// <param name="SpecularStrength">Specular strength (<c>uSpecularStrength</c>).</param>
/// <param name="Shininess">Specular exponent (<c>uShininess</c>).</param>
/// <param name="SkyColor">Sky color reflected by the water (<c>uSkyColor</c>).</param>
/// <param name="FogColor">Fog and clear color (<c>uFogColor</c>).</param>
/// <param name="FogDensity">Fog density (<c>uFogDensity</c>).</param>
public readonly record struct FrameLighting(
    Vector3 SunDirection, Vector3 SunColor, Vector3 AmbientColor, float SpecularStrength, float Shininess,
    Vector3 SkyColor, Vector3 FogColor, float FogDensity)
{
    /// <summary>The values <paramref name="settings"/> has now.</summary>
    /// <param name="settings">The lighting to copy.</param>
    public static FrameLighting FromLightingSettings(LightingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new FrameLighting(
            settings.SunDirection, settings.SunColor, settings.AmbientColor, settings.SpecularStrength, settings.Shininess,
            settings.SkyColor, settings.FogColor, settings.FogDensity);
    }

    /// <summary>The values <paramref name="settings"/> has now (see <see cref="FromLightingSettings"/>).</summary>
    /// <param name="settings">The lighting to copy.</param>
    public static implicit operator FrameLighting(LightingSettings settings) => FromLightingSettings(settings);
}

/// <summary>The water uniforms of one frame: a copy, so changing the settings later does not change a frame already built.</summary>
/// <param name="DeepColor">Deep water color (uniform <c>uWaterDeep</c>).</param>
/// <param name="ShallowColor">Shallow water color (<c>uWaterShallow</c>).</param>
/// <param name="WaveAmplitude">Wave height scale (<c>uWaveAmplitude</c>).</param>
/// <param name="WaveFrequency">Wave frequency scale (<c>uWaveFrequency</c>).</param>
/// <param name="WaveSpeed">Wave speed scale (<c>uWaveSpeed</c>).</param>
/// <param name="SkyReflection">How much sky the water reflects (<c>uSkyReflection</c>).</param>
/// <param name="Ripples">Strength of the fine ripples (<c>uRipples</c>).</param>
/// <param name="SunGlints">Strength of the sun glints (<c>uSunGlints</c>).</param>
/// <param name="BoatMotion">How much floating objects move with the waves (<c>uFloatMotion</c>).</param>
public readonly record struct FrameWater(
    Vector3 DeepColor, Vector3 ShallowColor, float WaveAmplitude, float WaveFrequency, float WaveSpeed,
    float SkyReflection, float Ripples, float SunGlints, float BoatMotion)
{
    /// <summary>The values <paramref name="settings"/> has now.</summary>
    /// <param name="settings">The water to copy.</param>
    public static FrameWater FromWaterSettings(WaterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new FrameWater(
            settings.DeepColor, settings.ShallowColor, settings.WaveAmplitude, settings.WaveFrequency, settings.WaveSpeed,
            settings.SkyReflection, settings.Ripples, settings.SunGlints, settings.BoatMotion);
    }

    /// <summary>The values <paramref name="settings"/> has now (see <see cref="FromWaterSettings"/>).</summary>
    /// <param name="settings">The water to copy.</param>
    public static implicit operator FrameWater(WaterSettings settings) => FromWaterSettings(settings);

    /// <summary>True when the water surface moves by itself, so a still view still has to be redrawn.</summary>
    public bool IsMoving => WaveSpeed > 0f && (WaveAmplitude > 0f || Ripples > 0f);
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

/// <summary>
/// Per-object animations evaluated on the GPU (uniform <c>uAnimation</c>, or the instance attribute), so an animated scene
/// needs no per-frame CPU updates; plus <see cref="Unlit"/>, the one flag about shading.
/// </summary>
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

    /// <summary>
    /// Not an animation: drawn in its color and tint alone, with no lighting (only fog). Shadows are drawn this way, since
    /// they are flattened and have no normals to light.
    /// </summary>
    Unlit = 16,
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
