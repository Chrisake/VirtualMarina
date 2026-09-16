# Hosting and custom views

## WinForms: `MarinaViewControl`

Namespace `VirtualMarina.WinForms`, project `VirtualMarina.WinForms` (net8.0-windows, OpenTK.GLControl).

| Member | Meaning |
|---|---|
| `MarinaViewControl()` / `MarinaViewControl(MarinaVisualizer)` | Create with a new or an existing visualizer |
| `Marina` | The displayed visualizer; can be swapped at runtime |
| `FrameIntervalMilliseconds` | Delay between frames (default 15) |
| `Animate` | Pause or resume rendering |
| `RendererDescription` | Backend and GPU after the first frame |
| `RenderError` | Raised when OpenGL 3.3 can't be initialized or rendering fails. The loop stops. Without a handler the exception is rethrown. |
| `SlipClicked`, `SlipSelected`, `MultiSlipSelected`, `SelectionChanged`, `SelectionCleared`, `SlipActionInvoked`, `PopupChanged`, `SlipHoverChanged`, `SlipStatusChanged`, `LayoutChanged` | The `Marina` events, forwarded by the control so they appear in the Visual Studio designer (Properties → Events → **Marina**). The sender is the control; the event data is identical. They follow `Marina` when it's swapped. |

The control:
- **Rendering:** hosts an OpenGL 3.3 core surface with 4× MSAA.
- **Input:** forwards mouse (including Ctrl, Shift and Alt) and keys (arrows, WASD, PageUp/PageDown, +/−, Home, Esc) to `Marina.Input`.
- **Popup:** draws the tooltip/actions popup as a custom-painted child control. It is shaped as a rounded card with a caret, repositioned every frame and hidden while the slip is off-screen.

```csharp
var view = new MarinaViewControl { Dock = DockStyle.Fill };
form.Controls.Add(view);
view.RenderError += (_, e) => log.Error(e.Exception);
view.SlipSelected += (_, e) => e.Actions.Add("checkin", "Check in");     // same as view.Marina.SlipSelected
view.Marina.InitializeLayout(layout);
```

- **In the Visual Studio designer:** drop the control from the Toolbox, then double-click an event in the Properties window under **Marina** to generate a handler.
- **At design time:** the control shows a placeholder instead of creating the OpenGL surface.

## Blazor WebAssembly: `<MarinaView>`

Namespace `VirtualMarina.Blazor`, project `VirtualMarina.Blazor` (Razor class library).

| Parameter | Meaning |
|---|---|
| `Marina` (required) | The `MarinaVisualizer` to display |
| `CssClass`, `Style` | Added to the outer element (which fills its parent) |
| `OnRendererReady` | `EventCallback<string>` with the backend/GPU description |
| `OnRendererError` | `EventCallback<string>` when WebGL 2 isn't available or fails |

`RendererDescription` is also available on the component instance.

The component:
- **Requirements:** needs Blazor **WebAssembly** (synchronous JS interop). Server-side Blazor shows an error message instead.
- **Rendering:** renders with WebGL 2 through `requestAnimationFrame`. Instance data is only re-sent when the scene changes.
- **Popup:** renders as HTML. Blazor re-renders its content on `PopupChanged`, and JavaScript repositions it every frame. The CSS is injected once (classes `vm-popup`, `vm-popup__action`, ...), so you can override the styles in your own stylesheet.
- **Input:** Cmd counts as Ctrl on macOS.

## The frame loop (custom views)

To host the visualizer in another UI framework (WPF, Avalonia, MAUI, a game engine...), drive it once per display frame and forward input:

```csharp
// every frame
marina.SetViewportSize(widthInPointerUnits, heightInPointerUnits);   // usually pixels
marina.Update(deltaSeconds);                                          // animation time + camera easing
renderer.Resize(framebufferWidth, framebufferHeight);                 // device pixels
renderer.Render(marina.BuildRenderFrame());

// input (coordinates in the same units as SetViewportSize, origin top-left)
marina.Input.PointerDown(x, y, PointerButton.Left, InputModifiers.Control);
marina.Input.PointerMove(x, y, modifiers);
marina.Input.PointerUp(x, y, PointerButton.Left, modifiers);
marina.Input.DoubleClick(x, y, PointerButton.Left, modifiers);
marina.Input.Wheel(notches, x, y);                                    // positive zooms in
marina.Input.PointerLeave();
marina.Input.KeyDown(MarinaKey.Escape, modifiers);                    // returns true when handled
```

