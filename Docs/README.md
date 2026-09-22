# VirtualMarina documentation

VirtualMarina is a .NET 8 library that renders an interactive 3D marina (piers, berths, boats, water) and exposes an API for host applications, typically an ERP. One core assembly drives both a WinForms/OpenGL view and a Blazor WebAssembly/WebGL view.

| Guide | What it covers |
|---|---|
| [Getting started](01-getting-started.md) | Build, reference the libraries, host the view in WinForms or Blazor, load a first marina |
| [Coordinates and conventions](02-coordinates-and-conventions.md) | Plan coordinates, headings, axes, ids, immutable snapshots, threading, exceptions |
| [Layout: piers, berths, dividers, land](03-layout.md) | `MarinaLayout`, `MarinaLayoutBuilder`, pier types, single-sided piers, polygon land and rock breakwaters, land berths, explicit positioning, runtime changes, batching |
| [Berth status, boats and flags](04-status-and-flags.md) | Free / Occupied / Reserved / Temporarily Free, boats, Visible / Disabled / Read-only, filter, statistics |
| [Multi-berths](05-multi-berths.md) | One boat across several berths: alongside and bow-in mooring |
| [Selection, tooltips and actions](06-selection-tooltips-actions.md) | Click / Ctrl or Shift+click / right-click, `SetSelection`, tooltip and action content, invoking actions, external data |
| [Camera and focus](07-camera-and-focus.md) | Presets, `FocusBerths` with `CameraAngle`, default focus angle, the orbit camera, constraints, input bindings |
| [Appearance](08-appearance.md) | Berth labels on the water, status colors, lighting, water, replacing boat models |
| [Events reference](09-events-reference.md) | Every event, when it is raised, and its data |
| [Hosting and custom views](10-hosting-and-custom-views.md) | `MarinaViewControl`, `<MarinaView>`, writing your own view or rendering backend |
| [Designer](12-designer.md) | Drawing land areas, piers, berths and land berths in the view, trees, undo, tracing a scaled aerial image, designer events, exporting the marina as objects |
| [Marina files](13-marina-file-format.md) | The `.marina.json` design file: what it holds, what it looks like, and how it stays compatible between versions |
| [Designer application](14-designer-app.md) | The stand-alone tool for drawing a marina to scale and saving it for the host application |
| [Localization](15-localization.md) | Resource files per assembly, choosing the language, adding a translation |
| [Compatibility and versioning](16-compatibility.md) | What the API promises between versions, what may change, and how to extend it safely |
| [API reference](11-api-reference.md) | Every public type and member, grouped by namespace |

The same descriptions are in the XML documentation comments. Visual Studio shows them in IntelliSense and Quick Info (the `VirtualMarina.*.xml` files are generated next to each DLL).

## The API at a glance

```csharp
using VirtualMarina.Core.Api;       // IMarinaVisualizer, MarinaVisualizer, events, BerthUpdate, tooltip/actions
using VirtualMarina.Core.Camera;    // CameraAngle, CameraPose, CameraPreset
using VirtualMarina.Core.Domain;    // Pier, Berth, Boat, Divider, MultiBerth, MarinaLayoutBuilder
using VirtualMarina.Core.Design;    // MarinaDesigner, DesignTool, ReferenceImage
using VirtualMarina.Core.Serialization; // MarinaDocument: load and save a whole marina

IMarinaVisualizer marina = marinaViewControl.Marina;           // WinForms (Blazor: new MarinaVisualizer())

marina.InitializeLayout(layout);                               // piers, berths, dividers, berths, land
marina.AssignBoat("A-L03", new Boat("B-77", "Aurora", BoatType.MotorYacht));
marina.MarkTemporarilyFree("A-L04");
marina.SetBerthDisabled("C-R08", true);
marina.MoorAlongside(new[] { "B-L10", "B-L11", "B-L12" }, superyacht);

marina.BerthSelected += (s, e) =>
{
    e.Tooltip.AddLine("Contract", erp.Contract(e.BerthId));
    e.Actions.Add("checkin", "Check in", enabled: e.Status == BerthStatus.Free);
};
marina.BerthActionInvoked += (s, e) => erp.Execute(e.ActionId, e.Berths);

marina.BerthLabelMode = BerthLabelMode.NonOccupied;
marina.SetSelection(new[] { "A-L01", "A-L02" }, focusCamera: true, CameraAngle.TopDown);

marina.Designer.IsActive = true;                               // draw land, piers and berths in the view
marina.Designer.Tool = DesignTool.DrawPier;
object[] everything = marina.ExportObjects();                  // land areas, piers, dividers, berths, berths
```

## Solution layout

| Project | Target | Purpose |
|---|---|---|
| `VirtualMarina.Core` | net8.0 | Everything platform-neutral: domain, API, events, camera, input, picking, geometry, shaders |
| `VirtualMarina.Rendering.OpenGL` | net8.0 | `ISceneRenderer` for OpenGL 3.3 (OpenTK) |
| `VirtualMarina.WinForms` | net8.0-windows | `MarinaViewControl`: OpenGL view plus the tooltip/actions popup; `MarinaDesignerPanel` |
| `VirtualMarina.Blazor` | net8.0 (browser) | `<MarinaView>` component, `WebGlSceneRenderer` and `<MarinaDesignerPanel>` |
| `samples/VirtualMarina.SampleData` | net8.0 | Sample marina and `SampleErpIntegration` (tooltip/action handlers) |
| `samples/VirtualMarina.TestHost.WinForms` / `.Blazor` | | Test harnesses exercising every API |
| `tests/VirtualMarina.Core.Tests` | net8.0 | xUnit tests |
