using System.Numerics;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Geometry;

/// <summary>
/// Lays out the lanes the passing traffic runs along, and says where each vessel on them is at a given moment.
/// </summary>
/// <remarks>
/// <para>
/// The lanes follow the coast rather than cutting across the map at some angle of their own: each one is the
/// shoreline pushed out to sea, so its ends run alongside the shoreline's endless segments and its middle curves
/// between them. A marina with no shoreline behind it has nothing to be parallel to, and gets straight lanes.
/// </para>
/// <para>
/// <b>How near they come.</b> <see cref="MarineTraffic.Clearance"/> is the closest approach of the <i>nearest</i>
/// lane to the middle of the marina, and the rest step out to sea from there by
/// <see cref="MarineTraffic.LaneSpacing"/> each. That is what makes the setting mean something on its own: 300 meters
/// puts the near lane 300 meters off, whatever size the marina is.
/// </para>
/// <para>
/// <b>Which way they run.</b> Every vessel in a lane runs the same way, and neighbouring lanes run opposite ways, as
/// a traffic separation scheme does — so vessels overtake within a lane and pass port to port between lanes, and
/// never meet head-on. The lane's points are stored in the direction its traffic travels.
/// </para>
/// <para>
/// <b>Why nothing looks drawn with a ruler.</b> Each lane bulges gently seaward along its length by a different
/// amount, so the lanes are not quite parallel; and each vessel holds its own offset within its lane, wandering
/// slowly across it as it goes. Both are bounded well inside <see cref="MarineTraffic.LaneSpacing"/>, so lanes never
/// cross and vessels from neighbouring lanes never meet.
/// </para>
/// <para>
/// Planning is done once, when the traffic or the layout changes; <see cref="Place"/> is then called on every frame
/// and only walks the vessels along lanes that are already known.
/// </para>
/// </remarks>
public static class MarineTrafficPlanner
{
    /// <summary>Builds one lane a given distance out to sea, bulging by <paramref name="wobble"/> along its length.</summary>
    /// <param name="offset">How far out from the coast, in meters.</param>
    /// <param name="wobble">How far the middle of it may bulge further seaward, in meters.</param>
    /// <param name="phase">Where that bulge sits along the lane, so neighbours do not bulge together.</param>
    private delegate List<Vector2> LaneBuilder(float offset, float wobble, float phase);

    /// <summary>
    /// How much of each end of a lane a vessel spends fading in or out. Short on purpose: the ends are far out in
    /// the flat sea, and a long fade there is a smear on the horizon rather than something arriving.
    /// </summary>
    private const float FadeFraction = 0.02f;

    /// <summary>Rounds of corner cutting on the offset coast, so a lane bends where the coast turns.</summary>
    private const int SmoothingPasses = 2;

    /// <summary>How many times the lanes may be pushed further out to get clear of land they would otherwise cross.</summary>
    private const int ClearanceAttempts = 8;

    /// <summary>How far a lane bulges seaward along its length, as a share of the spacing between lanes.</summary>
    private const float WobbleFraction = 0.12f;

    /// <summary>How many bulges a lane has along its length.</summary>
    private const float WobbleWaves = 1.5f;

    /// <summary>How far off the middle of its lane a vessel holds, as a share of the spacing.</summary>
    private const float OffsetFraction = 0.22f;

    /// <summary>How far a vessel wanders across its lane as it goes, as a share of the spacing.</summary>
    private const float SwayFraction = 0.09f;

    /// <summary>How fast that wander goes, in radians per second: one way and back in about seventy seconds.</summary>
    private const float SwayRate = 0.09f;

