# Multi-berths

A `MultiBerth` puts **one boat across two or more berths**, for example a superyacht moored alongside a row of small berths, or a wide catamaran taking two berths bow-in. There is no upper limit on the number of berths.

## Mooring styles

| `MooringStyle` | Boat placement |
|---|---|
| `Alongside` (default) | Parallel to the pier (across the berths), next to the pier end, centered along the combined width |
| `BowIn` | Bow toward the pier like a normal berth, centered across the combined width |

- **Reference frame:** geometry uses the **first** berth in `BerthIds` (the primary berth). The combined rectangle is measured along that berth's axes, so list berths that lie in a row.
- **Finger piers:** automatic finger piers between member berths are not drawn, so the boat doesn't clip through them.
- **Explicit dividers** (`Divider` records) are always drawn. Don't put pile or boom dividers between berths you intend to combine: `ConnectedBerthIds` says which berths can be (see below).

## Connected berths

Every berth carries `ConnectedBerthIds`: the berths right beside it that one boat can share it with, and so the berths a multi-berth can join to it. The visualizer works them out from the layout and keeps them up to date as berths and dividers come, go and move; they are written to the marina file too. Two berths are connected when:

- they are in the same place: along the same pier, or ashore on the same land area;
- they face the same way (within 10°);
- their long sides face each other: level along their length (the shorter one at least half beside the other), with no more than 1.5 m of water between them;
- neither has finger piers of its own (`HasFingerPiers`), which stand along both long sides;
- no divider of any type — finger pier, piles, boom or a single pile — stands on the boundary between them. A divider is a fixed obstacle, so no boat can lie across it.

Connections come in pairs: when B is in A's list, A is in B's. A row of berths drawn with the designer starts out connected end to end, and its [divider tool](12-designer.md#placing-dividers) parts them where the marina has dividers — every other boundary, for example, for berths that come in pairs. `MarinaLayout.WithBerthConnections()` works them out for a layout built in code without a visualizer.

```csharp
// Offer a wide boat the berths it could take along with the one chosen.
var berth = marina.GetBerth("B-L05")!;
foreach (var mateId in berth.ConnectedBerthIds) Console.WriteLine($"{berth.Id} + {mateId}");
```

The visualizer does not check a multi-berth's members against their connections: a host with a reason to join berths the layout keeps apart still can.

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
