namespace VirtualMarina.Core.Domain;

/// <summary>Occupancy state of a slip.</summary>
public enum SlipStatus
{
    /// <summary>Available. Color-coded green by default.</summary>
    Free = 0,

    /// <summary>A boat is moored in the slip. Color-coded red by default.</summary>
    Occupied = 1,

    /// <summary>Held for a boat that is arriving shortly. Color-coded blue by default.</summary>
    Reserved = 2,

    /// <summary>
    /// The berth holder's boat is away (e.g. cruising) and the slip can be let out for a while.
    /// The boat stays assigned and is drawn as a ghost, like Reserved. Color-coded yellow by default.
    /// </summary>
    TemporarilyFree = 3,
}

/// <summary>Set of statuses used to filter which slips are shown (<c>IMarinaVisualizer.SetStatusFilter</c>). Combine with <c>|</c>.</summary>
[Flags]
public enum SlipStatusFilter
{
    /// <summary>No status: every slip's status visuals and boats are hidden.</summary>
    None = 0,

    /// <summary>Free slips.</summary>
    Free = 1 << 0,

    /// <summary>Occupied slips.</summary>
    Occupied = 1 << 1,

    /// <summary>Reserved slips.</summary>
    Reserved = 1 << 2,

    /// <summary>Temporarily free slips.</summary>
    TemporarilyFree = 1 << 3,

    /// <summary>Every status (default).</summary>
    All = Free | Occupied | Reserved | TemporarilyFree,
}

/// <summary>Helpers for <see cref="SlipStatus"/> and <see cref="SlipStatusFilter"/>.</summary>
public static class SlipStatusExtensions
{
    /// <summary>The filter flag matching a status.</summary>
    public static SlipStatusFilter ToFilter(this SlipStatus status) => status switch
    {
        SlipStatus.Free => SlipStatusFilter.Free,
        SlipStatus.Occupied => SlipStatusFilter.Occupied,
        SlipStatus.Reserved => SlipStatusFilter.Reserved,
        SlipStatus.TemporarilyFree => SlipStatusFilter.TemporarilyFree,
        _ => SlipStatusFilter.None,
    };

    /// <summary>True when <paramref name="filter"/> shows slips with <paramref name="status"/>.</summary>
    public static bool Includes(this SlipStatusFilter filter, SlipStatus status) =>
        (filter & status.ToFilter()) != 0;

    /// <summary>A filter showing exactly the given statuses.</summary>
    public static SlipStatusFilter FromStatuses(IEnumerable<SlipStatus> statuses) =>
        statuses.Aggregate(SlipStatusFilter.None, (acc, s) => acc | s.ToFilter());

    /// <summary>Human-readable name, e.g. "Temporarily Free". Used by the default tooltip.</summary>
    public static string GetDisplayName(this SlipStatus status) => status switch
    {
        SlipStatus.Free => "Free",
        SlipStatus.Occupied => "Occupied",
        SlipStatus.Reserved => "Reserved",
        SlipStatus.TemporarilyFree => "Temporarily Free",
        _ => status.ToString(),
    };

    /// <summary>True for statuses that show the assigned boat as a translucent ghost (Reserved, TemporarilyFree).</summary>
    public static bool ShowsGhostBoat(this SlipStatus status) =>
        status is SlipStatus.Reserved or SlipStatus.TemporarilyFree;

    /// <summary>True for statuses that can carry a boat (everything except Free).</summary>
    public static bool CanHaveBoat(this SlipStatus status) => status != SlipStatus.Free;
}