    /// <summary>
    /// Works out the lanes and the vessels on them. The result is fixed for a given traffic setting and layout, so it
    /// is planned once and then only walked forward in time.
    /// </summary>
    /// <param name="traffic">The traffic settings. Returns nothing when it is off or unsound.</param>
    /// <param name="marina">Plan-view bounds of the marina; the clearance is measured from the middle of it.</param>
    /// <param name="land">Land areas the lanes must not cross.</param>
    /// <param name="shoreline">The mainland behind the shore, whose shape the lanes take. Null for straight lanes.</param>
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
        var usable = shoreline is { } shore && shore.Points.Count >= 2 && !shore.Validate().Any() ? shore : null;

        var build = usable is not null
            ? AlongTheCoast(usable, traffic)
            : AcrossOpenWater(outlines, center, traffic);

        var lanes = BuildLanes(build, outlines, center, traffic);
        if (lanes.Count == 0) return Array.Empty<TrafficLane>();

        // Share the vessels out over the lanes, so raising the lane count spreads the sea thinner rather than filling
        // it: each lane gets its share spread evenly along it, give or take a nudge so they are not in step.
        var random = new Random(traffic.Seed);
        var mix = traffic.EffectiveVessels;
        for (var index = 0; index < lanes.Count; index++)
        {
            var share = (wanted - index + lanes.Count - 1) / lanes.Count;
            for (var i = 0; i < share; i++)
            {
                lanes[index].Vessels.Add(new LaneVessel(
                    mix[random.Next(mix.Count)],
                    (i + 0.5f + ((float)random.NextDouble() - 0.5f) * 0.6f) / share,
                    0.8f + (float)random.NextDouble() * 0.4f,
                    ((float)random.NextDouble() * 2f - 1f) * OffsetFraction,
                    (float)random.NextDouble() * MathF.Tau));
            }
        }

