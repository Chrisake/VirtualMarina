using System.Collections.ObjectModel;
using System.Numerics;
using VirtualMarina.Core.Mathematics;

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
/// <b>Which side.</b> <see cref="LandOnLeft"/> picks the half: true for the left of the line walking from the first
/// point to the last. A designer chooses it by clicking the side that should be land.
/// </para>
/// <para>
/// <b>The one rule.</b> The two endless segments must not cross each other, or "the land side" means nothing —
/// <see cref="Validate"/> reports that. Everything else, including a coast that doubles back on itself, is allowed.
/// </para>
/// <para>
/// It is drawn beneath the land areas placed by hand, so a quay traced along the shore sits on top of it and the two
/// read as one piece of ground.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // A straight coast running east-west, with the land to the north.
/// marina.SetShoreline(new Shoreline(new[] { new Vector2(-500, -40), new Vector2(500, -40) }, landOnLeft: false));
/// </code>
/// </example>
public sealed record Shoreline
{
    /// <summary>How far out the endless segments are taken when the shape is built, in meters — at the least.</summary>
    /// <remarks>
    /// Far enough to be past the horizon in any usable view, and near enough that the shape stays a handful of
    /// triangles. The land is not really infinite; it only has to outlast the camera. A line drawn wider than this
    /// pushes the far edge out beyond itself, so the land always encloses the coast that made it.
    /// </remarks>
    public const float Reach = 12_000f;

    /// <summary>Creates a shoreline from the points of its open line.</summary>
    /// <param name="points">At least two points, in order. The line does not close.</param>
    /// <param name="landOnLeft">True when the land lies to the left of the line walked from first point to last.</param>
    /// <exception cref="ArgumentException">Fewer than two points were given.</exception>
    public Shoreline(IEnumerable<Vector2> points, bool landOnLeft)
    {
        ArgumentNullException.ThrowIfNull(points);
        Points = points.ToArray();
        if (Points.Count < 2) throw new ArgumentException("A shoreline needs at least two points.", nameof(points));
        LandOnLeft = landOnLeft;
    }

    /// <summary>The points of the open line, in order.</summary>
    public IReadOnlyList<Vector2> Points { get; init; }

    /// <summary>True when the land is to the left of the line walked from the first point to the last.</summary>
    public bool LandOnLeft { get; init; }

    /// <summary>Height of the ground above the water, in meters. Default 1.4.</summary>
    public float Height { get; init; } = 1.4f;

    /// <summary>Surface of the ground. Default <see cref="LandKind.Grass"/>, the usual thing behind a marina.</summary>
    public LandKind Kind { get; init; } = LandKind.Grass;

    /// <summary>What is scattered across it. Default <see cref="HinterlandScenery.Countryside"/>.</summary>
    public HinterlandScenery Scenery { get; init; } = HinterlandScenery.Countryside;

    /// <summary>Keeps the scenery the same between sessions. Any number will do.</summary>
    public int ScenerySeed { get; init; } = 1;

    /// <summary>Read-only string attributes the host application attaches to the shoreline. Saved with the design.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>Problems that would stop the shoreline being drawn, empty when it is sound.</summary>
    /// <remarks>
    /// The endless segments crossing is the only one that matters: if they meet, the line no longer divides the plan
    /// into two sides and "the land side" has no meaning.
    /// </remarks>
    public IEnumerable<string> Validate()
    {
        if (Points.Count < 2)
        {
            yield return "A shoreline needs at least two points.";
            yield break;
        }

        for (var i = 0; i < Points.Count; i++)
        {
            if (!float.IsFinite(Points[i].X) || !float.IsFinite(Points[i].Y))
            {
                yield return $"Shoreline point {i} is not a finite position.";
                yield break;
            }
        }

        if (Vector2.DistanceSquared(Points[0], Points[^1]) < 1e-6f && Points.Count == 2)
        {
            yield return "A shoreline's two points must not be in the same place.";
            yield break;
        }

        if (EndlessSegmentsCross())
        {
            yield return "The shoreline's two endless segments cross each other, so neither side of it is the land.";
        }
    }

    /// <summary>
    /// The shoreline as a closed shape covering the land side, ready to be triangulated: the line itself, its two
    /// segments run out to <see cref="Reach"/>, and the way round the far edge that keeps the land inside.
    /// </summary>
    /// <remarks>
    /// The far edge is a square of side 2 × <see cref="Reach"/>, so the whole mainland costs the points of the line
    /// plus at most four corners. Returns an empty list when <see cref="Validate"/> would complain.
    /// </remarks>
    public IReadOnlyList<Vector2> BuildOutline()
    {
        if (Validate().Any()) return Array.Empty<Vector2>();

        // Run the end segments out until they meet the far square, so the ring closes along its edges.
        var edge = FarEdge;
        var start = ExtendToEdge(Points[1], Points[0], edge);
        var end = ExtendToEdge(Points[^2], Points[^1], edge);

        var shape = new List<Vector2>(Points.Count + 6) { start };
        shape.AddRange(Points);
        shape.Add(end);

        // Then back round the outside of the square, the way that keeps the land within the ring.
        shape.AddRange(CornersBetween(end, start, edge));
        return shape;
    }

    /// <summary>True when a point in plan coordinates lies on the land side of the shoreline.</summary>
    /// <param name="point">The point to test.</param>
    public bool Contains(Vector2 point)
    {
        var outline = BuildOutline();
        return outline.Count >= 3 && PolygonMath.Contains(outline, point);
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

    /// <summary>True when the two endless segments meet, which would leave the plan without two clear sides.</summary>
    private bool EndlessSegmentsCross()
    {
        var edge = FarEdge;
        var firstFrom = Points[0];
        var firstTo = ExtendToEdge(Points[1], Points[0], edge);
        var lastFrom = Points[^1];
        var lastTo = ExtendToEdge(Points[^2], Points[^1], edge);

        // Two points make one straight coast: the "two" segments are the two halves of the same line.
        return Points.Count > 2 && SegmentsCross(firstFrom, firstTo, lastFrom, lastTo);
    }

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
}
