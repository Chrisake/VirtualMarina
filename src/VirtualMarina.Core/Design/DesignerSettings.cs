using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Design;

/// <summary>
/// The designer's tool settings as a plain value: what the next land area, pier, berth row or land berth will look like. Stored in a
/// marina file (<c>MarinaDocument.Designer</c>) so a design reopens the way it was left, and handy for presets ("small boats",
/// "superyachts").
/// </summary>
/// <example>
/// <code>
/// var superyachts = DesignerSettings.FromDesigner(marina.Designer) with { BerthWidth = 9, BerthLength = 28, BerthDepth = 4.5f };
/// superyachts.ApplyTo(marina.Designer);
/// </code>
/// </example>
public sealed record DesignerSettings
{
    /// <summary>Kind of land area the land tool draws.</summary>
    public LandKind LandKind { get; init; } = LandKind.Quay;

    /// <summary>Height of new land areas above the water, in meters.</summary>
    public float LandHeight { get; init; } = 1f;

    /// <summary>Trees per 1000 m² on new lawns and from the tree tool.</summary>
    public float TreeDensity { get; init; } = 8f;

    /// <summary>Construction of new piers.</summary>
    public PierType PierType { get; init; } = PierType.FloatingWooden;

    /// <summary>Deck width of new piers, in meters.</summary>
    public float PierWidth { get; init; } = 2.5f;

    /// <summary>Sides of new piers where boats berth.</summary>
    public PierSides PierBerthingSides { get; init; } = PierSides.Both;

    /// <summary>Width of new berths, in meters.</summary>
    public float BerthWidth { get; init; } = 5f;

    /// <summary>Length of new berths, in meters.</summary>
    public float BerthLength { get; init; } = 12f;

    /// <summary>Water depth of new berths, in meters.</summary>
    public float BerthDepth { get; init; } = 3f;

    /// <summary>What separates new berths.</summary>
    public BerthSeparator BerthSeparators { get; init; } = BerthSeparator.FingerPiers;

    /// <summary>Space between neighbouring berths, in meters.</summary>
    public float BerthGap { get; init; }

    /// <summary>Line new rows up with the berths already on that side of the pier.</summary>
    public bool AlignBerthsToExisting { get; init; } = true;

    /// <summary>Power/water pedestals switched on for a pier when berths are added to it.</summary>
    public PierServices BerthServices { get; init; } = PierServices.None;

    /// <summary>Bow direction of new land berths, in degrees.</summary>
    public float LandBerthHeading { get; init; }

    /// <summary>Snapping distance of the drawing tools, in pixels.</summary>
    public float SnapDistancePixels { get; init; } = 12f;

    /// <summary>Name given to a new pier, where <c>{pier}</c> stands for its generated id.</summary>
    public string PierNamePattern { get; init; } = "Pier {pier}";

    /// <summary>How new berths are named: the pattern, the first number and the step between them.</summary>
    public BerthNamingScheme BerthNaming { get; init; } = BerthNamingScheme.Default;

    /// <summary>Reads the current settings of a designer.</summary>
    /// <param name="designer">The designer to read.</param>
    public static DesignerSettings FromDesigner(MarinaDesigner designer)
    {
        ArgumentNullException.ThrowIfNull(designer);
        return new DesignerSettings
        {
            LandKind = designer.LandKind,
            LandHeight = designer.LandHeight,
            TreeDensity = designer.TreeDensity,
            PierType = designer.PierType,
            PierWidth = designer.PierWidth,
            PierBerthingSides = designer.PierBerthingSides,
            BerthWidth = designer.BerthWidth,
            BerthLength = designer.BerthLength,
            BerthDepth = designer.BerthDepth,
            BerthSeparators = designer.BerthSeparators,
            BerthGap = designer.BerthGap,
            AlignBerthsToExisting = designer.AlignBerthsToExisting,
            BerthServices = designer.BerthServices,
            LandBerthHeading = designer.LandBerthHeading,
            SnapDistancePixels = designer.SnapDistancePixels,
            PierNamePattern = designer.PierNamePattern,
            BerthNaming = designer.BerthNaming,
        };
    }

    /// <summary>Applies these settings to a designer. Values outside the allowed ranges are clamped instead of throwing.</summary>
    /// <param name="designer">The designer to change.</param>
    public void ApplyTo(MarinaDesigner designer)
    {
        ArgumentNullException.ThrowIfNull(designer);
        designer.LandKind = Enum.IsDefined(LandKind) ? LandKind : LandKind.Quay;
        designer.LandHeight = Clamp(LandHeight, 0f, 50f, 1f);
        designer.TreeDensity = Clamp(TreeDensity, 0f, 100f, 8f);
        designer.PierType = Enum.IsDefined(PierType) ? PierType : PierType.FloatingWooden;
        designer.PierWidth = Clamp(PierWidth, 0.5f, 30f, 2.5f);
        designer.PierBerthingSides = PierBerthingSides is PierSides.Left or PierSides.Right or PierSides.Both ? PierBerthingSides : PierSides.Both;
        designer.BerthWidth = Clamp(BerthWidth, 1f, 50f, 5f);
        designer.BerthLength = Clamp(BerthLength, 1f, 150f, 12f);
        designer.BerthDepth = Clamp(BerthDepth, 0.1f, 50f, 3f);
        designer.BerthSeparators = Enum.IsDefined(BerthSeparators) ? BerthSeparators : BerthSeparator.FingerPiers;
        designer.BerthGap = Clamp(BerthGap, 0f, 20f, 0f);
        designer.AlignBerthsToExisting = AlignBerthsToExisting;
        designer.BerthServices = (BerthServices & ~PierServices.PowerAndWater) == 0 ? BerthServices : PierServices.None;
        designer.LandBerthHeading = float.IsFinite(LandBerthHeading) ? LandBerthHeading : 0f;
        designer.SnapDistancePixels = Clamp(SnapDistancePixels, 0f, 100f, 12f);
        designer.PierNamePattern = string.IsNullOrWhiteSpace(PierNamePattern) ? "Pier {pier}" : PierNamePattern;
        designer.BerthNaming = BerthNaming is { } naming && !naming.Validate().Any() ? naming : BerthNamingScheme.Default;
    }

    private static float Clamp(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
