# Events reference

- **When:** all events are raised synchronously on the calling (UI) thread, after the state change has been applied.
- **Data:** event data carries immutable snapshots, plus the slip's shared `ExternalData`.
- **Re-entrancy:** handlers may call back into the API.

| Event | Data | Raised when |
|---|---|---|
| `SlipClicked` | `SlipEventArgs` | A slip or its boat is clicked or double-clicked with any button (after any selection change). Never for disabled slips. |
| `SlipSelected` | `SlipSelectedEventArgs` | One slip is selected: by click (also re-clicking it), by API, or to refresh an open popup |
| `MultiSlipSelected` | `MultiSlipSelectedEventArgs` | Two or more slips are selected (Ctrl+click, right-click in a multi-selection, API), or their popup is refreshed |
| `SelectionChanged` | `SelectionChangedEventArgs` | The set of selected slips or the primary slip changed, including clearing |
| `SelectionCleared` | `EventArgs` | The selection became empty (after `SelectionChanged`) |
| `SlipActionInvoked` | `SlipActionInvokedEventArgs` | An enabled action was clicked in the actions window, or `InvokeSlipAction` was called |
| `PopupChanged` | `SlipPopupChangedEventArgs` | The tooltip/actions popup opened, closed or changed content |
| `SlipHoverChanged` | `SlipHoverEventArgs` | The slip under the pointer changed (null when leaving all slips). Disabled slips are never hovered. |
| `SlipStatusChanged` | `SlipStatusChangedEventArgs` | A slip's status or boat changed through any API; once per slip |
| `LayoutChanged` | `LayoutChangedEventArgs` | Docks, slips, dividers or berths changed. Coalesced into one `BatchUpdated` inside `BeginUpdate`/`BatchUpdate`. |

## `SlipEventArgs`

| Property | Meaning |
|---|---|
| `Slip`, `SlipId`, `Status`, `Boat` | The slip snapshot and shortcuts |
| `Dock` | The slip's dock |
| `ExternalData` | The slip's host data bag (shared, mutable) |
| `Button` | `Left`, `Right`, `Middle`, or `None` for API calls |
| `IsDoubleClick` | Double-click |
| `WorldPoint` | World point under the pointer (clicks only) |

## `SlipSelectedEventArgs` : `SlipEventArgs`

| Property | Meaning |
|---|---|
| `Tooltip` | Pre-filled `SlipTooltip`; edit to change the popup |
| `Actions` | Empty `SlipActionCollection`; add the actions available for this slip |
| `Berth` | The slip's `MultiSlipBerth`, if any |
| `Reason` | `Pointer`, `Api` or `Refresh` |
| `IsNewSelection` | False when the slip was already selected |
| `OpensActions` | True for right-click (the actions window will open) |

## `MultiSlipSelectedEventArgs`

| Property | Meaning |
|---|---|
| `Slips`, `SlipIds` | Selection in order (primary last) |
| `PrimarySlip` | Most recently clicked slip |
| `ActionableSlips` | Selected slips that aren't read-only |
| `Tooltip`, `Actions` | As above, for the whole selection |
| `Reason`, `IsNewSelection`, `Button`, `OpensActions` | As above |

## `SlipActionInvokedEventArgs`

| Property | Meaning |
|---|---|
| `ActionId`, `Action` | The clicked action (with `Tag`) |
| `Slip`, `SlipId` | The primary slip |
| `Slips` | All slips the window was opened for (current snapshots) |
| `ActionableSlips` | Those that aren't read-only |
| `IsMultiSelection` | More than one slip |
| `KeepPopupOpen` | Set true to keep the window open |

## `SelectionChangedEventArgs`

| Property | Meaning |
|---|---|
| `Previous`, `Current` | Previous and new primary slip (`Current` null when cleared) |
| `PreviousSlips`, `CurrentSlips` | Full previous and new selections |
| `IsMultiSelection` | Two or more slips now selected |

## `SlipHoverEventArgs`

`Slip`: the hovered slip, or null.

## `SlipPopupChangedEventArgs`

`Previous` and `Current` popups (`SlipPopup`; null when none): `Kind`, `Slips`, `PrimarySlip`, `IsMultiSelection`, `Tooltip`, `Actions` (visible only), `Version`.

## `SlipStatusChangedEventArgs`

`Previous`, `Current` snapshots; `SlipId`, `OldStatus`, `NewStatus`, `OldBoat`, `NewBoat`.

## `LayoutChangedEventArgs`

`Kind` (`LayoutChangeKind`) and the ids that apply: `DockId`, `SlipId`, `DividerId`, `BerthId`.

| `LayoutChangeKind` | Ids set |
|---|---|
| `Initialized`, `Cleared`, `BatchUpdated` | none |
| `DockAdded`, `DockUpdated`, `DockRemoved` | `DockId` |
| `SlipAdded`, `SlipUpdated`, `SlipRemoved` | `SlipId`, `DockId` |
| `DividerAdded`, `DividerUpdated`, `DividerRemoved` | `DividerId`, `DockId` (if the divider has one) |
| `BerthAdded`, `BerthUpdated`, `BerthRemoved` | `BerthId` |

## Typical wiring

```csharp
marina.SelectionChanged  += (_, e) => detailsPanel.Show(e.CurrentSlips);
marina.SlipSelected      += (_, e) => FillSlipPopup(e);
marina.MultiSlipSelected += (_, e) => FillMultiPopup(e);
marina.SlipActionInvoked += (_, e) => RunAction(e);
marina.SlipStatusChanged += (_, e) => audit.Log(e.SlipId, e.OldStatus, e.NewStatus);
marina.LayoutChanged     += (_, _) => dashboard.Update(marina.GetStatistics());
marina.SlipHoverChanged  += (_, e) => statusBar.Text = e.Slip?.DisplayName ?? "";
```
