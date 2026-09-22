# Coordinates and conventions

## World and plan coordinates

- **World space** is right-handed and **Y-up**, measured in **meters**. The calm water surface is the plane Y = 0.
- **Plan coordinates** (`Vector2`) describe positions on the water seen from above: `X` = world X, `Y` = world Z. Every pier, berth, divider and land position is in plan coordinates.
- `MarinaMath.ToWorld(plan, y)` and `MarinaMath.ToPlan(world)` convert between the two.

## Headings

A heading is an angle in degrees, rotating about +Y:

| Heading | Direction (plan) |
|---|---|
| 0° | +Z (`(0, 1)`) |
| 90° | +X (`(1, 0)`) |
| 180° | −Z |
| 270° / −90° | −X |

- **`MarinaMath.HeadingToDirection(h)`** = `(sin h, cos h)`. This is the "forward" vector: `Pier.Direction`, `Berth.Forward`.
- **`MarinaMath.HeadingToRight(h)`** = `(cos h, −sin h)`. This is the heading's local **+X** axis: `Berth.Right`, `OrientedRect.Right`, `Divider.Right`. For heading 0° it is +X.
- **`Pier.Right`** = `−HeadingToRight(h)`: the right-hand side of someone standing at the pier's start (the shore) looking toward its end (the sea). For heading 0° it is −X.

> "Right" is an axis name, not a promise about what appears on the right of the screen. The camera can look from any side. In the Top Down preset (+Z at the top of the screen), +X appears on the **left**.

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

`PierSide.Right` is the right-hand side looking from the pier's start toward its end (where `Pier.Right` points), `PierSide.Left` the left-hand side. Generated berth ids use `L` and `R`: `A-L01`, `A-R01`.

## Ids

Pier, berth, divider and berth ids are **case-insensitive** (`"a-l01"` finds `"A-L01"`) and must be unique within their kind. The visualizer stores the id as first given, and APIs that return ids use that stored spelling. Ids are typically your ERP keys.

## Immutable snapshots

- **Immutable records:** `Pier`, `Berth`, `Boat`, `Divider`, `MultiBerth`, `MarinaLayout`, `LandArea`, `BerthUpdate` and `PierUpdate`. Use `with` expressions to derive changed copies.
- **Snapshots:** getters (`GetBerth`, `GetBerths`, `SelectedBerths`, ...) and event arguments return snapshots from the moment of the call. Changing marina state always goes through the API (`UpdateBerth`, `AssignBoat`, ...).
- **The one mutable exception** is `Berth.ExternalData` (a `MarinaDataBag`). Every snapshot of a berth shares the same bag, so host data written there persists. See [Selection, tooltips and actions](06-selection-tooltips-actions.md#external-data-on-berths).

```csharp
var berth = marina.GetBerth("A-L03")!;
marina.UpdateBerth(berth with { Label = "A-3 (long)", Length = 15 });   // replace
marina.UpdateBerth(new BerthUpdate("A-L03") { MaxDraft = 3.2f });       // partial
```

## Threading

`MarinaVisualizer` is **not thread-safe** and is affine to the UI thread that owns the view.

- **WinForms:** marshal with `control.BeginInvoke(() => marina.AssignBoat(...))`.
- **Blazor:** use `InvokeAsync(() => ...)` for data arriving from timers or SignalR callbacks.

Events are raised **synchronously**, on the calling thread, **after** the change has been applied. A handler can read the new state and call back into the API. Re-entrancy is handled; for example, updating a berth from inside `BerthSelected` doesn't loop.

## Validation and exceptions

| Exception | When |
|---|---|
| `MarinaLayoutException` | Invalid definitions (non-positive sizes, duplicate ids, unknown pier/berth references, invalid berth). `Errors` lists every problem. `InitializeLayout` validates everything before changing anything. |
| `KeyNotFoundException` | Updating a pier, berth, divider or berth that doesn't exist |
| `InvalidOperationException` | Adding an existing id; removing a pier with berths when `removeBerths: false`; putting a berth in two berths |
| `ArgumentNullException` / `ArgumentOutOfRangeException` | Null arguments or undefined enum values |

Lookups (`GetBerth`, `GetPier`, ...) return `null` instead of throwing. Operations that "try" (`SelectBerth`, `FocusBerths`, `RemoveBerth`, `InvokeBerthAction`, ...) return `false`. `BatchUpdate` collects failures in its result instead of throwing.

You can check a layout before loading it:

```csharp
IReadOnlyList<string> problems = layout.Validate();
```
