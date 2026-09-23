using System.Collections.ObjectModel;
using System.Numerics;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Domain;

/// <summary>Surface of a <see cref="LandArea"/>; controls how it is drawn.</summary>
public enum LandKind
{
    /// <summary>Paved quay or pier head: a solid light concrete block.</summary>
    Quay = 0,

    /// <summary>Rubble-mound breakwater: the area is filled with a pile of rocks sloping down to the water.</summary>
    Breakwater = 1,

    /// <summary>Lawn or park: a solid green block.</summary>
    Grass = 2,
}

/// <summary>Shape of a <see cref="LandTree"/>.</summary>
public enum TreeShape
{
    /// <summary>Round crown (plane tree, olive, oak).</summary>
    Broadleaf = 0,

    /// <summary>Tall, pointed crown (pine, cypress).</summary>
    Conifer = 1,

    /// <summary>Bare trunk with a spray of fronds on top (palm). Common along a promenade.</summary>
    Palm = 2,

    /// <summary>Narrow column (Italian cypress), the exclamation mark of a Mediterranean shore.</summary>
    Cypress = 3,

    /// <summary>
    /// Pink blossom (Japanese cherry). Scattered far more rarely than the rest, so finding one is a small surprise.
    /// </summary>
    Cherry = 4,
}

/// <summary>A tree standing on a <see cref="LandArea"/>. Positions and sizes are stored, so trees look the same in every session.</summary>
/// <param name="Position">Trunk position in plan coordinates.</param>
/// <param name="Height">Total height above the land surface, 1–40 m.</param>
/// <param name="CrownRadius">Radius of the crown, 0.3–15 m.</param>
/// <param name="Shape">Crown shape.</param>
public readonly record struct LandTree(Vector2 Position, float Height, float CrownRadius, TreeShape Shape = TreeShape.Broadleaf);

/// <summary>
/// A flat piece of land such as a quay, breakwater or lawn: a polygon outline in plan coordinates with one top height
/// for the whole area. Land berths (<see cref="Berth.OnLand"/>) can be placed on it for boats stored or maintained ashore.
/// </summary>
/// <remarks>
/// The outline may be convex or concave, in either winding order, but its edges must not cross. Don't repeat the first
/// point at the end.
/// </remarks>
/// <example>
/// <code>
/// // An L-shaped quay 1 m above the water.
/// new LandArea("quay", new[] { new Vector2(-130, -32), new Vector2(130, -32), new Vector2(130, -6), new Vector2(60, -6), new Vector2(60, 10), new Vector2(-130, 10) }, height: 1f)
/// </code>
/// </example>
public sealed record LandArea
{
    /// <summary>Creates a land area from its outline.</summary>
    /// <param name="id">Unique id (case-insensitive). Land berths reference it.</param>
    /// <param name="points">Outline in plan coordinates (X = world X, Y = world Z), at least three points.</param>
    /// <param name="height">Height of the top surface above the water, in meters (the same over the whole area).</param>
    /// <param name="kind">Surface type.</param>
    public LandArea(string id, IEnumerable<Vector2> points, float height, LandKind kind = LandKind.Quay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(points);
        Id = id;
        Points = points.ToArray();
        Height = height;
        Kind = kind;
    }

    /// <summary>Creates a rectangular land area.</summary>
    /// <param name="id">Unique id (case-insensitive).</param>
    /// <param name="area">Rectangle in plan coordinates.</param>
    /// <param name="height">Height of the top surface above the water, in meters.</param>
    /// <param name="kind">Surface type.</param>
    /// <example><code>new LandArea("quay", new OrientedRect(new Vector2(0, -19), new Vector2(260, 26), 0), 1.0f, LandKind.Quay)</code></example>
    public LandArea(string id, OrientedRect area, float height, LandKind kind = LandKind.Quay)
        : this(id, area.GetCorners(), height, kind)
    {
    }

    /// <summary>Unique id (case-insensitive). Land berths reference it through <see cref="Berth.LandAreaId"/>.</summary>
    public string Id { get; init; }

    /// <summary>Display name (tooltips); the id is used when null.</summary>
    public string? Name { get; init; }

    /// <summary>Outline in plan coordinates (X = world X, Y = world Z). The last point connects back to the first.</summary>
    public IReadOnlyList<Vector2> Points { get; init; }

    /// <summary>Height of the top surface above the water, in meters, over the whole area.</summary>
    public float Height { get; init; }

    /// <summary>Surface type.</summary>
    public LandKind Kind { get; init; }

    /// <summary>
    /// Trees standing on the area (usually lawns). Their positions are part of the layout, so they never move between sessions; generate
    /// them once with <see cref="GenerateTrees"/> or the designer.
    /// </summary>
    public IReadOnlyList<LandTree> Trees { get; init; } = Array.Empty<LandTree>();

