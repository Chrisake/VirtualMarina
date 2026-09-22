using System.Collections.ObjectModel;

namespace VirtualMarina.Core.Domain;

/// <summary>How a boat that spans several berths lies in them.</summary>
public enum MooringStyle
{
    /// <summary>
    /// Side-to: the boat lies parallel to the pier, across the berths, close to the pier end.
    /// Typical for a large yacht taking several small berths.
    /// </summary>
    Alongside = 0,

    /// <summary>Bow toward the pier, centered across the berths (e.g. a wide catamaran in two berths).</summary>
    BowIn = 1,
}

/// <summary>
/// One boat occupying (or reserved for, or temporarily away from) several berths at once.
/// </summary>
/// <remarks>
/// The visualizer keeps member berths consistent: each carries the berth's <see cref="Status"/> and
/// <see cref="Boat"/>, and its <see cref="Berth.MultiBerthId"/> is set. Changing the status or boat of any
/// member berth through the single-berth API changes the whole berth; setting a member Free releases
/// the berth. The boat is drawn once, across the combined area of the berths, using the first berth's
/// orientation as the reference. Finger piers between member berths are not drawn.
/// </remarks>
public sealed record MultiBerth
{
    /// <summary>Describes a berth. To create one at runtime use <c>IMarinaVisualizer.MoorAlongside</c> or <c>AssignBoatToBerths</c>; to load one use <see cref="MarinaLayout.MultiBerths"/>.</summary>
    /// <param name="id">Unique berth id.</param>
    /// <param name="berthIds">Member berths (at least two, no upper limit). The first is the primary berth.</param>
    /// <param name="boat">The boat occupying the berths.</param>
    /// <param name="status">Occupied, Reserved or TemporarilyFree.</param>
    /// <param name="style">How the boat lies across the berths.</param>
    public MultiBerth(string id, IReadOnlyList<string> berthIds, Boat boat, BerthStatus status = BerthStatus.Occupied, MooringStyle style = MooringStyle.Alongside)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(berthIds);
        ArgumentNullException.ThrowIfNull(boat);
        Id = id;
        BerthIds = berthIds.ToArray();
        Boat = boat;
        Status = status;
        Style = style;
    }

    /// <summary>Unique berth id (case-insensitive); member berths carry it in <see cref="Berth.MultiBerthId"/>.</summary>
    public string Id { get; init; }

    /// <summary>Member berths. The first one is the primary berth: its orientation places the boat and boat clicks resolve to it.</summary>
    public IReadOnlyList<string> BerthIds { get; init; }

    /// <summary>The boat; every member berth carries it in <see cref="Berth.Boat"/>.</summary>
    public Boat Boat { get; init; }

    /// <summary>Occupied, Reserved or TemporarilyFree. A berth is never Free (release it instead).</summary>
    public BerthStatus Status { get; init; }

    /// <summary>How the boat lies across the berths.</summary>
    public MooringStyle Style { get; init; }

    /// <summary>
    /// Read-only string attributes the host application attaches to this multi-berth, e.g. its own key or a contract
    /// reference. Saved to and loaded from a marina file, and never read by the visualizer.
    /// </summary>
    /// <example><code>group with { Metadata = new Dictionary&lt;string, string&gt; { ["contract"] = "2026-114" } }</code></example>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>The first member berth; its orientation places the boat.</summary>
    public string PrimaryBerthId => BerthIds[0];

    /// <summary>True when <paramref name="berthId"/> is a member (case-insensitive).</summary>
    public bool Contains(string berthId) => BerthIds.Contains(berthId, StringComparer.OrdinalIgnoreCase);

    internal IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) yield return "Berth id must not be empty.";
        if (BerthIds is null || BerthIds.Count < 2)
        {
            yield return $"Berth '{Id}' must span at least two berths.";
        }
        else
        {
            if (BerthIds.Any(string.IsNullOrWhiteSpace)) yield return $"Berth '{Id}' contains an empty berth id.";
            else if (BerthIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != BerthIds.Count) yield return $"Berth '{Id}' lists a berth more than once.";
        }

        if (Boat is null) yield return $"Berth '{Id}' must have a boat.";
        else foreach (var error in Boat.Validate()) yield return $"Berth '{Id}': {error}";

        if (!Enum.IsDefined(Status) || Status == BerthStatus.Free) yield return $"Berth '{Id}' status must be Occupied, Reserved or TemporarilyFree.";
        if (!Enum.IsDefined(Style)) yield return $"Berth '{Id}' has an unknown mooring style '{Style}'.";
    }
}
