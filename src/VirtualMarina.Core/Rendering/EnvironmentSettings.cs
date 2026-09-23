using System.Numerics;

namespace VirtualMarina.Core.Rendering;

/// <summary>Directional sun, ambient light, specular and distance fog. Changes apply on the next frame.</summary>
/// <remarks>
/// <para>
/// Every value goes straight into the shaders, so each is held to the range given with it: one outside is clamped to
/// the nearest end, and one that is not a number at all (NaN or infinity, which a file may carry) is replaced with the
/// default. Colors are held per channel to 0–<see cref="MaxColorChannel"/>.
/// </para>
/// <para>Like the other style sections it raises <see cref="StyleSection.Changed"/> when a value actually changes.</para>
/// </remarks>
public sealed class LightingSettings : StyleSection
{
    /// <summary>Brightest a color channel may be. Above 1 is allowed, for a light brighter than white.</summary>
    internal const float MaxColorChannel = 10f;

    private static readonly Vector3 DefaultSunDirection = Vector3.Normalize(new Vector3(0.45f, 0.8f, 0.35f));
    private static readonly Vector3 DefaultSunColor = new(1.0f, 0.95f, 0.86f);
    private static readonly Vector3 DefaultAmbientColor = new(0.36f, 0.40f, 0.48f);
    private static readonly Vector3 DefaultSkyColor = new(0.56f, 0.72f, 0.88f);
    private static readonly Vector3 DefaultFogColor = new(0.76f, 0.85f, 0.92f);

    private Vector3 _sunDirection = DefaultSunDirection;
    private Vector3 _sunColor = DefaultSunColor;
    private Vector3 _ambientColor = DefaultAmbientColor;
    private float _specularStrength = 0.35f;
    private float _shininess = 32f;
    private Vector3 _skyColor = DefaultSkyColor;
    private Vector3 _fogColor = DefaultFogColor;
    private float _fogDensity = 0.0022f;

    /// <summary>
    /// Unit vector pointing <em>toward</em> the sun, in world coordinates (+Y up, +X east, +Z south). A zero or non-finite
    /// vector points it straight down (from overhead).
    /// </summary>
    public Vector3 SunDirection
    {
        get => _sunDirection;
        set => SetField(ref _sunDirection, IsFinite(value) && value.LengthSquared() > 1e-8f ? Vector3.Normalize(value) : Vector3.UnitY);
    }

    /// <summary>Sunlight color and intensity (RGB, linear).</summary>
    public Vector3 SunColor { get => _sunColor; set => SetField(ref _sunColor, Color(value, DefaultSunColor)); }

    /// <summary>Ambient light color (RGB, linear).</summary>
    public Vector3 AmbientColor { get => _ambientColor; set => SetField(ref _ambientColor, Color(value, DefaultAmbientColor)); }

    /// <summary>Strength of specular highlights on objects, 0–2 (default 0.35; 0 = matte).</summary>
    public float SpecularStrength { get => _specularStrength; set => SetField(ref _specularStrength, Range(value, 0f, 2f, 0.35f)); }

    /// <summary>Specular exponent, 1–1024 (default 32); higher values give tighter highlights.</summary>
    public float Shininess { get => _shininess; set => SetField(ref _shininess, Range(value, 1f, 1024f, 32f)); }

    /// <summary>Sky color reflected by the water at grazing angles.</summary>
    public Vector3 SkyColor { get => _skyColor; set => SetField(ref _skyColor, Color(value, DefaultSkyColor)); }

    /// <summary>Horizon/fog color; also used as the clear color.</summary>
    public Vector3 FogColor { get => _fogColor; set => SetField(ref _fogColor, Color(value, DefaultFogColor)); }

    /// <summary>Squared-exponential fog density per meter, 0–1 (default 0.0022). 0 disables fog.</summary>
    public float FogDensity { get => _fogDensity; set => SetField(ref _fogDensity, Range(value, 0f, 1f, 0.0022f)); }

