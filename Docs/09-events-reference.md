# Events reference

- **When:** all events are raised synchronously on the calling (UI) thread, after the state change has been applied.
- **Data:** event data carries immutable snapshots, plus the berth's shared `ExternalData`.
- **Re-entrancy:** handlers may call back into the API.

| Event | Data | Raised when |
|---|---|---|
| `BerthClicked` | `BerthEventArgs` | A berth or its boat is clicked or double-clicked with any button (after any selection change). Never for disabled berths. |
| `BerthSelected` | `BerthSelectedEventArgs` | One berth is selected: by click (also re-clicking it), by API, or to refresh an open popup |
| `MultiBerthSelected` | `MultiBerthSelectedEventArgs` | Two or more berths are selected (Ctrl+click or Shift+click, right-click in a multi-selection, API), or their popup is refreshed |
| `SelectionChanged` | `SelectionChangedEventArgs` | The set of selected berths or the primary berth changed, including clearing |
| `SelectionCleared` | `EventArgs` | The selection became empty (after `SelectionChanged`) |
| `BerthActionInvoked` | `BerthActionInvokedEventArgs` | An enabled action was clicked in the actions window, or `InvokeBerthAction` was called |
| `PopupChanged` | `BerthPopupChangedEventArgs` | The tooltip/actions popup opened, closed or changed content |
| `BerthHoverChanged` | `BerthHoverEventArgs` | The berth under the pointer changed (null when leaving all berths). Disabled berths are never hovered. |
| `BerthStatusChanged` | `BerthStatusChangedEventArgs` | A berth's status or boat changed through any API; once per berth |
| `LayoutChanged` | `LayoutChangedEventArgs` | Piers, berths, dividers or berths changed. Coalesced into one `BatchUpdated` inside `BeginUpdate`/`BatchUpdate`. |

## `BerthEventArgs`

| Property | Meaning |
|---|---|
| `Berth`, `BerthId`, `Status`, `Boat` | The berth snapshot and shortcuts |
| `Pier` | The berth's pier |
| `ExternalData` | The berth's host data bag (shared, mutable) |
| `Button` | `Left`, `Right`, `Middle`, or `None` for API calls |
| `IsDoubleClick` | Double-click |
| `WorldPoint` | World point under the pointer (clicks only) |

## `BerthSelectedEventArgs` : `BerthEventArgs`

| Property | Meaning |
|---|---|
| `Tooltip` | Pre-filled `BerthTooltip`; edit to change the popup |
| `Actions` | Empty `BerthActionCollection`; add the actions available for this berth |
| `Berth` | The berth's `MultiBerth`, if any |
| `Reason` | `Pointer`, `Api` or `Refresh` |
| `IsNewSelection` | False when the berth was already selected |
| `OpensActions` | True for right-click (the actions window will open) |

## `MultiBerthSelectedEventArgs`

| Property | Meaning |
|---|---|
| `Berths`, `BerthIds` | Selection in order (primary last) |
| `PrimaryBerth` | Most recently clicked berth |
| `ActionableBerths` | Selected berths that aren't read-only |
| `Tooltip`, `Actions` | As above, for the whole selection |
| `Reason`, `IsNewSelection`, `Button`, `OpensActions` | As above |

## `BerthActionInvokedEventArgs`

| Property | Meaning |
|---|---|
| `ActionId`, `Action` | The clicked action (with `Tag`) |
| `Berth`, `BerthId` | The primary berth |
| `Berths` | All berths the window was opened for (current snapshots) |
| `ActionableBerths` | Those that aren't read-only |
| `IsMultiSelection` | More than one berth |
| `KeepPopupOpen` | Set true to keep the window open |

## `SelectionChangedEventArgs`

| Property | Meaning |
|---|---|
| `Previous`, `Current` | Previous and new primary berth (`Current` null when cleared) |
| `PreviousBerths`, `CurrentBerths` | Full previous and new selections |
| `IsMultiSelection` | Two or more berths now selected |

## `BerthHoverEventArgs`

`Berth`: the hovered berth, or null.

## `BerthPopupChangedEventArgs`

`Previous` and `Current` popups (`BerthPopup`; null when none): `Kind`, `Berths`, `PrimaryBerth`, `IsMultiSelection`, `Tooltip`, `Actions` (visible only), `Version`.

## `BerthStatusChangedEventArgs`

`Previous`, `Current` snapshots; `BerthId`, `OldStatus`, `NewStatus`, `OldBoat`, `NewBoat`.

## `LayoutChangedEventArgs`

`Kind` (`LayoutChangeKind`) and the ids that apply: `PierId`, `BerthId`, `DividerId`, `MultiBerthId`, `LandAreaId`.

| `LayoutChangeKind` | Ids set |
|---|---|
| `Initialized`, `Cleared`, `BatchUpdated` | none |
| `PierAdded`, `PierUpdated`, `PierRemoved` | `PierId` |
| `BerthAdded`, `BerthUpdated`, `BerthRemoved`, `BerthRenamed` | `BerthId` (the new one after a rename), and `PierId` or `LandAreaId` (land berths) |
| `DividerAdded`, `DividerUpdated`, `DividerRemoved` | `DividerId`, `PierId` (if the divider has one) |
| `MultiBerthAdded`, `MultiBerthUpdated`, `MultiBerthRemoved` | `MultiBerthId` |
| `LandAreaAdded`, `LandAreaUpdated`, `LandAreaRemoved` | `LandAreaId` |

## Designer events

`MarinaDesigner` (`marina.Designer`) raises `ActiveChanged`, `ToolChanged`, `DraftChanged`, `ElementCreating`, `ElementCreated`, `ElementErased`, `ElementRenaming`, `TreesPlanted`, `ActionUndone`, `ScaleLineDrawn`, `ReferenceImageChanged` and `StateChanged` while the user draws. See [Designer](12-designer.md#events).

## Typical wiring

```csharp
marina.SelectionChanged  += (_, e) => detailsPanel.Show(e.CurrentBerths);
marina.BerthSelected      += (_, e) => FillBerthPopup(e);
marina.MultiBerthSelected += (_, e) => FillMultiPopup(e);
marina.BerthActionInvoked += (_, e) => RunAction(e);
marina.BerthStatusChanged += (_, e) => audit.Log(e.BerthId, e.OldStatus, e.NewStatus);
marina.LayoutChanged     += (_, _) => dashboard.Update(marina.GetStatistics());
marina.BerthHoverChanged  += (_, e) => statusBar.Text = e.Berth?.DisplayName ?? "";
```
