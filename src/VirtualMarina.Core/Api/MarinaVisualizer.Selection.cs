using System.Numerics;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;

namespace VirtualMarina.Core.Api;

public sealed partial class MarinaVisualizer
{
    /// <summary>Selected berth ids in selection order; the last one is the primary berth.</summary>
    private readonly List<string> _selection = new();

    private BerthPopup? _popup;
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

    /// <summary>Show the tooltip when host code selects berths through the API (default true).</summary>
    public bool ShowTooltipOnApiSelection { get; set; } = true;

    // ---- Selection ------------------------------------------------------------------------------

    /// <summary>The primary (most recently clicked) selected berth.</summary>
    public Berth? SelectedBerth => _selection.Count > 0 ? GetBerth(_selection[^1]) : null;

    /// <summary>All selected berths in selection order; the last one is <see cref="SelectedBerth"/>.</summary>
    public IReadOnlyList<Berth> SelectedBerths => _selection.Select(GetBerth).OfType<Berth>().ToArray();

    /// <summary>True when two or more berths are selected.</summary>
    public bool IsMultiSelection => _selection.Count > 1;

    /// <inheritdoc/>
    public bool IsBerthSelected(string berthId) => berthId is not null && _selection.Contains(berthId, IdComparer);

    /// <summary>
    /// Makes <paramref name="berthId"/> the only selected berth and raises <see cref="BerthSelected"/>.
    /// Returns false (and changes nothing) when the berth doesn't exist, is hidden, disabled or filtered out.
    /// </summary>
    public bool SelectBerth(string berthId, bool focusCamera = false)
    {
        var berth = GetBerth(berthId);
        if (berth is null || !IsSelectable(berth)) return false;

        SetSelection(new[] { berth.Id }, focusCamera);
        return true;
    }

    /// <summary>
    /// Replaces the selection with the selectable berths among <paramref name="berthIds"/> (the last one becomes primary)
    /// and raises <see cref="BerthSelected"/> or <see cref="MultiBerthSelected"/>. Returns the number of selected berths.
    /// </summary>
    public int SelectBerths(IEnumerable<string> berthIds) => SetSelection(berthIds).Count;

    /// <summary>Replaces the selection. See <see cref="SetSelection(IEnumerable{string}, bool, CameraAngle?)"/>.</summary>
    public SelectionResult SetSelection(params string[] berthIds) => SetSelection(berthIds, focusCamera: false);

    /// <summary>
    /// Replaces the selection with one or more berths. Disabled berths are discarded, as are hidden, filtered-out and unknown
    /// ids and duplicates; the result lists what was skipped and why. The remaining berths are selected in the given order (the
    /// last becomes primary) and <see cref="BerthSelected"/> or <see cref="MultiBerthSelected"/> is raised as for a click.
    /// When nothing remains the selection is cleared.
    /// </summary>
    /// <param name="berthIds">Berths to select.</param>
    /// <param name="focusCamera">Also frame all selected berths with <see cref="FocusBerths"/>.</param>
    /// <param name="focusAngle">Angle for the focus; null uses <see cref="DefaultFocusAngle"/>.</param>
    public SelectionResult SetSelection(IEnumerable<string> berthIds, bool focusCamera = false, CameraAngle? focusAngle = null)
    {
        ArgumentNullException.ThrowIfNull(berthIds);
        var selected = new List<string>();
        var rejected = new List<RejectedBerth>();
        foreach (var id in berthIds)
        {
            if (id is null) continue;
            if (GetBerth(id) is not { } berth)
            {
                rejected.Add(new RejectedBerth(id, BerthSelectionRejection.NotFound));
                continue;
            }

            if (berth.IsDisabled) rejected.Add(new RejectedBerth(id, BerthSelectionRejection.Disabled));
            else if (!berth.IsVisible) rejected.Add(new RejectedBerth(id, BerthSelectionRejection.Hidden));
            else if (!_statusFilter.Includes(berth.Status)) rejected.Add(new RejectedBerth(id, BerthSelectionRejection.FilteredOut));
            else if (!selected.Contains(berth.Id, IdComparer)) selected.Add(berth.Id);
        }

        var changed = SetSelectionCore(selected);
        if (changed && selected.Count > 0)
        {
            RaiseContentAndShowPopup(SelectionReason.Api, isNewSelection: true, PointerButton.None, ShowTooltipOnApiSelection ? BerthPopupKind.Tooltip : null);
        }

        if (focusCamera && _selection.Count > 0) FocusBerths(_selection, focusAngle);
        return new SelectionResult(_selection.ToArray(), rejected, changed);
    }

