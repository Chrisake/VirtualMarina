using System.Numerics;

namespace VirtualMarina.Core.Rendering;

/// <summary>Directional sun, ambient light, specular and distance fog. Changes apply on the next frame.</summary>
public sealed class LightingSettings
{
    private Vector3 _sunDirection = Vector3.Normalize(new Vector3(0.45f, 0.8f, 0.35f));

    /// <summary>Unit vector pointing <em>toward</em> the sun.</summary>
    public Vector3 SunDirection
    {
        get => _sunDirection;
        set => _sunDirection = value.LengthSquared() > 1e-8f ? Vector3.Normalize(value) : Vector3.UnitY;
    }

    /// <summary>Sunlight color and intensity (RGB, linear).</summary>
    public Vector3 SunColor { get; set; } = new(1.0f, 0.95f, 0.86f);

    /// <summary>Ambient light color (RGB, linear).</summary>
    public Vector3 AmbientColor { get; set; } = new(0.36f, 0.40f, 0.48f);

    /// <summary>Strength of specular highlights on objects (0 = matte).</summary>
    public float SpecularStrength { get; set; } = 0.35f;

    /// <summary>Specular exponent; higher values give tighter highlights.</summary>
    public float Shininess { get; set; } = 32f;

    /// <summary>Sky color reflected by the water at grazing angles.</summary>
    public Vector3 SkyColor { get; set; } = new(0.56f, 0.72f, 0.88f);

    /// <summary>Horizon/fog color; also used as the clear color.</summary>
    public Vector3 FogColor { get; set; } = new(0.76f, 0.85f, 0.92f);

    /// <summary>Squared-exponential fog density per meter. 0 disables fog.</summary>
    public float FogDensity { get; set; } = 0.0022f;

    /// <summary>Sets the sun position from compass azimuth (0° = +Z) and elevation above the horizon.</summary>
    public void SetSunAngles(float azimuthDegrees, float elevationDegrees)
    {
        var az = azimuthDegrees * Mathematics.MarinaMath.DegToRad;
        var el = Math.Clamp(elevationDegrees, 1f, 90f) * Mathematics.MarinaMath.DegToRad;
        SunDirection = new Vector3(MathF.Sin(az) * MathF.Cos(el), MathF.Sin(el), MathF.Cos(az) * MathF.Cos(el));
    }
}

/// <summary>
/// Water surface look and wave animation (<see cref="MarinaStyle.Water"/>). Applied every frame. <see cref="Size"/> and
/// <see cref="GridResolution"/> are only read when the visualizer is created.
/// </summary>
public sealed class WaterSettings
{
    /// <summary>Edge length of the square water grid in meters.</summary>
    public float Size { get; init; } = 1400f;

    /// <summary>Cells per side of the water grid. Higher values give smoother waves.</summary>
    public int GridResolution { get; init; } = 160;

    /// <summary>Water color in shade (RGB, linear).</summary>
    public Vector3 DeepColor { get; set; } = new(0.03f, 0.20f, 0.30f);

    /// <summary>Water color where the sun lights it (RGB, linear).</summary>
    public Vector3 ShallowColor { get; set; } = new(0.10f, 0.42f, 0.48f);

    /// <summary>How big the waves are: base wave height in meters (default 0.08; 0 = flat water).</summary>
    public float WaveAmplitude { get; set; } = 0.08f;

    /// <summary>How close together the waves are: multiplier on wave frequency (larger = shorter, choppier waves; default 1).</summary>
    public float WaveFrequency { get; set; } = 1f;

    /// <summary>How fast the waves travel: multiplier on wave speed. Set to 0 to freeze the water and floating boats (default 1).</summary>
    public float WaveSpeed { get; set; } = 1f;

    /// <summary>
    /// Strength of the sky reflected on the water, 0–1 (default 1). These reflections form the bright, cloud-like patches that
    /// appear on the water toward the horizon and when seen from high above; lower it for a calmer, darker surface.
    /// </summary>
    public float SkyReflection { get; set; } = 1f;

    /// <summary>Strength of the small ripples that break up the reflections, 0–2 (default 1; 0 gives a smooth, glassy surface).</summary>
    public float Ripples { get; set; } = 1f;

    /// <summary>Strength of the sparkling sun glints on the water, 0–2 (default 1).</summary>
    public float SunGlints { get; set; } = 1f;

    /// <summary>
    /// How strongly white crests break on the open water, 0–1 (default 0.55; 0 turns them off). They appear only
    /// beyond <see cref="WhitecapDistance"/> from the middle of the marina and run inward, so the sea offshore is
    /// alive while the water among the piers stays calm. They fade out as the waves flatten.
    /// </summary>
    public float Whitecaps { get; set; } = 0.55f;

    /// <summary>
    /// How far from the middle of the marina the white crests start, in meters (default 220). Nothing breaks nearer
    /// than this, so the marina itself never fills with foam.
    /// </summary>
    public float WhitecapDistance { get; set; } = 220f;

    /// <summary>
    /// How much boats, buoys and boom floats rise, fall and roll with the waves, 0–3 (default 1; 0 keeps them still while the water moves).
    /// </summary>
    public float BoatMotion { get; set; } = 1f;
}
