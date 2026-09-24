# Hosting and custom views

Both ready-made views draw **on demand**: a frame is drawn while something changes (a status, the hover, the camera, the
style) or moves by itself (waves, a pulsing selection, passing traffic), and not at all while the marina sits still or
the view is hidden. A still marina costs no GPU time. `ContinuousRendering` turns that off and draws every frame.

## WinForms: `MarinaViewControl`

Namespace `VirtualMarina.WinForms`, project `VirtualMarina.WinForms` (net8.0-windows, OpenTK.GLControl).

| Member | Meaning |
|---|---|
| `MarinaViewControl()` / `MarinaViewControl(MarinaVisualizer)` | Create with a new or an existing visualizer |
| `Marina` | The displayed visualizer; can be swapped at runtime |
| `MarinaChanged` | Raised after `Marina` was given a different visualizer |
| `Style` | The same object as `Marina.Style` (see [Appearance](08-appearance.md)) |
| `FrameIntervalMilliseconds` | Shortest delay between frames while something moves (default 15). Frames also wait for the display's vertical sync |
| `Animate` | Whether the waves, floating boats, pulses and traffic move (default true). When false the picture holds still and the view stops drawing until something changes; the camera and input still work |
| `ContinuousRendering` | Draw every frame even when nothing changes (default false) |
| `Samples` | Multisample anti-aliasing asked of the driver, 0–16 (default 4). Changing it restarts the OpenGL surface |
| `RendererDescription` | Backend and GPU after the first frame |
| `RenderError` | Raised when OpenGL 3.3 can't be initialized or rendering fails (see below) |
| `RetryRendering()` | Starts OpenGL again after a failure |
| `LoadReferenceImage(path, metersPerPixel)` | Loads an image file as the designer's reference image |
| `BerthClicked`, `BerthSelected`, `MultiBerthSelected`, `SelectionChanged`, `SelectionCleared`, `BerthActionInvoked`, `PopupChanged`, `BerthHoverChanged`, `BerthStatusChanged`, `LayoutChanged` | The `Marina` events, forwarded by the control so they appear in the Visual Studio designer (Properties → Events → **Marina**). The sender is the control; the event data is identical. They follow `Marina` when it's swapped. |

The control:
- **Rendering:** hosts an OpenGL 3.3 core surface, with `Samples`× MSAA. It draws only while `Marina.NeedsRedraw` or (with
  `Animate` on) `Marina.IsAnimating` is true, or `ContinuousRendering` is set, and not at all while it, or the form it is
  on, is hidden or minimized. With nothing to draw it drops to a slow idle check that notices a camera moved by code.
