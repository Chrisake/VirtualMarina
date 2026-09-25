using System.Globalization;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Design;

/// <summary>
/// The allowed range of a numeric designer setting and the value it starts at, as <see cref="DesignerLimits"/> gives them
/// to a host that builds its own number fields.
/// </summary>
/// <param name="Minimum">Smallest allowed value.</param>
/// <param name="Maximum">Largest allowed value.</param>
/// <param name="Default">The value a new designer starts with.</param>
/// <example><code>heightField.Minimum = (decimal)DesignerLimits.LandHeight.Minimum;</code></example>
public readonly record struct DesignerSettingRange(float Minimum, float Maximum, float Default)
{
    /// <summary>True when the value is a finite number within the range, ends included.</summary>
    /// <param name="value">The value to test.</param>
    public bool Contains(float value) => float.IsFinite(value) && value >= Minimum && value <= Maximum;

    /// <summary>The value brought into range, or <see cref="Default"/> when it is not a number at all.</summary>
    /// <param name="value">The value to fit.</param>
    public float Clamp(float value) => float.IsFinite(value) ? Math.Clamp(value, Minimum, Maximum) : Default;

    /// <summary>The value itself when it is finite and in range.</summary>
    /// <param name="value">The value asked for.</param>
    /// <param name="parameterName">Name reported in the exception.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite, or outside the range.</exception>
    internal float Require(float value, string parameterName = "value") =>
        Contains(value)
            ? value
            : throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"Must be between {Minimum.ToString(CultureInfo.InvariantCulture)} and {Maximum.ToString(CultureInfo.InvariantCulture)}.");
}

/// <summary>
/// The range and default of every numeric <see cref="MarinaDesigner"/> setting, so a host's own number fields offer exactly
/// what the designer takes. <see cref="MarinaDesigner"/> refuses a value outside a range and <see cref="DesignerSettings"/>
/// clamps one read from a file into it; how finely a field steps through a range is left to the host.
/// </summary>
/// <example>
/// <code>
/// var range = DesignerLimits.BerthLength;
/// lengthField.Minimum = (decimal)range.Minimum;
/// lengthField.Maximum = (decimal)range.Maximum;
/// </code>
/// </example>
public static class DesignerLimits
{
    /// <summary><see cref="MarinaDesigner.LandHeight"/>, in meters.</summary>
    public static DesignerSettingRange LandHeight => DesignerDefaults.LandHeight;

    /// <summary><see cref="MarinaDesigner.TreeDensity"/>, trees per 1000 m².</summary>
    public static DesignerSettingRange TreeDensity => DesignerDefaults.TreeDensity;

    /// <summary><see cref="MarinaDesigner.PierWidth"/>, in meters.</summary>
    public static DesignerSettingRange PierWidth => DesignerDefaults.PierWidth;

    /// <summary><see cref="MarinaDesigner.BerthWidth"/>, in meters.</summary>
    public static DesignerSettingRange BerthWidth => DesignerDefaults.BerthWidth;

    /// <summary><see cref="MarinaDesigner.BerthLength"/>, in meters.</summary>
    public static DesignerSettingRange BerthLength => DesignerDefaults.BerthLength;

    /// <summary><see cref="MarinaDesigner.BerthDepth"/>, in meters.</summary>
    public static DesignerSettingRange BerthDepth => DesignerDefaults.BerthDepth;

    /// <summary><see cref="MarinaDesigner.BerthGap"/>, in meters.</summary>
    public static DesignerSettingRange BerthGap => DesignerDefaults.BerthGap;

    /// <summary><see cref="MarinaDesigner.DividerInterval"/>, in berths (a whole number).</summary>
    public static DesignerSettingRange DividerInterval => DesignerDefaults.DividerInterval;

    /// <summary>
    /// <see cref="MarinaDesigner.LandBerthHeading"/>, in degrees. The designer does not refuse a heading outside it: any
    /// finite angle is wrapped into the range.
    /// </summary>
    public static DesignerSettingRange LandBerthHeading => DesignerDefaults.LandBerthHeading;

    /// <summary><see cref="MarinaDesigner.SnapDistancePixels"/>, in pixels.</summary>
    public static DesignerSettingRange SnapDistancePixels => DesignerDefaults.SnapDistancePixels;

    /// <summary><see cref="MarinaDesigner.FogFactor"/>, a multiplier.</summary>
    public static DesignerSettingRange FogFactor => DesignerDefaults.FogFactor;

    /// <summary><see cref="MarinaDesigner.ReferenceImageOpacity"/>, 0 (invisible) to 1 (opaque).</summary>
    public static DesignerSettingRange ReferenceImageOpacity => DesignerDefaults.ImageOpacity;

