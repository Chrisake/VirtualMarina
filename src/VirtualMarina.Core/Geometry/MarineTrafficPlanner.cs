using System.Numerics;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Geometry;

/// <summary>
/// Works out the one path the passing traffic follows, and says where each vessel on it is at a given moment.
/// </summary>
/// <remarks>
/// <para>
/// The path follows the coast rather than cutting across the map at some angle of its own: it is the shoreline pushed
/// out to sea, so each end runs alongside one of the shoreline's endless segments and the middle curves between them.
/// A marina with no shoreline behind it has nothing to be parallel to, and gets a straight path instead.
/// </para>
/// <para>
/// How far out it is pushed is set by <see cref="MarineTraffic.Clearance"/>, which is the path's closest approach to
/// the middle of the marina. That is what makes the setting mean something on its own: 300 meters puts the shipping
/// 300 meters off, whatever size the marina is.
/// </para>
/// <para>
/// Planning is done once, when the traffic or the layout changes; <see cref="Place"/> is then called on every frame
/// and only walks the vessels along a path that is already known.
/// </para>
/// </remarks>
public static class MarineTrafficPlanner
{
    /// <summary>
    /// How much of each end of the path a vessel spends fading in or out. Short on purpose: the ends are far out in
    /// the flat sea, and a long fade there is a smear on the horizon rather than something arriving.
    /// </summary>
    private const float FadeFraction = 0.02f;

    /// <summary>Rounds of corner cutting on the offset coast, so the path bends where the coast turns.</summary>
    private const int SmoothingPasses = 2;

    /// <summary>How many times the path may be pushed further out to get clear of land it would otherwise cross.</summary>
    private const int ClearanceAttempts = 8;

    /// <summary>
    /// Works out the path and the vessels on it. The result is fixed for a given traffic setting and layout, so it is
    /// planned once and then only walked forward in time.
    /// </summary>
    /// <param name="traffic">The traffic settings. Returns null when it is off or unsound.</param>
    /// <param name="marina">Plan-view bounds of the marina; the clearance is measured from the middle of it.</param>
    /// <param name="land">Land areas the path must not cross.</param>
    /// <param name="shoreline">The mainland behind the shore, whose shape the path takes. Null for a straight path.</param>
    public static TrafficPath? Plan(
        MarineTraffic traffic,
        (Vector2 Min, Vector2 Max) marina,
        IEnumerable<LandArea> land,
        Shoreline? shoreline)
    {
        ArgumentNullException.ThrowIfNull(traffic);
        ArgumentNullException.ThrowIfNull(land);

        var wanted = traffic.VesselCount;
        if (wanted == 0 || traffic.Validate().Any()) return null;

        var outlines = land.Where(area => area?.Points is { Count: >= 3 }).Select(area => area.Points).ToArray();
        var center = (marina.Min + marina.Max) * 0.5f;
        var usable = shoreline is { } shore && shore.Points.Count >= 2 && !shore.Validate().Any() ? shore : null;

        var points = usable is not null
            ? AlongTheCoast(usable, outlines, center, traffic.Clearance, traffic.Reach)
            : AcrossOpenWater(outlines, center, traffic.Clearance, traffic.Reach, traffic.Seed);

        var path = new TrafficPath(points);
        if (path.Length < 1f) return null;

        var random = new Random(traffic.Seed);
        var mix = traffic.EffectiveVessels;
        for (var i = 0; i < wanted; i++)
        {
            // Spread along the path rather than dropped at random, so they read as shipping rather than a shoal.
            var phase = (i + 0.5f + ((float)random.NextDouble() - 0.5f) * 0.6f) / wanted;
            path.Vessels.Add(new PathVessel(
                mix[random.Next(mix.Count)],
                phase,
                0.8f + (float)random.NextDouble() * 0.4f,
                random.Next(2) == 0));
        }

        return path;
    }

