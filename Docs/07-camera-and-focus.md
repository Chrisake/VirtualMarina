# Camera and focus

The camera is an orbit camera looking at a **target** point on the water, described by a `CameraPose`:

| Field | Meaning |
|---|---|
| `Target` | Point looked at (world space, normally Y = 0) |
| `YawDegrees` | Compass position of the camera **around** the target: 0° = camera on the +Z side looking toward −Z, 180° = on the −Z side looking toward +Z |
| `PitchDegrees` | Elevation above the horizon: 90° looks straight down (clamped to 8°–89°) |
| `Distance` | Meters from target to eye |

All camera moves are smoothed (eased) unless you pass `immediate: true`.

## Focusing on berths

```csharp
marina.FocusBerth("C-L04");                                   // DefaultFocusAngle
marina.FocusBerth("C-L04", CameraAngle.TopDown);              // plan view
marina.FocusBerths(new[] { "A-R01", "D-L01" }, CameraAngle.TopDown);
marina.FocusBerths(marina.GetBerthsByPier("B").Select(s => s.Id), new CameraAngle(YawDegrees: 200, PitchDegrees: 45));
marina.FocusSelection(CameraAngle.TopDown, immediate: true);
marina.SetSelection(ids, focusCamera: true, focusAngle: CameraAngle.TopDown);   // select + frame
```

How `FocusBerths` frames the berths:
1. **Target:** the middle of all listed berths.
2. **Distance:** the closest that fits every berth's water area **and** its boat's height, inside the view with `FocusMargin` free space on each side (default 0.12 = 12% per side).
3. **Aspect ratio:** it uses the view's actual aspect ratio, so wide, tall and square views all fit.
4. **Minimum distance:** the camera never gets closer than `MinFocusDistance` (default 25 m), so a single berth keeps some surroundings.
5. **Maximum distance:** the camera's `Constraints.MaxDistance` still applies. If a very thin view would need more, the camera stops at the limit, centered on the berths.
6. **Ids:** unknown ids are ignored, and it returns false when none exist. Disabled and hidden berths **are** included, because focusing only moves the camera.
7. **Resizing:** if the view is resized while the camera is still at the focus pose (for example, focus was called before the view had its size), the focus is re-fitted. Once the user moves the camera, resizing leaves it alone.

`ComputeFocusPose(berths, angle)` returns the pose without moving the camera.

### `CameraAngle`

```csharp
public readonly record struct CameraAngle(float YawDegrees, float PitchDegrees)
```

| Member | Value |
|---|---|
| `CameraAngle.TopDown` | (180°, 89°): plan view with +Z (away from the shore in the usual layout) at the top |
| `CameraAngle.TopDownFacing(yaw)` | Plan view with a different orientation |
| `CameraAngle.Of(pose)` | The angle of an existing pose |
| `new CameraAngle(200, 42)` | Any angle (this one matches the Overview preset) |

### Default focus angle

```csharp
marina.DefaultFocusAngle = CameraAngle.TopDown;
```

This is used by `FocusBerth(id)`, `FocusBerths(ids)` or `FocusSelection()` without an angle, by `SelectBerth(id, focusCamera: true)`, by `SetSelection(..., focusCamera: true)` without an angle, and by **double-click**. When null (the default), focus keeps the current yaw and looks down at least 35°.

## Presets

| Built-in preset | Key | View |
|---|---|---|
| Overview | `Overview` (`MarinaVisualizer.OverviewPresetName`) | The whole marina, from yaw 200°, pitch 42° |
| Top Down | `Top Down` (`MarinaVisualizer.TopDownPresetName`) | Straight down, **north up** (yaw 0°: −Z at the top of the screen) |
| North, East, South, West | `North`, `East`, `South`, `West` | From each compass point (yaw 180°, 90°, 0°, 270°) |
| Pier: {Name} | `Pier:{id}` (`MarinaVisualizer.PierPresetKey(id)`) | Close-up of each pier, from its shore end |

Every whole-marina view is centred on the marina and pulled back far enough, from its own angle, to hold all of it,
so they stay right as the layout grows. The pier views stand off the pier's shore end and look down it.

> The **Top Down** preset and `CameraAngle.TopDown` are different views: the preset is north-up (yaw 0°), the angle has
> +Z at the top (yaw 180°). See [coordinate conventions](20-coordinate-conventions.md).

**Names and keys.** A preset's `Name` is what a person reads: it comes from the resource files, so it is translated
with the [current culture](15-localization.md), and a pier view is named after the pier ("Pier: A"; two piers with
the same name get "Pier: A (A2)" so both stay reachable). Its `Key` never changes with the language or a pier's
rename, so it is what a host should store. `ApplyCameraPreset`, `ApplyBuiltInCameraPreset` and
`SetCameraPresetEnabled` accept either, and try the key first. Saved views have no key (`Key` is null).

