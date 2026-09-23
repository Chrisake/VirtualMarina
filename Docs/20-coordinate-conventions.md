# Coordinate conventions, exactly

[Coordinates and conventions](02-coordinates-and-conventions.md) introduces plan coordinates and headings. This page is the reference behind it: every direction the API names, what it points to on the map, and the two places where the same word means opposite things. Every statement here is pinned by a unit test (`CoordinateConventionTests`), so it cannot drift from the code.

## The map

| | Direction | On the map |
|---|---|---|
| World +X / plan +X | east | right |
| World −X / plan −X | west | left |
| World +Z / plan +Y | **south** | down |
| World −Z / plan −Y | **north** | up |
| World +Y | up, out of the water | toward the viewer |

"On the map" means seen from above with north at the top, which is how the built-in **Top Down** camera preset shows it (yaw 0°: the camera stands on the south side and looks north). World space is right-handed and Y-up, so plan coordinates — `Vector2(x, y)` with `y` = world Z — are **mirrored** compared with the usual maths drawing: +y runs *down* the map. Keep that in mind whenever "left" or "right", "clockwise" or "anticlockwise" is read off a plan-coordinate formula.

> `CameraAngle.TopDown` (yaw 180°) is the other straight-down view: it stands north and looks south, so +Z is at the top of the screen and east is on the left. The **Top Down** preset and `CameraAngle.TopDown` are not the same view.

## Headings

A heading is in degrees, turning from +Z toward +X (about world +Y):

| Heading | Plan direction | On the map |
|---|---|---|
| 0° | `(0, 1)`, +Z | south |
| 90° | `(1, 0)`, +X | east |
| 180° | `(0, −1)`, −Z | north |
| 270° or −90° | `(−1, 0)`, −X | west |

`MarinaMath.HeadingToDirection(h)` is `(sin h, cos h)` and `MarinaMath.DirectionToHeading` is its inverse (−180°…180°).

**A heading is not a compass bearing.** A compass bearing counts from north; a heading counts from south. A compass bearing `b` is the heading `180 − b`: north (0°) is heading 180°, east (90°) is heading 90°. The same convention is used by

- `Pier.HeadingDegrees` (shore end toward sea end), `Berth.HeadingDegrees` (the way a moored boat's bow points), `Divider.HeadingDegrees`, `OrientedRect.HeadingDegrees`, `LandAreaBuilder.AddBerths`' row heading;
- `TrafficVessel.HeadingDegrees` — the passing traffic's bows;
- `LightingSettings.SetSunAngles(azimuth, elevation)` — azimuth 0° puts the sun in the south (+Z), 90° in the east.

Camera **yaw** is different in kind: it says where the camera *stands* around its target, not where it looks. Yaw 0° stands on the +Z (south) side looking north, 90° stands east looking west, 180° stands north looking south.

## The two meanings of "right"

`MarinaMath.HeadingToRight(h)` is `(cos h, −sin h)`: the heading's **local +X axis**. For heading 0° it is +X (east). Because plan coordinates are mirrored, this axis is on the **left** of someone facing along the heading and standing upright (+Y up).

| Member | Vector | Heading 0° (facing south) | Meaning |
|---|---|---|---|
| `Berth.Right`, `Berth.LocalX` | `(cos h, −sin h)` | +X, east | local +X; the **left** of a boat's bow direction |
| `Divider.Right`, `Divider.LocalX` | `(cos h, −sin h)` | +X, east | local +X; left looking along the divider |
| `OrientedRect.Right`, `OrientedRect.LocalX` | `(cos h, −sin h)` | +X, east | the width axis |
| `Pier.LocalX` | `(cos h, −sin h)` | +X, east | local +X; toward `PierSide.Left` |
| **`Pier.Right`** | **`(−cos h, sin h)`** | **−X, west** | the true right-hand side walking from shore end to sea end |
| `Pier.SideNormal(PierSide.Left)` | `= Pier.LocalX` | +X, east | out across the left side, where its berths lie |
| `Pier.SideNormal(PierSide.Right)` | `= Pier.Right` | −X, west | out across the right side |

So `Pier.Right == −Pier.LocalX`, while `Berth.Right == Berth.LocalX`. Both names are kept because designs, files and host code depend on them. In new code prefer the names that cannot be misread: **`LocalX`** for the axis, and **`Pier.SideNormal(side)`** for "the direction of that side of the pier".

`PierSide.Left` and `PierSide.Right` are the walker's left and right, looking from `Pier.Start` toward `Pier.End`: for a pier running south (heading 0°) the left side is east and the right side west; for a pier running north (heading 180°) the left side is west and the right side east. Generated berths lie along `SideNormal(side)` and point their bows back at the pier (`Berth.Forward == −SideNormal(side)`); generated dividers run along `SideNormal(side)`.

## Shoreline sides

`Shoreline.LandOnLeft` means left **in plan coordinates**: the side reached by turning the direction of travel from +X toward +Z. For a line walked east that is +Z — **south**, which on the map is the walker's *right*. It is the same side `Pier.Right` points to for a pier running the same way. The name stays because saved designs depend on it; `Shoreline.LandSideNormal(segment)` gives the direction of the land from any drawn segment, so code does not have to reason about it:

```csharp
var coast = new Shoreline(new[] { new Vector2(-500, -40), new Vector2(500, -40) }, landOnLeft: true);
coast.LandSideNormal(0);   // (0, 1): the land is to the south
```

## Turning and winding

- `OrientedRect.GetCorners()` returns back-left, back-right, front-right, front-left in terms of `LocalX`: counter-clockwise in plan coordinates (a positive `PolygonMath.SignedArea`), which is **clockwise on the map**.
- A land outline may be wound either way.
- Increasing a heading turns **anticlockwise on the map** (south → east → north), which is clockwise in the mirrored plan formula.

## Quick checks

```csharp
MarinaMath.HeadingToDirection(90)                    // (1, 0): east
new Pier("A", "A", Vector2.Zero, 0, 40).Right        // (-1, 0): west, the right hand walking south
new Pier("A", "A", Vector2.Zero, 0, 40).SideNormal(PierSide.Left)   // (1, 0): east
new Berth("B", "A", Vector2.Zero, 0, 12, 5).Right    // (1, 0): east — the local +X, not the boat's right hand
```
