using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Api;

public sealed partial class MarinaVisualizer
{
    /// <inheritdoc/>
    public Berth SetBerthStatus(string berthId, BerthStatus status, Boat? boat = null)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status), status, null);
        var update = new BerthUpdate(berthId)
        {
            Status = status,
            Boat = status == BerthStatus.Free ? null : boat,
            ClearBoat = status == BerthStatus.Free,
        };
        return UpdateBerth(update);
    }

    /// <inheritdoc/>
    public Berth AssignBoat(string berthId, Boat boat)
    {
        ArgumentNullException.ThrowIfNull(boat);
        return UpdateBerth(BerthUpdate.Occupy(berthId, boat));
    }

    /// <inheritdoc/>
    public Berth ReserveBerth(string berthId, Boat? expectedBoat = null) =>
        UpdateBerth(BerthUpdate.Reserve(berthId, expectedBoat));

    /// <inheritdoc/>
    public Berth ReleaseBerth(string berthId) => UpdateBerth(BerthUpdate.Free(berthId));

    /// <summary>
    /// Marks the berth Temporarily Free (yellow): the berth holder's boat is away. The assigned boat is kept and drawn as a ghost;
    /// pass <paramref name="boat"/> to set or replace it.
    /// </summary>
    public Berth MarkTemporarilyFree(string berthId, Boat? boat = null) =>
        UpdateBerth(BerthUpdate.TemporarilyFree(berthId, boat));

    // ---- Interaction flags ----------------------------------------------------------------------

    /// <summary>Hidden berths are not drawn at all and cannot be interacted with.</summary>
    public Berth SetBerthVisible(string berthId, bool visible) => UpdateBerth(BerthUpdate.Flags(berthId, visible: visible));

    /// <summary>Disabled berths are drawn in gray and cannot be hovered, selected, right-clicked or acted on.</summary>
    public Berth SetBerthDisabled(string berthId, bool disabled) => UpdateBerth(BerthUpdate.Flags(berthId, disabled: disabled));

    /// <summary>Read-only berths can be selected and show their tooltip, but their actions window does not open.</summary>
    public Berth SetBerthReadOnly(string berthId, bool readOnly) => UpdateBerth(BerthUpdate.Flags(berthId, readOnly: readOnly));

    /// <summary>Sets interaction flags on many berths with a single scene rebuild; null leaves a flag unchanged.</summary>
    public BatchUpdateResult SetBerthFlags(IEnumerable<string> berthIds, bool? visible = null, bool? disabled = null, bool? readOnly = null)
    {
        ArgumentNullException.ThrowIfNull(berthIds);
        return BatchUpdate(berthIds.Select(id => BerthUpdate.Flags(id, visible, disabled, readOnly)));
    }

    /// <inheritdoc/>
    public MarinaStatistics GetStatistics()
    {
        int free = 0, occupied = 0, reserved = 0, temporarilyFree = 0;
        foreach (var berth in _berths.Values)
        {
            switch (berth.Status)
            {
                case BerthStatus.Free: free++; break;
                case BerthStatus.Occupied: occupied++; break;
                case BerthStatus.Reserved: reserved++; break;
                case BerthStatus.TemporarilyFree: temporarilyFree++; break;
            }
        }

        return new MarinaStatistics(_berths.Count, free, occupied, reserved, temporarilyFree);
    }

    /// <inheritdoc/>
    public BerthStatusFilter StatusFilter => _statusFilter;

    /// <summary>Shows only berths whose status is in <paramref name="filter"/>. Filtered-out berths cannot be clicked or selected.</summary>
    public void SetStatusFilter(BerthStatusFilter filter)
    {
        filter &= BerthStatusFilter.All;
        if (filter == _statusFilter) return;
        _statusFilter = filter;

        var hidden = _selection.Where(id => _berths.TryGetValue(id, out var s) && !filter.Includes(s.Status)).ToArray();
        if (hidden.Length > 0) RemoveFromSelectionCore(hidden);
        if (HoveredBerth is { } hovered && !filter.Includes(hovered.Status)) SetHoveredBerth(null);
        InvalidatePickSet();
        MarkBerthsDirty();
    }

    /// <summary>Shows only berths with one of the given statuses, e.g. <c>SetStatusFilter(BerthStatus.Free, BerthStatus.TemporarilyFree)</c>.</summary>
    /// <param name="visibleStatuses">Statuses to show.</param>
    public void SetStatusFilter(params BerthStatus[] visibleStatuses) =>
        SetStatusFilter(BerthStatusExtensions.FromStatuses(visibleStatuses));

    /// <inheritdoc/>
    public void ShowAllStatuses() => SetStatusFilter(BerthStatusFilter.All);

    /// <summary>True when the berth exists, is not hidden, and passes the status filter.</summary>
    public bool IsBerthVisible(string berthId) => GetBerth(berthId) is { } berth && IsShown(berth);

    /// <inheritdoc/>
    public void SetStatusColor(BerthStatus status, ColorRgba color)
    {
        _colors.Set(status, color);
    }

    /// <inheritdoc/>
    public ColorRgba GetStatusColor(BerthStatus status) => _colors.Get(status);

    /// <summary>Changes the pad and buoy color used for disabled berths (boats of disabled berths are always desaturated).</summary>
    /// <param name="color">New color.</param>
    public void SetDisabledColor(ColorRgba color)
    {
        _colors.DisabledColor = color;
    }

    /// <summary>Sets the opacity of status pads and of reserved/temporarily free "ghost" boats. Values are clamped to 0.05–1.</summary>
    /// <param name="padOpacity">Opacity of the colored pads on the water (default 0.45).</param>
    /// <param name="ghostBoatOpacity">Opacity of ghost boats (default 0.4).</param>
    public void SetOverlayOpacity(float padOpacity, float ghostBoatOpacity)
    {
        _colors.PadOpacity = Math.Clamp(padOpacity, 0.05f, 1f);
        _colors.GhostBoatOpacity = Math.Clamp(ghostBoatOpacity, 0.05f, 1f);
    }

    /// <inheritdoc/>
    public void ResetStatusColors()
    {
        _colors.Reset();
    }
}
