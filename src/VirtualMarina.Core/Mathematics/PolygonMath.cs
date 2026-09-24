using System.Numerics;

namespace VirtualMarina.Core.Mathematics;

/// <summary>Plan-view polygon helpers (point lists in plan coordinates, X = world X, Y = world Z).</summary>
public static class PolygonMath
{
    /// <summary>How near two parts of an outline may come before they count as touching, in meters (a millimeter).</summary>
    public const float TouchTolerance = 1e-3f;

    /// <summary>
    /// How near two consecutive points of a drawn or loaded outline may be before they count as the same point, in meters
    /// (a centimeter). See <see cref="RemoveRepeatedPoints"/>.
    /// </summary>
    public const float PointTolerance = 0.01f;

    /// <summary>
    /// Signed area (shoelace formula on plan X and Y). Positive when the points run counter-clockwise in the X/Y plane
    /// (−X to +X, then toward +Y); negative for the opposite direction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Plan Y is world Z, which points south, so counter-clockwise here is clockwise seen from above in the
    /// right-handed, Y-up world.
    /// </para>
    /// <para>
    /// Worked out relative to the first point and summed in double precision, so an outline far from the origin (a
    /// layout in projected map coordinates, say) keeps its area instead of drowning it in the rounding of the
    /// products of large coordinates.
    /// </para>
    /// </remarks>
    public static float SignedArea(IReadOnlyList<Vector2> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 3) return 0f;

        var origin = points[0];
        var sum = 0d;
        for (var i = 1; i + 1 < points.Count; i++)
        {
            var a = points[i] - origin;
            var b = points[i + 1] - origin;
            sum += (double)a.X * b.Y - (double)b.X * a.Y;
        }

