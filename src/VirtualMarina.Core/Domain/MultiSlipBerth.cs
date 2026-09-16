namespace VirtualMarina.Core.Domain;

/// <summary>How a boat that spans several slips lies in them.</summary>
public enum MooringStyle
{
    /// <summary>
    /// Side-to: the boat lies parallel to the dock, across the slips, close to the dock end.
    /// Typical for a large yacht taking several small slips.
    /// </summary>
    Alongside = 0,

    /// <summary>Bow toward the dock, centered across the slips (e.g. a wide catamaran in two slips).</summary>
    BowIn = 1,
}

/// <summary>
/// One boat occupying (or reserved for, or temporarily away from) several slips at once.
/// </summary>
/// <remarks>
/// The visualizer keeps member slips consistent: each carries the berth's <see cref="Status"/> and
/// <see cref="Boat"/>, and its <see cref="Slip.BerthId"/> is set. Changing the status or boat of any
/// member slip through the single-slip API changes the whole berth; setting a member Free releases
/// the berth. The boat is drawn once, across the combined area of the slips, using the first slip's
/// orientation as the reference. Finger piers between member slips are not drawn.
/// </remarks>
public sealed record MultiSlipBerth
{
    /// <summary>Describes a berth. To create one at runtime use <c>IMarinaVisualizer.DockAlongside</c> or <c>AssignBoatToSlips</c>; to load one use <see cref="MarinaLayout.MultiSlipBerths"/>.</summary>
    /// <param name="id">Unique berth id.</param>
    /// <param name="slipIds">Member slips (at least two, no upper limit). The first is the primary slip.</param>
    /// <param name="boat">The boat occupying the slips.</param>
    /// <param name="status">Occupied, Reserved or TemporarilyFree.</param>
    /// <param name="style">How the boat lies across the slips.</param>
    public MultiSlipBerth(string id, IReadOnlyList<string> slipIds, Boat boat, SlipStatus status = SlipStatus.Occupied, MooringStyle style = MooringStyle.Alongside)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(slipIds);
        ArgumentNullException.ThrowIfNull(boat);
        Id = id;
        SlipIds = slipIds.ToArray();
        Boat = boat;
        Status = status;
        Style = style;
    }

    /// <summary>Unique berth id (case-insensitive); member slips carry it in <see cref="Slip.BerthId"/>.</summary>
    public string Id { get; init; }

    /// <summary>Member slips. The first one is the primary slip: its orientation places the boat and boat clicks resolve to it.</summary>
    public IReadOnlyList<string> SlipIds { get; init; }

    /// <summary>The boat; every member slip carries it in <see cref="Slip.Boat"/>.</summary>
    public Boat Boat { get; init; }

    /// <summary>Occupied, Reserved or TemporarilyFree. A berth is never Free (release it instead).</summary>
    public SlipStatus Status { get; init; }

    /// <summary>How the boat lies across the slips.</summary>
    public MooringStyle Style { get; init; }

    /// <summary>The first member slip; its orientation places the boat.</summary>
    public string PrimarySlipId => SlipIds[0];

    /// <summary>True when <paramref name="slipId"/> is a member (case-insensitive).</summary>
    public bool Contains(string slipId) => SlipIds.Contains(slipId, StringComparer.OrdinalIgnoreCase);

    internal IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) yield return "Berth id must not be empty.";
        if (SlipIds is null || SlipIds.Count < 2)
        {
            yield return $"Berth '{Id}' must span at least two slips.";
        }
        else
        {
            if (SlipIds.Any(string.IsNullOrWhiteSpace)) yield return $"Berth '{Id}' contains an empty slip id.";
            else if (SlipIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != SlipIds.Count) yield return $"Berth '{Id}' lists a slip more than once.";
        }

        if (Boat is null) yield return $"Berth '{Id}' must have a boat.";
        else foreach (var error in Boat.Validate()) yield return $"Berth '{Id}': {error}";

        if (!Enum.IsDefined(Status) || Status == SlipStatus.Free) yield return $"Berth '{Id}' status must be Occupied, Reserved or TemporarilyFree.";
        if (!Enum.IsDefined(Style)) yield return $"Berth '{Id}' has an unknown mooring style '{Style}'.";
    }
}
