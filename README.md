# VirtualMarina

A modular 3D marina visualization library for .NET 8. The same core DLL drives a desktop OpenGL view (WinForms) and a browser WebGL 2 view (Blazor WebAssembly).

## Documentation

**[Docs/](Docs/README.md)** has the full documentation:
- [getting started](Docs/01-getting-started.md), [conventions](Docs/02-coordinates-and-conventions.md) and the [coordinate conventions](Docs/20-coordinate-conventions.md) in detail
- guides for [layout](Docs/03-layout.md), [the sea and the shore](Docs/17-sea-and-shore.md), [the designer](Docs/12-designer.md), [the designer applications](Docs/14-designer-app.md), [marina files](Docs/13-marina-file-format.md) (with a [JSON schema](Docs/schema/marina.schema.json)), [status and flags](Docs/04-status-and-flags.md), [multi-berths](Docs/05-multi-berths.md), [selection, tooltips and actions](Docs/06-selection-tooltips-actions.md), [camera and focus](Docs/07-camera-and-focus.md), [appearance](Docs/08-appearance.md), [events](Docs/09-events-reference.md), [hosting](Docs/10-hosting-and-custom-views.md), [localization](Docs/15-localization.md), [samples](Docs/18-samples-and-use-cases.md), [compatibility](Docs/16-compatibility.md) and [static analysis](Docs/19-static-analysis.md)
- a generated [API reference](Docs/11-api-reference.md)

Every public type and member also has XML documentation comments, so Visual Studio shows them in IntelliSense. The libraries emit `VirtualMarina.*.xml` next to their DLLs (`src/Directory.Build.props`), and a missing comment on a public member is a build error. After changing the public API, regenerate the reference on Windows (the generator loads the WinForms library) with `dotnet run --project Docs/tools/ApiDocGen`; with no argument it rewrites `Docs/11-api-reference.md` in place. CI fails when the committed copy is out of date.

## Quick start

The solution builds on Windows, Linux and macOS. The WinForms projects target `net8.0-windows`: Linux and macOS compile them with `-p:EnableWindowsTargeting=true` but cannot run them, so the WinForms test host, designer and tests are Windows-only.

**Windows**

```powershell
dotnet build VirtualMarina.sln
dotnet test VirtualMarina.sln                                    # every test project, the WinForms ones included
dotnet run --project samples/VirtualMarina.TestHost.WinForms     # desktop, OpenGL 3.3
dotnet run --project samples/VirtualMarina.TestHost.Blazor       # then open http://localhost:5280
```

**Linux and macOS**

```bash
dotnet build VirtualMarina.sln -p:EnableWindowsTargeting=true
for project in tests/*.Tests/*.Tests.csproj; do                  # every test project except the WinForms one
  case "$project" in *WinForms.Tests*) continue ;; esac
  dotnet test "$project" --no-build
done
dotnet run --project samples/VirtualMarina.TestHost.Blazor       # then open http://localhost:5280
```