    /// <summary>Adds a berth to the selection (making it primary). Returns false when it can't be selected.</summary>
    public bool AddToSelection(string berthId)
    {
        var berth = GetBerth(berthId);
        if (berth is null || !IsSelectable(berth)) return false;
        var ids = _selection.Where(id => !IdComparer.Equals(id, berth.Id)).Append(berth.Id).ToArray();
        if (SetSelectionCore(ids))
        {
            RaiseContentAndShowPopup(SelectionReason.Api, isNewSelection: true, PointerButton.None, _popup?.Kind ?? (ShowTooltipOnApiSelection ? BerthPopupKind.Tooltip : null));
        }

        return true;
    }

    /// <inheritdoc/>
    public bool RemoveFromSelection(string berthId)
    {
        if (!IsBerthSelected(berthId)) return false;
        RemoveFromSelectionCore(berthId);
        return true;
    }

    /// <inheritdoc/>
    public void ClearSelection() => SetSelectionCore(Array.Empty<string>());

    // ---- Popup ----------------------------------------------------------------------------------

    /// <summary>The tooltip or actions window currently shown above the selection, or null.</summary>
    public BerthPopup? ActivePopup => _popup;

    /// <summary>Shows the tooltip for the current selection (raising the selection event again to collect content).</summary>
    public bool ShowTooltip() =>
        _selection.Count > 0 && RaiseContentAndShowPopup(SelectionReason.Api, isNewSelection: false, PointerButton.Left, BerthPopupKind.Tooltip);

