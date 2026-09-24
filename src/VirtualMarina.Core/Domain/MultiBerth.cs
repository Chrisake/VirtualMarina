using VirtualMarina.Core.Resources;

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
/// The visualizer keeps member berths consistent: each carries the multi-berth's <see cref="Status"/> and
/// <see cref="Boat"/>, and its <see cref="Berth.MultiBerthId"/> is set. Changing the status or boat of any
/// member berth through the single-berth API changes the whole multi-berth; setting a member Free releases
/// the multi-berth. The boat is drawn once, across the combined area of the berths, using the first berth's
/// orientation as the reference. Finger piers between member berths are not drawn.
/// </remarks>
public sealed record MultiBerth
{
    private readonly ValueList<string> _berthIds = ValueList<string>.Empty;
    private readonly ValueDictionary _metadata = ValueDictionary.Empty;

    /// <summary>Describes a multi-berth. To create one at runtime use <c>IMarinaVisualizer.MoorAlongside</c> or <c>AssignBoatToBerths</c>; to load one use <see cref="MarinaLayout.MultiBerths"/>.</summary>
    /// <param name="id">Unique multi-berth id; it must not also be the id of a berth.</param>
    /// <param name="berthIds">
    /// Member berths (at least two, no upper limit): water berths along one pier, or land berths on one land area. The first is
    /// the primary berth.
    /// </param>
    /// <param name="boat">The boat occupying the berths.</param>
    /// <param name="status">Occupied, Reserved or TemporarilyFree.</param>
    /// <param name="style">How the boat lies across the berths.</param>
    public MultiBerth(string id, IReadOnlyList<string> berthIds, Boat boat, BerthStatus status = BerthStatus.Occupied, MooringStyle style = MooringStyle.Alongside)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(berthIds);
        ArgumentNullException.ThrowIfNull(boat);
        Id = id;
        _berthIds = ValueList<string>.From(berthIds);
        Boat = boat;
        Status = status;
        Style = style;
    }

    /// <summary>Unique multi-berth id (case-insensitive), distinct from every berth id; member berths carry it in <see cref="Berth.MultiBerthId"/>.</summary>
    public string Id { get; init; }

    /// <summary>
    /// Member berths. The first one is the primary berth: its orientation places the boat and boat clicks resolve to it. The record
    /// keeps its own copy; null reads as empty (which <see cref="MarinaLayout.Validate"/> reports).
    /// </summary>
    public IReadOnlyList<string> BerthIds { get => _berthIds; init => _berthIds = ValueList<string>.From(value); }

    /// <summary>The boat; every member berth carries it in <see cref="Berth.Boat"/>.</summary>
    public Boat Boat { get; init; }

    /// <summary>Occupied, Reserved or TemporarilyFree. A multi-berth is never Free (release it instead).</summary>
    public BerthStatus Status { get; init; }

    /// <summary>How the boat lies across the berths.</summary>
    public MooringStyle Style { get; init; }

    /// <summary>
    /// Read-only string attributes the host application attaches to this multi-berth, e.g. its own key or a contract
    /// reference. Saved to and loaded from a marina file, and never read by the visualizer.
    /// </summary>
    /// <example><code>group with { Metadata = new Dictionary&lt;string, string&gt; { ["contract"] = "2026-114" } }</code></example>
    public IReadOnlyDictionary<string, string> Metadata { get => _metadata; init => _metadata = ValueDictionary.From(value); }

    /// <summary>The first member berth; its orientation places the boat. Empty when there are no members (an invalid multi-berth).</summary>
    public string PrimaryBerthId => BerthIds.Count > 0 ? BerthIds[0] : string.Empty;

    /// <summary>True when <paramref name="berthId"/> is a member (case-insensitive).</summary>
    public bool Contains(string berthId) => BerthIds.Contains(berthId, StringComparer.OrdinalIgnoreCase);

    internal IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) yield return Strings.ErrorMultiBerthIdEmpty;
        if (BerthIds.Count < 2)
        {
            yield return Strings.Format(Strings.ErrorMultiBerthTooFew, Id);
        }
        else
        {
            if (BerthIds.Any(string.IsNullOrWhiteSpace)) yield return Strings.Format(Strings.ErrorMultiBerthEmptyMember, Id);
            else if (BerthIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != BerthIds.Count) yield return Strings.Format(Strings.ErrorMultiBerthDuplicateMember, Id);
        }

        if (Boat is null) yield return Strings.Format(Strings.ErrorMultiBerthNoBoat, Id);
        else foreach (var error in Boat.Validate()) yield return Strings.Format(Strings.ErrorMultiBerthBoat, Id, error);

        if (!Enum.IsDefined(Status) || Status == BerthStatus.Free) yield return Strings.Format(Strings.ErrorMultiBerthStatus, Id);
        if (!Enum.IsDefined(Style)) yield return Strings.Format(Strings.ErrorMultiBerthUnknownStyle, Id, Style);
    }

    /// <summary>
    /// Problems with where the members are: one boat lies across them, so they must all be water berths along the same pier, or
    /// all land berths on the same land area. Members that could not be found are left to the caller to report.
    /// </summary>
    /// <param name="members">The member berths that exist, in any order.</param>
    internal IEnumerable<string> ValidateMembers(IEnumerable<Berth> members)
    {
        var found = members.Where(member => member is not null).ToList();
        if (found.Count < 2) yield break;

        if (found.Any(member => member.IsOnLand) && found.Any(member => !member.IsOnLand))
        {
            yield return Strings.Format(Strings.ErrorMultiBerthWaterAndLand, Id);
            yield break;
        }

        var places = found.Select(member => member.IsOnLand ? member.LandAreaId : member.PierId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (places > 1)
        {
            yield return found[0].IsOnLand
                ? Strings.Format(Strings.ErrorMultiBerthSeveralLandAreas, Id)
                : Strings.Format(Strings.ErrorMultiBerthSeveralPiers, Id);
        }
    }
}