    /// <summary>
    /// Where every vessel is at a moment in time. Cheap enough to call on every frame: it is a walk along a line that
    /// was worked out once.
    /// </summary>
    /// <param name="path">The path from <see cref="Plan"/>. Null yields nothing.</param>
    /// <param name="traffic">The traffic settings the path was planned with.</param>
    /// <param name="seconds">Seconds since the visualizer started.</param>
    public static IEnumerable<TrafficVessel> Place(TrafficPath? path, MarineTraffic traffic, double seconds)
    {
        ArgumentNullException.ThrowIfNull(traffic);
        if (path is null || path.Length < 1f) yield break;

        foreach (var vessel in path.Vessels)
        {
            var speed = traffic.SpeedMetersPerSecond * vessel.SpeedFactor;
            var travelled = (float)(seconds * speed) / path.Length;

            // Round and round: as one vessel fades out at the far end another fades in behind it.
            var along = Fraction(vessel.Phase + (vessel.Reversed ? -travelled : travelled));
            var (position, direction) = path.At(along);
            if (vessel.Reversed) direction = -direction;

            yield return new TrafficVessel(vessel.Type, position, MarinaMath.DirectionToHeading(direction), FadeAt(along));
        }
    }

    /// <summary>
    /// The shoreline pushed out to sea until its closest approach to <paramref name="center"/> is
    /// <paramref name="clearance"/>, with both ends carried on along the shoreline's endless segments.
    /// </summary>
    private static List<Vector2> AlongTheCoast(
        Shoreline shore,
        IReadOnlyList<IReadOnlyList<Vector2>> outlines,
        Vector2 center,
        float clearance,
        float reach)
    {
        // Once the path is past the marina, pushing it further out only ever moves it further away, so the right
        // amount can be halved in to rather than solved.
        var near = MathF.Max(1f, shore.DistanceToShore(center));
        for (var relax = 0; relax < ClearanceAttempts && Approach(near) > clearance; relax++) near *= 0.5f;

        var far = near + clearance * 2f + 200f;
        while (Approach(far) < clearance && far < reach) far *= 1.6f;

        for (var step = 0; step < 24 && far - near > 0.5f; step++)
        {
            var middle = (near + far) * 0.5f;
            if (Approach(middle) < clearance) near = middle;
            else far = middle;
        }

        // Nothing may sail over a quay, whatever the clearance asks for, so a path that would cross one is pushed
        // out until it does not. This is the one case where the closest approach ends up further than the setting.
        var offset = far;
        if (IsClear(Offset(shore, offset, reach), outlines)) return Offset(shore, offset, reach);

        // Half as far again each time, so a breakwater reaching a long way out is cleared in a few steps.
        var blocked = offset;
        for (var attempt = 0; attempt < ClearanceAttempts && !IsClear(Offset(shore, offset, reach), outlines); attempt++)
        {
            blocked = offset;
            offset = MathF.Max(offset * 1.5f, offset + 100f);
        }

        // Then back towards the marina again, so the traffic ends up as near as the land allows rather than wherever
        // the last step happened to land. Without this the slider jumps: 150 m is honoured, 200 m overshoots to 364.
        for (var step = 0; step < 10 && offset - blocked > 5f; step++)
        {
            var middle = (blocked + offset) * 0.5f;
            if (IsClear(Offset(shore, middle, reach), outlines)) offset = middle;
            else blocked = middle;
        }

        return Offset(shore, offset, reach);

        float Approach(float distance) => ClosestApproach(Offset(shore, distance, reach), center);
    }

