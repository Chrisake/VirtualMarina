# VirtualMarina

A modular 3D marina visualization library for .NET 8. The same core DLL drives a desktop OpenGL view (WinForms) and a browser WebGL 2 view (Blazor WebAssembly).

## Documentation

**[Docs/](Docs/README.md)** has the full documentation:
- [getting started](Docs/01-getting-started.md) and [conventions](Docs/02-coordinates-and-conventions.md)
- guides for [layout](Docs/03-layout.md), [status and flags](Docs/04-status-and-flags.md), [multi-slip berths](Docs/05-multi-slip-berths.md), [selection, tooltips and actions](Docs/06-selection-tooltips-actions.md), [camera and focus](Docs/07-camera-and-focus.md), [appearance](Docs/08-appearance.md), [events](Docs/09-events-reference.md) and [hosting](Docs/10-hosting-and-custom-views.md)
- a generated [API reference](Docs/11-api-reference.md)

Every public type and member also has XML documentation comments, so Visual Studio shows them in IntelliSense. The libraries emit `VirtualMarina.*.xml` next to their DLLs (`src/Directory.Build.props`), and a missing comment on a public member is a build warning. After changing the public API, regenerate the reference with `dotnet run --project Docs/tools/ApiDocGen -- Docs/11-api-reference.md`.

## Quick start

```powershell
dotnet build VirtualMarina.sln
dotnet test                                                      # Core unit tests
dotnet run --project samples/VirtualMarina.TestHost.WinForms     # desktop, OpenGL 3.3
dotnet run --project samples/VirtualMarina.TestHost.Blazor       # then open http://localhost:5280
```

Requirements: .NET 8 SDK or newer (`global.json` rolls forward), and a GPU/driver with OpenGL 3.3 (desktop) or WebGL 2 (browser). The WinForms host accepts `--preset "Top Down"` and `--select A-L03`.

## Solution layout

```
VirtualMarina.sln
├─ src/
│  ├─ VirtualMarina.Core/                 net8.0 – no graphics/UI dependencies
│  │  ├─ Domain/      Dock, Slip, Boat, BoatType, SlipStatus, OrientedRect, LandArea,
│  │  │               MarinaLayout (+Validate), MarinaLayoutBuilder, SlipGenerator
│  │  ├─ Api/         IMarinaVisualizer, MarinaVisualizer (Layout/Status/View partials),
│  │  │               SlipUpdate, BatchUpdateResult, MarinaStatistics, events, StatusColorScheme
│  │  ├─ Camera/      OrbitCamera (smoothed pan/zoom/orbit), CameraConstraints, CameraPreset
│  │  ├─ Input/       MarinaInputController (platform-neutral pointer/keyboard → camera/picking)
│  │  ├─ Picking/     Ray, CPU ScenePicker (slip footprints + boat bounding boxes)
│  │  ├─ Geometry/    MeshBuilder, BoatMeshFactory (7 low-poly boats), MarinaMeshFactory, MeshLibrary
│  │  ├─ Rendering/   ISceneRenderer, RenderFrame, RenderObject, Lighting/WaterSettings,
│  │  │               ShaderSources (shared GLSL 330 / GLSL ES 300), SceneBuilder
│  │  └─ Mathematics/ MarinaMath
│  ├─ VirtualMarina.Rendering.OpenGL/     net8.0 – ISceneRenderer for OpenGL 3.3 (OpenTK bindings only)
│  ├─ VirtualMarina.WinForms/             net8.0-windows – MarinaViewControl (GLControl host)
│  └─ VirtualMarina.Blazor/               Razor class library – <MarinaView>, WebGlSceneRenderer, marinaWebGL.js
├─ samples/
│  ├─ VirtualMarina.SampleData/           mock marina + simulated ERP activity (shared by both hosts)
│  ├─ VirtualMarina.TestHost.WinForms/    WinExe test harness
│  └─ VirtualMarina.TestHost.Blazor/      Blazor WebAssembly test harness
└─ tests/
   └─ VirtualMarina.Core.Tests/           xUnit tests: API, events, picking, camera, geometry
```

