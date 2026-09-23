using System.Numerics;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Domain;

/// <summary>What is drawn on the mainland behind the shore, so the marina does not sit in an empty sea.</summary>
public enum HinterlandScenery
{
    /// <summary>Bare ground. The cheapest, and right when a reference photo is showing through.</summary>
    None = 0,

    /// <summary>Scattered trees and hedges thinning out inland.</summary>
    Countryside = 1,

    /// <summary>Blocks of crops in different colours, as seen from the air.</summary>
    Fields = 2,

    /// <summary>Low blocks standing in rows, reading as a town along the coast.</summary>
    Town = 3,
}

/// <summary>
/// The mainland behind the marina: an open line of points whose first and last segments run on for ever, splitting
/// the plan in two, with one side of it being land. It is what stops a marina looking like an island in an empty sea.
/// </summary>
/// <remarks>
/// <para>
/// Two points are enough — that is simply a straight coast. More points bend it into bays and headlands. The line is
/// open, not a ring: the segment before the first point and the segment after the last one carry on outward without
/// end, so the land behind them never runs out however far the camera pulls back.
/// </para>
/// <para>
/// <b>Which side.</b> <see cref="LandOnLeft"/> picks the half. "Left" is meant in plan coordinates, with plan Y as
/// world Z: true puts the land on the side reached by turning the line's direction from +X toward +Z, so a line
/// running east (+X) has its land to the south (+Z). Seen from above, where north (−Z) is up and east to the right,
/// that is the right-hand side of someone walking the line from the first point to the last — the side
/// <see cref="Pier.Right"/> points to for a pier running the same way. A designer chooses it by clicking the side
/// that should be land.
/// </para>
/// <para>
/// <b>The rules.</b> The line, carried on without end at both ends, must split the plan cleanly in two, or "the land side"
/// means nothing: the drawn line must not cross or touch itself, the endless segments must not cross each other, and neither
/// may run back across the drawn line. <see cref="Validate"/> reports each of these. (Up to this version a line crossing
/// itself was allowed, and drew land in the wrong places; a design holding one now fails to load with that message, as a
/// land area crossing itself always has.)
/// </para>
/// <para>
/// It is drawn beneath the land areas placed by hand, so a quay traced along the shore sits on top of it and the two
/// read as one piece of ground.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // A straight coast running east (+X), with the land to the north (−Z): "left" is toward +Z, so this is false.
/// marina.SetShoreline(new Shoreline(new[] { new Vector2(-500, -40), new Vector2(500, -40) }, landOnLeft: false));
/// </code>
/// </example>
public sealed record Shoreline
{
    private readonly ValueList<Vector2> _points = ValueList<Vector2>.Empty;
    private readonly ValueDictionary _metadata = ValueDictionary.Empty;
    private readonly bool _landOnLeft;

    /// <summary>
    /// What <see cref="Validate"/> and <see cref="BuildOutline"/> worked out, kept so a caller testing thousands of points with
    /// <see cref="Contains"/> does not rebuild the shape for each. Replaced whenever the line or its side is set, which is all the
    /// shape depends on.
    /// </summary>
    private readonly ShapeCache _shape = new();

    /// <summary>How far out the endless segments are taken when the shape is built, in meters — at the least.</summary>
    /// <remarks>
    /// Far enough to be past the horizon in any usable view, and near enough that the shape stays a handful of
    /// triangles. The land is not really infinite; it only has to outlast the camera. A line drawn wider than this
    /// pushes the far edge out beyond itself, so the land always encloses the coast that made it.
    /// </remarks>
    public const float Reach = 12_000f;

    /// <summary>
    /// How close two neighbouring points may be before they count as the same place, in meters — the same
    /// centimeter the designer uses to drop a repeated click.
    /// </summary>
    private const float MinimumSpacing = 0.01f;

    /// <summary>How near two parts of the line may come before they count as touching, in meters: a millimeter.</summary>
    private const float TouchTolerance = 1e-3f;