        return lanes;
    }

    /// <summary>
    /// Where every vessel is at a moment in time. Cheap enough to call on every frame: it is a walk along lines that
    /// were worked out once, plus the slow wander that keeps them off a ruled line.
    /// </summary>
    /// <param name="lanes">The lanes from <see cref="Plan"/>.</param>
    /// <param name="traffic">The traffic settings the lanes were planned with.</param>
    /// <param name="seconds">Seconds since the visualizer started.</param>
    public static IEnumerable<TrafficVessel> Place(IReadOnlyList<TrafficLane> lanes, MarineTraffic traffic, double seconds)
    {
        ArgumentNullException.ThrowIfNull(lanes);
        ArgumentNullException.ThrowIfNull(traffic);

        var spread = traffic.LaneSpacing;
        foreach (var lane in lanes)
        {
            if (lane.Length < 1f) continue;

            // Far enough ahead to read the way the vessel is actually going, including its wander, and near enough
            // that it is still the same stretch of lane.
            var step = MathF.Min(0.25f, MathF.Max(4f, lane.Length * 0.0008f) / lane.Length);

            foreach (var vessel in lane.Vessels)
            {
                var travelled = (float)(seconds * traffic.SpeedMetersPerSecond * vessel.SpeedFactor) / lane.Length;
                var along = Fraction(vessel.Phase + travelled);

                var from = MathF.Min(along, 1f - step);
                var here = Drift(lane, vessel, from, seconds, spread);
                var ahead = Drift(lane, vessel, from + step, seconds, spread);

                var heading = ahead - here;
                var direction = heading.LengthSquared() > 1e-8f ? Vector2.Normalize(heading) : lane.At(along).Direction;

                yield return new TrafficVessel(
                    vessel.Type,
                    Drift(lane, vessel, along, seconds, spread),
                    MarinaMath.DirectionToHeading(direction),
                    FadeAt(along));
            }
        }
    }

    /// <summary>Where a vessel actually is: its place along the lane, pushed off the middle of it by its own wander.</summary>
    private static Vector2 Drift(TrafficLane lane, LaneVessel vessel, float along, double seconds, float spacing)
    {
        var (position, direction) = lane.At(along);

        // Two slow waves of different periods, so the wander never reads as a repeating wobble.
        var time = (float)seconds * SwayRate;
        var sway = MathF.Sin(time + vessel.SwayPhase) * 0.62f + MathF.Sin(time * 0.41f + vessel.SwayPhase * 1.7f) * 0.38f;
        var sideways = (vessel.Offset + sway * SwayFraction) * spacing;

        return position + new Vector2(-direction.Y, direction.X) * sideways;
    }

    /// <summary>
    /// Builds the lanes at their final distance out: near enough that the first one passes the marina at the asked-for
    /// clearance, and far enough that none of them crosses a quay.
    /// </summary>
    private static List<TrafficLane> BuildLanes(
        LaneBuilder build,
        IReadOnlyList<IReadOnlyList<Vector2>> outlines,
        Vector2 center,
        MarineTraffic traffic)
    {
        var count = Math.Clamp(traffic.LaneCount, 1, MarineTraffic.LaneLimit);
        var spacing = traffic.LaneSpacing;
        var wobble = spacing * WobbleFraction;

        List<Vector2> Lane(float offset, int index) => build(offset + index * spacing, wobble, index * 1.7f);
        List<List<Vector2>> All(float offset) => Enumerable.Range(0, count).Select(index => Lane(offset, index)).ToList();

        // Once the near lane is past the marina, pushing it further out only ever moves it further away, so the right
        // distance can be halved in to rather than solved.
        float Approach(float offset) => ClosestApproach(Lane(offset, 0), center);

        var near = MathF.Max(1f, traffic.Clearance);
        for (var relax = 0; relax < ClearanceAttempts && Approach(near) > traffic.Clearance; relax++) near *= 0.5f;

        var far = near + traffic.Clearance * 2f + 200f;
        while (Approach(far) < traffic.Clearance && far < traffic.Reach) far *= 1.6f;

        for (var step = 0; step < 24 && far - near > 0.5f; step++)
        {
            var middle = (near + far) * 0.5f;
            if (Approach(middle) < traffic.Clearance) near = middle;
            else far = middle;
        }

        // Nothing may sail over a quay, whatever the clearance asks for, so lanes that would cross one are pushed out
        // until none does. This is the one case where the near lane ends up further out than the setting.
        var offset = far;
        if (!IsClear(All(offset), outlines))
        {
            // Half as far again each time, so a breakwater reaching a long way out is cleared in a few steps.
            var blocked = offset;
            for (var attempt = 0; attempt < ClearanceAttempts && !IsClear(All(offset), outlines); attempt++)
            {
                blocked = offset;
                offset = MathF.Max(offset * 1.5f, offset + 100f);
            }

            // Then back towards the marina again, so the traffic ends up as near as the land allows rather than
            // wherever the last step happened to land, which made the slider jump rather than slide.
            for (var step = 0; step < 10 && offset - blocked > 5f; step++)
            {
                var middle = (blocked + offset) * 0.5f;
                if (IsClear(All(middle), outlines)) offset = middle;
                else blocked = middle;
            }
        }

        var lanes = new List<TrafficLane>(count);
        for (var i = 0; i < count; i++)
        {
            var points = Lane(offset, i);
            if (points.Count < 2) continue;

            // Every other lane runs the other way, and its points are turned round so that "along the lane" and
            // "the way its traffic goes" are the same thing everywhere else.
            var reversed = i % 2 == 1;
            if (reversed) points.Reverse();
            lanes.Add(new TrafficLane(points, reversed));
        }

        return lanes;
    }

    /// <summary>Builds a lane a given distance out from the coast, following its shape.</summary>
    private static LaneBuilder AlongTheCoast(Shoreline shore, MarineTraffic traffic) =>
        (offset, wobble, phase) => Wobbled(Offset(shore, offset, traffic.Reach), wobble, phase, shore.LandOnLeft ? -1f : 1f);

    /// <summary>Builds a straight lane a given distance to one side, for a marina with no coast behind it.</summary>
    private static LaneBuilder AcrossOpenWater(
        IReadOnlyList<IReadOnlyList<Vector2>> outlines,
        Vector2 center,
        MarineTraffic traffic)
    {
        var random = new Random(traffic.Seed);
        var heading = (float)random.NextDouble() * MathF.Tau;
        var direction = new Vector2(MathF.Cos(heading), MathF.Sin(heading));

        // The direction is arbitrary, so simply try a few until the near lane misses the land.
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var sideways = new Vector2(-direction.Y, direction.X);
            var middle = center + sideways * MathF.Max(traffic.Clearance, 1f);
            if (IsClear(new[] { Straight(middle, direction, traffic.Reach) }, outlines)) break;

            heading = (float)random.NextDouble() * MathF.Tau;
            direction = new Vector2(MathF.Cos(heading), MathF.Sin(heading));
        }

        var across = new Vector2(-direction.Y, direction.X);
        return (offset, wobble, phase) => Wobbled(Straight(center + across * offset, direction, traffic.Reach), wobble, phase, 1f);
    }

    /// <summary>A straight line through a point, reaching the same distance either way.</summary>
    private static List<Vector2> Straight(Vector2 middle, Vector2 direction, float reach)
    {
        // Divided rather than left as two points, so the bulge along it has somewhere to go.
        const int steps = 24;
        var points = new List<Vector2>(steps + 1);
        for (var i = 0; i <= steps; i++) points.Add(middle + direction * ((i / (float)steps * 2f - 1f) * reach));
        return points;
    }

    /// <summary>
    /// Pushes the middle of a lane seaward by up to <paramref name="amplitude"/>, so lanes side by side are not quite
    /// parallel. Outward only, so the near lane never comes closer than the clearance allows, and nothing at the two
    /// ends moves, so they stay parallel to the coast's endless segments.
    /// </summary>
    private static List<Vector2> Wobbled(List<Vector2> points, float amplitude, float phase, float seawardIsLeft)
    {
        if (amplitude <= 0.01f || points.Count < 5) return points;

        for (var i = 1; i < points.Count - 1; i++)
        {
            var step = points[i + 1] - points[i - 1];
            if (step.LengthSquared() < 1e-8f) continue;

            var along = (i - 1) / (float)(points.Count - 3);
            var window = MathF.Sin(along * MathF.PI);
            var bulge = 0.5f + 0.5f * MathF.Sin(along * WobbleWaves * MathF.Tau + phase);

            var direction = Vector2.Normalize(step);
            points[i] += new Vector2(-direction.Y, direction.X) * (seawardIsLeft * amplitude * window * bulge);
        }

        return points;
    }

    /// <summary>
    /// The shoreline moved <paramref name="offset"/> meters out to sea, its corners rounded off, with both ends
    /// carried on for <paramref name="reach"/> meters the way the endless segments go.
    /// </summary>
    private static List<Vector2> Offset(Shoreline shore, float offset, float reach)
    {
        var points = shore.Points;
        var moved = new List<Vector2>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            // At a bend, lean on both stretches meeting there so the corner moves out evenly.
            var before = i > 0 ? Seaward(shore, points[i - 1], points[i]) : Vector2.Zero;
            var after = i < points.Count - 1 ? Seaward(shore, points[i], points[i + 1]) : Vector2.Zero;
            var normal = before + after;
            normal = normal.LengthSquared() > 1e-6f
                ? Vector2.Normalize(normal)
                : (before.LengthSquared() > 1e-6f ? before : after);
            moved.Add(points[i] + normal * offset);
        }

        var smoothed = Smooth(Divide(moved));

        // The ends carry on the way the shoreline's own endless segments go — taken from the shoreline itself rather
        // than from the offset line, whose first and last segments swing a little where the coast bends.
        var start = Onward(points[1], points[0]);
        var end = Onward(points[^2], points[^1]);

        var path = new List<Vector2>(smoothed.Count + 2) { smoothed[0] + start * reach };
        path.AddRange(smoothed);
        path.Add(smoothed[^1] + end * reach);
        return path;
    }

    /// <summary>The unit direction from a stretch of coast out to sea, which is the side the land is not on.</summary>
    private static Vector2 Seaward(Shoreline shore, Vector2 from, Vector2 to)
    {
        var step = to - from;
        if (step.LengthSquared() < 1e-8f) return Vector2.Zero;

        var direction = Vector2.Normalize(step);
        return shore.LandOnLeft ? new Vector2(direction.Y, -direction.X) : new Vector2(-direction.Y, direction.X);
    }

    /// <summary>
    /// Splits a short line into more points. A straight coast is two points, and two points offset to two points
    /// leave a lane with nowhere to bulge and nothing to round off.
    /// </summary>
    private static List<Vector2> Divide(List<Vector2> points)
    {
        const int wanted = 12;
        if (points.Count >= wanted) return points;

        var each = (int)MathF.Ceiling((wanted - 1f) / (points.Count - 1));
        var divided = new List<Vector2>(points.Count * each) { points[0] };
        for (var i = 0; i < points.Count - 1; i++)
        {
            for (var step = 1; step <= each; step++) divided.Add(Vector2.Lerp(points[i], points[i + 1], step / (float)each));
        }

        return divided;
    }

    /// <summary>Chaikin's corner cutting: each bend becomes a short curve, while the two ends stay where they are.</summary>
    private static List<Vector2> Smooth(List<Vector2> points)
    {
        for (var pass = 0; pass < SmoothingPasses && points.Count >= 3; pass++)
        {
            var rounded = new List<Vector2>(points.Count * 2) { points[0] };
            for (var i = 0; i < points.Count - 1; i++)
            {
                rounded.Add(Vector2.Lerp(points[i], points[i + 1], 0.25f));
                rounded.Add(Vector2.Lerp(points[i], points[i + 1], 0.75f));
            }

            rounded.Add(points[^1]);
            points = rounded;
        }

        return points;
    }

    /// <summary>True when no part of any lane crosses a land area.</summary>
    private static bool IsClear(IReadOnlyList<IReadOnlyList<Vector2>> lanes, IReadOnlyList<IReadOnlyList<Vector2>> outlines)
    {
        if (outlines.Count == 0) return true;

        foreach (var lane in lanes)
        {
            for (var i = 0; i < lane.Count - 1; i++)
            {
                // Long end segments need sampling; the offset coast itself is already finely divided. The ceiling
                // has to be generous: at 64 samples a 6 km run is tested every 93 m, which steps clean over a
                // breakwater 30 m wide and calls the lane clear.
                var steps = Math.Clamp((int)(Vector2.Distance(lane[i], lane[i + 1]) / 20f), 1, 1024);
                for (var step = 0; step < steps; step++)
                {
                    var point = Vector2.Lerp(lane[i], lane[i + 1], step / (float)steps);
                    foreach (var outline in outlines)
                    {
                        if (PolygonMath.Contains(outline, point)) return false;
                    }
                }
            }
        }

        return true;
    }

    /// <summary>How near a lane comes to a point, in meters.</summary>
    private static float ClosestApproach(IReadOnlyList<Vector2> lane, Vector2 point)
    {
        var best = float.MaxValue;
        for (var i = 0; i < lane.Count - 1; i++)
        {
            best = MathF.Min(best, DistanceToSegment(point, lane[i], lane[i + 1]));
        }

        return best;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        if (lengthSquared < 1e-8f) return Vector2.Distance(point, a);

        var t = Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, a + ab * t);
    }

    private static Vector2 Onward(Vector2 from, Vector2 through)
    {
        var step = through - from;
        return step.LengthSquared() < 1e-8f ? Vector2.UnitX : Vector2.Normalize(step);
    }

    /// <summary>Full strength along the lane, fading to nothing at both ends so nothing pops in or out.</summary>
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
}

