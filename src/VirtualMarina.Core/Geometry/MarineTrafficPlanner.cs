using System.Numerics;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Geometry;

/// <summary>Lays out the lanes the passing traffic runs along.</summary>
/// <remarks>
/// <para>
/// A lane is drawn through three points and curved smoothly between them: one at each edge of the map, out where the
/// mainland ends and <see cref="MarineTraffic.EdgeClearance"/> off the coast, and one in the middle passing the
/// marina at <see cref="MarineTraffic.Clearance"/>. So a lane sweeps in towards the marina and back out again, and
/// both its ends are far enough away that a vessel appears and disappears out of sight.
/// </para>
/// <para>
/// Three points is the whole of it. Pushing the coastline itself out to sea, which is what this used to do, folds
/// over on itself wherever the coast turns in — a bay narrower than the push becomes a loop — and the lane came out
/// with a right-angle kink in it and a six-kilometre jump between its first two points.
/// </para>
/// <para>
/// Lanes beyond the first step out to sea by <see cref="MarineTraffic.LaneSpacing"/> on average, give or take, so
/// they are not ruled parallel. Every vessel in a lane runs the same way and nothing meets head-on; one or two lanes
/// run opposite ways, and beyond that the directions are drawn at random.
/// </para>
/// </remarks>
public static class MarineTrafficPlanner
{
    /// <summary>Points a lane is drawn with. Enough that the curve reads as a curve at any zoom.</summary>
    private const int CurvePoints = 48;

    /// <summary>How much the spacing between two lanes may differ from the setting, as a share of it.</summary>
    private const float SpacingJitter = 0.18f;

    /// <summary>
    /// Works out the lanes. The result depends only on the settings and the shape of the coast, so it is worked out
    /// once and then left alone while the traffic moves along it.
    /// </summary>
    /// <param name="traffic">The traffic settings. Returns nothing when it is off or unsound.</param>
    /// <param name="marina">Plan-view bounds of the marina; the clearance is measured from the middle of it.</param>
    /// <param name="shoreline">The mainland behind the shore, which the lanes take their ends from. Null for straight lanes.</param>
    public static IReadOnlyList<TrafficLane> Plan(MarineTraffic traffic, (Vector2 Min, Vector2 Max) marina, Shoreline? shoreline)
    {
        ArgumentNullException.ThrowIfNull(traffic);
        if (!traffic.IsEnabled || traffic.Validate().Any()) return Array.Empty<TrafficLane>();

        var center = (marina.Min + marina.Max) * 0.5f;
        var shape = shoreline is { } shore && shore.Points.Count >= 2 && shore.EndsAtTheMapEdge() is { } ends
            ? AlongTheCoast(shore, ends, center)
            : AcrossOpenWater(center, traffic.Reach, traffic.Clearance, traffic.Seed);

        var random = new Random(traffic.Seed);
        var lanes = new List<TrafficLane>(traffic.EffectiveLanes);
        var offset = 0f;

        for (var index = 0; index < traffic.EffectiveLanes; index++)
        {
            List<Vector2> Draw(float middle) => Curve(
                shape.Start + shape.StartSeaward * (traffic.EdgeClearance + offset),
                shape.Middle + shape.MiddleSeaward * middle,
                shape.End + shape.EndSeaward * (traffic.EdgeClearance + offset));

            // The curve goes through the middle point, but on a coast that turns sharply it can swing nearer to the
            // marina somewhere else along its length. Measure what it actually does and pull the middle back out
            // until it matches, so the clearance setting means what it says on any shape of coast.
            var wanted = traffic.Clearance + offset;
            var middleOffset = wanted;
            var points = Draw(middleOffset);
            for (var pass = 0; pass < 4; pass++)
            {
                var actual = new TrafficLane(points).DistanceTo(center);
                if (MathF.Abs(actual - wanted) <= MathF.Max(1f, wanted * 0.01f)) break;
                middleOffset += wanted - actual;
                points = Draw(middleOffset);
            }

            // One or two lanes are a separation scheme and run opposite ways; more than that is a stretch of open
            // water with shipping crossing it, and always alternating would look like a diagram.
            var reversed = traffic.EffectiveLanes <= 2 ? index % 2 == 1 : random.Next(2) == 0;
            if (reversed) points.Reverse();
            lanes.Add(new TrafficLane(points, reversed));

            // On average the spacing asked for, so the lanes do not sit in ruled parallel.
            offset += traffic.LaneSpacing * (1f + ((float)random.NextDouble() * 2f - 1f) * SpacingJitter);
        }

        return lanes;
    }

