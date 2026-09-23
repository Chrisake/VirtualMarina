# Coordinates and conventions

## World and plan coordinates

- **World space** is right-handed and **Y-up**, measured in **meters**. The calm water surface is the plane Y = 0.
- **Plan coordinates** (`Vector2`) describe positions on the water seen from above: `X` = world X, `Y` = world Z. Every pier, berth, divider and land position is in plan coordinates.
- `MarinaMath.ToWorld(plan, y)` and `MarinaMath.ToPlan(world)` convert between the two.
- Seen from above with north at the top — the built-in **Top Down** camera preset — +X is east (right) and **+Z is south (down)**: plan coordinates are mirrored compared with the usual maths drawing.

[Coordinate conventions, exactly](20-coordinate-conventions.md) is the reference behind this page: every direction the API names, what it points to on the map, and the two places where the same word means opposite things.

## Headings

A heading is an angle in degrees, rotating about +Y:

| Heading | Direction (plan) |
|---|---|
| 0° | +Z (`(0, 1)`), south on the map |
| 90° | +X (`(1, 0)`), east |
| 180° | −Z, north |
| 270° / −90° | −X, west |

A heading is **not** a compass bearing: it counts from south, so a compass bearing `b` is the heading `180 − b`.

- **`MarinaMath.HeadingToDirection(h)`** = `(sin h, cos h)`. This is the "forward" vector: `Pier.Direction`, `Berth.Forward`.
- **`MarinaMath.HeadingToRight(h)`** = `(cos h, −sin h)`. This is the heading's local **+X** axis: `LocalX` on `Berth`, `Pier`, `Divider` and `OrientedRect`, and also `Berth.Right`, `OrientedRect.Right` and `Divider.Right`. For heading 0° it is +X.
- **`Pier.Right`** = `−HeadingToRight(h)` = `−Pier.LocalX`: the right-hand side of someone standing at the pier's start (the shore) looking toward its end (the sea). For heading 0° it is −X.
- **`Pier.SideNormal(side)`** is the direction of a side of the pier, where its berths lie: `LocalX` for `PierSide.Left`, `Right` for `PierSide.Right`.

> "Right" is an axis name, not a promise about what appears on the right of the screen, and `Pier.Right` and `Berth.Right` point opposite ways for the same heading. Prefer `LocalX` and `Pier.SideNormal(side)` in new code; the `Right` names are kept because designs and host code depend on them. The camera can look from any side: in the **Top Down** preset (north up) +X appears on the right, while from `CameraAngle.TopDown` (+Z at the top of the screen) it appears on the **left**.

### What headings mean for each element

| Element | Heading means |
|---|---|
| `Pier.HeadingDegrees` | Direction from the shore end (`Start`) to the sea end (`End`) |
| `Berth.HeadingDegrees` | Direction a moored boat's **bow** points, normally toward the pier |
| `Divider.HeadingDegrees` | Direction from `Start`, normally away from the pier |
| `OrientedRect.HeadingDegrees` | Direction of the rectangle's length axis |

### Sizes

- **Pier:** `Length` runs along the heading and `Width` across it.
- **Berth:** `Length` runs along the heading (bow to stern) and `Width` across it.
- **`OrientedRect.Size`:** `X` is the width (across) and `Y` is the length (along).

### Pier sides

`PierSide.Right` is the right-hand side looking from the pier's start toward its end (where `Pier.Right` points), `PierSide.Left` the left-hand side. Generated berth ids use `L` and `R`: `A-L01`, `A-R01`. A pier with berths along one side only has no left and right to tell apart: its berths are `A-01`, `A-02`, ... Generated dividers are `A-L-D01` and `A-R-D01`, or `A-D01` on a single-sided pier.

## Ids

Pier, berth, divider, multi-berth and land-area ids are **case-insensitive** (`"a-l01"` finds `"A-L01"`) and must be unique within their kind. The visualizer stores the id as first given, and APIs that return ids use that stored spelling. Ids are typically your ERP keys.

