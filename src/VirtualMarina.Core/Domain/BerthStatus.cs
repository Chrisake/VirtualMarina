using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Domain;

/// <summary>Occupancy state of a berth.</summary>
public enum BerthStatus
{
    /// <summary>Available. Color-coded green by default.</summary>
    Free = 0,

    /// <summary>A boat is moored in the berth. Color-coded red by default.</summary>
    Occupied = 1,

    /// <summary>Held for a boat that is arriving shortly. Color-coded blue by default.</summary>
    Reserved = 2,

    /// <summary>
    /// The berth holder's boat is away (e.g. cruising) and the berth can be let out for a while.
    /// The boat stays assigned and is drawn as a ghost, like Reserved. Color-coded yellow by default.
    /// </summary>
    TemporarilyFree = 3,
}

/// <summary>Set of statuses used to filter which berths are shown (<c>IMarinaVisualizer.SetStatusFilter</c>). Combine with <c>|</c>.</summary>
[Flags]
public enum BerthStatusFilter
{
    /// <summary>No status: every berth's status visuals and boats are hidden.</summary>
    None = 0,

    /// <summary>Free berths.</summary>
    Free = 1 << 0,

    /// <summary>Occupied berths.</summary>
    Occupied = 1 << 1,

    /// <summary>Reserved berths.</summary>
    Reserved = 1 << 2,

    /// <summary>Temporarily free berths.</summary>
    TemporarilyFree = 1 << 3,

    /// <summary>Every status (default).</summary>
    All = Free | Occupied | Reserved | TemporarilyFree,
}

/// <summary>Helpers for <see cref="BerthStatus"/> and <see cref="BerthStatusFilter"/>.</summary>
public static class BerthStatusExtensions
{
    /// <summary>The filter flag matching a status.</summary>
    public static BerthStatusFilter ToFilter(this BerthStatus status) => status switch
    {
        BerthStatus.Free => BerthStatusFilter.Free,
        BerthStatus.Occupied => BerthStatusFilter.Occupied,
        BerthStatus.Reserved => BerthStatusFilter.Reserved,
        BerthStatus.TemporarilyFree => BerthStatusFilter.TemporarilyFree,
        _ => BerthStatusFilter.None,
    };

    /// <summary>True when <paramref name="filter"/> shows berths with <paramref name="status"/>.</summary>
    public static bool Includes(this BerthStatusFilter filter, BerthStatus status) =>
        (filter & status.ToFilter()) != 0;

    /// <summary>A filter showing exactly the given statuses.</summary>
    public static BerthStatusFilter FromStatuses(IEnumerable<BerthStatus> statuses) =>
        statuses.Aggregate(BerthStatusFilter.None, (acc, s) => acc | s.ToFilter());

    /// <summary>Human-readable name, e.g. "Temporarily Free". Used by the default tooltip.</summary>
    public static string GetDisplayName(this BerthStatus status) => status switch
    {
        BerthStatus.Free => Strings.StatusFree,
        BerthStatus.Occupied => Strings.StatusOccupied,
        BerthStatus.Reserved => Strings.StatusReserved,
        BerthStatus.TemporarilyFree => Strings.StatusTemporarilyFree,
        _ => status.ToString(),
    };

    /// <summary>True for statuses that show the assigned boat as a translucent ghost (Reserved, TemporarilyFree).</summary>
    public static bool ShowsGhostBoat(this BerthStatus status) =>
        status is BerthStatus.Reserved or BerthStatus.TemporarilyFree;

    /// <summary>True for statuses that can carry a boat (everything except Free).</summary>
    public static bool CanHaveBoat(this BerthStatus status) => status != BerthStatus.Free;
}