The designer runs in the browser too: `dotnet run --project apps/VirtualMarina.Designer.Blazor` (then open http://localhost:5290), or as a windowed app through the desktop launcher, `dotnet run --project apps/VirtualMarina.Designer.Desktop`. See [the designer applications](Docs/14-designer-app.md).

Requirements: a .NET 8 SDK (`global.json` accepts any 8.0 feature band, so the analysers stay the same everywhere), and a GPU/driver with OpenGL 3.3 (desktop) or WebGL 2 (browser). The JavaScript checks CI runs need Node 20 or newer: `npm ci && npx eslint . && npx tsc -p jsconfig.json`.

Every build runs the .NET and SonarAnalyzer static analysers with every finding an error, and `tools/sonar-scan.ps1` sends the same build to SonarQube Cloud. See [static analysis](Docs/19-static-analysis.md) for which rules are on, which are deliberately off, and why, and for what CI checks.

The libraries are licensed GPL-3.0-only. Versions come from git tags (MinVer); pushing a `v*` tag packs the four libraries (`.github/workflows/release.yml`).

## Solution layout

```
VirtualMarina.sln
├─ src/
│  ├─ VirtualMarina.Core/                 net8.0 – no graphics/UI dependencies
│  │  ├─ Domain/      Pier, Berth, Boat, BoatType, BerthStatus, OrientedRect, LandArea (polygon), Shoreline,
│  │  │               MarineTraffic, MarinaLayout (+Validate), MarinaLayoutBuilder, LandAreaBuilder, BerthGenerator
│  │  ├─ Design/      MarinaDesigner (draw land/piers/berths/land berths, trees, undo/redo, reference image, calibration;
│  │  │               split into partial files, one handler per tool under Tools/), ReferenceImage, DesignerSettings
│  │  ├─ Api/         IMarinaVisualizer, MarinaVisualizer (Layout/Status/Selection/View partials),
│  │  │               BerthUpdate, BatchUpdateResult, MarinaStatistics, events, StatusColorScheme
│  │  ├─ Camera/      OrbitCamera (smoothed pan/zoom/orbit), CameraConstraints, CameraPreset
│  │  ├─ Input/       MarinaInputController (platform-neutral pointer/keyboard → camera/picking), MarinaKeyMap
│  │  ├─ Picking/     Ray, CPU ScenePicker (berth footprints + boat triangles)
│  │  ├─ Geometry/    MeshBuilder, BoatMeshFactory (low-poly boats), MarinaMeshFactory,
│  │  │               LandMeshFactory (polygon slabs, rock breakwaters, trees), MeshLibrary
│  │  ├─ Rendering/   ISceneRenderer, RenderFrame, RenderLayer/RenderBatch/InstanceData, MarinaStyle,
│  │  │               ShaderSources (shared GLSL 330 / GLSL ES 300), SceneBuilder
│  │  ├─ Serialization/ MarinaDocument (.marina.json: layout + look + motion + camera), MarinaJson, migrations
│  │  └─ Mathematics/ MarinaMath, PolygonMath
│  ├─ VirtualMarina.Rendering.OpenGL/     net8.0 – ISceneRenderer for OpenGL 3.3 (OpenTK bindings only)
│  ├─ VirtualMarina.WinForms/             net8.0-windows – MarinaViewControl (GLControl host), MarinaDesignerPanel, ReferenceImageLoader
│  └─ VirtualMarina.Blazor/               Razor class library – <MarinaView>, <MarinaDesignerPanel>, WebGlSceneRenderer,
│                                         marinaWebGL.js, marinaView.css
├─ apps/
│  ├─ VirtualMarina.Designer/             net8.0-windows WinExe – the marina designer tool (draws to scale, saves .marina.json)
│  ├─ VirtualMarina.Designer.Blazor/      Blazor WebAssembly – the same designer in the browser
│  ├─ VirtualMarina.Designer.Common/      net8.0 – what both designers share: session, command table, dialogs contract, strings
│  └─ VirtualMarina.Designer.Desktop/     net8.0 – launcher that serves the Blazor designer and opens it in an app window
├─ samples/
│  ├─ VirtualMarina.SampleData/           mock marina + simulated ERP activity (shared by both hosts)
│  ├─ VirtualMarina.TestHost.WinForms/    WinExe test harness
│  └─ VirtualMarina.TestHost.Blazor/      Blazor WebAssembly test harness
├─ tests/
│  ├─ VirtualMarina.Core.Tests/           xUnit: API, events, picking, camera, geometry, designer, file format (golden
│  │                                      files, property-based tests), shaders; API baselines for Core and OpenGL
│  ├─ VirtualMarina.Blazor.Tests/         <MarinaView>, <MarinaDesignerPanel> and the WebGL renderer against a fake JS runtime
│  ├─ VirtualMarina.Designer.Common.Tests/ the shared designer session, commands, renaming and launcher liveness
│  ├─ VirtualMarina.WinForms.Tests/       net8.0-windows – the WinForms view and panel (Windows only)
│  └─ VirtualMarina.TestSupport/          shared helpers: API baselines, a fixed clock, the repository root
└─ Docs/tools/ApiDocGen/                  generates Docs/11-api-reference.md
```

## Architecture

```
 Host ERP (WinForms / Blazor page)
        │  API calls                         ▲ events (BerthClicked, BerthSelected, BerthStatusChanged…)
        ▼                                    │
 ┌──────────────────── VirtualMarina.Core (portable) ─────────────────────┐
 │ MarinaVisualizer ── domain state, selection, filter, color scheme      │
 │   ├─ MarinaInputController ── raw pointer/keys → OrbitCamera / picking │
 │   ├─ SceneBuilder ── domain → layered RenderObject instances           │
 │   │    (each layer rebuilt only when what it shows changes)            │
 │   └─ BuildRenderFrame() → RenderFrame (matrices, lights, water, layers)│
 │ ShaderSources ── one GLSL body, desktop + WebGL headers                │
 └──────────────────────────────┬─────────────────────────────────────────┘
                                │ ISceneRenderer.Render(frame)
             ┌──────────────────┴───────────────────┐
   OpenGlSceneRenderer (OpenTK)            WebGlSceneRenderer (JS interop → WebGL 2)
   hosted by MarinaViewControl             hosted by <MarinaView>
```

- **Backends only draw.** Camera math, hit testing, scene composition and animation logic live in Core. A new backend (WebGPU, Vulkan, Avalonia, MAUI) implements `ISceneRenderer` and forwards input to `marina.Input`; see [hosting and custom views](Docs/10-hosting-and-custom-views.md).
- **Layered, instanced scene.** `RenderFrame.Layers` splits the scene by what changes it (structure, shadows, berths and boats, selection markers, designer overlay), and both backends draw each layer's batches instanced. A layer is uploaded again only when its versions say so, and a hover or status change patches just the instances it touched.
- **GPU-side animation, on-demand frames.** Water waves, boats bobbing and rolling, the selection marker's spin, and highlight pulses are computed in the shaders from `uTime`. The views draw only while something changes or moves (`MarinaVisualizer.NeedsRedraw`, `IsAnimating`); a still marina costs nothing, and `ContinuousRendering` on either view draws every frame regardless.
- **Immutable snapshots.** `Berth`, `Pier`, `Boat` and the other domain types are records with value equality. Events carry snapshots, so host code can't change marina state without going through the API.

## Using the API

```csharp
var marina = new MarinaVisualizer();                 // or marinaViewControl.Marina
marina.InitializeLayout(layout);                     // MarinaLayout or MarinaLayoutBuilder

// Events → ERP
marina.BerthClicked  += (s, e) => erp.OpenBerth(e.BerthId, e.Status, e.Boat);
marina.BerthSelected += (s, e) => erp.ShowDetails(e.Berth);
marina.BerthStatusChanged += (s, e) => audit.Log(e.BerthId, e.OldStatus, e.NewStatus);

// Space management
marina.AddPier(new Pier("E", "Pier E", new Vector2(150, -6), 0, 60));
marina.AddBerths(BerthGenerator.AlongPier(marina.GetPier("E")!, PierSide.Left, 10, 5, 12));
marina.UpdateBerth(new BerthUpdate("E-L01") { Length = 14, Label = "E-1 (long)" });
marina.RemoveBerth("E-L10");

// Status
marina.AssignBoat("A-L03", new Boat("B-77", "Aurora", BoatType.MotorYacht) { LengthMeters = 18 });  // Occupied (red)
marina.ReserveBerth("A-L04", expectedBoat);                                                           // Reserved (blue)
marina.ReleaseBerth("A-L05");                                                                          // Free (green)
var result = marina.BatchUpdate(updatesFromErp);   // one scene rebuild and one LayoutChanged; errors collected

// Utilities
marina.SetStatusFilter(BerthStatusFilter.Free | BerthStatusFilter.Reserved);
marina.ClearSelection();
marina.ResetCamera();
marina.ApplyCameraPreset(MarinaVisualizer.PierPresetKey("A"));   // by key: preset names are localized
marina.ShowPierCloseUp("A");                        // the same close-up, directly
marina.FocusBerths(new[] { "C-R02", "C-R05" }, CameraAngle.TopDown);
marina.SetStatusColor(BerthStatus.Reserved, ColorRgba.FromHex("#8A4FFF"));
marina.Lighting.SetSunAngles(azimuthDegrees: 220, elevationDegrees: 35);
marina.Water.WaveSpeed = 0;                         // freeze the water
```

### Piers, dividers and berths: position, size, orientation, type

```csharp
// Piers: from the shore end (constructor) or from the center; three construction types render differently.
marina.AddPier(Pier.FromCenter("E", "Pier E", center: new Vector2(150, 30), length: 60, width: 3, headingDegrees: 0, PierType.Concrete));
marina.UpdatePier(new PierUpdate("E") { Type = PierType.FloatingConcrete, HeadingDegrees = 10 });   // keeps the center

// Berths at an explicit center, heading (bow direction), length and width.
marina.AddBerth("E-01", "E", center: new Vector2(155, 10), headingDegrees: -90, length: 12, width: 5);
marina.UpdateBerth(BerthUpdate.Geometry("E-01", width: 6));

// Dividers between berths: finger piers, pile rows or floating booms.
marina.AddDivider(new Divider("E-D1", start: new Vector2(151.5f, 7.5f), headingDegrees: 90, length: 9, DividerType.Piles) { PierId = "E" });

// Or let the builder lay berths and dividers along a pier.
new MarinaLayoutBuilder().AddPier("A", "Pier A", Vector2.Zero, 0, 60, pier => pier
    .AddBerths(PierSide.Right, 10, 5, 12, dividers: DividerType.FingerPier)
    .AddBerth("A-GUEST", PierSide.Left, offsetAlong: 40, berthWidth: 8, berthLength: 16), type: PierType.FloatingWooden);
```

| `PierType` | Look | Default deck height |
|---|---|---|
| `FloatingWooden` (default) | plank deck, walers, dark pontoon floats | 0.5 m |
| `FloatingConcrete` | monolithic pontoon, rubber fenders, section joints, cleats | 0.55 m |
| `Concrete` | fixed slab on columns, curbs, bollards | 1.1 m |

### Statuses and berth flags

`BerthStatus.TemporarilyFree` (yellow) means the berth holder's boat is away. The boat stays assigned and is drawn as a ghost, as for `Reserved`. Set it with `MarkTemporarilyFree(berthId)` or `BerthUpdate.TemporarilyFree(berthId)`.

| Flag | Rendering | Interaction |
|---|---|---|
| `IsVisible = false` | nothing is drawn (not even finger piers) | none |
| `IsDisabled = true` | pad and buoy gray, boat desaturated | no hover, selection, tooltip, actions or `BerthClicked` |
| `IsReadOnly = true` | normal | selectable, tooltip shows, actions window never opens |

`SetBerthVisible`, `SetBerthDisabled`, `SetBerthReadOnly`, or `SetBerthFlags(ids, visible, disabled, readOnly)` for many berths at once. A berth that becomes hidden, disabled or filtered out leaves the selection.

### Berth labels on the water

```csharp
marina.BerthLabelMode = BerthLabelMode.NonOccupied;   // None (default) | OnlyFree | NonOccupied | All
```

Writes each berth's `DisplayName` (its `Label`, or else its id) flat on the water, just past the berth's open end.

- **Size:** the text is scaled to use at most 65% of the berth width, between 0.3 m and 1 m tall, so neighbouring labels don't run together.
- **Orientation:** the top of the text points away from the pier, so it reads upright from the pier.
- **Colour:** white normally, yellow while hovered or selected, grey for disabled berths.
- **Which berths:** hidden berths and berths excluded by the status filter get no label.

The text uses a built-in stroke font: one flat mesh per character, no textures. It renders the same on every backend. The shader lifts the text just above the highest point the waves can reach, which is the sum of the wave component amplitudes × `Water.WaveAmplitude` (`ShaderSources.MaxWaveHeightFactor`). Since both the camera and the text are above that level, waves never cover a name from any viewpoint, even if you raise the amplitude. It covers A–Z, 0–9 and `- _ + . , : / ( ) # ?`. Lowercase letters are drawn as uppercase, and anything else is drawn as `?`.

### One boat across several berths

```csharp
var berth = marina.MoorAlongside(new[] { "B-L10", "B-L11", "B-L12" }, yacht);             // parallel to the pier; any number of berths (≥ 2)
marina.AssignBoatToBerths(new[] { "C-L01", "C-L02" }, catamaran, BerthStatus.Reserved, MooringStyle.BowIn);
marina.UpdateMultiBerth(berth.Id, status: BerthStatus.TemporarilyFree, berthIds: new[] { "B-L10", "B-L11" });
marina.ReleaseMultiBerth(berth.Id);                                                   // frees all its berths
```

Each member berth carries the berth's status and boat, plus `Berth.MultiBerthId`. `MarinaLayout.MultiBerths` stores berths so layouts round-trip. The boat is drawn once, across the berths, using the first berth's orientation, and finger piers between member berths are not drawn. If you change the status or boat of a member through the single-berth API, the whole berth changes. Setting a member Free, or clearing its boat, releases the berth.

### Tooltip, actions and multi-select

- **Left click:** selects the berth and shows a tooltip above it.
- **Right click:** selects the berth and opens the actions window.
- **Ctrl+click or Shift+click:** adds or removes berths from the selection. Right-clicking inside a multi-selection opens its actions.

The popup follows its berth while the camera moves. Esc closes the popup, and a second Esc clears the selection.

```csharp
marina.BerthSelected += (s, e) =>                   // pre-filled with berth, pier, status and boat details
{
    var contract = e.ExternalData.GetOrAdd("Contract", () => erp.LoadContract(e.BerthId));   // host data kept on the berth
    e.Tooltip.AddLine("Contract", contract.Number);
    e.Actions.Add("checkin", "Check in", enabled: e.Status == BerthStatus.Free, icon: "⚓").Style = BerthActionStyle.Primary;
    e.Actions.Add("invoice", "Open invoice…").Description = "Opens the ERP invoice screen";
};
marina.MultiBerthSelected += (s, e) => e.Actions.Add("free-all", $"Free {e.ActionableBerths.Count} berths");
marina.BerthActionInvoked += (s, e) => erp.Execute(e.ActionId, e.Berths);   // e.Berth = primary, e.KeepPopupOpen to keep it open
```

- **Why `BerthSelected` fires:** `e.Reason` tells you: `Pointer` (a click, including a re-click on the selected berth), `Api`, or `Refresh`. A refresh happens when a selected berth changes while its popup is open, so actions can follow the new status.
- **`BerthAction` options:** `ActionId`, `Caption`, `Enabled`, `Visible`, `Description` (hover hint), `Icon`, `ShortcutText`, `Style` (Normal/Primary/Danger), `BeginGroup` (separator above), `KeepOpen` and `Tag`.
- **Showing the popup from code:** `ShowTooltip()`, `ShowActions()`, `RefreshPopup()`, `ClosePopup()` and `InvokeBerthAction(id)`.
- **Turning features off:** `TooltipsEnabled`, `ActionsEnabled` and `MultiSelectEnabled`.
- **Selection API:** `SetSelection`, `SelectBerths`, `AddToSelection`, `RemoveFromSelection` and `SelectedBerths` (see below).

### Changing the selection and focusing the camera

```csharp
// One or many berths. Disabled berths are discarded (so are hidden, filtered-out and unknown ids).
SelectionResult result = marina.SetSelection(new[] { "A-L01", "A-L02", "C-R08" }, focusCamera: true, CameraAngle.TopDown);
foreach (var rejected in result.Rejected) log($"{rejected.BerthId}: {rejected.Reason}");   // C-R08: Disabled
marina.SetSelection("B-L04");                                  // params overload, no focus

// Frame one or many berths: centered on their middle, zoomed so all of them (and their boats) fit.
marina.FocusBerth("C-L04", CameraAngle.TopDown);
marina.FocusBerths(marina.GetBerthsByPier("B").Select(s => s.Id), new CameraAngle(YawDegrees: 200, PitchDegrees: 45));
marina.FocusSelection();                                       // uses DefaultFocusAngle

marina.DefaultFocusAngle = CameraAngle.TopDown;                // for calls without an angle, and double-click
marina.FocusMargin = 0.12f;                                    // free space around the berths, per side
CameraPose preview = marina.ComputeFocusPose(berths, CameraAngle.TopDown);   // compute without moving
```

- **What `SetSelection` does:** raises `BerthSelected` or `MultiBerthSelected` as a click would. When nothing selectable remains, it clears the selection.
- **What focus includes:** focus uses every existing berth you pass, including disabled ones, because it only moves the camera.
- **`CameraAngle`:** yaw is the compass position of the camera around the target (180° = on the shore side, +Z at the top of the screen). Pitch is the angle above the horizon (up to 89°, i.e. straight down). `CameraAngle.TopDown` is (180°, 89°).
- **Without an angle:** the call uses `DefaultFocusAngle`. If that's null, it keeps the current yaw and looks down at least 35°.
- **Zoom limits:** `MinFocusDistance` (25 m) leaves some context around a single berth, and the camera's `MaxDistance` constraint caps how far out it goes.
- **Resizing:** if the view size changes while the camera is still where a focus put it (for example, focus was called before the view had its size), the focus is re-fitted. If the user has moved the camera since, a resize leaves it alone.

`Berth.ExternalData` is a `MarinaDataBag` (string → object). All snapshots of a berth share the same instance, so values written in an event handler survive later updates. `BerthUpdate.ExternalData` merges entries from a batch. The visualizer never reads it.

**Custom views.** `MarinaViewControl` and `<MarinaView>` render the popup for you. Another host can subscribe to `PopupChanged`, render `ActivePopup`, and call `TryGetPopupAnchor(out screenPoint)` every frame to position it.

**Threading.** `MarinaVisualizer` is UI-thread affine. Marshal calls from background threads with `Control.BeginInvoke` (WinForms) or `InvokeAsync` (Blazor). Events are raised synchronously, after the state change has been applied. If an update reaches a `MarinaViewControl`'s visualizer from another thread anyway, the control marshals its own response (redraw, popup, forwarded events) onto the UI thread, but the visualizer itself is still not thread-safe.

### Hosting

**WinForms**
```csharp
var view = new MarinaViewControl { Dock = DockStyle.Fill };
Controls.Add(view);
view.Marina.InitializeLayout(layout);
```

If OpenGL 3.3 is not available (a VM, a remote desktop), the control retries without anti-aliasing, then shows a placeholder and raises `RenderError`; `RetryRendering()` tries again once the cause is gone.

**Blazor WebAssembly** (reference `VirtualMarina.Blazor`)
```razor
<MarinaView Marina="_marina" OnRendererReady="info => ..." OnRendererError="message => ..." AriaLabel="Marina map" />
```

The script and the popup CSS (`marinaView.css`, in the `virtualmarina` cascade layer so any host rule wins) are linked automatically. `<MarinaDesignerPanel>` uses scoped CSS, so the host page needs the usual `<link rel="stylesheet" href="{YourApp}.styles.css" />`.

### Default controls

| Input | Action |
|---|---|
| Left-drag / middle-drag | Pan (map-style: the point under the pointer stays under it) |
| Right-drag, or Shift+left-drag | Orbit |
| Mouse wheel | Zoom toward what is under the cursor |
| Hover | Highlight the berth or boat under the cursor (exact shape); the cursor becomes a pointer |
| Click | Select the berth or boat and show its tooltip (raises `BerthClicked` and `BerthSelected`) |
| Right-click | Select and open the actions window |
| Ctrl+click or Shift+click (Cmd+click in browsers) | Add/remove berths from a multi-selection (raises `MultiBerthSelected`) |
| Double-click | Focus the camera on the berth |
| Arrows / WASD, Shift+arrows, PageUp/PageDown, +/- | Pan, orbit, tilt, zoom |
| Esc / Home | Close the popup, then clear the selection / reset the camera |

The view only takes unmodified navigation and editing keys (plus Shift, which orbits). Chords with Ctrl, Alt or Cmd go to the host application or the browser, so its own shortcuts always win; the one exception is Undo (Ctrl/Cmd+Z) and Redo (Ctrl+Shift+Z, Ctrl+Y, Cmd+Shift+Z) for the designer, and only when the host has no accelerator of its own for them. Esc is claimed only when there is something to close, so a dialog's Cancel button still gets it. `MarinaKeyMap` holds the table both views use.

Drag bindings are configurable through `marina.Input.LeftDragAction` and the related properties. `CameraConstraints` limits pitch (8°–89°, so the view never flips), eye height above the water, zoom distance, and how far the target can move.

### Replacing placeholder boats with GLTF models

Boats are procedural low-poly placeholders built by `BoatMeshFactory`. To use real models, load them into a `MeshData` in the same model space (bow +Z, waterline Y = 0, nominal dimensions from `BoatTypeCatalog`) and register them:

```csharp
marina.Meshes.Register(new MeshData(MeshIds.ForBoat(BoatType.MotorYacht), "Yacht.gltf", vertices, indices));
```

Both renderers re-upload changed meshes automatically.