```csharp
marina.ResetCamera();                                    // Overview
marina.ApplyCameraPreset("Top Down", immediate: true);   // a key or a name, case-insensitive
marina.ApplyCameraPreset(MarinaVisualizer.PierPresetKey("A"));   // the pier's close-up, whatever its name or the language
marina.ShowPierCloseUp("A");                             // the same
marina.FocusPier("A", CameraAngle.TopDown);              // fits the whole pier and its berths from an angle instead
marina.AddCameraPreset(new CameraPreset("Fuel pier", new CameraPose(new Vector3(40, 0, 10), 150, 35, 60), "Fuel station close-up"));
marina.RemoveCameraPreset("Fuel pier");
foreach (CameraPreset p in marina.CameraPresets) menu.Add(p.Name, p.Description);
```

`FocusPier(string pierId, bool immediate)` — the old spelling of the close-up — is obsolete: use `ShowPierCloseUp`
for the close-up, or `FocusPier(pierId, angle)` to fit the whole pier.

**Worked out when asked for.** The automatic views depend on the layout and on the view's size, but they are only
worked out when `CameraPresets` is read or a preset is applied, not on every change. `CameraPresets` returns a
read-only snapshot that does not change after it is handed out; when the list goes out of date,
`CameraPresetsChanged` is raised — once, and not again until `CameraPresets` has been read, so a host that refills
its menu in the handler hears about every change and one that does not is not flooded during a window resize.
Inside `BeginUpdate` the event waits for the end of the scope.

```csharp
marina.CameraPresetsChanged += (_, _) => RefillViewMenu(marina.CameraPresets.Where(p => p.IsEnabled));
```

Built-in presets follow the layout; custom presets are kept.

### Saved views

`SaveCameraPreset(name)` stores where the camera is now. Saved views are written to the marina file, so the host
application gets them with the layout and can offer them as "go to this view".

A saved view may be named after an automatic one — a marina really can want its own "North". Both are kept, and both
appear in `CameraPresets`, told apart by `IsBuiltIn`. Hand the one you mean to `ApplyCameraPreset(preset)`; asked for
by name alone, the saved one wins, since someone chose it deliberately. Deleting a saved view leaves the automatic
one of that name untouched, and switching an automatic view off never touches a saved one.

### Switching a view off

Not every marina wants every automatic view offered. `SetCameraPresetEnabled(name, false)` marks one as not to be
offered; it stays in `CameraPresets` with `IsEnabled` false and can still be applied by name on purpose.

```csharp
marina.SetCameraPresetEnabled("South", false);
foreach (var p in marina.CameraPresets.Where(p => p.IsEnabled)) menu.Add(p.Name, p.Description);
```

Which built-in views are switched off is saved with the design, by key, and survives the layout changing under them
(and the language changing). Files written before views had keys named them; those names are matched to the views'
keys when the design is loaded.

## The orbit camera

`marina.Camera` (`OrbitCamera`) gives low-level control:

| Member | Meaning |
|---|---|
| `Pose` / `DesiredPose` | Pose on screen / pose being eased toward |
| `SetPose(pose, immediate)` | Move to a pose (constrained; yaw takes the short way round) |
| `Orbit(dYaw, dPitch)` | Rotate around the target |
| `Pan(fromPixel, toPixel, viewportWidth, viewportHeight)` | Grab-pan: the ground that was under `fromPixel` ends up under `toPixel`, anywhere in an oblique view (what dragging uses) |
| `Pan(dxPixels, dyPixels, viewportHeight)` | Drag by a screen delta, exact only at the middle of the view |
| `PanWorld(rightMeters, forwardMeters)` | Move the target along the ground |
| `Zoom(factor, focusPoint?)` | Factor > 1 moves closer, optionally toward a point |
| `Update(deltaSeconds)` | Eases toward the desired pose (called by the visualizer every frame) |
| `Smoothing` | Easing rate (default 10/s; 0 disables smoothing) |
| `FieldOfViewDegrees`, `NearPlane`, `FarPlane` | Projection |
| `Position`, `IsMoving` | Eye position; still easing |
| `ScreenPointToRay(x, y, w, h)`, `WorldToScreen(world, w, h)` | Picking helpers (pixels, origin top-left) |
| `GetViewMatrix()`, `GetProjectionMatrix(aspect)` | Matrices |
| `Constrain(pose)` | Apply the constraints to a pose |

`marina.TryProjectToScreen(world, out screen)` and `marina.GetWaterPoint(x, y)` do the same with the visualizer's viewport size.

### Constraints

`marina.Camera.Constraints` (`CameraConstraints`):

