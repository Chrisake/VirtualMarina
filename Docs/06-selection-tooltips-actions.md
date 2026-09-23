# Selection, tooltips and actions

## How the user selects

| Gesture | Selection | Popup | Events (in order) |
|---|---|---|---|
| Left click on a berth or its boat | That berth only | Tooltip | `SelectionChanged` (if changed), `BerthSelected`, `PopupChanged`, `BerthClicked` |
| Left click on a selected berth | Unchanged | Tooltip (rebuilt) | `BerthSelected` (`IsNewSelection = false`), `PopupChanged`, `BerthClicked` |
| Ctrl+click or Shift+click (Cmd+click in browsers) | Toggles the berth in the selection; the clicked berth becomes primary | Tooltip for the new selection | `SelectionChanged`, then `MultiBerthSelected` (2+ berths) or `BerthSelected` (1 berth) |
| Right click on a berth | That berth only | Actions window | as left click, with `Button = Right` |
| Right click inside a multi-selection | Kept; the clicked berth becomes primary | Actions window for all selected berths | `SelectionChanged` (primary changed), `MultiBerthSelected` |
| Double-click | Unchanged | Unchanged | `BerthClicked` (`IsDoubleClick`); a left double-click focuses the camera |
| Left click on empty water | Cleared | Closed | `SelectionChanged`, `SelectionCleared`, `PopupChanged` |
| Right click on empty water | Unchanged | Closed | `PopupChanged` |
| Esc | Closes the popup; pressed again, clears the selection. With neither, the key is left to the host | | as closing or clearing |
| Any click on a disabled berth | Unchanged | Unchanged | None |

- **Primary berth:** the most recently clicked (or last listed) selected berth. The popup points at it and the selection marker above it is larger.
- **Turning gestures off:** `MultiSelectEnabled`, `TooltipsEnabled` and `ActionsEnabled` disable the corresponding behavior.

## Changing the selection from code

```csharp
// One or many berths; disabled, hidden, filtered-out and unknown ids are discarded (and reported).
SelectionResult result = marina.SetSelection(new[] { "A-L01", "A-L02", "C-R08" });
SelectionResult same = marina.SetSelection("A-L01", "A-L02");                     // params overload

// Select and frame them with the camera
marina.SetSelection(pierBIds, focusCamera: true, focusAngle: CameraAngle.TopDown);

foreach (RejectedBerth r in result.Rejected)
    log($"{r.BerthId} skipped: {r.Reason}");      // NotFound, Disabled, Hidden, FilteredOut

bool ok      = marina.SelectBerth("A-L03", focusCamera: true);   // single berth; false if not selectable
int count    = marina.SelectBerths(ids);                         // like SetSelection, returns the count
bool added   = marina.AddToSelection("A-L04");                  // becomes primary
bool removed = marina.RemoveFromSelection("A-L01");
marina.ClearSelection();

Berth? primary = marina.SelectedBerth;
IReadOnlyList<Berth> all = marina.SelectedBerths;                 // selection order, primary last; a read-only snapshot
bool isSelected = marina.IsBerthSelected("A-L02");
```

- **`SelectedBerths`** is a read-only snapshot: it does not change after it is handed out (read it again after the selection changes), and it cannot be cast back to a list and modified.
- **`SelectionResult`:** `SelectedBerthIds` (the selection after the call), `Rejected`, `Changed`, `Count` and `IsEmpty`.
- **Events:** API selection raises `BerthSelected` or `MultiBerthSelected` with `Reason = SelectionReason.Api`, but only when the selection actually changed.
- **Tooltip:** shown after an API selection when `ShowTooltipOnApiSelection` is true (the default).
- **No selectable berths:** passing no selectable berths clears the selection.

## Filling the tooltip and actions

The popup's content comes from the selection events. The visualizer pre-fills the tooltip, raises the event, and shows whatever the handlers leave:

