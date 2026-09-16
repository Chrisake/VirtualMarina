using System.Numerics;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;

namespace VirtualMarina.Core.Api;

public sealed partial class MarinaVisualizer
{
    /// <summary>Selected slip ids in selection order; the last one is the primary slip.</summary>
    private readonly List<string> _selection = new();

    private SlipPopup? _popup;
    private int _popupVersion;
    private bool _popupRefreshPending;
    private int _popupRefreshSuppression;

    // ---- Options --------------------------------------------------------------------------------

    /// <summary>Show a tooltip above the selection on left-click (default true).</summary>
    public bool TooltipsEnabled { get; set; } = true;

    /// <summary>Show the actions window on right-click (default true).</summary>
    public bool ActionsEnabled { get; set; } = true;

    /// <summary>Allow Ctrl+click or Shift+click to build a multi-selection (default true).</summary>
    public bool MultiSelectEnabled { get; set; } = true;

    /// <summary>Show the tooltip when host code selects slips through the API (default true).</summary>
    public bool ShowTooltipOnApiSelection { get; set; } = true;

    // ---- Selection ------------------------------------------------------------------------------

    /// <summary>The primary (most recently clicked) selected slip.</summary>
    public Slip? SelectedSlip => _selection.Count > 0 ? GetSlip(_selection[^1]) : null;

    /// <summary>All selected slips in selection order; the last one is <see cref="SelectedSlip"/>.</summary>
    public IReadOnlyList<Slip> SelectedSlips => _selection.Select(GetSlip).OfType<Slip>().ToArray();

    /// <summary>True when two or more slips are selected.</summary>
    public bool IsMultiSelection => _selection.Count > 1;

    /// <inheritdoc/>
    public bool IsSlipSelected(string slipId) => slipId is not null && _selection.Contains(slipId, IdComparer);

    /// <summary>
    /// Makes <paramref name="slipId"/> the only selected slip and raises <see cref="SlipSelected"/>.
    /// Returns false (and changes nothing) when the slip doesn't exist, is hidden, disabled or filtered out.
    /// </summary>
    public bool SelectSlip(string slipId, bool focusCamera = false)
    {
        var slip = GetSlip(slipId);
        if (slip is null || !IsSelectable(slip)) return false;

        SetSelection(new[] { slip.Id }, focusCamera);
        return true;
    }

    /// <summary>
    /// Replaces the selection with the selectable slips among <paramref name="slipIds"/> (the last one becomes primary)
    /// and raises <see cref="SlipSelected"/> or <see cref="MultiSlipSelected"/>. Returns the number of selected slips.
    /// </summary>
    public int SelectSlips(IEnumerable<string> slipIds) => SetSelection(slipIds).Count;

    /// <summary>Replaces the selection. See <see cref="SetSelection(IEnumerable{string}, bool, CameraAngle?)"/>.</summary>
    public SelectionResult SetSelection(params string[] slipIds) => SetSelection(slipIds, focusCamera: false);

    /// <summary>
    /// Replaces the selection with one or more slips. Disabled slips are discarded, as are hidden, filtered-out and unknown
    /// ids and duplicates; the result lists what was skipped and why. The remaining slips are selected in the given order (the
    /// last becomes primary) and <see cref="SlipSelected"/> or <see cref="MultiSlipSelected"/> is raised as for a click.
    /// When nothing remains the selection is cleared.
    /// </summary>
    /// <param name="slipIds">Slips to select.</param>
    /// <param name="focusCamera">Also frame all selected slips with <see cref="FocusSlips"/>.</param>
    /// <param name="focusAngle">Angle for the focus; null uses <see cref="DefaultFocusAngle"/>.</param>
    public SelectionResult SetSelection(IEnumerable<string> slipIds, bool focusCamera = false, CameraAngle? focusAngle = null)
    {
        ArgumentNullException.ThrowIfNull(slipIds);
        var selected = new List<string>();
        var rejected = new List<RejectedSlip>();
        foreach (var id in slipIds)
        {
            if (id is null) continue;
            if (GetSlip(id) is not { } slip)
            {
                rejected.Add(new RejectedSlip(id, SlipSelectionRejection.NotFound));
                continue;
            }

            if (slip.IsDisabled) rejected.Add(new RejectedSlip(id, SlipSelectionRejection.Disabled));
            else if (!slip.IsVisible) rejected.Add(new RejectedSlip(id, SlipSelectionRejection.Hidden));
            else if (!_statusFilter.Includes(slip.Status)) rejected.Add(new RejectedSlip(id, SlipSelectionRejection.FilteredOut));
            else if (!selected.Contains(slip.Id, IdComparer)) selected.Add(slip.Id);
        }

        var changed = SetSelectionCore(selected);
        if (changed && selected.Count > 0)
        {
            RaiseContentAndShowPopup(SelectionReason.Api, isNewSelection: true, PointerButton.None, ShowTooltipOnApiSelection ? SlipPopupKind.Tooltip : null);
        }

        if (focusCamera && _selection.Count > 0) FocusSlips(_selection, focusAngle);
        return new SelectionResult(_selection.ToArray(), rejected, changed);
    }

