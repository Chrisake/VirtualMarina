using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;

namespace VirtualMarina.Core.Api;

public sealed partial class MarinaVisualizer
{
    /// <inheritdoc/>
    public void InitializeLayout(MarinaLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var errors = layout.Validate();
        if (errors.Count > 0) throw new MarinaLayoutException(errors);

        var previousSelection = SelectedBerths;
        ClosePopup();
        ClearState();

        MarinaName = layout.Name;
        foreach (var land in layout.LandAreas)
        {
            _landAreas[land.Id] = land;
            _landOrder.Add(land.Id);
        }

        foreach (var pier in layout.Piers)
        {
            _piers[pier.Id] = pier;
            _pierOrder.Add(pier.Id);
        }

        foreach (var berth in layout.Berths)
        {
            _berths[berth.Id] = Normalize(berth with { MultiBerthId = null });
            _berthOrder.Add(berth.Id);
        }

        foreach (var divider in layout.Dividers)
        {
            _dividers[divider.Id] = divider;
            _dividerOrder.Add(divider.Id);
        }

        foreach (var berth in layout.MultiBerths)
        {
            var canonical = berth with { BerthIds = berth.BerthIds.Select(id => _berths[id].Id).ToArray() };
            _multiBerths[berth.Id] = canonical;
            _multiBerthOrder.Add(berth.Id);
            foreach (var berthId in canonical.BerthIds)
            {
                _berths[berthId] = _berths[berthId] with { Status = canonical.Status, Boat = canonical.Boat, MultiBerthId = canonical.Id };
            }
        }

        RebuildLandMeshes();

        var (min, max) = layout.ComputeBounds();
        SetWaterGrid((min + max) * 0.5f, Water.Size);
        RebuildBuiltInPresets();
        ResetCamera(immediate: true);
        MarkSceneDirty();

        if (previousSelection.Count > 0)
        {
            SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(previousSelection, Array.Empty<Berth>()));
            SelectionCleared?.Invoke(this, EventArgs.Empty);
        }