    /// <summary>
    /// Sets the sun position from an azimuth and an elevation above the horizon (1–90°).
    /// </summary>
    /// <param name="azimuthDegrees">
    /// Direction of the sun seen from the marina, in the library's heading convention: 0° = +Z (south), 90° = +X (east),
    /// 180° = −Z (north). This is not a compass bearing (where 0° is north); a compass bearing <c>b</c> is <c>180 − b</c> here.
    /// </param>
    /// <param name="elevationDegrees">Height of the sun above the horizon, clamped to 1–90°.</param>
    public void SetSunAngles(float azimuthDegrees, float elevationDegrees)
    {
        var az = azimuthDegrees * Mathematics.MarinaMath.DegToRad;
        var el = Math.Clamp(elevationDegrees, 1f, 90f) * Mathematics.MarinaMath.DegToRad;
        SunDirection = new Vector3(MathF.Sin(az) * MathF.Cos(el), MathF.Sin(el), MathF.Cos(az) * MathF.Cos(el));
    }

    /// <summary><paramref name="value"/> held to <paramref name="min"/>–<paramref name="max"/>, or <paramref name="fallback"/> when it is not a number.</summary>
    internal static float Range(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    /// <summary>A color with every channel held to 0–<see cref="MaxColorChannel"/>; a channel that is not a number takes the default's.</summary>
    internal static Vector3 Color(Vector3 value, Vector3 fallback) => new(
        Range(value.X, 0f, MaxColorChannel, fallback.X),
        Range(value.Y, 0f, MaxColorChannel, fallback.Y),
        Range(value.Z, 0f, MaxColorChannel, fallback.Z));

    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    /// <summary>Takes every value from <paramref name="other"/> (a <see cref="MarinaStyle.Clone"/> step).</summary>
    internal void CopyFrom(LightingSettings other)
    {
        SunDirection = other.SunDirection;
        SunColor = other.SunColor;
        AmbientColor = other.AmbientColor;
        SpecularStrength = other.SpecularStrength;
        Shininess = other.Shininess;
        SkyColor = other.SkyColor;
        FogColor = other.FogColor;
        FogDensity = other.FogDensity;
    }
}

/// <summary>
/// Water surface look and wave animation (<see cref="MarinaStyle.Water"/>). Applied every frame.
/// <see cref="GridResolution"/> is only read when the visualizer is created.
/// </summary>
/// <remarks>
/// As with <see cref="LightingSettings"/>, every value is held to the range given with it, and one that is not a number
/// is replaced with the default, and <see cref="StyleSection.Changed"/> is raised when a value actually changes.
/// </remarks>
public sealed class WaterSettings : StyleSection
{
    /// <summary>Fewest cells per side <see cref="GridResolution"/> takes.</summary>
    internal const int MinGridResolution = 2;

    /// <summary>Most cells per side <see cref="GridResolution"/> takes, and the most the grid is ever built with as it grows.</summary>
    internal const int MaxGridResolution = 400;

    private static readonly Vector3 DefaultDeepColor = new(0.03f, 0.20f, 0.30f);
    private static readonly Vector3 DefaultShallowColor = new(0.10f, 0.42f, 0.48f);

    private float _size = 4200f;
    private int _gridResolution = 160;
    private Vector3 _deepColor = DefaultDeepColor;
    private Vector3 _shallowColor = DefaultShallowColor;
    private float _waveAmplitude = 0.08f;
    private float _waveFrequency = 1f;
    private float _waveSpeed = 1f;
    private float _skyReflection = 1f;
    private float _ripples = 1f;
    private float _sunGlints = 1f;
    private float _boatMotion = 1f;