/// <summary>One lane of passing traffic, and the vessels running along it.</summary>
/// <remarks>
/// The points run in the direction the lane's traffic travels, so everything on it moves from
/// <see cref="Points"/>[0] towards the last point and nothing ever meets head-on.
/// </remarks>
public sealed class TrafficLane
{
    /// <summary>How far along the lane each point is, so a position can be found without walking from the start.</summary>
    private readonly float[] _reached;

    /// <summary>Creates a lane through a line of points in plan coordinates.</summary>
    /// <param name="points">At least two points, in the direction the traffic travels.</param>
    /// <param name="reversed">True when this lane runs the opposite way to the first one.</param>
    /// <exception cref="ArgumentException">Fewer than two points were given.</exception>
    public TrafficLane(IEnumerable<Vector2> points, bool reversed = false)
    {
        ArgumentNullException.ThrowIfNull(points);
        Points = points.ToArray();
        if (Points.Count < 2) throw new ArgumentException("A traffic lane needs at least two points.", nameof(points));

        Reversed = reversed;
        _reached = new float[Points.Count];
        for (var i = 1; i < Points.Count; i++)
        {
            _reached[i] = _reached[i - 1] + Vector2.Distance(Points[i - 1], Points[i]);
        }

        Length = _reached[^1];
    }