    /// <summary><see cref="MarinaDesigner.ReferenceImageMetersPerPixel"/>, in meters.</summary>
    public static DesignerSettingRange ReferenceImageMetersPerPixel => DesignerDefaults.ImageMetersPerPixel;
}

/// <summary>Ranges and defaults of every designer setting, written once; <see cref="DesignerLimits"/> is the public view of the ranges.</summary>
internal static class DesignerDefaults
{
    /// <summary><see cref="MarinaDesigner.LandHeight"/>, in meters.</summary>
    public static readonly DesignerSettingRange LandHeight = new(0f, 50f, 1f);

    /// <summary><see cref="MarinaDesigner.TreeDensity"/>, trees per 1000 m².</summary>
    public static readonly DesignerSettingRange TreeDensity = new(0f, 100f, 8f);

    /// <summary><see cref="MarinaDesigner.PierWidth"/>, in meters.</summary>
    public static readonly DesignerSettingRange PierWidth = new(0.5f, 30f, 2.5f);

    /// <summary><see cref="MarinaDesigner.BerthWidth"/>, in meters.</summary>
    public static readonly DesignerSettingRange BerthWidth = new(1f, 50f, 5f);

    /// <summary><see cref="MarinaDesigner.BerthLength"/>, in meters.</summary>
    public static readonly DesignerSettingRange BerthLength = new(1f, 150f, 12f);

    /// <summary><see cref="MarinaDesigner.BerthDepth"/>, in meters.</summary>
    public static readonly DesignerSettingRange BerthDepth = new(0.1f, 50f, 3f);

    /// <summary><see cref="MarinaDesigner.BerthGap"/>, in meters.</summary>
    public static readonly DesignerSettingRange BerthGap = new(0f, 20f, 0f);

    /// <summary><see cref="MarinaDesigner.LandBerthHeading"/>, in degrees; a heading outside it is wrapped, not refused.</summary>
    public static readonly DesignerSettingRange LandBerthHeading = new(-180f, 180f, 0f);

    /// <summary><see cref="MarinaDesigner.SnapDistancePixels"/>, in pixels.</summary>
    public static readonly DesignerSettingRange SnapDistancePixels = new(0f, 100f, 12f);

    /// <summary><see cref="MarinaDesigner.FogFactor"/>, a multiplier.</summary>
    public static readonly DesignerSettingRange FogFactor = new(0f, 1f, 0.15f);

    /// <summary><see cref="MarinaDesigner.ReferenceImageOpacity"/>.</summary>
    public static readonly DesignerSettingRange ImageOpacity = new(0f, 1f, 0.6f);

    /// <summary><see cref="MarinaDesigner.ReferenceImageMetersPerPixel"/>.</summary>
    public static readonly DesignerSettingRange ImageMetersPerPixel = new(1e-4f, 1000f, 0.25f);

    /// <summary><see cref="MarinaDesigner.LandKind"/>.</summary>
    public const LandKind LandKind = Domain.LandKind.Quay;

    /// <summary><see cref="MarinaDesigner.PierType"/>.</summary>
    public const PierType PierType = Domain.PierType.FloatingWooden;

    /// <summary><see cref="MarinaDesigner.PierBerthingSides"/>.</summary>
    public const PierSides PierBerthingSides = PierSides.Both;

    /// <summary><see cref="MarinaDesigner.DividerType"/>.</summary>
    public const DividerType DividerType = Domain.DividerType.FingerPier;

    /// <summary><see cref="MarinaDesigner.DividerInterval"/>: the fewest and most berths between dividers, and the default.</summary>
    public static readonly DesignerSettingRange DividerInterval = new(1f, 10f, 1f);

    /// <summary><see cref="MarinaDesigner.BerthServices"/>.</summary>
    public const PierServices BerthServices = PierServices.None;

    /// <summary><see cref="MarinaDesigner.Scenery"/>.</summary>
    public const HinterlandScenery Scenery = HinterlandScenery.Countryside;

    /// <summary><see cref="MarinaDesigner.PierNamePattern"/>.</summary>
    public const string PierNamePattern = "Pier {pier}";

    /// <summary>True for the pier berthing sides a pier can be drawn with.</summary>
    /// <param name="sides">The sides asked for.</param>
    public static bool IsValidBerthingSides(PierSides sides) => sides is PierSides.Left or PierSides.Right or PierSides.Both;

    /// <summary>True for pedestal flags the designer knows about.</summary>
    /// <param name="services">The pedestals asked for.</param>
    public static bool IsValidServices(PierServices services) => (services & ~PierServices.PowerAndWater) == 0;
}
