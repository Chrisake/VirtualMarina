# The designer application

`apps/VirtualMarina.Designer` is a stand-alone Windows application for drawing a marina to scale and saving it as a
[`.marina.json` design](13-marina-file-format.md). It is meant for the people who set a marina up rather than for
developers, so it hides everything the test hosts show for debugging.

```
dotnet run --project apps/VirtualMarina.Designer
```

It is built on the same pieces as any other host: a `MarinaViewControl` and `marina.Designer`. The
[designer guide](12-designer.md) documents the tools themselves; this page is about the application around them.

## The window

- **A fixed toolbar** across the top, grouped by what each tool is for: Navigate and Select; Coast, Land, Piers,
  Berths and Ashore; Trees and Pedestals; Rename and Erase; then Look, Cameras and the two view commands. The tool in
  hand is highlighted, and the toolbar never scrolls away. Undo is not on it — Ctrl+Z and Edit ▸ Undo are where
  people look for it.
- **The marina fills the window.** Left-drag pans, right-drag orbits, the wheel zooms, exactly as in the viewer.
- **One panel beside it** showing the name of the current tool, a line saying what to do with it, and only that
  tool's settings. Picking *Berths* shows berth sizes, separators, the gap, the pedestals and how berths are named;
  picking *Land* shows the surface and height. Nothing else is on screen to scroll past.
- **A status bar** with the same instruction, the pointer's position in meters, and the camera's height and tilt, so
  you can see where you are while tracing a map.
- **The activity log is hidden** (View ▸ Show activity log), being useful only when something looks wrong.

## Drawing to scale

1. **File ▸ Load reference image…** puts a top-down photo or map (north at the top) under the marina.
2. The application switches to **Draw scale line**: drag over something whose length you know — the map's scale bar,
   a quay, a known pier.
3. Type that length into **Real length (m)** and press **Apply**. The image is rescaled about the start of the line,
   and everything drawn from then on is in real meters.
4. Trace the land, then the piers, then fill them with berths. Sizes can also be typed exactly in the panel, and
   corners snap to the corners, pier ends and edges already drawn — hold Alt to ignore the snapping.

## Naming piers and berths

New piers are called "Pier A", "Pier B" and so on. The **Name** box on the Pier panel changes that, where `{pier}`
stands for the letter: `Terminal {pier}` gives "Terminal A".

The Berths panel has a **Names** section deciding what the berths drawn next are called:

| Field | What it does |
|---|---|
| **Pattern** | The name itself. `{pier}` is the pier, `{pierName}` its name, `{side}` the side (L or R), `{number}` the running number |
| **Start at** | The number the first new berth gets |
| **Count by** | The step from one berth to the next: 1 gives 1, 2, 3; 2 gives 1, 3, 5 |
| **Digits** | Leading zeros: 2 writes 1 as `01` |

A line under the fields shows the next few names as you type, so the pattern is never a guess. Names already taken
are skipped, so filling a second pier carries on where the first left off.

The **Ashore** panel has the same four fields for the spots on land, counted separately from the berths on the
water: the berths can run 101, 103, 105 while the yard runs YARD-01, YARD-02. Left alone, the spots ashore follow the
berths' numbering.

### Renaming

The **Rename** tool (or Edit ▸ Rename, F2) does three different things depending on what you click:

| Click | What it asks for |
|---|---|
| A berth | Its new name |
| A pier | The pier's name, its id, and the pattern its berths are named by |
| A berth with **Alt** held | A naming pattern for that berth's whole pier |

**One berth.** A berth's name is also its id, so a name already in use is refused and the log says so. The name
written on the water follows unless you had given the berth a label of your own.

**A pier.** Renaming a pier renames every berth on it, so the dialog asks for the berth pattern too, filled in with
the one those berths follow now — leaving it alone changes nothing. A pier's name is only a title and need not be
unique; its id must be.

**A whole pier of berths.** Holding Alt asks for a pattern alone and leaves the pier's own name and id where they
are. The pattern comes filled in from the kind of pier — `{pier}-{side}{number}`, or `{pier}-{number}` where boats
berth on one side only — and you can put anything around the tokens. Every berth on the pier is renamed and
renumbered along it, whatever it was called before, including berths named entirely by hand. If the pattern would
give two berths the same name, or a name something else already has, the dialog says which names and asks again
rather than renaming half the pier.

Ctrl+Z puts any of it back in one step.

## Look and Cameras