| Property | Default | Notes |
|---|---|---|
| `MinPitchDegrees` | 8 | Keeps the view above the horizon |
| `MaxPitchDegrees` | 89 | Never flips over the top |
| `MinDistance` | 6 m | |
| `MaxDistance` | 650 m | Raised automatically to fit the layout |
| `MinEyeHeight` | 1.5 m | Keeps the eye above the water |
| `TargetBoundsMin` / `TargetBoundsMax` | ±600 m | Set to the layout bounds (and the designer's reference image) plus 120 m when they change |
| `MinTargetHeight` / `MaxTargetHeight` | 0 / 50 m | How high the target may sit, e.g. when zooming toward raised land or a mast |

The limits are tolerant: `Constrain` puts a pair given the wrong way round (minimum above maximum) the right way
round, ignores a limit that is not a number, and never lets the distance reach zero, so no setting can make the camera
throw or produce a blank frame. A pose with a non-finite target, yaw, pitch or distance is repaired the same way.

## Mouse and keyboard bindings

`marina.Input` (`MarinaInputController`) turns platform-neutral input into camera moves, hover and clicks. Views forward native events to it. Settings:

| Property | Default |
|---|---|
| `LeftDragAction` | `Pan` |
| `RightDragAction` | `Orbit` (a right-click without dragging opens the actions window) |
| `MiddleDragAction` | `Pan` |
| `OrbitDegreesPerPixel` | 0.3 |
| `ZoomStepFactor` | 1.15 per wheel notch |
| `ClickTolerancePixels` | 5 (movement below this counts as a click) |
| `KeyboardPanMeters` | 10 |
| `KeyboardPanScalesWithDistance` | true |
| `KeyboardOrbitDegrees` | 10 |
| `HoverEnabled` | true |

- **Dragging grab-pans:** the spot of ground under the pointer stays under it, wherever it is in an oblique view.
  Shift swaps pan and orbit while dragging, so touchpad users can do both.
- **The wheel zooms toward what is under the pointer** — a boat, a berth, raised land, or else the water — so the
  thing pointed at stays put.
- **Arrow keys scale with the zoom:** with `KeyboardPanScalesWithDistance` on, a press pans `KeyboardPanMeters` when
  the camera is `MarinaInputController.KeyboardPanReferenceDistance` (100 m) from its target, and proportionally more
  or less at other distances, so a press moves the picture by about the same share of the view zoomed in on a berth
  as zoomed out over the whole marina. Turn it off for a fixed step.
- **`CancelPointer()`** forgets the press in progress without treating it as a click. A host calls it when it loses
  the pointer without seeing it released (mouse capture lost, a touch cancelled, another window taking the focus);
  the built-in views do. A new press while one is still recorded also ends the old one without a click, so a lost
  release never leaves the view stuck mid-drag.
- **`ModifiersChanged(modifiers)`** tells the view when a modifier key goes down or up, so a preview that depends on
  one (the designer's eraser taking a whole row while Alt is held) updates without waiting for the pointer to move.

The default key map is in [Getting started](01-getting-started.md#default-mouse-and-keyboard-controls).

### Which keys the view takes

`MarinaKeyMap` is the one table both built-in views use to turn a native key into a `MarinaKey`
(`FromVirtualKey` for a WinForms `Keys` code, `FromDomKey` for a browser `KeyboardEvent`'s `code` and `key`). The
contract is that **the host's own shortcuts come first**:

- The view only takes keys nobody else would want: arrows, W/A/S/D, `+`/`=`/`−`, PageUp/PageDown, Home, Escape,
  Enter, Backspace and Delete — pressed alone, or with Shift (Shift+arrows orbits instead of panning).
- A chord with **Ctrl, Alt or Cmd maps to nothing**, so Ctrl+S, Alt+N or the browser's Ctrl+/Ctrl− zoom reach the
  application or the browser even while the view has the focus (`MarinaKeyMap.IsChord`).
- The exceptions are **Undo** (Ctrl+Z, Cmd+Z) and **Redo** (Ctrl+Shift+Z, Ctrl+Y, Cmd+Shift+Z), which the designer
  uses. They are a fallback for a host with no Undo/Redo shortcut of its own: a host that has one (an Edit menu
  item) sees the key first and the view never gets it. A chord with Alt in it is never taken, since Ctrl+Alt is AltGr
  on many layouts.
- Movement letters go by where the key sits (W/A/S/D is the same square on AZERTY or a Cyrillic layout); Undo and
  Redo follow the letter printed on the key, like every application's Ctrl+Z.

`KeyDown(key, modifiers)` returns true only when the key did something, so the host can mark it handled; **Escape
and Home only count when they changed something** (Escape with no popup, selection or drawing to dismiss, or Home
with the camera already at the overview, still reaches the host — a dialog's Cancel button, say).
`WantsKey(key, modifiers)` answers the same question before the key is pressed through, for a host that has to decide
whether to claim a key its platform would give to dialog navigation. Both built-in views follow this: the WinForms
control lets the form's accelerators (`ProcessCmdKey`, menu shortcuts) run first, and the Blazor component calls
`preventDefault` and `stopPropagation` only for keys the view handled. A custom view should do the same (see
[the keyboard contract](10-hosting-and-custom-views.md#the-keyboard-contract)).