## Architecture

```
 Host ERP (WinForms / Blazor page)
        │  API calls                         ▲ events (SlipClicked, SlipSelected, SlipStatusChanged…)
        ▼                                    │
 ┌──────────────────── VirtualMarina.Core (portable) ─────────────────────┐
 │ MarinaVisualizer ── domain state, selection, filter, color scheme      │
 │   ├─ MarinaInputController ── raw pointer/keys → OrbitCamera / picking │
 │   ├─ SceneBuilder ── domain → RenderObject list (rebuilt only on change)│
 │   └─ BuildRenderFrame() → RenderFrame (matrices, lights, water, objects)│
 │ ShaderSources ── one GLSL body, desktop + WebGL headers                 │
 └──────────────────────────────┬─────────────────────────────────────────┘
                                │ ISceneRenderer.Render(frame)
             ┌──────────────────┴───────────────────┐
   OpenGlSceneRenderer (OpenTK)            WebGlSceneRenderer (JS interop → WebGL 2)
   hosted by MarinaViewControl             hosted by <MarinaView>
```

- **Backends only draw.** Camera math, hit testing, scene composition and animation logic live in Core. A new backend (WebGPU, Vulkan, Avalonia, MAUI) implements `ISceneRenderer` and forwards input to `marina.Input`.
- **GPU-side animation.** Water waves, boats bobbing and rolling, the selection marker's spin, and highlight pulses are computed in the shaders from `uTime`. Instance data is re-sent only when `RenderFrame.SceneVersion` changes, so the browser renderer sends about 63 floats per frame.
- **Immutable snapshots.** `Slip`, `Dock` and `Boat` are records. Events carry snapshots, so host code can't change marina state without going through the API.

## Using the API

```csharp
var marina = new MarinaVisualizer();                 // or marinaViewControl.Marina
marina.InitializeLayout(layout);                     // MarinaLayout or MarinaLayoutBuilder

// Events → ERP
marina.SlipClicked  += (s, e) => erp.OpenBerth(e.SlipId, e.Status, e.Boat);
marina.SlipSelected += (s, e) => erp.ShowDetails(e.Slip);
marina.SlipStatusChanged += (s, e) => audit.Log(e.SlipId, e.OldStatus, e.NewStatus);

// Space management
marina.AddDock(new Dock("E", "Dock E", new Vector2(150, -6), 0, 60));
marina.AddSlips(SlipGenerator.AlongDock(marina.GetDock("E")!, DockSide.Left, 10, 5, 12));
marina.UpdateSlip(new SlipUpdate("E-L01") { Length = 14, Label = "E-1 (long)" });
marina.RemoveSlip("E-L10");

// Status
marina.AssignBoat("A-L03", new Boat("B-77", "Aurora", BoatType.MotorYacht) { LengthMeters = 18 });  // Occupied (red)
marina.ReserveSlip("A-L04", expectedBoat);                                                           // Reserved (blue)
marina.ReleaseSlip("A-L05");                                                                          // Free (green)
var result = marina.BatchUpdate(updatesFromErp);   // one scene rebuild and one LayoutChanged; errors collected

// Utilities
marina.SetStatusFilter(SlipStatusFilter.Free | SlipStatusFilter.Reserved);
marina.ClearSelection();
marina.ResetCamera();
marina.ApplyCameraPreset("Dock: Dock A");
marina.FocusSlips(new[] { "C-R02", "C-R05" }, CameraAngle.TopDown);
marina.SetStatusColor(SlipStatus.Reserved, ColorRgba.FromHex("#8A4FFF"));
marina.Lighting.SetSunAngles(azimuthDegrees: 220, elevationDegrees: 35);
marina.Water.WaveSpeed = 0;                         // freeze the water
```

