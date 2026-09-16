# Getting started

## Requirements

- **.NET SDK:** .NET 8 SDK or newer (`global.json` rolls forward to later major versions).
- **Desktop:** a GPU and driver supporting OpenGL 3.3 core profile.
- **Browser:** a browser with WebGL 2. The Blazor component requires **Blazor WebAssembly**, because it uses synchronous JS interop.

```powershell
dotnet build VirtualMarina.sln
dotnet test tests/VirtualMarina.Core.Tests
dotnet run --project samples/VirtualMarina.TestHost.WinForms      # desktop harness
dotnet run --project samples/VirtualMarina.TestHost.Blazor        # then open http://localhost:5280
```

The WinForms harness accepts command-line options:

| Option | Effect |
|---|---|
| `--preset "Top Down"` | Apply a camera preset |
| `--select A-L03,A-L04` | Select slips (disabled ones are skipped) |
| `--focus A-L03,D-L01` | Frame slips top-down |
| `--labels NonOccupied` | Set the slip label mode |

## Referencing the libraries

| Application | Reference |
|---|---|
| WinForms | `VirtualMarina.WinForms` (brings `Core` and `Rendering.OpenGL`, plus the `OpenTK.GLControl` package) |
| Blazor WebAssembly | `VirtualMarina.Blazor` (brings `Core`) |
| Custom host or backend | `VirtualMarina.Core` |

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
        _view.RenderError += (_, e) => MessageBox.Show(e.Exception.Message, "OpenGL 3.3 is not available");

        IMarinaVisualizer marina = _view.Marina;           // the control owns a MarinaVisualizer
        marina.InitializeLayout(BuildLayout());
        marina.DefaultFocusAngle = CameraAngle.TopDown;   // optional

        marina.SlipSelected += (_, e) => e.Actions.Add("details", "Open berth card");
        marina.SlipActionInvoked += (_, e) => OpenBerthCard(e.SlipId);
    }
}
```

The control renders at about 60 FPS (`FrameIntervalMilliseconds`, `Animate`) and draws the tooltip/actions popup itself. It forwards mouse and keyboard input to the visualizer. You can also pass a pre-configured visualizer: `new MarinaViewControl(marina)`.

## Blazor WebAssembly

```razor
@using VirtualMarina.Blazor
@using VirtualMarina.Core.Api
@using VirtualMarina.Core.Camera

<div style="height: 80vh">
    <MarinaView Marina="_marina"
                OnRendererReady="d => _renderer = d"
                OnRendererError="m => _error = m" />
</div>

@code {
    private readonly MarinaVisualizer _marina = new() { DefaultFocusAngle = CameraAngle.TopDown };
    private string? _renderer, _error;

    protected override void OnInitialized()
    {
        _marina.SlipSelected += (_, e) => e.Actions.Add("details", "Open berth card");
        _marina.SlipActionInvoked += (_, e) => Nav.NavigateTo($"/berths/{e.SlipId}");
        _marina.InitializeLayout(BuildLayout());
    }
}
```

The component fills its parent element, so give the parent a height. The JavaScript module (`_content/VirtualMarina.Blazor/marinaWebGL.js`) and the popup CSS load automatically; nothing needs to be added to `index.html`.

## A first marina

```csharp
using System.Numerics;
using VirtualMarina.Core.Domain;

static MarinaLayout BuildLayout() =>
    new MarinaLayoutBuilder("Harbor")
        .AddLandArea(new LandArea("quay", new OrientedRect(new Vector2(0, -12), new Vector2(160, 12), 0), 1.0f))
        .AddDock("A", "Dock A", start: new Vector2(0, -6), headingDegrees: 0, length: 60, dock => dock
            .AddSlips(DockSide.Left, count: 10, slipWidth: 5, slipLength: 12)
            .AddSlips(DockSide.Right, count: 10, slipWidth: 5, slipLength: 12, dividers: DividerType.Piles),
            width: 3, type: DockType.FloatingConcrete)
        .Build();
```

Slips are generated with ids `A-L01…A-L10` and `A-R01…A-R10`. Then set statuses from your ERP data:

```csharp
marina.BatchUpdate(erpBerths.Select(b => b.BoatOnBerth is { } boat
    ? SlipUpdate.Occupy(b.Number, new Boat(boat.Id, boat.Name, MapType(boat.Kind)) { LengthMeters = boat.Loa, BeamMeters = boat.Beam })
    : SlipUpdate.Free(b.Number)));
```

## Default mouse and keyboard controls

| Input | Action |
|---|---|
| Left click | Select the slip and show its tooltip |
| Right click | Select the slip and open its actions window |
| Ctrl+click (Cmd+click in browsers) | Add/remove slips from the selection |
| Double-click | Focus the camera on the slip (at `DefaultFocusAngle`) |
| Left-drag / middle-drag | Pan |
| Right-drag, Shift+left-drag | Orbit |
| Mouse wheel | Zoom toward the cursor |
| Arrows / WASD | Pan (Shift: orbit) |
| PageUp / PageDown | Tilt |
| + / − | Zoom |
| Esc | Close the popup; press again to clear the selection |
| Home | Reset the camera to the overview |

Next: [Coordinates and conventions](02-coordinates-and-conventions.md).