- **Input:** forwards mouse buttons (with Ctrl, Shift and Alt), the wheel, and the keys of the
  [keyboard contract](#the-keyboard-contract) to `Marina.Input`. The wheel is marked handled, so a scrollable form around
  the view does not scroll as well. The surface's mouse events are raised again as the control's own, in its client
  coordinates, for hosts that listen on the control (a status bar showing the pointer, say).
- **Popup:** draws the tooltip/actions popup as a custom-painted child control. It is shaped as a rounded card with a
  caret, follows its berth as the camera moves, and hides while the berth is off-screen. It works from the keyboard:
  Tab from the view reaches it, Up/Down (or Home/End) move between its actions and its close button, Enter or Space runs
  the one marked, and Esc closes it. Screen readers see it as a dialog whose lines are text and whose actions are menu items.
- **Reference image:** a reference image that came out of a marina file carries only its encoded bytes; the control
  decodes it (through `ReferenceImageLoader.Decoded`) before handing the frame to the OpenGL renderer, once per image.

```csharp
var view = new MarinaViewControl { Dock = DockStyle.Fill };
form.Controls.Add(view);
view.RenderError += (_, e) => log.Error(e.Exception);
view.BerthSelected += (_, e) => e.Actions.Add("checkin", "Check in");     // same as view.Marina.BerthSelected
view.Marina.InitializeLayout(layout);
```

- **In the Visual Studio designer:** drop the control from the Toolbox, then double-click an event in the Properties window under **Marina** to generate a handler.
- **At design time:** the control shows a placeholder instead of creating the OpenGL surface.

### When OpenGL is not available

The OpenGL context is created as the surface joins the control, which is where a machine without a usable driver (a
virtual machine, a remote desktop session) fails. The control catches that and tries again **without anti-aliasing**,
since multisampling is what such drivers most often refuse. If that fails too, or a frame throws later, the control:

1. stops drawing and releases the renderer;
2. shows a placeholder describing the render area, with the error;
3. raises `RenderError` with the exception. Nothing is rethrown: the form stays up whether or not there is a handler.

`RetryRendering()` makes a new surface and renderer, with the anti-aliasing fallback tried again — for example after
reconnecting from a remote desktop session.

### Threading

The visualizer is not thread-safe: call `Marina`'s members on the UI thread. If a status update does arrive on another
thread anyway, the control marshals what it does in response onto its own thread: the redraw, the popup, and the events
it forwards. Events whose handlers fill in the event data (`BerthSelected`, `MultiBerthSelected`) are marshalled
synchronously; the rest are posted, so a background thread never waits on the UI thread. The handlers you attach to
`Marina` itself still run on the thread that made the change.

### `ReferenceImageLoader`

Decodes image files into `ReferenceImage`s for the designer with GDI+ (PNG, JPEG, BMP, GIF, TIFF).

| Member | |
|---|---|
| `FromFile(path)`, `FromStream(stream)`, `FromBytes(bytes, contentType)` | Decode, keeping the file's bytes so a design can store the picture. The type is read from the bytes' signature, not the file name |
| `Decoded(image)` | The same picture with pixels, for one loaded from a marina file with only its encoded bytes |
| `FromImage(image, encodedData, contentType)` | From a GDI+ `Image` |
| `MaxDimension` | 8192: larger images are scaled down to stay within common GPU texture limits |
| `FileDialogFilter` | A localized filter for an `OpenFileDialog` |

A photo with an EXIF orientation is turned the right way up. A picture that had to be turned or scaled is no longer
the file it came from, so the design stores the turned or scaled copy (re-encoded as PNG, or JPEG when it was one)
rather than the original bytes.

## Blazor WebAssembly: `<MarinaView>`

Namespace `VirtualMarina.Blazor`, project `VirtualMarina.Blazor` (Razor class library).

| Parameter | Meaning |
|---|---|
| `Marina` (required) | The `MarinaVisualizer` to display |
| `MarinaStyle` | Assigned to `Marina.Style` when set; null keeps the visualizer's style. (Named so because `Style` is the element's inline CSS) |
| `CssClass`, `Style` | Added to the outer element (which fills its parent) |
| `ContinuousRendering` | Draw every display frame even when nothing changes (default false) |
| `Animate` | Whether the waves, floating boats, pulses and traffic move (default true) |
| `AriaLabel` | The canvas's accessible name; a short localized description when null |
| `OnRendererReady` | `EventCallback<string>` with the backend/GPU description |
| `OnRendererError` | `EventCallback<string>` when WebGL 2 isn't available, or drawing keeps failing and the view stops |

On the component instance: `RendererDescription`, and `LoadReferenceImageAsync(bytes, contentType, metersPerPixel)`,
which lets the browser decode a PNG, JPEG or WebP file as the designer's reference image (no .NET image library needed).

The component:
- **Requirements:** needs Blazor **WebAssembly** (synchronous JS interop). Server-side Blazor shows an error message instead.
- **Rendering:** renders with WebGL 2. The frame loop runs on `requestAnimationFrame` only while `NeedsRedraw`,
  `IsAnimating` (with `Animate` on) or `ContinuousRendering` says there is something to draw; otherwise it **sleeps**, and is
  woken at once by the visualizer's `RedrawRequested`, by input or by a resize, with a slow poll (every 250 ms) for changes
  the visualizer could not announce, such as a camera moved by code. It stops altogether while the canvas is scrolled out
  of sight or the tab is hidden.