```csharp
marina.BerthSelected += (sender, e) =>
{
    // e.Berth, e.BerthId, e.Status, e.Boat, e.Pier (or e.LandArea), e.MultiBerth, e.ExternalData
    // e.Button (Left / Right / None), e.OpensActions, e.Reason (Pointer / Api / Refresh), e.IsNewSelection

    // Tooltip: pre-filled with berth, pier, status, size, boat, owner, registration, ETA, multi-berth, access
    e.Tooltip.AddLine("Contract", contract.Number);
    e.Tooltip.AddLine("Balance", contract.Balance.ToString("C"), emphasize: contract.Balance > 0);
    e.Tooltip.SetLine("Owner", contract.HolderName);     // replace a default row
    e.Tooltip.RemoveLine("Registration");
    e.Tooltip.Footer = "Last visit: 2 days ago";
    // e.Tooltip.Clear();            // start from scratch (Title, Subtitle, rows, footer, accent)
    // e.Tooltip.IsVisible = false;  // no tooltip for this berth

    // Actions: empty by default; shown on right-click
    var checkIn = e.Actions.Add("checkin", "Check in", enabled: e.Status is BerthStatus.Free or BerthStatus.Reserved, icon: "⚓");
    checkIn.Style = BerthActionStyle.Primary;
    if (!checkIn.Enabled) checkIn.Description = "Berth is occupied";

    var contractAction = e.Actions.Add("contract", "Open contract…", enabled: e.Boat is not null, icon: "📄");
    contractAction.BeginGroup = true;          // separator above
    contractAction.ShortcutText = "Ctrl+O";    // informational text on the right
    contractAction.Tag = contract;             // passed back on invoke

    e.Actions.Add("release", "Release berth", icon: "⇥").Style = BerthActionStyle.Danger;
};

marina.MultiBerthSelected += (sender, e) =>
{
    // e.Berths (selection order), e.PrimaryBerth, e.BerthIds, e.ActionableBerths (not read-only), e.Button, e.Reason
    e.Tooltip.AddLine("Total length", $"{e.Berths.Sum(s => s.Length):0} m");
    e.Actions.Add("free-all", $"Free {e.ActionableBerths.Count} berths").Style = BerthActionStyle.Danger;
    e.Actions.Add("moor", "Moor one yacht alongside", enabled: e.ActionableBerths.Count >= 2);
};
```

### `BerthTooltip`

| Member | Meaning |
|---|---|
| `Title`, `Subtitle` | Heading lines (default: berth name and pier; for a multi-selection, "N berths selected" and pier names) |
| `AccentColor` | Color of the top bar (default: status color) |
| `Lines` | `BerthTooltipLine(Label, Value) { IsEmphasized }`; an empty label makes a full-width row |
| `AddLine`, `SetLine`, `RemoveLine`, `Clear` | Editing helpers |
| `Footer` | Muted text under the rows |
| `IsVisible` | False: no tooltip (the actions window still shows its header) |

The default content comes from `DefaultPopupContent.ForBerth` and `ForBerths`, which you can also call yourself.

The default rows and headings come from the resource files, so they follow the [current UI culture](15-localization.md), and counts are worded for the number ("1 berth selected", "3 berths selected"). Match rows by the localized label you get from them, or rebuild the tooltip with `Clear()` if your code needs to know exactly what is in it.

### `BerthAction`

