# Multi-berths

A `MultiBerth` puts **one boat across two or more berths**, for example a superyacht moored alongside a row of small berths, or a wide catamaran taking two berths bow-in. There is no upper limit on the number of berths.

## Mooring styles

| `MooringStyle` | Boat placement |
|---|---|
| `Alongside` (default) | Parallel to the pier (across the berths), next to the pier end, centered along the combined width |
| `BowIn` | Bow toward the pier like a normal berth, centered across the combined width |

- **Reference frame:** geometry uses the **first** berth in `BerthIds` (the primary berth). The combined rectangle is measured along that berth's axes, so list berths that lie in a row.
- **Finger piers:** automatic finger piers between member berths are not drawn, so the boat doesn't clip through them.
- **Explicit dividers** (`Divider` records) are always drawn. Don't put pile or boom dividers between berths you intend to combine.

## Creating a multi-berth at runtime

```csharp
var superyacht = new Boat("SY-1", "Meltemi Star", BoatType.MotorYacht) { LengthMeters = 44, BeamMeters = 8.5f };

// Alongside (shortcut)
MultiBerth multi = marina.MoorAlongside(new[] { "B-L05", "B-L06", "B-L07", "B-L08", "B-L09", "B-L10", "B-L11", "B-L12" }, superyacht);

// General form
marina.AssignBoatToBerths(new[] { "C-L01", "C-L02" }, catamaran, BerthStatus.Reserved, MooringStyle.BowIn, multiBerthId: "CAT-7");
```

- **What happens to the berths:** every member berth gets the multi-berth's `Status`, its `Boat` and `MultiBerthId`, and raises `BerthStatusChanged`. The boat is drawn once.
- **Ids:** `multiBerthId` is generated from the first berth (`MB-{berthId}`, then `MB-{berthId}-2`, ... if taken) when null. It must not be the id of a berth.
- **Status:** must be `Occupied`, `Reserved` or `TemporarilyFree`. To end a multi-berth, release it.

### Validation

One boat lies across the members, so they have to be somewhere one boat can lie:

- at least two berths, none listed twice, none empty, all existing;
- **all water berths along the same pier, or all land berths on the same land area** — never a mix of water and land, and never berths of two piers or two land areas;
- a valid boat, a status other than `Free`, a defined `MooringStyle`;
- an id that is not empty and not the id of a berth.

A berth already in another multi-berth throws `InvalidOperationException` (release or update that one first). Everything else above throws `MarinaLayoutException`, with every problem in `Errors`; `InitializeLayout` and `MarinaLayout.Validate()` report the same problems for the multi-berths of a layout.

## Updating and releasing

```csharp
marina.UpdateMultiBerth("CAT-7", status: BerthStatus.Occupied);                 // arrival
marina.UpdateMultiBerth("CAT-7", berthIds: new[] { "C-L01", "C-L02", "C-L03" }); // grow (berths no longer listed become Free)
MultiBerth b = marina.GetMultiBerth("CAT-7")!;
marina.UpdateMultiBerth(b with { Style = MooringStyle.BowIn, Boat = biggerBoat });
marina.ReleaseMultiBerth("CAT-7");                                             // all member berths become Free

MultiBerth? ofBerth = marina.GetMultiBerthFor("C-L02");
IReadOnlyList<MultiBerth> all = marina.GetMultiBerths();
```

## Interaction with the single-berth API

The visualizer keeps member berths consistent:

| Call on a member berth | Effect |
|---|---|
| `MarkTemporarilyFree`, `ReserveBerth(id, boat)`, `AssignBoat`, `SetBerthStatus(non-Free)` | Changes the **whole multi-berth**'s status and boat |
| `ReleaseBerth`, `SetBerthStatus(Free)`, or clearing the boat (`ReserveBerth(id)` with no boat) | **Releases the multi-berth**: all members become Free, then the change applies to that berth |
| Geometry, label or flag changes | Affect only that berth |
| `RemoveBerth` | The multi-berth shrinks. With fewer than two berths left it dissolves, and the remaining berth keeps the boat as a normal assignment. |

## In layouts

Multi-berths are part of `MarinaLayout.MultiBerths` and are included in `GetLayout()`:

```csharp
new MarinaLayoutBuilder()
    .AddPier("B", "Pier B", new Vector2(35, -6), 0, 66, pier => pier.AddBerths(PierSide.Left, 12, 5, 11))
    .AddMultiBerth(new MultiBerth("BIG", new[] { "B-L10", "B-L11", "B-L12" }, superyacht, BerthStatus.Occupied, MooringStyle.Alongside))
    .Build();
```

`InitializeLayout` applies each multi-berth's status and boat to its member berths. Status and boat values already set on those berths in the layout are overwritten.

## In events, tooltips and picking

- **`BerthSelectedEventArgs.MultiBerth`** is the multi-berth of the selected berth.
- **The default tooltip** adds a row such as `Boat lies: Alongside across B-L10, B-L11, B-L12`.
- **Clicking the boat** selects the member berth nearest the click point.
- **Highlighting:** the boat is highlighted when any member berth is selected or hovered.