**Look** and **Cameras** open beneath the current tool's settings rather than replacing them, so switching to them
never means losing sight of what the tool in hand is doing. The two are one scrolling column with one scrollbar: the
tool's settings lead, and scrolling down carries them off the top and leaves the whole height to what follows.

**Look** shows the appearance settings while the marina stays visible, so the effect of each one can be seen as it is
changed: water and waves, light and air, shadows, berth colours, the land and its trees, berth labels. Each setting
has a small ↺ beside it that puts only that one back to its default, and a full reset sits at the bottom.
**Add preview boats** fills empty berths with a random mix so colours and water can be judged against a full marina;
those boats are not saved with the design.

The **labels** card sets the **Font** berth names are drawn in, listing every font installed on this machine.
Choosing one captures its letter shapes into the design, so the marina reads the same wherever it is opened — the
machine showing it to customers does not need the font, and neither does the web view. *Bold* takes the bold face.
*Built-in lettering* falls back to the plain strokes the library carries, which is what a design gets when no font
was chosen.

Two cards are about what surrounds the marina rather than the marina itself, both covered in full in
[The sea and the shore](17-sea-and-shore.md):

- **The mainland**, drawn with the **Coast** tool, takes its colours from the land card, including the walls and
  roofs of a town behind the shore.
- **Passing traffic** puts vessels out in the bay, on lanes that sweep in past the marina and back out to the edge
  of the map. Tick *Vessels out at sea*, then set *Keep clear by* (how near the marina the nearest lane passes),
  *Clear of land at the edge*, *Lanes* and *Space between lanes*; the card reports where the nearest lane ended up.
  *Speed* is a percentage — every kind of vessel keeps its own, so a fishing boat still plods and a jet ski still
  tears past — while *Vessels* caps how many are out at once and *Wait before the next one* sets how long after one
  leaves before another appears. *Show the lanes* draws them while you set it, tinted by which way each one runs.

**Cameras** lists the views. *Save this view* stores where the camera is now under a name you choose, so you and the
host application can come back to it. *Automatic views* — the whole marina, straight down, one from each compass
point, and one per pier — each have a tick: untick one to leave it out of the list the host application offers. That
choice is saved with the design. Saved views can be gone to or deleted.

## Saving, and what the host does with it

**File ▸ Save** writes a `.marina.json` file holding the layout, the appearance and motion settings, the berth
labels, the camera position and the designer's tool settings. Opening one puts all of it back on screen, panels
included, so the sliders, colours and ticks show what the design was saved with. The title bar shows the file name
and marks unsaved changes with `*`; closing with unsaved work asks first.

The host application loads that one file:

```csharp
MarinaDocument.Load(path).ApplyTo(marinaView.Marina);   // WinForms
MarinaDocument.Load(path).ApplyTo(_marina);             // Blazor
```

Because the appearance is saved with the layout, a host does not have to configure any of it. The WinForms test host
has **Open design…** and **Save design…** buttons on its Designer tab as a worked example.

## Menus

| Menu | What it holds |
|---|---|
| **File** | New marina, Open design, Save, Save as, Load reference image, Exit |
| **Edit** | Undo (Ctrl+Z), Cancel drawing, Rename (F2), Properties (the marina's name) |
| **View** | Top view north up, Fit the marina, Fit the image, Show activity log |
| **Marina** | Appearance (the Look panel), Cameras, Berth labels |
| **Help** | Shortcuts (F1), About |

## Keyboard and mouse

| Input | Effect |
|---|---|
| Left drag / right drag / wheel | Pan / orbit / zoom |
| Click | Use the current tool |
| Right click | Finish a land area, or clear the trees under the pointer |
| Esc | Cancel the shape being drawn, then go back to Navigate |
| Enter | Finish the land area being drawn |
| Backspace | Take back the last corner |
| Delete | Remove what the eraser points at |
| Ctrl+Z | Undo the last change |
| Shift while drawing | Snap a pier, or a bow direction, to 15° steps |
| Alt while drawing | Ignore the snapping to corners and edges |
| Alt while erasing | Take the whole row of berths, not just the one under the pointer |
| Alt while renaming | Rename the whole pier by a pattern, not just the berth under the pointer |
| Ctrl+N / O / S / Shift+S / I | New, open, save, save as, load reference image |
| Ctrl+T / Ctrl+F | Top view north up / fit the marina |
| F1 / F2 | Shortcuts / rename |
