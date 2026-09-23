# Designer: drawing a marina

`MarinaVisualizer.Designer` (a `MarinaDesigner`) lets users build a layout directly in the 3D view. They draw land areas as polygons, draw piers, add berths along piers, put boats ashore on land berths, plant trees and erase elements, and step back and forth through it all with undo and redo. A top-down aerial image of the real marina can be shown underneath and scaled to real size, so the drawing matches reality.

Everything the designer creates goes through the normal API (`AddLandArea`, `AddPier`, `AddBerths`, `AddDividers`, `RemoveBerth`, ...), so `LayoutChanged` is raised as usual, on top of the designer's own events.

## Ready-made panels

Both hosts include a tool panel. It has the design mode switch, a button for every tool (Navigate, Select, Coast, land, pier, berths, land berths, trees, Pedestals, Rename, erase, and the reference image's move and scale-line tools), Undo and Redo buttons, land / pier / berth settings (including the tree coverage slider, berth spacing, separators and pedestals) and the reference image controls. Undo there works like Ctrl+Z in the view (see [Undo and redo](#undo-and-redo)).

**WinForms**

```csharp
var panel = new MarinaDesignerPanel { Dock = DockStyle.Right, Width = 330, View = marinaView };
Controls.Add(panel);
```

`View` can be set in the Windows Forms designer as well as in code, and the panel follows it when the view is given another marina; setting `Marina` instead connects the panel to a marina without a view. The panel is laid out for 96 DPI and scales with the monitor it is on.

"Load image…" decodes PNG, JPEG, BMP, GIF or TIFF with GDI+ (`ReferenceImageLoader`), recognising the kind of image from its bytes rather than the file extension. A photo with an EXIF orientation is turned the right way up, and one wider or taller than `ReferenceImageLoader.MaxDimension` (8192 px) is scaled down to stay within common GPU texture limits; either way the design stores the corrected copy, not the original file. `MarinaViewControl.LoadReferenceImage(path)` does the same from code, and a picture that arrives undecoded from a marina file is decoded by the view automatically.

**Blazor WebAssembly**

```razor
<MarinaView @ref="_view" Marina="_marina" />
<MarinaDesignerPanel Marina="_marina" View="_view" />
```

The image file (PNG, JPEG or WebP, at most `MaxImageFileSize`, 25 MB by default) comes from an `<InputFile>` and the browser decodes it. `MarinaView.LoadReferenceImageAsync(bytes, contentType)` does the same from code.

The panel's look is in `MarinaDesignerPanel.razor.css`, a scoped stylesheet that reaches the page through the application's CSS bundle, so the host page must link it:

```html
<link rel="stylesheet" href="MyApp.styles.css" />   <!-- {AssemblyName}.styles.css -->
```

Every rule there is a single class of weight and the classes start with `vm-designer`, so any rule of the host's own that names one of those classes wins. `CssClass` adds classes to the outer element.

Both test hosts have a **Designer** tab with the panel, "New empty marina", "Load sample marina" and "Export objects".

## Design mode and tools

```csharp
var designer = marina.Designer;
designer.IsActive = true;               // closes the popup, clears the selection and hover
designer.Tool = DesignTool.DrawLandArea;
```

While `IsActive` is true, clicks go to the designer instead of selecting berths. Left-drag pans, right-drag orbits, the wheel zooms and the keyboard moves the camera, the same as outside design mode. `ToolHint` holds a one-line instruction for the current state, for a status bar, in the current UI language ([localization](15-localization.md)).

| `DesignTool` | Mouse | Keys |
|---|---|---|
| `Navigate` | Nothing (camera only) | |
| `DrawLandArea` | Click to add corners. Finish with a double-click, a right-click or a click on the first corner | Enter finishes, Backspace removes the last corner, Esc cancels |
| `DrawPier` | Click the shore end, then the far end. The direction squares up with the quay and the other piers; Alt draws it free, Shift snaps to 15° steps | Esc cancels |
| `AddBerths` | Click beside a pier where the row starts, then where it ends. Clicking the same spot twice adds one berth | Esc cancels |
| `AddLandBerths` | Click a land area where the boat should stand, then click where its bow should point (Shift snaps to 15°; the same spot twice uses `LandBerthHeading`) | Esc cancels |
| `PlantTrees` | Click a lawn to scatter trees on it, replacing the ones it has. Ctrl+click or right-click removes them | |
| `Erase` | Click a berth, pier or land area to remove it (piers and land areas take their berths with them, berths take their own dividers). Alt+click on a berth clears every berth off its pier, leaving the pier | Delete removes the element under the pointer |
| `Rename` | Click a berth or a pier to give it another name; the designer asks the host for it through `ElementRenaming` | |
| `EditServices` | Click a berth to give it the pedestals in `BerthServices`; Alt or Ctrl changes every berth down that side of the pier. The berths about to change are highlighted | |
| `SelectArea` | Drag a box over the water to select the berths inside it; Shift or Ctrl adds to the selection. The box follows the camera, so it selects what it looked like it covered | Delete removes the selected berths (`EraseSelectedBerths`) |
| `DrawShoreline` | Click along the coast of the mainland (two points make a straight one), then click the side that is land | Enter settles the line, Backspace takes it back to the points, Esc cancels |
| `MoveReferenceImage` | Drag the image with the left button | |
| `MeasureScale` | Click both ends of the image's scale bar | |

Ctrl+Z undoes the last change and Ctrl+Shift+Z or Ctrl+Y makes it again, with any tool (see [Undo and redo](#undo-and-redo)); while a drawing is in progress, Ctrl+Z takes back its last point instead. When there's nothing to cancel, Esc puts the tool down and switches back to `Navigate` — unless `EscapeReturnsToNavigate` is false, in which case that Esc is left to the host (to close a panel or leave design mode on the first press); Esc still abandons a drag or a drawing first either way.

**Snapping.** Land corners and pier ends snap, within `SnapDistancePixels` (default 12) on screen, to existing land corners, pier ends, points of the coast, berth corners and the drawing's own points; failing those, to the nearest point of a land edge or the coast. Each candidate is judged where it is actually drawn — a quay's corner at the quay's height — not where it would be on the water. Holding Alt turns snapping off. The point under the pointer (`PointerPosition`) is picked on the plane the drawing is shown on — the new land's height for an outline, the deck for a pier, the land for a berth ashore — so a drawing seen at an angle stays under the pointer rather than drifting to where the ray meets the water.

**Keys and the host.** The view gives the designer only unmodified keys (Esc, Enter, Backspace, Delete) plus Ctrl+Z and Ctrl+Shift+Z/Ctrl+Y, and only claims one when it would do something right now; everything else, and any accelerator the host has for the same keys, goes to the host first ([hosting](10-hosting-and-custom-views.md)). A host with its own Undo and Redo commands therefore calls `TryUndo()` and `TryRedo()` itself: they behave exactly like the keys in the view (taking back the last point while drawing, and reporting a refused step through `ActionFailed` rather than throwing).

**Modifiers show at once.** What Alt and Shift are about to do appears in the preview the moment the key goes down
— the eraser sweeping a whole row, a point that has stopped snapping — and goes back when it is released. Modifiers
otherwise arrive only with a pointer event, so a host that can see keys go down and up calls
`marina.Input.ModifiersChanged(modifiers)`; `MarinaViewControl` does this already. The Blazor `<MarinaView>` does not,
so there the preview catches up with the next pointer move.

**Piers square up.** A pier being drawn takes a direction at right angles to what is already there: the piers in the marina, and the land edges within 40 m of its shore end — the quay it springs from. A direction within 6° of square is corrected; anything further is left as drawn, so a deliberately angled pier still works. The preview marks a squared-up direction with a short line back along the pier. Alt draws exactly what the pointer says, and Shift asks for 15° steps instead.

While drawing, the view shows a preview on top of everything: outline and rubber band, the ghost pier with its length, the berths a click would add, and the element the eraser would remove. Invalid drawings (a crossing outline, a closed pier side) are shown in red. The measurements written beside the pointer ("12.5 M", "4 BERTHS", "LAND THIS SIDE") come from the core resources like every other text, so they are translated with it; the overlay's stroke font has capitals only.

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
| `Scenery` (a `HinterlandScenery`) | `DrawShoreline` | `Countryside` |
| `FogFactor` (fog multiplier while designing, 0–1; 1 keeps the normal fog) | rendering | 0.15 |

Numeric settings are checked against the ranges in `DesignerDefaults` (for example `PierWidth` 0.5–30 m, `BerthWidth` 1–50 m, `BerthLength` 1–150 m, `SnapDistancePixels` 0–100); a value outside its range throws `ArgumentOutOfRangeException`. `DesignerSettings.FromDesigner(designer)` and `settings.ApplyTo(designer)` copy the whole set, which is how a marina file keeps them.

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

`Rename` asks the host for a new name through `ElementRenaming`, so the application decides how to ask. A berth's
name is also its id, so each one has to be free; `IsBerthNameAvailable` and `IsPierIdAvailable` say whether one is,
and `ChangePierId` throws on a clash.

There are three things a click can rename, and `ElementRenaming` tells them apart through `Scope`:

| Click | `Scope` | What changes |
|---|---|---|
| A berth | `Element` | That berth's name |
| A pier | `Element` | The pier's name and id, and every berth on it |
| A berth with **Alt** held | `BerthsOfPier` | Every berth on that berth's pier, by a naming pattern |

**One berth.** Its `Label` — what is written on the water — follows the name when it merely repeated the old id, so
the water shows the new name; a label the host wrote is theirs and stays. That lives in `RenameBerth` on the
visualizer, so it holds however the rename was asked for, and it is a single `BerthRenamed` notification.

**A whole pier.** Giving a pier another id takes its berths with it, and their names are built again from the naming
scheme rather than having the old prefix swapped out. That matters on a pier that berths to one side: the scheme
leaves the side letter out, so `K-R07` becomes `T-07`, not `T-R07`. The running number each berth has is kept. The
pier's own name follows when it is still the generated one — `Pier K` becomes `Pier T` — while a name someone chose
is left alone. A berth named by hand keeps its name, as does one whose new name is already taken.

Every id that moved is reported through `LayoutChanged`, so a host tracking berths by id can follow them, and one
Ctrl+Z puts the whole move back.

#### Renaming berths by a pattern

Holding **Alt** over a berth renames the whole pier by a naming pattern, leaving the pier's own name and id where
they are — the same modifier that makes the eraser sweep a whole row.

The pattern offered comes from the **kind of pier**: `{pier}-{side}{number}`, or `{pier}-{number}` on a pier that
takes boats on one side only, where there is no other side to tell a berth apart from. That is
`DefaultBerthPattern(pierId)`. The user can put anything around the tokens and try as many patterns as they like
before settling on one.

This throws the old names away. Every berth on the pier is renamed and renumbered along it from
`BerthNaming.StartNumber`, whatever it was called before — including berths named entirely by hand, with no number
and no prefix to read a pattern out of. Berths are counted down one side and then the other when the pattern tells
the sides apart, and straight through when it does not, so `{pier}-{number}` on a pier that berths both sides gives
one run of numbers rather than two sets of the same ones.

Work it out first, then apply it:

```csharp
var plan = designer.PlanBerthNames("A", "{pier}.{side}{number}");
if (plan.IsClear) designer.ApplyBerthNames(plan);
else Warn(string.Join(", ", plan.Clashes));
```

`PlanBerthNames` changes nothing. `Renames` lists every berth and the name it would get, and `Clashes` names what
stops it: names two berths on the pier would share, and names a berth elsewhere in the marina already has. The
designer application shows those names and asks for another pattern rather than renaming part of the pier;
`ApplyBerthNames` refuses a plan that still clashes.

Applying one renames the whole pier in a single step for Ctrl+Z. The berths go to temporary names first and then to
the ones asked for, so a pattern that shuffles names around the pier — every berth moving up one — does not collide
with itself half way through.

`RenumberBerths` is the narrower operation, and **keeps** the number each berth already has:

```csharp
designer.RenumberBerths("E");                      // rebuild the names from BerthNaming
designer.RenumberBerths("E", "{pier}.{number}");   // E-R01 becomes E.01
```

It is the repair for berths whose names no longer match their pier — one that used to take boats on both sides and
now takes them on one, or berths still carrying a prefix from an id the pier had long ago — and it leaves a berth
named by hand alone. Renaming a pier's id runs it.

The side token is read from `BerthNaming`, since nothing in a name says which letter stood for the side.

The API behind all of it:

| Member | What it does |
|---|---|
| `RenameBerth(berthId, newBerthId)` | Gives a berth another name, keeping its place, boat, status, external data, selection and multi-berth |
| `RenamePier(pierId, name)` | Gives a pier another display name; its id, and the berth names built from it, stay |
| `ChangePierId(pierId, newPierId)` | Moves a pier's id, taking its berths with it |
| `DefaultBerthPattern(pierId)` | The pattern a pier's berths are named by when nothing else is asked for |
| `PlanBerthNames(pierId, pattern)` / `ApplyBerthNames(plan)` | Name a whole pier from scratch, checking for clashes first |
| `RenumberBerths(pierId)` / `RenumberBerths(pierId, pattern)` | Rebuild the names, keeping the number each berth has |

All of them are undoable. `DesignTool.Rename` raises `ElementRenaming` for the new name, so the application decides
how to ask:

```csharp
designer.ElementRenaming += (s, e) =>
{
    if (e.Scope == DesignRenameScope.BerthsOfPier)
    {
        e.NewBerthPattern = Prompt($"Name the berths on pier {e.Pier!.Id}", e.BerthPattern);
        return;
    }

    var name = Prompt(e.Berth is not null ? "Rename berth" : "Name the pier", e.CurrentName);
    if (name is null) e.Cancel = true;
    else e.NewName = name;
};

designer.Tool = DesignTool.Rename;
```

Without a handler the tool does nothing, and a name already taken is refused, so the host can offer another on the
next click. `IMarinaVisualizer.RenameBerth` is the same rename without the undo step, for a host renaming from its
own UI; it raises `LayoutChangeKind.BerthRenamed`. A berth's id is what an ERP stores against a contract, so
renaming one already in use means updating that reference too — `Berth.Label` is the alternative, a display name
that leaves the id alone.

The names of elements the designer is about to add can also be replaced in `ElementCreating` (see
[Events](#events)).

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

`designer.Scenery` picks what covers the land — `Countryside`, `Fields`, `Town` or `None` — scattered in a band along the coast
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

## Undo and redo

Every change the designer makes is recorded, and `Undo()` reverts the last one; `Redo()` makes an undone change again, exactly as it was made. Both throw when a step can no longer be done (`MarinaLayoutException`) or an action from `BeginAction` is still open (`InvalidOperationException`). `TryUndo()` and `TryRedo()` are what the keys, the panels' **Undo** and **Redo** buttons and the Designer apps' commands use: while a drawing is in progress `TryUndo()` takes back its last point instead of undoing, and a step that cannot be done raises `ActionFailed` and returns false instead of throwing. In the view Ctrl+Z undoes, and Ctrl+Shift+Z or Ctrl+Y (Cmd+Shift+Z on a Mac browser) redoes.

```csharp
if (designer.CanUndo) Console.WriteLine(designer.UndoDescription);   // "Add 4 berths"
designer.Undo();
if (designer.CanRedo) designer.Redo();
```

| Member | Meaning |
|---|---|
| `Undo()` | Reverts the last change; false when there is nothing to undo |
| `Redo()` | Makes the last undone change again; false when there is nothing to redo. Any new change empties the redo list |
| `TryUndo()`, `TryRedo()` | What Ctrl+Z and Ctrl+Y do in the view, for a host's own Undo and Redo commands: `TryUndo()` takes back the last point while drawing, and either one reports a step that cannot be done through `ActionFailed` instead of throwing |
| `CanUndo`, `UndoCount`, `UndoDescription` | State for an Undo button or menu item |
| `CanRedo`, `RedoCount`, `RedoDescription` | The same for Redo |
| `BeginAction(description)` | Groups several changes into one step (see below) |
| `ClearHistory()` | Forgets everything recorded, undone steps included |
| `MaxUndoSteps` | How many steps are kept (50; older ones are dropped) |
| `ActionUndone`, `ActionRedone` | Raised after an undo or a redo, with the `Description` and `RemainingSteps` |
| `ActionFailed` | Something asked for in the view (a click, Enter, Ctrl+Z) could not be done; nothing was changed |

A drawn land area, pier, berth row or land berth is removed again; erased elements come back with their dividers; planted or removed trees, names and switched-on pedestals are put back. Only what the designer changed is put back — the trees it planted, not the rest of the lawn — so anything the host changed through the normal API in between stays as it is. Loading or clearing a layout empties the history. A berth restored by undo is no longer part of a multi-berth.

**When the host got there first.** A change the host has since built on or changed again itself — a berth added to the pier the undo would take away, trees replaced since — is not overwritten. The undo (or redo) is refused as a whole and the step stays where it was. Called from code, `Undo()` and `Redo()` throw `MarinaLayoutException` for this; from the keyboard in the view there is nobody to throw to, so the designer raises `ActionFailed` instead, with a localized message and the exception:

```csharp
designer.ActionFailed += (s, e) => statusBar.Text = e.Exception.Message;   // also: e.Description
```

`ActionFailed` is raised for every refusal that starts in the view: a name typed for a berth that is already taken, an outline that is not valid, an undo blocked by a later change.

**One step from several changes.** `BeginAction` opens a scope that everything the designer changes goes into, until it is disposed. Call `Complete()` when the whole change is made; disposing the scope without that takes every change made inside it back, so an exception half way leaves nothing behind:

```csharp
using (var action = designer.BeginAction("Rebuild pier A"))
{
    designer.EraseBerthsOfPier("A");
    designer.CreateBerths("A", PierSide.Left, 0f, 60f);
    action.Complete();
}   // one Undo takes both back
```

Scopes nest — an inner one adds its changes to the outer one, and rolling back the inner one only takes back its own — and must be closed in the reverse order they were opened, as `using` does. `StateChanged` is raised once, when the outermost scope closes. `Undo()` and `Redo()` throw `InvalidOperationException` while a scope is open.

## Reference image

Load a top-down picture of the marina, such as a Google Maps satellite screenshot that includes the scale bar. **North must be at the top.** The image lies flat with north toward −Z. `ViewTopDown()` looks straight down with north up, and `FocusReferenceImage()` frames the whole image that way.

```csharp
designer.SetReferenceImage(new ReferenceImage(width, height, rgbaBytes));   // decoded pixels, top row first
designer.SetReferenceImage(ReferenceImage.FromEncoded(pngBytes, width, height, "image/png")); // file bytes, decoded by the view
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

An image made with `FromEncoded` carries only the file: the browser decodes it for WebGL, and `MarinaViewControl` decodes it
with `ReferenceImageLoader` before handing it to OpenGL, so an image loaded from a marina file shows on either. An image
with `EncodedData` (`CanBeSaved`) is written into the marina file when the design is saved; one made from raw pixels alone
is not.

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
| `ElementCreating` | A drawing is complete and about to be added | `Tool`, `LandArea`, `Pier`, `Berths`, `Dividers`, `Shoreline` (all settable), `Cancel` |
| `ElementCreated` | The element was added | `Tool`, `LandArea`, `Pier`, `Berths`, `Dividers`, `Shoreline` |
| `ElementErased` | Something was removed with the eraser (or `Erase`, `EraseBerthsOfPier`, `EraseSelectedBerths`); once per berth for a selection | `Element`, `RemovedBerths`, `RemovedDividers` |
| `ElementRenaming` | A berth or pier was clicked with `DesignTool.Rename` | `Scope`, `Berth`, `Pier`, `CurrentName`, `BerthPattern`, settable `NewName`, `NewPierId`, `NewBerthPattern`, `Cancel` |
| `TreesPlanted` | Trees were scattered or removed | `LandArea`, `PreviousCount` |
| `ActionUndone` | `Undo()` reverted a change | `Description`, `RemainingSteps` |
| `ActionRedone` | `Redo()` made an undone change again | `Description`, `RemainingSteps` (steps left to redo) |
| `ActionFailed` | Something asked for in the view could not be done; nothing changed | `Description`, `Exception` |
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
designer.SetBerthServices(berthId, wholeSide: true);   // pedestals, as the EditServices tool does
designer.EraseBerthsOfPier(pier.Id);                   // the berths, not the pier
designer.EraseSelectedBerths();                        // whatever marina.SelectedBerths holds
designer.Erase(pier);
designer.Undo();
designer.Redo();
```

`CompleteDraft()`, `CancelDraft()` and `RemoveLastPoint()` act on the drawing in progress. The creating methods throw
where the tools in the view would raise `ActionFailed` — `MarinaLayoutException` for an outline whose edges cross,
`KeyNotFoundException` for an unknown pier or land area, and so on, as documented on each one — and return null when an
`ElementCreating` handler cancels.

## Exporting the marina

The whole marina — layout, look, motion, labels and camera — goes into one JSON file with
[`MarinaDocument`](13-marina-file-format.md), which is what the [designer application](14-designer-app.md) saves and the host
application loads:

```csharp
MarinaDocument.FromVisualizer(marina, generator: "My Designer 1.0").Save(path);
MarinaDocument.Load(path).ApplyTo(marina);
```

`ExportObjects()` is the alternative for an ERP that keeps its own tables: the whole marina as a flat array of its immutable records, in dependency order: the shoreline (when there is one), the passing-traffic settings (`MarineTraffic`, even when switched off), land areas, piers, dividers, berths, then multi-berths. Everything in the layout but its name is there; the style, camera views and reference image are not (a [marina file](13-marina-file-format.md) holds those).

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

## How the designer is put together

For contributors: `MarinaDesigner` is one public class split across partial files in `src/VirtualMarina.Core/Design`,
each holding one concern, with the work itself in internal helpers beside them.

| File | What it holds |
|---|---|
| `MarinaDesigner.cs` | Events, `IsActive`, `Tool`, the drawing in progress, and the tool handlers table |
| `MarinaDesigner.Settings.cs` | The settings the tools draw with; ranges and defaults from `DesignerDefaults` |
| `MarinaDesigner.Elements.cs` | Creating, changing and erasing elements, from the tools or from code |
| `MarinaDesigner.Naming.cs` | Renaming berths and piers, naming patterns (with `DesignNaming`, `BerthNamingScheme`) |
| `MarinaDesigner.History.cs` | Undo, redo and `BeginAction` (with `DesignHistory`, `DesignActionScope` and the `IDesignCommand`s in `DesignCommands.cs`) |
| `MarinaDesigner.Input.cs` | Pointer and key input forwarded by `MarinaInputController`, snapping, area selection |
| `MarinaDesigner.Image.cs` | The reference image (state in `ReferenceImageController`) and the top-down views |
| `MarinaDesigner.Rendering.cs` | What the designer adds to the scene: the image and the previews (`DesignOverlay`) |

Each `DesignTool` has a handler in `Design/Tools` — `OutlineTools.cs` (land, pier, coast, scale line),
`BerthTools.cs` (berths and land berths) and `PickTools.cs` (navigate, erase, rename, pedestals, trees, area selection,
moving the image) — derived from `DesignToolHandler`. A handler keeps everything its tool needs to remember, says which
plane the pointer is picked on (`PointerPlane`), whether it snaps and which keys it wants, and draws its own preview;
the designer hands every input to the handler of the tool in hand and forgets its state when the tool is put down.
`DesignPicker` finds what is under the pointer and does the snapping, and `BerthPlanner` works out where a row of
berths and its separators go. A new tool is a new `DesignTool` value, a handler, and its entry in the handlers table.
Every change a tool makes is recorded as an `IDesignCommand` that can undo and redo itself, which is what keeps redo
exact.
