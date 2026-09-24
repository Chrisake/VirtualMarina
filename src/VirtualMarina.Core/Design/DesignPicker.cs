using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Picking;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Design;

/// <summary>
/// Finds what is under the pointer for the designer: the point on the plane a drawing lies on, the land area, pier or
/// berth under a pixel, and the corner or edge a point snaps to.
/// </summary>
/// <remarks>
/// Every candidate is first ruled in or out by a cheap distance in plan meters — the snapping radius, turned from pixels
/// into meters where the pointer is — and only the few left are projected to the screen for the exact test, so a move of
/// the pointer costs little however large the marina is.
/// </remarks>
internal sealed class DesignPicker
{
    /// <summary>How close a pier's direction must be to a square one before it snaps, in degrees.</summary>
    private const float PierAngleSnapDegrees = 6f;

    /// <summary>How far from the pier's shore end a land edge still counts as the quay it springs from, in meters.</summary>
    private const float PierAngleReferenceRange = 40f;

    /// <summary>
    /// How much wider than the snapping radius the first, cheap cut is: a candidate standing on land above the plane seen
    /// at an angle can project a little further from where the pointer meets its own plane than the radius alone says.
    /// </summary>
    private const float CullMargin = 1.5f;

    private readonly MarinaVisualizer _marina;

    /// <summary>Bounds of each land outline, worked out once per outline (a changed land area is a new record).</summary>
    private readonly Dictionary<LandArea, (Vector2 Min, Vector2 Max)> _landBounds = new(ReferenceEqualityComparer.Instance);

    public DesignPicker(MarinaVisualizer marina) => _marina = marina;

    /// <summary>Forgets the cached bounds, once the layout has changed.</summary>
    public void Invalidate() => _landBounds.Clear();

    /// <summary>The ray from the camera through a view pixel.</summary>
    public Ray RayAt(float x, float y) => _marina.Camera.ScreenPointToRay(x, y, _marina.ViewportSize.X, _marina.ViewportSize.Y);

    /// <summary>Where the ray through a pixel meets the horizontal plane at <paramref name="height"/>, in plan coordinates.</summary>
    public Vector2? PlanPointAt(float x, float y, float height) => PlanPoint(RayAt(x, y), height);

    /// <summary>True when a plan point at the given height shows within <paramref name="pixels"/> of a screen point.</summary>
    public bool IsNearOnScreen(Vector2 plan, float height, Vector2 screen, float pixels) =>
        _marina.TryProjectToScreen(MarinaMath.ToWorld(plan, height), out var projected) && Vector2.Distance(projected, screen) <= pixels;

