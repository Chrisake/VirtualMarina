using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Design;

/// <summary>The designer settings a row of berths is laid out by, taken as they stand when the row is planned.</summary>
internal readonly record struct BerthRowSettings(
    float Width,
    float Length,
    float Depth,
    BerthSeparator Separators,
    float Gap,
    bool Align,
    BerthNamingScheme Naming);

/// <summary>
/// Lays out rows of berths along a pier — where each one goes, what it is called and which separators it needs — and
/// works out which separators an erased berth leaves with nothing to separate.
/// </summary>
/// <remarks>
/// The preview under the pointer is planned again whenever the overlay is rebuilt, so it keeps what it looked up about
/// each side of each pier until the layout changes, and neither names its berths nor plans their separators, which only
/// a real row needs.
/// </remarks>
internal sealed class BerthPlanner
{
    /// <summary>Gap left between berths separated by nothing when <see cref="BerthRowSettings.Gap"/> is smaller, in meters.</summary>
    public const float MinimumSeparatorGap = 0.3f;

    /// <summary>How far a divider may sit from a berth's edge and still count as its separator, in meters.</summary>
    private const float SeparatorTolerance = 1f;

    /// <summary>How much two stretches must overlap along a pier before they count as the same place, in meters.</summary>
    private const float OverlapTolerance = 0.05f;

    /// <summary>Most berths one row can add.</summary>
    private const int MaxRow = 500;

    private readonly MarinaVisualizer _marina;
    private readonly Dictionary<(string PierId, PierSide Side), Occupancy> _previewCache = [];

    public BerthPlanner(MarinaVisualizer marina) => _marina = marina;

    /// <summary>Forgets what the preview cached, once the layout has changed.</summary>
    public void Invalidate() => _previewCache.Clear();

    /// <summary>
    /// The berths (and dividers) a row between two distances along a pier would add: one every width + gap meters, lined
    /// up with the existing berths on that side unless the settings say otherwise, skipping the places already taken.
    /// </summary>
    /// <param name="pier">The pier.</param>
    /// <param name="side">Its side.</param>
    /// <param name="fromAlong">Where the row begins, from the pier's start.</param>
    /// <param name="toAlong">Where it ends.</param>
    /// <param name="settings">How the berths are sized, spaced, separated and named.</param>
    /// <param name="preview">
    /// True for the preview under the pointer: uses what was cached about the pier, and leaves the berths unnamed and the
    /// separators out.
    /// </param>
    public (IReadOnlyList<Berth> Berths, IReadOnlyList<Divider> Dividers) Plan(Pier pier, PierSide side, float fromAlong, float toAlong, BerthRowSettings settings, bool preview)
    {
        var width = settings.Width;
        var gap = SeparatorGap(settings);
        var pitch = width + gap;
        var occupied = preview ? PreviewOccupancy(pier, side) : Occupancy.Of(_marina, pier, side);

        var lo = MathF.Min(fromAlong, toAlong);
        var hi = MathF.Max(fromAlong, toAlong);
        float first;
        float last;
        if (settings.Align)
        {
            // Line up with the nearest existing berth edge on this side, or with the pier's start.
            var origin = occupied.NearestSlotEdge(fromAlong, gap);
            first = origin + MathF.Floor((lo - origin) / pitch + 1e-4f) * pitch;
            last = MathF.Max(origin + MathF.Ceiling((hi - origin) / pitch - 1e-4f) * pitch, first + pitch);
        }
        else
        {
            // The row starts exactly where the user clicked, at any offset from the pier's start.
            first = lo;
            last = MathF.Max(hi, first + pitch);
        }

        var names = preview ? null : new BerthNames(settings.Naming, _marina.GetBerths().Select(s => s.Id));
        var berths = new List<Berth>();
        var offsets = new List<float>();
        for (var offset = first; offset < last - 1e-3f && berths.Count < MaxRow; offset += pitch)
        {
            var center = offset + width * 0.5f;
            if (center < -1e-3f || center > pier.Length + 1e-3f) continue;
            if (occupied.Overlaps(offset, offset + width)) continue;

            var id = names?.Next(number => settings.Naming.Format(pier, side, number)) ?? "~preview";
            berths.Add(BerthGenerator.AtPier(pier, id, side, offset, width, settings.Length) with
            {
                MaxDraft = settings.Depth,
                HasFingerPiers = settings.Separators == BerthSeparator.FingerPiers,
            });
            offsets.Add(offset);
        }

        if (preview || DividerTypeOf(settings.Separators) is not { } type || berths.Count == 0) return (berths, Array.Empty<Divider>());
        return (berths, PlanDividers(pier, side, offsets, occupied, settings, type));
    }