    /// <summary>Creates a shoreline from the points of its open line.</summary>
    /// <param name="points">At least two points, in order. The line does not close.</param>
    /// <param name="landOnLeft">
    /// True when the land lies on the plan-coordinate left of the line walked from first point to last: toward +Z for
    /// a line running along +X, which seen from above is the walker's right. See <see cref="LandOnLeft"/>.
    /// </param>
    /// <exception cref="ArgumentException">Fewer than two points were given.</exception>
    public Shoreline(IEnumerable<Vector2> points, bool landOnLeft)
    {
        ArgumentNullException.ThrowIfNull(points);
        _points = ValueList<Vector2>.From(points);
        if (Points.Count < 2) throw new ArgumentException(Strings.ErrorShorelineTooFewPoints, nameof(points));
        _landOnLeft = landOnLeft;
    }

    /// <summary>The points of the open line, in order.</summary>
    /// <remarks>The record keeps its own copy, so changing the list it was given later changes nothing; null reads as empty.</remarks>
    public IReadOnlyList<Vector2> Points
    {
        get => _points;
        init
        {
            _points = ValueList<Vector2>.From(value);
            _shape = new ShapeCache();
        }
    }

    /// <summary>
    /// True when the land lies on the left of the line walked from the first point to the last, taking left in plan
    /// coordinates (X = world X, Y = world Z): the side reached by turning the direction of travel from +X toward +Z.
    /// </summary>
    /// <remarks>
    /// Plan coordinates look mirrored from above, because world +Z points south, toward the viewer's bottom edge. So
    /// seen from above this is the walker's <em>right</em>-hand side — the side <see cref="Pier.Right"/> points to
    /// for a pier running the same way. For a line running east (+X) the land is to the south (+Z) when this is true
    /// and to the north (−Z) when it is false. The name is kept as it is because saved designs depend on it.
    /// </remarks>
    public bool LandOnLeft
    {
        get => _landOnLeft;
        init
        {
            _landOnLeft = value;
            _shape = new ShapeCache();
        }
    }

    /// <summary>Height of the ground above the water, in meters. Default 1.4.</summary>
    public float Height { get; init; } = 1.4f;

    /// <summary>Surface of the ground. Default <see cref="LandKind.Grass"/>, the usual thing behind a marina.</summary>
    public LandKind Kind { get; init; } = LandKind.Grass;

    /// <summary>What is scattered across it. Default <see cref="HinterlandScenery.Countryside"/>.</summary>
    public HinterlandScenery Scenery { get; init; } = HinterlandScenery.Countryside;

    /// <summary>Keeps the scenery the same between sessions. Any number will do.</summary>
    public int ScenerySeed { get; init; } = 1;

    /// <summary>Read-only string attributes the host application attaches to the shoreline. Saved with the design.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get => _metadata; init => _metadata = ValueDictionary.From(value); }

    /// <summary>Problems that would stop the shoreline being drawn, empty when it is sound.</summary>
    /// <remarks>
    /// Two neighbouring points in the same place (within a centimeter) are refused, since a segment with no length has no
    /// direction to carry on in. Beyond that, the line and its endless segments must divide the plan into two clean sides: the
    /// drawn line must not cross or touch itself (nor turn straight back along itself), the endless segments must not cross each
    /// other, and neither may run back across the drawn line. The answer is worked out once per shoreline and kept.
    /// </remarks>
    public IEnumerable<string> Validate() => Shape.Problems;

    /// <summary>
    /// The shoreline as a closed shape covering the land side, ready to be triangulated: the line itself, its two
    /// segments run out to <see cref="Reach"/>, and the way round the far edge that keeps the land inside.
    /// </summary>
    /// <remarks>
    /// The far edge is a square of side 2 × <see cref="Reach"/>, so the whole mainland costs the points of the line
    /// plus at most four corners. Returns an empty list when <see cref="Validate"/> would complain. The list is built once per
    /// shoreline and shared between calls; it is read-only.
    /// </remarks>
    public IReadOnlyList<Vector2> BuildOutline() => Shape.Outline;

    /// <summary>
    /// Where the two endless segments reach the edge of the map: the far ends of the coast, as far out as the
    /// mainland is ever drawn. Returns null when <see cref="Validate"/> would complain.
    /// </summary>
    /// <remarks>
    /// This is where something crossing the whole map passes the coast for the last time, which is what the passing
    /// traffic aims its lanes at.
    /// </remarks>
    public (Vector2 Start, Vector2 End)? EndsAtTheMapEdge() => Shape.Ends;