- `BuildRenderFrame()` rebuilds the render object list only when the scene changed (`RenderFrame.SceneVersion`).
- `InvalidateScene()` forces a rebuild.
- `Time` is the accumulated animation time.

## Rendering the popup

```csharp
marina.PopupChanged += (_, e) => popupView.Show(e.Current);   // null = hide

// every frame, after rendering
if (marina.ActivePopup is { } popup && marina.TryGetPopupAnchor(out Vector2 anchor))
    popupView.PlaceAbove(anchor);        // anchor = point above the primary slip, in view pixels
else
    popupView.Hide();
```

Render from `SlipPopup`:
- `Kind` (`Tooltip` / `Actions`)
- `Tooltip.Title`, `Subtitle`, `AccentColor`, `Lines`, `Footer`, `IsVisible`
- `Actions` (visible only): `Caption`, `Icon`, `Enabled`, `Style`, `BeginGroup`, `ShortcutText`, `Description`

When the user clicks an action, call `marina.InvokeSlipAction(action.ActionId)`. A close button should call `marina.ClosePopup()`.

## Rendering backends: `ISceneRenderer`

```csharp
public interface ISceneRenderer : IDisposable
{
    string BackendName { get; }
    void Initialize();                         // compile shaders; context current
    void Resize(int pixelWidth, int pixelHeight);
    void Render(RenderFrame frame);
}
```

Backend responsibilities:

1. **Meshes:** upload every mesh in `frame.Meshes` by id, and re-upload when `frame.MeshLibraryVersion` changes. Vertex layout: 9 floats (position, normal, color); indices are `uint` triangles. The mesh with `IsWater` is drawn by the water pass.
2. **Opaque pass:** draw objects with `IsTransparent == false` using the model shader, with depth test and writes on.
3. **Water pass:** draw the water grid with the water shader.
4. **Transparent pass:** draw objects with `IsTransparent == true`, blending on (`SRC_ALPHA, ONE_MINUS_SRC_ALPHA`) and depth writes off.
5. **Uniforms:**
   - Per frame: `uView`, `uProjection`, `uCameraPos`, `uTime`, `uSunDirection`, `uSunColor`, `uAmbientColor`, `uSpecularStrength`, `uShininess`, `uSkyColor`, `uFogColor`, `uFogDensity`, `uWaveAmplitude`, `uWaveFrequency`, `uWaveSpeed`; for water also `uWaterDeep` and `uWaterShallow`.
   - Per object: `uModel`, `uTint`, `uEmissive`, `uDesaturation`, `uAnimation` (int flags), `uPhase`.

Shader source comes from `ShaderSources.ModelVertex/ModelFragment/WaterVertex/WaterFragment(ShaderDialect)`. It is the same GLSL body for OpenGL 3.3 (`DesktopGL33`) and WebGL 2 (`WebGL2`). Matrices use the System.Numerics row-vector convention; uploading M11..M44 in order gives the column-major matrices GLSL expects. `RenderAnimation` flags (float on water, spin and bob, pulse, above waves) are evaluated in the vertex and fragment shaders.

Existing backends: `OpenGlSceneRenderer` (OpenTK; the host makes the context current) and `WebGlSceneRenderer` (created by `<MarinaView>`).

## Hit testing

```csharp
SlipHit? hit = marina.HitTest(x, y);       // nearest slip pad or boat under a pixel
if (hit is { } h) Console.WriteLine($"{h.SlipId} at {h.WorldPoint} (boat: {h.HitBoat}, {h.Distance:0.0} m)");
```

Hit testing runs on the CPU against slip footprints and the actual triangles of the boat models (hull, cabin, mast, sails), using the same placement code as rendering. A boat's bounding box is only a quick pre-check, so the empty space around a tall boat's mast never blocks clicks or hover on the boat or slip visible behind it. Hidden and filtered-out slips aren't hit. Disabled slips are hit but ignored by input.
