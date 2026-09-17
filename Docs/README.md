# VirtualMarina documentation

VirtualMarina is a .NET 8 library that renders an interactive 3D marina (docks, slips, boats, water) and exposes an API for host applications, typically an ERP. One core assembly drives both a WinForms/OpenGL view and a Blazor WebAssembly/WebGL view.

| Guide | What it covers |
|---|---|
| [Getting started](01-getting-started.md) | Build, reference the libraries, host the view in WinForms or Blazor, load a first marina |
| [Coordinates and conventions](02-coordinates-and-conventions.md) | Plan coordinates, headings, axes, ids, immutable snapshots, threading, exceptions |
| [Layout: docks, slips, dividers, land](03-layout.md) | `MarinaLayout`, `MarinaLayoutBuilder`, dock types, single-sided docks, polygon land and rock breakwaters, land slips, explicit positioning, runtime changes, batching |
| [Slip status, boats and flags](04-status-and-flags.md) | Free / Occupied / Reserved / Temporarily Free, boats, Visible / Disabled / Read-only, filter, statistics |
| [Multi-slip berths](05-multi-slip-berths.md) | One boat across several slips: alongside and bow-in mooring |
| [Selection, tooltips and actions](06-selection-tooltips-actions.md) | Click / Ctrl or Shift+click / right-click, `SetSelection`, tooltip and action content, invoking actions, external data |
| [Camera and focus](07-camera-and-focus.md) | Presets, `FocusSlips` with `CameraAngle`, default focus angle, the orbit camera, constraints, input bindings |
| [Appearance](08-appearance.md) | Slip labels on the water, status colors, lighting, water, replacing boat models |
| [Events reference](09-events-reference.md) | Every event, when it is raised, and its data |
| [Hosting and custom views](10-hosting-and-custom-views.md) | `MarinaViewControl`, `<MarinaView>`, writing your own view or rendering backend |
| [API reference](11-api-reference.md) | Every public type and member, grouped by namespace |

The same descriptions are in the XML documentation comments. Visual Studio shows them in IntelliSense and Quick Info (the `VirtualMarina.*.xml` files are generated next to each DLL).

## The API at a glance

```csharp
using VirtualMarina.Core.Api;       // IMarinaVisualizer, MarinaVisualizer, events, SlipUpdate, tooltip/actions
using VirtualMarina.Core.Camera;    // CameraAngle, CameraPose, CameraPreset
using VirtualMarina.Core.Domain;    // Dock, Slip, Boat, Divider, MultiSlipBerth, MarinaLayoutBuilder

IMarinaVisualizer marina = marinaViewControl.Marina;           // WinForms (Blazor: new MarinaVisualizer())

marina.InitializeLayout(layout);                               // docks, slips, dividers, berths, land
marina.AssignBoat("A-L03", new Boat("B-77", "Aurora", BoatType.MotorYacht));
marina.MarkTemporarilyFree("A-L04");
marina.SetSlipDisabled("C-R08", true);
marina.DockAlongside(new[] { "B-L10", "B-L11", "B-L12" }, superyacht);

marina.SlipSelected += (s, e) =>
{
    e.Tooltip.AddLine("Contract", erp.Contract(e.SlipId));
    e.Actions.Add("checkin", "Check in", enabled: e.Status == SlipStatus.Free);
};
marina.SlipActionInvoked += (s, e) => erp.Execute(e.ActionId, e.Slips);

marina.SlipLabelMode = SlipLabelMode.NonOccupied;
marina.SetSelection(new[] { "A-L01", "A-L02" }, focusCamera: true, CameraAngle.TopDown);
```

## Solution layout

| Project | Target | Purpose |
|---|---|---|
| `VirtualMarina.Core` | net8.0 | Everything platform-neutral: domain, API, events, camera, input, picking, geometry, shaders |
| `VirtualMarina.Rendering.OpenGL` | net8.0 | `ISceneRenderer` for OpenGL 3.3 (OpenTK) |
| `VirtualMarina.WinForms` | net8.0-windows | `MarinaViewControl`: OpenGL view plus the tooltip/actions popup |
| `VirtualMarina.Blazor` | net8.0 (browser) | `<MarinaView>` component and `WebGlSceneRenderer` |
| `samples/VirtualMarina.SampleData` | net8.0 | Sample marina and `SampleErpIntegration` (tooltip/action handlers) |
| `samples/VirtualMarina.TestHost.WinForms` / `.Blazor` | | Test harnesses exercising every API |
| `tests/VirtualMarina.Core.Tests` | net8.0 | xUnit tests |
