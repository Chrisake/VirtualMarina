# Designer: drawing a marina

`MarinaVisualizer.Designer` (a `MarinaDesigner`) lets users build a layout directly in the 3D view. They draw land areas as polygons, draw piers, add berths along piers, put boats ashore on land berths, plant trees and erase elements, and step back through it all with undo. A top-down aerial image of the real marina can be shown underneath and scaled to real size, so the drawing matches reality.

Everything the designer creates goes through the normal API (`AddLandArea`, `AddPier`, `AddBerths`, `AddDividers`, `RemoveBerth`, ...), so `LayoutChanged` is raised as usual, on top of the designer's own events.

## Ready-made panels

Both hosts include a tool panel. It has the design mode switch, tool buttons, the undo button, land / pier / berth settings (including the tree coverage slider, berth spacing, separators and pedestals) and the reference image controls.

**WinForms**

```csharp
var panel = new MarinaDesignerPanel { Dock = DockStyle.Right, Width = 330, Marina = marinaView.Marina };
Controls.Add(panel);
```

"Load image…" decodes PNG, JPEG, BMP, GIF or TIFF with GDI+ (`ReferenceImageLoader`). `MarinaViewControl.LoadReferenceImage(path)` does the same from code.

**Blazor WebAssembly**

```razor
<MarinaView @ref="_view" Marina="_marina" />
<MarinaDesignerPanel Marina="_marina" View="_view" />
```

The image file comes from an `<InputFile>` and the browser decodes it. `MarinaView.LoadReferenceImageAsync(bytes, contentType)` does the same from code. The panel's classes start with `vm-designer` so you can restyle them.

Both test hosts have a **Designer** tab with the panel, "New empty marina", "Load sample marina" and "Export objects".

## Design mode and tools

```csharp
var designer = marina.Designer;
designer.IsActive = true;               // closes the popup, clears the selection and hover
designer.Tool = DesignTool.DrawLandArea;
```

While `IsActive` is true, clicks go to the designer instead of selecting berths. Left-drag pans, right-drag orbits and the wheel zooms, the same as outside design mode. `ToolHint` holds a one-line instruction for the current state, for a status bar.

| `DesignTool` | Mouse | Keys |
|---|---|---|
| `Navigate` | Nothing (camera only) | |
| `DrawLandArea` | Click to add corners. Finish with a double-click, a right-click or a click on the first corner | Enter finishes, Backspace removes the last corner, Esc cancels |
| `DrawPier` | Click the shore end, then the far end. The direction squares up with the quay and the other piers; Alt draws it free, Shift snaps to 15° steps | Esc cancels |
| `AddBerths` | Click beside a pier where the row starts, then where it ends. Clicking the same spot twice adds one berth | Esc cancels |
| `AddLandBerths` | Click a land area where the boat should stand, then click where its bow should point (Shift snaps to 15°; the same spot twice uses `LandBerthHeading`) | Esc cancels |
| `PlantTrees` | Click a lawn to scatter trees on it, replacing the ones it has. Ctrl+click or right-click removes them | |
| `Erase` | Click a berth, pier or land area to remove it (piers and land areas take their berths with them, berths take their own dividers) | Delete removes the element under the pointer |
| `Rename` | Click a berth or a pier to give it another name; the designer asks the host for it through `ElementRenaming` | |
| `SelectArea` | Drag a box over the water to select the berths inside it; Shift or Ctrl adds to the selection. The box follows the camera, so it selects what it looked like it covered | |
| `DrawShoreline` | Click along the coast of the mainland (two points make a straight one), then click the side that is land | Enter settles the line, Backspace takes it back to the points, Esc cancels |
| `MoveReferenceImage` | Drag the image with the left button | |
| `MeasureScale` | Click both ends of the image's scale bar | |