    /// <summary>
    /// Edge length of the detailed water, in meters, 50–100000 (default 4200). This is the part that has waves, reflections and
    /// sun glints; beyond it the sea carries on flat to the horizon, so raising it buys detail rather than more sea.
    /// </summary>
    /// <remarks>
    /// It can be changed at any time — the visualizer notices and rebuilds the grid on the next frame — and the grid
    /// is grown automatically when a layout or a reference image reaches past it.
    /// </remarks>
    public float Size { get => _size; set => SetField(ref _size, LightingSettings.Range(value, 50f, 100_000f, 4200f)); }

    /// <summary>Cells per side of the water grid, 2–400 (default 160). Higher values give smoother waves.</summary>
    public int GridResolution { get => _gridResolution; init => _gridResolution = Math.Clamp(value, MinGridResolution, MaxGridResolution); }

    /// <summary>Water color in shade (RGB, linear).</summary>
    public Vector3 DeepColor { get => _deepColor; set => SetField(ref _deepColor, LightingSettings.Color(value, DefaultDeepColor)); }

    /// <summary>Water color where the sun lights it (RGB, linear).</summary>
    public Vector3 ShallowColor { get => _shallowColor; set => SetField(ref _shallowColor, LightingSettings.Color(value, DefaultShallowColor)); }

    /// <summary>How big the waves are: base wave height in meters, 0–5 (default 0.08; 0 = flat water).</summary>
    public float WaveAmplitude { get => _waveAmplitude; set => SetField(ref _waveAmplitude, LightingSettings.Range(value, 0f, 5f, 0.08f)); }

    /// <summary>How close together the waves are: multiplier on wave frequency, 0–10 (larger = shorter, choppier waves; default 1).</summary>
    public float WaveFrequency { get => _waveFrequency; set => SetField(ref _waveFrequency, LightingSettings.Range(value, 0f, 10f, 1f)); }

    /// <summary>How fast the waves travel: multiplier on wave speed, 0–10. Set to 0 to freeze the water and floating boats (default 1).</summary>
    public float WaveSpeed { get => _waveSpeed; set => SetField(ref _waveSpeed, LightingSettings.Range(value, 0f, 10f, 1f)); }

    /// <summary>
    /// Strength of the sky reflected on the water, 0–1 (default 1). These reflections form the bright, cloud-like patches that
    /// appear on the water toward the horizon and when seen from high above; lower it for a calmer, darker surface.
    /// </summary>
    public float SkyReflection { get => _skyReflection; set => SetField(ref _skyReflection, LightingSettings.Range(value, 0f, 1f, 1f)); }

    /// <summary>Strength of the small ripples that break up the reflections, 0–2 (default 1; 0 gives a smooth, glassy surface).</summary>
    public float Ripples { get => _ripples; set => SetField(ref _ripples, LightingSettings.Range(value, 0f, 2f, 1f)); }

    /// <summary>Strength of the sparkling sun glints on the water, 0–2 (default 1).</summary>
    public float SunGlints { get => _sunGlints; set => SetField(ref _sunGlints, LightingSettings.Range(value, 0f, 2f, 1f)); }

    /// <summary>
    /// How much boats, buoys and boom floats rise, fall and roll with the waves, 0–3 (default 1; 0 keeps them still while the water moves).
    /// </summary>
    public float BoatMotion { get => _boatMotion; set => SetField(ref _boatMotion, LightingSettings.Range(value, 0f, 3f, 1f)); }

    /// <summary>
    /// Takes every value from <paramref name="other"/> except <see cref="GridResolution"/>, which is fixed when the section is
    /// built (a <see cref="MarinaStyle.Clone"/> step).
    /// </summary>
    internal void CopyFrom(WaterSettings other)
    {
        Size = other.Size;
        DeepColor = other.DeepColor;
        ShallowColor = other.ShallowColor;
        WaveAmplitude = other.WaveAmplitude;
        WaveFrequency = other.WaveFrequency;
        WaveSpeed = other.WaveSpeed;
        SkyReflection = other.SkyReflection;
        Ripples = other.Ripples;
        SunGlints = other.SunGlints;
        BoatMotion = other.BoatMotion;
    }
}
