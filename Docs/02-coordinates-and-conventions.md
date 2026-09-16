# Coordinates and conventions

## World and plan coordinates

- **World space** is right-handed and **Y-up**, measured in **meters**. The calm water surface is the plane Y = 0.
- **Plan coordinates** (`Vector2`) describe positions on the water seen from above: `X` = world X, `Y` = world Z. Every dock, slip, divider and land position is in plan coordinates.
- `MarinaMath.ToWorld(plan, y)` and `MarinaMath.ToPlan(world)` convert between the two.

## Headings

A heading is an angle in degrees, rotating about +Y:

| Heading | Direction (plan) |
|---|---|
| 0° | +Z (`(0, 1)`) |
| 90° | +X (`(1, 0)`) |
| 180° | −Z |
| 270° / −90° | −X |

- **`MarinaMath.HeadingToDirection(h)`** = `(sin h, cos h)`. This is the "forward" vector: `Dock.Direction`, `Slip.Forward`.
- **`MarinaMath.HeadingToRight(h)`** = `(cos h, −sin h)`. This is the heading's local **+X** axis: `Dock.Right`, `Slip.Right`, `OrientedRect.Right`. For heading 0° it is +X.

> "Right" is an axis name, not a promise about what appears on the right of the screen. The camera can look from any side. In the Top Down preset (+Z at the top of the screen), +X appears on the **left**.

### What headings mean for each element

| Element | Heading means |
|---|---|
| `Dock.HeadingDegrees` | Direction from the shore end (`Start`) to the sea end (`End`) |
| `Slip.HeadingDegrees` | Direction a moored boat's **bow** points, normally toward the dock |
| `Divider.HeadingDegrees` | Direction from `Start`, normally away from the dock |
| `OrientedRect.HeadingDegrees` | Direction of the rectangle's length axis |

### Sizes

- **Dock:** `Length` runs along the heading and `Width` across it.
- **Slip:** `Length` runs along the heading (bow to stern) and `Width` across it.
- **`OrientedRect.Size`:** `X` is the width (across) and `Y` is the length (along).

### Dock sides

`DockSide.Right` is the side `Dock.Right` points to. `DockSide.Left` is the opposite side. Generated slip ids use `L` and `R`: `A-L01`, `A-R01`.

## Ids

Dock, slip, divider and berth ids are **case-insensitive** (`"a-l01"` finds `"A-L01"`) and must be unique within their kind. The visualizer stores the id as first given, and APIs that return ids use that stored spelling. Ids are typically your ERP keys.

## Immutable snapshots

- **Immutable records:** `Dock`, `Slip`, `Boat`, `Divider`, `MultiSlipBerth`, `MarinaLayout`, `LandArea`, `SlipUpdate` and `DockUpdate`. Use `with` expressions to derive changed copies.
- **Snapshots:** getters (`GetSlip`, `GetSlips`, `SelectedSlips`, ...) and event arguments return snapshots from the moment of the call. Changing marina state always goes through the API (`UpdateSlip`, `AssignBoat`, ...).
- **The one mutable exception** is `Slip.ExternalData` (a `SlipDataBag`). Every snapshot of a slip shares the same bag, so host data written there persists. See [Selection, tooltips and actions](06-selection-tooltips-actions.md#external-data-on-slips).

```csharp
var slip = marina.GetSlip("A-L03")!;
marina.UpdateSlip(slip with { Label = "A-3 (long)", Length = 15 });   // replace
marina.UpdateSlip(new SlipUpdate("A-L03") { MaxDraft = 3.2f });       // partial
```

## Threading

`MarinaVisualizer` is **not thread-safe** and is affine to the UI thread that owns the view.

- **WinForms:** marshal with `control.BeginInvoke(() => marina.AssignBoat(...))`.
- **Blazor:** use `InvokeAsync(() => ...)` for data arriving from timers or SignalR callbacks.

Events are raised **synchronously**, on the calling thread, **after** the change has been applied. A handler can read the new state and call back into the API. Re-entrancy is handled; for example, updating a slip from inside `SlipSelected` doesn't loop.

## Validation and exceptions

| Exception | When |
|---|---|
| `MarinaLayoutException` | Invalid definitions (non-positive sizes, duplicate ids, unknown dock/slip references, invalid berth). `Errors` lists every problem. `InitializeLayout` validates everything before changing anything. |
| `KeyNotFoundException` | Updating a dock, slip, divider or berth that doesn't exist |
| `InvalidOperationException` | Adding an existing id; removing a dock with slips when `removeSlips: false`; putting a slip in two berths |
| `ArgumentNullException` / `ArgumentOutOfRangeException` | Null arguments or undefined enum values |

Lookups (`GetSlip`, `GetDock`, ...) return `null` instead of throwing. Operations that "try" (`SelectSlip`, `FocusSlips`, `RemoveSlip`, `InvokeSlipAction`, ...) return `false`. `BatchUpdate` collects failures in its result instead of throwing.

You can check a layout before loading it:

```csharp
IReadOnlyList<string> problems = layout.Validate();
```