    /// <summary>A straight path passing the marina at the asked-for distance, for a marina with no coast behind it.</summary>
    private static List<Vector2> AcrossOpenWater(
        IReadOnlyList<IReadOnlyList<Vector2>> outlines,
        Vector2 center,
        float clearance,
        float reach,
        int seed)
    {
        var random = new Random(seed);
        var best = new List<Vector2>();

        // The direction is arbitrary, so simply try a few until one of them misses the land.
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var heading = (float)random.NextDouble() * MathF.Tau;
            var direction = new Vector2(MathF.Cos(heading), MathF.Sin(heading));
            var sideways = new Vector2(-direction.Y, direction.X);
            var middle = center + sideways * MathF.Max(clearance, 1f);

            var path = new List<Vector2> { middle - direction * reach, middle + direction * reach };
            if (best.Count == 0) best = path;
            if (IsClear(path, outlines)) return path;
        }

        return best;
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

        var smoothed = Smooth(moved);

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

    /// <summary>True when no part of the path crosses a land area.</summary>
    private static bool IsClear(IReadOnlyList<Vector2> path, IReadOnlyList<IReadOnlyList<Vector2>> outlines)
    {
        if (outlines.Count == 0) return true;

        for (var i = 0; i < path.Count - 1; i++)
        {
            // Long end segments need sampling; the offset coast itself is already finely divided.
            var steps = Math.Clamp((int)(Vector2.Distance(path[i], path[i + 1]) / 25f), 1, 64);
            for (var step = 0; step < steps; step++)
            {
                var point = Vector2.Lerp(path[i], path[i + 1], step / (float)steps);
                foreach (var outline in outlines)
                {
                    if (PolygonMath.Contains(outline, point)) return false;
                }
            }
        }

        return true;
    }

    /// <summary>How near a path comes to a point, in meters.</summary>
    private static float ClosestApproach(IReadOnlyList<Vector2> path, Vector2 point)
    {
        var best = float.MaxValue;
        for (var i = 0; i < path.Count - 1; i++)
        {
            best = MathF.Min(best, DistanceToSegment(point, path[i], path[i + 1]));
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

    /// <summary>Full strength along the path, fading to nothing at both ends so nothing pops in or out.</summary>
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

/// <summary>The one line the passing traffic follows, and the vessels running along it.</summary>
public sealed class TrafficPath
{
    /// <summary>How far along the path each point is, so a position can be found without walking from the start.</summary>
    private readonly float[] _reached;

    /// <summary>Creates a path through a line of points in plan coordinates.</summary>
    /// <param name="points">At least two points, in order.</param>
    /// <exception cref="ArgumentException">Fewer than two points were given.</exception>
    public TrafficPath(IEnumerable<Vector2> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        Points = points.ToArray();
        if (Points.Count < 2) throw new ArgumentException("A traffic path needs at least two points.", nameof(points));

        _reached = new float[Points.Count];
        for (var i = 1; i < Points.Count; i++)
        {
            _reached[i] = _reached[i - 1] + Vector2.Distance(Points[i - 1], Points[i]);
        }

        Length = _reached[^1];
    }

    /// <summary>The points the path runs through, in order, in plan coordinates.</summary>
    public IReadOnlyList<Vector2> Points { get; }

    /// <summary>How long the path is, in meters.</summary>
    public float Length { get; }

    /// <summary>How many vessels run along it.</summary>
    public int VesselCount => Vessels.Count;

    /// <summary>The vessels running along this path.</summary>
    internal List<PathVessel> Vessels { get; } = new();

    /// <summary>How near the path comes to a point, in meters.</summary>
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

    /// <summary>Where the path is a fraction of the way along it, and which way it is heading there.</summary>
    /// <param name="along">0 at the start of the path, 1 at the end. Values outside are clamped.</param>
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

/// <summary>A vessel's place on the path: what it is, where it started and how fast it goes.</summary>
/// <param name="Type">The kind of vessel.</param>
/// <param name="Phase">Where along the path it was at time zero, 0–1.</param>
/// <param name="SpeedFactor">Multiplies the traffic's speed, so they do not move in lockstep.</param>
/// <param name="Reversed">True when it runs the other way along the path.</param>
internal readonly record struct PathVessel(BoatType Type, float Phase, float SpeedFactor, bool Reversed);
