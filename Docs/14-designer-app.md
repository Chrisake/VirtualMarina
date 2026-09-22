# The designer application

`apps/VirtualMarina.Designer` is a stand-alone Windows application for drawing a marina to scale and saving it as a [`.marina.json` design](13-marina-file-format.md). It is meant for the people who set a marina up — not for developers — so it hides everything the library's test host shows for debugging.

```
dotnet run --project apps/VirtualMarina.Designer
```

It is built on the same pieces as any other host: a `MarinaViewControl` and `marina.Designer`. The [designer guide](12-designer.md) documents the tools themselves; this page is about the application around them.

## The window

- **A fixed toolbar** across the top, grouped by what each tool is for: Navigate and Select; Coast, Land, Piers, Berths and Ashore; Trees and Pedestals; Rename and Erase; then Look, Cameras and the two view commands. The tool in hand is highlighted, and the toolbar never scrolls away. Undo is not on it — Ctrl+Z and Edit ▸ Undo are where people look for it.
- **The marina fills the window.** Left-drag pans, right-drag orbits, the wheel zooms, exactly as in the viewer.
- **One panel beside it** showing the name of the current tool, a line telling you what to do with it, and only that tool's settings. Picking "Berths" shows berth sizes, separators, the gap, the pedestals and how the berths are named; picking "Land" shows the surface and height. Nothing else is on screen to scroll past.
- **A status bar** with the same instruction, the position of the pointer in meters and the camera's height and tilt — so you can see where you are while tracing a map.
- **The activity log is hidden** (View ▸ Show activity log), because it is only useful when something looks wrong.

## The Look and Cameras tabs

**Look** and **Cameras** open beneath the current tool's settings rather than replacing them, so switching to them
never means losing sight of what the tool in hand is doing. The tool's own settings keep the top of the panel (and
scroll on their own if they are tall), with the rest underneath.

**Cameras** lists the views. *Save this view* stores where the camera is now under a name you choose, so you and the
host application can come back to it. *Automatic views* — the whole marina, straight down, one from each compass
point, and one per pier — each have a tick: untick one to leave it out of the list the host application offers. That
choice is saved with the design. Saved views can be gone to or deleted.

**Look** shows the appearance settings, so the marina stays visible while they are changed. Water
and waves, light and air, berth colours, the land and its trees, berth labels — each setting has a small ↺ beside it
that puts only that one back to its default, and a full reset sits at the bottom.

Two of its cards are about what surrounds the marina rather than the marina itself:

- **The mainland**, drawn with the **Coast** tool, takes its colours from the land card, including the walls and roofs
  of a town behind the shore.
- **Passing traffic** puts vessels out in the bay: tick *Vessels out at sea*, then set how busy it is, how far the
  lanes keep clear of the marina and the land, and how fast they go. The card says how many lanes found room out
  there, and tells you when the clearance leaves none.

**Add preview boats** fills empty berths with a random mix so the colours and the water can be judged against a full
marina. Those boats are not saved with the design.

## Drawing to scale

1. **File ▸ Load reference image…** puts a top-down photo or map (north at the top) under the marina.
2. The application switches to **Draw scale line**: drag over something whose length you know — the map's scale bar, a quay, a known pier.
3. Type that length into **Real length (m)** and press **Apply**. The image is rescaled about the start of the line, and from then on everything you draw is in real meters.
4. Trace the land, then the piers, then fill them with berths. Sizes can also be typed exactly in the panel, and corners snap to the corners, pier ends and edges already drawn (hold Alt to ignore the snapping).

## Menus

| Menu | What it holds |
|---|---|
| **File** | New marina, Open design, Save, Save as, Load reference image |
| **Edit** | Undo (Ctrl+Z), cancel the shape being drawn, rename a berth or pier (F2), rename the marina |
| **View** | Top view north up, fit the marina, fit the image, show the activity log |
| **Marina** | Water, light and motion…; berth labels |
| **Help** | Keyboard and mouse; about |

**Marina ▸ Water, light and motion…** is where the design's personalization lives: wave height, length and speed, how much the boats move with the water, reflections, ripples, sun glints, water colors, sun direction and height, haze, and the berth status colors. Changes are visible in the view while the dialog is open, Cancel puts them back, and everything is saved inside the design — so the host application does not have to configure any of it.

## Naming berths and piers

New piers are called "Pier A", "Pier B" and so on; the **Name** box on the Pier panel changes that, where `{pier}` stands for the letter — `Terminal {pier}` gives "Terminal A".

The Berths panel has a **Names** section that decides what the berths drawn next are called:

| Field | What it does |
|---|---|
| **Pattern** | The name itself. `{pier}` is the pier, `{pierName}` its name, `{side}` the side (L or R), `{number}` the running number |
| **Start at** | The number the first new berth gets |
| **Count by** | The step from one berth to the next: 1 gives 1, 2, 3; 2 gives 1, 3, 5 |
| **Digits** | Leading zeros: 2 writes 1 as `01` |

A line under the fields shows the next few names as you type, so the pattern is never a guess. Numbers already taken are skipped, so filling a second pier carries on where the first left off.

The **Ashore** panel has the same four fields for the slots on land, counted separately from the berths on the water: the berths can run 101, 103, 105 while the yard runs YARD-01, YARD-02. Left alone, the slots ashore follow the berths’ numbering.

The **Rename** tool (or Edit ▸ Rename, F2) gives one element another name: click a berth or a pier, type the name, press Enter. A berth's name is also its id, so a name already used is refused and the log says so; a pier's name is only a title, so any name will do. Ctrl+Z puts the old name back.

## What it saves

**File ▸ Save** writes a `.marina.json` file holding the layout, the appearance and motion settings, the berth labels, the camera position and the designer's tool settings. The title bar shows the file name and marks unsaved changes with `*`; closing with unsaved work asks first.

The host application loads that one file:

```csharp
MarinaDocument.Load(path).ApplyTo(marinaView.Marina);   // WinForms
MarinaDocument.Load(path).ApplyTo(_marina);             // Blazor
```

The WinForms test host has **Open design…** and **Save design…** buttons on its Designer tab that do exactly this, as a worked example.

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
| Ctrl+N / O / S / Shift+S / I | New, open, save, save as, load reference image |
| Ctrl+T / Ctrl+F | Top view north up / fit the marina |