    /// <summary>The three points a lane is built from, and which way out to sea is at each of them.</summary>
    private readonly record struct LaneShape(
        Vector2 Start,
        Vector2 StartSeaward,
        Vector2 Middle,
        Vector2 MiddleSeaward,
        Vector2 End,
        Vector2 EndSeaward);

    /// <summary>A lane hugging the coast: its ends where the mainland ends, its middle beside the marina.</summary>
    private static LaneShape AlongTheCoast(Shoreline shore, (Vector2 Start, Vector2 End) ends, Vector2 center)
    {
        // Both taken walking the line the way it was drawn, since that is the way "the land is on the left" is
        // meant. Reading the first segment backwards, which is the way its endless extension runs, turns the
        // normal round and puts the end of the lane on the land.
        var points = shore.Points;
        var startSeaward = Seaward(shore, points[0], points[1]);
        var endSeaward = Seaward(shore, points[^2], points[^1]);

        // Out from the coast at the marina, taken from the stretch of coast nearest to it so the middle of the lane
        // sits off the right piece of shore however the coast bends.
        var nearest = NearestSegment(points, center);
        var middleSeaward = Seaward(shore, points[nearest], points[nearest + 1]);

        return new LaneShape(ends.Start, startSeaward, center, middleSeaward, ends.End, endSeaward);
    }

    /// <summary>A straight run past the marina, for a marina with no coast behind it.</summary>
    private static LaneShape AcrossOpenWater(Vector2 center, float reach, float clearance, int seed)
    {
        var heading = (float)new Random(seed).NextDouble() * MathF.Tau;
        var direction = new Vector2(MathF.Cos(heading), MathF.Sin(heading));
        var seaward = new Vector2(-direction.Y, direction.X);

        // Long enough to be worth crossing even when the reach is shorter than the clearance, which a design saved
        // by an older version may well be.
        var run = MathF.Max(reach, clearance * 3f + 200f);
        return new LaneShape(center - direction * run, seaward, center, seaward, center + direction * run, seaward);
    }

    /// <summary>
    /// The way out to sea from a stretch of coast: at right angles to it, on the side the land is not. Always given
    /// the two points in the order the line was drawn, since that is what decides which side is which.
    /// </summary>
    private static Vector2 Seaward(Shoreline shore, Vector2 from, Vector2 to)
    {
        var step = to - from;
        if (step.LengthSquared() < 1e-8f) return Vector2.UnitY;

        var direction = Vector2.Normalize(step);
        return shore.LandOnLeft ? new Vector2(direction.Y, -direction.X) : new Vector2(-direction.Y, direction.X);
    }

    /// <summary>Which stretch of the coast a point lies nearest to, as the index of the point that starts it.</summary>
    private static int NearestSegment(IReadOnlyList<Vector2> points, Vector2 point)
    {
        var best = 0;
        var closest = float.MaxValue;
        for (var i = 0; i < points.Count - 1; i++)
        {
            var distance = DistanceToSegment(point, points[i], points[i + 1]);
            if (distance >= closest) continue;
            closest = distance;
            best = i;
        }

        return best;
    }

    /// <summary>
    /// A smooth curve from one end to the other, passing exactly through the middle point.
    /// </summary>
    /// <remarks>
    /// A quadratic Bezier reaches the middle of its control net at the halfway mark, so the control point is placed
    /// where it drags the curve onto the point asked for. That keeps the clearance setting honest: the nearest the
    /// lane comes to the marina is the middle point, which is where it was put.
    /// </remarks>
    private static List<Vector2> Curve(Vector2 start, Vector2 middle, Vector2 end)
    {
        var control = middle * 2f - (start + end) * 0.5f;
        var points = new List<Vector2>(CurvePoints + 1);
        for (var i = 0; i <= CurvePoints; i++)
        {
            var t = i / (float)CurvePoints;
            var inverse = 1f - t;
            points.Add(start * (inverse * inverse) + control * (2f * inverse * t) + end * (t * t));
        }

        return points;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        if (lengthSquared < 1e-8f) return Vector2.Distance(point, a);

        var t = Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, a + ab * t);
    }
}

/// <summary>One lane of passing traffic: a smooth curve sweeping past the marina from one map edge to the other.</summary>
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
