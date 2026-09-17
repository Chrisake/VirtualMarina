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

        var previousSelection = SelectedSlips;
        ClosePopup();
        ClearState();

        MarinaName = layout.Name;
        foreach (var land in layout.LandAreas)
        {
            _landAreas[land.Id] = land;
            _landOrder.Add(land.Id);
        }

        foreach (var dock in layout.Docks)
        {
            _docks[dock.Id] = dock;
            _dockOrder.Add(dock.Id);
        }

        foreach (var slip in layout.Slips)
        {
            _slips[slip.Id] = Normalize(slip with { BerthId = null });
            _slipOrder.Add(slip.Id);
        }

        foreach (var divider in layout.Dividers)
        {
            _dividers[divider.Id] = divider;
            _dividerOrder.Add(divider.Id);
        }

        foreach (var berth in layout.MultiSlipBerths)
        {
            var canonical = berth with { SlipIds = berth.SlipIds.Select(id => _slips[id].Id).ToArray() };
            _berths[berth.Id] = canonical;
            _berthOrder.Add(berth.Id);
            foreach (var slipId in canonical.SlipIds)
            {
                _slips[slipId] = _slips[slipId] with { Status = canonical.Status, Boat = canonical.Boat, BerthId = canonical.Id };
            }
        }

        RegisterLandMeshes();

        var (min, max) = layout.ComputeBounds();
        Meshes.Register(MarinaMeshFactory.CreateWaterGrid(MeshIds.Water, Water.Size, Water.GridResolution, (min + max) * 0.5f));
        RebuildBuiltInPresets();
        ResetCamera(immediate: true);
        MarkSceneDirty();

        if (previousSelection.Count > 0)
        {
            SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(previousSelection, Array.Empty<Slip>()));
            SelectionCleared?.Invoke(this, EventArgs.Empty);
        }

        RaiseLayoutChanged(LayoutChangeKind.Initialized);
    }

    /// <inheritdoc/>
    public MarinaLayout GetLayout() => new()
    {
        Name = MarinaName,
        Docks = OrderedDocks().ToArray(),
        Slips = OrderedSlips().ToArray(),
        Dividers = OrderedDividers().ToArray(),
        MultiSlipBerths = OrderedBerths().ToArray(),
        LandAreas = OrderedLandAreas().ToArray(),
    };

    /// <inheritdoc/>
    public void ClearLayout()
    {
        var previous = SelectedSlips;
        ClosePopup();
        ClearState();
        RegisterLandMeshes();
        RebuildBuiltInPresets();
        MarkSceneDirty();
        if (previous.Count > 0)
        {
            SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(previous, Array.Empty<Slip>()));
            SelectionCleared?.Invoke(this, EventArgs.Empty);
        }

        RaiseLayoutChanged(LayoutChangeKind.Cleared);
    }

    // ---- Docks ----------------------------------------------------------------------------------

    /// <inheritdoc/>
    public void AddDock(Dock dock)
    {
        ArgumentNullException.ThrowIfNull(dock);
        ThrowIfInvalid(dock.Validate());
        if (_docks.ContainsKey(dock.Id)) throw new InvalidOperationException($"Dock '{dock.Id}' already exists.");

        _docks[dock.Id] = dock;
        _dockOrder.Add(dock.Id);
        RebuildBuiltInPresets();
        MarkSceneDirty();
        RaiseLayoutChanged(LayoutChangeKind.DockAdded, dock.Id);
    }

    /// <inheritdoc/>
    public void UpdateDock(Dock dock)
    {
        ArgumentNullException.ThrowIfNull(dock);
        ThrowIfInvalid(dock.Validate());
        if (!_docks.TryGetValue(dock.Id, out var existing)) throw new KeyNotFoundException($"Dock '{dock.Id}' does not exist.");

        _docks[existing.Id] = dock with { Id = existing.Id };
        RebuildBuiltInPresets();
        MarkSceneDirty();
        RefreshPopupIfShowingDock(existing.Id);
        RaiseLayoutChanged(LayoutChangeKind.DockUpdated, existing.Id);
    }

    /// <inheritdoc/>
    public Dock UpdateDock(DockUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var existing = GetDock(update.DockId) ?? throw new KeyNotFoundException($"Dock '{update.DockId}' does not exist.");
        var updated = update.ApplyTo(existing);
        UpdateDock(updated);
        return _docks[existing.Id];
    }

    /// <inheritdoc/>
    public bool RemoveDock(string dockId, bool removeSlips = true)
    {
        ArgumentNullException.ThrowIfNull(dockId);
        if (!_docks.TryGetValue(dockId, out var dock)) return false;

        var slipIds = _slipOrder.Where(id => IdComparer.Equals(_slips[id].DockId, dock.Id)).ToList();
        if (slipIds.Count > 0 && !removeSlips)
        {
            throw new InvalidOperationException($"Dock '{dockId}' still has {slipIds.Count} slip(s).");
        }

        using (BeginUpdate())
        {
            foreach (var slipId in slipIds) RemoveSlip(slipId);
            foreach (var dividerId in _dividerOrder.Where(id => IdComparer.Equals(_dividers[id].DockId, dock.Id)).ToList()) RemoveDivider(dividerId);
            _docks.Remove(dock.Id);
            _dockOrder.RemoveAll(id => IdComparer.Equals(id, dock.Id));
            RebuildBuiltInPresets();
            MarkSceneDirty();
            RaiseLayoutChanged(LayoutChangeKind.DockRemoved, dock.Id);
        }

        return true;
    }

    /// <inheritdoc/>
    public Dock? GetDock(string dockId) => dockId is not null && _docks.TryGetValue(dockId, out var dock) ? dock : null;

    /// <inheritdoc/>
    public IReadOnlyList<Dock> GetDocks() => OrderedDocks().ToArray();

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
        RaiseLayoutChanged(LayoutChangeKind.DividerAdded, divider.DockId, dividerId: divider.Id);
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
        RaiseLayoutChanged(LayoutChangeKind.DividerUpdated, divider.DockId, dividerId: existing.Id);
    }

    /// <inheritdoc/>
    public bool RemoveDivider(string dividerId)
    {
        ArgumentNullException.ThrowIfNull(dividerId);
        if (!_dividers.TryGetValue(dividerId, out var divider)) return false;

        _dividers.Remove(divider.Id);
        _dividerOrder.RemoveAll(id => IdComparer.Equals(id, divider.Id));
        MarkSceneDirty();
        RaiseLayoutChanged(LayoutChangeKind.DividerRemoved, divider.DockId, dividerId: divider.Id);
        return true;
    }

    /// <inheritdoc/>
    public Divider? GetDivider(string dividerId) => dividerId is not null && _dividers.TryGetValue(dividerId, out var divider) ? divider : null;

    /// <inheritdoc/>
    public IReadOnlyList<Divider> GetDividers() => OrderedDividers().ToArray();

    /// <inheritdoc/>
    public IReadOnlyList<Divider> GetDividersByDock(string dockId) =>
        OrderedDividers().Where(d => IdComparer.Equals(d.DockId, dockId)).ToArray();

    // ---- Slips ----------------------------------------------------------------------------------

    /// <inheritdoc/>
    public void AddSlip(Slip slip)
    {
        ArgumentNullException.ThrowIfNull(slip);
        ValidateSlipForStorage(slip);
        if (_slips.ContainsKey(slip.Id)) throw new InvalidOperationException($"Slip '{slip.Id}' already exists.");

        _slips[slip.Id] = Normalize(slip with { BerthId = null });
        _slipOrder.Add(slip.Id);
        MarkSceneDirty();
        RaiseLayoutChanged(LayoutChangeKind.SlipAdded, slip.DockId, slip.Id);
    }

    /// <inheritdoc/>
    public Slip AddSlip(string slipId, string dockId, System.Numerics.Vector2 center, float headingDegrees, float length, float width, string? label = null)
    {
        var slip = new Slip(slipId, dockId, center, headingDegrees, length, width) { Label = label };
        AddSlip(slip);
        return _slips[slip.Id];
    }

    /// <inheritdoc/>
    public void AddSlips(IEnumerable<Slip> slips)
    {
        ArgumentNullException.ThrowIfNull(slips);
        using (BeginUpdate())
        {
            foreach (var slip in slips) AddSlip(slip);
        }
    }

    /// <inheritdoc/>
    public void UpdateSlip(Slip slip)
    {
        ArgumentNullException.ThrowIfNull(slip);
        var existing = RequireSlip(slip.Id);
        ReplaceSlipRoutingBerth(existing, slip);
    }

    /// <inheritdoc/>
    public Slip UpdateSlip(SlipUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var existing = RequireSlip(update.SlipId);
        var replacement = update.ApplyTo(existing);
        if (update.ExternalData is not null)
        {
            foreach (var (key, value) in update.ExternalData) existing.ExternalData[key] = value;
        }

        return ReplaceSlipRoutingBerth(existing, replacement);
    }

    /// <inheritdoc/>
    public bool RemoveSlip(string slipId)
    {
        ArgumentNullException.ThrowIfNull(slipId);
        if (!_slips.TryGetValue(slipId, out var slip)) return false;

        using (BeginUpdate())
        {
            RemoveFromSelectionCore(slip.Id);
            if (IdComparer.Equals(_hoveredSlipId, slip.Id)) SetHoveredSlip(null);
            if (slip.BerthId is not null) DetachFromBerth(slip);

            _slips.Remove(slip.Id);
            _slipOrder.RemoveAll(id => IdComparer.Equals(id, slip.Id));
            MarkSceneDirty();
            RaiseLayoutChanged(LayoutChangeKind.SlipRemoved, slip.DockId, slip.Id);
        }

        return true;
    }

    /// <inheritdoc/>
    public Slip? GetSlip(string slipId) => slipId is not null && _slips.TryGetValue(slipId, out var slip) ? slip : null;

    /// <inheritdoc/>
    public IReadOnlyList<Slip> GetSlips() => OrderedSlips().ToArray();

    /// <inheritdoc/>
    public IReadOnlyList<Slip> GetSlipsByDock(string dockId) =>
        OrderedSlips().Where(s => IdComparer.Equals(s.DockId, dockId)).ToArray();

    /// <inheritdoc/>
    public IReadOnlyList<Slip> GetSlipsByLandArea(string landAreaId) =>
        OrderedSlips().Where(s => IdComparer.Equals(s.LandAreaId, landAreaId)).ToArray();

    // ---- Land -----------------------------------------------------------------------------------

    /// <inheritdoc/>
    public LandArea? GetLandArea(string landAreaId) => landAreaId is not null && _landAreas.TryGetValue(landAreaId, out var land) ? land : null;

    /// <inheritdoc/>
    public IReadOnlyList<LandArea> GetLandAreas() => OrderedLandAreas().ToArray();

    // ---- Slips (continued) ----------------------------------------------------------------------

    /// <inheritdoc/>
    public IReadOnlyList<Slip> GetSlipsByStatus(SlipStatus status) =>
        OrderedSlips().Where(s => s.Status == status).ToArray();

    /// <inheritdoc/>
    public BatchUpdateResult BatchUpdate(IEnumerable<SlipUpdate> updates)
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
                    UpdateSlip(update);
                    applied++;
                }
                catch (Exception ex) when (ex is KeyNotFoundException or MarinaLayoutException or ArgumentException or InvalidOperationException)
                {
                    errors.Add(new BatchUpdateError(update.SlipId ?? string.Empty, ex.Message));
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
    /// Replaces a slip. A status or boat change on a member of a multi-slip berth applies to the whole berth
    /// (or releases it, when the slip becomes Free or loses its boat).
    /// </summary>
    private Slip ReplaceSlipRoutingBerth(Slip existing, Slip replacement)
    {
        if (existing.BerthId is not { } berthId || !_berths.TryGetValue(berthId, out var berth) ||
            (replacement.Status == existing.Status && replacement.Boat == existing.Boat))
        {
            return ReplaceSlip(existing, replacement);
        }

        ValidateSlipForStorage(replacement);
        using (BeginUpdate())
        {
            if (!replacement.Status.CanHaveBoat() || replacement.Boat is null)
            {
                ReleaseMultiSlipBerth(berth.Id);
                return ReplaceSlip(_slips[existing.Id], replacement with { BerthId = null });
            }

            var updated = UpdateMultiSlipBerth(berth with { Status = replacement.Status, Boat = replacement.Boat });
            return ReplaceSlip(_slips[existing.Id], replacement with { Status = updated.Status, Boat = updated.Boat, BerthId = updated.Id });
        }
    }

    /// <summary>Stores a new definition for an existing slip, keeping selection, hover and events consistent.</summary>
    /// <param name="existing">The currently stored slip.</param>
    /// <param name="replacement">The new definition (same id).</param>
    /// <param name="keepBerthId">When false, the replacement's <see cref="Slip.BerthId"/> is used as-is (berth management).</param>
    private Slip ReplaceSlip(Slip existing, Slip replacement, bool keepBerthId = true)
    {
        if (!IdComparer.Equals(existing.Id, replacement.Id))
        {
            throw new InvalidOperationException("A slip's id cannot be changed; remove it and add a new slip instead.");
        }

        ValidateSlipForStorage(replacement);

        if (!ReferenceEquals(existing.ExternalData, replacement.ExternalData))
        {
            foreach (var (key, value) in replacement.ExternalData) existing.ExternalData[key] = value;
        }

        var normalized = Normalize(replacement with
        {
            Id = existing.Id,
            ExternalData = existing.ExternalData,
            BerthId = keepBerthId && replacement.BerthId is null ? existing.BerthId : replacement.BerthId,
        });
        _slips[existing.Id] = normalized;
        MarkSceneDirty();

        if (IsSlipSelected(existing.Id))
        {
            if (!IsSelectable(normalized)) RemoveFromSelectionCore(existing.Id);
            else RequestPopupRefresh();
        }

        if (IdComparer.Equals(_hoveredSlipId, existing.Id) && !(normalized.IsInteractive && _statusFilter.Includes(normalized.Status)))
        {
            SetHoveredSlip(null);
        }

        if (existing.Status != normalized.Status || existing.Boat != normalized.Boat)
        {
            SlipStatusChanged?.Invoke(this, new SlipStatusChangedEventArgs(existing, normalized));
        }

        RaiseLayoutChanged(LayoutChangeKind.SlipUpdated, normalized.DockId, normalized.Id);
        return normalized;
    }

    private void ValidateSlipForStorage(Slip slip)
    {
        var errors = slip.Validate().ToList();
        if (!string.IsNullOrWhiteSpace(slip.DockId) && !_docks.ContainsKey(slip.DockId))
        {
            errors.Add($"Slip '{slip.Id}' references unknown dock '{slip.DockId}'.");
        }

        if (!string.IsNullOrWhiteSpace(slip.LandAreaId) && !_landAreas.ContainsKey(slip.LandAreaId))
        {
            errors.Add($"Slip '{slip.Id}' references unknown land area '{slip.LandAreaId}'.");
        }

        ThrowIfInvalid(errors);
    }

    private void ValidateDividerForStorage(Divider divider)
    {
        var errors = divider.Validate().ToList();
        if (divider.DockId is not null && !_docks.ContainsKey(divider.DockId))
        {
            errors.Add($"Divider '{divider.Id}' references unknown dock '{divider.DockId}'.");
        }

        ThrowIfInvalid(errors);
    }

    private static void ThrowIfInvalid(IEnumerable<string> errors)
    {
        var list = errors as IReadOnlyList<string> ?? errors.ToList();
        if (list.Count > 0) throw new MarinaLayoutException(list);
    }

    /// <summary>Enforces invariants: a free slip never carries a boat.</summary>
    private static Slip Normalize(Slip slip) =>
        slip.Status == SlipStatus.Free && slip.Boat is not null ? slip with { Boat = null } : slip;

    private void ClearState()
    {
        _docks.Clear();
        _dockOrder.Clear();
        _slips.Clear();
        _slipOrder.Clear();
        _dividers.Clear();
        _dividerOrder.Clear();
        _berths.Clear();
        _berthOrder.Clear();
        _landAreas.Clear();
        _landOrder.Clear();
        _selection.Clear();
        _hoveredSlipId = null;
        _popupRefreshPending = false;
    }
}
