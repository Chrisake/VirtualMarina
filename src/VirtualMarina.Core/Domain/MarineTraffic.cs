using System.Numerics;
using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Domain;

/// <summary>One vessel of the passing traffic, where it is at a moment in time.</summary>
/// <param name="Type">What kind of vessel it is.</param>
/// <param name="Position">Where it is, in plan coordinates.</param>
/// <param name="HeadingDegrees">
/// Which way its bow points, in the library's heading convention (see <c>MarinaMath.HeadingToDirection</c>): 0° = +Z (south),
/// 90° = +X (east), 180° = −Z (north), 270° = −X (west). This is <em>not</em> a compass bearing, where 0° is north; a compass
/// bearing <c>b</c> is the heading <c>180 − b</c>.
/// </param>
/// <param name="Opacity">0–1. Vessels fade in where they appear and out again where they leave.</param>
public readonly record struct TrafficVessel(BoatType Type, Vector2 Position, float HeadingDegrees, float Opacity);

/// <summary>
/// Passing traffic out at sea: vessels running along lanes that sweep past the marina from one edge of the map to
/// the other, appearing far out, crossing the bay and leaving on the far side.
/// </summary>
/// <remarks>
/// <para>
/// It is decoration, not layout: the vessels are not berths and cannot be clicked. Only these settings are stored —
/// the lanes are worked out from the shoreline and the traffic from <see cref="Seed"/>, so a busy sea costs no more
/// to store than an empty one.
/// </para>
/// <para>
/// <b>Where the lanes go.</b> A lane is drawn through three points: one at each edge of the map, out where the
/// mainland ends, sitting <see cref="EdgeClearance"/> off the coast; and one in the middle, passing the marina at
/// <see cref="Clearance"/>. It curves smoothly between them, so a lane sweeps in towards the marina and back out
/// again. Further lanes step out to sea from the first by <see cref="LaneSpacing"/> on average.
/// </para>
/// <para>
/// <b>Which way they run.</b> Every vessel in a lane runs the same way, so nothing ever meets head-on. One or two
/// lanes run opposite ways, as a traffic separation scheme does; beyond that the directions are drawn at random.
/// </para>
/// <para>
/// <b>How many there are.</b> The full <see cref="MaximumVessels"/> is out there to begin with, scattered at random
/// along the lanes, so the sea is busy from the first frame. As each one leaves the map another appears after about
/// <see cref="SpawnDelaySeconds"/> — on a lane of its own, of a different kind, at its own speed and its own offset
/// within the lane.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// marina.SetMarineTraffic(MarineTraffic.None with { IsEnabled = true, Clearance = 400f, LaneCount = 3 });
/// </code>
/// </example>
public sealed record MarineTraffic
{
    private readonly ValueList<BoatType> _vessels = ValueList<BoatType>.Empty;
    private readonly ValueDictionary _metadata = ValueDictionary.Empty;

    /// <summary>The most vessels <see cref="MaximumVessels"/> may be set to.</summary>
    public const int VesselLimit = 60;

    /// <summary>The most lanes <see cref="LaneCount"/> may be set to.</summary>
    /// <remarks>
    /// Past a handful the lanes stop reading as shipping and start reading as a grid, and the outer ones are so far
    /// out that nothing on them can be made out anyway.
    /// </remarks>
    public const int LaneLimit = 8;

    /// <summary>One knot in meters per second.</summary>
    private const float KnotsToMetersPerSecond = 0.514444f;

    /// <summary>Empty sea. This is what a marina has until traffic is switched on.</summary>
    public static MarineTraffic None { get; } = new();

    /// <summary>The mix used when <see cref="Vessels"/> is left empty: what is plausibly passing a marina offshore.</summary>
    public static IReadOnlyList<BoatType> DefaultVessels { get; } = ValueList<BoatType>.From(new[]
    {
        BoatType.MonohullSailboat,
        BoatType.MonohullSailboat,
        BoatType.CatamaranSailboat,
        BoatType.FishingBoat,
        BoatType.FishingBoat,
        BoatType.DayMotorBoat,
        BoatType.MotorYacht,
        BoatType.Ferry,
        BoatType.JetSki,
    });

    /// <summary>
    /// How fast a kind of vessel actually travels, in knots, before <see cref="SpeedPercent"/> is applied: a fishing
    /// boat plods, a jet ski tears past.
    /// </summary>
    /// <param name="type">The kind of vessel.</param>
    /// <remarks>
    /// One speed for the whole sea had a jet ski crawling alongside a fishing boat, which reads as wrong at once. The
    /// speed setting scales these rather than replacing them, so the mix keeps its character however fast it is
    /// turned up.
    /// </remarks>
    public static float CruisingKnots(BoatType type) => type switch
    {
        BoatType.FishingBoat => 7f,
        BoatType.MonohullSailboat => 8f,
        BoatType.CatamaranSailboat => 9.5f,
        BoatType.DayMotorBoat => 14f,
        BoatType.CatamaranMotorboat => 15f,
        BoatType.Ferry => 18f,
        BoatType.MotorYacht => 22f,
        BoatType.JetSki => 30f,
        _ => 10f,
    };

    /// <summary>Draw the traffic. Default false, so a marina is in empty sea until it is asked for.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// The most vessels on the water at once, 1–<see cref="VesselLimit"/> (default 16). How many there actually are
    /// wanders below this as vessels leave and others take their place.
    /// </summary>
    public int MaximumVessels { get; init; } = 16;