### Docks, dividers and slips: position, size, orientation, type

```csharp
// Docks: from the shore end (constructor) or from the center; three construction types render differently.
marina.AddDock(Dock.FromCenter("E", "Dock E", center: new Vector2(150, 30), length: 60, width: 3, headingDegrees: 0, DockType.Concrete));
marina.UpdateDock(new DockUpdate("E") { Type = DockType.FloatingConcrete, HeadingDegrees = 10 });   // keeps the center

// Slips at an explicit center, heading (bow direction), length and width.
marina.AddSlip("E-01", "E", center: new Vector2(155, 10), headingDegrees: -90, length: 12, width: 5);
marina.UpdateSlip(SlipUpdate.Geometry("E-01", width: 6));

// Dividers between slips: finger piers, pile rows or floating booms.
marina.AddDivider(new Divider("E-D1", start: new Vector2(151.5f, 7.5f), headingDegrees: 90, length: 9, DividerType.Piles) { DockId = "E" });

// Or let the builder lay slips and dividers along a dock.
new MarinaLayoutBuilder().AddDock("A", "Dock A", Vector2.Zero, 0, 60, dock => dock
    .AddSlips(DockSide.Right, 10, 5, 12, dividers: DividerType.FingerPier)
    .AddSlip("A-GUEST", DockSide.Left, offsetAlong: 40, slipWidth: 8, slipLength: 16), type: DockType.FloatingWooden);
```

| `DockType` | Look | Default deck height |
|---|---|---|
| `FloatingWooden` (default) | plank deck, walers, dark pontoon floats, wooden guide piles | 0.5 m |
| `FloatingConcrete` | monolithic pontoon, rubber fenders, section joints, cleats, steel guide piles | 0.55 m |
| `Concrete` | fixed slab on columns, curbs, bollards | 1.1 m |

### Statuses and slip flags

`SlipStatus.TemporarilyFree` (yellow) means the berth holder's boat is away. The boat stays assigned and is drawn as a ghost, as for `Reserved`. Set it with `MarkTemporarilyFree(slipId)` or `SlipUpdate.TemporarilyFree(slipId)`.

| Flag | Rendering | Interaction |
|---|---|---|
| `IsVisible = false` | nothing is drawn (not even finger piers) | none |
| `IsDisabled = true` | pad and buoy gray, boat desaturated | no hover, selection, tooltip, actions or `SlipClicked` |
| `IsReadOnly = true` | normal | selectable, tooltip shows, actions window never opens |

`SetSlipVisible`, `SetSlipDisabled`, `SetSlipReadOnly`, or `SetSlipFlags(ids, visible, disabled, readOnly)` for many slips at once. A slip that becomes hidden, disabled or filtered out leaves the selection.

### Slip labels on the water

```csharp
marina.SlipLabelMode = SlipLabelMode.NonOccupied;   // None (default) | OnlyFree | NonOccupied | All
```

Writes each slip's `DisplayName` (its `Label`, or else its id) flat on the water, just past the slip's open end.

- **Size:** the text is scaled to use at most 65% of the slip width, between 0.3 m and 1 m tall, so neighbouring labels don't run together.
- **Orientation:** the top of the text points away from the dock, so it reads upright from the dock.
- **Colour:** white normally, yellow while hovered or selected, grey for disabled slips.
- **Which slips:** hidden slips and slips excluded by the status filter get no label.

The text uses a built-in stroke font: one flat mesh per character, no textures. It renders the same on every backend. The shader lifts the text just above the highest point the waves can reach, which is the sum of the wave component amplitudes × `Water.WaveAmplitude` (`ShaderSources.MaxWaveHeightFactor`). Since both the camera and the text are above that level, waves never cover a name from any viewpoint, even if you raise the amplitude. It covers A–Z, 0–9 and `- _ + . , : / ( ) # ?`. Lowercase letters are drawn as uppercase, and anything else is drawn as `?`.

### One boat across several slips

