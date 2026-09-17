using System.Numerics;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Domain;

/// <summary>Surface of a <see cref="LandArea"/>; controls how it is drawn.</summary>
public enum LandKind
{
    /// <summary>Paved quay or pier head: a solid light concrete block.</summary>
    Quay,

    /// <summary>Rubble-mound breakwater: the area is filled with a pile of rocks sloping down to the water.</summary>
    Breakwater,

    /// <summary>Lawn or park: a solid green block.</summary>
    Grass,
}

/// <summary>
/// A flat piece of land such as a quay, breakwater or lawn: a polygon outline in plan coordinates with one top height
/// for the whole area. Land slips (<see cref="Slip.OnLand"/>) can be placed on it for boats stored or maintained ashore.
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
    /// <param name="id">Unique id (case-insensitive). Land slips reference it.</param>
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

    /// <summary>Unique id (case-insensitive). Land slips reference it through <see cref="Slip.LandAreaId"/>.</summary>
    public string Id { get; init; }

    /// <summary>Display name (tooltips); the id is used when null.</summary>
    public string? Name { get; init; }

    /// <summary>Outline in plan coordinates (X = world X, Y = world Z). The last point connects back to the first.</summary>
    public IReadOnlyList<Vector2> Points { get; init; }

    /// <summary>Height of the top surface above the water, in meters, over the whole area.</summary>
    public float Height { get; init; }

    /// <summary>Surface type.</summary>
    public LandKind Kind { get; init; }

    /// <summary><see cref="Name"/> when set, otherwise <see cref="Id"/>.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name!;

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
        if (!Enum.IsDefined(Kind)) yield return $"Land area '{Id}' has an unknown kind '{Kind}'.";
    }
}
