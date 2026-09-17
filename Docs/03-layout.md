# Layout: docks, slips, dividers and land

A marina is a `MarinaLayout`:

| Property | Contents |
|---|---|
| `Name` | Marina name (`IMarinaVisualizer.MarinaName`) |
| `Docks` | `Dock` records |
| `Slips` | `Slip` records, each referencing a dock by `DockId`, or a land area by `LandAreaId` for [land slips](#land-slips) |
| `Dividers` | Finger piers, pile rows and booms between slips |
| `MultiSlipBerths` | Boats spanning several slips (see [Multi-slip berths](05-multi-slip-berths.md)) |
| `LandAreas` | Polygon quays, breakwaters and lawns (see [Land areas](#land-areas)) |

Load it with `InitializeLayout(layout)`. This replaces everything, clears the selection, rebuilds the camera presets and resets the camera. Save the current state with `GetLayout()`, which round-trips through `InitializeLayout`.

## Building a layout

### With `MarinaLayoutBuilder` (slips positioned along docks)

```csharp
var layout = new MarinaLayoutBuilder("VirtualMarina Harbor")
    .AddLandArea(new LandArea("quay", new[] { new Vector2(-130, -32), new Vector2(130, -32), new Vector2(130, -6), new Vector2(-130, -6) }, 1.0f))
    .AddLandArea(new LandArea("yard", yardOutline, 1.0f) { Name = "Boatyard" }, yard => yard
        .AddSlips("Y-", firstPosition: new Vector2(-120, -45), rowHeadingDegrees: 90, count: 8, slipWidth: 5.5f, slipLength: 12))
    .AddDock("A", "Dock A", start: new Vector2(80, -6), headingDegrees: 0, length: 72, dock => dock
        .AddSlips(DockSide.Left, count: 12, slipWidth: 5.5f, slipLength: 13,
                  customize: (i, slip) => slip with { Status = SlipStatus.Occupied, Boat = boats[i] })
        .AddSlips(DockSide.Right, count: 12, slipWidth: 5.5f, slipLength: 13, dividers: DividerType.Piles)
        .AddSlip("A-GUEST", DockSide.Right, offsetAlong: 68, slipWidth: 8, slipLength: 16),
        width: 3.5f, type: DockType.Concrete)
    .AddMultiSlipBerth(new MultiSlipBerth("BIG", new[] { "A-L10", "A-L11", "A-L12" }, superyacht))
    .Build();
```

**`DockBuilder.AddSlips(side, count, slipWidth, slipLength, customize, startOffset = 2, gap = 0, dividers = null)`**
- Slips are perpendicular to the dock with their bows toward it.
- Ids are `{DockId}-L01…` or `{DockId}-R01…`, and numbering continues across calls on the same side.
- `startOffset` is the distance from the dock's start to the first slip (it only applies to the first call on a side).
- `dividers`: when set, a divider of that type is generated at every slip boundary and the slips' automatic finger piers are turned off.

**`DockBuilder.AddSlip(id, side, offsetAlong, slipWidth, slipLength, customize)`** adds one slip at an explicit distance from the dock's start.

**`DockBuilder.AddSlip(Slip)`** and **`DockBuilder.AddDivider(Divider)`** add absolutely positioned elements (the `DockId` is set for you).

**`LandAreaBuilder.AddSlip(id, position, headingDegrees, length, width, customize)`** and **`LandAreaBuilder.AddSlips(idPrefix, firstPosition, rowHeadingDegrees, count, slipWidth, slipLength, customize, gap = 0.5, boatHeadingDegrees = row − 90°)`** add [land slips](#land-slips) to the land area passed to `MarinaLayoutBuilder.AddLandArea(landArea, configure)`. Row ids are `{idPrefix}01…` and continue across calls with the same prefix.

**`MarinaLayoutBuilder.AddSlip`, `AddDivider` and `AddMultiSlipBerth`** add elements that aren't tied to the dock callback.

### With explicit positions

Every element can be placed by position, size and orientation:

```csharp
var dock  = Dock.FromCenter("E", "Dock E", center: new Vector2(150, 30), length: 60, width: 3, headingDegrees: 0, DockType.FloatingWooden);
var slip  = new Slip("E-01", "E", center: new Vector2(142.5f, 10), headingDegrees: 90, length: 12, width: 5);   // left of E, bow toward the dock
var piles = new Divider("E-D1", start: new Vector2(148.5f, 7.5f), headingDegrees: -90, length: 12, DividerType.Piles) { DockId = "E" };

var layout = new MarinaLayout { Name = "Custom", Docks = new[] { dock }, Slips = new[] { slip }, Dividers = new[] { piles } };
```

### With `SlipGenerator` (compute geometry, add yourself)

| Method | Returns |
|---|---|
| `SlipGenerator.AlongDock(dock, side, count, slipWidth, slipLength, startOffset, gap, firstNumber)` | A row of slips |
| `SlipGenerator.AtDock(dock, id, side, offsetAlong, slipWidth, slipLength)` | One slip |
| `SlipGenerator.DividersAlongDock(dock, side, count, slipWidth, slipLength, type, startOffset, gap)` | Dividers at the boundaries of such a row |

Useful when your ERP stores only slip numbers and dock dimensions.

## Docks

| Member | Meaning |
|---|---|
| `new Dock(id, name, start, headingDegrees, length, width = 2.5, type = FloatingWooden)` | Created from the shore-end point |
| `Dock.FromCenter(id, name, center, length, width, headingDegrees, type)` | Created from the center point |
| `Start`, `End`, `Center`, `Direction`, `Right`, `Bounds` | Geometry (plan coordinates) |
| `Type` | `DockType`: controls the look and the default deck height |
| `DeckHeight` | Deck height above water; defaults from `Type` unless set |
| `PilingSpacing` | Column spacing (`Concrete`) or cleat spacing (`FloatingConcrete`); default 6 m |
| `BerthingSides` | `DockSides.Both` (default), `Left` or `Right`: see [Single-sided docks](#single-sided-docks) |
| `HasBerthsOn(side)` | True when boats can berth on that side |
| `WithCenter(center)` | Copy moved to a new center |

| `DockType` | Rendering | Default deck height |
|---|---|---|
| `FloatingWooden` (default) | Plank deck with seams, walers, dark pontoon floats | 0.5 m |
| `FloatingConcrete` | Monolithic pontoon, rubber fenders, section joints, cleats | 0.55 m |
| `Concrete` | Fixed slab on square columns, curbs, bollards | 1.1 m |

Automatic finger piers of slips and finger-pier dividers take the dock's material: wood for floating wooden docks, concrete otherwise.

### Single-sided docks

A dock that runs along the edge of a land area (a quay wall, a pontoon moored against a mole) only takes boats on its water side. Set `BerthingSides` to that side:

```csharp
// Heading 0° runs along +Z, so Left is −X: the land is on the +X side.
var pontoon = new Dock("E", "Dock E (along the mole)", new Vector2(110.75f, -6), 0, 50, 2.5f, DockType.FloatingConcrete)
{
    BerthingSides = DockSides.Left,
};
builder.AddDock(pontoon, dock => dock.AddSlips(DockSide.Left, 9, 5, 10));
```

- Mooring points are drawn on the open side only: bollards (`Concrete`), cleats and rubber fenders (`FloatingConcrete`).
- `DockBuilder.AddSlips`, `SlipGenerator.AtDock`, `AlongDock` and `DividersAlongDock` throw `InvalidOperationException` for the closed side. Slips placed by absolute position aren't checked, so guest berths off the dock's end still work.
- `DockUpdate.BerthingSides` changes it at runtime; existing slips are not moved or removed.

## Slips

| Member | Meaning |
|---|---|
| `new Slip(id, dockId, center, headingDegrees, length, width)` | A Free slip along a dock |
| `Slip.OnLand(id, landAreaId, position, headingDegrees = 0, length = 12, width = 5)` | A Free [land slip](#land-slips) |
| `DockId` / `LandAreaId` | Exactly one is set; `IsOnLand` is true for land slips |
| `Label` / `DisplayName` | Display text (`DisplayName` falls back to `Id`) |
| `Center`, `HeadingDegrees`, `Length`, `Width`, `Forward`, `Right`, `Bounds` | Geometry of the water area |
| `MaxDraft` | Optional; shown in the default tooltip |
| `Status`, `Boat` | See [Slip status, boats and flags](04-status-and-flags.md) |
| `HasFingerPiers` | Draw simple finger piers on both long sides (default true) |
| `IsVisible`, `IsDisabled`, `IsReadOnly` | Interaction flags |
| `BerthId` | Set by the visualizer for members of a multi-slip berth |
| `Metadata` | Read-only string attributes you supply |
| `ExternalData` | Mutable host data bag (see [Selection, tooltips and actions](06-selection-tooltips-actions.md#external-data-on-slips)) |

Each slip's water area gets a translucent status pad, a status buoy at the seaward end and, when it has a boat, the boat model.

## Dividers

| Member | Meaning |
|---|---|
| `new Divider(id, start, headingDegrees, length, type = FingerPier)` / `Divider.FromCenter(...)` | Created from the start point or the center |
| `DockId` | Optional owning dock: sets deck height and material, and the divider is removed with the dock |
| `Width` | Finger width, boom float size or pile diameter (default 0.8 m) |
| `Spacing` | Distance between piles or boom floats (default 4 m) |

| `DividerType` | Rendering |
|---|---|
| `FingerPier` | Narrow walkable pier with a pile at its end |
| `Piles` | Row of mooring piles (wood, or steel next to concrete docks) |
| `Boom` | Dark line with orange floats (yellow at the ends) bobbing on the water |

## Land areas

A land area is a polygon outline in plan coordinates with one top height for the whole area:

```csharp
var mole = new LandArea("east-mole", new[]
{
    new Vector2(112, -6), new Vector2(150, -6), new Vector2(150, 38), new Vector2(138, 50), new Vector2(112, 50),
}, height: 1.0f, LandKind.Quay)
{
    Name = "East mole",
};

// Rectangles still work.
var lawn = new LandArea("lawn", new OrientedRect(new Vector2(95, -24), new Vector2(50, 12), 0), 1.15f, LandKind.Grass);
```

| Member | Meaning |
|---|---|
| `Id` | Unique (case-insensitive); land slips reference it |
| `Name` / `DisplayName` | Optional display name, used in tooltips (`DisplayName` falls back to `Id`) |
| `Points` | Outline, at least 3 points. Convex or concave, either winding, edges must not cross, don't repeat the first point |
| `Height` | Top surface above the water, 0–50 m |
| `Kind` | `LandKind`, see below |
| `Area`, `Contains(point)`, `GetAxisAlignedBounds()` | Geometry helpers |

| `LandKind` | Rendering |
|---|---|
| `Quay` (default) | Solid light concrete block: flat top at `Height`, walls down into the water |
| `Grass` | Solid green block |
| `Breakwater` | Rubble mound: the outline is filled with irregular rocks that reach `Height` in the middle and slope down to the water at the edges |

Each land area gets its own world-space mesh (`MeshIds.ForLand(index)`, built by `LandMeshFactory`) when the layout is loaded. Land areas themselves aren't interactive; read them with `GetLandArea(id)` and `GetLandAreas()`.

`PolygonMath` has the helpers used for outlines: `SignedArea`, `Contains`, `DistanceToBoundary`, `IsSimple` and `Triangulate`.

## Land slips

A land slip is a spot on a land area where a boat is stored or maintained ashore (boatyard, hard standing, maintenance area). It is an ordinary `Slip` with `LandAreaId` set instead of `DockId`, so status, boats, flags, selection, tooltips, actions, labels, focus, filters and events all work the same, with the same status colors.

```csharp
var spot = Slip.OnLand("Y-01", "yard", position: new Vector2(-120, -45), headingDegrees: 180, length: 12, width: 5.5f)
    with { Status = SlipStatus.Occupied, Boat = boat };
marina.AddSlip(spot);

IReadOnlyList<Slip> stored = marina.GetSlipsByLandArea("yard");
```

- **Drawn on the land:** a status pad on the surface, a status post with a colored ball at the rear (instead of a buoy), and the name on the ground when labels are on. No finger piers.
- **Boats ashore** are centered on the spot, don't bob, and rest on keel blocks and cradle stands with the keel `SlipPlacement.CradleHeight` (0.7 m) above the land. Reserved and temporarily free spots show the usual ghost boat.
- **Events:** `SlipEventArgs.Dock` is null and `SlipEventArgs.LandArea` is set. The default tooltip's subtitle reads "On land · {land area name}".
- **Moving a boat** between the water and the land is a batch of two updates:

  ```csharp
  marina.BatchUpdate(new[] { SlipUpdate.Free("A-L03"), SlipUpdate.Occupy("Y-01", boat) });
  ```

- Validation rejects a land slip whose land area doesn't exist, or a slip with both `DockId` and `LandAreaId`.

## Changing the layout at runtime

| Docks | Dividers | Slips |
|---|---|---|
| `AddDock(dock)` | `AddDivider(divider)` / `AddDividers(...)` | `AddSlip(slip)` / `AddSlip(id, dockId, center, heading, length, width, label)` / `AddSlips(...)` |
| `UpdateDock(dock)` / `UpdateDock(DockUpdate)` | `UpdateDivider(divider)` | `UpdateSlip(slip)` / `UpdateSlip(SlipUpdate)` |
| `RemoveDock(id, removeSlips = true)` | `RemoveDivider(id)` | `RemoveSlip(id)` |
| `GetDock`, `GetDocks` | `GetDivider`, `GetDividers`, `GetDividersByDock` | `GetSlip`, `GetSlips`, `GetSlipsByDock`, `GetSlipsByLandArea`, `GetSlipsByStatus` |

Land areas are set with the layout (`InitializeLayout`) and read with `GetLandArea` and `GetLandAreas`.

- **Moving a dock doesn't move its slips.** Slips and dividers have absolute positions, so move them explicitly if needed.
- **`DockUpdate`** keeps the dock's center when only length or heading changes:

  ```csharp
  marina.UpdateDock(new DockUpdate("E") { Length = 80, HeadingDegrees = 10, Type = DockType.FloatingConcrete });
  marina.UpdateDock(new DockUpdate("E") { Center = new Vector2(160, 30) });
  ```

- **`SlipUpdate`** applies only the members you set:

  ```csharp
  marina.UpdateSlip(SlipUpdate.Geometry("E-01", center: new Vector2(143, 12), width: 6));
  marina.UpdateSlip(new SlipUpdate("E-01") { Label = "E-1", HasFingerPiers = false, MaxDraft = 2.8f });
  ```

- **`RemoveDock`** also removes the dock's dividers. With `removeSlips: false` it throws if slips remain.
- **`RemoveSlip`** drops the slip from the selection and shrinks or dissolves its multi-slip berth.

## Batching changes

- **`BatchUpdate(IEnumerable<SlipUpdate>)`** applies all updates with one scene rebuild and one `LayoutChanged` (`BatchUpdated`). Failures are returned instead of thrown:

  ```csharp
  BatchUpdateResult result = marina.BatchUpdate(updates);
  if (!result.Succeeded) foreach (var err in result.Errors) log($"{err.SlipId}: {err.Message}");
  ```

- **`BeginUpdate()`** coalesces `LayoutChanged` notifications and defers popup refreshes for any mix of calls:

  ```csharp
  using (marina.BeginUpdate())
  {
      marina.AddDock(dock);
      marina.AddSlips(SlipGenerator.AlongDock(dock, DockSide.Left, 10, 5, 12));
      marina.AddDividers(SlipGenerator.DividersAlongDock(dock, DockSide.Left, 10, 5, 12, DividerType.Boom));
  }   // one LayoutChanged(BatchUpdated) here
  ```

`SlipStatusChanged` is still raised once per affected slip.