```csharp
var berth = marina.DockAlongside(new[] { "B-L10", "B-L11", "B-L12" }, yacht);             // parallel to the dock; any number of slips (≥ 2)
marina.AssignBoatToSlips(new[] { "C-L01", "C-L02" }, catamaran, SlipStatus.Reserved, MooringStyle.BowIn);
marina.UpdateMultiSlipBerth(berth.Id, status: SlipStatus.TemporarilyFree, slipIds: new[] { "B-L10", "B-L11" });
marina.ReleaseMultiSlipBerth(berth.Id);                                                   // frees all its slips
```

Each member slip carries the berth's status and boat, plus `Slip.BerthId`. `MarinaLayout.MultiSlipBerths` stores berths so layouts round-trip. The boat is drawn once, across the slips, using the first slip's orientation, and finger piers between member slips are not drawn. If you change the status or boat of a member through the single-slip API, the whole berth changes. Setting a member Free, or clearing its boat, releases the berth.

### Tooltip, actions and multi-select

- **Left click:** selects the slip and shows a tooltip above it.
- **Right click:** selects the slip and opens the actions window.
- **Ctrl+click or Shift+click:** adds or removes slips from the selection. Right-clicking inside a multi-selection opens its actions.

The popup follows its slip while the camera moves. Esc closes the popup, and a second Esc clears the selection.

```csharp
marina.SlipSelected += (s, e) =>                   // pre-filled with slip, dock, status and boat details
{
    var contract = e.ExternalData.GetOrAdd("Contract", () => erp.LoadContract(e.SlipId));   // host data kept on the slip
    e.Tooltip.AddLine("Contract", contract.Number);
    e.Actions.Add("checkin", "Check in", enabled: e.Status == SlipStatus.Free, icon: "⚓").Style = SlipActionStyle.Primary;
    e.Actions.Add("invoice", "Open invoice…").Description = "Opens the ERP invoice screen";
};
marina.MultiSlipSelected += (s, e) => e.Actions.Add("free-all", $"Free {e.ActionableSlips.Count} slips");
marina.SlipActionInvoked += (s, e) => erp.Execute(e.ActionId, e.Slips);   // e.Slip = primary, e.KeepPopupOpen to keep it open
```

- **Why `SlipSelected` fires:** `e.Reason` tells you: `Pointer` (a click, including a re-click on the selected slip), `Api`, or `Refresh`. A refresh happens when a selected slip changes while its popup is open, so actions can follow the new status.
- **`SlipAction` options:** `ActionId`, `Caption`, `Enabled`, `Visible`, `Description` (hover hint), `Icon`, `ShortcutText`, `Style` (Normal/Primary/Danger), `BeginGroup` (separator above), `KeepOpen` and `Tag`.
- **Showing the popup from code:** `ShowTooltip()`, `ShowActions()`, `RefreshPopup()`, `ClosePopup()` and `InvokeSlipAction(id)`.
- **Turning features off:** `TooltipsEnabled`, `ActionsEnabled` and `MultiSelectEnabled`.
- **Selection API:** `SetSelection`, `SelectSlips`, `AddToSelection`, `RemoveFromSelection` and `SelectedSlips` (see below).

### Changing the selection and focusing the camera

```csharp
// One or many slips. Disabled slips are discarded (so are hidden, filtered-out and unknown ids).
SelectionResult result = marina.SetSelection(new[] { "A-L01", "A-L02", "C-R08" }, focusCamera: true, CameraAngle.TopDown);
foreach (var rejected in result.Rejected) log($"{rejected.SlipId}: {rejected.Reason}");   // C-R08: Disabled
marina.SetSelection("B-L04");                                  // params overload, no focus

// Frame one or many slips: centered on their middle, zoomed so all of them (and their boats) fit.
marina.FocusSlip("C-L04", CameraAngle.TopDown);
marina.FocusSlips(marina.GetSlipsByDock("B").Select(s => s.Id), new CameraAngle(YawDegrees: 200, PitchDegrees: 45));
marina.FocusSelection();                                       // uses DefaultFocusAngle

marina.DefaultFocusAngle = CameraAngle.TopDown;                // for calls without an angle, and double-click
marina.FocusMargin = 0.12f;                                    // free space around the slips, per side
CameraPose preview = marina.ComputeFocusPose(slips, CameraAngle.TopDown);   // compute without moving
```

