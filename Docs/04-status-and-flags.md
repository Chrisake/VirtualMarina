# Berth status, boats and flags

## Statuses

| `BerthStatus` | Default color | Boat | Rendering |
|---|---|---|---|
| `Free` | Green | Never (setting Free removes the boat) | Pad and buoy |
| `Occupied` | Red | The moored boat (optional) | Opaque boat model |
| `Reserved` | Blue | The expected boat (optional) | Translucent "ghost" boat tinted blue |
| `TemporarilyFree` | Yellow | The berth holder's boat, which is away (optional) | Translucent ghost boat tinted yellow |

`TemporarilyFree` means the berth is rented, but the holder's boat is out (cruising, in the yard) and the berth can be let out for a while. The boat stays assigned, so the tooltip still shows it; `Boat.ExpectedArrival` is shown as "Returns".

### Changing status

```csharp
marina.AssignBoat("A-L03", boat);                        // Occupied
marina.ReserveBerth("A-L04", expectedBoat);               // Reserved (null expectedBoat = no boat)
marina.MarkTemporarilyFree("A-L05");                     // TemporarilyFree, keeps the current boat
marina.MarkTemporarilyFree("A-L06", awayBoat);           // ... or sets it
marina.ReleaseBerth("A-L07");                             // Free, boat removed
marina.SetBerthStatus("A-L08", BerthStatus.Occupied);      // generic; null boat keeps the current boat (except Free)
```

In batches:

```csharp
marina.BatchUpdate(new[]
{
    BerthUpdate.Occupy("B-L01", boat),
    BerthUpdate.Reserve("B-L02", expected),     // Reserve(id) with no boat clears the boat
    BerthUpdate.TemporarilyFree("B-L03"),
    BerthUpdate.Free("B-L04"),
    new BerthUpdate("B-L05") { Status = BerthStatus.Occupied, ClearBoat = true },
});
```

Every status or boat change raises `BerthStatusChanged` with `Previous`/`Current` snapshots (`OldStatus`, `NewStatus`, `OldBoat`, `NewBoat`). On a berth that belongs to a multi-berth, a status or boat change applies to the whole berth. See [Multi-berths](05-multi-berths.md).

## Boats

```csharp
var boat = new Boat("BT-10442", "Aurora", BoatType.MotorYacht)
{
    LengthMeters = 18.5f,            // defaults to the type's nominal length
    BeamMeters = 5.2f,               // defaults to the type's nominal beam
    OwnerName = "M. Rossi",
    RegistrationNumber = "GR-PIR-1234",
    ExpectedArrival = DateTimeOffset.Now.AddHours(6),
    Metadata = new Dictionary<string, string> { ["Insurance"] = "Valid" },
};
```

| `BoatType` | Model | Nominal length × beam |
|---|---|---|
| `MonohullSailboat` | Sailing yacht with mast and sails | 12 × 4 m |
| `CatamaranSailboat` | Sailing catamaran | 12 × 7 m |
| `DayMotorBoat` | Small motor boat | 7 × 2.5 m |
| `CatamaranMotorboat` | Power catamaran | 13 × 6.5 m |
| `MotorYacht` | Multi-deck motor yacht | 20 × 5.5 m |
| `FishingBoat` | Boat with wheelhouse | 10 × 3.5 m |
| `JetSki` | Personal watercraft | 3.2 × 1.2 m |

- **Size:** the model is scaled to `LengthMeters × BeamMeters` and placed toward the pier end of the berth, bow toward the pier.
- **Display names:** `BoatTypeCatalog.GetNominalDimensions(type)` and `GetDisplayName(type)` expose the reference data above.
- **Custom models:** to replace a model, see [Appearance](08-appearance.md#replacing-boat-models).

## Interaction flags

| Flag | `true` means | Rendering | Interaction |
|---|---|---|---|
| `IsVisible = false` | Hidden | Nothing is drawn: no pad, buoy, boat, label or finger piers | None; can't be selected, hit or hovered |
| `IsDisabled` | Disabled | Pad and buoy gray, boat desaturated to grayscale | No hover, selection, tooltip, actions or `BerthClicked` |
| `IsReadOnly` | Read-only | Normal | Can be selected and shows its tooltip; the actions window never opens and `InvokeBerthAction` refuses it |

Helper properties on `Berth`:
- `IsInteractive` = visible and not disabled.
- `AllowsActions` = interactive and not read-only.

```csharp
marina.SetBerthVisible("B-R12", false);
marina.SetBerthDisabled("C-R08", true);          // e.g. under maintenance
marina.SetBerthReadOnly("D-L09", true);          // e.g. contract locked
marina.SetBerthFlags(pierCBerthIds, disabled: false, readOnly: false);   // many at once; null = unchanged
marina.UpdateBerth(BerthUpdate.Flags("A-L01", visible: true, disabled: false));
```

The flags can also be set in the layout (`berth with { IsDisabled = true }`).

- **Disabled or hidden berths** leave the selection when the flag is set. They are skipped by `SetSelection`, `SelectBerth` and `AddToSelection`.
- **Clicking a disabled berth** does nothing: it doesn't clear the selection or raise events. Hit testing still finds it, so it blocks what's behind it.
- **Read-only berths** in a multi-selection stay selected. Actions apply to `ActionableBerths` (the non-read-only ones). The actions window opens only if at least one selected berth allows actions.
- **Changing a flag while a popup is open** refreshes it; for example, an actions window becomes a tooltip when its berth turns read-only.
- **Boats spanning several berths** are grayed only when every visible member berth is disabled.

## Status filter

```csharp
marina.SetStatusFilter(BerthStatusFilter.Free | BerthStatusFilter.TemporarilyFree);
marina.SetStatusFilter(BerthStatus.Free, BerthStatus.Reserved);   // params overload
marina.ShowAllStatuses();
BerthStatusFilter current = marina.StatusFilter;
bool shown = marina.IsBerthVisible("A-L03");                      // exists, not hidden, passes the filter
```

Filtered-out berths keep their physical structure (finger piers) but lose the status pad, buoy, boat and label. They can't be clicked or selected, and they leave the selection when the filter changes.

## Statistics

```csharp
MarinaStatistics s = marina.GetStatistics();
// s.TotalBerths, s.Free, s.Occupied, s.Reserved, s.TemporarilyFree, s.OccupancyRate (Occupied / Total)
```

Counts include hidden and disabled berths.

## Colors

```csharp
marina.SetStatusColor(BerthStatus.Reserved, ColorRgba.FromHex("#8A4FFF"));
marina.SetDisabledColor(new ColorRgba(0.5f, 0.5f, 0.5f));
marina.SetOverlayOpacity(padOpacity: 0.6f, ghostBoatOpacity: 0.3f);
marina.ResetStatusColors();
ColorRgba occupied = marina.GetStatusColor(BerthStatus.Occupied);
```

The defaults are `StatusColorScheme.DefaultFree`, `DefaultOccupied`, `DefaultReserved`, `DefaultTemporarilyFree` and `DefaultDisabled`; `MarinaVisualizer.Colors` shows the current scheme. Status colors also tint ghost boats and set the tooltip's accent bar.
