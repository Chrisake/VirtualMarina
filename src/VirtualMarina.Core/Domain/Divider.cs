using System.Collections.ObjectModel;
using System.Numerics;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Domain;

/// <summary>How a divider between berths is built.</summary>
public enum DividerType
{
    /// <summary>Narrow walkable pier with a pile at its outer end.</summary>
    FingerPier = 0,

    /// <summary>A row of mooring piles.</summary>
    Piles = 1,

    /// <summary>A floating boom: a line of floats on the water.</summary>
    Boom = 2,

    /// <summary>A single mooring pile standing at the outer end of the boundary (Mediterranean mooring), with nothing in between.</summary>
    SinglePile = 3,
}

/// <summary>
/// A structure that separates berths: a finger pier, a row of piles, a floating boom or a single pile. It starts at
/// <see cref="Start"/> (usually at the pier edge) and runs <see cref="Length"/> meters along <see cref="HeadingDegrees"/>.
/// </summary>
/// <remarks>
/// Dividers are independent of <see cref="Berth.HasFingerPiers"/>, which draws simple finger piers
/// automatically. Turn that off on berths whose separators you define explicitly.
/// </remarks>
public sealed record Divider
{
    /// <summary>Creates a divider from its start point, heading and length.</summary>
    /// <param name="id">Unique id (case-insensitive).</param>
    /// <param name="start">Start point in plan coordinates, usually at the pier edge.</param>
    /// <param name="headingDegrees">Direction from the start, usually away from the pier (0° = +Z, 90° = +X).</param>
    /// <param name="length">Length in meters.</param>
    /// <param name="type">Finger pier, row of piles, floating boom or single pile.</param>
    /// <example><code>new Divider("A-D1", start: new Vector2(81.75f, 2), headingDegrees: 90, length: 10, DividerType.Piles) { PierId = "A" }</code></example>
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
    /// Optional owning pier. Sets the deck height and material of finger piers, and the divider is
    /// removed together with the pier.
    /// </summary>
    public string? PierId { get; init; }

    /// <summary>Start point in plan coordinates (usually at the pier edge).</summary>
    public Vector2 Start { get; init; }

    /// <summary>Direction from <see cref="Start"/>, in degrees (0° = +Z, 90° = +X).</summary>
    public float HeadingDegrees { get; init; }

    /// <summary>Length in meters.</summary>
    public float Length { get; init; }

    /// <summary>Width of a finger pier or boom, or pile diameter.</summary>
    public float Width { get; init; } = 0.8f;

    /// <summary>Finger pier, row of piles, floating boom or a single pile at the outer end.</summary>
    public DividerType Type { get; init; }

    /// <summary>Distance between piles (<see cref="DividerType.Piles"/>) or floats (<see cref="DividerType.Boom"/>).</summary>
    public float Spacing { get; init; } = 4f;

    /// <summary>
    /// Read-only string attributes the host application attaches to this divider, e.g. its own key or a contract
    /// reference. Saved to and loaded from a marina file, and never read by the visualizer.
    /// </summary>
    /// <example><code>divider with { Metadata = new Dictionary&lt;string, string&gt; { ["asset"] = "BOOM-114" } }</code></example>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>Unit plan-view vector from start to end.</summary>
    public Vector2 Direction => MarinaMath.HeadingToDirection(HeadingDegrees);

    /// <summary>Unit plan-view vector across the divider: the heading's local +X axis, <c>(cos h, −sin h)</c>.</summary>
    public Vector2 Right => MarinaMath.HeadingToRight(HeadingDegrees);

    /// <summary>End point (the pile of a finger pier or a <see cref="DividerType.SinglePile"/> divider stands here).</summary>
    public Vector2 End => Start + Direction * Length;

    /// <summary>Middle point.</summary>
    public Vector2 Center => Start + Direction * (Length * 0.5f);

    /// <summary>Footprint (<see cref="Width"/> × <see cref="Length"/>).</summary>
    public OrientedRect Bounds => new(Center, new Vector2(Width, Length), HeadingDegrees);

    internal IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) yield return "Divider id must not be empty.";
        if (!(Length > 0f && float.IsFinite(Length))) yield return $"Divider '{Id}' must have a positive, finite length.";
        if (!(Width > 0f)) yield return $"Divider '{Id}' must have a positive width.";
        if (!(Spacing >= 0.5f)) yield return $"Divider '{Id}' spacing must be at least 0.5 m.";
        if (!float.IsFinite(Start.X) || !float.IsFinite(Start.Y) || !float.IsFinite(HeadingDegrees)) yield return $"Divider '{Id}' has a non-finite position or heading.";
        if (!Enum.IsDefined(Type)) yield return $"Divider '{Id}' has an unknown type '{Type}'.";
    }
}
