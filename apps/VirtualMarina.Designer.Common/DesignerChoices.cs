using System.Globalization;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.Designer;

/// <summary>The values each drop-down of the Designer offers, in the order it offers them.</summary>
public static class DesignerChoices
{
    /// <summary>What a land area can be made of.</summary>
    public static IReadOnlyList<LandKind> LandKinds { get; } = [LandKind.Quay, LandKind.Grass, LandKind.Breakwater];

    /// <summary>Which sides of a new pier take boats.</summary>
    public static IReadOnlyList<PierSides> BerthingSides { get; } = [PierSides.Both, PierSides.Left, PierSides.Right];

    /// <summary>What covers the mainland behind the coast.</summary>
    public static IReadOnlyList<HinterlandScenery> Sceneries { get; } =
        [HinterlandScenery.Countryside, HinterlandScenery.Fields, HinterlandScenery.Town, HinterlandScenery.None];

    /// <summary>What the pedestals between berths offer.</summary>
    public static IReadOnlyList<PierServices> Services { get; } =
        [PierServices.None, PierServices.PowerAndWater, PierServices.Power, PierServices.Water];

    /// <summary>What stands between neighbouring berths.</summary>
    public static IReadOnlyList<BerthSeparator> Separators { get; } =
    [
        BerthSeparator.FingerPiers, BerthSeparator.PairedFingerPiers, BerthSeparator.FingerPier,
        BerthSeparator.Piles, BerthSeparator.SinglePile, BerthSeparator.Boom, BerthSeparator.None,
    ];

    /// <summary>Every kind of pier construction.</summary>
    public static IReadOnlyList<PierType> PierTypes { get; } = Enum.GetValues<PierType>();

    /// <summary>The compass buttons that turn the bow of a berth ashore: the letter shown and the heading it sets.</summary>
    public static IReadOnlyList<(string Text, float Heading)> CompassHeadings { get; } = [("N", 0f), ("E", 90f), ("S", 180f), ("W", -90f)];

    /// <summary>The name a value is shown under in a drop-down: the one the core library gives it.</summary>
    /// <typeparam name="T">One of the enums above.</typeparam>
    /// <param name="value">The value.</param>
    public static string Describe<T>(T value) where T : struct, Enum => value switch
    {
        LandKind kind => kind.GetDisplayName(),
        PierType type => Pier.GetDisplayName(type),
        PierSides sides => sides.GetDisplayName(),
        PierServices services => services.GetDisplayName(),
        BerthSeparator separator => separator.GetDisplayName(),
        HinterlandScenery scenery => scenery.GetDisplayName(),
        _ => value.ToString(),
    };
}

/// <summary>
/// The range, step and precision of one number field. The Designer apps take every field from <see cref="DesignerRanges"/>,
/// so the desktop and browser editions offer the same numbers.
/// </summary>
/// <param name="Min">Smallest value.</param>
/// <param name="Max">Largest value.</param>
/// <param name="Step">What one click of the arrows adds.</param>
/// <param name="Decimals">Decimal places shown.</param>
public readonly record struct NumberRange(decimal Min, decimal Max, decimal Step, int Decimals)
{
    /// <summary><see cref="Min"/> as an HTML attribute wants it, whatever the culture.</summary>
    public string MinText => Min.ToString(CultureInfo.InvariantCulture);

    /// <summary><see cref="Max"/> as an HTML attribute wants it.</summary>
    public string MaxText => Max.ToString(CultureInfo.InvariantCulture);

    /// <summary><see cref="Step"/> as an HTML attribute wants it.</summary>
    public string StepText => Step.ToString(CultureInfo.InvariantCulture);

    /// <summary>A value brought into the range and rounded to <see cref="Decimals"/>, as a number field can show it.</summary>
    /// <param name="value">The value to fit; NaN and infinities come back as <see cref="Min"/>.</param>
    public decimal Fit(float value) =>
        float.IsFinite(value)
            ? Math.Clamp(Math.Round((decimal)Math.Clamp(value, (float)Min, (float)Max), Decimals), Min, Max)
            : Min;

    /// <summary>A field over the range of a designer setting, as <see cref="DesignerLimits"/> gives it.</summary>
    /// <param name="range">The setting's range.</param>
    /// <param name="step">What one click of the arrows adds.</param>
    /// <param name="decimals">Decimal places shown.</param>
    /// <param name="scale">What the field shows per unit of the setting, e.g. 100 for a 0–1 value shown in percent.</param>
    public static NumberRange Of(DesignerSettingRange range, decimal step, int decimals, decimal scale = 1m) =>
        new((decimal)range.Minimum * scale, (decimal)range.Maximum * scale, step, decimals);
}

/// <summary>
/// The number fields of the Designer, for both apps. The limits of a designer setting come from VirtualMarina.Core's
/// <see cref="DesignerLimits"/>; the steps, the decimals and the fields that are not designer settings are the apps' own.
/// </summary>
public static class DesignerRanges
{
    /// <summary>Land area height above the water, meters.</summary>
    public static readonly NumberRange LandHeight = NumberRange.Of(DesignerLimits.LandHeight, 0.25m, 2);

    /// <summary>Deck width of a new pier, meters.</summary>
    public static readonly NumberRange PierWidth = NumberRange.Of(DesignerLimits.PierWidth, 0.25m, 2);

    /// <summary>Berth width, meters.</summary>
    public static readonly NumberRange BerthWidth = NumberRange.Of(DesignerLimits.BerthWidth, 0.25m, 2);

    /// <summary>Berth length, meters.</summary>
    public static readonly NumberRange BerthLength = NumberRange.Of(DesignerLimits.BerthLength, 0.5m, 2);

    /// <summary>Water depth of a berth, meters.</summary>
    public static readonly NumberRange BerthDepth = NumberRange.Of(DesignerLimits.BerthDepth, 0.1m, 2);

    /// <summary>Gap between neighbouring berths, meters.</summary>
    public static readonly NumberRange BerthGap = NumberRange.Of(DesignerLimits.BerthGap, 0.1m, 2);

    /// <summary>Where the bow of a berth ashore points, degrees from north.</summary>
    public static readonly NumberRange LandBerthHeading = NumberRange.Of(DesignerLimits.LandBerthHeading, 15m, 0);

    /// <summary>Trees per 1000 m².</summary>
    public static readonly NumberRange TreeDensity = NumberRange.Of(DesignerLimits.TreeDensity, 1m, 0);

    /// <summary>Opacity of the reference image, percent.</summary>
    public static readonly NumberRange ImageOpacityPercent = NumberRange.Of(DesignerLimits.ReferenceImageOpacity, 1m, 0, scale: 100m);

    /// <summary>Real length of the scale line, meters.</summary>
    public static readonly NumberRange ScaleLength = new(0.01m, 100000m, 1m, 1);

    /// <summary>Meters per pixel of the reference image.</summary>
    public static readonly NumberRange MetersPerPixel = NumberRange.Of(DesignerLimits.ReferenceImageMetersPerPixel, 0.01m, 4);

    /// <summary>Number the first berth of a row gets.</summary>
    public static readonly NumberRange NamingStart = new(-99999m, 99999m, 1m, 0);

    /// <summary>Step between berth numbers. Zero is refused: every berth would get the same number.</summary>
    public static readonly NumberRange NamingIncrement = new(-999m, 999m, 1m, 0);

    /// <summary>Digits a berth number is padded to.</summary>
    public static readonly NumberRange NamingDigits = new(1m, 9m, 1m, 0);
}