        return (float)(sum * 0.5d);
    }

    /// <summary>True when the point lies inside the polygon (even-odd rule). Points exactly on an edge may go either way.</summary>
    public static bool Contains(IReadOnlyList<Vector2> points, Vector2 point)
    {
        ArgumentNullException.ThrowIfNull(points);
        var inside = false;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            var a = points[i];
            var b = points[j];
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>Shortest distance from the point to the polygon's outline.</summary>
    public static float DistanceToBoundary(IReadOnlyList<Vector2> points, Vector2 point)
    {
        ArgumentNullException.ThrowIfNull(points);
        var best = float.MaxValue;
        for (var i = 0; i < points.Count; i++)
        {
            best = MathF.Min(best, DistanceToSegment(point, points[i], points[(i + 1) % points.Count]));
        }

        return best;
    }

    /// <summary>Axis-aligned bounds of the points (zero box when empty).</summary>
    public static (Vector2 Min, Vector2 Max) GetBounds(IReadOnlyList<Vector2> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0) return (Vector2.Zero, Vector2.Zero);
        var min = points[0];
        var max = points[0];
        foreach (var p in points)
        {
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }

        return (min, max);
    }

    /// <summary>
    /// True when the outline is a simple polygon: no edge has zero length, no two edges cross or touch except where
    /// neighbours share their corner, and no edge doubles back along the one before it. A point repeated anywhere
    /// in the outline, a corner resting on another edge (a T-junction) and edges overlapping along a line all fail.
    /// </summary>
    /// <remarks>Points within a millimeter of each other, or of an edge, count as touching.</remarks>
    public static bool IsSimple(IReadOnlyList<Vector2> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var n = points.Count;
        if (n < 3) return false;
        for (var i = 0; i < n; i++)
        {
            var a1 = points[i];
            var a2 = points[(i + 1) % n];
            if (Vector2.DistanceSquared(a1, a2) < 1e-8f) return false;

            // The neighbour sharing a2 must leave it without running back over this edge.
            var a3 = points[(i + 2) % n];
            if (DistanceToSegment(a3, a1, a2) < TouchTolerance || DistanceToSegment(a1, a2, a3) < TouchTolerance) return false;

            for (var j = i + 1; j < n; j++)
            {
                // Adjacent edges share a vertex, and were checked above.
                if (j == i + 1 || (i == 0 && j == n - 1)) continue;
                if (SegmentsTouch(a1, a2, points[j], points[(j + 1) % n])) return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Splits a simple polygon (convex or concave, either winding) into triangles by ear clipping.
    /// Returns index triples into <paramref name="points"/>, each wound counter-clockwise in the X/Y plane.
    /// </summary>
    /// <remarks>
    /// The outline is walked as a linked ring and only the vertices that could lie inside an ear — reflex or flat
    /// corners, and the doubled vertices a hole's bridge leaves — are tested against each candidate, so a typical
    /// outline takes O(n²) at worst. Input that is not a simple polygon can leave a ring with no ear at all; then the
    /// flattest remaining corner is dropped, which loses the least area, and clipping carries on.
    /// </remarks>
    public static IReadOnlyList<(int A, int B, int C)> Triangulate(IReadOnlyList<Vector2> points) => Triangulate(points, out _);

    /// <summary>
    /// <see cref="Triangulate(IReadOnlyList{Vector2})"/>, also saying how many corners had to be dropped because no ear
    /// could be found — zero for a simple polygon. Every dropped corner is a sliver of the outline left uncovered.
    /// </summary>
    /// <param name="points">The outline, in either winding.</param>
    /// <param name="droppedCorners">How many corners were given up on.</param>
    internal static IReadOnlyList<(int A, int B, int C)> Triangulate(IReadOnlyList<Vector2> points, out int droppedCorners)
    {
        ArgumentNullException.ThrowIfNull(points);
        droppedCorners = 0;
        var n = points.Count;
        var result = new List<(int, int, int)>(Math.Max(0, n - 2));
        if (n < 3) return result;

        var ring = new EarRing(points, counterClockwise: SignedArea(points) >= 0f);
        var current = ring.First;
        var sinceClip = 0;
        while (ring.Count > 3)
        {
            if (ring.IsEar(current))
            {
                var before = ring.Previous(current);
                result.Add((before, current, ring.Next(current)));
                ring.Remove(current);
                current = ring.Next(before);
                sinceClip = 0;
                continue;
            }

            current = ring.Next(current);
            if (++sinceClip < ring.Count) continue;

            // Once round the ring without an ear: the input is not a simple polygon. Give up the corner that covers
            // the least, rather than loop for ever or drop an arbitrary one.
            var flattest = ring.Flattest(current);
            current = ring.Next(flattest);
            ring.Remove(flattest);
            droppedCorners++;
            sinceClip = 0;
        }

        result.Add((ring.Previous(current), current, ring.Next(current)));
        return result;
    }

    /// <summary>
    /// Splits a polygon with holes into triangles: letters with counters (O, A, 8), a quay with a pond in it.
    /// Returns the points it actually triangulated and index triples into them.
    /// </summary>
    /// <param name="outer">The outline, in either winding.</param>
    /// <param name="holes">Outlines of the holes inside it, in either winding. Empty is the same as no holes.</param>
    /// <remarks>
    /// Each hole is joined to the outline by a bridge — a pair of coincident edges running out to it and back —
    /// which turns the whole thing into one simple polygon that ordinary ear clipping can handle. The bridge is the
    /// shortest one that crosses nothing — neither the outline, nor a bridge already made, nor any other hole — found
    /// by trying the outline's vertices nearest the hole first, which is slow in principle and instant on the few
    /// dozen points a glyph or a quay actually has.
    /// </remarks>
    public static (IReadOnlyList<Vector2> Points, IReadOnlyList<(int A, int B, int C)> Triangles) TriangulateWithHoles(
        IReadOnlyList<Vector2> outer,
        IReadOnlyList<IReadOnlyList<Vector2>> holes)
    {
        ArgumentNullException.ThrowIfNull(outer);
        ArgumentNullException.ThrowIfNull(holes);
        if (outer.Count < 3) return (Array.Empty<Vector2>(), Array.Empty<(int, int, int)>());

        // The outline anticlockwise and every hole clockwise, so a hole walked in its own order runs against the
        // outline and leaves the ring simple once it is spliced in.
        var merged = new List<Vector2>(outer);
        if (SignedArea(merged) < 0f) merged.Reverse();

        // Rightmost first, the usual order; the bridges themselves are checked against every hole still waiting.
        var pending = holes
            .Where(hole => hole is { Count: >= 3 })
            .Select(hole =>
            {
                var ring = new List<Vector2>(hole);
                if (SignedArea(ring) > 0f) ring.Reverse();
                return ring;
            })
            .OrderByDescending(ring => ring.Max(point => point.X))
            .ToList();

        while (pending.Count > 0)
        {
            // A bridge must not cut through a hole still waiting its turn, or that hole's outline ends up crossing
            // the bridge once it is merged. A hole that cannot yet see the outline past the others waits until one
            // of them has been merged, when its vertices become places to bridge to as well.
            var chosen = -1;
            var from = 0;
            var to = -1;
            for (var h = 0; h < pending.Count && to < 0; h++)
            {
                from = RightmostVertex(pending[h]);
                to = NearestVisible(merged, pending, h, from, avoidPending: true);
                chosen = h;
            }

            if (to < 0)
            {
                // Nothing clean anywhere (touching or overlapping holes): fall back to ignoring the other holes.
                chosen = 0;
                from = RightmostVertex(pending[0]);
                to = NearestVisible(merged, pending, 0, from, avoidPending: false);
            }

            var hole = pending[chosen];
            pending.RemoveAt(chosen);
            if (to < 0) continue;   // nothing can see it; leave the hole unfilled rather than tearing the outline

            var spliced = new List<Vector2>(merged.Count + hole.Count + 2);
            spliced.AddRange(merged.Take(to + 1));
            for (var i = 0; i <= hole.Count; i++) spliced.Add(hole[(from + i) % hole.Count]);
            spliced.AddRange(merged.Skip(to));
            merged = spliced;
        }

        return (merged, Triangulate(merged));
    }

    /// <summary>The index of the vertex furthest along +X, where a hole is bridged from.</summary>
    private static int RightmostVertex(List<Vector2> hole)
    {
        var from = 0;
        for (var i = 1; i < hole.Count; i++)
        {
            if (hole[i].X > hole[from].X) from = i;
        }

        return from;
    }

    /// <summary>
    /// The vertex of <paramref name="ring"/> nearest the bridging point of hole <paramref name="current"/> that can be
    /// joined to it without the bridge crossing the ring, the hole itself or, when <paramref name="avoidPending"/> is
    /// set, any other hole not yet merged. -1 when there is none.
    /// </summary>
    private static int NearestVisible(List<Vector2> ring, List<List<Vector2>> holes, int current, int from, bool avoidPending)
    {
        var target = holes[current][from];

        // Nearest first, so the first vertex that can see the hole is the shortest bridge. Sorted by hand rather than
        // through LINQ: this runs once per hole, and a glyph has several.
        var order = new int[ring.Count];
        var distances = new float[ring.Count];
        for (var i = 0; i < ring.Count; i++)
        {
            order[i] = i;
            distances[i] = Vector2.DistanceSquared(ring[i], target);
        }

        Array.Sort(distances, order);
        foreach (var candidate in order)
        {
            var start = ring[candidate];

            // The bridge has to leave into the inside of the ring. That also picks the right copy of a vertex an
            // earlier bridge has already doubled: splicing into the other one folds the ring over itself.
            var before = ring[(candidate + ring.Count - 1) % ring.Count];
            var after = ring[(candidate + 1) % ring.Count];
            if (!InSweep(after - start, before - start, target - start)) continue;
            if (Crosses(ring, start, target) || Crosses(holes[current], start, target)) continue;
            if (avoidPending && BlockedByAnother(holes, current, start, target)) continue;
            return candidate;
        }

        return -1;
    }

    /// <summary>True when a hole other than <paramref name="current"/> is in the way of a bridge.</summary>
    private static bool BlockedByAnother(List<List<Vector2>> holes, int current, Vector2 a, Vector2 b)
    {
        for (var h = 0; h < holes.Count; h++)
        {
            if (h != current && Blocks(holes[h], a, b)) return true;
        }

        return false;
    }

    /// <summary>True when a bridge would cross another hole's outline, or pass through its interior.</summary>
    private static bool Blocks(List<Vector2> hole, Vector2 a, Vector2 b)
    {
        if (Crosses(hole, a, b) || Contains(hole, (a + b) * 0.5f)) return true;
        foreach (var point in hole)
        {
            if (DistanceToSegment(point, a, b) < TouchTolerance) return true;
        }

        return false;
    }

    /// <summary>True when a segment properly crosses any edge of a ring, ignoring edges it merely touches.</summary>
    private static bool Crosses(List<Vector2> ring, Vector2 a, Vector2 b)
    {
        for (var i = 0; i < ring.Count; i++)
        {
            if (SegmentsIntersect(a, b, ring[i], ring[(i + 1) % ring.Count])) return true;
        }

        return false;
    }

    /// <summary>True when <paramref name="d"/> lies strictly inside the sweep from <paramref name="from"/>
    /// counter-clockwise to <paramref name="to"/>, which may be more than 180°.</summary>
    private static bool InSweep(Vector2 from, Vector2 to, Vector2 d) =>
        Cross(from, to) >= 0f
            ? Cross(from, d) > 0f && Cross(d, to) > 0f
            : !(Cross(to, d) >= 0f && Cross(d, from) >= 0f);

    private static float Cross(Vector2 u, Vector2 v) => u.X * v.Y - u.Y * v.X;

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b) => Vector2.Distance(p, ClosestPointOnSegment(p, a, b));

    /// <summary>The point of the segment <paramref name="a"/>–<paramref name="b"/> nearest <paramref name="point"/>.</summary>
    /// <param name="point">The point to measure from.</param>
    /// <param name="a">One end of the segment.</param>
    /// <param name="b">The other end. A segment with no length is the point <paramref name="a"/>.</param>
    public static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        return lengthSquared < 1e-12f ? a : a + ab * Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
    }

    /// <summary>
    /// The points with every point that repeats the one before it (within <paramref name="tolerance"/>) left out, as a
    /// double-click or a hand-edited file leaves them. For a closed outline, trailing points that come back onto the
    /// first are dropped too, since the outline closes by itself.
    /// </summary>
    /// <param name="points">The points, in order.</param>
    /// <param name="closed">True for an outline whose last point joins the first; false for an open line.</param>
    /// <param name="tolerance">How near two points must be to count as one, in meters.</param>
    /// <returns>A new list; the input is left alone.</returns>
    public static IReadOnlyList<Vector2> RemoveRepeatedPoints(IReadOnlyList<Vector2> points, bool closed, float tolerance = PointTolerance)
    {
        ArgumentNullException.ThrowIfNull(points);
        var result = new List<Vector2>(points.Count);
        foreach (var point in points)
        {
            if (result.Count == 0 || Vector2.Distance(result[^1], point) > tolerance) result.Add(point);
        }

        while (closed && result.Count > 1 && Vector2.Distance(result[0], result[^1]) <= tolerance) result.RemoveAt(result.Count - 1);
        return result;
    }

    /// <summary>
    /// True when two segments cross or come within <see cref="TouchTolerance"/> of each other anywhere, including
    /// meeting end to end, one ending on the other, or overlapping along a line.
    /// </summary>
    private static bool SegmentsTouch(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2) =>
        SegmentsIntersect(p1, p2, q1, q2) ||
        DistanceToSegment(p1, q1, q2) < TouchTolerance || DistanceToSegment(p2, q1, q2) < TouchTolerance ||
        DistanceToSegment(q1, p1, p2) < TouchTolerance || DistanceToSegment(q2, p1, p2) < TouchTolerance;

    private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
        var d1 = Cross(p2 - p1, q1 - p1);
        var d2 = Cross(p2 - p1, q2 - p1);
        var d3 = Cross(q2 - q1, p1 - q1);
        var d4 = Cross(q2 - q1, p2 - q1);
        return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
    }

    /// <summary>
    /// The outline being clipped, as a ring of vertex indices linked both ways, counter-clockwise, with the set of
    /// vertices that can still block an ear kept alongside so an ear test only looks at those.
    /// </summary>
    private sealed class EarRing
    {
        private readonly IReadOnlyList<Vector2> _points;
        private readonly int[] _next;
        private readonly int[] _previous;
        private readonly bool[] _twin;

        /// <summary>Vertices that are reflex, flat or doubled: the only ones an ear can contain.</summary>
        private readonly List<int> _blockers = [];

        /// <summary>Where each vertex sits in <see cref="_blockers"/>, or -1 when it is not there.</summary>
        private readonly int[] _blockerSlot;

        public EarRing(IReadOnlyList<Vector2> points, bool counterClockwise)
        {
            _points = points;
            var n = points.Count;
            _next = new int[n];
            _previous = new int[n];
            _blockerSlot = new int[n];
            _twin = FindTwins(points);
            for (var i = 0; i < n; i++)
            {
                var after = (i + 1) % n;
                var before = (i + n - 1) % n;
                _next[i] = counterClockwise ? after : before;
                _previous[i] = counterClockwise ? before : after;
                _blockerSlot[i] = -1;
            }

            Count = n;
            First = counterClockwise ? 0 : n - 1;
            for (var i = 0; i < n; i++) UpdateBlocker(i);
        }

        public int Count { get; private set; }

        /// <summary>A vertex still in the ring, to start walking from.</summary>
        public int First { get; }

        public int Next(int vertex) => _next[vertex];

        public int Previous(int vertex) => _previous[vertex];

        public void Remove(int vertex)
        {
            var before = _previous[vertex];
            var after = _next[vertex];
            _next[before] = after;
            _previous[after] = before;
            SetBlocker(vertex, false);
            Count--;

            // Clipping a corner changes the angle at both of its neighbours.
            UpdateBlocker(before);
            UpdateBlocker(after);
        }

        /// <summary>True when the corner at <paramref name="vertex"/> can be cut off without covering anything outside the ring.</summary>
        public bool IsEar(int vertex)
        {
            var prev = _previous[vertex];
            var next = _next[vertex];
            var a = _points[prev];
            var b = _points[vertex];
            var c = _points[next];
            if (!IsConvex(a, b, c)) return false;

            foreach (var index in _blockers)
            {
                if (index == prev || index == vertex || index == next) continue;

                // Bridging a hole into an outline leaves vertices duplicated on purpose. A copy sitting on a corner of
                // the ear is not inside it, but the ring may still run from it into the ear, or open out around it
                // across the ear, and clipping then lays one triangle over another. Only that blocks the ear: counting
                // every copy as a blocker would stop every ear near a bridge being clipped and lose whole wedges.
                var point = _points[index];
                var before = _points[_previous[index]];
                var after = _points[_next[index]];
                if (Same(point, a))
                {
                    if (CopyOverlapsCorner(a, b, c, before, after)) return false;
                }
                else if (Same(point, b))
                {
                    if (CopyOverlapsCorner(b, c, a, before, after)) return false;
                }
                else if (Same(point, c))
                {
                    if (CopyOverlapsCorner(c, a, b, before, after)) return false;
                }
                else if (InTriangle(point, a, b, c))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The vertex whose corner encloses the least area: the one losing the least when it is dropped.</summary>
        /// <param name="start">Any vertex still in the ring.</param>
        public int Flattest(int start)
        {
            var best = start;
            var bestArea = float.MaxValue;
            var vertex = start;
            for (var i = 0; i < Count; i++)
            {
                var area = MathF.Abs(Cross(_points[vertex] - _points[_previous[vertex]], _points[_next[vertex]] - _points[vertex]));
                if (area < bestArea)
                {
                    bestArea = area;
                    best = vertex;
                }

                vertex = _next[vertex];
            }

            return best;
        }

        /// <summary>
        /// True when another copy of a triangle's corner, with the ring arriving from <paramref name="before"/> and
        /// leaving for <paramref name="after"/>, reaches into the triangle's angle at that corner. The triangle is
        /// <paramref name="corner"/>, <paramref name="u"/>, <paramref name="v"/>, counter-clockwise.
        /// </summary>
        private static bool CopyOverlapsCorner(Vector2 corner, Vector2 u, Vector2 v, Vector2 before, Vector2 after)
        {
            var toU = u - corner;
            var toV = v - corner;

            // One of its edges runs into the triangle.
            if (StrictlyBetween(toU, toV, before - corner) || StrictlyBetween(toU, toV, after - corner)) return true;

            // Or the ring's inside at the copy, which lies to the left of the way it runs, wraps round the whole angle.
            var middle = Vector2.Normalize(toU) + Vector2.Normalize(toV);
            return InSweep(after - corner, before - corner, middle);
        }

        private static bool Same(Vector2 a, Vector2 b) => Vector2.DistanceSquared(a, b) < 1e-12f;

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c) =>
            Cross(b - a, p - a) >= 0f && Cross(c - b, p - b) >= 0f && Cross(a - c, p - c) >= 0f;

        /// <summary>True when <paramref name="d"/> lies strictly inside the angle (under 180°) from <paramref name="from"/>
        /// counter-clockwise to <paramref name="to"/>.</summary>
        private static bool StrictlyBetween(Vector2 from, Vector2 to, Vector2 d) => Cross(from, d) > 0f && Cross(d, to) > 0f;

        private static bool IsConvex(Vector2 a, Vector2 b, Vector2 c) => Cross(b - a, c - b) > 1e-7f;

        private void UpdateBlocker(int vertex) =>
            SetBlocker(vertex, _twin[vertex] || !IsConvex(_points[_previous[vertex]], _points[vertex], _points[_next[vertex]]));

        private void SetBlocker(int vertex, bool blocks)
        {
            var slot = _blockerSlot[vertex];
            if (blocks == slot >= 0) return;

            if (blocks)
            {
                _blockerSlot[vertex] = _blockers.Count;
                _blockers.Add(vertex);
                return;
            }

            // Swap the last one into the gap, so taking one out costs nothing.
            var last = _blockers[^1];
            _blockers[slot] = last;
            _blockerSlot[last] = slot;
            _blockers.RemoveAt(_blockers.Count - 1);
            _blockerSlot[vertex] = -1;
        }

        /// <summary>Which vertices share their position with another one, as a hole's bridge leaves them.</summary>
        private static bool[] FindTwins(IReadOnlyList<Vector2> points)
        {
            var twin = new bool[points.Count];
            var seen = new Dictionary<Vector2, int>(points.Count);
            for (var i = 0; i < points.Count; i++)
            {
                if (seen.TryGetValue(points[i], out var first))
                {
                    twin[first] = true;
                    twin[i] = true;
                }
                else
                {
                    seen[points[i]] = i;
                }
            }

            return twin;
        }
    }
}
