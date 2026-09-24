using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Resources;

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
        var wasHovering = _hoveredBerthId is not null;
        ClosePopup();
        ClearState();

        MarinaName = layout.Name;
        _shoreline = layout.Shoreline;
        _traffic = layout.MarineTraffic ?? MarineTraffic.None;
        foreach (var land in layout.LandAreas) _landAreas[land.Id] = land;
        foreach (var pier in layout.Piers) _piers[pier.Id] = pier;
        foreach (var berth in layout.Berths) _berths[berth.Id] = Normalize(berth with { MultiBerthId = null });
        foreach (var divider in layout.Dividers) _dividers[divider.Id] = divider;

        foreach (var berth in layout.MultiBerths)
        {
            var canonical = berth with { BerthIds = berth.BerthIds.Select(id => _berths[id].Id).ToArray() };
            _multiBerths[berth.Id] = canonical;
            foreach (var berthId in canonical.BerthIds)
            {
                _berths[berthId] = _berths[berthId] with { Status = canonical.Status, Boat = canonical.Boat, MultiBerthId = canonical.Id };
            }
        }

        RebuildLandMeshes();

        var (min, max) = layout.ComputeBounds();
        SetWaterGrid((min + max) * 0.5f, Water.Size);
        InvalidateLayoutGeometry();
        ResetCamera(immediate: true);
        MarkSceneDirty();

        if (previousSelection.Count > 0)
        {
            SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(previousSelection, Array.Empty<Berth>()));
            SelectionCleared?.Invoke(this, EventArgs.Empty);
        }

        if (wasHovering) BerthHoverChanged?.Invoke(this, new BerthHoverEventArgs(null));
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
        Shoreline = _shoreline,
        MarineTraffic = _traffic,
    };

    /// <inheritdoc/>
    public void ClearLayout()
    {
        var previous = SelectedBerths;
        var wasHovering = _hoveredBerthId is not null;
        ClosePopup();
        ClearState();
        RebuildLandMeshes();
        InvalidateLayoutGeometry();
        MarkSceneDirty();
        if (previous.Count > 0)
        {
            SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(previous, Array.Empty<Berth>()));
            SelectionCleared?.Invoke(this, EventArgs.Empty);
        }

        if (wasHovering) BerthHoverChanged?.Invoke(this, new BerthHoverEventArgs(null));
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
        InvalidateLayoutGeometry();
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
        InvalidateLayoutGeometry();
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

        var berthIds = BerthsOfPier(pier.Id).Select(berth => berth.Id).ToList();
        if (berthIds.Count > 0 && !removeBerths)
        {
            throw new InvalidOperationException($"Pier '{pierId}' still has {berthIds.Count} berth(s).");
        }

        using (BeginUpdate())
        {
            foreach (var berthId in berthIds) RemoveBerth(berthId);
            foreach (var dividerId in _dividers.WithKey(ByPier, pier.Id).Select(divider => divider.Id).ToList()) RemoveDivider(dividerId);
            _piers.Remove(pier.Id);
            InvalidateLayoutGeometry();
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
        InvalidateLayoutGeometry();
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
        InvalidateLayoutGeometry();
        MarkSceneDirty();
        RaiseLayoutChanged(LayoutChangeKind.DividerUpdated, divider.PierId, dividerId: existing.Id);
    }

    /// <inheritdoc/>
    public bool RemoveDivider(string dividerId)
    {
        ArgumentNullException.ThrowIfNull(dividerId);
        if (!_dividers.TryGetValue(dividerId, out var divider)) return false;

        _dividers.Remove(divider.Id);
        InvalidateLayoutGeometry();
        MarkSceneDirty();
        RaiseLayoutChanged(LayoutChangeKind.DividerRemoved, divider.PierId, dividerId: divider.Id);
        return true;
    }

    /// <inheritdoc/>
    public Divider? GetDivider(string dividerId) => dividerId is not null && _dividers.TryGetValue(dividerId, out var divider) ? divider : null;

    /// <inheritdoc/>
    public IReadOnlyList<Divider> GetDividers() => OrderedDividers().ToArray();

    /// <inheritdoc/>
    public IReadOnlyList<Divider> GetDividersByPier(string pierId) => _dividers.WithKey(ByPier, pierId).ToArray();

    // ---- Berths ----------------------------------------------------------------------------------

    /// <inheritdoc/>
    public void AddBerth(Berth berth)
    {
        ArgumentNullException.ThrowIfNull(berth);
        ValidateBerthForStorage(berth);
        if (_berths.ContainsKey(berth.Id)) throw new InvalidOperationException($"Berth '{berth.Id}' already exists.");
        if (_multiBerths.ContainsKey(berth.Id))
        {
            throw new MarinaLayoutException(new[] { Strings.Format(Strings.ErrorBerthIdIsMultiBerthId, berth.Id) });
        }

        _berths[berth.Id] = Normalize(berth with { MultiBerthId = null });
        InvalidateLayoutGeometry();
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

        // Checked before the data bag is touched: the bag is shared with the live berth, so a rejected update must not leave
        // half of itself behind in it.
        ValidateBerthForStorage(replacement);
        if (update.ExternalDataRemovals is not null)
        {
            foreach (var key in update.ExternalDataRemovals)
            {
                if (key is not null) existing.ExternalData.Remove(key);
            }
        }

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
            InvalidateLayoutGeometry();
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
            // Keeps its place among the piers.
            _piers.Rekey([(existing.Id, renamed.Id, renamed)]);

            // Everything that pointed at the old id now points at the new one.
            foreach (var berth in BerthsOfPier(existing.Id).ToArray()) _berths[berth.Id] = berth with { PierId = renamed.Id };
            foreach (var divider in _dividers.WithKey(ByPier, existing.Id).ToArray()) _dividers[divider.Id] = divider with { PierId = renamed.Id };
            _selectedSnapshot = null;

            // The pier's own view is keyed by its id: one that was switched off stays off under the new id.
            if (_disabledPresets.Remove(PierPresetKey(existing.Id))) _disabledPresets.Add(PierPresetKey(renamed.Id));
            InvalidateLayoutGeometry();
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
    public IReadOnlyList<Berth> GetBerthsByPier(string pierId) => BerthsOfPier(pierId).ToArray();

    /// <inheritdoc/>
    public IReadOnlyList<Berth> GetBerthsByLandArea(string landAreaId) => BerthsOfLandArea(landAreaId).ToArray();

    // ---- Land -----------------------------------------------------------------------------------

    /// <inheritdoc/>
    public void AddLandArea(LandArea landArea)
    {
        ArgumentNullException.ThrowIfNull(landArea);
        ThrowIfInvalid(landArea.Validate());
        if (_landAreas.ContainsKey(landArea.Id)) throw new InvalidOperationException($"Land area '{landArea.Id}' already exists.");

        _landAreas[landArea.Id] = landArea;
        RegisterLandMesh(landArea);
        InvalidateLayoutGeometry();
        MarkSceneDirty();
        RaiseLayoutChanged(LayoutChangeKind.LandAreaAdded, landAreaId: landArea.Id);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Planting trees, renaming or re-tagging a land area leaves its outline alone, so the outline is not checked
    /// again (a check that grows with the square of its points) and its ground is not rebuilt; only what stands on it
    /// is. The automatic views are only refitted when the outline moved.
    /// </remarks>
    public void UpdateLandArea(LandArea landArea)
    {
        ArgumentNullException.ThrowIfNull(landArea);
        var existing = GetLandArea(landArea.Id);
        var sameOutline = existing is not null && existing.Points.SequenceEqual(landArea.Points);
        ThrowIfInvalid(landArea.Validate(checkOutline: !sameOutline));
        if (existing is null) throw new KeyNotFoundException($"Land area '{landArea.Id}' does not exist.");

        var updated = landArea with { Id = existing.Id };
        _landAreas[existing.Id] = updated;
        var sameGround = sameOutline && existing.Height == updated.Height && existing.Kind == updated.Kind;
        RegisterLandMesh(updated, rebuildGround: !sameGround);
        if (!sameOutline) InvalidateLayoutGeometry();
        MarkSceneDirty();
        RefreshPopupIfShowingLand(existing.Id);
        RaiseLayoutChanged(LayoutChangeKind.LandAreaUpdated, landAreaId: existing.Id);
    }

    /// <inheritdoc/>
    public bool RemoveLandArea(string landAreaId, bool removeBerths = true)
    {
        ArgumentNullException.ThrowIfNull(landAreaId);
        if (!_landAreas.TryGetValue(landAreaId, out var land)) return false;

        var berthIds = BerthsOfLandArea(land.Id).Select(berth => berth.Id).ToList();
        if (berthIds.Count > 0 && !removeBerths)
        {
            throw new InvalidOperationException($"Land area '{landAreaId}' still has {berthIds.Count} berth(s).");
        }

        // Removing land berths too is coalesced into one BatchUpdated notification.
        using (berthIds.Count > 0 ? BeginUpdate() : null)
        {
            foreach (var berthId in berthIds) RemoveBerth(berthId);
            _landAreas.Remove(land.Id);
            UnregisterLandMesh(land.Id);
            InvalidateLayoutGeometry();
            MarkSceneDirty();
            RaiseLayoutChanged(LayoutChangeKind.LandAreaRemoved, landAreaId: land.Id);
        }

        return true;
    }

    // ---- The mainland ---------------------------------------------------------------------------

    /// <inheritdoc/>
    public Shoreline? Shoreline => _shoreline;

    /// <inheritdoc/>
    public void SetShoreline(Shoreline? shoreline)
    {
        if (shoreline is not null) ThrowIfInvalid(shoreline.Validate());

        _shoreline = shoreline;
        RegisterShorelineMesh();
        MarkSceneDirty();
        RaiseLayoutChanged(LayoutChangeKind.ShorelineChanged);
    }

    /// <inheritdoc/>
    public bool RemoveShoreline()
    {
        if (_shoreline is null) return false;
        SetShoreline(null);
        return true;
    }

    // ---- Passing traffic ------------------------------------------------------------------------

    /// <inheritdoc/>
    public MarineTraffic MarineTraffic => _traffic;

    /// <inheritdoc/>
    public void SetMarineTraffic(MarineTraffic? traffic)
    {
        var wanted = traffic ?? MarineTraffic.None;
        ThrowIfInvalid(wanted.Validate());

        _traffic = wanted;
        ReplanTraffic();
        MarkOverlayDirty();
        RaiseLayoutChanged(LayoutChangeKind.MarineTrafficChanged);
    }

    /// <inheritdoc/>
    public IReadOnlyList<TrafficVessel> GetTrafficVessels()
    {
        // The lanes are planned around the marina, so a host that asks before the next frame is drawn would
        // otherwise get vessels laid out around whatever the marina was when the traffic was last set.
        if (_trafficDirty && (_traffic.IsEnabled || _trafficField is not null)) ReplanTraffic();
        AdvanceTraffic();
        return _trafficField?.Vessels.ToArray() ?? Array.Empty<TrafficVessel>();
    }

    /// <summary>Moves the traffic on to the current moment, once per moment however often it is asked for.</summary>
    private void AdvanceTraffic()
    {
        if (_trafficField is null) return;
        var elapsed = _time - _trafficTime;
        _trafficTime = _time;
        _trafficField.Advance(elapsed);
    }

    /// <inheritdoc/>
    public IReadOnlyList<TrafficLane> TrafficLanes => _trafficField?.Lanes ?? Array.Empty<TrafficLane>();

    /// <inheritdoc/>
    public bool ShowTrafficLanes
    {
        get => _showTrafficLanes;
        set
        {
            if (_showTrafficLanes == value) return;
            _showTrafficLanes = value;
            MarkOverlayDirty();
        }
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

        if (_pendingChanges.Count > 0)
        {
            var changes = _pendingChanges.ToArray();
            _pendingChanges.Clear();
            LayoutChanged?.Invoke(this, new LayoutChangedEventArgs(changes));
        }

        FlushPopupRefresh();
        FlushPresetsChanged();
    }

    /// <summary>
    /// Replaces a berth. A status or boat change on a member of a multi-berth applies to the whole multi-berth
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
    private Berth ReplaceBerthId(Berth existing, string newBerthId) => MoveBerthIds([(existing, newBerthId)])[0];

    /// <summary>
    /// Renames several berths as one change: every berth takes its new name at once, so names can be shuffled among them
    /// (<c>A-01</c> to <c>A-02</c> while <c>A-02</c> goes to <c>A-03</c>, or two berths swapping) without one landing on
    /// a name another is still using. Returns the renamed berths, in the order asked, skipping any whose name did not change.
    /// </summary>
    /// <param name="renames">(current id, new id) pairs. New ids are trimmed.</param>
    /// <remarks>
    /// The whole set is checked before anything moves, so a rename that cannot happen leaves every berth as it was. One
    /// <see cref="LayoutChangeKind.BerthRenamed"/> is raised per berth that changed name, and no berth is ever seen under a
    /// name it was only passing through.
    /// </remarks>
    /// <exception cref="KeyNotFoundException">A berth to rename does not exist.</exception>
    /// <exception cref="ArgumentException">A berth is named twice, or a new id is blank.</exception>
    /// <exception cref="InvalidOperationException">
    /// Two berths would end up with the same name, or a new name belongs to a berth that is not itself being renamed.
    /// </exception>
    internal IReadOnlyList<Berth> RenameBerths(IReadOnlyList<(string From, string To)> renames)
    {
        ArgumentNullException.ThrowIfNull(renames);
        var sources = new HashSet<string>(IdComparer);
        var moving = new List<(Berth Existing, string To)>(renames.Count);
        foreach (var (from, to) in renames)
        {
            if (string.IsNullOrWhiteSpace(to)) throw new ArgumentException("A berth cannot be renamed to a blank name.", nameof(renames));
            var existing = RequireBerth(from);
            if (!sources.Add(existing.Id)) throw new ArgumentException($"Berth '{existing.Id}' is renamed twice.", nameof(renames));
            var target = to.Trim();
            if (existing.Id != target) moving.Add((existing, target));
        }

        return moving.Count == 0 ? Array.Empty<Berth>() : MoveBerthIds(moving);
    }

    /// <summary>
    /// The one place berth ids change: checks that the new ids are free once the berths moving have left theirs, then
    /// moves every one at once and raises one <see cref="LayoutChangeKind.BerthRenamed"/> for each.
    /// </summary>
    private List<Berth> MoveBerthIds(List<(Berth Existing, string To)> moving)
    {
        var leaving = new HashSet<string>(moving.Select(entry => entry.Existing.Id), IdComparer);
        var arriving = new HashSet<string>(IdComparer);
        foreach (var (_, to) in moving)
        {
            if (!arriving.Add(to)) throw new InvalidOperationException($"Two berths cannot both be named '{to}'.");
            if (_berths.TryGetValue(to, out var holder) && !leaving.Contains(holder.Id)) throw new InvalidOperationException($"Berth '{to}' already exists.");
        }

        var renamed = new List<Berth>(moving.Count);
        var moves = new List<(string OldId, string NewId, Berth Item)>(moving.Count);
        var map = new Dictionary<string, string>(IdComparer);
        using (BeginUpdate())
        {
            foreach (var (existing, to) in moving)
            {
                // A label that merely repeated the old id is the id showing on the water, so it goes with the name. One the
                // host wrote is theirs and stays. Without this a renamed berth went on displaying the name it used to have.
                var label = existing.Label is null || string.Equals(existing.Label, existing.Id, StringComparison.Ordinal)
                    ? null
                    : existing.Label;
                var berth = Normalize(existing with { Id = to, Label = label });
                renamed.Add(berth);
                moves.Add((existing.Id, to, berth));
                map[existing.Id] = to;
            }

            // Every berth keeps its place in the order under its new name.
            _berths.Rekey(moves);
            RenameInSelection(map);

            if (_hoveredBerthId is not null && map.TryGetValue(_hoveredBerthId, out var hovered)) _hoveredBerthId = hovered;

            foreach (var group in _multiBerths.Values.ToArray())
            {
                if (!group.BerthIds.Any(map.ContainsKey)) continue;
                _multiBerths[group.Id] = group with { BerthIds = group.BerthIds.Select(id => map.GetValueOrDefault(id, id)).ToArray() };
            }

            MarkSceneDirty();
            InvalidatePickSet();
            if (renamed.Any(berth => IsBerthSelected(berth.Id))) RequestPopupRefresh();
        }

        // Outside the update scope, so listeners are told each berth was renamed rather than just "something changed".
        foreach (var berth in renamed) RaiseLayoutChanged(LayoutChangeKind.BerthRenamed, berth.PierId, berth.Id, landAreaId: berth.LandAreaId);
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
        FollowBerthChange(existing, normalized);

        if (existing.Status != normalized.Status || existing.Boat != normalized.Boat)
        {
            BerthStatusChanged?.Invoke(this, new BerthStatusChangedEventArgs(existing, normalized));
        }

        RaiseLayoutChanged(LayoutChangeKind.BerthUpdated, normalized.PierId, normalized.Id, landAreaId: normalized.LandAreaId);
        return normalized;
    }

    /// <summary>
    /// Brings everything that depends on a berth up to date after it was stored as <paramref name="after"/>: the scene, the
    /// automatic views, the selection and its popup, and the hover.
    /// </summary>
    private void FollowBerthChange(Berth before, Berth after)
    {
        if (IsBerthSelected(before.Id)) _selectedSnapshot = null;

        // A new status, boat or label changes only what the berths layer shows; the piers, fingers and pedestals stay.
        if (ShapesStructure(before, after)) MarkSceneDirty();
        else MarkBerthsDirty();

        // Moved, resized or given to another pier: the automatic views frame it somewhere else.
        if (before.Bounds != after.Bounds || !IdComparer.Equals(before.PierId, after.PierId) ||
            !IdComparer.Equals(before.LandAreaId, after.LandAreaId))
        {
            InvalidateLayoutGeometry();
        }

        if (IsBerthSelected(before.Id))
        {
            if (!IsSelectable(after)) RemoveFromSelectionCore(before.Id);
            else RequestPopupRefresh();
        }

        if (IdComparer.Equals(_hoveredBerthId, before.Id) && !(after.IsInteractive && _statusFilter.Includes(after.Status)))
        {
            SetHoveredBerth(null);
        }
    }

    private void ValidateBerthForStorage(Berth berth)
    {
        var errors = berth.Validate().ToList();
        if (!string.IsNullOrWhiteSpace(berth.PierId) && !_piers.ContainsKey(berth.PierId))
        {
            errors.Add(Strings.Format(Strings.ErrorBerthUnknownPier, berth.Id, berth.PierId));
        }

        if (!string.IsNullOrWhiteSpace(berth.LandAreaId) && !_landAreas.ContainsKey(berth.LandAreaId))
        {
            errors.Add(Strings.Format(Strings.ErrorBerthUnknownLandArea, berth.Id, berth.LandAreaId));
        }

        ThrowIfInvalid(errors);
    }

    private void ValidateDividerForStorage(Divider divider)
    {
        var errors = divider.Validate().ToList();
        if (divider.PierId is not null && !_piers.ContainsKey(divider.PierId))
        {
            errors.Add(Strings.Format(Strings.ErrorDividerUnknownPier, divider.Id, divider.PierId));
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
        _berths.Clear();
        _dividers.Clear();
        _multiBerths.Clear();
        _landAreas.Clear();
        _shoreline = null;
        _traffic = MarineTraffic.None;
        _trafficField = null;
        _selection.Clear();
        _selectedIds.Clear();
        _selectedSnapshot = null;
        _hoveredBerthId = null;
        _popupRefreshPending = false;
        Designer.ClearHistory(); // The old elements are gone; undoing into them would resurrect stale ids.
    }
}
