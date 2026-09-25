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
    public LandKind LandKind { get; init; } = DesignerDefaults.LandKind;

    /// <summary>Height of new land areas above the water, in meters.</summary>
    public float LandHeight { get; init; } = DesignerDefaults.LandHeight.Default;

    /// <summary>Trees per 1000 m² on new lawns and from the tree tool.</summary>
    public float TreeDensity { get; init; } = DesignerDefaults.TreeDensity.Default;

    /// <summary>Construction of new piers.</summary>
    public PierType PierType { get; init; } = DesignerDefaults.PierType;

    /// <summary>Deck width of new piers, in meters.</summary>
    public float PierWidth { get; init; } = DesignerDefaults.PierWidth.Default;

    /// <summary>Sides of new piers where boats berth.</summary>
    public PierSides PierBerthingSides { get; init; } = DesignerDefaults.PierBerthingSides;

    /// <summary>Width of new berths, in meters.</summary>
    public float BerthWidth { get; init; } = DesignerDefaults.BerthWidth.Default;

    /// <summary>Length of new berths, in meters.</summary>
    public float BerthLength { get; init; } = DesignerDefaults.BerthLength.Default;

    /// <summary>Water depth of new berths, in meters.</summary>
    public float BerthDepth { get; init; } = DesignerDefaults.BerthDepth.Default;

    /// <summary>Kind of divider the divider tool places.</summary>
    public DividerType DividerType { get; init; } = DesignerDefaults.DividerType;

    /// <summary>How many berths lie between the dividers the divider tool puts along a whole row.</summary>
    public int DividerInterval { get; init; } = (int)DesignerDefaults.DividerInterval.Default;

    /// <summary>Space between neighbouring berths, in meters.</summary>
    public float BerthGap { get; init; } = DesignerDefaults.BerthGap.Default;

    /// <summary>Line new rows up with the berths already on that side of the pier.</summary>
    public bool AlignBerthsToExisting { get; init; } = true;

    /// <summary>Power/water pedestals switched on for a pier when berths are added to it.</summary>
    public PierServices BerthServices { get; init; } = DesignerDefaults.BerthServices;

    /// <summary>Bow direction of new land berths, in degrees.</summary>
    public float LandBerthHeading { get; init; }

    /// <summary>Snapping distance of the drawing tools, in pixels.</summary>
    public float SnapDistancePixels { get; init; } = DesignerDefaults.SnapDistancePixels.Default;

    /// <summary>Name given to a new pier, where <c>{pier}</c> stands for its generated id.</summary>
    public string PierNamePattern { get; init; } = DesignerDefaults.PierNamePattern;

    /// <summary>How new berths are named: the pattern, the first number and the step between them.</summary>
    public BerthNamingScheme BerthNaming { get; init; } = BerthNamingScheme.Default;

    /// <summary>What is scattered across the mainland drawn by the shoreline tool.</summary>
    public HinterlandScenery Scenery { get; init; } = DesignerDefaults.Scenery;

    /// <summary>Multiplier for the distance fog while the designer is active (1 keeps the normal fog).</summary>
    public float FogFactor { get; init; } = DesignerDefaults.FogFactor.Default;

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
            DividerType = designer.DividerType,
            DividerInterval = designer.DividerInterval,
            BerthGap = designer.BerthGap,
            AlignBerthsToExisting = designer.AlignBerthsToExisting,
            BerthServices = designer.BerthServices,
            LandBerthHeading = designer.LandBerthHeading,
            SnapDistancePixels = designer.SnapDistancePixels,
            PierNamePattern = designer.PierNamePattern,
            BerthNaming = designer.BerthNaming,
            Scenery = designer.Scenery,
            FogFactor = designer.FogFactor,
        };
    }

    /// <summary>
    /// Applies these settings to a designer. Values outside the allowed ranges are clamped instead of throwing. The designer
    /// announces the change once (<see cref="MarinaDesigner.StateChanged"/>), not once per setting.
    /// </summary>
    /// <param name="designer">The designer to change.</param>
    public void ApplyTo(MarinaDesigner designer)
    {
        ArgumentNullException.ThrowIfNull(designer);
        using (designer.BeginUpdate())
        {
            designer.LandKind = Enum.IsDefined(LandKind) ? LandKind : DesignerDefaults.LandKind;
            designer.LandHeight = DesignerDefaults.LandHeight.Clamp(LandHeight);
            designer.TreeDensity = DesignerDefaults.TreeDensity.Clamp(TreeDensity);
            designer.PierType = Enum.IsDefined(PierType) ? PierType : DesignerDefaults.PierType;
            designer.PierWidth = DesignerDefaults.PierWidth.Clamp(PierWidth);
            designer.PierBerthingSides = DesignerDefaults.IsValidBerthingSides(PierBerthingSides) ? PierBerthingSides : DesignerDefaults.PierBerthingSides;
            designer.BerthWidth = DesignerDefaults.BerthWidth.Clamp(BerthWidth);
            designer.BerthLength = DesignerDefaults.BerthLength.Clamp(BerthLength);
            designer.BerthDepth = DesignerDefaults.BerthDepth.Clamp(BerthDepth);
            designer.DividerType = Enum.IsDefined(DividerType) ? DividerType : DesignerDefaults.DividerType;
            designer.DividerInterval = (int)DesignerDefaults.DividerInterval.Clamp(DividerInterval);
            designer.BerthGap = DesignerDefaults.BerthGap.Clamp(BerthGap);
            designer.AlignBerthsToExisting = AlignBerthsToExisting;
            designer.BerthServices = DesignerDefaults.IsValidServices(BerthServices) ? BerthServices : DesignerDefaults.BerthServices;
            designer.LandBerthHeading = float.IsFinite(LandBerthHeading) ? LandBerthHeading : 0f;
            designer.SnapDistancePixels = DesignerDefaults.SnapDistancePixels.Clamp(SnapDistancePixels);
            designer.PierNamePattern = string.IsNullOrWhiteSpace(PierNamePattern) ? DesignerDefaults.PierNamePattern : PierNamePattern;
            designer.BerthNaming = BerthNaming is { } naming && !naming.Validate().Any() ? naming : BerthNamingScheme.Default;
            designer.Scenery = Enum.IsDefined(Scenery) ? Scenery : DesignerDefaults.Scenery;
            designer.FogFactor = DesignerDefaults.FogFactor.Clamp(FogFactor);
        }
    }
}
