# Layout: piers, berths, dividers and land

A marina is a `MarinaLayout`:

| Property | Contents |
|---|---|
| `Name` | Marina name (`IMarinaVisualizer.MarinaName`) |
| `Piers` | `Pier` records |
| `Berths` | `Berth` records, each referencing a pier by `PierId`, or a land area by `LandAreaId` for [land berths](#land-berths) |
| `Dividers` | Finger piers, pile rows, booms and single piles between berths |
| `MultiBerths` | Boats spanning several berths (see [Multi-berths](05-multi-berths.md)) |
| `LandAreas` | Polygon quays, breakwaters and lawns (see [Land areas](#land-areas)) |

Load it with `InitializeLayout(layout)`. This replaces everything, clears the selection, rebuilds the camera presets and resets the camera. Save the current state with `GetLayout()`, which round-trips through `InitializeLayout`.

## Building a layout

### With `MarinaLayoutBuilder` (berths positioned along piers)

```csharp
var layout = new MarinaLayoutBuilder("VirtualMarina Harbor")
    .AddLandArea(new LandArea("quay", new[] { new Vector2(-130, -32), new Vector2(130, -32), new Vector2(130, -6), new Vector2(-130, -6) }, 1.0f))
    .AddLandArea(new LandArea("yard", yardOutline, 1.0f) { Name = "Boatyard" }, yard => yard
        .AddBerths("Y-", firstPosition: new Vector2(-120, -45), rowHeadingDegrees: 90, count: 8, berthWidth: 5.5f, berthLength: 12))
    .AddPier("A", "Pier A", start: new Vector2(80, -6), headingDegrees: 0, length: 72, pier => pier
        .AddBerths(PierSide.Left, count: 12, berthWidth: 5.5f, berthLength: 13,
                  customize: (i, berth) => berth with { Status = BerthStatus.Occupied, Boat = boats[i] })
        .AddBerths(PierSide.Right, count: 12, berthWidth: 5.5f, berthLength: 13, dividers: DividerType.Piles)
        .AddBerth("A-GUEST", PierSide.Right, offsetAlong: 68, berthWidth: 8, berthLength: 16),
        width: 3.5f, type: PierType.Concrete)
    .AddMultiBerth(new MultiBerth("BIG", new[] { "A-L10", "A-L11", "A-L12" }, superyacht))
    .Build();
```

**`PierBuilder.AddBerths(side, count, berthWidth, berthLength, customize, startOffset = 2, gap = 0, dividers = null)`**
- Berths are perpendicular to the pier with their bows toward it.
- Ids are `{PierId}-L01…` or `{PierId}-R01…`, and numbering continues across calls on the same side.
- `startOffset` is the distance from the pier's start to the first berth (it only applies to the first call on a side).
- `dividers`: when set, a divider of that type is generated at every berth boundary and the berths' automatic finger piers are turned off.

**`PierBuilder.AddBerth(id, side, offsetAlong, berthWidth, berthLength, customize)`** adds one berth at an explicit distance from the pier's start.

**`PierBuilder.AddBerth(Berth)`** and **`PierBuilder.AddDivider(Divider)`** add absolutely positioned elements (the `PierId` is set for you).

**`LandAreaBuilder.AddBerth(id, position, headingDegrees, length, width, customize)`** and **`LandAreaBuilder.AddBerths(idPrefix, firstPosition, rowHeadingDegrees, count, berthWidth, berthLength, customize, gap = 0.5, boatHeadingDegrees = row − 90°)`** add [land berths](#land-berths) to the land area passed to `MarinaLayoutBuilder.AddLandArea(landArea, configure)`. Row ids are `{idPrefix}01…` and continue across calls with the same prefix.

**`MarinaLayoutBuilder.AddBerth`, `AddDivider` and `AddMultiBerth`** add elements that aren't tied to the pier callback.

### With explicit positions

Every element can be placed by position, size and orientation:

```csharp
var pier  = Pier.FromCenter("E", "Pier E", center: new Vector2(150, 30), length: 60, width: 3, headingDegrees: 0, PierType.FloatingWooden);
var berth  = new Berth("E-01", "E", center: new Vector2(142.5f, 10), headingDegrees: 90, length: 12, width: 5);   // left of E, bow toward the pier
var piles = new Divider("E-D1", start: new Vector2(148.5f, 7.5f), headingDegrees: -90, length: 12, DividerType.Piles) { PierId = "E" };

var layout = new MarinaLayout { Name = "Custom", Piers = new[] { pier }, Berths = new[] { berth }, Dividers = new[] { piles } };
```

### With `BerthGenerator` (compute geometry, add yourself)

| Method | Returns |
|---|---|
| `BerthGenerator.AlongPier(pier, side, count, berthWidth, berthLength, startOffset, gap, firstNumber)` | A row of berths |
| `BerthGenerator.AtPier(pier, id, side, offsetAlong, berthWidth, berthLength)` | One berth |
| `BerthGenerator.DividersAlongPier(pier, side, count, berthWidth, berthLength, type, startOffset, gap)` | Dividers at the boundaries of such a row |

Useful when your ERP stores only berth numbers and pier dimensions.

## Piers

| Member | Meaning |
|---|---|
| `new Pier(id, name, start, headingDegrees, length, width = 2.5, type = FloatingWooden)` | Created from the shore-end point |
| `Pier.FromCenter(id, name, center, length, width, headingDegrees, type)` | Created from the center point |
| `Start`, `End`, `Center`, `Direction`, `Right`, `Bounds` | Geometry (plan coordinates) |
| `Type` | `PierType`: controls the look and the default deck height |
| `DeckHeight` | Deck height above water; defaults from `Type` unless set |
| `PilingSpacing` | Column spacing (`Concrete`) or cleat spacing (`FloatingConcrete`); default 6 m |
| `BerthingSides` | `PierSides.Both` (default), `Left` or `Right`: see [Single-sided piers](#single-sided-piers) |
| `Services` | `PierServices.None` (default), `Power`, `Water` or `PowerAndWater`: see [Power and water pedestals](#power-and-water-pedestals) |
| `HasBerthsOn(side)` | True when boats can berth on that side |
| `WithCenter(center)` | Copy moved to a new center |

| `PierType` | Rendering | Default deck height |
|---|---|---|
| `FloatingWooden` (default) | Plank deck with seams, walers, dark pontoon floats | 0.5 m |
| `FloatingConcrete` | Monolithic pontoon, rubber fenders, section joints, cleats | 0.55 m |
| `Concrete` | Fixed slab on square columns, curbs, bollards | 1.1 m |

Automatic finger piers of berths and finger-pier dividers take the pier's material: wood for floating wooden piers, concrete otherwise.

### Single-sided piers

A pier that runs along the edge of a land area (a quay wall, a pontoon moored against a mole) only takes boats on its water side. Set `BerthingSides` to that side:

```csharp
// Heading 0° runs along +Z; looking that way, the right-hand side is −X (the water), the land is on the left (+X).
var pontoon = new Pier("E", "Pier E (along the mole)", new Vector2(110.75f, -6), 0, 50, 2.5f, PierType.FloatingConcrete)
{
    BerthingSides = PierSides.Right,
};
builder.AddPier(pontoon, pier => pier.AddBerths(PierSide.Right, 9, 5, 10));
```

- Mooring points are drawn on the open side only: bollards (`Concrete`), cleats and rubber fenders (`FloatingConcrete`).
- `PierBuilder.AddBerths`, `BerthGenerator.AtPier`, `AlongPier` and `DividersAlongPier` throw `InvalidOperationException` for the closed side. Berths placed by absolute position aren't checked, so guest berths off the pier's end still work.
- `PierUpdate.BerthingSides` changes it at runtime; existing berths are not moved or removed.

### Power and water pedestals

`Pier.Services` draws supply pedestals along the pier. They appear on the berthing sides only, and only where berths exist: one for
every two berths, standing between them, so each berth has exactly one within reach. A berth left on its own at the end of a row gets
one halfway along it, and a stretch of pier without berths stays empty.

```csharp
marina.UpdatePier(new PierUpdate("A") { Services = PierServices.PowerAndWater });
```

| `PierServices` | Pedestal |
|---|---|
| `None` (default) | None |
| `Power` | Yellow top |
| `Water` | Blue top |
| `PowerAndWater` | Yellow top with a blue band |

Their colors come from `Style.Piers` (`PedestalColor`, `PowerColor`, `WaterColor`). The designer switches them on for you with
`MarinaDesigner.BerthServices`.

## Berths

| Member | Meaning |
|---|---|
| `new Berth(id, pierId, center, headingDegrees, length, width)` | A Free berth along a pier |
| `Berth.OnLand(id, landAreaId, position, headingDegrees = 0, length = 12, width = 5)` | A Free [land berth](#land-berths) |
| `PierId` / `LandAreaId` | Exactly one is set; `IsOnLand` is true for land berths |
| `Label` / `DisplayName` | Display text (`DisplayName` falls back to `Id`) |
| `Center`, `HeadingDegrees`, `Length`, `Width`, `Forward`, `Right`, `Bounds` | Geometry of the water area |
| `MaxDraft` | Optional; shown in the default tooltip |
| `Status`, `Boat` | See [Berth status, boats and flags](04-status-and-flags.md) |
| `HasFingerPiers` | Draw simple finger piers on both long sides (default true) |
| `IsVisible`, `IsDisabled`, `IsReadOnly` | Interaction flags |
| `MultiBerthId` | Set by the visualizer for members of a multi-berth |
| `Metadata` | Read-only string attributes you supply |
| `ExternalData` | Mutable host data bag (see [Selection, tooltips and actions](06-selection-tooltips-actions.md#external-data-on-berths)) |

Each berth's water area gets a translucent status pad, a status buoy at the seaward end and, when it has a boat, the boat model.

## Dividers

| Member | Meaning |
|---|---|
| `new Divider(id, start, headingDegrees, length, type = FingerPier)` / `Divider.FromCenter(...)` | Created from the start point or the center |
| `PierId` | Optional owning pier: sets deck height and material, and the divider is removed with the pier |
| `Width` | Finger width, boom float size or pile diameter (default 0.8 m) |
| `Spacing` | Distance between piles or boom floats (default 4 m) |

| `DividerType` | Rendering |
|---|---|
| `FingerPier` | Narrow walkable pier with a pile at its end |
| `Piles` | Row of mooring piles (wood, or steel next to concrete piers) |
| `Boom` | Dark line with orange floats (yellow at the ends) bobbing on the water |
| `SinglePile` | One mooring pile at the outer end of the boundary, nothing in between (Mediterranean mooring) |

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
| `Id` | Unique (case-insensitive); land berths reference it |
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

Each land area gets its own world-space mesh (`MeshIds.ForLand(slot)`, built by `LandMeshFactory`), built when the layout is loaded or the land area is added or updated. Land areas themselves aren't clickable; read them with `GetLandArea(id)` and `GetLandAreas()`, and change them with `AddLandArea`, `UpdateLandArea` and `RemoveLandArea`.

`PolygonMath` has the helpers used for outlines: `SignedArea`, `Contains`, `DistanceToBoundary`, `IsSimple` and `Triangulate`.

## Land berths

A land berth is a spot on a land area where a boat is stored or maintained ashore (boatyard, hard standing, maintenance area). It is an ordinary `Berth` with `LandAreaId` set instead of `PierId`, so status, boats, flags, selection, tooltips, actions, labels, focus, filters and events all work the same, with the same status colors.

```csharp
var spot = Berth.OnLand("Y-01", "yard", position: new Vector2(-120, -45), headingDegrees: 180, length: 12, width: 5.5f)
    with { Status = BerthStatus.Occupied, Boat = boat };
marina.AddBerth(spot);

IReadOnlyList<Berth> stored = marina.GetBerthsByLandArea("yard");
```

- **Drawn on the land:** a status pad on the surface, a status post with a colored ball at the rear (instead of a buoy), and the name on the ground when labels are on. No finger piers.
- **Boats ashore** are centered on the spot, don't bob, and rest on keel blocks and cradle stands with the keel `BerthPlacement.CradleHeight` (0.7 m) above the land. Reserved and temporarily free spots show the usual ghost boat.
- **Events:** `BerthEventArgs.Pier` is null and `BerthEventArgs.LandArea` is set. The default tooltip's subtitle reads "On land · {land area name}".
- **Moving a boat** between the water and the land is a batch of two updates:

  ```csharp
  marina.BatchUpdate(new[] { BerthUpdate.Free("A-L03"), BerthUpdate.Occupy("Y-01", boat) });
  ```

- Validation rejects a land berth whose land area doesn't exist, or a berth with both `PierId` and `LandAreaId`.

## Changing the layout at runtime

| Piers | Dividers | Berths |
|---|---|---|
| `AddPier(pier)` | `AddDivider(divider)` / `AddDividers(...)` | `AddBerth(berth)` / `AddBerth(id, pierId, center, heading, length, width, label)` / `AddBerths(...)` |
| `UpdatePier(pier)` / `UpdatePier(PierUpdate)` | `UpdateDivider(divider)` | `UpdateBerth(berth)` / `UpdateBerth(BerthUpdate)` |
| `RemovePier(id, removeBerths = true)` | `RemoveDivider(id)` | `RemoveBerth(id)` / `RenameBerth(id, newId)` |
| `GetPier`, `GetPiers` | `GetDivider`, `GetDividers`, `GetDividersByPier` | `GetBerth`, `GetBerths`, `GetBerthsByPier`, `GetBerthsByLandArea`, `GetBerthsByStatus` |

| Land areas | |
|---|---|
| `AddLandArea(land)`, `UpdateLandArea(land)`, `RemoveLandArea(id, removeBerths = true)` | `GetLandArea`, `GetLandAreas`, `GetBerthsByLandArea` |

`ExportObjects()` returns everything as one array of records (land areas, piers, dividers, berths and multi-berths); `MarinaLayout.FromObjects` turns such an array back into a layout. To draw layouts interactively, see [Designer](12-designer.md).

- **Moving a pier doesn't move its berths.** Berths and dividers have absolute positions, so move them explicitly if needed.
- **`PierUpdate`** keeps the pier's center when only length or heading changes:

  ```csharp
  marina.UpdatePier(new PierUpdate("E") { Length = 80, HeadingDegrees = 10, Type = PierType.FloatingConcrete });
  marina.UpdatePier(new PierUpdate("E") { Center = new Vector2(160, 30) });
  ```

- **`BerthUpdate`** applies only the members you set:

  ```csharp
  marina.UpdateBerth(BerthUpdate.Geometry("E-01", center: new Vector2(143, 12), width: 6));
  marina.UpdateBerth(new BerthUpdate("E-01") { Label = "E-1", HasFingerPiers = false, MaxDraft = 2.8f });
  ```

- **`RemovePier`** also removes the pier's dividers. With `removeBerths: false` it throws if berths remain.
- **`RemoveBerth`** drops the berth from the selection and shrinks or dissolves its multi-berth.
- **`RenameBerth`** gives a berth another id, keeping its place, boat, status, `ExternalData`, place in the selection and multi-berth, and raises `BerthRenamed`. It throws when the new id is taken. The id is what an ERP stores against a contract, so `Berth.Label` — a display name that leaves the id alone — is often the better answer; the [designer](12-designer.md#renaming-one-element) renames berths this way with an undo step.

## Batching changes

- **`BatchUpdate(IEnumerable<BerthUpdate>)`** applies all updates with one scene rebuild and one `LayoutChanged` (`BatchUpdated`). Failures are returned instead of thrown:

  ```csharp
  BatchUpdateResult result = marina.BatchUpdate(updates);
  if (!result.Succeeded) foreach (var err in result.Errors) log($"{err.BerthId}: {err.Message}");
  ```

- **`BeginUpdate()`** coalesces `LayoutChanged` notifications and defers popup refreshes for any mix of calls:

  ```csharp
  using (marina.BeginUpdate())
  {
      marina.AddPier(pier);
      marina.AddBerths(BerthGenerator.AlongPier(pier, PierSide.Left, 10, 5, 12));
      marina.AddDividers(BerthGenerator.DividersAlongPier(pier, PierSide.Left, 10, 5, 12, DividerType.Boom));
  }   // one LayoutChanged(BatchUpdated) here
  ```

`BerthStatusChanged` is still raised once per affected berth.