    /// <summary>
    /// The dividers along <paramref name="doomedBerths"/> that no other berth uses: the separators of the berths about to
    /// go. A divider shared with a berth that stays is kept, so erasing one of two neighbours leaves the pier between them
    /// standing.
    /// </summary>
    /// <remarks>
    /// A divider belonging to a pier can only separate berths of that pier, so only those are looked at. A divider that
    /// belongs to no pier could in principle stand between berths of any, so for the few of those every berth is checked.
    /// </remarks>
    public List<Divider> OrphanedDividers(IReadOnlyList<Berth> doomedBerths)
    {
        var result = new List<Divider>();
        if (doomedBerths.Count == 0) return result;

        var doomedIds = new HashSet<string>(doomedBerths.Select(berth => berth.Id), StringComparer.OrdinalIgnoreCase);
        var piers = doomedBerths.Where(berth => berth.PierId is not null).Select(berth => berth.PierId!).Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var pierId in piers)
        {
            var dividers = _marina.GetDividersByPier(pierId);
            if (dividers.Count == 0) continue;
            var survivors = _marina.GetBerthsByPier(pierId).Where(berth => !doomedIds.Contains(berth.Id)).ToArray();
            foreach (var divider in dividers)
            {
                if (SeparatesAny(divider, doomedBerths) && !SeparatesAny(divider, survivors)) result.Add(divider);
            }
        }

