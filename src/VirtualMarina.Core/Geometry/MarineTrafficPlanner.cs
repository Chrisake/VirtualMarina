using System.Numerics;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Geometry;

/// <summary>
/// Lays out the lanes the passing traffic runs along, and says where each vessel is at a given moment.
/// </summary>
/// <remarks>
/// <para>
/// A lane is a straight line across the map. Lanes are tried at random from the traffic's seed and kept only when the
/// whole line stays <see cref="MarineTraffic.Clearance"/> away from the marina, from every land area, and from the
/// mainland behind the shore — so a vessel can never appear to sail over a quay or through the piers.
/// </para>
/// <para>
/// Planning is done once, when the traffic or the layout changes; <see cref="Place"/> is then called on every frame
/// and only walks the vessels along lanes that are already known to be clear.
/// </para>
/// </remarks>
public static class MarineTrafficPlanner
{
    /// <summary>How much of each end of a lane a vessel spends fading in or out.</summary>
    private const float FadeFraction = 0.12f;

    /// <summary>Points tested along a candidate lane. Enough to catch a lane clipping a corner of the land.</summary>
    private const int SamplesPerLane = 48;

    /// <summary>How many lanes are tried for each one kept, before giving up on a crowded map.</summary>
    private const int AttemptsPerLane = 12;

    /// <summary>
    /// Works out the lanes and the vessels on them. The result is fixed for a given traffic setting and layout, so it
    /// is planned once and then only walked forward in time.
    /// </summary>
    /// <param name="traffic">The traffic settings. Returns nothing when it is off or unsound.</param>
    /// <param name="marina">Plan-view bounds of the marina, which lanes must keep clear of.</param>
    /// <param name="land">Land areas to keep clear of.</param>
    /// <param name="shoreline">The mainland behind the shore, or null.</param>
    public static IReadOnlyList<TrafficLane> Plan(
        MarineTraffic traffic,
        (Vector2 Min, Vector2 Max) marina,
        IEnumerable<LandArea> land,
        Shoreline? shoreline)
    {
        ArgumentNullException.ThrowIfNull(traffic);
        ArgumentNullException.ThrowIfNull(land);
        var wanted = traffic.VesselCount;
        if (wanted == 0 || traffic.Validate().Any()) return Array.Empty<TrafficLane>();

        var outlines = land.Where(area => area?.Points is { Count: >= 3 }).Select(area => area.Points).ToArray();
        var center = (marina.Min + marina.Max) * 0.5f;
        var mix = traffic.EffectiveVessels;
        var random = new Random(traffic.Seed);

        // Vessels are spread over lanes rather than given one each, so a busy sea still reads as shipping lanes.
        var laneCount = Math.Max(1, (int)MathF.Ceiling(wanted / 2.4f));
        var lanes = new List<TrafficLane>(laneCount);

        for (var attempt = 0; attempt < laneCount * AttemptsPerLane && lanes.Count < laneCount; attempt++)
        {
            var heading = (float)random.NextDouble() * MathF.Tau;
            var direction = new Vector2(MathF.Cos(heading), MathF.Sin(heading));
            var sideways = new Vector2(-direction.Y, direction.X);

            // How far off to one side of the marina the lane passes, never nearer than the clearance asks.
            var nearest = traffic.Clearance + Vector2.Distance(marina.Min, marina.Max) * 0.5f;
            if (nearest >= traffic.Reach) break;
            var offset = nearest + (float)random.NextDouble() * (traffic.Reach - nearest);
            if (random.Next(2) == 0) offset = -offset;

            var middle = center + sideways * offset;
            var lane = new TrafficLane(middle - direction * traffic.Reach, middle + direction * traffic.Reach);
            if (!IsClear(lane, traffic.Clearance, marina, outlines, shoreline)) continue;

            // Keep the lanes apart, so the traffic does not stack up along one line.
            if (lanes.Any(other => MathF.Abs(other.DistanceTo(middle)) < traffic.Clearance * 0.5f)) continue;
            lanes.Add(lane);
        }

        if (lanes.Count == 0) return Array.Empty<TrafficLane>();

        // Share the vessels out over the lanes that were found room for.
        for (var i = 0; i < wanted; i++)
        {
            var lane = lanes[i % lanes.Count];
            lane.Vessels.Add(new LaneVessel(
                mix[random.Next(mix.Count)],
                (float)random.NextDouble(),
                0.75f + (float)random.NextDouble() * 0.5f,
                random.Next(2) == 0));
        }

        return lanes;
    }

