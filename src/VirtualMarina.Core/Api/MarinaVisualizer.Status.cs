using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Api;

public sealed partial class MarinaVisualizer
{
    /// <inheritdoc/>
    public Slip SetSlipStatus(string slipId, SlipStatus status, Boat? boat = null)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status), status, null);
        var update = new SlipUpdate(slipId)
        {
            Status = status,
            Boat = status == SlipStatus.Free ? null : boat,
            ClearBoat = status == SlipStatus.Free,
        };
        return UpdateSlip(update);
    }

    /// <inheritdoc/>
    public Slip AssignBoat(string slipId, Boat boat)
    {
        ArgumentNullException.ThrowIfNull(boat);
        return UpdateSlip(SlipUpdate.Occupy(slipId, boat));
    }

    /// <inheritdoc/>
    public Slip ReserveSlip(string slipId, Boat? expectedBoat = null) =>
        UpdateSlip(SlipUpdate.Reserve(slipId, expectedBoat));

    /// <inheritdoc/>
    public Slip ReleaseSlip(string slipId) => UpdateSlip(SlipUpdate.Free(slipId));

    /// <summary>
    /// Marks the slip Temporarily Free (yellow): the berth holder's boat is away. The assigned boat is kept and drawn as a ghost;
    /// pass <paramref name="boat"/> to set or replace it.
    /// </summary>
    public Slip MarkTemporarilyFree(string slipId, Boat? boat = null) =>
        UpdateSlip(SlipUpdate.TemporarilyFree(slipId, boat));

    // ---- Interaction flags ----------------------------------------------------------------------

    /// <summary>Hidden slips are not drawn at all and cannot be interacted with.</summary>
    public Slip SetSlipVisible(string slipId, bool visible) => UpdateSlip(SlipUpdate.Flags(slipId, visible: visible));

    /// <summary>Disabled slips are drawn in gray and cannot be hovered, selected, right-clicked or acted on.</summary>
    public Slip SetSlipDisabled(string slipId, bool disabled) => UpdateSlip(SlipUpdate.Flags(slipId, disabled: disabled));

    /// <summary>Read-only slips can be selected and show their tooltip, but their actions window does not open.</summary>
    public Slip SetSlipReadOnly(string slipId, bool readOnly) => UpdateSlip(SlipUpdate.Flags(slipId, readOnly: readOnly));

    /// <summary>Sets interaction flags on many slips with a single scene rebuild; null leaves a flag unchanged.</summary>
    public BatchUpdateResult SetSlipFlags(IEnumerable<string> slipIds, bool? visible = null, bool? disabled = null, bool? readOnly = null)
    {
        ArgumentNullException.ThrowIfNull(slipIds);
        return BatchUpdate(slipIds.Select(id => SlipUpdate.Flags(id, visible, disabled, readOnly)));
    }

    /// <inheritdoc/>
    public MarinaStatistics GetStatistics()
    {
        int free = 0, occupied = 0, reserved = 0, temporarilyFree = 0;
        foreach (var slip in _slips.Values)
        {
            switch (slip.Status)
            {
                case SlipStatus.Free: free++; break;
                case SlipStatus.Occupied: occupied++; break;
                case SlipStatus.Reserved: reserved++; break;
                case SlipStatus.TemporarilyFree: temporarilyFree++; break;
            }
        }

        return new MarinaStatistics(_slips.Count, free, occupied, reserved, temporarilyFree);
    }

    /// <inheritdoc/>
    public SlipStatusFilter StatusFilter => _statusFilter;

    /// <summary>Shows only slips whose status is in <paramref name="filter"/>. Filtered-out slips cannot be clicked or selected.</summary>
    public void SetStatusFilter(SlipStatusFilter filter)
    {
        filter &= SlipStatusFilter.All;
        if (filter == _statusFilter) return;
        _statusFilter = filter;

        var hidden = _selection.Where(id => _slips.TryGetValue(id, out var s) && !filter.Includes(s.Status)).ToArray();
        if (hidden.Length > 0) RemoveFromSelectionCore(hidden);
        if (HoveredSlip is { } hovered && !filter.Includes(hovered.Status)) SetHoveredSlip(null);
        MarkSceneDirty();
    }

    /// <summary>Shows only slips with one of the given statuses, e.g. <c>SetStatusFilter(SlipStatus.Free, SlipStatus.TemporarilyFree)</c>.</summary>
    /// <param name="visibleStatuses">Statuses to show.</param>
    public void SetStatusFilter(params SlipStatus[] visibleStatuses) =>
        SetStatusFilter(SlipStatusExtensions.FromStatuses(visibleStatuses));

    /// <inheritdoc/>
    public void ShowAllStatuses() => SetStatusFilter(SlipStatusFilter.All);

    /// <summary>True when the slip exists, is not hidden, and passes the status filter.</summary>
    public bool IsSlipVisible(string slipId) => GetSlip(slipId) is { } slip && IsShown(slip);

    /// <inheritdoc/>
    public void SetStatusColor(SlipStatus status, ColorRgba color)
    {
        _colors.Set(status, color);
        MarkSceneDirty();
        RequestPopupRefresh();
    }

    /// <inheritdoc/>
    public ColorRgba GetStatusColor(SlipStatus status) => _colors.Get(status);

    /// <summary>Changes the pad and buoy color used for disabled slips (boats of disabled slips are always desaturated).</summary>
    /// <param name="color">New color.</param>
    public void SetDisabledColor(ColorRgba color)
    {
        _colors.DisabledColor = color;
        MarkSceneDirty();
    }

    /// <summary>Sets the opacity of status pads and of reserved/temporarily free "ghost" boats. Values are clamped to 0.05–1.</summary>
    /// <param name="padOpacity">Opacity of the colored pads on the water (default 0.45).</param>
    /// <param name="ghostBoatOpacity">Opacity of ghost boats (default 0.4).</param>
    public void SetOverlayOpacity(float padOpacity, float ghostBoatOpacity)
    {
        _colors.PadOpacity = Math.Clamp(padOpacity, 0.05f, 1f);
        _colors.GhostBoatOpacity = Math.Clamp(ghostBoatOpacity, 0.05f, 1f);
        MarkSceneDirty();
    }

    /// <inheritdoc/>
    public void ResetStatusColors()
    {
        _colors.Reset();
        MarkSceneDirty();
        RequestPopupRefresh();
    }
}
