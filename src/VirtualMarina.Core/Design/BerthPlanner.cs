using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Design;

/// <summary>The designer settings a row of berths is laid out by, taken as they stand when the row is planned.</summary>
internal readonly record struct BerthRowSettings(
    float Width,
    float Length,
    float Depth,
    float Gap,
    bool Align,
    BerthNamingScheme Naming);

/// <summary>
/// Lays out rows of berths along a pier — where each one goes and what it is called — and works out which separators an
/// erased berth leaves with nothing to separate. A row has no separators of its own: they are placed afterwards with
/// <see cref="DesignTool.PlaceDividers"/> (see <see cref="DividerPlanner"/>).
/// </summary>
/// <remarks>
/// The preview under the pointer is planned again whenever the overlay is rebuilt, so it keeps what it looked up about
/// each side of each pier until the layout changes, and does not name its berths, which only a real row needs.
/// </remarks>
internal sealed class BerthPlanner
{
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
    /// The berths a row between two distances along a pier would add: one every width + gap meters, lined up with the
    /// existing berths on that side unless the settings say otherwise, skipping the places already taken. They have no
    /// finger piers of their own, and no dividers come with them.
    /// </summary>
    /// <param name="pier">The pier.</param>
    /// <param name="side">Its side.</param>
    /// <param name="fromAlong">Where the row begins, from the pier's start.</param>
    /// <param name="toAlong">Where it ends.</param>
    /// <param name="settings">How the berths are sized, spaced and named.</param>
    /// <param name="preview">True for the preview under the pointer: uses what was cached about the pier, and leaves the berths unnamed.</param>
    public IReadOnlyList<Berth> Plan(Pier pier, PierSide side, float fromAlong, float toAlong, BerthRowSettings settings, bool preview)
    {
        var width = settings.Width;
        var gap = settings.Gap;
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
        for (var offset = first; offset < last - 1e-3f && berths.Count < MaxRow; offset += pitch)
        {
            var center = offset + width * 0.5f;
            if (center < -1e-3f || center > pier.Length + 1e-3f) continue;
            if (occupied.Overlaps(offset, offset + width)) continue;

            var id = names?.Next(number => settings.Naming.Format(pier, side, number)) ?? "~preview";
            berths.Add(BerthGenerator.AtPier(pier, id, side, offset, width, settings.Length) with
            {
                MaxDraft = settings.Depth,
                HasFingerPiers = false,
            });
        }

        return berths;
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
}