        AddOrphansOfNoPier(doomedBerths, doomedIds, result);
        return result;
    }

    /// <summary>The part of <see cref="OrphanedDividers"/> for dividers that belong to no pier, checked against every berth.</summary>
    private void AddOrphansOfNoPier(IReadOnlyList<Berth> doomedBerths, HashSet<string> doomedIds, List<Divider> result)
    {
        Berth[]? everyoneElse = null;
        foreach (var divider in _marina.GetDividers())
        {
            if (divider.PierId is not null || !SeparatesAny(divider, doomedBerths)) continue;
            everyoneElse ??= _marina.GetBerths().Where(berth => !doomedIds.Contains(berth.Id)).ToArray();
            if (!SeparatesAny(divider, everyoneElse)) result.Add(divider);
        }
    }

    /// <summary>The space left between neighbouring berths: the gap asked for, but never less than a hand's width without a separator.</summary>
    private static float SeparatorGap(BerthRowSettings settings) =>
        settings.Separators == BerthSeparator.None ? MathF.Max(settings.Gap, MinimumSeparatorGap) : settings.Gap;

    /// <summary>The divider generated between the berths, or null when they have their own finger piers or nothing at all.</summary>
    private static DividerType? DividerTypeOf(BerthSeparator separator) => separator switch
    {
        BerthSeparator.FingerPier or BerthSeparator.PairedFingerPiers => DividerType.FingerPier,
        BerthSeparator.Piles => DividerType.Piles,
        BerthSeparator.Boom => DividerType.Boom,
        BerthSeparator.SinglePile => DividerType.SinglePile,
        _ => null,
    };

    private static bool SeparatesAny(Divider divider, IReadOnlyList<Berth> berths)
    {
        foreach (var berth in berths)
        {
            if (Separates(divider, berth)) return true;
        }

        return false;
    }

    /// <summary>True when the divider runs along one of the berth's long sides, as the generated separators do.</summary>
    internal static bool Separates(Divider divider, Berth berth)
    {
        if (berth.IsOnLand || (divider.PierId is { } pierId && !string.Equals(pierId, berth.PierId, StringComparison.OrdinalIgnoreCase))) return false;
        var offset = divider.Center - berth.Center;
        var across = MathF.Abs(Vector2.Dot(offset, berth.Right));
        var along = MathF.Abs(Vector2.Dot(offset, berth.Forward));
        return MathF.Abs(across - berth.Width * 0.5f) <= SeparatorTolerance && along <= berth.Length * 0.6f &&
            MathF.Abs(Vector2.Dot(divider.Direction, berth.Right)) < 0.35f;
    }

    private Occupancy PreviewOccupancy(Pier pier, PierSide side)
    {
        if (!_previewCache.TryGetValue((pier.Id, side), out var occupancy) || !ReferenceEquals(occupancy.Pier, pier))
        {
            occupancy = Occupancy.Of(_marina, pier, side);
            _previewCache[(pier.Id, side)] = occupancy;
        }

        return occupancy;
    }

    private List<Divider> PlanDividers(Pier pier, PierSide side, IReadOnlyList<float> offsets, Occupancy occupied, BerthRowSettings settings, DividerType type)
    {
        var dividerPrefix = BerthGenerator.DividerPrefix(pier, side);
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var number = 0;
        foreach (var existingDivider in _marina.GetDividers())
        {
            usedIds.Add(existingDivider.Id);
            number = Math.Max(number, DesignNaming.ParseNumber(existingDivider.Id, dividerPrefix));
        }

        // Where the pier already has a divider of this kind, no second one goes on top of it.
        var taken = new PointSet(0.1f);
        foreach (var existing in _marina.GetDividersByPier(pier.Id))
        {
            if (existing.Type == type) taken.Add(existing.Start);
        }

        var dividers = new List<Divider>();
        foreach (var edge in SeparatorEdges(offsets, occupied, settings.Width, settings.Separators))
        {
            var divider = BerthGenerator.DividerAtPier(pier, "-", side, edge, settings.Length, type);
            if (!taken.Add(divider.Start)) continue;
            string id;
            do id = $"{dividerPrefix}{++number:00}"; while (!usedIds.Add(id));
            dividers.Add(divider with { Id = id });
        }

        return dividers;
    }

    /// <summary>
    /// Distances along the pier where the new berths get a separator: both edges of every new berth, or — for
    /// <see cref="BerthSeparator.PairedFingerPiers"/> — every other boundary of the whole row, so the berths end up in pairs
    /// with one pier each and a pier at both ends of the row.
    /// </summary>
    private static List<float> SeparatorEdges(IReadOnlyList<float> newOffsets, Occupancy occupied, float width, BerthSeparator separators)
    {
        var mine = new List<float>(newOffsets.Count * 2);
        foreach (var offset in newOffsets)
        {
            mine.Add(offset);
            mine.Add(offset + width);
        }

        if (separators != BerthSeparator.PairedFingerPiers) return mine;

        // Pair up the whole row, not just the berths being added, so a row built in several goes keeps its rhythm.
        var row = newOffsets.Select(offset => (Min: offset, Max: offset + width)).Concat(occupied.Spans).OrderBy(span => span.Min).ToList();
        var edges = new List<float>();
        for (var i = 0; i < row.Count; i += 2) edges.Add(row[i].Min);
        if (row.Count > 0) edges.Add(row[^1].Max);

        // Only the boundaries of the berths being added are ours to build. Both lists are in order along the pier.
        mine.Sort();
        return edges.Where(edge => ContainsNear(mine, edge)).ToList();
    }

    /// <summary>True when the sorted list holds a value within the overlap tolerance of <paramref name="value"/>.</summary>
    private static bool ContainsNear(List<float> sorted, float value)
    {
        var index = sorted.BinarySearch(value - OverlapTolerance);
        if (index < 0) index = ~index;
        return index < sorted.Count && sorted[index] < value + OverlapTolerance;
    }

    /// <summary>
    /// The stretches of one side of a pier its berths already take up, in order along the pier, with the furthest reach of
    /// every prefix alongside so whether a new berth overlaps any of them is one binary search.
    /// </summary>
    private sealed class Occupancy
    {
        private readonly float[] _mins;
        private readonly float[] _reach;

        private Occupancy(Pier pier, List<(float Min, float Max)> spans)
        {
            Pier = pier;
            spans.Sort((a, b) => a.Min.CompareTo(b.Min));
            Spans = spans;
            _mins = new float[spans.Count];
            _reach = new float[spans.Count];
            var reach = float.NegativeInfinity;
            for (var i = 0; i < spans.Count; i++)
            {
                _mins[i] = spans[i].Min;
                reach = MathF.Max(reach, spans[i].Max);
                _reach[i] = reach;
            }
        }

        public Pier Pier { get; }

        public IReadOnlyList<(float Min, float Max)> Spans { get; }

        public static Occupancy Of(MarinaVisualizer marina, Pier pier, PierSide side)
        {
            var spans = new List<(float Min, float Max)>();
            foreach (var berth in marina.GetBerthsByPier(pier.Id))
            {
                if (PierGeometry.SideOf(pier, berth.Center) != side) continue;
                var along = Vector2.Dot(berth.Center - pier.Start, pier.Direction);
                var extent = MathF.Abs(Vector2.Dot(berth.Right, pier.Direction)) * berth.Width * 0.5f +
                    MathF.Abs(Vector2.Dot(berth.Forward, pier.Direction)) * berth.Length * 0.5f;

                // A sliver can never overlap a berth by more than the tolerance, so it is left out.
                if (extent * 2f > OverlapTolerance) spans.Add((along - extent, along + extent));
            }

            return new Occupancy(pier, spans);
        }

        /// <summary>
        /// True when [<paramref name="min"/>, <paramref name="max"/>] overlaps a stretch already taken by more than the
        /// tolerance. The stretches starting early enough to overlap are a prefix of the sorted list, and one of them
        /// overlaps exactly when the furthest of them reaches far enough in.
        /// </summary>
        public bool Overlaps(float min, float max)
        {
            var count = UpperBound(_mins, max - OverlapTolerance);
            return count > 0 && _reach[count - 1] > min + OverlapTolerance;
        }

        /// <summary>The existing slot edge — a berth's near edge, or one gap past its far edge — nearest <paramref name="along"/>; 0 without berths.</summary>
        public float NearestSlotEdge(float along, float gap)
        {
            var origin = 0f;
            var nearest = float.MaxValue;
            foreach (var (min, max) in Spans)
            {
                Consider(min);
                Consider(max + gap);
            }

            return origin;

            void Consider(float edge)
            {
                var distance = MathF.Abs(edge - along);
                if (distance >= nearest) return;
                nearest = distance;
                origin = edge;
            }
        }

        /// <summary>How many values of the sorted array are strictly below <paramref name="value"/>.</summary>
        private static int UpperBound(float[] sorted, float value)
        {
            var lo = 0;
            var hi = sorted.Length;
            while (lo < hi)
            {
                var mid = (lo + hi) >>> 1;
                if (sorted[mid] < value) lo = mid + 1;
                else hi = mid;
            }

            return lo;
        }
    }

    /// <summary>Points on a grid of cells, to ask quickly whether one is already near a given spot.</summary>
    private sealed class PointSet
    {
        private readonly float _radius;
        private readonly Dictionary<(int X, int Y), List<Vector2>> _cells = [];

        public PointSet(float radius) => _radius = radius;

        /// <summary>Adds the point unless one is already within the radius of it. Returns false when one was.</summary>
        public bool Add(Vector2 point)
        {
            var (cx, cy) = Cell(point);
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (!_cells.TryGetValue((cx + dx, cy + dy), out var near)) continue;
                    foreach (var other in near)
                    {
                        if (Vector2.DistanceSquared(other, point) < _radius * _radius) return false;
                    }
                }
            }

            if (!_cells.TryGetValue((cx, cy), out var cell))
            {
                cell = [];
                _cells[(cx, cy)] = cell;
            }

            cell.Add(point);
            return true;
        }

        private (int X, int Y) Cell(Vector2 point) => ((int)MathF.Floor(point.X / _radius), (int)MathF.Floor(point.Y / _radius));
    }
}