- **What `SetSelection` does:** raises `SlipSelected` or `MultiSlipSelected` as a click would. When nothing selectable remains, it clears the selection.
- **What focus includes:** focus uses every existing slip you pass, including disabled ones, because it only moves the camera.
- **`CameraAngle`:** yaw is the compass position of the camera around the target (180° = on the shore side, +Z at the top of the screen). Pitch is the angle above the horizon (up to 89°, i.e. straight down). `CameraAngle.TopDown` is (180°, 89°).
- **Without an angle:** the call uses `DefaultFocusAngle`. If that's null, it keeps the current yaw and looks down at least 35°.
- **Zoom limits:** `MinFocusDistance` (25 m) leaves some context around a single slip, and the camera's `MaxDistance` constraint caps how far out it goes.
- **Resizing:** if the view size changes while the camera is still where a focus put it (for example, focus was called before the view had its size), the focus is re-fitted. If the user has moved the camera since, a resize leaves it alone.

`Slip.ExternalData` is a `SlipDataBag` (string → object). All snapshots of a slip share the same instance, so values written in an event handler survive later updates. `SlipUpdate.ExternalData` merges entries from a batch. The visualizer never reads it.

**Custom views.** `MarinaViewControl` and `<MarinaView>` render the popup for you. Another host can subscribe to `PopupChanged`, render `ActivePopup`, and call `TryGetPopupAnchor(out screenPoint)` every frame to position it.

**Threading.** `MarinaVisualizer` is UI-thread affine. Marshal calls from background threads with `Control.BeginInvoke` (WinForms) or `InvokeAsync` (Blazor). Events are raised synchronously, after the state change has been applied.

### Hosting

**WinForms**
```csharp
var view = new MarinaViewControl { Dock = DockStyle.Fill };
Controls.Add(view);
view.Marina.InitializeLayout(layout);
```

**Blazor WebAssembly** (reference `VirtualMarina.Blazor`)
```razor
<MarinaView Marina="_marina" OnRendererReady="info => ..." />
```

### Default controls

| Input | Action |
|---|---|
| Left-drag / middle-drag | Pan (map-style) |
| Right-drag, or Shift+left-drag | Orbit |
| Mouse wheel | Zoom toward the cursor |
| Click | Select the slip or boat and show its tooltip (raises `SlipClicked` and `SlipSelected`) |
| Right-click | Select and open the actions window |
| Ctrl+click or Shift+click (Cmd+click in browsers) | Add/remove slips from a multi-selection (raises `MultiSlipSelected`) |
| Double-click | Focus the camera on the slip |
| Arrows / WASD, Shift+arrows, PageUp/PageDown, +/- | Pan, orbit, tilt, zoom |
| Esc / Home | Close the popup, then clear the selection / reset the camera |

Drag bindings are configurable through `marina.Input.LeftDragAction` and the related properties. `CameraConstraints` limits pitch (8°–89°, so the view never flips), eye height above the water, zoom distance, and how far the target can move.

### Replacing placeholder boats with GLTF models

Boats are procedural low-poly placeholders built by `BoatMeshFactory`. To use real models, load them into a `MeshData` in the same model space (bow +Z, waterline Y = 0, nominal dimensions from `BoatTypeCatalog`) and register them:

```csharp
marina.Meshes.Register(new MeshData(MeshIds.ForBoat(BoatType.MotorYacht), "Yacht.gltf", vertices, indices));
```

Both renderers re-upload changed meshes automatically.
