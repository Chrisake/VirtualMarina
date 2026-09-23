# Getting started

## Requirements

- **.NET SDK:** a .NET 8 SDK. `global.json` rolls forward within 8.0 only (`latestFeature`), so a newer SDK on the machine does not bring new analyser rules into a warnings-as-errors build.
- **Desktop:** a GPU and driver supporting OpenGL 3.3 core profile.
- **Browser:** a browser with WebGL 2. The Blazor component requires **Blazor WebAssembly**, because it uses synchronous JS interop.

- **Operating system:** the libraries, the Blazor apps and every test project but one build and run on Windows, Linux and macOS. The WinForms projects (`VirtualMarina.WinForms`, the WinForms designer and test host, `VirtualMarina.WinForms.Tests`) target `net8.0-windows`: Linux and macOS compile them with `-p:EnableWindowsTargeting=true`, but only Windows runs them.

On Windows:

```powershell
dotnet build VirtualMarina.sln
dotnet test VirtualMarina.sln                                     # all test projects
dotnet run --project samples/VirtualMarina.TestHost.WinForms      # desktop harness
dotnet run --project samples/VirtualMarina.TestHost.Blazor        # then open http://localhost:5280
```

On Linux and macOS:

```bash
dotnet build VirtualMarina.sln -p:EnableWindowsTargeting=true
dotnet test tests/VirtualMarina.Core.Tests --no-build             # likewise Blazor.Tests and Designer.Common.Tests
dotnet run --project samples/VirtualMarina.TestHost.Blazor        # then open http://localhost:5280
```