    /// <summary>The points the lane runs through, in the order its traffic travels, in plan coordinates.</summary>
    public IReadOnlyList<Vector2> Points { get; }

    /// <summary>How long the lane is, in meters.</summary>
    public float Length { get; }

    /// <summary>True when this lane's traffic runs the opposite way to the first lane's.</summary>
    public bool Reversed { get; }

    /// <summary>How many vessels run along it.</summary>
    public int VesselCount => Vessels.Count;

    /// <summary>The vessels running along this lane.</summary>
    internal List<LaneVessel> Vessels { get; } = new();

    /// <summary>How near the lane comes to a point, in meters.</summary>
    /// <param name="point">The point to measure from, in plan coordinates.</param>
    public float DistanceTo(Vector2 point)
    {
        var best = float.MaxValue;
        for (var i = 0; i < Points.Count - 1; i++)
        {
            var ab = Points[i + 1] - Points[i];
            var lengthSquared = ab.LengthSquared();
            var t = lengthSquared < 1e-8f ? 0f : Math.Clamp(Vector2.Dot(point - Points[i], ab) / lengthSquared, 0f, 1f);
            best = MathF.Min(best, Vector2.Distance(point, Points[i] + ab * t));
        }

        return best;
    }

    /// <summary>Where the lane is a fraction of the way along it, and which way its traffic heads there.</summary>
    /// <param name="along">0 at the start of the lane, 1 at the end. Values outside are clamped.</param>
    public (Vector2 Position, Vector2 Direction) At(float along)
    {
        var distance = Math.Clamp(along, 0f, 1f) * Length;

        var segment = 0;
        while (segment < Points.Count - 2 && _reached[segment + 1] < distance) segment++;

        var from = Points[segment];
        var to = Points[segment + 1];
        var span = _reached[segment + 1] - _reached[segment];
        var t = span > 1e-4f ? (distance - _reached[segment]) / span : 0f;
        var step = to - from;

        return (Vector2.Lerp(from, to, t), step.LengthSquared() > 1e-8f ? Vector2.Normalize(step) : Vector2.UnitX);
    }
}

/// <summary>A vessel's place on its lane: what it is, where it started, how fast it goes and how it wanders.</summary>
/// <param name="Type">The kind of vessel.</param>
/// <param name="Phase">Where along the lane it was at time zero, 0–1.</param>
/// <param name="SpeedFactor">Multiplies the traffic's speed, so they do not move in lockstep.</param>
/// <param name="Offset">How far off the middle of the lane it holds, as a share of the spacing between lanes.</param>
/// <param name="SwayPhase">Where in its slow wander across the lane it started, in radians.</param>
internal readonly record struct LaneVessel(BoatType Type, float Phase, float SpeedFactor, float Offset, float SwayPhase);
