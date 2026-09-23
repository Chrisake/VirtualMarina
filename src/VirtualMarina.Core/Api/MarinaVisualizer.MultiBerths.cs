using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Api;

public sealed partial class MarinaVisualizer
{
    /// <summary>
    /// Moors one boat alongside (parallel to the pier) across several berths, e.g. a superyacht taking three small berths.
    /// </summary>
    /// <param name="berthIds">Member berths; the first one's orientation places the boat. At least two.</param>
    /// <param name="multiBerthId">Id for the multi-berth; generated from the first berth id when null.</param>
    /// <param name="boat">The boat.</param>
    /// <param name="status">Occupied, Reserved or TemporarilyFree.</param>
    /// <exception cref="InvalidOperationException">A berth already belongs to another multi-berth, or the id is taken.</exception>
    public MultiBerth MoorAlongside(IEnumerable<string> berthIds, Boat boat, BerthStatus status = BerthStatus.Occupied, string? multiBerthId = null) =>
        AssignBoatToBerths(berthIds, boat, status, MooringStyle.Alongside, multiBerthId);

    /// <summary>
    /// Puts a single boat in several berths at once. Every member berth takes <paramref name="status"/> and <paramref name="boat"/>,
    /// and the boat is drawn once across them.
    /// </summary>
    public MultiBerth AssignBoatToBerths(
        IEnumerable<string> berthIds, Boat boat, BerthStatus status = BerthStatus.Occupied,
        MooringStyle style = MooringStyle.Alongside, string? multiBerthId = null)
    {
        ArgumentNullException.ThrowIfNull(berthIds);
        ArgumentNullException.ThrowIfNull(boat);
        var ids = berthIds.ToArray();
        var id = multiBerthId ?? GenerateMultiBerthId(ids.FirstOrDefault());
        if (_multiBerths.ContainsKey(id)) throw new InvalidOperationException($"Multi-berth '{id}' already exists; use UpdateMultiBerth.");

        return ApplyMultiBerth(null, new MultiBerth(id, ids, boat, status, style));
    }

    /// <summary>
    /// Changes a multi-berth: its boat, status, mooring style and/or member berths. Berths no longer listed become Free.
    /// </summary>
    public MultiBerth UpdateMultiBerth(MultiBerth multiBerth)
    {
        ArgumentNullException.ThrowIfNull(multiBerth);
        var existing = GetMultiBerth(multiBerth.Id) ?? throw new KeyNotFoundException($"Multi-berth '{multiBerth.Id}' does not exist.");
        return ApplyMultiBerth(existing, multiBerth with { Id = existing.Id });
    }

    /// <summary>Partial update of a multi-berth; null arguments leave values unchanged.</summary>
    public MultiBerth UpdateMultiBerth(
        string multiBerthId, Boat? boat = null, BerthStatus? status = null, MooringStyle? style = null, IEnumerable<string>? berthIds = null)
    {
        var existing = GetMultiBerth(multiBerthId) ?? throw new KeyNotFoundException($"Multi-berth '{multiBerthId}' does not exist.");
        return UpdateMultiBerth(existing with
        {
            Boat = boat ?? existing.Boat,
            Status = status ?? existing.Status,
            Style = style ?? existing.Style,
            BerthIds = berthIds?.ToArray() ?? existing.BerthIds,
        });
    }

    /// <summary>Removes the multi-berth and sets all its berths Free.</summary>
    public bool ReleaseMultiBerth(string multiBerthId)
    {
        ArgumentNullException.ThrowIfNull(multiBerthId);
        if (!_multiBerths.TryGetValue(multiBerthId, out var group)) return false;

        using (BeginUpdate())
        {
            RemoveMultiBerthEntry(group.Id);
            foreach (var berthId in group.BerthIds)
            {
                if (_berths.TryGetValue(berthId, out var berth))
                {
                    ReplaceBerth(berth, berth with { Status = BerthStatus.Free, Boat = null, MultiBerthId = null }, keepMultiBerthId: false);
                }
            }

            RaiseLayoutChanged(LayoutChangeKind.MultiBerthRemoved, multiBerthId: group.Id);
        }

        return true;
    }

    /// <inheritdoc/>
    public MultiBerth? GetMultiBerth(string multiBerthId) =>
        multiBerthId is not null && _multiBerths.TryGetValue(multiBerthId, out var group) ? group : null;

    /// <inheritdoc/>
    public IReadOnlyList<MultiBerth> GetMultiBerths() => OrderedMultiBerths().ToArray();