    /// <summary>Adds a slip to the selection (making it primary). Returns false when it can't be selected.</summary>
    public bool AddToSelection(string slipId)
    {
        var slip = GetSlip(slipId);
        if (slip is null || !IsSelectable(slip)) return false;
        var ids = _selection.Where(id => !IdComparer.Equals(id, slip.Id)).Append(slip.Id).ToArray();
        if (SetSelectionCore(ids))
        {
            RaiseContentAndShowPopup(SelectionReason.Api, isNewSelection: true, PointerButton.None, _popup?.Kind ?? (ShowTooltipOnApiSelection ? SlipPopupKind.Tooltip : null));
        }

        return true;
    }

    /// <inheritdoc/>
    public bool RemoveFromSelection(string slipId)
    {
        if (!IsSlipSelected(slipId)) return false;
        RemoveFromSelectionCore(slipId);
        return true;
    }

    /// <inheritdoc/>
    public void ClearSelection() => SetSelectionCore(Array.Empty<string>());

    // ---- Popup ----------------------------------------------------------------------------------

    /// <summary>The tooltip or actions window currently shown above the selection, or null.</summary>
    public SlipPopup? ActivePopup => _popup;

    /// <summary>Shows the tooltip for the current selection (raising the selection event again to collect content).</summary>
    public bool ShowTooltip() =>
        _selection.Count > 0 && RaiseContentAndShowPopup(SelectionReason.Api, isNewSelection: false, PointerButton.Left, SlipPopupKind.Tooltip);

    /// <summary>
    /// Shows the actions window for the current selection (raising the selection event with <see cref="PointerButton.Right"/>).
    /// Falls back to the tooltip when the selection is read-only or has no actions.
    /// </summary>
    public bool ShowActions() =>
        _selection.Count > 0 && RaiseContentAndShowPopup(SelectionReason.Api, isNewSelection: false, PointerButton.Right, SlipPopupKind.Actions);

    /// <summary>Rebuilds the open popup's content by raising the selection event with <see cref="SelectionReason.Refresh"/>.</summary>
    public void RefreshPopup()
    {
        if (_popup is null) return;
        _popupRefreshPending = true;
        FlushPopupRefresh();
    }

    /// <inheritdoc/>
    public void ClosePopup() => SetPopup(null);

