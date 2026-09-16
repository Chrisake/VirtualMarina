# Selection, tooltips and actions

## How the user selects

| Gesture | Selection | Popup | Events (in order) |
|---|---|---|---|
| Left click on a slip or its boat | That slip only | Tooltip | `SelectionChanged` (if changed), `SlipSelected`, `PopupChanged`, `SlipClicked` |
| Left click on a selected slip | Unchanged | Tooltip (rebuilt) | `SlipSelected` (`IsNewSelection = false`), `PopupChanged`, `SlipClicked` |
| Ctrl+click (Cmd+click in browsers) | Toggles the slip in the selection; the clicked slip becomes primary | Tooltip for the new selection | `SelectionChanged`, then `MultiSlipSelected` (2+ slips) or `SlipSelected` (1 slip) |
| Right click on a slip | That slip only | Actions window | as left click, with `Button = Right` |
| Right click inside a multi-selection | Kept; the clicked slip becomes primary | Actions window for all selected slips | `SelectionChanged` (primary changed), `MultiSlipSelected` |
| Double-click | Unchanged | Unchanged | `SlipClicked` (`IsDoubleClick`); a left double-click focuses the camera |
| Left click on empty water | Cleared | Closed | `SelectionChanged`, `SelectionCleared`, `PopupChanged` |
| Right click on empty water | Unchanged | Closed | `PopupChanged` |
| Esc | Closes the popup; pressed again, clears the selection | | |
| Any click on a disabled slip | Unchanged | Unchanged | None |

- **Primary slip:** the most recently clicked (or last listed) selected slip. The popup points at it and the selection marker above it is larger.
- **Turning gestures off:** `MultiSelectEnabled`, `TooltipsEnabled` and `ActionsEnabled` disable the corresponding behavior.

## Changing the selection from code

```csharp
// One or many slips; disabled, hidden, filtered-out and unknown ids are discarded (and reported).
SelectionResult result = marina.SetSelection(new[] { "A-L01", "A-L02", "C-R08" });
SelectionResult same = marina.SetSelection("A-L01", "A-L02");                     // params overload

// Select and frame them with the camera
marina.SetSelection(dockBIds, focusCamera: true, focusAngle: CameraAngle.TopDown);

foreach (RejectedSlip r in result.Rejected)
    log($"{r.SlipId} skipped: {r.Reason}");      // NotFound, Disabled, Hidden, FilteredOut

bool ok      = marina.SelectSlip("A-L03", focusCamera: true);   // single slip; false if not selectable
int count    = marina.SelectSlips(ids);                         // like SetSelection, returns the count
bool added   = marina.AddToSelection("A-L04");                  // becomes primary
bool removed = marina.RemoveFromSelection("A-L01");
marina.ClearSelection();

Slip? primary = marina.SelectedSlip;
IReadOnlyList<Slip> all = marina.SelectedSlips;                 // selection order, primary last
bool isSelected = marina.IsSlipSelected("A-L02");
```

- **`SelectionResult`:** `SelectedSlipIds` (the selection after the call), `Rejected`, `Changed`, `Count` and `IsEmpty`.
- **Events:** API selection raises `SlipSelected` or `MultiSlipSelected` with `Reason = SelectionReason.Api`, but only when the selection actually changed.
- **Tooltip:** shown after an API selection when `ShowTooltipOnApiSelection` is true (the default).
- **No selectable slips:** passing no selectable slips clears the selection.

## Filling the tooltip and actions

The popup's content comes from the selection events. The visualizer pre-fills the tooltip, raises the event, and shows whatever the handlers leave:

```csharp
marina.SlipSelected += (sender, e) =>
{
    // e.Slip, e.SlipId, e.Status, e.Boat, e.Dock, e.Berth, e.ExternalData
    // e.Button (Left / Right / None), e.OpensActions, e.Reason (Pointer / Api / Refresh), e.IsNewSelection

    // Tooltip: pre-filled with slip, dock, status, size, boat, owner, registration, ETA, berth, access
    e.Tooltip.AddLine("Contract", contract.Number);
    e.Tooltip.AddLine("Balance", contract.Balance.ToString("C"), emphasize: contract.Balance > 0);
    e.Tooltip.SetLine("Owner", contract.HolderName);     // replace a default row
    e.Tooltip.RemoveLine("Registration");
    e.Tooltip.Footer = "Last visit: 2 days ago";
    // e.Tooltip.Clear();            // start from scratch (Title, Subtitle, rows, footer, accent)
    // e.Tooltip.IsVisible = false;  // no tooltip for this slip

    // Actions: empty by default; shown on right-click
    var checkIn = e.Actions.Add("checkin", "Check in", enabled: e.Status is SlipStatus.Free or SlipStatus.Reserved, icon: "⚓");
    checkIn.Style = SlipActionStyle.Primary;
    if (!checkIn.Enabled) checkIn.Description = "Slip is occupied";

    var contractAction = e.Actions.Add("contract", "Open contract…", enabled: e.Boat is not null, icon: "📄");
    contractAction.BeginGroup = true;          // separator above
    contractAction.ShortcutText = "Ctrl+O";    // informational text on the right
    contractAction.Tag = contract;             // passed back on invoke

    e.Actions.Add("release", "Release slip", icon: "⇥").Style = SlipActionStyle.Danger;
};

marina.MultiSlipSelected += (sender, e) =>
{
    // e.Slips (selection order), e.PrimarySlip, e.SlipIds, e.ActionableSlips (not read-only), e.Button, e.Reason
    e.Tooltip.AddLine("Total length", $"{e.Slips.Sum(s => s.Length):0} m");
    e.Actions.Add("free-all", $"Free {e.ActionableSlips.Count} slips").Style = SlipActionStyle.Danger;
    e.Actions.Add("moor", "Moor one yacht alongside", enabled: e.ActionableSlips.Count >= 2);
};
```