    /// <summary>True when a point in plan coordinates lies on the land side of the shoreline.</summary>
    /// <param name="point">The point to test.</param>
    public bool Contains(Vector2 point)
    {
        var outline = Shape.Outline;
        return outline.Count >= 3 && PolygonMath.Contains(outline, point);
    }

    /// <summary>
    /// Unit plan-view vector at right angles to one drawn segment, pointing to the land side of it: the direction
    /// <see cref="LandOnLeft"/> picks, spelled out.
    /// </summary>
    /// <param name="segment">Index of the segment, from <see cref="Points"/>[segment] to <see cref="Points"/>[segment + 1].</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="segment"/> is not the index of a segment.</exception>
    /// <example>
    /// <code>
    /// // A line running east (+X) with LandOnLeft = true has its land to the south: (0, 1), toward +Z.
    /// var towardLand = shoreline.LandSideNormal(0);
    /// </code>
    /// </example>
    public Vector2 LandSideNormal(int segment)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(segment);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(segment, Points.Count - 1);
        var along = Points[segment + 1] - Points[segment];
        if (along.LengthSquared() < 1e-12f) return Vector2.Zero;

        // Turning +X toward +Z in plan coordinates is (x, y) -> (-y, x).
        var left = Vector2.Normalize(new Vector2(-along.Y, along.X));
        return LandOnLeft ? left : -left;
    }

    /// <summary>Distance from a point to the line itself (not to the far edge of the built shape), in meters.</summary>
    /// <param name="point">The point to measure from.</param>
    public float DistanceToShore(Vector2 point)
    {
        var best = float.MaxValue;
        for (var i = 0; i < Points.Count - 1; i++)
        {
            best = MathF.Min(best, DistanceToSegment(point, Points[i], Points[i + 1]));
        }

        return best;
    }

    /// <summary>
    /// Half the width of the far square: <see cref="Reach"/>, or enough to clear the drawn line when that reaches
    /// further, so the endless segments always run outward and never fold back inside.
    /// </summary>
    private float FarEdge
    {
        get
        {
            var furthest = 0f;
            foreach (var point in Points) furthest = MathF.Max(furthest, MathF.Max(MathF.Abs(point.X), MathF.Abs(point.Y)));
            return MathF.Max(Reach, furthest * 1.25f);
        }
    }

    /// <summary>The four corners of the far square, in the order the perimeter measure walks them.</summary>
    private static Vector2[] Corners(float edge) =>
        new Vector2[] { new(-edge, -edge), new(edge, -edge), new(edge, edge), new(-edge, edge) };

    /// <summary>
    /// Carries the line on beyond its first point until it meets the far square, in the direction of the first
    /// segment long enough to have one — so a point repeated at the start cannot leave the end pointing nowhere.
    /// </summary>
    private Vector2 ExtendStart(float edge)
    {
        var inner = 1;
        while (inner < Points.Count - 1 && Vector2.Distance(Points[inner], Points[0]) <= MinimumSpacing) inner++;
        return ExtendToEdge(Points[inner], Points[0], edge);
    }

    /// <summary>Carries the line on beyond its last point until it meets the far square; see <see cref="ExtendStart"/>.</summary>
    private Vector2 ExtendEnd(float edge)
    {
        var inner = Points.Count - 2;
        while (inner > 0 && Vector2.Distance(Points[inner], Points[^1]) <= MinimumSpacing) inner--;
        return ExtendToEdge(Points[inner], Points[^1], edge);
    }

    /// <summary>Carries one endless segment out until it meets the far square, away from the line.</summary>
    private static Vector2 ExtendToEdge(Vector2 from, Vector2 through, float edge)
    {
        var direction = through - from;
        if (direction.LengthSquared() < 1e-8f) return through;

        direction = Vector2.Normalize(direction);
        var toSideX = direction.X > 1e-6f ? (edge - through.X) / direction.X
            : direction.X < -1e-6f ? (-edge - through.X) / direction.X
            : float.MaxValue;
        var toSideY = direction.Y > 1e-6f ? (edge - through.Y) / direction.Y
            : direction.Y < -1e-6f ? (-edge - through.Y) / direction.Y
            : float.MaxValue;

        // The nearer of the two crossings is the edge the segment actually leaves by.
        return through + direction * MathF.Max(0f, MathF.Min(toSideX, toSideY));
    }

    /// <summary>
    /// The corners of the far square passed walking from one end of the line round to the other on the land side.
    /// </summary>
    /// <remarks>
    /// Each point on the square is given a measure in 0–4: 0–1 along the bottom edge, 1–2 up the right, 2–3 back
    /// along the top, 3–4 down the left. A corner is passed when it falls between the two ends in the direction of
    /// travel, which is anticlockwise when the land is on the left and clockwise when it is on the right.
    /// </remarks>
    private IEnumerable<Vector2> CornersBetween(Vector2 from, Vector2 to, float edge)
    {
        var corners = Corners(edge);
        var start = PerimeterMeasure(from, edge);
        var finish = PerimeterMeasure(to, edge);
        var sweep = LandOnLeft ? Wrap(finish - start) : Wrap(start - finish);

        // A corner is on the way round when it lies within the sweep, and it is emitted in the order reached.
        var passed = new List<(float At, Vector2 Corner)>(4);
        for (var i = 0; i < corners.Length; i++)
        {
            var reached = LandOnLeft ? Wrap(i - start) : Wrap(start - i);
            if (reached > 1e-3f && reached < sweep - 1e-3f) passed.Add((reached, corners[i]));
        }

        foreach (var (_, corner) in passed.OrderBy(entry => entry.At)) yield return corner;
    }

    /// <summary>Where a point on the far square lies along its edge, measured 0–4 from the bottom-left corner.</summary>
    private static float PerimeterMeasure(Vector2 point, float edge)
    {
        var side = edge * 2f;
        const float slack = 0.5f;   // half a meter, so a point just off the edge still lands on it

        if (point.Y <= -edge + slack) return Math.Clamp((point.X + edge) / side, 0f, 1f);
        if (point.X >= edge - slack) return 1f + Math.Clamp((point.Y + edge) / side, 0f, 1f);
        if (point.Y >= edge - slack) return 2f + Math.Clamp((edge - point.X) / side, 0f, 1f);
        return 3f + Math.Clamp((edge - point.Y) / side, 0f, 1f);
    }

    private static float Wrap(float value)
    {
        var wrapped = value % 4f;
        return wrapped < 0f ? wrapped + 4f : wrapped;
    }

    /// <summary>The problems, the outline and the far ends, worked out on first use and kept.</summary>
    private ShapeData Shape => _shape.Data ??= BuildShape();

    private ShapeData BuildShape()
    {
        var problems = FindProblems().ToArray();
        if (problems.Length > 0) return new ShapeData(problems, Array.Empty<Vector2>(), null);

        // Run the end segments out until they meet the far square, so the ring closes along its edges.
        var edge = FarEdge;
        var start = ExtendStart(edge);
        var end = ExtendEnd(edge);

        var shape = new List<Vector2>(Points.Count + 6) { start };
        shape.AddRange(Points);
        shape.Add(end);

        // Then back round the outside of the square, the way that keeps the land within the ring.
        shape.AddRange(CornersBetween(end, start, edge));
        return new ShapeData(problems, shape.AsReadOnly(), (start, end));
    }

    private IEnumerable<string> FindProblems()
    {
        if (Points.Count < 2)
        {
            yield return Strings.ErrorShorelineTooFewPoints;
            yield break;
        }

        for (var i = 0; i < Points.Count; i++)
        {
            if (!float.IsFinite(Points[i].X) || !float.IsFinite(Points[i].Y))
            {
                yield return Strings.Format(Strings.ErrorShorelineNonFinitePoint, i);
                yield break;
            }
        }

        for (var i = 1; i < Points.Count; i++)
        {
            if (Vector2.Distance(Points[i - 1], Points[i]) <= MinimumSpacing)
            {
                yield return Points.Count == 2
                    ? Strings.ErrorShorelineTwoPointsTogether
                    : Strings.Format(Strings.ErrorShorelinePointsTogether, i - 1, i);
                yield break;
            }
        }

        if (FindSelfCrossing() is { } crossing)
        {
            yield return Strings.Format(Strings.ErrorShorelineCrossesItself, crossing.First, crossing.Second);
            yield break;
        }

        // Two points make one straight coast: the "two" endless segments are the two halves of the same line.
        if (Points.Count <= 2) yield break;

        var edge = FarEdge;
        var startRay = (From: Points[0], To: ExtendStart(edge));
        var endRay = (From: Points[^1], To: ExtendEnd(edge));
        if (SegmentsCross(startRay.From, startRay.To, endRay.From, endRay.To))
        {
            yield return Strings.ErrorShorelineEndsCross;
            yield break;
        }

        if (RayMeetsLine(startRay.From, startRay.To, skipFirst: true))
        {
            yield return Strings.ErrorShorelineStartCrosses;
        }

        if (RayMeetsLine(endRay.From, endRay.To, skipFirst: false))
        {
            yield return Strings.ErrorShorelineEndCrosses;
        }
    }

    /// <summary>
    /// The first pair of drawn segments that cross or touch, or that turn straight back along each other, or null when the line
    /// is clean. Neighbouring segments share their corner, so for them only a turn back counts.
    /// </summary>
    private (int First, int Second)? FindSelfCrossing()
    {
        var segments = Points.Count - 1;
        for (var i = 0; i < segments; i++)
        {
            var a1 = Points[i];
            var a2 = Points[i + 1];
            if (i + 2 <= segments && TurnsBack(a1, a2, Points[i + 2])) return (i, i + 1);

            for (var j = i + 2; j < segments; j++)
            {
                if (SegmentsTouch(a1, a2, Points[j], Points[j + 1])) return (i, j);
            }
        }

        return null;
    }

    /// <summary>True when an endless segment, from the end of the line outward, meets any drawn segment but the one it continues.</summary>
    private bool RayMeetsLine(Vector2 from, Vector2 to, bool skipFirst)
    {
        var segments = Points.Count - 1;
        for (var i = 0; i < segments; i++)
        {
            // The segment the ray carries on from shares its end point, and runs the other way.
            if (skipFirst ? i == 0 : i == segments - 1) continue;
            var a = Points[i];
            var b = Points[i + 1];

            // The ray starts on the line's end point, which the segment next to that end also touches; only a real meeting counts.
            if (SegmentsCross(from, to, a, b)) return true;
            if (DistanceToSegment(a, from, to) < TouchTolerance && Vector2.Distance(a, from) > TouchTolerance) return true;
            if (DistanceToSegment(b, from, to) < TouchTolerance && Vector2.Distance(b, from) > TouchTolerance) return true;
        }

        return false;
    }

    /// <summary>True when the segment b→c runs back over a→b (the line folds onto itself at b).</summary>
    private static bool TurnsBack(Vector2 a, Vector2 b, Vector2 c)
    {
        var ab = b - a;
        var bc = c - b;
        var sine = Cross(ab, bc) / (ab.Length() * bc.Length());
        return MathF.Abs(sine) < 1e-4f && Vector2.Dot(ab, bc) < 0f;
    }

    private static bool SegmentsTouch(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2) =>
        SegmentsCross(p1, p2, q1, q2) ||
        DistanceToSegment(p1, q1, q2) < TouchTolerance || DistanceToSegment(p2, q1, q2) < TouchTolerance ||
        DistanceToSegment(q1, p1, p2) < TouchTolerance || DistanceToSegment(q2, p1, p2) < TouchTolerance;

    private static bool SegmentsCross(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
    {
        var d1 = Cross(b2 - b1, a1 - b1);
        var d2 = Cross(b2 - b1, a2 - b1);
        var d3 = Cross(a2 - a1, b1 - a1);
        var d4 = Cross(a2 - a1, b2 - a1);
        return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        if (lengthSquared < 1e-8f) return Vector2.Distance(point, a);
        var t = Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, a + ab * t);
    }

    /// <summary>What a shoreline's shape comes to: its problems, or the outline and the far ends of the coast.</summary>
    private sealed record ShapeData(string[] Problems, IReadOnlyList<Vector2> Outline, (Vector2 Start, Vector2 End)? Ends);

    /// <summary>
    /// Holds the <see cref="ShapeData"/> once worked out. It takes no part in equality, since it only remembers what the line
    /// already says; every instance is equal to every other.
    /// </summary>
    private sealed class ShapeCache
    {
        public ShapeData? Data { get; set; }

        public override bool Equals(object? obj) => obj is ShapeCache;

        public override int GetHashCode() => 0;
    }
}