        RaiseLayoutChanged(LayoutChangeKind.Initialized);
    }

    /// <inheritdoc/>
    public MarinaLayout GetLayout() => new()
    {
        Name = MarinaName,
        Piers = OrderedPiers().ToArray(),
        Berths = OrderedBerths().ToArray(),
        Dividers = OrderedDividers().ToArray(),
        MultiBerths = OrderedMultiBerths().ToArray(),
        LandAreas = OrderedLandAreas().ToArray(),
    };

    /// <inheritdoc/>
    public void ClearLayout()
    {
        var previous = SelectedBerths;
        ClosePopup();
        ClearState();
        RebuildLandMeshes();
        RebuildBuiltInPresets();
        MarkSceneDirty();
        if (previous.Count > 0)
        {
            SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(previous, Array.Empty<Berth>()));
            SelectionCleared?.Invoke(this, EventArgs.Empty);
        }

        RaiseLayoutChanged(LayoutChangeKind.Cleared);
    }

    // ---- Piers ----------------------------------------------------------------------------------

    /// <inheritdoc/>
    public void AddPier(Pier pier)
    {
        ArgumentNullException.ThrowIfNull(pier);
        ThrowIfInvalid(pier.Validate());
        if (_piers.ContainsKey(pier.Id)) throw new InvalidOperationException($"Pier '{pier.Id}' already exists.");

        _piers[pier.Id] = pier;
        _pierOrder.Add(pier.Id);
        RebuildBuiltInPresets();
        MarkSceneDirty();
        RaiseLayoutChanged(LayoutChangeKind.PierAdded, pier.Id);
    }

    /// <inheritdoc/>
    public void UpdatePier(Pier pier)
    {
        ArgumentNullException.ThrowIfNull(pier);
        ThrowIfInvalid(pier.Validate());
        if (!_piers.TryGetValue(pier.Id, out var existing)) throw new KeyNotFoundException($"Pier '{pier.Id}' does not exist.");

        _piers[existing.Id] = pier with { Id = existing.Id };
        RebuildBuiltInPresets();
        MarkSceneDirty();
        RefreshPopupIfShowingPier(existing.Id);
        RaiseLayoutChanged(LayoutChangeKind.PierUpdated, existing.Id);
    }

    /// <inheritdoc/>
    public Pier UpdatePier(PierUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var existing = GetPier(update.PierId) ?? throw new KeyNotFoundException($"Pier '{update.PierId}' does not exist.");
        var updated = update.ApplyTo(existing);
        UpdatePier(updated);
        return _piers[existing.Id];
    }

    /// <inheritdoc/>
    public bool RemovePier(string pierId, bool removeBerths = true)
    {
        ArgumentNullException.ThrowIfNull(pierId);
        if (!_piers.TryGetValue(pierId, out var pier)) return false;

        var berthIds = _berthOrder.Where(id => IdComparer.Equals(_berths[id].PierId, pier.Id)).ToList();
        if (berthIds.Count > 0 && !removeBerths)
        {
            throw new InvalidOperationException($"Pier '{pierId}' still has {berthIds.Count} berth(s).");
        }

        using (BeginUpdate())
        {
            foreach (var berthId in berthIds) RemoveBerth(berthId);
            foreach (var dividerId in _dividerOrder.Where(id => IdComparer.Equals(_dividers[id].PierId, pier.Id)).ToList()) RemoveDivider(dividerId);
            _piers.Remove(pier.Id);
            _pierOrder.RemoveAll(id => IdComparer.Equals(id, pier.Id));
            RebuildBuiltInPresets();
            MarkSceneDirty();
            RaiseLayoutChanged(LayoutChangeKind.PierRemoved, pier.Id);
        }

        return true;
    }

    /// <inheritdoc/>
    public Pier? GetPier(string pierId) => pierId is not null && _piers.TryGetValue(pierId, out var pier) ? pier : null;

    /// <inheritdoc/>
    public IReadOnlyList<Pier> GetPiers() => OrderedPiers().ToArray();

    // ---- Dividers -------------------------------------------------------------------------------

    /// <inheritdoc/>
    public void AddDivider(Divider divider)
    {
        ArgumentNullException.ThrowIfNull(divider);
        ValidateDividerForStorage(divider);
        if (_dividers.ContainsKey(divider.Id)) throw new InvalidOperationException($"Divider '{divider.Id}' already exists.");

        _dividers[divider.Id] = divider;
        _dividerOrder.Add(divider.Id);
        MarkSceneDirty();
        RaiseLayoutChanged(LayoutChangeKind.DividerAdded, divider.PierId, dividerId: divider.Id);
    }

    /// <inheritdoc/>
    public void AddDividers(IEnumerable<Divider> dividers)
    {
        ArgumentNullException.ThrowIfNull(dividers);
        using (BeginUpdate())
        {
            foreach (var divider in dividers) AddDivider(divider);
        }
    }

    /// <inheritdoc/>
    public void UpdateDivider(Divider divider)
    {
        ArgumentNullException.ThrowIfNull(divider);
        ValidateDividerForStorage(divider);
        if (!_dividers.TryGetValue(divider.Id, out var existing)) throw new KeyNotFoundException($"Divider '{divider.Id}' does not exist.");

        _dividers[existing.Id] = divider with { Id = existing.Id };
        MarkSceneDirty();
        RaiseLayoutChanged(LayoutChangeKind.DividerUpdated, divider.PierId, dividerId: existing.Id);
    }

    /// <inheritdoc/>
    public bool RemoveDivider(string dividerId)
    {
        ArgumentNullException.ThrowIfNull(dividerId);
        if (!_dividers.TryGetValue(dividerId, out var divider)) return false;

        _dividers.Remove(divider.Id);
        _dividerOrder.RemoveAll(id => IdComparer.Equals(id, divider.Id));
        MarkSceneDirty();
        RaiseLayoutChanged(LayoutChangeKind.DividerRemoved, divider.PierId, dividerId: divider.Id);
        return true;
    }

    /// <inheritdoc/>
    public Divider? GetDivider(string dividerId) => dividerId is not null && _dividers.TryGetValue(dividerId, out var divider) ? divider : null;

    /// <inheritdoc/>
    public IReadOnlyList<Divider> GetDividers() => OrderedDividers().ToArray();

    /// <inheritdoc/>
    public IReadOnlyList<Divider> GetDividersByPier(string pierId) =>
        OrderedDividers().Where(d => IdComparer.Equals(d.PierId, pierId)).ToArray();

    // ---- Berths ----------------------------------------------------------------------------------

    /// <inheritdoc/>
    public void AddBerth(Berth berth)
    {
        ArgumentNullException.ThrowIfNull(berth);
        ValidateBerthForStorage(berth);
        if (_berths.ContainsKey(berth.Id)) throw new InvalidOperationException($"Berth '{berth.Id}' already exists.");

        _berths[berth.Id] = Normalize(berth with { MultiBerthId = null });
        _berthOrder.Add(berth.Id);
        MarkSceneDirty();
        RaiseLayoutChanged(LayoutChangeKind.BerthAdded, berth.PierId, berth.Id, landAreaId: berth.LandAreaId);
    }

    /// <inheritdoc/>
    public Berth AddBerth(string berthId, string pierId, System.Numerics.Vector2 center, float headingDegrees, float length, float width, string? label = null)
    {
        var berth = new Berth(berthId, pierId, center, headingDegrees, length, width) { Label = label };
        AddBerth(berth);
        return _berths[berth.Id];
    }

    /// <inheritdoc/>
    public void AddBerths(IEnumerable<Berth> berths)
    {
        ArgumentNullException.ThrowIfNull(berths);
        using (BeginUpdate())
        {
            foreach (var berth in berths) AddBerth(berth);
        }
    }

    /// <inheritdoc/>
    public void UpdateBerth(Berth berth)
    {
        ArgumentNullException.ThrowIfNull(berth);
        var existing = RequireBerth(berth.Id);
        ReplaceBerthRoutingMultiBerth(existing, berth);
    }

    /// <inheritdoc/>
    public Berth UpdateBerth(BerthUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var existing = RequireBerth(update.BerthId);
        var replacement = update.ApplyTo(existing);
        if (update.ExternalData is not null)
        {
            foreach (var (key, value) in update.ExternalData) existing.ExternalData[key] = value;
        }

        return ReplaceBerthRoutingMultiBerth(existing, replacement);
    }

    /// <inheritdoc/>
    public bool RemoveBerth(string berthId)
    {
        ArgumentNullException.ThrowIfNull(berthId);
        if (!_berths.TryGetValue(berthId, out var berth)) return false;

        using (BeginUpdate())
        {
            RemoveFromSelectionCore(berth.Id);
            if (IdComparer.Equals(_hoveredBerthId, berth.Id)) SetHoveredBerth(null);
            if (berth.MultiBerthId is not null) DetachFromMultiBerth(berth);

            _berths.Remove(berth.Id);
            _berthOrder.RemoveAll(id => IdComparer.Equals(id, berth.Id));
            MarkSceneDirty();
            RaiseLayoutChanged(LayoutChangeKind.BerthRemoved, berth.PierId, berth.Id, landAreaId: berth.LandAreaId);
        }

        return true;
    }

    /// <inheritdoc/>
    public Berth RenameBerth(string berthId, string newBerthId)
    {
        ArgumentNullException.ThrowIfNull(berthId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newBerthId);
        var existing = RequireBerth(berthId);
        newBerthId = newBerthId.Trim();
        if (IdComparer.Equals(existing.Id, newBerthId))
        {
            // Same name but spelled differently (case, spacing): store the new spelling, nothing else moves.
            return existing.Id == newBerthId ? existing : ReplaceBerthId(existing, newBerthId);
        }

        if (_berths.ContainsKey(newBerthId)) throw new InvalidOperationException($"Berth '{newBerthId}' already exists.");
        return ReplaceBerthId(existing, newBerthId);
    }

    /// <inheritdoc/>
    public Pier ChangePierId(string pierId, string newPierId)
    {
        ArgumentNullException.ThrowIfNull(pierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPierId);
        var existing = _piers.TryGetValue(pierId, out var found) ? found : throw new KeyNotFoundException($"Pier '{pierId}' does not exist.");
        newPierId = newPierId.Trim();

        if (!IdComparer.Equals(existing.Id, newPierId) && _piers.ContainsKey(newPierId))
        {
            throw new InvalidOperationException($"Pier '{newPierId}' already exists.");
        }

        if (existing.Id == newPierId) return existing;

        var renamed = existing with { Id = newPierId };
        using (BeginUpdate())
        {
            _piers.Remove(existing.Id);
            _piers[renamed.Id] = renamed;

            var position = _pierOrder.FindIndex(id => IdComparer.Equals(id, existing.Id));
            if (position >= 0) _pierOrder[position] = renamed.Id;

            // Everything that pointed at the old id now points at the new one.
            foreach (var berthId in _berthOrder.ToArray())
            {
                if (_berths.TryGetValue(berthId, out var berth) && IdComparer.Equals(berth.PierId, existing.Id))
                {
                    _berths[berthId] = berth with { PierId = renamed.Id };
                }
            }

            foreach (var dividerId in _dividerOrder.ToArray())
            {
                if (_dividers.TryGetValue(dividerId, out var divider) && IdComparer.Equals(divider.PierId, existing.Id))
                {
                    _dividers[dividerId] = divider with { PierId = renamed.Id };
                }
            }

            MarkSceneDirty();
        }

        RaiseLayoutChanged(LayoutChangeKind.PierRenamed, renamed.Id);
        return renamed;
    }

    /// <inheritdoc/>
    public Berth? GetBerth(string berthId) => berthId is not null && _berths.TryGetValue(berthId, out var berth) ? berth : null;

    /// <inheritdoc/>
    public IReadOnlyList<Berth> GetBerths() => OrderedBerths().ToArray();

    /// <inheritdoc/>
    public IReadOnlyList<Berth> GetBerthsByPier(string pierId) =>
        OrderedBerths().Where(s => IdComparer.Equals(s.PierId, pierId)).ToArray();

    /// <inheritdoc/>
    public IReadOnlyList<Berth> GetBerthsByLandArea(string landAreaId) =>
        OrderedBerths().Where(s => IdComparer.Equals(s.LandAreaId, landAreaId)).ToArray();

    // ---- Land -----------------------------------------------------------------------------------

    /// <inheritdoc/>
    public void AddLandArea(LandArea landArea)
    {
        ArgumentNullException.ThrowIfNull(landArea);
        ThrowIfInvalid(landArea.Validate());
        if (_landAreas.ContainsKey(landArea.Id)) throw new InvalidOperationException($"Land area '{landArea.Id}' already exists.");

        _landAreas[landArea.Id] = landArea;
        _landOrder.Add(landArea.Id);
        RegisterLandMesh(landArea);
        RebuildBuiltInPresets();
        MarkSceneDirty();
        RaiseLayoutChanged(LayoutChangeKind.LandAreaAdded, landAreaId: landArea.Id);
    }

    /// <inheritdoc/>
    public void UpdateLandArea(LandArea landArea)
    {
        ArgumentNullException.ThrowIfNull(landArea);
        ThrowIfInvalid(landArea.Validate());
        if (!_landAreas.TryGetValue(landArea.Id, out var existing)) throw new KeyNotFoundException($"Land area '{landArea.Id}' does not exist.");

        var updated = landArea with { Id = existing.Id };
        _landAreas[existing.Id] = updated;
        RegisterLandMesh(updated);
        RebuildBuiltInPresets();
        MarkSceneDirty();
        RefreshPopupIfShowingLand(existing.Id);
        RaiseLayoutChanged(LayoutChangeKind.LandAreaUpdated, landAreaId: existing.Id);
    }

    /// <inheritdoc/>
    public bool RemoveLandArea(string landAreaId, bool removeBerths = true)
    {
        ArgumentNullException.ThrowIfNull(landAreaId);
        if (!_landAreas.TryGetValue(landAreaId, out var land)) return false;

        var berthIds = _berthOrder.Where(id => IdComparer.Equals(_berths[id].LandAreaId, land.Id)).ToList();
        if (berthIds.Count > 0 && !removeBerths)
        {
            throw new InvalidOperationException($"Land area '{landAreaId}' still has {berthIds.Count} berth(s).");
        }

        // Removing land berths too is coalesced into one BatchUpdated notification.
        using (berthIds.Count > 0 ? BeginUpdate() : null)
        {
            foreach (var berthId in berthIds) RemoveBerth(berthId);
            _landAreas.Remove(land.Id);
            _landOrder.RemoveAll(id => IdComparer.Equals(id, land.Id));
            UnregisterLandMesh(land.Id);
            RebuildBuiltInPresets();
            MarkSceneDirty();
            RaiseLayoutChanged(LayoutChangeKind.LandAreaRemoved, landAreaId: land.Id);
        }

        return true;
    }

    /// <inheritdoc/>
    public object[] ExportObjects() => GetLayout().ToObjects();

    /// <inheritdoc/>
    public LandArea? GetLandArea(string landAreaId) => landAreaId is not null && _landAreas.TryGetValue(landAreaId, out var land) ? land : null;

    /// <inheritdoc/>
    public IReadOnlyList<LandArea> GetLandAreas() => OrderedLandAreas().ToArray();

    // ---- Berths (continued) ----------------------------------------------------------------------

    /// <inheritdoc/>
    public IReadOnlyList<Berth> GetBerthsByStatus(BerthStatus status) =>
        OrderedBerths().Where(s => s.Status == status).ToArray();

    /// <inheritdoc/>
    public BatchUpdateResult BatchUpdate(IEnumerable<BerthUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        var errors = new List<BatchUpdateError>();
        var applied = 0;

        using (BeginUpdate())
        {
            foreach (var update in updates)
            {
                if (update is null) continue;
                try
                {
                    UpdateBerth(update);
                    applied++;
                }
                catch (Exception ex) when (ex is KeyNotFoundException or MarinaLayoutException or ArgumentException or InvalidOperationException)
                {
                    errors.Add(new BatchUpdateError(update.BerthId ?? string.Empty, ex.Message));
                }
            }
        }

        return new BatchUpdateResult(applied, errors);
    }

    /// <inheritdoc/>
    public IDisposable BeginUpdate()
    {
        _updateDepth++;
        return new UpdateScope(this);
    }

    private void EndUpdate()
    {
        if (_updateDepth == 0) return;
        _updateDepth--;
        if (_updateDepth > 0) return;

        if (_layoutChangePending)
        {
            _layoutChangePending = false;
            LayoutChanged?.Invoke(this, new LayoutChangedEventArgs(LayoutChangeKind.BatchUpdated));
        }

        FlushPopupRefresh();
    }

    /// <summary>
    /// Replaces a berth. A status or boat change on a member of a multi-berth applies to the whole berth
    /// (or releases it, when the berth becomes Free or loses its boat).
    /// </summary>
    private Berth ReplaceBerthRoutingMultiBerth(Berth existing, Berth replacement)
    {
        if (existing.MultiBerthId is not { } multiBerthId || !_multiBerths.TryGetValue(multiBerthId, out var berth) ||
            (replacement.Status == existing.Status && replacement.Boat == existing.Boat))
        {
            return ReplaceBerth(existing, replacement);
        }

        ValidateBerthForStorage(replacement);
        using (BeginUpdate())
        {
            if (!replacement.Status.CanHaveBoat() || replacement.Boat is null)
            {
                ReleaseMultiBerth(berth.Id);
                return ReplaceBerth(_berths[existing.Id], replacement with { MultiBerthId = null });
            }

            var updated = UpdateMultiBerth(berth with { Status = replacement.Status, Boat = replacement.Boat });
            return ReplaceBerth(_berths[existing.Id], replacement with { Status = updated.Status, Boat = updated.Boat, MultiBerthId = updated.Id });
        }
    }

    /// <summary>Moves a berth to another id, taking its place in the order, the selection and its multi-berth with it.</summary>
    private Berth ReplaceBerthId(Berth existing, string newBerthId)
    {
        var renamed = Normalize(existing with { Id = newBerthId });
        using (BeginUpdate())
        {
            _berths.Remove(existing.Id);
            _berths[renamed.Id] = renamed;

            var position = _berthOrder.FindIndex(id => IdComparer.Equals(id, existing.Id));
            if (position >= 0) _berthOrder[position] = renamed.Id;

            for (var i = 0; i < _selection.Count; i++)
            {
                if (IdComparer.Equals(_selection[i], existing.Id)) _selection[i] = renamed.Id;
            }

            if (IdComparer.Equals(_hoveredBerthId, existing.Id)) _hoveredBerthId = renamed.Id;

            if (renamed.MultiBerthId is { } multiBerthId && _multiBerths.TryGetValue(multiBerthId, out var group))
            {
                _multiBerths[group.Id] = group with
                {
                    BerthIds = group.BerthIds.Select(id => IdComparer.Equals(id, existing.Id) ? renamed.Id : id).ToArray(),
                };
            }

            MarkSceneDirty();
            if (IsBerthSelected(renamed.Id)) RequestPopupRefresh();
        }

        // Outside the update scope, so listeners are told the berth was renamed rather than just "something changed".
        RaiseLayoutChanged(LayoutChangeKind.BerthRenamed, renamed.PierId, renamed.Id, landAreaId: renamed.LandAreaId);
        return renamed;
    }

    /// <summary>Stores a new definition for an existing berth, keeping selection, hover and events consistent.</summary>
    /// <param name="existing">The currently stored berth.</param>
    /// <param name="replacement">The new definition (same id).</param>
    /// <param name="keepMultiBerthId">When false, the replacement's <see cref="Berth.MultiBerthId"/> is used as-is (berth management).</param>
    private Berth ReplaceBerth(Berth existing, Berth replacement, bool keepMultiBerthId = true)
    {
        if (!IdComparer.Equals(existing.Id, replacement.Id))
        {
            throw new InvalidOperationException("A berth's id cannot be changed; remove it and add a new berth instead.");
        }

        ValidateBerthForStorage(replacement);

        if (!ReferenceEquals(existing.ExternalData, replacement.ExternalData))
        {
            foreach (var (key, value) in replacement.ExternalData) existing.ExternalData[key] = value;
        }

        var normalized = Normalize(replacement with
        {
            Id = existing.Id,
            ExternalData = existing.ExternalData,
            MultiBerthId = keepMultiBerthId && replacement.MultiBerthId is null ? existing.MultiBerthId : replacement.MultiBerthId,
        });
        _berths[existing.Id] = normalized;
        MarkSceneDirty();

        if (IsBerthSelected(existing.Id))
        {
            if (!IsSelectable(normalized)) RemoveFromSelectionCore(existing.Id);
            else RequestPopupRefresh();
        }

        if (IdComparer.Equals(_hoveredBerthId, existing.Id) && !(normalized.IsInteractive && _statusFilter.Includes(normalized.Status)))
        {
            SetHoveredBerth(null);
        }

        if (existing.Status != normalized.Status || existing.Boat != normalized.Boat)
        {
            BerthStatusChanged?.Invoke(this, new BerthStatusChangedEventArgs(existing, normalized));
        }

        RaiseLayoutChanged(LayoutChangeKind.BerthUpdated, normalized.PierId, normalized.Id, landAreaId: normalized.LandAreaId);
        return normalized;
    }

    private void ValidateBerthForStorage(Berth berth)
    {
        var errors = berth.Validate().ToList();
        if (!string.IsNullOrWhiteSpace(berth.PierId) && !_piers.ContainsKey(berth.PierId))
        {
            errors.Add($"Berth '{berth.Id}' references unknown pier '{berth.PierId}'.");
        }

        if (!string.IsNullOrWhiteSpace(berth.LandAreaId) && !_landAreas.ContainsKey(berth.LandAreaId))
        {
            errors.Add($"Berth '{berth.Id}' references unknown land area '{berth.LandAreaId}'.");
        }

        ThrowIfInvalid(errors);
    }

    private void ValidateDividerForStorage(Divider divider)
    {
        var errors = divider.Validate().ToList();
        if (divider.PierId is not null && !_piers.ContainsKey(divider.PierId))
        {
            errors.Add($"Divider '{divider.Id}' references unknown pier '{divider.PierId}'.");
        }

        ThrowIfInvalid(errors);
    }

    private static void ThrowIfInvalid(IEnumerable<string> errors)
    {
        var list = errors as IReadOnlyList<string> ?? errors.ToList();
        if (list.Count > 0) throw new MarinaLayoutException(list);
    }

    /// <summary>Enforces invariants: a free berth never carries a boat.</summary>
    private static Berth Normalize(Berth berth) =>
        berth.Status == BerthStatus.Free && berth.Boat is not null ? berth with { Boat = null } : berth;

    private void ClearState()
    {
        _piers.Clear();
        _pierOrder.Clear();
        _berths.Clear();
        _berthOrder.Clear();
        _dividers.Clear();
        _dividerOrder.Clear();
        _multiBerths.Clear();
        _multiBerthOrder.Clear();
        _landAreas.Clear();
        _landOrder.Clear();
        _selection.Clear();
        _hoveredBerthId = null;
        _popupRefreshPending = false;
        Designer.ClearHistory(); // The old elements are gone; undoing into them would resurrect stale ids.
    }
}