## Immutable snapshots

- **Immutable records:** `Pier`, `Berth`, `Boat`, `Divider`, `MultiBerth`, `MarinaLayout`, `LandArea`, `Shoreline`, `MarineTraffic`, `BerthUpdate` and `PierUpdate`. Use `with` expressions to derive changed copies.
- **Value equality:** records compare by value, collections included — a `LandArea` compares its outline and trees point by point, a `Boat` or `Berth` its `Metadata` entry by entry, a `MarinaLayout` its piers, berths and the rest in order. Each collection is a private copy, so `land with { Points = myList }` does not follow later changes to `myList`. The one reference-compared part is `Berth.ExternalData` (below): two snapshots of the same berth are equal, two berths built separately are not.
- **Boat sizes:** a `Boat` whose `LengthMeters` or `BeamMeters` was never set takes the nominal size of its `Type`, so `boat with { Type = BoatType.JetSki }` shrinks it. `HasCustomLength` and `HasCustomBeam` say whether a size was given.
- **Snapshots:** getters (`GetBerth`, `GetBerths`, `SelectedBerths`, ...) and event arguments return snapshots from the moment of the call. Changing marina state always goes through the API (`UpdateBerth`, `AssignBoat`, ...).
- **The one mutable exception** is `Berth.ExternalData` (a `MarinaDataBag`). Every snapshot of a berth shares the same bag, so host data written there persists. See [Selection, tooltips and actions](06-selection-tooltips-actions.md#external-data-on-berths).

```csharp
var berth = marina.GetBerth("A-L03")!;
marina.UpdateBerth(berth with { Label = "A-3 (long)", Length = 15 });   // replace
marina.UpdateBerth(new BerthUpdate("A-L03") { MaxDraft = 3.2f });       // partial
```

## Threading

`MarinaVisualizer` is **not thread-safe** and is affine to the UI thread that owns the view.

- **WinForms:** marshal with `control.BeginInvoke(() => marina.AssignBoat(...))`. If a change does arrive on another thread anyway, `MarinaViewControl` marshals what it does in response (redrawing, the popup, the events it forwards) onto its own thread, but the visualizer's state is still unguarded: do not rely on it.
- **Blazor:** use `InvokeAsync(() => ...)` for data arriving from timers or SignalR callbacks.

Events are raised **synchronously**, on the calling thread, **after** the change has been applied. A handler can read the new state and call back into the API. Re-entrancy is handled; for example, updating a berth from inside `BerthSelected` doesn't loop.

## Validation and exceptions

| Exception | When |
|---|---|
| `MarinaLayoutException` | Invalid definitions (non-positive sizes, duplicate ids, unknown pier/berth references, an invalid multi-berth or shoreline). `Errors` lists every problem. `InitializeLayout` validates everything before changing anything. |
| `KeyNotFoundException` | Updating a pier, berth, divider or multi-berth that doesn't exist |
| `InvalidOperationException` | Adding an existing id; removing a pier with berths when `removeBerths: false`; putting a berth in two multi-berths |
| `ArgumentNullException` / `ArgumentOutOfRangeException` | Null arguments or undefined enum values |

Lookups (`GetBerth`, `GetPier`, ...) return `null` instead of throwing. Operations that "try" (`SelectBerth`, `FocusBerths`, `RemoveBerth`, `InvokeBerthAction`, ...) return `false`. `BatchUpdate` collects failures in its result instead of throwing.

You can check a layout before loading it:

```csharp
IReadOnlyList<string> problems = layout.Validate();
```

Messages (in `Errors`, in `BatchUpdateResult.Failures`, in exception text) come from the resource files, so they are in the [current UI culture](15-localization.md), and counts are worded for the number ("1 berth", "3 berths"). Do not parse them; the kind of exception and the ids you passed in are the stable part.