Ctrl+Z undoes the last change, with any tool (see [Undo](#undo)). When there's nothing to cancel, Esc switches back to `Navigate`. Corners and pier ends snap to existing land corners, pier ends and land edges within `SnapDistancePixels` (default 12). Holding Alt turns snapping off.

**Modifiers show at once.** What Alt and Shift are about to do shows in the preview the moment the key goes down —
the eraser sweeping a whole row rather than one berth, a point that has stopped snapping — and goes back the moment
it is released. Modifiers otherwise only arrive with a pointer event, so a host that can see keys go down and up
calls `marina.Input.ModifiersChanged(modifiers)` to keep the preview honest; `MarinaViewControl` already does.

**Piers square up.** A pier being drawn takes a direction at right angles to what is already there: the piers in the marina, and the land edges within 40 m of its shore end — the quay it springs from. A direction within 6° of square is corrected; anything further is left as drawn, so a deliberately angled pier still works. The preview marks a squared-up direction with a short line back along the pier. Alt draws exactly what the pointer says, and Shift asks for 15° steps instead.

While drawing, the view shows a preview on top of everything: outline and rubber band, the ghost pier with its length, the berths a click would add, and the element the eraser would remove. Invalid drawings (a crossing outline, a closed pier side) are shown in red.

### Settings

| Property | Used by | Default |
|---|---|---|
| `LandKind` (`Quay`, `Grass`, `Breakwater`) | `DrawLandArea` | `Quay` |
| `LandHeight` | `DrawLandArea` | 1 m |
| `TreeDensity` (trees per 1000 m², 0–100) | `PlantTrees`, new lawns | 8 |
| `PierType` | `DrawPier` | `FloatingWooden` |
| `PierWidth` | `DrawPier` | 2.5 m |
| `PierBerthingSides` (`Both`, `Left`, `Right`) | `DrawPier` | `Both` |
| `BerthWidth`, `BerthLength` | `AddBerths`, `AddLandBerths` | 5 m, 12 m |
| `BerthDepth` (stored as `Berth.MaxDraft`) | `AddBerths` | 3 m |
| `BerthSeparators` (a `BerthSeparator`) | `AddBerths` | `FingerPiers` |
| `BerthGap` (space between neighbouring berths, 0–20 m) | `AddBerths` | 0 m |
| `AlignBerthsToExisting` | `AddBerths` | true |
| `BerthServices` (`PierServices`: pedestals switched on for the pier) | `AddBerths`, `DrawPier` | `None` |
| `LandBerthHeading` (bow direction in degrees) | `AddLandBerths` | 0° |
| `BerthNaming` (a `BerthNamingScheme`) | `AddBerths`, `AddLandBerths` | `A-L01`, `A-R01`, ... |
| `PierNamePattern` | `DrawPier` | `Pier {pier}` |
| `SnapDistancePixels` | drawing tools | 12 |
| `FogFactor` (fog multiplier while designing) | rendering | 0.15 |

### How berths are placed

Berths are perpendicular to the pier, bows toward it, on the side you click. A row covers the stretch between the two clicks, one berth every `BerthWidth` + `BerthGap` meters. Places that are already taken are skipped, and berths can't be added on the closed side of a single-sided pier.

- **Where the row starts.** With `AlignBerthsToExisting` (the default) the row lines up with the nearest existing berth edge on that side, or with the pier's start, so a second row continues the first. Turn it off and the first berth starts exactly where you click — at any offset from the pier's start, not a multiple of the berth width.
- **What separates them.** `BerthSeparators` picks between the berths' own finger piers, nothing at all, or generated `Divider` elements shared by neighbours:

| `BerthSeparator` | Between two berths |
|---|---|
| `FingerPiers` (default) | Each berth's own finger piers (`Berth.HasFingerPiers`); no `Divider` elements |
| `None` | Nothing: only the gap, at least `MarinaDesigner.MinimumSeparatorGap` (0.3 m) wide |
| `FingerPier` | One walkable finger pier, 75% of the berth length, between every two berths |
| `PairedFingerPiers` | A pier at every other boundary: each boat has a pier on one side and its neighbour on the other, and the boats at the ends of the row get one on their outer side |
| `Piles` | A row of mooring piles |
| `Boom` | A floating boom |
| `SinglePile` | One pile at the outer end of the boundary (Mediterranean mooring) |

- **Power and water.** `BerthServices` switches the pier's `Pier.Services` on when berths are added to it. Pedestals are drawn on the berthing sides only and only where berths exist — one for every two berths, standing between them, so each berth has exactly one within reach (a berth left on its own at the end of a row gets one halfway along it).

### Land berths

`AddLandBerths` puts a boat ashore (`Berth.OnLand`): the first click sets the spot on a land area, the second aims the bow (Shift snaps to 15°). Clicking the same spot twice uses `LandBerthHeading`, so a whole boatyard can be laid out in one direction with single double-clicks. The preview shows the footprint and an arrow along the bow; it turns red when the spot falls outside the land area. Land berths are `BerthWidth` × `BerthLength` and take part in selection, status and tooltips like any other berth.

### Trees

`PlantTrees` works on lawns (`LandKind.Grass`) only; clicking a quay or breakwater does nothing. Every click **replaces** the lawn's trees with a new random scattering at `TreeDensity` (the "Tree coverage" slider in the panels), kept clear of the land berths on it. Ctrl+click or a right-click removes them. From code: `PlantTrees(landAreaId, density)` and `RemoveTrees(landAreaId)` (which works on any land area). A new lawn is planted at `TreeDensity` as it is drawn. Positions are stored in `LandArea.Trees`, so trees never move between sessions.

### Renaming

`Rename` asks the host for the new name through `ElementRenaming`, so the application decides how to ask. A berth's
name is also its id, so each one has to be free.

Giving a **pier** another id takes its berths with it, and the names are built again from the naming scheme rather
than having the old prefix swapped out. That matters on a pier that berths to one side: the scheme leaves the side
letter out, so `K-R07` becomes `T-07`, not `T-R07`. The running number each berth already has is kept.

The pier's own name follows too, when it is still the generated one — `Pier K` becomes `Pier T` — while a name
someone chose is left as it is. `IsPierIdAvailable` and `IsBerthNameAvailable` say whether a name is free; the
designer application asks again rather than letting a clash through, and `ChangePierId` throws on one.

A berth someone named by hand keeps that name, as does one whose new name is already taken. Every id that moved is
reported through `LayoutChanged`, so a host tracking berths by id can follow them, and one Ctrl+Z puts the whole move
back.

`RenumberBerths(pierId)` does the same on its own, without changing the id. It is the repair for berths whose names
no longer match their pier — one that used to take boats on both sides and now takes them on one, or berths still
carrying a prefix from an id the pier had long ago. Renaming a pier in the designer runs it too.

#### Renaming a whole pier to a pattern

Because renaming a pier renames every berth on it, `ElementRenaming` also offers the **pattern** those berths are
named by. `BerthPattern` is read back out of the names they actually have — `{pier}-{side}{number}` for berths called
`A-L01` — rather than being whatever `BerthNaming` happens to be set to, so it describes the pier in front of the
user. It is null for a berth, and for a pier whose berths were all named by hand.

Setting `NewBerthPattern` renames every numbered berth on the pier to match, keeping the number and the padding each
one already has, so putting the same pattern back changes nothing. `RenumberBerths(pierId, pattern)` does it directly:

```csharp
designer.RenumberBerths("E", "{pier}.{number}");   // E-R01 becomes E.01
```

A berth whose new name is already taken is left alone rather than overwritten, so a pattern that would give two
berths the same name — dropping `{side}` from a pier that berths on both — renames neither of them. The whole pier is
one step for Ctrl+Z.

The side token is still read from `BerthNaming`, since nothing in a name says which letter stood for the side, and it
is only looked for right next to the number. A berth named under a scheme whose side letters differed comes back with
that letter as plain text, which still reproduces the name it has.

### The mainland

`DrawShoreline` draws the coast behind the marina, so it stops looking like an island in an empty sea. Click along the
coast — two points are enough for a straight one — and press Enter. That **settles** the line rather than finishing the
drawing: the next click says which side of it is land, and the mainland appears on that side.

The first and last stretches of the line run on without end, so the land never runs out however far the camera pulls
back. The one rule is that those two stretches must not cross each other, since then neither side of the line is "the
land"; the preview turns red and Enter refuses. Backspace while the line is settled puts it back on the drawing board.

The mainland is drawn **beneath** the land areas placed by hand, so a quay traced along the shore sits on top of it and
the two read as one piece of ground. There is only ever one: drawing another replaces it, and `DeleteShoreline()` (the
"Remove the mainland" button) takes it away. Ctrl+Z undoes any of that.

`Scenery` picks what covers the land — `Countryside`, `Fields`, `Town` or `None` — scattered in a band along the coast
and thinning inland. It is generated from a seed rather than stored, so it costs nothing in the file and stays put
between sessions. From code: `CreateShoreline(line, landOnLeft)`, or `marina.SetShoreline(...)` for full control of
its height, surface and scenery.

### Names

| Element | Name | Set by |
|---|---|---|
| Land area | `quay-1`, `lawn-1`, `breakwater-1`, ... ("Quay 1", ...) | |
| Pier | Id: the first free letter `A`–`Z`, then `D27`, ... Name: `PierNamePattern` | `PierNamePattern`, default `Pier {pier}` |
| Berth | `A-L01`, `A-R01`, ... | `BerthNaming` |
| Land berth | `yard-01`, ... | `BerthNaming.LandPattern` |

A berth's name is also its id, so it is unique; a pier's name is a title beside its id and need not be.

#### The berth naming scheme

`BerthNaming` (a `BerthNamingScheme`) decides what the berths drawn from now on are called. The pattern is plain text with tokens in braces:

| Token | Becomes |
|---|---|
| `{pier}` | The pier's id, or the land area's id for a berth ashore |
| `{pierName}` | The pier's display name |
| `{side}` | `LeftSide` / `RightSide` (`L` and `R` by default); empty ashore |
| `{number}` | The running number, padded to `NumberDigits` digits |

```csharp
// A-L01, A-L02, ... (the default)
designer.BerthNaming = BerthNamingScheme.Default;

// 101, 103, 105 ... whichever pier they are drawn on, and YARD-01 ... ashore
designer.BerthNaming = new BerthNamingScheme
{
    Pattern = "{number}",
    LandPattern = "YARD-{number}",
    StartNumber = 101,
    Increment = 2,
    NumberDigits = 3,
};
```

Slots ashore have a numbering of their own: `LandStartNumber`, `LandIncrement` and `LandNumberDigits` each fall back to the berths’ setting when left null, so a yard can run `YARD-01, YARD-02` while the berths run `101, 103, 105`. Numbering starts at `StartNumber` and goes up by `Increment`. Names already taken are skipped, so a second row on the same pier carries on after the first instead of clashing with it. `Validate()` reports a scheme that could not name anything (an empty pattern, a zero increment); the setter throws on one. An unknown token is written out as it stands, so a stray brace never swallows part of a name.

`PierNamePattern` does the same, more simply, for a pier's display name: `{pier}` stands for its generated id, so `"Pontoon {pier}"` gives "Pontoon A". Both settings are part of `DesignerSettings`, so a marina file reopens with the naming it was saved with.

#### Renaming one element

| Member | What it does |
|---|---|
| `RenameBerth(berthId, newBerthId)` | Gives a berth another name, keeping its place, boat, status, external data, selection and multi-berth. Undoable |
| `RenamePier(pierId, name)` | Gives a pier another display name. Its id, and the berth names built from it, stay. Undoable |
| `DesignTool.Rename` | Click a berth or pier in the view; the designer raises `ElementRenaming` for the new name |

```csharp
designer.ElementRenaming += (s, e) =>
{
    var name = Prompt(e.Berth is not null ? "Rename berth" : "Name the pier", e.CurrentName);
    if (name is null) e.Cancel = true;
    else e.NewName = name;
};

designer.Tool = DesignTool.Rename;
```

Without a handler the tool does nothing, and a name already taken is refused (the host can offer another one on the next click). `IMarinaVisualizer.RenameBerth` is the same rename without the undo step, for a host that renames from its own UI; it raises `LayoutChangeKind.BerthRenamed`. A berth's id is what an ERP stores against a contract, so renaming one that is already in use means updating that reference too — `Berth.Label` is the alternative, a display name that leaves the id alone.

The names of the elements the designer is about to add can also be replaced in `ElementCreating` (see below).

## Undo

Every change the designer makes is recorded, and `Undo()` reverts the last one — the panels have an **Undo** button and Ctrl+Z works in the view.

```csharp
if (designer.CanUndo) Console.WriteLine(designer.UndoDescription);   // "Add 4 berths"
designer.Undo();
```

| Member | Meaning |
|---|---|
| `Undo()` | Reverts the last change; false when there is nothing to undo |
| `CanUndo`, `UndoCount`, `UndoDescription` | State for a toolbar button |
| `ClearHistory()` | Forgets everything recorded |
| `MaxUndoSteps` | How many steps are kept (50; older ones are dropped) |
| `ActionUndone` | Raised after an undo, with the `Description` and `RemainingSteps` |

A drawn land area, pier, berth row or land berth is removed again; erased elements come back with their dividers; planted or removed trees and switched-on pedestals are restored. Only the designer's own changes are recorded — anything the host changed through the normal API in between stays as it is — and loading or clearing a layout empties the history. A berth restored by undo is no longer part of a multi-berth.

## Reference image

Load a top-down picture of the marina, such as a Google Maps satellite screenshot that includes the scale bar. **North must be at the top.** The image lies flat with north toward −Z. `ViewTopDown()` looks straight down with north up, and `FocusReferenceImage()` frames the whole image that way.

```csharp
designer.SetReferenceImage(new ReferenceImage(width, height, rgbaBytes));   // decoded pixels, top row first
designer.SetReferenceImage(ReferenceImage.FromEncoded(pngBytes, width, height, "image/png")); // browser decodes (WebGL only)
designer.FocusReferenceImage();
```

Without a known scale, the image is first sized to about 300 m across (or to the current layout). To give it its real size:

1. Pick `DesignTool.MeasureScale` (the panels switch to it after loading an image).
2. Click both ends of the map's scale bar. `ScaleLineDrawn` reports the line's current length in meters.
3. Enter the length the bar represents and call `CalibrateReferenceImage(meters)` ("Apply scale" in the panels).

The image is scaled about the line's first end, so the bar ends up exactly that long. A handler can also calibrate right away:

```csharp
designer.ScaleLineDrawn += (s, e) => e.KnownLengthMeters = PromptForLength(e.MeasuredLength);
```

If the pixel size is already known, set `ReferenceImageMetersPerPixel` directly or pass `metersPerPixel` to `SetReferenceImage`. Move the image with `MoveReferenceImage` or `ReferenceImageCenter`.

| Property | Meaning |
|---|---|
| `ReferenceImageOpacity` | 0–1, default 0.6 |
| `ReferenceImageVisible` | Show or hide the image (it is shown whether or not the designer is active) |
| `ReferenceImageAboveScene` | True (default): drawn over land, piers and boats. False: lies on the water, hidden by structures |
| `ReferenceImageSize`, `ReferenceImageBounds` | Ground size and corners in plan coordinates |
| `ScaleLine` | The last scale line (updated by calibration) |

The camera limits and the water surface grow to cover the image.

## Events

| Event | Raised when | Data |
|---|---|---|
| `ActiveChanged` | `IsActive` changed | |
| `ToolChanged` | `Tool` changed | `Previous`, `Current` |
| `DraftChanged` | A point was placed or removed, or a drawing was finished or abandoned | `Tool`, `Change` (`PointAdded`, `PointRemoved`, `Completed`, `Canceled`), `Points` |
| `ElementCreating` | A drawing is complete and about to be added | `LandArea`, `Pier`, `Berths`, `Dividers` (all settable), `Cancel` |
| `ElementCreated` | The element was added | `LandArea`, `Pier`, `Berths`, `Dividers` |
| `ElementErased` | Something was removed with the eraser (or `Erase(element)`) | `Element`, `RemovedBerths`, `RemovedDividers` |
| `ElementRenaming` | A berth or pier was clicked with `DesignTool.Rename` | `Berth`, `Pier`, `CurrentName`, `BerthPattern`, settable `NewName`, `NewPierId`, `NewBerthPattern`, `Cancel` |
| `TreesPlanted` | Trees were scattered or removed | `LandArea`, `PreviousCount` |
| `ActionUndone` | `Undo()` reverted a change | `Description`, `RemainingSteps` |
| `ScaleLineDrawn` | A scale line was drawn | `Start`, `End`, `MeasuredLength`, settable `KnownLengthMeters` |
| `ReferenceImageChanged` | The image was set, cleared, moved, scaled or restyled | `Change`, `Image`, `Center`, `MetersPerPixel` |
| `StateChanged` | Any of the above, or a setting changed (for refreshing a UI) | |

```csharp
designer.ElementCreating += (s, e) =>
{
    if (e.Pier is { } pier) e.Pier = pier with { Id = erp.NextPierCode(), Name = "Pier " + erp.NextPierCode() };
    if (e.Berths.Count > 50) e.Cancel = true;
};
designer.ElementCreated += (s, e) => erp.SaveLayout(marina.ExportObjects());
```

`LayoutChanged` is raised as well, with the kinds `LandAreaAdded`, `LandAreaUpdated` and `LandAreaRemoved` (and `LayoutChangedEventArgs.LandAreaId`) for land.

## Drawing from code

The tools use public methods that raise the same events:

```csharp
var lawn = designer.CreateLandArea(new[] { new Vector2(-50, -40), new Vector2(50, -40), new Vector2(50, -20), new Vector2(-50, -20) });
var pier = designer.CreatePier(start: new Vector2(0, -20), end: new Vector2(0, 40));
designer.CreateBerths(pier!.Id, PierSide.Right, fromAlong: 2, toAlong: 50);
designer.CreateLandBerth(lawn!.Id, new Vector2(-20, -30), headingDegrees: 90);
designer.PlantTrees(lawn.Id, treesPer1000SquareMeters: 12);
designer.RemoveTrees(lawn.Id);
designer.Erase(pier);
designer.Undo();
```

`CompleteDraft()`, `CancelDraft()` and `RemoveLastPoint()` act on the drawing in progress.

## Exporting the marina

The whole marina — layout, look, motion, labels and camera — goes into one JSON file with
[`MarinaDocument`](13-marina-file-format.md), which is what the [designer application](14-designer-app.md) saves and the host
application loads:

```csharp
MarinaDocument.FromVisualizer(marina, generator: "My Designer 1.0").Save(path);
MarinaDocument.Load(path).ApplyTo(marina);
```

`ExportObjects()` is the alternative for an ERP that keeps its own tables: the whole marina as a flat array of its immutable records, in dependency order: land areas, piers, dividers, berths, then multi-berths.

```csharp
foreach (var element in marina.ExportObjects())
{
    switch (element)
    {
        case LandArea land: db.SaveLand(land.Id, land.Kind, land.Height, land.Points); break;
        case Pier pier:     db.SavePier(pier.Id, pier.Type, pier.Start, pier.HeadingDegrees, pier.Length, pier.Width, pier.BerthingSides); break;
        case Divider div:   db.SaveDivider(div); break;
        case Berth berth:     db.SaveBerth(berth.Id, berth.PierId, berth.LandAreaId, berth.Center, berth.HeadingDegrees, berth.Width, berth.Length, berth.MaxDraft); break;
        case MultiBerth berth: db.SaveBerth(berth); break;
    }
}

marina.InitializeLayout(MarinaLayout.FromObjects(db.LoadAll(), "Harbor"));   // and back
```

`GetLayout()` returns the same content as a `MarinaLayout`.

## Land areas at runtime

The designer uses these, and they work without it too:

| Method | Effect |
|---|---|
| `AddLandArea(land)` | Adds it and builds its mesh (`LandAreaAdded`) |
| `UpdateLandArea(land)` | Replaces it by id and rebuilds only its mesh (`LandAreaUpdated`) |
| `RemoveLandArea(id, removeBerths = true)` | Removes it and its land berths (`LandAreaRemoved`) |
| `UpdateLandArea(land with { Trees = ... })` | Replaces its trees (`LandArea.GenerateTrees` scatters them) |
