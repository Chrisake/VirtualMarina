using System.Numerics;

namespace VirtualMarina.Core.Mathematics;

/// <summary>Plan-view polygon helpers (point lists in plan coordinates, X = world X, Y = world Z).</summary>
public static class PolygonMath
{
    /// <summary>
    /// Signed area (shoelace formula on plan X and Y). Positive when the points run counter-clockwise in the X/Y plane
    /// (−X to +X, then toward +Y); negative for the opposite direction.
    /// </summary>
    public static float SignedArea(IReadOnlyList<Vector2> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var sum = 0f;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }

        return sum * 0.5f;
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

    /// <summary>True when no two non-adjacent edges cross and no edge has zero length.</summary>
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
            for (var j = i + 1; j < n; j++)
            {
                // Adjacent edges share a vertex.
                if (j == i + 1 || (i == 0 && j == n - 1)) continue;
                if (SegmentsIntersect(a1, a2, points[j], points[(j + 1) % n])) return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Splits a simple polygon (convex or concave, either winding) into triangles by ear clipping.
    /// Returns index triples into <paramref name="points"/>, each wound counter-clockwise in the X/Y plane.
    /// </summary>
    public static IReadOnlyList<(int A, int B, int C)> Triangulate(IReadOnlyList<Vector2> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var result = new List<(int, int, int)>();
        if (points.Count < 3) return result;

        var remaining = Enumerable.Range(0, points.Count).ToList();
        if (SignedArea(points) < 0f) remaining.Reverse();

        var guard = 0;
        while (remaining.Count > 3 && guard++ < points.Count * points.Count)
        {
            var clipped = false;
            for (var i = 0; i < remaining.Count; i++)
            {
                var prev = remaining[(i + remaining.Count - 1) % remaining.Count];
                var curr = remaining[i];
                var next = remaining[(i + 1) % remaining.Count];
                if (!IsEar(points, remaining, prev, curr, next)) continue;

                result.Add((prev, curr, next));
                remaining.RemoveAt(i);
                clipped = true;
                break;
            }

            // Degenerate input (collinear or self-touching points): drop a vertex rather than loop forever.
            if (!clipped) remaining.RemoveAt(0);
        }

        if (remaining.Count == 3) result.Add((remaining[0], remaining[1], remaining[2]));
        return result;
    }

    private static bool IsEar(IReadOnlyList<Vector2> points, List<int> remaining, int prev, int curr, int next)
    {
        var a = points[prev];
        var b = points[curr];
        var c = points[next];
        if (Cross(b - a, c - b) <= 1e-7f) return false; // reflex or collinear

        foreach (var index in remaining)
        {
            if (index == prev || index == curr || index == next) continue;
            if (InTriangle(points[index], a, b, c)) return false;
        }

        return true;
    }

    private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c) =>
        Cross(b - a, p - a) >= 0f && Cross(c - b, p - b) >= 0f && Cross(a - c, p - c) >= 0f;

    private static float Cross(Vector2 u, Vector2 v) => u.X * v.Y - u.Y * v.X;

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        var t = lengthSquared < 1e-12f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / lengthSquared, 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }

    private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
        var d1 = Cross(p2 - p1, q1 - p1);
        var d2 = Cross(p2 - p1, q2 - p1);
        var d3 = Cross(q2 - q1, p1 - q1);
        var d4 = Cross(q2 - q1, p2 - q1);
        return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
    }
}
