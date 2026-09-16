# Multi-slip berths

A `MultiSlipBerth` puts **one boat across two or more slips**, for example a superyacht moored alongside a row of small slips, or a wide catamaran taking two slips bow-in. There is no upper limit on the number of slips.

## Mooring styles

| `MooringStyle` | Boat placement |
|---|---|
| `Alongside` (default) | Parallel to the dock (across the slips), next to the dock end, centered along the combined width |
| `BowIn` | Bow toward the dock like a normal slip, centered across the combined width |

- **Reference frame:** geometry uses the **first** slip in `SlipIds` (the primary slip). The combined rectangle is measured along that slip's axes, so list slips that lie in a row.
- **Finger piers:** automatic finger piers between member slips are not drawn, so the boat doesn't clip through them.
- **Explicit dividers** (`Divider` records) are always drawn. Don't put pile or boom dividers between slips you intend to combine.

## Creating a berth at runtime

```csharp
var superyacht = new Boat("SY-1", "Meltemi Star", BoatType.MotorYacht) { LengthMeters = 44, BeamMeters = 8.5f };

// Alongside (shortcut)
MultiSlipBerth berth = marina.DockAlongside(new[] { "B-L05", "B-L06", "B-L07", "B-L08", "B-L09", "B-L10", "B-L11", "B-L12" }, superyacht);

// General form
marina.AssignBoatToSlips(new[] { "C-L01", "C-L02" }, catamaran, SlipStatus.Reserved, MooringStyle.BowIn, berthId: "CAT-7");
```

- **What happens to the slips:** every member slip gets the berth's `Status`, its `Boat` and `BerthId`, and raises `SlipStatusChanged`. The boat is drawn once.
- **Ids:** `berthId` is generated from the first slip (`BERTH-{slipId}`) when null.
- **Status:** must be `Occupied`, `Reserved` or `TemporarilyFree`. To end a berth, release it.
- **Errors:** a slip already in another berth throws `InvalidOperationException`. Fewer than two slips, unknown slips or an invalid boat throw `MarinaLayoutException`.

## Updating and releasing

```csharp
marina.UpdateMultiSlipBerth("CAT-7", status: SlipStatus.Occupied);                 // arrival
marina.UpdateMultiSlipBerth("CAT-7", slipIds: new[] { "C-L01", "C-L02", "C-L03" }); // grow (slips no longer listed become Free)
marina.UpdateMultiSlipBerth(berth with { Style = MooringStyle.BowIn, Boat = biggerBoat });
marina.ReleaseMultiSlipBerth("CAT-7");                                             // all member slips become Free

MultiSlipBerth? b = marina.GetMultiSlipBerth("CAT-7");
MultiSlipBerth? ofSlip = marina.GetMultiSlipBerthForSlip("C-L02");
IReadOnlyList<MultiSlipBerth> all = marina.GetMultiSlipBerths();
```

## Interaction with the single-slip API

The visualizer keeps member slips consistent:

| Call on a member slip | Effect |
|---|---|
| `MarkTemporarilyFree`, `ReserveSlip(id, boat)`, `AssignBoat`, `SetSlipStatus(non-Free)` | Changes the **whole berth**'s status and boat |
| `ReleaseSlip`, `SetSlipStatus(Free)`, or clearing the boat (`ReserveSlip(id)` with no boat) | **Releases the berth**: all members become Free, then the change applies to that slip |
| Geometry, label or flag changes | Affect only that slip |
| `RemoveSlip` | The berth shrinks. With fewer than two slips left it dissolves, and the remaining slip keeps the boat as a normal assignment. |

## In layouts

Berths are part of `MarinaLayout.MultiSlipBerths` and are included in `GetLayout()`:

```csharp
new MarinaLayoutBuilder()
    .AddDock("B", "Dock B", new Vector2(35, -6), 0, 66, dock => dock.AddSlips(DockSide.Left, 12, 5, 11))
    .AddMultiSlipBerth(new MultiSlipBerth("BIG", new[] { "B-L10", "B-L11", "B-L12" }, superyacht, SlipStatus.Occupied, MooringStyle.Alongside))
    .Build();
```

`InitializeLayout` applies each berth's status and boat to its member slips. Status and boat values already set on those slips in the layout are overwritten.

## In events, tooltips and picking

- **`SlipSelectedEventArgs.Berth`** is the berth of the selected slip.
- **The default tooltip** adds a row such as `Berth: Alongside across B-L10, B-L11, B-L12`.
- **Clicking the boat** selects the member slip nearest the click point.
- **Highlighting:** the boat is highlighted when any member slip is selected or hovered.
