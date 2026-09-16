using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Api;

public sealed partial class MarinaVisualizer
{
    /// <summary>
    /// Moors one boat alongside (parallel to the dock) across several slips, e.g. a superyacht taking three small slips.
    /// </summary>
    /// <param name="slipIds">Member slips; the first one's orientation places the boat. At least two.</param>
    /// <param name="berthId">Id for the berth; generated from the first slip id when null.</param>
    /// <param name="boat">The boat.</param>
    /// <param name="status">Occupied, Reserved or TemporarilyFree.</param>
    /// <exception cref="InvalidOperationException">A slip already belongs to another berth, or the berth id is taken.</exception>
    public MultiSlipBerth DockAlongside(IEnumerable<string> slipIds, Boat boat, SlipStatus status = SlipStatus.Occupied, string? berthId = null) =>
        AssignBoatToSlips(slipIds, boat, status, MooringStyle.Alongside, berthId);

    /// <summary>
    /// Puts a single boat in several slips at once. Every member slip takes <paramref name="status"/> and <paramref name="boat"/>,
    /// and the boat is drawn once across them.
    /// </summary>
    public MultiSlipBerth AssignBoatToSlips(
        IEnumerable<string> slipIds, Boat boat, SlipStatus status = SlipStatus.Occupied,
        MooringStyle style = MooringStyle.Alongside, string? berthId = null)
    {
        ArgumentNullException.ThrowIfNull(slipIds);
        ArgumentNullException.ThrowIfNull(boat);
        var ids = slipIds.ToArray();
        var id = berthId ?? GenerateBerthId(ids.FirstOrDefault());
        if (_berths.ContainsKey(id)) throw new InvalidOperationException($"Berth '{id}' already exists; use UpdateMultiSlipBerth.");

        return ApplyBerth(null, new MultiSlipBerth(id, ids, boat, status, style));
    }

    /// <summary>
    /// Changes a multi-slip berth: its boat, status, mooring style and/or member slips. Slips no longer listed become Free.
    /// </summary>
    public MultiSlipBerth UpdateMultiSlipBerth(MultiSlipBerth berth)
    {
        ArgumentNullException.ThrowIfNull(berth);
        var existing = GetMultiSlipBerth(berth.Id) ?? throw new KeyNotFoundException($"Berth '{berth.Id}' does not exist.");
        return ApplyBerth(existing, berth with { Id = existing.Id });
    }

    /// <summary>Partial update of a multi-slip berth; null arguments leave values unchanged.</summary>
    public MultiSlipBerth UpdateMultiSlipBerth(
        string berthId, Boat? boat = null, SlipStatus? status = null, MooringStyle? style = null, IEnumerable<string>? slipIds = null)
    {
        var existing = GetMultiSlipBerth(berthId) ?? throw new KeyNotFoundException($"Berth '{berthId}' does not exist.");
        return UpdateMultiSlipBerth(existing with
        {
            Boat = boat ?? existing.Boat,
            Status = status ?? existing.Status,
            Style = style ?? existing.Style,
            SlipIds = slipIds?.ToArray() ?? existing.SlipIds,
        });
    }

    /// <summary>Removes the berth and sets all its slips Free.</summary>
    public bool ReleaseMultiSlipBerth(string berthId)
    {
        ArgumentNullException.ThrowIfNull(berthId);
        if (!_berths.TryGetValue(berthId, out var berth)) return false;

        using (BeginUpdate())
        {
            RemoveBerthEntry(berth.Id);
            foreach (var slipId in berth.SlipIds)
            {
                if (_slips.TryGetValue(slipId, out var slip))
                {
                    ReplaceSlip(slip, slip with { Status = SlipStatus.Free, Boat = null, BerthId = null }, keepBerthId: false);
                }
            }

            RaiseLayoutChanged(LayoutChangeKind.BerthRemoved, berthId: berth.Id);
        }

        return true;
    }

    /// <inheritdoc/>
    public MultiSlipBerth? GetMultiSlipBerth(string berthId) =>
        berthId is not null && _berths.TryGetValue(berthId, out var berth) ? berth : null;