    /// <summary>
    /// How near the middle of the marina the nearest lane passes, in meters (default 300): the closest approach of
    /// the lane, not a margin added to the size of the marina.
    /// </summary>
    public float Clearance { get; init; } = 300f;

    /// <summary>
    /// How far off the coast a lane sits where it reaches the edge of the map, in meters (default 700). The two ends
    /// of a lane are set by this and its middle by <see cref="Clearance"/>, so the two together say how sharply a
    /// lane sweeps in towards the marina.
    /// </summary>
    public float EdgeClearance { get; init; } = 700f;

    /// <summary>
    /// How many lanes of traffic there are, 1–<see cref="LaneLimit"/> (default 2). The first passes at
    /// <see cref="Clearance"/> and each one after it is <see cref="LaneSpacing"/> further out to sea on average.
    /// </summary>
    public int LaneCount { get; init; } = 2;

    /// <summary>
    /// How far apart the lanes are, in meters (default 160). It also sets how far a vessel may hold off the middle of
    /// its lane, so widening the lanes loosens the traffic on them too.
    /// </summary>
    public float LaneSpacing { get; init; } = 160f;

    /// <summary>
    /// How fast the traffic goes, as a percentage of what each kind of vessel really does (default 100). Every
    /// vessel keeps its own speed from <see cref="CruisingKnots"/>; this speeds the whole sea up or slows it down.
    /// </summary>
    public float SpeedPercent { get; init; } = 100f;

    /// <summary>
    /// Roughly how long after a vessel leaves the map before another appears, in seconds (default 25). Each wait is
    /// drawn at random around this, so they do not arrive in step.
    /// </summary>
    public float SpawnDelaySeconds { get; init; } = 25f;

    /// <summary>
    /// How far the lanes run when there is no shoreline to take their ends from, in meters (default 8000). With a
    /// shoreline the ends come from where its endless segments reach the edge of the map instead.
    /// </summary>
    /// <remarks>
    /// A reach shorter than <see cref="Clearance"/> is not an error: the planner simply runs the lane out far enough
    /// to be worth having. It used to be refused, which meant a design carrying a short reach from an older version
    /// threw the moment the clearance slider was pushed past it.
    /// </remarks>
    public float Reach { get; init; } = 8000f;

    /// <summary>Keeps the lanes and the traffic on them the same between sessions. Any number will do.</summary>
    public int Seed { get; init; } = 1;

    /// <summary>
    /// The kinds of vessel out there, drawn from at random. Repeat a type to make it more common. Empty means
    /// <see cref="DefaultVessels"/>, and so does null. The record keeps its own copy.
    /// </summary>
    public IReadOnlyList<BoatType> Vessels { get => _vessels; init => _vessels = ValueList<BoatType>.From(value); }

    /// <summary>Read-only string attributes the host application attaches to the traffic. Saved with the design.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get => _metadata; init => _metadata = ValueDictionary.From(value); }

    /// <summary>How many lanes this actually lays out; 0 when it is switched off.</summary>
    public int EffectiveLanes => IsEnabled ? Math.Clamp(LaneCount, 1, LaneLimit) : 0;

    /// <summary>The most vessels that may be out at once; 0 when it is switched off.</summary>
    public int VesselCount => IsEnabled ? Math.Clamp(MaximumVessels, 1, VesselLimit) : 0;

    /// <summary>The mix actually used: <see cref="Vessels"/>, or <see cref="DefaultVessels"/> when that is empty.</summary>
    public IReadOnlyList<BoatType> EffectiveVessels => Vessels.Count > 0 ? Vessels : DefaultVessels;

    /// <summary>How fast one kind of vessel goes here, in meters per second, with the speed setting applied.</summary>
    /// <param name="type">The kind of vessel.</param>
    public float SpeedMetersPerSecond(BoatType type) =>
        CruisingKnots(type) * KnotsToMetersPerSecond * MathF.Max(0f, SpeedPercent) * 0.01f;

    /// <summary>Problems that would stop the traffic being drawn, empty when it is sound.</summary>
    public IEnumerable<string> Validate()
    {
        if (MaximumVessels < 1 || MaximumVessels > VesselLimit)
        {
            yield return Strings.Format(Strings.ErrorTrafficVessels, VesselLimit);
        }

        if (LaneCount < 1 || LaneCount > LaneLimit) yield return Strings.Format(Strings.ErrorTrafficLanes, LaneLimit);
        if (!IsPositive(LaneSpacing)) yield return Strings.ErrorTrafficLaneSpacing;
        if (!IsNonNegative(Clearance)) yield return Strings.ErrorTrafficClearance;
        if (!IsNonNegative(EdgeClearance)) yield return Strings.ErrorTrafficEdgeClearance;
        if (!IsPositive(SpeedPercent)) yield return Strings.ErrorTrafficSpeed;
        if (!IsNonNegative(SpawnDelaySeconds)) yield return Strings.ErrorTrafficSpawnDelay;
        if (!IsPositive(Reach)) yield return Strings.ErrorTrafficReach;

        foreach (var vessel in Vessels.Where(vessel => !Enum.IsDefined(vessel)).Distinct())
        {
            yield return Strings.Format(Strings.ErrorTrafficUnknownVessel, vessel);
        }
    }

    private static bool IsPositive(float value) => float.IsFinite(value) && value > 0f;

    private static bool IsNonNegative(float value) => float.IsFinite(value) && value >= 0f;
}
