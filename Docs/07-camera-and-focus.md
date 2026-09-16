# Camera and focus

The camera is an orbit camera looking at a **target** point on the water, described by a `CameraPose`:

| Field | Meaning |
|---|---|
| `Target` | Point looked at (world space, normally Y = 0) |
| `YawDegrees` | Compass position of the camera **around** the target: 0° = camera on the +Z side looking toward −Z, 180° = on the −Z side looking toward +Z |
| `PitchDegrees` | Elevation above the horizon: 90° looks straight down (clamped to 8°–89°) |
| `Distance` | Meters from target to eye |

All camera moves are smoothed (eased) unless you pass `immediate: true`.

## Focusing on slips

```csharp
marina.FocusSlip("C-L04");                                   // DefaultFocusAngle
marina.FocusSlip("C-L04", CameraAngle.TopDown);              // plan view
marina.FocusSlips(new[] { "A-R01", "D-L01" }, CameraAngle.TopDown);
marina.FocusSlips(marina.GetSlipsByDock("B").Select(s => s.Id), new CameraAngle(YawDegrees: 200, PitchDegrees: 45));
marina.FocusSelection(CameraAngle.TopDown, immediate: true);
marina.SetSelection(ids, focusCamera: true, focusAngle: CameraAngle.TopDown);   // select + frame
```

How `FocusSlips` frames the slips:
1. **Target:** the middle of all listed slips.
2. **Distance:** the closest that fits every slip's water area **and** its boat's height, inside the view with `FocusMargin` free space on each side (default 0.12 = 12% per side).
3. **Aspect ratio:** it uses the view's actual aspect ratio, so wide, tall and square views all fit.
4. **Minimum distance:** the camera never gets closer than `MinFocusDistance` (default 25 m), so a single slip keeps some surroundings.
5. **Maximum distance:** the camera's `Constraints.MaxDistance` still applies. If a very thin view would need more, the camera stops at the limit, centered on the slips.
6. **Ids:** unknown ids are ignored, and it returns false when none exist. Disabled and hidden slips **are** included, because focusing only moves the camera.
7. **Resizing:** if the view is resized while the camera is still at the focus pose (for example, focus was called before the view had its size), the focus is re-fitted. Once the user moves the camera, resizing leaves it alone.

`ComputeFocusPose(slips, angle)` returns the pose without moving the camera.

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

This is used by `FocusSlip(id)`, `FocusSlips(ids)` or `FocusSelection()` without an angle, by `SelectSlip(id, focusCamera: true)`, by `SetSelection(..., focusCamera: true)` without an angle, and by **double-click**. When null (the default), focus keeps the current yaw and looks down at least 35°.

## Presets

| Built-in preset | View |
|---|---|
| `Overview` (`MarinaVisualizer.OverviewPresetName`) | Whole marina from the shore side |
| `Top Down` | Plan view, shore at the bottom |
| `Sea Side` | Looking back toward the shore |
| `East`, `West` | From the sides |
| `Low Angle` | Close to the water line |
| `Dock: {Name}` | Close-up of each dock |

```csharp
marina.ResetCamera();                                    // Overview
marina.ApplyCameraPreset("Top Down", immediate: true);   // case-insensitive
marina.FocusDock("A");                                   // same as "Dock: {name}"
marina.AddCameraPreset(new CameraPreset("Fuel dock", new CameraPose(new Vector3(40, 0, 10), 150, 35, 60), "Fuel station close-up"));
marina.RemoveCameraPreset("Fuel dock");
foreach (CameraPreset p in marina.CameraPresets) menu.Add(p.Name, p.Description);
```

Built-in presets are regenerated whenever the layout changes. Custom presets are kept.

## The orbit camera

`marina.Camera` (`OrbitCamera`) gives low-level control:

| Member | Meaning |
|---|---|
| `Pose` / `DesiredPose` | Pose on screen / pose being eased toward |
| `SetPose(pose, immediate)` | Move to a pose (constrained; yaw takes the short way round) |
| `Orbit(dYaw, dPitch)` | Rotate around the target |
| `Pan(dxPixels, dyPixels, viewportHeight)` | Map-style drag |
| `PanWorld(rightMeters, forwardMeters)` | Move the target along the ground |
| `Zoom(factor, focusPoint?)` | Factor > 1 moves closer, optionally toward a point |
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
| `TargetBoundsMin` / `TargetBoundsMax` | ±600 m | Set to the layout bounds plus 120 m when the layout changes |

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
| `KeyboardOrbitDegrees` | 10 |
| `HoverEnabled` | true |

Shift swaps pan and orbit while dragging, so touchpad users can do both. The default key map is in [Getting started](01-getting-started.md#default-mouse-and-keyboard-controls).
