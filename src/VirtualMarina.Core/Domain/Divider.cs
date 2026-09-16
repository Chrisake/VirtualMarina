using System.Numerics;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Domain;

/// <summary>How a divider between slips is built.</summary>
public enum DividerType
{
    /// <summary>Narrow walkable pier with a pile at its outer end.</summary>
    FingerPier = 0,

    /// <summary>A row of mooring piles.</summary>
    Piles = 1,

    /// <summary>A floating boom: a line of floats on the water.</summary>
    Boom = 2,
}

/// <summary>
/// A structure that separates slips: a finger pier, a row of piles or a floating boom. It starts at
/// <see cref="Start"/> (usually at the dock edge) and runs <see cref="Length"/> meters along <see cref="HeadingDegrees"/>.
/// </summary>
/// <remarks>
/// Dividers are independent of <see cref="Slip.HasFingerPiers"/>, which draws simple finger piers
/// automatically. Turn that off on slips whose separators you define explicitly.
/// </remarks>
public sealed record Divider
{
    /// <summary>Creates a divider from its start point, heading and length.</summary>
    /// <param name="id">Unique id (case-insensitive).</param>
    /// <param name="start">Start point in plan coordinates, usually at the dock edge.</param>
    /// <param name="headingDegrees">Direction from the start, usually away from the dock (0° = +Z, 90° = +X).</param>
    /// <param name="length">Length in meters.</param>
    /// <param name="type">Finger pier, row of piles or floating boom.</param>
    /// <example><code>new Divider("A-D1", start: new Vector2(81.75f, 2), headingDegrees: 90, length: 10, DividerType.Piles) { DockId = "A" }</code></example>
    public Divider(string id, Vector2 start, float headingDegrees, float length, DividerType type = DividerType.FingerPier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        Start = start;
        HeadingDegrees = headingDegrees;
        Length = length;
        Type = type;
    }

    /// <summary>Creates a divider from its center point, length and orientation.</summary>
    public static Divider FromCenter(string id, Vector2 center, float length, float headingDegrees, DividerType type = DividerType.FingerPier) =>
        new(id, center - MarinaMath.HeadingToDirection(headingDegrees) * (length * 0.5f), headingDegrees, length, type);

    /// <summary>Unique id (case-insensitive).</summary>
    public string Id { get; init; }

    /// <summary>
    /// Optional owning dock. Sets the deck height and material of finger piers, and the divider is
    /// removed together with the dock.
    /// </summary>
    public string? DockId { get; init; }

    /// <summary>Start point in plan coordinates (usually at the dock edge).</summary>
    public Vector2 Start { get; init; }

    /// <summary>Direction from <see cref="Start"/>, in degrees (0° = +Z, 90° = +X).</summary>
    public float HeadingDegrees { get; init; }

    /// <summary>Length in meters.</summary>
    public float Length { get; init; }

    /// <summary>Width of a finger pier or boom, or pile diameter.</summary>
    public float Width { get; init; } = 0.8f;

    /// <summary>Finger pier, row of piles or floating boom.</summary>
    public DividerType Type { get; init; }

    /// <summary>Distance between piles (<see cref="DividerType.Piles"/>) or floats (<see cref="DividerType.Boom"/>).</summary>
    public float Spacing { get; init; } = 4f;

    /// <summary>Unit plan-view vector from start to end.</summary>
    public Vector2 Direction => MarinaMath.HeadingToDirection(HeadingDegrees);

    /// <summary>Unit plan-view vector across the divider: the heading's local +X axis, <c>(cos h, −sin h)</c>.</summary>
    public Vector2 Right => MarinaMath.HeadingToRight(HeadingDegrees);

    /// <summary>End point (a finger pier's end pile stands here).</summary>
    public Vector2 End => Start + Direction * Length;

    /// <summary>Middle point.</summary>
    public Vector2 Center => Start + Direction * (Length * 0.5f);

    /// <summary>Footprint (<see cref="Width"/> × <see cref="Length"/>).</summary>
    public OrientedRect Bounds => new(Center, new Vector2(Width, Length), HeadingDegrees);

    internal IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) yield return "Divider id must not be empty.";
        if (!(Length > 0f)) yield return $"Divider '{Id}' must have a positive length.";
        if (!(Width > 0f)) yield return $"Divider '{Id}' must have a positive width.";
        if (!(Spacing >= 0.5f)) yield return $"Divider '{Id}' spacing must be at least 0.5 m.";
        if (!float.IsFinite(Start.X) || !float.IsFinite(Start.Y) || !float.IsFinite(HeadingDegrees)) yield return $"Divider '{Id}' has a non-finite position or heading.";
        if (!Enum.IsDefined(Type)) yield return $"Divider '{Id}' has an unknown type '{Type}'.";
    }
}
