# The designer application

The Designer is a stand-alone application for drawing a marina to scale and saving it as a
[`.marina.json` design](13-marina-file-format.md). It is meant for the people who set a marina up rather than for
developers, so it hides everything the test hosts show for debugging. It comes in two editions that look and behave
the same and save the same files:

| Project | What it is | Runs on |
|---|---|---|
| `apps/VirtualMarina.Designer` | The native desktop application (WinForms, OpenGL) | Windows |
| `apps/VirtualMarina.Designer.Blazor` | The same tool as a Blazor WebAssembly app (WebGL) | Any browser with WebGL 2 |
| `apps/VirtualMarina.Designer.Desktop` | A launcher that opens the Blazor edition in its own window | Windows, macOS, Linux |
| `apps/VirtualMarina.Designer.Common` | What the two editions share: the file workflow, the command table, the text | (library) |

```
dotnet run --project apps/VirtualMarina.Designer            # Windows
dotnet run --project apps/VirtualMarina.Designer.Blazor     # the Blazor edition in an app window (through the launcher)
```

Both are built on the same pieces as any other host: a view (`MarinaViewControl` or `<MarinaView>`) and
`marina.Designer`. The [designer guide](12-designer.md) documents the tools themselves; this page is about the
application around them. Everything below applies to both editions unless it says otherwise; the ways the browser
edition differs are collected in [The browser edition](#the-browser-edition).

## The window

- **A fixed toolbar** across the top, grouped by what each tool is for: Navigate and Select; Coast, Land, Piers,
  Berths and Ashore; Trees and Pedestals; Rename and Erase; then Look, Cameras, the two view commands (Top View and
  Fit Marina), and Undo and Redo. The tool in hand is highlighted, each tool's tooltip names its key, and the toolbar
  never scrolls away. Undo and Redo are greyed out when there is nothing to take back or put back.
- **The marina fills the window.** Left-drag pans, right-drag orbits, the wheel zooms, exactly as in the viewer.
- **One panel beside it** showing the name of the current tool, a line saying what to do with it, and only that
  tool's settings. Picking *Berths* shows berth sizes, separators, the gap, the pedestals and how berths are named;
  picking *Land* shows the surface and height. Nothing else is on screen to scroll past.
- **A status bar** with the same instruction, the pointer's position in meters, and the camera's height and tilt, so
  you can see where you are while tracing a map. A change the designer refused (a name already taken, an undo blocked
  by a later change) shows there as a passing notice.
- **The activity log is hidden** (View ▸ Activity Log), being useful only when something looks wrong.

While a tool is in hand the scene's distance haze is damped (`MarinaDesigner.FogFactor`), so the shapes being traced
stay crisp from high above. The Look panel puts it back to full while it is open, since the haze is one of the things
it sets. Neither counts as a change to the design.

The desktop window is laid out for 96 DPI and scaled per monitor (`PerMonitorV2`), so it stays sharp when dragged
between screens of different scaling. The settings panel can be dragged wider or narrower, within limits that keep
its rows readable and leave the marina room. The mouse wheel scrolls the settings column rather than turning
whichever slider, number field or drop-down passes under the pointer on the way; click a control first to dial its
value in with the wheel.

## Drawing to scale

1. **File ▸ Reference Image…** puts a top-down photo or map (north at the top) under the marina.
2. The application switches to **Draw scale line**: click both ends of something whose length you know — the map's
   scale bar, a quay, a known pier.
3. Type that length into **Real length (m)** and press **Apply**. The image is rescaled about the start of the line,
   and everything drawn from then on is in real meters.
4. Trace the land, then the piers, then fill them with berths. Sizes can also be typed exactly in the panel, and
   corners snap to the corners, pier ends, berth corners, the coast and the edges already drawn — hold Alt to ignore
   the snapping.

The picture is saved with the design, so it can be traced further or checked against later.

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
are skipped, so filling a second pier carries on where the first left off. A combination that could name nothing — a
blank pattern, counting by zero — is not applied: the field is marked (on the desktop with an error icon whose tooltip
gives the reason) and the reason replaces the example line until it is put right. A blank pier name pattern is marked
the same way.

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

**One berth.** A berth's name is also its id, so a name already in use is refused and the dialog asks again. The name
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

Ctrl+Z puts any of it back in one step, and Redo makes it again.

## Look and Cameras

**Look** and **Cameras** open beneath the current tool's settings rather than replacing them, so switching to them
never means losing sight of what the tool in hand is doing. The two are one scrolling column with one scrollbar: the
tool's settings lead, and scrolling down carries them off the top and leaves the whole height to what follows.

**Look** shows the appearance settings while the marina stays visible, so the effect of each one can be seen as it is
changed: water and waves, light and air, berth colours, the land and its trees, berth labels. Each setting
has a small ↺ beside it that puts only that one back to its default, and a full reset sits at the bottom.
**Marina full** fills the marina with preview boats, so colours, light and water can be judged against a marina with
boats in it rather than an empty one. The slider says how full the marina ends up, not how many boats get added: the
boats already there are cleared first, so pressing *Add boats* again deals a fresh fleet to the same figure, and 100%
means every berth in the marina has a boat in it, the ones ashore included. Each boat is picked to suit the berth it
goes in — no jet ski rattling around in a twenty-metre yacht berth — and a boat too wide for one berth, a catamaran
above all, is moored across two berths side by side (`PreviewFleet` in the core library does the choosing, for both
editions). None of it is saved with the design; the host application decides who is really in the marina.

The **labels** card sets the **Font** berth names are drawn in. Choosing one captures its letter shapes into the
design, so the marina reads the same wherever it is opened — the machine showing it to customers does not need the
font, and neither does the web view. The desktop edition lists every font installed on the machine; a browser will
not list them, so the browser edition offers the common families it finds present. *Bold* takes the bold face.
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
labels, the camera position, the reference image and the designer's tool settings. Opening one puts all of it back on
screen, panels included, so the sliders, colours and ticks show what the design was saved with; a file that cannot be
read is reported and leaves the design on screen as it was, and one written by a newer version is opened with a note
in the log. The title shows the file name and marks unsaved changes with `*`; closing with unsaved work asks first.
**File ▸ New** starts an empty marina but keeps the tool settings, which are the user's rather than the design's.

On the desktop, Save replaces the file only once the new one is completely written, so a failed save never leaves a
half-written design behind. How the browser edition saves is described [below](#opening-and-saving-in-a-browser).

The host application loads that one file:

```csharp
MarinaDocument.Load(path).ApplyTo(marinaView.Marina);   // WinForms
MarinaDocument.Parse(json).ApplyTo(_marina);            // Blazor (the bytes come from an upload or an HTTP call)
```

Because the appearance is saved with the layout, a host does not have to configure any of it. The WinForms test host
has **Open design…** and **Save design…** buttons on its Designer tab as a worked example.

## Menus

| Menu | What it holds |
|---|---|
| **File** | New, Open…, Save, Save As…, Reference Image…, Exit (desktop only) |
| **Edit** | Undo, Redo, Cancel Drawing, Rename, Properties… (the marina's name) |
| **View** | Top View (north up), Fit Marina, Fit Image, Activity Log |
| **Marina** | Appearance (the Look panel), Cameras, Berth Labels |
| **Help** | Shortcuts… (F1), About |

Undo and Redo, on the toolbar and in the Edit menu, say in their tooltip what they would take back or put back
("Add 4 berths"). They do what Ctrl+Z and Ctrl+Y do in the view, whichever way they are asked for: while a shape is
being drawn Undo takes back its last point, and a step that can no longer be done is reported in the log instead of
stopping the app.

## Keyboard and mouse

Every command and its keys come from one table, `DesignerCommands`, shared by both editions: the menus show their
shortcuts from it, the browser edition hands it to its script, and Help ▸ Shortcuts is written from it
(`ShortcutHelp`), so the three cannot disagree. A field being typed in keeps Ctrl+Z, Ctrl+Y, F2, Esc and the tool
letters for itself, so Ctrl+Z there undoes the typing, not the last design step.

| Input | Effect |
|---|---|
| Left drag / right drag / wheel | Pan / orbit / zoom |
| Click | Use the current tool |
| Right click | Finish a land area, or clear the trees under the pointer |
| Arrows or W A S D / Shift+arrows | Pan / orbit (click the view first) |
| Page Up, Page Down / + − / Home | Tilt / zoom / frame the whole marina |
| Esc | Cancel the shape being drawn, then go back to Navigate |
| Enter | Finish the shape being drawn |
| Backspace | Take back the last corner |
| Delete | Remove what the eraser points at, or the berths selected with Select |
| Ctrl+Z | Undo the last change (held down, step after step); while drawing, take back the last point |
| Ctrl+Y, Ctrl+Shift+Z | Redo |
| Shift while drawing | Snap a pier, or a bow direction, to 15° steps |
| Alt while drawing | Ignore the snapping to corners and edges |
| Alt while erasing | Take the whole row of berths, not just the one under the pointer |
| Alt while renaming | Rename the whole pier by a pattern, not just the berth under the pointer |
| Ctrl+O / S / Shift+S / I | Open, save, save as, load reference image |
| Ctrl+N, Ctrl+T | New, top view north up (desktop) |
| Alt+N, Alt+T | New, top view north up (browser) |
| Ctrl+F | Fit the marina |
| F1 / F2 | Shortcuts / rename |

**Tool letters.** While the 3D view has the focus, a single letter picks a tool: N Navigate, X Select, C Coast,
L Land, P Piers, B Berths, Y Ashore, T Trees, U Pedestals, R Rename, E Erase. None of them clashes with the view's
own W A S D, and they never fire while the focus is anywhere else, so typing an "e" into a name field does not pick
the eraser. The toolbar buttons show each tool's letter in their tooltips.

## The browser edition

`apps/VirtualMarina.Designer.Blazor` is the same application running in the browser: every piece of designer logic is
the shared `MarinaDesigner` in `VirtualMarina.Core`, drawn by the reusable `<MarinaView>`; only the shell, the panels
and the dialogs are Razor components. It needs a browser with WebGL 2.

### Building, running and publishing

```
dotnet run --project apps/VirtualMarina.Designer.Blazor                 # an app window, through the launcher
dotnet run --project apps/VirtualMarina.Designer.Blazor --property:DesignerDevServer=true   # a tab, http://localhost:5290
dotnet publish apps/VirtualMarina.Designer.Blazor -c Release -o out/designer-web
```

Running the project starts the [desktop launcher](#the-desktop-launcher) rather than the WebAssembly dev server, so the
designer opens in an app window of its own: Chrome, Edge, Chromium or Brave in application mode, or the default
browser when none of them is installed. The switch is in the project's `Directory.Build.targets`. Setting
`DesignerDevServer` to true brings back the plain dev server, a tab at
http://localhost:5290 with WebAssembly debugging.

The published app is static files: serve `out/designer-web/wwwroot` from any web server or static host. It is
served from the root of its site (`<base href="/">` in `wwwroot/index.html`); to serve it from a sub-path, change
that `href` to match. It runs on the .NET WebAssembly interpreter rather than compiled ahead of time — the drawing is
WebGL's either way — so publishing needs no extra workload. To give it a window of its own on a desktop, use the
[launcher](#the-desktop-launcher).

### Opening and saving in a browser

A page cannot write to the disk on its own, so the browser edition uses what the browser offers:

- **Chromium browsers — Chrome, Edge and the like** (the File System Access API): Open and Save As show the system's file pickers, and **Save
  writes back to the same file**, as on the desktop. The browser may ask once for permission to write to it. The file
  handle lasts as long as the page, so after a reload the first Save asks where again.
- **Other browsers**: Open uses an ordinary file upload, and Save downloads the design as a file — every time, since
  there is no file to write back to. A download cannot tell whether it was kept, so it counts as saved.

Closing or reloading the tab with unsaved changes asks first, as the browser's own "Leave site?" question. The
reference image may be PNG, JPEG or WebP, decoded by the browser.

### Dialogs, focus and keys

Every question — save first?, a name, a message — is a native HTML `<dialog>` opened as a modal: the focus stays
inside it, the page behind is inert, Esc cancels, and the focus goes back where it was when it closes. The menu bar
is a real menu bar for assistive technology (`role="menubar"`, arrow keys, Home, End and Esc move through it), the
toolbar's tool buttons report which is pressed and their key (`aria-pressed`, `aria-keyshortcuts`), sliders read out
their value with its unit, each ↺ button says which setting it resets, and the status notice and the activity log
are live regions, so a screen reader announces a refusal or a new log line.

Two shortcuts differ from the desktop: **Alt+N** for New and **Alt+T** for Top View, because Chrome keeps Ctrl+N
(new window) and Ctrl+T (new tab) for itself and never hands them to a page. On a Mac, Cmd works wherever Ctrl is
written. There is no File ▸ Exit: close the tab or window.

## The desktop launcher

`apps/VirtualMarina.Designer.Desktop` runs the browser edition as a windowed app on Windows, macOS and Linux. It
serves the WebAssembly build on a free loopback port and opens it in a Chromium-family browser in application mode (a
window with no tabs or address bar). It is not a native wrapper: the page is the ordinary app in the ordinary browser
engine, so WebGL behaves exactly as in a tab.

**Publish it before handing it out.**

```
dotnet publish apps/VirtualMarina.Designer.Desktop -c Release -o out/designer
out/designer/VirtualMarina.Designer.Desktop        # VirtualMarina.Designer.Desktop.exe on Windows
```

A plain build serves the client's files through ASP.NET Core's *static web assets* manifest, which points at the
source and `obj` folders of the machine it was built on; it runs there (`dotnet run --project
apps/VirtualMarina.Designer.Desktop`) and nowhere else. `dotnet publish` copies everything into the output's
`wwwroot`, which the launcher serves from its own folder whatever the working directory.

**Which window opens.** Tried in this order, the first one found wins:

1. `VIRTUALMARINA_BROWSER`, if it names an executable.
2. Google Chrome, Microsoft Edge, Chromium, Brave — `Program Files` / `%LOCALAPPDATA%` on Windows, `/Applications`
   and `~/Applications` on macOS, the `PATH` and `/snap/bin` on Linux.
3. On Linux, the same browsers installed as a Flatpak.
4. The default browser, as an ordinary tab.

**No console window.** On Windows the launcher is a windowed program, so starting it — from Explorer, a shortcut or a
terminal — opens the app window and nothing else. Two switches bring a console back:

- `--console` (or `-c`) shows the launcher's messages: in the terminal it was started from, where Ctrl+C then stops it,
  or in a console window of its own when it was started from Explorer or a shortcut. Output sent to a file or a pipe
  stays there. Running `apps/VirtualMarina.Designer.Blazor` passes it, so `dotnet run` still shows them.
- `--no-browser` opens nothing and only prints the address, for opening it by hand; it implies `--console`, since the
  address is the only way in.

Should no browser open at all — not even the default one — and there is no console, a message box gives the address.
On macOS and Linux the launcher is an ordinary command-line program and both switches just work as described.

A browser the launcher can start as a process of its own gets a profile of its own, kept between launches under the
user's local application data (`%LOCALAPPDATA%\VirtualMarina\Designer\BrowserProfiles` on Windows,
`~/.local/share/VirtualMarina/Designer/BrowserProfiles` on Linux, `~/Library/Application Support/...` on macOS),
readable by that user alone. Snaps and Flatpaks cannot use it, so they open the window in the user's usual profile.

**When it stops.** The launcher stops when its window closes, however the window was opened. It opens the page with a
random token in its address (`?launcher=<token>`); a page opened that way probes `/launcher/heartbeat`, keeps a
Server-Sent Events stream open to `/launcher/events` for as long as it lives, and says goodbye on `/launcher/bye` when
it is closed. About eight seconds after the last page has gone — long enough for a reload — the launcher shuts down.
If no page connects within a minute of starting, it gives up and closes the window it opened. Where the launcher
started the window's own process, that process ending stops it too; a Flatpak or snap browser, a browser that was
already running and the default browser all put the window somewhere the launcher cannot wait on, which is why the
page itself reports in. Ctrl+C in the console stops the launcher and closes the window it opened.

The endpoints under `/launcher/` need the token; without it they answer 404, as any static host would, and the same
app served by any static host never asks for them.

## How the two editions share one design

For contributors: everything the two editions have in common lives in `apps/VirtualMarina.Designer.Common` (a plain
`net8.0` library with no UI), so each edition is only its shell, panels and dialogs.

| Type | What it does |
|---|---|
| `DesignerSession` | One window's worth of state that is not drawing: the file, unsaved changes and the title, the activity log, the side panel shown, and the whole New / Open / Save / rename workflow |
| `IDesignerDialogs` | Everything the session asks the user — save first?, confirm, a text prompt, a message, pick a file to open, save a file. `WinFormsDialogs` answers with message boxes and file dialogs, `BlazorDialogs` with the `<dialog>` modal and the browser's file pickers |
| `DesignerCommands` | The command table: each command's id, its menu text and its keys on the desktop and in the browser, which keys a text field keeps, which repeat when held, and the tool letters |
| `ShortcutHelp` | The Help ▸ Shortcuts text for a platform, written from the command table |
| `RenamePlanner` | The rename dialogs: builds the question for a berth, a pier or a whole row, reads and checks the answer (a name taken, a pattern that clashes) and applies it through the designer as one undo step. The session asks again for as long as the check fails |
| `LauncherLiveness` | Decides when the desktop launcher should stop, from the pages connected to it |
| `Resources/Strings.resx` | Every piece of text both editions display ([localization](15-localization.md)) |

The designer raises `ElementRenaming` in the middle of handling a click, which is no place for a dialog, so the
session lets the click finish and asks afterwards, applying the answer through the public API. Adding a command means
adding it to `DesignerCommands` and handling its id in both editions' command switch; its menu shortcut, browser key
and help line follow from the table.