    /// <summary>
    /// Where every vessel is at a moment in time. Cheap enough to call on every frame: it is a walk along lines that
    /// were already checked when they were planned.
    /// </summary>
    /// <param name="lanes">Lanes from <see cref="Plan"/>.</param>
    /// <param name="traffic">The traffic settings the lanes were planned with.</param>
    /// <param name="seconds">Seconds since the visualizer started.</param>
    public static IEnumerable<TrafficVessel> Place(IReadOnlyList<TrafficLane> lanes, MarineTraffic traffic, double seconds)
    {
        ArgumentNullException.ThrowIfNull(lanes);
        ArgumentNullException.ThrowIfNull(traffic);

        foreach (var lane in lanes)
        {
            var length = lane.Length;
            if (length < 1f) continue;

            foreach (var vessel in lane.Vessels)
            {
                var speed = traffic.SpeedMetersPerSecond * vessel.SpeedFactor;
                var travelled = (float)(seconds * speed) / length;

                // Round and round the lane: as one vessel fades out at the far end another fades in behind it.
                var along = Fraction(vessel.Phase + travelled);
                var direction = vessel.Reversed ? -lane.Direction : lane.Direction;
                var position = vessel.Reversed
                    ? Vector2.Lerp(lane.End, lane.Start, along)
                    : Vector2.Lerp(lane.Start, lane.End, along);

                yield return new TrafficVessel(
                    vessel.Type,
                    position,
                    MarinaMath.DirectionToHeading(direction),
                    FadeAt(along));
            }
        }
    }

    /// <summary>Full strength in the middle of the lane, fading to nothing at both ends so nothing pops in or out.</summary>
    private static float FadeAt(float along)
    {
        var edge = MathF.Min(along, 1f - along);
        return Math.Clamp(edge / FadeFraction, 0f, 1f);
    }

    private static float Fraction(float value)
    {
        var wrapped = value % 1f;
        return wrapped < 0f ? wrapped + 1f : wrapped;
    }

    /// <summary>True when no part of the lane comes within the clearance of the marina, the land or the mainland.</summary>
    private static bool IsClear(
        TrafficLane lane,
        float clearance,
        (Vector2 Min, Vector2 Max) marina,
        IReadOnlyList<IReadOnlyList<Vector2>> outlines,
        Shoreline? shoreline)
    {
        for (var i = 0; i <= SamplesPerLane; i++)
        {
            var point = Vector2.Lerp(lane.Start, lane.End, i / (float)SamplesPerLane);
            if (DistanceToBox(point, marina.Min, marina.Max) < clearance) return false;

            foreach (var outline in outlines)
            {
                if (PolygonMath.Contains(outline, point)) return false;
                if (PolygonMath.DistanceToBoundary(outline, point) < clearance) return false;
            }

            if (shoreline is not null && (shoreline.Contains(point) || shoreline.DistanceToShore(point) < clearance))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Distance from a point to an axis-aligned box; 0 when the point is inside it.</summary>
    private static float DistanceToBox(Vector2 point, Vector2 min, Vector2 max)
    {
        var outside = Vector2.Max(Vector2.Max(min - point, point - max), Vector2.Zero);
        return outside.Length();
    }
}

/// <summary>One straight lane of passing traffic, and the vessels running along it.</summary>
public sealed class TrafficLane
{
    /// <summary>Creates a lane between two points in plan coordinates.</summary>
    /// <param name="start">Where the lane begins.</param>
    /// <param name="end">Where it ends.</param>
    public TrafficLane(Vector2 start, Vector2 end)
    {
        Start = start;
        End = end;
        Length = Vector2.Distance(start, end);
        Direction = Length > 1e-3f ? (end - start) / Length : Vector2.UnitX;
    }

    /// <summary>Where the lane begins, in plan coordinates.</summary>
    public Vector2 Start { get; }

    /// <summary>Where the lane ends.</summary>
    public Vector2 End { get; }

    /// <summary>Unit direction from <see cref="Start"/> to <see cref="End"/>.</summary>
    public Vector2 Direction { get; }

    /// <summary>How long the lane is, in meters.</summary>
    public float Length { get; }

    /// <summary>The vessels running along this lane.</summary>
    internal List<LaneVessel> Vessels { get; } = new();

    /// <summary>How many vessels run along this lane.</summary>
    public int VesselCount => Vessels.Count;

    /// <summary>Signed distance from a point to the infinite line the lane lies on, in meters.</summary>
    /// <param name="point">The point to measure from.</param>
    public float DistanceTo(Vector2 point)
    {
        var offset = point - Start;
        return offset.X * Direction.Y - offset.Y * Direction.X;
    }
}

/// <summary>A vessel's place on its lane: what it is, where it started and how fast it goes.</summary>
/// <param name="Type">The kind of vessel.</param>
/// <param name="Phase">Where along the lane it was at time zero, 0–1.</param>
/// <param name="SpeedFactor">Multiplies the traffic's speed, so they do not move in lockstep.</param>
/// <param name="Reversed">True when it runs from the end of the lane back to the start.</param>
internal readonly record struct LaneVessel(BoatType Type, float Phase, float SpeedFactor, bool Reversed);