    /// <inheritdoc/>
    public IReadOnlyList<MultiSlipBerth> GetMultiSlipBerths() => OrderedBerths().ToArray();

    /// <summary>The berth a slip belongs to, or null.</summary>
    public MultiSlipBerth? GetMultiSlipBerthForSlip(string slipId) =>
        GetSlip(slipId)?.BerthId is { } berthId ? GetMultiSlipBerth(berthId) : null;

    private MultiSlipBerth ApplyBerth(MultiSlipBerth? existing, MultiSlipBerth berth)
    {
        var errors = berth.Validate().ToList();
        var canonicalIds = new List<string>();
        foreach (var slipId in berth.SlipIds ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(slipId)) continue;
            if (!_slips.TryGetValue(slipId, out var slip))
            {
                errors.Add($"Berth '{berth.Id}' references unknown slip '{slipId}'.");
                continue;
            }

            canonicalIds.Add(slip.Id);
            if (slip.BerthId is { } other && !IdComparer.Equals(other, berth.Id))
            {
                throw new InvalidOperationException($"Slip '{slip.Id}' already belongs to berth '{other}'. Release or update that berth first.");
            }
        }

        ThrowIfInvalid(errors);

        var stored = berth with { SlipIds = canonicalIds.ToArray() };
        using (BeginUpdate())
        {
            if (existing is null) _berthOrder.Add(stored.Id);
            _berths[stored.Id] = stored;

            if (existing is not null)
            {
                foreach (var removedId in existing.SlipIds.Where(id => !stored.Contains(id)))
                {
                    if (_slips.TryGetValue(removedId, out var removed))
                    {
                        ReplaceSlip(removed, removed with { Status = SlipStatus.Free, Boat = null, BerthId = null }, keepBerthId: false);
                    }
                }
            }

            foreach (var slipId in stored.SlipIds)
            {
                var slip = _slips[slipId];
                ReplaceSlip(slip, slip with { Status = stored.Status, Boat = stored.Boat, BerthId = stored.Id }, keepBerthId: false);
            }

            // Finger piers between members appear/disappear and the boat moves.
            MarkSceneDirty();
            RaiseLayoutChanged(existing is null ? LayoutChangeKind.BerthAdded : LayoutChangeKind.BerthUpdated, berthId: stored.Id);
        }

        return stored;
    }

    /// <summary>Called when a member slip is removed: shrinks the berth, or dissolves it when fewer than two slips remain.</summary>
    private void DetachFromBerth(Slip slip)
    {
        if (slip.BerthId is not { } berthId || !_berths.TryGetValue(berthId, out var berth)) return;

        var remaining = berth.SlipIds.Where(id => !IdComparer.Equals(id, slip.Id)).ToArray();
        if (remaining.Length >= 2)
        {
            _berths[berth.Id] = berth with { SlipIds = remaining };
            RaiseLayoutChanged(LayoutChangeKind.BerthUpdated, berthId: berth.Id);
            return;
        }

        // A single remaining slip keeps the boat as an ordinary assignment.
        RemoveBerthEntry(berth.Id);
        foreach (var id in remaining)
        {
            if (_slips.TryGetValue(id, out var member)) ReplaceSlip(member, member with { BerthId = null }, keepBerthId: false);
        }

        RaiseLayoutChanged(LayoutChangeKind.BerthRemoved, berthId: berth.Id);
    }

    private void RemoveBerthEntry(string berthId)
    {
        _berths.Remove(berthId);
        _berthOrder.RemoveAll(id => IdComparer.Equals(id, berthId));
        MarkSceneDirty();
    }

    private string GenerateBerthId(string? firstSlipId)
    {
        var baseId = $"BERTH-{(string.IsNullOrWhiteSpace(firstSlipId) ? "X" : firstSlipId)}";
        var id = baseId;
        for (var n = 2; _berths.ContainsKey(id); n++) id = $"{baseId}-{n}";
        return id;
    }
}