    /// <summary>
    /// Read-only string attributes the host application attaches to this land area, e.g. its own key or a contract
    /// reference. Saved to and loaded from a marina file, and never read by the visualizer.
    /// </summary>
    /// <example><code>land with { Metadata = new Dictionary&lt;string, string&gt; { ["zone"] = "winter storage" } }</code></example>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>
    /// Scatters trees randomly inside an outline, at about <paramref name="treesPer1000SquareMeters"/>, keeping them apart, away from the
    /// edges and out of <paramref name="keepClear"/> (e.g. land berths). Store the result in <see cref="Trees"/>.
    /// </summary>
    /// <param name="outline">The land outline.</param>
    /// <param name="treesPer1000SquareMeters">Density; 0 returns no trees.</param>
    /// <param name="random">Random source: pass a seeded one for repeatable results.</param>
    /// <param name="keepClear">Areas that must stay free of trees.</param>
    /// <example><code>lawn = lawn with { Trees = LandArea.GenerateTrees(lawn.Points, 6, new Random()) };</code></example>
    public static IReadOnlyList<LandTree> GenerateTrees(IReadOnlyList<Vector2> outline, float treesPer1000SquareMeters, Random random, IEnumerable<OrientedRect>? keepClear = null)
    {
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentNullException.ThrowIfNull(random);
        if (outline.Count < 3 || !(treesPer1000SquareMeters > 0f)) return Array.Empty<LandTree>();

        var area = MathF.Abs(PolygonMath.SignedArea(outline));
        var target = Math.Min(5000, (int)MathF.Round(area / 1000f * treesPer1000SquareMeters));
        var clear = keepClear?.ToArray() ?? Array.Empty<OrientedRect>();
        var (min, max) = PolygonMath.GetBounds(outline);
        var trees = new List<LandTree>(target);
        for (var attempt = 0; attempt < target * 30 && trees.Count < target; attempt++)
        {
            var shape = PickShape(random);
            var narrow = shape is TreeShape.Conifer or TreeShape.Cypress or TreeShape.Palm;
            var height = shape switch
            {
                TreeShape.Conifer => 6f + (float)random.NextDouble() * 7f,
                TreeShape.Cypress => 7f + (float)random.NextDouble() * 5f,
                TreeShape.Palm => 5f + (float)random.NextDouble() * 5f,
                TreeShape.Cherry => 4f + (float)random.NextDouble() * 3f,
                _ => 4f + (float)random.NextDouble() * 5f,
            };

            var radius = shape switch
            {
                TreeShape.Cypress => height * (0.08f + (float)random.NextDouble() * 0.03f),
                TreeShape.Palm => height * (0.22f + (float)random.NextDouble() * 0.06f),
                _ when narrow => height * (0.16f + (float)random.NextDouble() * 0.06f),
                _ => height * (0.3f + (float)random.NextDouble() * 0.12f),
            };
            var position = new Vector2(min.X + (float)random.NextDouble() * (max.X - min.X), min.Y + (float)random.NextDouble() * (max.Y - min.Y));

            if (!PolygonMath.Contains(outline, position) || PolygonMath.DistanceToBoundary(outline, position) < radius * 0.8f + 0.5f) continue;
            if (clear.Any(r => new OrientedRect(r.Center, r.Size + new Vector2(radius * 2f + 1f), r.HeadingDegrees).Contains(position))) continue;
            if (trees.Any(t => Vector2.Distance(t.Position, position) < (t.CrownRadius + radius) * 0.9f)) continue;

            trees.Add(new LandTree(position, MathF.Round(height, 2), MathF.Round(radius, 2), shape));
        }

        return trees;
    }

    /// <summary>
    /// What the next scattered tree is: mostly broadleaf, a good share of conifers, a few cypresses and palms, and
    /// once in a great while a cherry in blossom.
    /// </summary>
    internal static TreeShape PickShape(Random random) => random.NextDouble() switch
    {
        < 0.010 => TreeShape.Cherry,
        < 0.075 => TreeShape.Palm,
        < 0.150 => TreeShape.Cypress,
        < 0.430 => TreeShape.Conifer,
        _ => TreeShape.Broadleaf,
    };

    /// <summary><see cref="Name"/> when set, otherwise <see cref="Id"/>.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name;

    /// <summary>Plan-view area in square meters.</summary>
    public float Area => MathF.Abs(PolygonMath.SignedArea(Points));

    /// <summary>True when the plan-view point lies inside the outline.</summary>
    public bool Contains(Vector2 point) => PolygonMath.Contains(Points, point);

    /// <summary>The smallest axis-aligned rectangle containing the outline.</summary>
    public (Vector2 Min, Vector2 Max) GetAxisAlignedBounds() => PolygonMath.GetBounds(Points);

    internal IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) yield return "Land area id must not be empty.";
        if (Points is null || Points.Count < 3)
        {
            yield return $"Land area '{Id}' needs at least three points.";
            yield break;
        }

        if (Points.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y))) yield return $"Land area '{Id}' has a non-finite point.";
        else if (!PolygonMath.IsSimple(Points)) yield return $"Land area '{Id}' outline must not cross itself or repeat points.";
        else if (!(Area > 1e-3f)) yield return $"Land area '{Id}' outline has no area.";
        if (!float.IsFinite(Height) || Height < 0f || Height > 50f) yield return $"Land area '{Id}' height must be between 0 and 50 m.";
        if (Trees is null)
        {
            yield return $"Land area '{Id}' must have a Trees list.";
        }
        else if (Trees.Any(t => !float.IsFinite(t.Position.X) || !float.IsFinite(t.Position.Y) || !(t.Height >= 1f && t.Height <= 40f) ||
                                !(t.CrownRadius >= 0.3f && t.CrownRadius <= 15f) || !Enum.IsDefined(t.Shape)))
        {
            yield return $"Land area '{Id}' has a tree with a non-finite position, a height outside 1–40 m or a crown radius outside 0.3–15 m.";
        }
        if (!Enum.IsDefined(Kind)) yield return $"Land area '{Id}' has an unknown kind '{Kind}'.";
    }
}