    /// <summary>The land area under a view pixel, optionally only of one kind (trees go on lawns only).</summary>
    public LandArea? LandUnder(float x, float y, LandKind? kind = null)
    {
        var ray = RayAt(x, y);
        LandArea? best = null;
        var bestDistance = float.MaxValue;
        foreach (var land in _marina.GetLandAreas())
        {
            if (kind is { } required && land.Kind != required) continue;
            if (HitsLand(ray, land, out var distance) && distance < bestDistance)
            {
                best = land;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>The berth, pier or land area under a view pixel, nearest the camera first; null over open water.</summary>
    public object? ElementUnder(float x, float y)
    {
        if (_marina.HitTest(x, y) is { } hit && _marina.GetBerth(hit.BerthId) is { } berth) return berth;

        var ray = RayAt(x, y);
        object? best = null;
        var bestDistance = float.MaxValue;
        foreach (var pier in _marina.GetPiers())
        {
            if (ray.IntersectHorizontalPlane(pier.DeckHeight, out var distance) && distance < bestDistance &&
                new OrientedRect(pier.Center, new Vector2(pier.Width + 0.6f, pier.Length), pier.HeadingDegrees).Contains(MarinaMath.ToPlan(ray.GetPoint(distance))))
            {
                best = pier;
                bestDistance = distance;
            }
        }

        foreach (var land in _marina.GetLandAreas())
        {
            if (HitsLand(ray, land, out var distance) && distance < bestDistance)
            {
                best = land;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>The pier and side a row of berths would go on for a point on the water, and how far along the pier.</summary>
    public (Pier Pier, PierSide Side, float Along)? BerthTarget(Vector2 point, float berthWidth, float berthLength)
    {
        (Pier Pier, PierSide Side, float Along)? best = null;
        var bestLateral = float.MaxValue;
        foreach (var pier in _marina.GetPiers())
        {
            var along = Vector2.Dot(point - pier.Start, pier.Direction);
            if (along < -berthWidth || along > pier.Length + berthWidth) continue;
            var lateral = PierGeometry.Lateral(pier, point);
            if (MathF.Abs(lateral) > pier.Width * 0.5f + berthLength + 2f || MathF.Abs(lateral) >= bestLateral) continue;
            bestLateral = MathF.Abs(lateral);
            best = (pier, PierGeometry.SideOf(pier, point), Math.Clamp(along, 0f, pier.Length));
        }

        return best;
    }

    /// <summary>
    /// Snaps to the nearest land corner, pier end, coast point, berth corner or point of the drawing within
    /// <paramref name="pixels"/>; failing those, to the nearest point of a land edge or the coast. Each is judged where it
    /// is actually drawn — a quay's corner at the quay's height — not where it would be on the water.
    /// </summary>
    /// <param name="x">Pointer X in view pixels.</param>
    /// <param name="y">Pointer Y in view pixels.</param>
    /// <param name="point">The pointer on the drawing's own plane, returned when nothing is near.</param>
    /// <param name="draft">The points of the drawing so far.</param>
    /// <param name="draftHeight">Height of the plane the drawing lies on.</param>
    /// <param name="pixels">The snapping radius on screen.</param>
    /// <param name="snapped">True when the point snapped.</param>
    public Vector2 Snap(float x, float y, Vector2 point, IReadOnlyList<Vector2> draft, float draftHeight, float pixels, out bool snapped)
    {
        var search = new SnapSearch(this, x, y, point, pixels);

        foreach (var land in _marina.GetLandAreas())
        {
            if (!search.MayReach(land.Height, Bounds(land))) continue;
            foreach (var corner in land.Points) search.Try(corner, land.Height);
        }

        foreach (var pier in _marina.GetPiers())
        {
            search.Try(pier.Start, pier.DeckHeight);
            search.Try(pier.End, pier.DeckHeight);
        }

        if (_marina.Shoreline is { } coast)
        {
            foreach (var corner in coast.Points) search.Try(corner, coast.Height);
        }

        foreach (var berth in _marina.GetBerths()) TryBerthCorners(ref search, berth);
        foreach (var drafted in draft) search.Try(drafted, draftHeight);

        if (!search.Found)
        {
            foreach (var land in _marina.GetLandAreas())
            {
                if (!search.MayReach(land.Height, Bounds(land))) continue;
                TryEdges(ref search, land.Points, land.Height, closed: true);
            }

            if (_marina.Shoreline is { } shore) TryEdges(ref search, shore.Points, shore.Height, closed: false);
        }

        snapped = search.Found;
        return search.Result;
    }

    /// <summary>
    /// The direction a pier should really take: square (a multiple of 90°) with the piers already in the marina and with
    /// the shore it springs from, when <paramref name="heading"/> is within <see cref="PierAngleSnapDegrees"/> of one. Null
    /// when nothing is close enough.
    /// </summary>
    /// <param name="heading">The direction the pointer is actually indicating.</param>
    /// <param name="start">The pier's shore end, which decides which land edges count as its quay.</param>
    public float? SquareWithSurroundings(float heading, Vector2 start)
    {
        float? best = null;
        var bestDelta = PierAngleSnapDegrees;

        void Consider(float reference)
        {
            // Along the reference, across it, and both the other ways round: the four square directions.
            for (var quarter = 0; quarter < 4; quarter++)
            {
                var candidate = MarinaMath.DeltaAngle(0f, reference + quarter * 90f);
                var delta = MathF.Abs(MarinaMath.DeltaAngle(candidate, heading));
                if (delta >= bestDelta) continue;
                bestDelta = delta;
                best = candidate;
            }
        }

        foreach (var pier in _marina.GetPiers()) Consider(pier.HeadingDegrees);

        foreach (var land in _marina.GetLandAreas())
        {
            // Only the edges near the shore end: the far side of a big quay says nothing about this pier.
            var (min, max) = Bounds(land);
            if (!Near(start, min, max, PierAngleReferenceRange)) continue;
            for (var i = 0; i < land.Points.Count; i++)
            {
                var from = land.Points[i];
                var to = land.Points[(i + 1) % land.Points.Count];
                if (Vector2.Distance(PolygonMath.ClosestPointOnSegment(start, from, to), start) > PierAngleReferenceRange) continue;
                Consider(MarinaMath.DirectionToHeading(to - from));
            }
        }

        return best;
    }

    private static Vector2? PlanPoint(Ray ray, float height) =>
        ray.IntersectHorizontalPlane(height, out var distance) ? MarinaMath.ToPlan(ray.GetPoint(distance)) : null;

    private bool HitsLand(Ray ray, LandArea land, out float distance)
    {
        if (!ray.IntersectHorizontalPlane(land.Height, out distance)) return false;
        var point = MarinaMath.ToPlan(ray.GetPoint(distance));
        var (min, max) = Bounds(land);
        return Near(point, min, max, 0f) && land.Contains(point);
    }

    private (Vector2 Min, Vector2 Max) Bounds(LandArea land)
    {
        if (!_landBounds.TryGetValue(land, out var bounds))
        {
            bounds = land.GetAxisAlignedBounds();
            _landBounds[land] = bounds;
        }

        return bounds;
    }

    private static bool Near(Vector2 point, Vector2 min, Vector2 max, float margin) =>
        point.X >= min.X - margin && point.X <= max.X + margin && point.Y >= min.Y - margin && point.Y <= max.Y + margin;

    private void TryBerthCorners(ref SnapSearch search, Berth berth)
    {
        var height = BerthPlacement.PadHeightFor(SceneBuilder.GroundHeight(berth, _marina.GetLandArea));
        var halfWidth = berth.Right * (berth.Width * 0.5f);
        var halfLength = berth.Forward * (berth.Length * 0.5f);
        if (!search.MayReach(height, berth.Center, berth.Width + berth.Length)) return;

        search.Try(berth.Center + halfWidth + halfLength, height);
        search.Try(berth.Center - halfWidth + halfLength, height);
        search.Try(berth.Center - halfWidth - halfLength, height);
        search.Try(berth.Center + halfWidth - halfLength, height);
    }

    private static void TryEdges(ref SnapSearch search, IReadOnlyList<Vector2> points, float height, bool closed)
    {
        if (search.PlaneCenter(height) is not { } center) return;
        var count = closed ? points.Count : points.Count - 1;
        for (var i = 0; i < count; i++)
        {
            search.Try(PolygonMath.ClosestPointOnSegment(center, points[i], points[(i + 1) % points.Count]), height);
        }
    }

    /// <summary>The best snap found so far, and where the pointer meets each height candidates stand at.</summary>
    private struct SnapSearch
    {
        private readonly DesignPicker _picker;
        private readonly Ray _ray;
        private readonly float _x;
        private readonly float _y;
        private readonly Vector2 _screen;
        private readonly float _pixels;
        private readonly List<(float Height, Vector2? Center, float Radius)> _planes;
        private float _best;

        public SnapSearch(DesignPicker picker, float x, float y, Vector2 point, float pixels)
        {
            _picker = picker;
            _ray = picker.RayAt(x, y);
            _x = x;
            _y = y;
            _screen = new Vector2(x, y);
            _pixels = pixels;
            _planes = new List<(float, Vector2?, float)>(4);
            _best = pixels;
            Result = point;
            Found = false;
        }

        public Vector2 Result { get; private set; }

        public bool Found { get; private set; }

        /// <summary>Where the pointer meets the plane at this height, or null when it does not.</summary>
        public Vector2? PlaneCenter(float height) => Plane(height).Center;

        /// <summary>False when nothing within <paramref name="reach"/> of <paramref name="center"/> can be near the pointer.</summary>
        public bool MayReach(float height, Vector2 center, float reach)
        {
            var (planeCenter, radius) = Plane(height);
            return planeCenter is not { } c || Vector2.Distance(center, c) <= radius + reach;
        }

        /// <summary>False when nothing inside these bounds can be near the pointer.</summary>
        public bool MayReach(float height, (Vector2 Min, Vector2 Max) bounds)
        {
            var (planeCenter, radius) = Plane(height);
            return planeCenter is not { } c || Near(c, bounds.Min, bounds.Max, radius);
        }

        public void Try(Vector2 candidate, float height)
        {
            var (center, radius) = Plane(height);
            if (center is { } c && Vector2.DistanceSquared(candidate, c) > radius * radius) return;
            if (!_picker._marina.TryProjectToScreen(MarinaMath.ToWorld(candidate, height), out var projected)) return;

            var distance = Vector2.Distance(projected, _screen);
            if (distance >= _best) return;
            _best = distance;
            Result = candidate;
            Found = true;
        }

        /// <summary>
        /// Where the pointer meets the plane at <paramref name="height"/> and how many meters the snapping radius covers
        /// there. With no meeting point (the plane is above the camera or seen edge on) nothing is ruled out cheaply.
        /// </summary>
        private (Vector2? Center, float Radius) Plane(float height)
        {
            foreach (var plane in _planes)
            {
                if (plane.Height == height) return (plane.Center, plane.Radius);
            }

            var center = PlanPoint(_ray, height);
            var radius = float.PositiveInfinity;
            if (center is { } c &&
                _picker.PlanPointAt(_x + _pixels, _y, height) is { } across &&
                _picker.PlanPointAt(_x, _y + _pixels, height) is { } down)
            {
                radius = MathF.Max(Vector2.Distance(c, across), Vector2.Distance(c, down)) * CullMargin + 0.01f;
            }

            _planes.Add((height, center, radius));
            return (center, radius);
        }
    }
}
