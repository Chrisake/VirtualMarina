namespace VirtualMarina.Core.Domain;

/// <summary>
/// How tall a tree of each <see cref="TreeShape"/> grows and how wide its crown is for its height: the one table both the trees
/// scattered on a <see cref="LandArea"/> and those drawn on the mainland behind the shore are drawn from.
/// </summary>
/// <param name="MinHeight">Shortest tree, in meters.</param>
/// <param name="HeightRange">How much taller than that a tree may be, in meters.</param>
/// <param name="MinCrownRatio">Narrowest crown radius, as a fraction of the height.</param>
/// <param name="CrownRatioRange">How much wider than that, as a fraction of the height.</param>
internal readonly record struct TreeShapeProfile(float MinHeight, float HeightRange, float MinCrownRatio, float CrownRatioRange)
{
    /// <summary>The widest crown any profile produces, in meters: the tallest broadleaf at its widest.</summary>
    public const float MaxCrownRadius = 9f * 0.42f;

    private static readonly TreeShapeProfile Broadleaf = new(4f, 5f, 0.30f, 0.12f);
    private static readonly TreeShapeProfile Conifer = new(6f, 7f, 0.16f, 0.06f);
    private static readonly TreeShapeProfile Cypress = new(7f, 5f, 0.08f, 0.03f);
    private static readonly TreeShapeProfile Palm = new(5f, 5f, 0.22f, 0.06f);
    private static readonly TreeShapeProfile Cherry = new(4f, 3f, 0.30f, 0.12f);

    /// <summary>The profile of a shape; an unknown shape grows like a broadleaf.</summary>
    public static TreeShapeProfile For(TreeShape shape) => shape switch
    {
        TreeShape.Conifer => Conifer,
        TreeShape.Cypress => Cypress,
        TreeShape.Palm => Palm,
        TreeShape.Cherry => Cherry,
        _ => Broadleaf,
    };

    /// <summary>A random height within the profile.</summary>
    public float NextHeight(Random random) => MinHeight + (float)random.NextDouble() * HeightRange;

    /// <summary>A random crown radius for a tree of <paramref name="height"/>.</summary>
    public float NextCrownRadius(float height, Random random) => height * (MinCrownRatio + (float)random.NextDouble() * CrownRatioRange);

    /// <summary>The middle crown radius for a tree of <paramref name="height"/>, for when no random draw is wanted.</summary>
    public float TypicalCrownRadius(float height) => height * (MinCrownRatio + CrownRatioRange * 0.5f);
}

/// <summary>
/// The width and spacing a generated divider gets for its type: the one place <see cref="BerthGenerator"/> and the designer
/// take them from.
/// </summary>
internal static class DividerDefaults
{
    /// <summary>A finger pier is shorter than the berths it separates, so a boat's stern clears its end.</summary>
    public const float FingerPierLengthRatio = 0.75f;

    /// <summary>Width of a generated divider: a walkable finger pier, a slim boom, or the diameter of a pile.</summary>
    public static float Width(DividerType type) => type switch
    {
        DividerType.FingerPier => 0.9f,
        DividerType.Boom => 0.35f,
        _ => 0.4f,
    };

    /// <summary>Spacing of piles along a row (at least 3 m, three to a berth) or of floats along a boom.</summary>
    public static float Spacing(DividerType type, float length) => type == DividerType.Piles ? MathF.Max(3f, length / 3f) : 1.6f;

    /// <summary>Length of a generated divider beside berths of <paramref name="berthLength"/>.</summary>
    public static float Length(DividerType type, float berthLength) =>
        type == DividerType.FingerPier ? berthLength * FingerPierLengthRatio : berthLength;
}