| Member | Meaning |
|---|---|
| `ActionId` | Identifier passed back in `BerthActionInvoked` |
| `Caption` | Text |
| `Enabled` | False: grayed out, can't be invoked |
| `Visible` | False: not shown |
| `Description` | Hover hint (e.g. why it's disabled) |
| `Icon` | Short glyph before the caption (emoji or symbol) |
| `ShortcutText` | Informational text on the right |
| `Style` | `Normal`, `Primary` (highlighted) or `Danger` (red) |
| `BeginGroup` | Draw a separator above |
| `KeepOpen` | Keep the window open after invoking |
| `Tag` | Your object, passed back on invoke |

`BerthActionCollection` adds `Add(id, caption, enabled, icon)`, `Find(id)` and `Remove(id)`.

## When the actions window opens

On right-click (or `ShowActions()`) the actions window opens only if all of these hold:
- `ActionsEnabled` is true,
- at least one visible action exists, and
- at least one selected berth allows actions (it isn't read-only).

Otherwise the tooltip is shown instead. A tooltip with no content, or with `IsVisible = false`, isn't shown at all.

## Handling actions

```csharp
marina.BerthActionInvoked += (sender, e) =>
{
    // e.ActionId, e.Action (incl. Tag), e.Berth / e.BerthId (primary), e.Berths, e.ActionableBerths, e.IsMultiSelection
    switch (e.ActionId)
    {
        case "checkin":  marina.AssignBoat(e.BerthId, erp.NextArrival(e.BerthId)); break;
        case "contract": ShowContract((Contract)e.Action.Tag!); e.KeepPopupOpen = true; break;
        case "release":  marina.ReleaseBerth(e.BerthId); break;
        case "free-all": marina.BatchUpdate(e.ActionableBerths.Select(s => BerthUpdate.Free(s.Id))); break;
    }
};
```

- **Closing:** the window closes after the handler unless `e.KeepPopupOpen` or `action.KeepOpen` is set.
- **Snapshots:** `e.Berths` are current snapshots taken when the action was invoked.
- **Invoking from code:** `InvokeBerthAction(actionId)` returns false when no actions window is open, or the action doesn't exist, is disabled or hidden, or no berth allows actions.

## Popup lifecycle

- **Refresh on change:** while a popup is open, a change to a selected berth (status, boat, flags, geometry, pier, status colors) raises the selection event again with `Reason = SelectionReason.Refresh`. For example, "Check in" can become "Check out" right after the status changes. Inside `BeginUpdate`/`BatchUpdate`, refreshes wait until the scope ends. Updates a handler makes during the event don't trigger another refresh.
- **Handler changes the selection:** if a handler calls a selection method during the event, that call handles the popup and the original popup isn't shown.
- **Popup API:**
  - `ActivePopup` is the current `BerthPopup` (`Kind`, `Berths`, `PrimaryBerth`, `IsMultiSelection`, `Tooltip`, `Actions`, `Version`), or null.
  - `ShowTooltip()` and `ShowActions()` re-raise the selection event with `Reason = Api` and open the popup.
  - `RefreshPopup()` rebuilds the content and `ClosePopup()` closes it.
  - `PopupChanged` fires on every open, close or content change.
- **Following the camera:** the popup stays anchored above the primary berth while the camera moves. Views hide it when the berth is off-screen.

The WinForms control and the Blazor component render the popup. Custom views see [Hosting and custom views](10-hosting-and-custom-views.md#rendering-the-popup).

## External data on berths

Every berth has an `ExternalData` bag (`MarinaDataBag`, string → object) for your own objects. The bag is shared by every snapshot of the berth, so values written from any event are visible later everywhere:

```csharp
marina.BerthSelected += (s, e) =>
{
    var contract = e.ExternalData.GetOrAdd("Erp.Contract", () => erp.LoadContract(e.BerthId));   // load once, cache on the berth
    e.ExternalData["Erp.Views"] = e.ExternalData.Get<int>("Erp.Views") + 1;
    e.Tooltip.AddLine("Contract", contract.Number);
};

// Anywhere else
if (marina.GetBerth("A-L03")!.ExternalData.TryGet<Contract>("Erp.Contract", out var cached)) { /* ... */ }
```

| Member | Meaning |
|---|---|
| `this[key]`, `Set`, `Add`, `Remove`, `ContainsKey`, `TryGetValue`, `Clear`, `Count`, `Keys`, `Values` | Dictionary operations (keys are case-sensitive) |
| `Get<T>(key)` | Value if present and of type T, otherwise default |
| `TryGet<T>(key, out value)` | Typed lookup |
| `GetOrAdd<T>(key, factory)` | Lazy creation |

- **Preserved:** the bag survives status, boat, flag and geometry updates. `UpdateBerth(newBerthObject)` keeps the existing bag and merges entries from the new object's bag.
- **In batches:** `BerthUpdate.ExternalData` merges entries: keys listed are added or overwritten, keys not listed are left alone, and a null value is stored as null (the key stays). To take keys out, list them in `BerthUpdate.ExternalDataRemovals`; removals are applied first, so a key both removed and written ends up with the written value.

  ```csharp
  marina.BatchUpdate(new[]
  {
      new BerthUpdate("A-L03") { ExternalData = new Dictionary<string, object?> { ["Note"] = "VIP" } },
      new BerthUpdate("A-L04") { ExternalDataRemovals = new[] { "Note", "Erp.Contract" } },
  });
  ```
- **Not interpreted:** the visualizer never reads, renders or serializes the bag.
- **Equality:** a `Berth` compares its bag by reference, so two snapshots of the same berth are equal while two berths built separately are not, even with the same entries.
- **For your own attributes:** use `Berth.Metadata` for read-only string attributes you supply with the layout.