    /// <summary>The multi-berth a berth belongs to, or null.</summary>
    public MultiBerth? GetMultiBerthFor(string berthId) =>
        GetBerth(berthId)?.MultiBerthId is { } multiBerthId ? GetMultiBerth(multiBerthId) : null;

    private MultiBerth ApplyMultiBerth(MultiBerth? existing, MultiBerth group)
    {
        var errors = group.Validate().ToList();
        if (errors.Count == 0 && _berths.ContainsKey(group.Id))
        {
            errors.Add(Strings.Format(Strings.ErrorMultiBerthIdIsBerthId, group.Id));
        }

        var canonicalIds = new List<string>();
        var members = new List<Berth>();
        foreach (var berthId in group.BerthIds ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(berthId)) continue;
            if (!_berths.TryGetValue(berthId, out var berth))
            {
                errors.Add(Strings.Format(Strings.ErrorMultiBerthUnknownBerth, group.Id, berthId));
                continue;
            }

            canonicalIds.Add(berth.Id);
            members.Add(berth);
            if (berth.MultiBerthId is { } other && !IdComparer.Equals(other, group.Id))
            {
                throw new InvalidOperationException(Strings.Format(Strings.ErrorBerthAlreadyInMultiBerth, berth.Id, other));
            }
        }

        // The same checks a layout gets when it is loaded: one boat lies across the members, so they have to be
        // together — all on the water along one pier, or all ashore on one land area.
        errors.AddRange(group.ValidateMembers(members));
        ThrowIfInvalid(errors);

        var stored = group with { BerthIds = canonicalIds.ToArray() };
        using (BeginUpdate())
        {
            _multiBerths[stored.Id] = stored;

            if (existing is not null)
            {
                foreach (var removedId in existing.BerthIds.Where(id => !stored.Contains(id)))
                {
                    if (_berths.TryGetValue(removedId, out var removed))
                    {
                        ReplaceBerth(removed, removed with { Status = BerthStatus.Free, Boat = null, MultiBerthId = null }, keepMultiBerthId: false);
                    }
                }
            }

            foreach (var berthId in stored.BerthIds)
            {
                var berth = _berths[berthId];
                ReplaceBerth(berth, berth with { Status = stored.Status, Boat = stored.Boat, MultiBerthId = stored.Id }, keepMultiBerthId: false);
            }

            // Finger piers between members appear/disappear and the boat moves.
            MarkSceneDirty();
            RaiseLayoutChanged(existing is null ? LayoutChangeKind.MultiBerthAdded : LayoutChangeKind.MultiBerthUpdated, multiBerthId: stored.Id);
        }

        return stored;
    }

    /// <summary>Called when a member berth is removed: shrinks the multi-berth, or dissolves it when fewer than two berths remain.</summary>
    private void DetachFromMultiBerth(Berth berth)
    {
        if (berth.MultiBerthId is not { } multiBerthId || !_multiBerths.TryGetValue(multiBerthId, out var group)) return;

        var remaining = group.BerthIds.Where(id => !IdComparer.Equals(id, berth.Id)).ToArray();
        if (remaining.Length >= 2)
        {
            _multiBerths[group.Id] = group with { BerthIds = remaining };
            RaiseLayoutChanged(LayoutChangeKind.MultiBerthUpdated, multiBerthId: group.Id);
            return;
        }

        // A single remaining berth keeps the boat as an ordinary assignment.
        RemoveMultiBerthEntry(group.Id);
        foreach (var id in remaining)
        {
            if (_berths.TryGetValue(id, out var member)) ReplaceBerth(member, member with { MultiBerthId = null }, keepMultiBerthId: false);
        }

        RaiseLayoutChanged(LayoutChangeKind.MultiBerthRemoved, multiBerthId: group.Id);
    }

    private void RemoveMultiBerthEntry(string multiBerthId)
    {
        _multiBerths.Remove(multiBerthId);
        MarkSceneDirty();
    }

    private string GenerateMultiBerthId(string? firstBerthId)
    {
        var baseId = $"MB-{(string.IsNullOrWhiteSpace(firstBerthId) ? "X" : firstBerthId)}";
        var id = baseId;
        // A while loop, because the condition tests the id the body rewrites rather than the counter.
        var n = 2;
        while (_multiBerths.ContainsKey(id)) id = $"{baseId}-{n++}";
        return id;
    }
}
