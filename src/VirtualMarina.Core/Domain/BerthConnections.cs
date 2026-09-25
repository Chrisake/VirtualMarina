using System.Numerics;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Domain;

/// <summary>
/// Works out which berths are connected (<see cref="Berth.ConnectedBerthIds"/>): side by side, facing the same way, with open
/// water between them, so one boat can lie across both.
/// </summary>
/// <remarks>
/// Two berths are connected when all of this holds:
/// <list type="bullet">
/// <item><description>They are in the same place: both along the same pier, or both ashore on the same land area.</description></item>
/// <item><description>They face the same way, within <see cref="MaxHeadingDifference"/>.</description></item>
/// <item><description>Their long sides face each other: level with each other along their length, and no more than
/// <see cref="MaxGap"/> of water between them.</description></item>
/// <item><description>Neither has finger piers of its own (<see cref="Berth.HasFingerPiers"/>), which stand along both long sides.</description></item>
/// <item><description>No <see cref="Divider"/> of any type stands along the boundary between them: a divider is a fixed
/// obstacle, so no boat can lie across it.</description></item>
/// </list>
/// </remarks>
internal static class BerthConnections
{
    /// <summary>Most water between the long sides of two berths that still counts as one open space, in meters.</summary>
    public const float MaxGap = 1.5f;

    /// <summary>How far apart two berths' headings may be and still count as facing the same way, in degrees.</summary>
    public const float MaxHeadingDifference = 10f;

    /// <summary>How far two berths may overlap across their width and still count as side by side, in meters.</summary>
    private const float MaxOverlap = 0.5f;

    /// <summary>How far a divider may stand outside the boundary between two berths and still stand in it, in meters.</summary>
    private const float DividerTolerance = 0.75f;

    /// <summary>
    /// The connections of every berth that has any, keyed by berth id (case-insensitive). Each list follows the order of
    /// <paramref name="berths"/>, and a berth with no connection has no entry.
    /// </summary>
    public static Dictionary<string, string[]> Compute(IEnumerable<Berth> berths, IEnumerable<Divider> dividers)
    {
        var (dividersByPier, looseDividers) = GroupByPier(dividers);
        var found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var places = berths
            .Where(berth => berth is not null && (berth.PierId ?? berth.LandAreaId) is not null)
            .GroupBy(berth => (Land: berth.IsOnLand, Place: berth.IsOnLand ? berth.LandAreaId! : berth.PierId!), PlaceComparer.Instance);
        foreach (var place in places)
        {
            // Dividers belong to a pier; one that belongs to none may stand anywhere.
            var obstacles = !place.Key.Land && dividersByPier.TryGetValue(place.Key.Place, out var own) ? [.. own, .. looseDividers] : looseDividers;
            Connect(place.ToArray(), obstacles, found);
        }

        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, list) in found) result[id] = [.. list];
        return result;
    }

    /// <summary>Records every connected pair among the berths of one place, both ways round.</summary>
    private static void Connect(Berth[] group, List<Divider> obstacles, Dictionary<string, List<string>> found)
    {
        for (var i = 0; i < group.Length; i++)
        {
            for (var j = i + 1; j < group.Length; j++)
            {
                if (!AreConnected(group[i], group[j], obstacles)) continue;
                Add(group[i].Id, group[j].Id);
                Add(group[j].Id, group[i].Id);
            }
        }

        void Add(string id, string mate)
        {
            if (!found.TryGetValue(id, out var list)) found[id] = list = new List<string>(2);
            list.Add(mate);
        }
    }

    /// <summary>The dividers by the pier they belong to, and those that belong to none.</summary>
    private static (Dictionary<string, List<Divider>> ByPier, List<Divider> Loose) GroupByPier(IEnumerable<Divider> dividers)
    {
        var byPier = new Dictionary<string, List<Divider>>(StringComparer.OrdinalIgnoreCase);
        var loose = new List<Divider>();
        foreach (var divider in dividers)
        {
            if (divider is null) continue;
            if (divider.PierId is not { } pierId)
            {
                loose.Add(divider);
                continue;
            }

            if (!byPier.TryGetValue(pierId, out var list)) byPier[pierId] = list = [];
            list.Add(divider);
        }

        return (byPier, loose);
    }

    /// <summary>True when one boat could lie across <paramref name="a"/> and <paramref name="b"/> (see the class remarks).</summary>
    public static bool AreConnected(Berth a, Berth b, IReadOnlyList<Divider> dividers)
    {
        if (a.HasFingerPiers || b.HasFingerPiers) return false;
        if (MathF.Abs(MarinaMath.DeltaAngle(a.HeadingDegrees, b.HeadingDegrees)) > MaxHeadingDifference) return false;

        var offset = b.Center - a.Center;
        var across = Vector2.Dot(offset, a.Right);
        var along = MathF.Abs(Vector2.Dot(offset, a.Forward));
        var gap = MathF.Abs(across) - (a.Width + b.Width) * 0.5f;
        if (gap > MaxGap || gap < -MaxOverlap) return false;

        // Level with each other: most of the shorter berth lies beside the other one.
        if (along > MathF.Min(a.Length, b.Length) * 0.5f) return false;

        // The boundary runs along a's long side toward b, from a's edge to b's edge.
        var toward = across >= 0f ? a.Right : -a.Right;
        var nearEdge = a.Width * 0.5f;
        var farEdge = nearEdge + MathF.Max(gap, 0f);
        foreach (var divider in dividers)
        {
            if (StandsBetween(divider, a, toward, nearEdge, farEdge)) return false;
        }

        return true;
    }

    /// <summary>
    /// True when the divider runs along <paramref name="a"/>'s long side facing <paramref name="toward"/>, somewhere between
    /// <paramref name="nearEdge"/> and <paramref name="farEdge"/> from a's middle, and beside a rather than beyond its ends.
    /// </summary>
    private static bool StandsBetween(Divider divider, Berth a, Vector2 toward, float nearEdge, float farEdge)
    {
        // Across the berth it would be the pier end or the far end, not a boundary between neighbours.
        if (MathF.Abs(Vector2.Dot(divider.Direction, toward)) > 0.35f) return false;

        var tolerance = DividerTolerance + divider.Width * 0.5f;
        var across = Vector2.Dot(divider.Center - a.Center, toward);
        if (across < nearEdge - tolerance || across > farEdge + tolerance) return false;

        // Where the divider runs along the berth's length; it blocks when it reaches alongside the berth at all.
        var start = Vector2.Dot(divider.Start - a.Center, a.Forward);
        var end = Vector2.Dot(divider.End - a.Center, a.Forward);
        var half = a.Length * 0.5f;
        return MathF.Max(start, end) > -half && MathF.Min(start, end) < half;
    }

    /// <summary>Compares places by kind, then by id ignoring case, as ids are compared everywhere else.</summary>
    private sealed class PlaceComparer : IEqualityComparer<(bool Land, string Place)>
    {
        public static readonly PlaceComparer Instance = new();

        public bool Equals((bool Land, string Place) x, (bool Land, string Place) y) =>
            x.Land == y.Land && StringComparer.OrdinalIgnoreCase.Equals(x.Place, y.Place);

        public int GetHashCode((bool Land, string Place) obj) => HashCode.Combine(obj.Land, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Place));
    }
}
