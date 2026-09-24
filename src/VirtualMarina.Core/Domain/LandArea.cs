using System.Numerics;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Resources;

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
    private readonly ValueList<Vector2> _points = ValueList<Vector2>.Empty;
    private readonly ValueList<LandTree> _trees = ValueList<LandTree>.Empty;
    private readonly ValueDictionary _metadata = ValueDictionary.Empty;

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
        _points = ValueList<Vector2>.From(points);
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

    /// <summary>
    /// Outline in plan coordinates (X = world X, Y = world Z). The last point connects back to the first. The record keeps its own
    /// copy, so changing the list it was given later changes nothing; null reads as empty.
    /// </summary>
    public IReadOnlyList<Vector2> Points { get => _points; init => _points = ValueList<Vector2>.From(value); }

    /// <summary>Height of the top surface above the water, in meters, over the whole area.</summary>
    public float Height { get; init; }

    /// <summary>Surface type.</summary>
    public LandKind Kind { get; init; }

    /// <summary>
    /// Trees standing on the area (usually lawns). Their positions are part of the layout, so they never move between sessions; generate
    /// them once with <see cref="GenerateTrees"/> or the designer.
    /// </summary>
    public IReadOnlyList<LandTree> Trees { get => _trees; init => _trees = ValueList<LandTree>.From(value); }

    /// <summary>
    /// Read-only string attributes the host application attaches to this land area, e.g. its own key or a contract
    /// reference. Saved to and loaded from a marina file, and never read by the visualizer.
    /// </summary>
    /// <example><code>land with { Metadata = new Dictionary&lt;string, string&gt; { ["zone"] = "winter storage" } }</code></example>
    public IReadOnlyDictionary<string, string> Metadata { get => _metadata; init => _metadata = ValueDictionary.From(value); }

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
        var clear = (keepClear ?? Enumerable.Empty<OrientedRect>()).Select(rect => new ClearZone(rect)).ToArray();
        var (min, max) = PolygonMath.GetBounds(outline);
        var trees = new List<LandTree>(target);
        var grid = new TreeGrid();
        for (var attempt = 0; attempt < target * 30 && trees.Count < target; attempt++)
        {
            var shape = PickShape(random);
            var profile = TreeShapeProfile.For(shape);
            var height = profile.NextHeight(random);
            var radius = profile.NextCrownRadius(height, random);
            var position = new Vector2(min.X + (float)random.NextDouble() * (max.X - min.X), min.Y + (float)random.NextDouble() * (max.Y - min.Y));

            if (IsInKeepClear(clear, position, radius) || grid.Crowds(position, radius)) continue;
            if (!PolygonMath.Contains(outline, position) || PolygonMath.DistanceToBoundary(outline, position) < radius * 0.8f + 0.5f) continue;

            var tree = new LandTree(position, MathF.Round(height, 2), MathF.Round(radius, 2), shape);
            trees.Add(tree);
            grid.Add(tree);
        }

        return trees;
    }

    /// <summary>True when a crown of <paramref name="radius"/> at <paramref name="position"/> reaches into an area kept clear.</summary>
    private static bool IsInKeepClear(ClearZone[] zones, Vector2 position, float radius)
    {
        // The area grows by the crown's diameter and half a meter each way, as the crown must clear it, not just the trunk.
        var margin = radius + 0.5f;
        foreach (var zone in zones)
        {
            var rel = position - zone.Center;
            if (MathF.Abs(Vector2.Dot(rel, zone.Across)) <= zone.HalfWidth + margin && MathF.Abs(Vector2.Dot(rel, zone.Along)) <= zone.HalfLength + margin) return true;
        }

        return false;
    }

    /// <summary>An area kept clear of trees, with its axes worked out once rather than for every tree tried.</summary>
    private readonly struct ClearZone
    {
        public ClearZone(OrientedRect rect)
        {
            Center = rect.Center;
            Across = rect.LocalX;
            Along = rect.Forward;
            HalfWidth = rect.Width * 0.5f;
            HalfLength = rect.Length * 0.5f;
        }

        public Vector2 Center { get; }

        public Vector2 Across { get; }

        public Vector2 Along { get; }

        public float HalfWidth { get; }

        public float HalfLength { get; }
    }

    /// <summary>
    /// The trees placed so far, bucketed by position, so a new one is only checked against its neighbours instead of against
    /// every tree already standing (up to 5000 of them).
    /// </summary>
    private sealed class TreeGrid
    {
        // No two crowns reach further apart than this, so the neighbours of a tree are all in the 3×3 cells around it.
        private const float CellSize = TreeShapeProfile.MaxCrownRadius * 2f;

        private readonly Dictionary<(int X, int Y), List<LandTree>> _cells = [];

        public void Add(LandTree tree)
        {
            var cell = CellOf(tree.Position);
            if (!_cells.TryGetValue(cell, out var list)) _cells[cell] = list = [];
            list.Add(tree);
        }

        /// <summary>True when a crown of <paramref name="radius"/> at <paramref name="position"/> would overlap a tree already placed.</summary>
        public bool Crowds(Vector2 position, float radius)
        {
            var (cx, cy) = CellOf(position);
            for (var x = cx - 1; x <= cx + 1; x++)
            {
                for (var y = cy - 1; y <= cy + 1; y++)
                {
                    if (!_cells.TryGetValue((x, y), out var list)) continue;
                    foreach (var tree in list)
                    {
                        if (Vector2.Distance(tree.Position, position) < (tree.CrownRadius + radius) * 0.9f) return true;
                    }
                }
            }

            return false;
        }

        private static (int X, int Y) CellOf(Vector2 position) =>
            ((int)MathF.Floor(position.X / CellSize), (int)MathF.Floor(position.Y / CellSize));
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

    /// <summary>
    /// True when every point lies on one straight line, which folds the outline back over itself. That is reported
    /// as having no area rather than as crossing itself, which is what the user actually did wrong.
    /// </summary>
    private static bool IsFlat(IReadOnlyList<Vector2> points)
    {
        var far = points.MaxBy(p => Vector2.DistanceSquared(p, points[0]));
        var along = far - points[0];
        if (along.LengthSquared() < 1e-8f) return true;
        along = Vector2.Normalize(along);
        return points.All(p => MathF.Abs(along.X * (p.Y - points[0].Y) - along.Y * (p.X - points[0].X)) < 1e-3f);
    }

    internal IEnumerable<string> Validate() => Validate(checkOutline: true);

    /// <param name="checkOutline">False to skip the (quadratic) self-crossing check, for an outline already known to be good.</param>
    internal IEnumerable<string> Validate(bool checkOutline)
    {
        if (string.IsNullOrWhiteSpace(Id)) yield return Strings.ErrorLandIdEmpty;
        if (Points.Count < 3)
        {
            yield return Strings.Format(Strings.ErrorLandTooFewPoints, Id);
            yield break;
        }

        if (Points.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y))) yield return Strings.Format(Strings.ErrorLandNonFinitePoint, Id);
        else if (checkOutline && !PolygonMath.IsSimple(Points) && !IsFlat(Points)) yield return Strings.Format(Strings.ErrorLandCrossesItself, Id);
        else if (!(Area > 1e-3f)) yield return Strings.Format(Strings.ErrorLandNoArea, Id);
        if (!float.IsFinite(Height) || Height < 0f || Height > 50f) yield return Strings.Format(Strings.ErrorLandHeight, Id);
        if (Trees.Any(t => !float.IsFinite(t.Position.X) || !float.IsFinite(t.Position.Y) || !(t.Height >= 1f && t.Height <= 40f) ||
                                !(t.CrownRadius >= 0.3f && t.CrownRadius <= 15f) || !Enum.IsDefined(t.Shape)))
        {
            yield return Strings.Format(Strings.ErrorLandTree, Id);
        }
        if (!Enum.IsDefined(Kind)) yield return Strings.Format(Strings.ErrorLandUnknownKind, Id, Kind);
    }
}
