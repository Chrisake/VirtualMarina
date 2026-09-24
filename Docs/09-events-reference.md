# Events reference

- **When:** all events are raised synchronously on the calling (UI) thread, after the state change has been applied. Inside `BeginUpdate` or `BatchUpdate`, `LayoutChanged` and `CameraPresetsChanged` wait for the end of the scope, and so does refreshing an open popup (with its `BerthSelected`/`MultiBerthSelected` and `PopupChanged`); the other events are raised at once.
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
| `BerthStatusChanged` | `BerthStatusChangedEventArgs` | A berth's status or boat changed through any API; once per berth. Raised **at once**, as each berth changes, even inside `BeginUpdate` or `BatchUpdate`: a handler may see one berth of a batch changed while later changes of the same batch are still to come. |
| `LayoutChanged` | `LayoutChangedEventArgs` | Piers, berths, dividers, multi-berths, land areas, the shoreline or the passing traffic changed, or the layout was initialized or cleared. Coalesced into one `BatchUpdated` inside `BeginUpdate`/`BatchUpdate`, whose `Changes` lists each change. |
| `CameraPresetsChanged` | `EventArgs` | `CameraPresets` may have changed: a saved view added or removed, a view switched on or off, or the layout or the view's size changed. Raised once, then not again until `CameraPresets` has been read (see [Camera](07-camera-and-focus.md#presets)). |
| `RedrawRequested` | `EventArgs` | (`MarinaVisualizer` only, not on the interface.) Something the view draws changed. Raised once until the next frame is built; for views that stop drawing while nothing changes (see [the frame loop](10-hosting-and-custom-views.md#the-frame-loop-custom-views)). |

## `BerthEventArgs`

| Property | Meaning |
|---|---|
| `Berth`, `BerthId`, `Status`, `Boat` | The berth snapshot and shortcuts |
| `Pier` | The berth's pier; null for a land berth |
| `LandArea` | The land area of a land berth; null for a water berth |
| `ExternalData` | The berth's host data bag (shared, mutable) |
| `Button` | `Left`, `Right`, `Middle`, or `None` for API calls |
| `IsDoubleClick` | Double-click |
| `WorldPoint` | World point under the pointer (clicks only) |

## `BerthSelectedEventArgs` : `BerthEventArgs`

| Property | Meaning |
|---|---|
| `Tooltip` | Pre-filled `BerthTooltip`; edit to change the popup |
| `Actions` | Empty `BerthActionCollection`; add the actions available for this berth |
| `MultiBerth` | The berth's `MultiBerth`, if any |
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

`Changes` is a list of `LayoutChange` records (`Kind` and the same ids). For `BatchUpdated` it holds every change made inside the batch, in the order it was made (a berth changed twice appears twice), so a listener can update just what was touched instead of reloading everything; for any other kind it holds the one change the notification is about.

```csharp
marina.LayoutChanged += (_, e) =>
{
    foreach (var change in e.Changes)
        if (change.BerthId is { } id) grid.RefreshRow(id);
};
```

| `LayoutChangeKind` | Ids set |
|---|---|
| `Initialized`, `Cleared`, `ShorelineChanged`, `MarineTrafficChanged` | none |
| `BatchUpdated` | none on the event itself; see `Changes` |
| `PierAdded`, `PierUpdated`, `PierRemoved`, `PierRenamed` | `PierId` (the new one after a rename) |
| `BerthAdded`, `BerthUpdated`, `BerthRemoved`, `BerthRenamed` | `BerthId` (the new one after a rename), and `PierId` or `LandAreaId` (land berths) |
| `DividerAdded`, `DividerUpdated`, `DividerRemoved` | `DividerId`, `PierId` (if the divider has one) |
| `MultiBerthAdded`, `MultiBerthUpdated`, `MultiBerthRemoved` | `MultiBerthId` |
| `LandAreaAdded`, `LandAreaUpdated`, `LandAreaRemoved` | `LandAreaId` |

## Designer events

`MarinaDesigner` (`marina.Designer`) raises `ActiveChanged`, `ToolChanged`, `DraftChanged`, `ElementCreating`, `ElementCreated`, `ElementErased`, `ElementRenaming`, `TreesPlanted`, `ActionUndone`, `ActionRedone`, `ActionFailed`, `ScaleLineDrawn`, `ReferenceImageChanged` and `StateChanged` while the user draws. `ActionFailed` reports something asked for in the view (a click, Enter, Ctrl+Z) that could not be done; nothing was changed. See [Designer](12-designer.md#events).

## View events

| Where | Event | Raised when |
|---|---|---|
| `MarinaViewControl` (WinForms) | The visualizer's events above | Forwarded from `Marina` with the control as sender, so they can be wired in the Visual Studio designer |
| | `RenderError` | OpenGL could not be started or a frame failed to draw; the view shows a placeholder until `RetryRendering()` |
| | `MarinaChanged` | The `Marina` property was given a different visualizer |
| `MarinaDesignerPanel` (WinForms) | `ImageLoadFailed` | Loading a reference image file failed (without a handler, a message box) |
| `<MarinaView>` (Blazor) | `OnRendererReady` | WebGL is running (with a description of the GPU) |
| | `OnRendererError` | WebGL could not be started, or drawing kept failing and the view stopped |

Each `StyleSection` of `marina.Style` also raises `Changed` when one of its properties changes; the visualizer listens to redraw.

## Typical wiring

```csharp
marina.SelectionChanged  += (_, e) => detailsPanel.Show(e.CurrentBerths);
marina.BerthSelected      += (_, e) => FillBerthPopup(e);
marina.MultiBerthSelected += (_, e) => FillMultiPopup(e);
marina.BerthActionInvoked += (_, e) => RunAction(e);
marina.BerthStatusChanged += (_, e) => audit.Log(e.BerthId, e.OldStatus, e.NewStatus);
marina.LayoutChanged      += (_, _) => dashboard.Update(marina.GetStatistics());
marina.BerthHoverChanged  += (_, e) => statusBar.Text = e.Berth?.DisplayName ?? "";
```