### `SlipTooltip`

| Member | Meaning |
|---|---|
| `Title`, `Subtitle` | Heading lines (default: slip name and dock; for a multi-selection, "N slips selected" and dock names) |
| `AccentColor` | Color of the top bar (default: status color) |
| `Lines` | `SlipTooltipLine(Label, Value) { IsEmphasized }`; an empty label makes a full-width row |
| `AddLine`, `SetLine`, `RemoveLine`, `Clear` | Editing helpers |
| `Footer` | Muted text under the rows |
| `IsVisible` | False: no tooltip (the actions window still shows its header) |

The default content comes from `DefaultPopupContent.ForSlip` and `ForSlips`, which you can also call yourself.

### `SlipAction`

| Member | Meaning |
|---|---|
| `ActionId` | Identifier passed back in `SlipActionInvoked` |
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

`SlipActionCollection` adds `Add(id, caption, enabled, icon)`, `Find(id)` and `Remove(id)`.

## When the actions window opens

On right-click (or `ShowActions()`) the actions window opens only if all of these hold:
- `ActionsEnabled` is true,
- at least one visible action exists, and
- at least one selected slip allows actions (it isn't read-only).

Otherwise the tooltip is shown instead. A tooltip with no content, or with `IsVisible = false`, isn't shown at all.

## Handling actions

```csharp
marina.SlipActionInvoked += (sender, e) =>
{
    // e.ActionId, e.Action (incl. Tag), e.Slip / e.SlipId (primary), e.Slips, e.ActionableSlips, e.IsMultiSelection
    switch (e.ActionId)
    {
        case "checkin":  marina.AssignBoat(e.SlipId, erp.NextArrival(e.SlipId)); break;
        case "contract": ShowContract((Contract)e.Action.Tag!); e.KeepPopupOpen = true; break;
        case "release":  marina.ReleaseSlip(e.SlipId); break;
        case "free-all": marina.BatchUpdate(e.ActionableSlips.Select(s => SlipUpdate.Free(s.Id))); break;
    }
};
```

- **Closing:** the window closes after the handler unless `e.KeepPopupOpen` or `action.KeepOpen` is set.
- **Snapshots:** `e.Slips` are current snapshots taken when the action was invoked.
- **Invoking from code:** `InvokeSlipAction(actionId)` returns false when no actions window is open, or the action doesn't exist, is disabled or hidden, or no slip allows actions.

## Popup lifecycle

- **Refresh on change:** while a popup is open, a change to a selected slip (status, boat, flags, geometry, dock, status colors) raises the selection event again with `Reason = SelectionReason.Refresh`. For example, "Check in" can become "Check out" right after the status changes. Inside `BeginUpdate`/`BatchUpdate`, refreshes wait until the scope ends. Updates a handler makes during the event don't trigger another refresh.
- **Handler changes the selection:** if a handler calls a selection method during the event, that call handles the popup and the original popup isn't shown.
- **Popup API:**
  - `ActivePopup` is the current `SlipPopup` (`Kind`, `Slips`, `PrimarySlip`, `IsMultiSelection`, `Tooltip`, `Actions`, `Version`), or null.
  - `ShowTooltip()` and `ShowActions()` re-raise the selection event with `Reason = Api` and open the popup.
  - `RefreshPopup()` rebuilds the content and `ClosePopup()` closes it.
  - `PopupChanged` fires on every open, close or content change.
- **Following the camera:** the popup stays anchored above the primary slip while the camera moves. Views hide it when the slip is off-screen.

The WinForms control and the Blazor component render the popup. Custom views see [Hosting and custom views](10-hosting-and-custom-views.md#rendering-the-popup).

## External data on slips

Every slip has an `ExternalData` bag (`SlipDataBag`, string → object) for your own objects. The bag is shared by every snapshot of the slip, so values written from any event are visible later everywhere:

```csharp
marina.SlipSelected += (s, e) =>
{
    var contract = e.ExternalData.GetOrAdd("Erp.Contract", () => erp.LoadContract(e.SlipId));   // load once, cache on the slip
    e.ExternalData["Erp.Views"] = e.ExternalData.Get<int>("Erp.Views") + 1;
    e.Tooltip.AddLine("Contract", contract.Number);
};

// Anywhere else
if (marina.GetSlip("A-L03")!.ExternalData.TryGet<Contract>("Erp.Contract", out var cached)) { /* ... */ }
```

| Member | Meaning |
|---|---|
| `this[key]`, `Set`, `Add`, `Remove`, `ContainsKey`, `TryGetValue`, `Clear`, `Count`, `Keys`, `Values` | Dictionary operations (keys are case-sensitive) |
| `Get<T>(key)` | Value if present and of type T, otherwise default |
| `TryGet<T>(key, out value)` | Typed lookup |
| `GetOrAdd<T>(key, factory)` | Lazy creation |

- **Preserved:** the bag survives status, boat, flag and geometry updates. `UpdateSlip(newSlipObject)` keeps the existing bag and merges entries from the new object's bag.
- **In batches:** `SlipUpdate.ExternalData` merges entries (`new SlipUpdate(id) { ExternalData = new Dictionary<string, object?> { ["Note"] = "VIP" } }`).
- **Not interpreted:** the visualizer never reads, renders or serializes the bag.
- **For your own attributes:** use `Slip.Metadata` for read-only string attributes you supply with the layout.