- **Transport:** meshes, layer instances and the per-frame uniforms cross to JavaScript as `byte[]` (which Blazor hands over
  as a `Uint8Array`, without Base64). A mesh is sent once; a layer only when it changed, and then only the instances that
  changed (see [Rendering backends](#rendering-backends-iscenerenderer)).
- **Failures:** a frame that throws is retried. After three failing frames in a row the loop stops, the view shows the
  error over the canvas (`vm-marina-view__error`, `role="alert"`), and `OnRendererError` is raised. A lost WebGL context
  (GPU reset, driver update) is waited out and everything is uploaded again when the browser restores it.
- **Popup:** renders as HTML. Blazor re-renders its content on `PopupChanged`, and the script positions it as part of each
  frame. It is a `dialog` (actions) or a `status` region (tooltip) with an accessible name; Esc on one of its buttons closes
  it and gives the focus back to the view.
- **Styles:** the popup's look and the focus ring come from `_content/VirtualMarina.Blazor/marinaView.css`, which the script
  links into the page's `<head>` by itself the first time a view is created — there is nothing to add to `index.html`.
  Its rules sit in the `@layer virtualmarina` cascade layer, so **any** rule of the host's for the `vm-popup` classes wins,
  however plain its selector. A page with a strict Content-Security-Policy can reference the file itself with a
  `<link rel="stylesheet">`; the script then leaves it alone.
- **Accessibility:** the canvas is focusable (`tabindex="0"`), has `role="application"` and an `aria-label` (`AriaLabel`),
  and shows a focus ring when focused from the keyboard.
- **Input:** pointer events, with pointer capture while a button is held; `pointercancel` and `lostpointercapture` end a
  drag through `CancelPointer` rather than as a click. The wheel zooms and never scrolls the page. Keys follow the
  [keyboard contract](#the-keyboard-contract); Cmd counts as Ctrl on macOS.

## The keyboard contract

The host's own accelerators come first. A view takes only keys nobody else would want, and both ready-made views use the
same table, `MarinaKeyMap`, so they answer the same keys the same way:

| Keys | `MarinaKey` | Does |
|---|---|---|
| Arrows, W/A/S/D | `Left`, `Right`, `Up`, `Down` | Pan; with Shift, orbit and tilt |
| PageUp / PageDown | `PageUp`, `PageDown` | Tilt |
| + / = / numpad +, − / _ / numpad − | `ZoomIn`, `ZoomOut` | Zoom |
| Home | `Home` | Back to the overview |
| Esc | `Escape` | Close the popup, then clear the selection; in the designer, cancel the drawing, then return to navigation |
| Enter, Backspace, Delete | `Enter`, `Backspace`, `Delete` | Designer: finish the drawing; remove the last point; erase what is under the eraser, or the berths the area tool selected |
| Ctrl+Z (Cmd+Z) | `Undo` | Designer undo, or the last point while drawing — a fallback only |
| Ctrl+Shift+Z, Ctrl+Y (Cmd+Shift+Z) | `Redo` | Designer redo — a fallback only |

- **Chords go to the host.** A key with Ctrl, Alt or Cmd held maps to nothing, so Ctrl+S, Alt+N or the browser's Ctrl+/Ctrl−
  zoom always reach the application or the browser, even while the view has the focus. Undo and Redo are the only
  exceptions, and only as a fallback for a host with no shortcut of its own for them: a host that has one (an
  Edit ▸ Undo menu item, say) sees the key first. A chord with Alt in it is never taken, since Ctrl+Alt is AltGr on many layouts.
- **Movement letters go by position,** so W/A/S/D is the same square on AZERTY or Cyrillic; Undo and Redo follow the letter printed on the key.
- **Handled means acted on.** `MarinaInputController.KeyDown` returns true only when the key did something; Esc with nothing
  to dismiss and Home with the camera already home return false and reach the host (a dialog's Cancel button, say).
  `WantsKey(key, modifiers)` asks the same question before the key is pressed.

How the views honour it:

- **WinForms:** only the arrows, and Esc and Enter when `WantsKey` says the view has a use for them, are claimed in
  `PreviewKeyDown`, because a container would otherwise take them for focus movement or its Cancel/Accept buttons.
  Everything else reaches the view's `KeyDown` only after the form's `ProcessCmdKey` (menu shortcuts, `ToolStrip`
  accelerators) has passed on it. The control watches modifier keys going down and up while its window is active, so a
  preview that depends on Alt or Shift updates at once.
- **Blazor:** the key goes to .NET first; only when it was handled does the script call `preventDefault()` and
  `stopPropagation()`. Anything the view did not act on carries on to the page's own shortcuts and the browser.

A custom view does the same: map the native key with `MarinaKeyMap.FromVirtualKey(virtualKey, modifiers)` (Windows
virtual-key codes, as WinForms `Keys` holds them) or `MarinaKeyMap.FromDomKey(code, key, modifiers)` (a browser
`KeyboardEvent`), pass the result to `KeyDown`, and mark the event handled only when it returns true.

## The frame loop (custom views)

To host the visualizer in another UI framework (WPF, Avalonia, MAUI, a game engine...), drive it once per display frame and forward input:

```csharp
// once per display frame
marina.SetViewportSize(widthInPointerUnits, heightInPointerUnits);   // usually pixels
if (continuous || marina.NeedsRedraw || (animate && marina.IsAnimating))
{
    marina.Update(deltaSeconds, animate);                             // animation time + camera easing
    renderer.Resize(framebufferWidth, framebufferHeight);             // device pixels
    renderer.Render(marina.BuildRenderFrame());
}

// wake a loop that stopped asking for frames
marina.RedrawRequested += (_, _) => ScheduleFrame();                 // raised on the thread that made the change

// input (coordinates in the same units as SetViewportSize, origin top-left)
marina.Input.PointerDown(x, y, PointerButton.Left, InputModifiers.Control);
marina.Input.PointerMove(x, y, modifiers);
marina.Input.PointerUp(x, y, PointerButton.Left, modifiers);
marina.Input.DoubleClick(x, y, PointerButton.Left, modifiers);
marina.Input.Wheel(notches, x, y);                                    // positive zooms in, toward what is under the pointer
marina.Input.PointerLeave();
marina.Input.CancelPointer();                                         // capture lost, touch cancelled: end the drag, no click
marina.Input.ModifiersChanged(modifiers);                             // a modifier went down or up
if (MarinaKeyMap.FromVirtualKey(vk, modifiers) is { } key && marina.Input.KeyDown(key, modifiers))
    markHandled();
```

- `NeedsRedraw` is true when something changed since the last `BuildRenderFrame` — the scene, the camera or view size, the
  lighting, the water — or the camera is still easing. `IsAnimating` is true when the picture moves by itself: moving
  water, a selection marker or pulse, passing traffic. A view that draws only when either is true costs nothing at rest.
- `RedrawRequested` is raised once per change until the next `BuildRenderFrame`, for anything done through the visualizer
  or its style. A camera moved directly is only seen by `NeedsRedraw`, so a sleeping view should still poll it now and then.
- `RequestRedraw()` asks for a frame explicitly (sets `NeedsRedraw` and raises `RedrawRequested`).
- `Update(deltaSeconds, animate: false)` still eases the camera but leaves the waves, boats, pulses and traffic where they are.
  Long gaps are capped, so a view left alone does not jump.
- `BuildRenderFrame()` rebuilds only the layers of the scene that changed; `InvalidateScene()` forces all of them.
- `Time` is the accumulated animation time.
- `HitTest`, `TryGetPopupAnchor` and the rest are covered below. The visualizer is not thread-safe: call it on one thread.

## Rendering the popup

```csharp
marina.PopupChanged += (_, e) => popupView.Show(e.Current);   // null = hide

// every frame, after rendering
if (marina.ActivePopup is { } popup && marina.TryGetPopupAnchor(out Vector2 anchor))
    popupView.PlaceAbove(anchor);        // anchor = point above the primary berth, in view pixels
else
    popupView.Hide();
```

Render from `BerthPopup`:
- `Kind` (`Tooltip` / `Actions`)
- `Tooltip.Title`, `Subtitle`, `AccentColor`, `Lines`, `Footer`, `IsVisible`
- `Actions` (visible only): `Caption`, `Icon`, `Enabled`, `Style`, `BeginGroup`, `ShortcutText`, `Description`

When the user clicks an action, call `marina.InvokeBerthAction(action.ActionId)`. A close button should call `marina.ClosePopup()`.

## Designer panels

`MarinaDesignerPanel` (WinForms control, and a Blazor component of the same name) is a ready-made tool panel for `marina.Designer`: design mode, tools, undo, land / pier / berth settings and the reference image. See [Designer](12-designer.md).

- **WinForms:** set `View` to the `MarinaViewControl` (in the Windows Forms designer or in code). The panel then follows the
  view: when the view is given another marina (`MarinaChanged`), the panel drives that one. Setting `Marina` instead
  connects it to a visualizer without a view. `ImageLoadFailed` replaces the default message box when an image file
  cannot be read.
- **Blazor:** `<MarinaDesignerPanel Marina="..." View="..." />`. `View` is needed to decode image files in the browser;
  `MaxImageFileSize` (default 25 MB) and `CssClass` are optional. Its look is **CSS isolation** (`MarinaDesignerPanel.razor.css`),
  which reaches the page through the host's own bundle, so the host's `index.html` must link it:

  ```html
  <link rel="stylesheet" href="{YourApp}.styles.css" />
  ```

  (the Blazor template has this line already). Every rule in it weighs one class, so a host rule naming a `vm-designer` class wins.

A custom view needs nothing extra for the designer: forward input to `MarinaInputController` as usual (including
`MarinaKey.Enter`, `Backspace`, `Delete`, `Undo` and `Redo`). It only has to draw `RenderFrame.ReferenceImage` if it wants the reference image.

## Rendering backends: `ISceneRenderer`

```csharp
public interface ISceneRenderer : IDisposable
{
    string BackendName { get; }
    string? DeviceDescription => null;         // GPU and API version once initialized; optional
    void Initialize();                         // compile shaders; context current
    void Resize(int pixelWidth, int pixelHeight);
    void Render(RenderFrame frame);
}
```

All calls happen on the thread that owns the graphics context. Anything added to this interface later comes with a
default implementation, so an existing backend keeps compiling (see [Compatibility](16-compatibility.md)).

### What a frame holds

`RenderFrame` (built by `marina.BuildRenderFrame()`):

| Member | |
|---|---|
| `View`, `Projection`, `CameraPosition`, `Time` | Camera and animation time |
| `Lighting` | A `FrameLighting` snapshot: sun direction and color, ambient, specular, shininess, sky, fog color and density |
| `Water` | A `FrameWater` snapshot: deep and shallow colors, wave amplitude, frequency and speed, sky reflection, ripples, sun glints, boat motion |
| `Layers` | The scene, in `RenderLayer`s, in drawing order |
| `Objects` | The same scene as one flat list of `RenderObject`s |
| `SceneVersion` | Changes whenever anything in the scene changes |
| `WaterCenter`, `WaterDetailRadius`, `MarinaCenter` | Where the detailed water is, and the middle of the marina (for a custom backend; the built-in ones do not read it) |
| `Meshes`, `MeshLibraryVersion` | The mesh library and its version |
| `ReferenceImage` | The designer's reference image, or null |

`Lighting` and `Water` are copies taken when the frame was built, so changing the settings later does not change a frame
already built. `LightingSettings` and `WaterSettings` convert to them implicitly, so a frame put together by hand can
still say `Lighting = marina.Lighting`. A hand-made frame may give either `Layers` or `Objects`; the other is worked out
from it (a flat list becomes a single `RenderLayerKind.Scene` layer versioned by `SceneVersion`).

### Layers, batches and versions

The scene is split by how often each part changes, so that a change rebuilds, and a backend uploads, only the part it
touched: hovering a berth never re-sends the piers, and passing traffic never re-sends the berths.

| `RenderLayerKind` | Holds |
|---|---|
| `Structure` | Land, trees, piers, pedestals, dividers and fingers: what only a layout or style change moves |
| `Berths` | Boats, status pads, buoys and labels |
| `Highlight` | Selection markers |
| `Overlay` | The designer's drawing and measurements, and the traffic lanes while shown |
| `Traffic` | The passing vessels, which move every frame |
| `Scene` | A whole scene in one layer (a frame built from `Objects`) |

A `RenderLayer` has:

- `Instances` — its `RenderObject`s, grouped batch by batch; `Count`.
- `Batches` — `RenderBatch(MeshId, Pass, Start, Count)`: runs of instances sharing one mesh and one `RenderPass`, each
  drawn with a single instanced draw call. Opaque batches come first, then the transparent ones in the order
  they are to be drawn.
- `LayoutVersion` — changes when the instances were laid out afresh (their number, meshes or passes changed): upload everything.
- `Version` — changes whenever any instance changes. The same `LayoutVersion` with a new `Version` means some instances
  were rewritten in place, and `TryGetChangesSince(version, ranges)` lists which (`InstanceRange(Start, Count)`), so only
  those are sent. It returns false when that is no longer known — too much changed since — and the whole layer is sent.

Versions come from one counter for the whole process, so a layer from another visualizer never looks like one already
uploaded. The layer object is reused frame after frame and changes only while a frame is built: read it on the thread that builds frames.

`LayerUploadTracker` does the bookkeeping:

```csharp
foreach (var layer in frame.Layers)
{
    switch (tracker.Check(layer, ranges))
    {
        case LayerUpload.Full: UploadAll(layer); break;                        // instances and batches
        case LayerUpload.Changes: foreach (var r in ranges) UploadRange(layer, r); break;
    }
    tracker.Uploaded(layer);
}
foreach (var kind in tracker.RemoveMissing(frame.Layers)) DeleteLayer(kind);
// after losing the graphics context: tracker.Clear();
```

`RenderPass` of an instance: `Opaque` when its tint alpha is 1, otherwise `Transparent`.

### Instance data

For the instanced program, `InstanceData.Pack(objects, destination)` writes each `RenderObject` as `InstanceData.Stride`
(20) floats, read as five `vec4` attributes at locations 3 to 7 (`FirstAttributeLocation`, `AttributeCount`):

| Floats | Location | |
|---|---|---|
| 0–11 | 3–5 | The model matrix's three columns (M11 M12 M13, M21 M22 M23, M31 M32 M33), each followed by one component of the translation (M41, M42, M43) |
| 12–15 | 6 | Tint |
| 16–19 | 7 | Emissive, animation flags (as a float), phase, desaturation |

Transforms are affine, so the fourth column is always (0, 0, 0, 1) and is not stored.

### Backend responsibilities

1. **Meshes:** upload every mesh in `frame.Meshes` by id. When `frame.MeshLibraryVersion` changes, upload meshes that are
   new or replaced (a different `MeshData` instance under the same id) and free those no longer in the library. Vertex
   layout: `MeshData.VertexStride` (9) floats — position, normal, color — at attribute locations 0, 1 and 2; indices are
   `uint` triangles. The mesh with `IsWater` is drawn by the water pass.
2. **Opaque pass:** draw the `Opaque` batches with depth test and writes on.
3. **Water pass:** draw the water grid with the water shader.
4. **Reference image** (when `frame.ReferenceImage` is set): upload a texture once per `Image.Key` and draw
   `ShaderSources.ImageQuadCorners` with the image shaders (`uView`, `uProjection`, `uImageMin`, `uImageMax`,
   `uImageHeight`, `uOpacity`, `uImage` on unit 0). Blending is on and depth writes off; the depth test is off when
   `AboveScene` is true. Free the texture once no image is shown.
5. **Transparent pass:** draw the `Transparent` instances, blending on (`SRC_ALPHA, ONE_MINUS_SRC_ALPHA`) and depth writes off. This includes status pads,
   ghost boats and the designer's drawing previews. `TransparentSorter.Sort(layers, cameraPosition, sceneVersion)` puts
   them back to front and groups them into runs of one mesh (`Sorted`, `Runs`); it returns false when neither the camera
   nor the scene moved, so the sorted buffer need not be uploaded again.
6. **Uniforms:**
   - Per frame: `uView`, `uProjection`, `uCameraPos`, `uTime`, `uSunDirection`, `uSunColor`, `uAmbientColor`,
     `uSpecularStrength`, `uShininess`, `uSkyColor`, `uFogColor`, `uFogDensity`, `uWaveAmplitude`, `uWaveFrequency`,
     `uWaveSpeed`, `uFloatMotion` (from `Water.BoatMotion`); the water adds `uWaterDeep`, `uWaterShallow`,
     `uSkyReflection`, `uRipples`, `uSunGlints`, `uWaterCenter` and `uDetailRadius` (from `WaterDetailRadius`).
   - Per object, when drawing one object per call with `ModelVertex`: `uModel`, `uTint`, `uEmissive`, `uDesaturation`,
     `uAnimation` (int flags), `uPhase`. The instanced program reads these from the instance attributes instead.

A backend may instead draw `frame.Objects` one by one with `ShaderSources.ModelVertex` / `ModelFragment` — simpler, and
fine for a small scene — drawing the opaque ones, then the water, then the transparent ones (`RenderObject.IsTransparent`).
`SceneVersion` then tells it when the list changed.

Shader source comes from `ShaderSources.InstancedModelVertex/InstancedModelFragment`, `ModelVertex/ModelFragment`,
`WaterVertex/WaterFragment` and `ImageVertex/ImageFragment(ShaderDialect)`. It is the same GLSL body for OpenGL 3.3
(`DesktopGL33`) and WebGL 2 (`WebGL2`). Matrices use the System.Numerics row-vector convention; uploading M11..M44 in
order gives the column-major matrices GLSL expects. `RenderAnimation` flags (float on water, spin and bob, pulse, above
waves) are evaluated in the shaders, so an animated scene needs no per-frame uploads.

Existing backends, both drawing every mesh instanced, one draw call per batch:

- `OpenGlSceneRenderer` (OpenTK; the host makes the context current). Each layer has an instance buffer of its own,
  updated only as the versions say; transparent instances are sorted back to front with `TransparentSorter`. The reference
  image must carry decoded pixels (`ReferenceImage.Rgba`); encoded-only images are skipped, which is why
  `MarinaViewControl` decodes them first. Images larger than the GPU's texture limit are scaled down.
- `WebGlSceneRenderer` (created by `<MarinaView>`). The same layer and patch uploads, sent as bytes; transparent instances
  are sorted back to front with `TransparentSorter` too, in .NET, and sent to the script only when the camera or the scene
  changed their order. The browser decodes encoded reference images itself.

## Hit testing

```csharp
BerthHit? hit = marina.HitTest(x, y);       // nearest berth pad or boat under a pixel
if (hit is { } h) Console.WriteLine($"{h.BerthId} at {h.WorldPoint} (boat: {h.HitBoat}, {h.Distance:0.0} m)");
```

Hit testing runs on the CPU against berth footprints and the actual triangles of the boat models (hull, cabin, mast, sails), using the same placement code as rendering. A boat's bounding box is only a quick pre-check, so the empty space around a tall boat's mast never blocks clicks or hover on the boat or berth visible behind it. Hidden and filtered-out berths aren't hit. Disabled berths are hit but ignored by input.