    /// <summary>
    /// Shows the actions window for the current selection (raising the selection event with <see cref="PointerButton.Right"/>).
    /// Falls back to the tooltip when the selection is read-only or has no actions.
    /// </summary>
    public bool ShowActions() =>
        _selection.Count > 0 && RaiseContentAndShowPopup(SelectionReason.Api, isNewSelection: false, PointerButton.Right, BerthPopupKind.Actions);

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
    /// Invokes an action of the open actions window: raises <see cref="BerthActionInvoked"/> and closes the window
    /// (unless the action or handler keeps it open). Returns false when there is no such enabled action.
    /// </summary>
    public bool InvokeBerthAction(string actionId)
    {
        if (_popup is not { Kind: BerthPopupKind.Actions } popup || string.IsNullOrEmpty(actionId)) return false;

        var action = popup.Actions.FirstOrDefault(a => string.Equals(a.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
        if (action is null || !action.Enabled || !action.Visible) return false;

        var berths = popup.Berths.Select(s => GetBerth(s.Id)).OfType<Berth>().ToArray();
        if (!berths.Any(s => s.AllowsActions)) return false;

        var args = new BerthActionInvokedEventArgs(action, berths);
        _popupRefreshSuppression++;
        try
        {
            BerthActionInvoked?.Invoke(this, args);
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
        var berth = hit is { } h ? GetBerth(h.BerthId) : null;
        // Ctrl+click or Shift+click adds/removes berths. (Shift+drag still orbits: drags never reach this method.)
        var additive = MultiSelectEnabled && (modifiers & (InputModifiers.Control | InputModifiers.Shift)) != 0;

        if (berth is null)
        {
            if (isDoubleClick || additive) return;
            if (button == PointerButton.Left) ClearSelection();
            else if (button == PointerButton.Right) ClosePopup();
            return;
        }

        // Disabled berths are inert: no selection, no popup, no click event.
        if (!berth.IsInteractive) return;

        var worldPoint = hit!.Value.WorldPoint;
        if (isDoubleClick)
        {
            if (button == PointerButton.Left) FocusBerth(berth.Id);
            BerthClicked?.Invoke(this, CreateBerthArgs(berth, button, isDoubleClick: true, worldPoint));
            return;
        }

        switch (button)
        {
            case PointerButton.Left when additive:
            {
                var ids = IsBerthSelected(berth.Id)
                    ? _selection.Where(id => !IdComparer.Equals(id, berth.Id)).ToArray()
                    : _selection.Append(berth.Id).ToArray();
                if (SetSelectionCore(ids) && ids.Length > 0)
                {
                    RaiseContentAndShowPopup(SelectionReason.Pointer, isNewSelection: true, button, BerthPopupKind.Tooltip, worldPoint);
                }

                break;
            }

            case PointerButton.Left:
            {
                var changed = SetSelectionCore(new[] { berth.Id });
                RaiseContentAndShowPopup(SelectionReason.Pointer, changed, button, BerthPopupKind.Tooltip, worldPoint);
                break;
            }

            case PointerButton.Right:
            {
                var before = new HashSet<string>(_selection, IdComparer);
                string[] ids;
                if (before.Contains(berth.Id) && (before.Count > 1 || additive))
                {
                    // Right-click inside a multi-selection keeps it and moves the popup to the clicked berth.
                    ids = _selection.Where(id => !IdComparer.Equals(id, berth.Id)).Append(berth.Id).ToArray();
                }
                else
                {
                    ids = additive ? _selection.Append(berth.Id).ToArray() : new[] { berth.Id };
                }

                SetSelectionCore(ids);
                var isNew = !before.SetEquals(ids);
                RaiseContentAndShowPopup(SelectionReason.Pointer, isNew, button, BerthPopupKind.Actions, worldPoint);
                break;
            }
        }

        BerthClicked?.Invoke(this, CreateBerthArgs(berth, button, isDoubleClick: false, worldPoint));
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

        var previous = SelectedBerths;
        _selection.Clear();
        _selection.AddRange(ids);
        MarkSceneDirty();

        if (_selection.Count == 0) ClosePopup();

        SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(previous, SelectedBerths));
        if (_selection.Count == 0) SelectionCleared?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Removes berths that can no longer be selected; refreshes the popup for what remains.</summary>
    private void RemoveFromSelectionCore(params string[] berthIds)
    {
        var remaining = _selection.Where(id => !berthIds.Contains(id, IdComparer)).ToArray();
        if (!SetSelectionCore(remaining)) return;
        if (remaining.Length > 0) RequestPopupRefresh();
    }

    /// <summary>
    /// Raises <see cref="BerthSelected"/> or <see cref="MultiBerthSelected"/> with default content and opens the requested popup
    /// with whatever the handlers produced. Returns false if nothing was shown.
    /// </summary>
    private bool RaiseContentAndShowPopup(SelectionReason reason, bool isNewSelection, PointerButton button, BerthPopupKind? kind, Vector3? worldPoint = null)
    {
        var berths = SelectedBerths;
        if (berths.Count == 0) return false;

        var snapshot = _selection.ToArray();
        var tooltip = berths.Count == 1
            ? DefaultPopupContent.ForBerth(berths[0], GetPier(berths[0].PierId!), GetMultiBerthFor(berths[0].Id), _colors, GetLandArea(berths[0].LandAreaId!))
            : DefaultPopupContent.ForBerths(berths, GetPier, _colors, GetLandArea);
        var actions = new BerthActionCollection();

        _popupRefreshSuppression++;
        try
        {
            if (berths.Count == 1)
            {
                var berth = berths[0];
                BerthSelected?.Invoke(this, new BerthSelectedEventArgs(
                    berth, GetPier(berth.PierId!), GetMultiBerthFor(berth.Id), tooltip, actions, reason, isNewSelection,
                    button, isDoubleClick: false, worldPoint)
                {
                    LandArea = GetLandArea(berth.LandAreaId!),
                });
            }
            else
            {
                MultiBerthSelected?.Invoke(this, new MultiBerthSelectedEventArgs(berths, tooltip, actions, reason, isNewSelection, button));
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

    private bool ShowPopup(BerthPopupKind requested, BerthTooltip tooltip, BerthActionCollection actions)
    {
        var berths = SelectedBerths;
        var visibleActions = actions.Where(a => a.Visible).ToArray();
        var kind = requested;

        // Read-only selections, or selections without actions, only get the tooltip.
        if (kind == BerthPopupKind.Actions && (!ActionsEnabled || visibleActions.Length == 0 || !berths.Any(s => s.AllowsActions)))
        {
            kind = BerthPopupKind.Tooltip;
        }

        if (kind == BerthPopupKind.Tooltip && (!TooltipsEnabled || !tooltip.IsVisible || IsTooltipEmpty(tooltip)))
        {
            ClosePopup();
            return false;
        }

        SetPopup(new BerthPopup(kind, berths, tooltip.Clone(), kind == BerthPopupKind.Actions ? visibleActions : Array.Empty<BerthAction>(), ++_popupVersion));
        return true;
    }

    private static bool IsTooltipEmpty(BerthTooltip tooltip) =>
        string.IsNullOrWhiteSpace(tooltip.Title) && string.IsNullOrWhiteSpace(tooltip.Subtitle) &&
        string.IsNullOrWhiteSpace(tooltip.Footer) && tooltip.Lines.Count == 0;

    private void SetPopup(BerthPopup? popup)
    {
        if (ReferenceEquals(_popup, popup)) return;
        var previous = _popup;
        _popup = popup;
        if (popup is null) _popupRefreshPending = false;
        PopupChanged?.Invoke(this, new BerthPopupChangedEventArgs(previous, popup));
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

        var button = popup.Kind == BerthPopupKind.Actions ? PointerButton.Right : PointerButton.Left;
        RaiseContentAndShowPopup(SelectionReason.Refresh, isNewSelection: false, button, popup.Kind);
    }

    private void RefreshPopupIfShowingPier(string pierId)
    {
        if (_popup is { } popup && popup.Berths.Any(s => IdComparer.Equals(s.PierId, pierId))) RequestPopupRefresh();
    }

    private void RefreshPopupIfShowingLand(string landAreaId)
    {
        if (_popup is { } popup && popup.Berths.Any(s => IdComparer.Equals(s.LandAreaId, landAreaId))) RequestPopupRefresh();
    }
}