    /// <summary>
    /// Invokes an action of the open actions window: raises <see cref="SlipActionInvoked"/> and closes the window
    /// (unless the action or handler keeps it open). Returns false when there is no such enabled action.
    /// </summary>
    public bool InvokeSlipAction(string actionId)
    {
        if (_popup is not { Kind: SlipPopupKind.Actions } popup || string.IsNullOrEmpty(actionId)) return false;

        var action = popup.Actions.FirstOrDefault(a => string.Equals(a.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
        if (action is null || !action.Enabled || !action.Visible) return false;

        var slips = popup.Slips.Select(s => GetSlip(s.Id)).OfType<Slip>().ToArray();
        if (!slips.Any(s => s.AllowsActions)) return false;

        var args = new SlipActionInvokedEventArgs(action, slips);
        _popupRefreshSuppression++;
        try
        {
            SlipActionInvoked?.Invoke(this, args);
        }
        finally
        {
            _popupRefreshSuppression--;
        }

        if (!args.KeepPopupOpen && !action.KeepOpen && _popup is not null)
        {
            ClosePopup();
        }

        FlushPopupRefresh();
        return true;
    }

    // ---- Called by MarinaInputController --------------------------------------------------------

    internal void HandleClick(float x, float y, PointerButton button, bool isDoubleClick, InputModifiers modifiers = InputModifiers.None)
    {
        var hit = HitTest(x, y);
        var slip = hit is { } h ? GetSlip(h.SlipId) : null;
        // Ctrl+click or Shift+click adds/removes slips. (Shift+drag still orbits: drags never reach this method.)
        var additive = MultiSelectEnabled && (modifiers & (InputModifiers.Control | InputModifiers.Shift)) != 0;

        if (slip is null)
        {
            if (isDoubleClick || additive) return;
            if (button == PointerButton.Left) ClearSelection();
            else if (button == PointerButton.Right) ClosePopup();
            return;
        }

        // Disabled slips are inert: no selection, no popup, no click event.
        if (!slip.IsInteractive) return;

        var worldPoint = hit!.Value.WorldPoint;
        if (isDoubleClick)
        {
            if (button == PointerButton.Left) FocusSlip(slip.Id);
            SlipClicked?.Invoke(this, CreateSlipArgs(slip, button, isDoubleClick: true, worldPoint));
            return;
        }

        switch (button)
        {
            case PointerButton.Left when additive:
            {
                var ids = IsSlipSelected(slip.Id)
                    ? _selection.Where(id => !IdComparer.Equals(id, slip.Id)).ToArray()
                    : _selection.Append(slip.Id).ToArray();
                if (SetSelectionCore(ids) && ids.Length > 0)
                {
                    RaiseContentAndShowPopup(SelectionReason.Pointer, isNewSelection: true, button, SlipPopupKind.Tooltip, worldPoint);
                }

                break;
            }

            case PointerButton.Left:
            {
                var changed = SetSelectionCore(new[] { slip.Id });
                RaiseContentAndShowPopup(SelectionReason.Pointer, changed, button, SlipPopupKind.Tooltip, worldPoint);
                break;
            }

            case PointerButton.Right:
            {
                var before = new HashSet<string>(_selection, IdComparer);
                string[] ids;
                if (before.Contains(slip.Id) && (before.Count > 1 || additive))
                {
                    // Right-click inside a multi-selection keeps it and moves the popup to the clicked slip.
                    ids = _selection.Where(id => !IdComparer.Equals(id, slip.Id)).Append(slip.Id).ToArray();
                }
                else
                {
                    ids = additive ? _selection.Append(slip.Id).ToArray() : new[] { slip.Id };
                }

                SetSelectionCore(ids);
                var isNew = !before.SetEquals(ids);
                RaiseContentAndShowPopup(SelectionReason.Pointer, isNew, button, SlipPopupKind.Actions, worldPoint);
                break;
            }
        }

        SlipClicked?.Invoke(this, CreateSlipArgs(slip, button, isDoubleClick: false, worldPoint));
    }

    /// <summary>Escape closes the popup first, then clears the selection.</summary>
    internal void HandleEscape()
    {
        if (_popup is not null) ClosePopup();
        else ClearSelection();
    }

    // ---- Core -----------------------------------------------------------------------------------

    /// <summary>Applies a new selection list. Raises SelectionChanged/SelectionCleared and closes the popup when emptied. Returns true if it changed.</summary>
    private bool SetSelectionCore(IReadOnlyList<string> ids)
    {
        if (ids.Count == _selection.Count && ids.Zip(_selection).All(p => IdComparer.Equals(p.First, p.Second))) return false;

        var previous = SelectedSlips;
        _selection.Clear();
        _selection.AddRange(ids);
        MarkSceneDirty();

        if (_selection.Count == 0) ClosePopup();

        SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(previous, SelectedSlips));
        if (_selection.Count == 0) SelectionCleared?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Removes slips that can no longer be selected; refreshes the popup for what remains.</summary>
    private void RemoveFromSelectionCore(params string[] slipIds)
    {
        var remaining = _selection.Where(id => !slipIds.Contains(id, IdComparer)).ToArray();
        if (!SetSelectionCore(remaining)) return;
        if (remaining.Length > 0) RequestPopupRefresh();
    }

    /// <summary>
    /// Raises <see cref="SlipSelected"/> or <see cref="MultiSlipSelected"/> with default content and opens the requested popup
    /// with whatever the handlers produced. Returns false if nothing was shown.
    /// </summary>
    private bool RaiseContentAndShowPopup(SelectionReason reason, bool isNewSelection, PointerButton button, SlipPopupKind? kind, Vector3? worldPoint = null)
    {
        var slips = SelectedSlips;
        if (slips.Count == 0) return false;

        var snapshot = _selection.ToArray();
        var tooltip = slips.Count == 1
            ? DefaultPopupContent.ForSlip(slips[0], GetDock(slips[0].DockId), GetMultiSlipBerthForSlip(slips[0].Id), _colors)
            : DefaultPopupContent.ForSlips(slips, GetDock, _colors);
        var actions = new SlipActionCollection();

        _popupRefreshSuppression++;
        try
        {
            if (slips.Count == 1)
            {
                var slip = slips[0];
                SlipSelected?.Invoke(this, new SlipSelectedEventArgs(
                    slip, GetDock(slip.DockId), GetMultiSlipBerthForSlip(slip.Id), tooltip, actions, reason, isNewSelection,
                    button, isDoubleClick: false, worldPoint));
            }
            else
            {
                MultiSlipSelected?.Invoke(this, new MultiSlipSelectedEventArgs(slips, tooltip, actions, reason, isNewSelection, button));
            }
        }
        finally
        {
            _popupRefreshSuppression--;
        }

        // The content just built is current; drop refreshes requested by the handlers themselves.
        _popupRefreshPending = false;

        // A handler changed the selection (and handled its own popup).
        if (!snapshot.SequenceEqual(_selection, IdComparer)) return false;
        if (kind is not { } requested)
        {
            // No popup requested: don't leave one pointing at the previous selection.
            ClosePopup();
            return false;
        }

        return ShowPopup(requested, tooltip, actions);
    }

    private bool ShowPopup(SlipPopupKind requested, SlipTooltip tooltip, SlipActionCollection actions)
    {
        var slips = SelectedSlips;
        var visibleActions = actions.Where(a => a.Visible).ToArray();
        var kind = requested;

        // Read-only selections, or selections without actions, only get the tooltip.
        if (kind == SlipPopupKind.Actions && (!ActionsEnabled || visibleActions.Length == 0 || !slips.Any(s => s.AllowsActions)))
        {
            kind = SlipPopupKind.Tooltip;
        }

        if (kind == SlipPopupKind.Tooltip && (!TooltipsEnabled || !tooltip.IsVisible || IsTooltipEmpty(tooltip)))
        {
            ClosePopup();
            return false;
        }

        SetPopup(new SlipPopup(kind, slips, tooltip.Clone(), kind == SlipPopupKind.Actions ? visibleActions : Array.Empty<SlipAction>(), ++_popupVersion));
        return true;
    }

    private static bool IsTooltipEmpty(SlipTooltip tooltip) =>
        string.IsNullOrWhiteSpace(tooltip.Title) && string.IsNullOrWhiteSpace(tooltip.Subtitle) &&
        string.IsNullOrWhiteSpace(tooltip.Footer) && tooltip.Lines.Count == 0;

    private void SetPopup(SlipPopup? popup)
    {
        if (ReferenceEquals(_popup, popup)) return;
        var previous = _popup;
        _popup = popup;
        if (popup is null) _popupRefreshPending = false;
        PopupChanged?.Invoke(this, new SlipPopupChangedEventArgs(previous, popup));
    }

    /// <summary>Marks the open popup's content stale. Applied immediately, or when the current update scope/event ends.</summary>
    private void RequestPopupRefresh()
    {
        if (_popup is null) return;
        _popupRefreshPending = true;
        FlushPopupRefresh();
    }

    private void FlushPopupRefresh()
    {
        if (!_popupRefreshPending || _updateDepth > 0 || _popupRefreshSuppression > 0) return;
        _popupRefreshPending = false;
        if (_popup is not { } popup) return;

        if (_selection.Count == 0)
        {
            ClosePopup();
            return;
        }

        var button = popup.Kind == SlipPopupKind.Actions ? PointerButton.Right : PointerButton.Left;
        RaiseContentAndShowPopup(SelectionReason.Refresh, isNewSelection: false, button, popup.Kind);
    }

    private void RefreshPopupIfShowingDock(string dockId)
    {
        if (_popup is { } popup && popup.Slips.Any(s => IdComparer.Equals(s.DockId, dockId))) RequestPopupRefresh();
    }
}