The test projects are `VirtualMarina.Core.Tests`, `VirtualMarina.Blazor.Tests`, `VirtualMarina.Designer.Common.Tests` and the Windows-only `VirtualMarina.WinForms.Tests`; `tests/VirtualMarina.TestSupport` holds their shared helpers. How they are organised, and what CI runs on each OS, is in [static analysis](19-static-analysis.md#tests).

## Referencing the libraries

| Application | Reference |
|---|---|
| WinForms | `VirtualMarina.WinForms` (brings `Core` and `Rendering.OpenGL`, plus the `OpenTK.GLControl` package) |
| Blazor WebAssembly | `VirtualMarina.Blazor` (brings `Core`) |
| Custom host or backend | `VirtualMarina.Core` |

The four libraries are also NuGet packages of the same names (GPL-3.0-only), produced by `dotnet pack` and by the release workflow when a `v*` tag is pushed. Nothing else in the solution is packable.

Each library produces an XML documentation file. Keep it next to the DLL so Visual Studio shows the API docs in IntelliSense.

## WinForms

```csharp
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.WinForms;

public class MarinaForm : Form
{
    private readonly MarinaViewControl _view = new() { Dock = DockStyle.Fill };

    public MarinaForm()
    {
        Controls.Add(_view);
        _view.RenderError += (_, e) => Text = "3D view unavailable: " + e.Exception.Message;   // the view shows the error itself

        IMarinaVisualizer marina = _view.Marina;           // the control owns a MarinaVisualizer
        marina.InitializeLayout(BuildLayout());
        marina.DefaultFocusAngle = CameraAngle.TopDown;   // optional

        marina.BerthSelected += (_, e) => e.Actions.Add("details", "Open berth card");
        marina.BerthActionInvoked += (_, e) => OpenBerthCard(e.BerthId);
    }
}
```

The control draws only while something changes or moves (waves, a moving camera, a pulse), in step with the display and at most every `FrameIntervalMilliseconds`; a still marina, or a minimized window, costs nothing. `Animate = false` stills the water, boats and traffic, and `ContinuousRendering = true` draws every frame regardless. It draws the tooltip/actions popup itself and forwards mouse, wheel and keyboard input to the visualizer. You can also pass a pre-configured visualizer: `new MarinaViewControl(marina)`, or swap it later through `Marina` (which raises `MarinaChanged`).

If OpenGL cannot start (no 3.3 driver, a virtual machine, a remote desktop), the control first retries without anti-aliasing (`Samples`), then shows a placeholder with the error in place of the marina and raises `RenderError`, rather than taking the form down. `RetryRendering()` starts again once the cause is gone.

Call the visualizer on the UI thread. If an update arrives on another thread anyway, the control marshals its own response to it (the redraw, the popup, the events it forwards) onto the UI thread, but the visualizer itself is not thread-safe: marshal with `BeginInvoke` first.

## Blazor WebAssembly

```razor
@using VirtualMarina.Blazor
@using VirtualMarina.Core.Api
@using VirtualMarina.Core.Camera

<div style="height: 80vh">
    <MarinaView Marina="_marina"
                AriaLabel="Marina map"
                OnRendererReady="d => _renderer = d"
                OnRendererError="m => _error = m" />
</div>

@code {
    private readonly MarinaVisualizer _marina = new() { DefaultFocusAngle = CameraAngle.TopDown };
    private string? _renderer, _error;

    protected override void OnInitialized()
    {
        _marina.BerthSelected += (_, e) => e.Actions.Add("details", "Open berth card");
        _marina.BerthActionInvoked += (_, e) => Nav.NavigateTo($"/berths/{e.BerthId}");
        _marina.InitializeLayout(BuildLayout());
    }
}
```

The component fills its parent element, so give the parent a height. The JavaScript module (`_content/VirtualMarina.Blazor/marinaWebGL.js`) loads automatically, and it links the popup's stylesheet (`_content/VirtualMarina.Blazor/marinaView.css`) at the top of the page's `<head>`; nothing needs to be added to `index.html` for the view. The stylesheet's rules sit in the `virtualmarina` cascade layer, so any rule of the page's for the `vm-popup` classes wins without needing a heavier selector.

- **Like the WinForms control, it draws on demand:** the frame loop sleeps while nothing changes and wakes on the next change. `ContinuousRendering` and `Animate` are parameters.
- **`OnRendererError`** is invoked when WebGL 2 cannot be created, and also when drawing fails several frames in a row: the view then stops and shows an error overlay rather than failing every frame.
- **Accessibility:** the canvas is focusable, has `role="application"` and an accessible name, which `AriaLabel` sets (a short localized description when null). The popup is announced (`role="status"` for a tooltip, `role="dialog"` for the actions window) and the error overlay is an alert.
- **`<MarinaDesignerPanel>`** uses Blazor scoped CSS, which the host page loads through its bundle: keep (or add) `<link rel="stylesheet" href="{YourAppAssembly}.styles.css" />` in `index.html`, as the Blazor templates do.

## A first marina

```csharp
using System.Numerics;
using VirtualMarina.Core.Domain;

static MarinaLayout BuildLayout() =>
    new MarinaLayoutBuilder("Harbor")
        .AddLandArea(new LandArea("quay", new OrientedRect(new Vector2(0, -12), new Vector2(160, 12), 0), 1.0f))
        .AddPier("A", "Pier A", start: new Vector2(0, -6), headingDegrees: 0, length: 60, pier => pier
            .AddBerths(PierSide.Left, count: 10, berthWidth: 5, berthLength: 12)
            .AddBerths(PierSide.Right, count: 10, berthWidth: 5, berthLength: 12, dividers: DividerType.Piles),
            width: 3, type: PierType.FloatingConcrete)
        .Build();
```

Berths are generated with ids `A-L01…A-L10` and `A-R01…A-R10`. Then set statuses from your ERP data:

```csharp
marina.BatchUpdate(erpBerths.Select(b => b.BoatOnBerth is { } boat
    ? BerthUpdate.Occupy(b.Number, new Boat(boat.Id, boat.Name, MapType(boat.Kind)) { LengthMeters = boat.Loa, BeamMeters = boat.Beam })
    : BerthUpdate.Free(b.Number)));
```

## Default mouse and keyboard controls

| Input | Action |
|---|---|
| Hover | Highlights the berth or boat under the cursor (exact shape, not its bounding box); the cursor becomes a hand/pointer |
| Left click | Select the berth and show its tooltip |
| Right click | Select the berth and open its actions window |
| Ctrl+click or Shift+click (Cmd+click in browsers) | Add/remove berths from the selection |
| Double-click | Focus the camera on the berth (at `DefaultFocusAngle`) |
| Left-drag / middle-drag | Pan: the ground under the pointer stays under it |
| Right-drag, Shift+left-drag | Orbit (Shift only changes a *drag*; a Shift+click without moving multi-selects) |
| Mouse wheel | Zoom toward what is under the cursor (a boat, a berth, raised land, or else the water) |
| Arrows / WASD | Pan (Shift: orbit). WASD is matched by key position, so it is the same square on AZERTY |
| PageUp / PageDown | Tilt |
| + / − | Zoom |
| Esc | Close the popup; press again to clear the selection |
| Home | Reset the camera to the overview |

**Which keys the view takes.** Only the keys above, pressed on their own or with Shift, plus Enter, Backspace and Delete for the [designer](12-designer.md). A chord with Ctrl, Alt or Cmd is left to the host application or the browser, even while the view has the focus: Ctrl+S, Alt+N or the browser's Ctrl+/Ctrl− zoom always reach their owner. The exceptions are Undo (Ctrl+Z, Cmd+Z) and Redo (Ctrl+Shift+Z, Ctrl+Y, Cmd+Shift+Z), taken as a fallback for the designer; a host with its own Undo accelerator sees them first (WinForms runs the form's `ProcessCmdKey` before the view, and the Blazor view calls `preventDefault`/`stopPropagation` only for keys it actually handled). Esc and Home count as handled only when they do something, so an Esc with no popup and no selection still reaches a dialog's Cancel button. Both views use the same table, `MarinaKeyMap`, and ask `marina.Input.WantsKey` before claiming a key.

Next: [Coordinates and conventions](02-coordinates-and-conventions.md).
