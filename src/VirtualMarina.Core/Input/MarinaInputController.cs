using System.Numerics;
using VirtualMarina.Core.Api;

namespace VirtualMarina.Core.Input;

/// <summary>
/// Turns raw, platform-neutral pointer and keyboard input into camera moves, hover, clicks and selection.
/// Every host view (WinForms, Blazor, ...) just forwards its native events here.
/// </summary>
/// <remarks>
/// Defaults: left-drag pans (map-style), right-drag orbits, middle-drag pans, Shift+left-drag orbits,
/// wheel zooms toward the cursor, click selects and shows the tooltip, Ctrl+click or Shift+click adds/removes berths,
/// right-click opens the actions window, double-click focuses a berth, Escape closes the popup and then
/// clears the selection, Home resets the view. The popup stays anchored above its berth while the camera moves.
/// Keys pressed with Ctrl or Alt are left to the host's own shortcuts, all but Undo and Redo (see <see cref="MarinaKeyMap"/>).
/// While <see cref="Design.MarinaDesigner.IsActive"/> is true, clicks, double-clicks, pointer movement and Enter/Backspace/Delete/Escape/Undo/Redo
/// go to the designer instead (camera dragging, the wheel and navigation keys work as usual).
/// </remarks>
public sealed class MarinaInputController
{
    private readonly MarinaVisualizer _marina;
    private PointerButton _activeButton;
    private InputModifiers _activeModifiers;
    private Vector2 _downPosition;
    private Vector2 _lastPosition;
    private bool _dragging;
    private bool _designerDrag;

    internal MarinaInputController(MarinaVisualizer marina)
    {
        _marina = marina;
    }

    /// <summary>What left-dragging does (default <see cref="CameraDragAction.Pan"/>; Shift swaps pan and orbit).</summary>
    public CameraDragAction LeftDragAction { get; set; } = CameraDragAction.Pan;

    /// <summary>What right-dragging does (default <see cref="CameraDragAction.Orbit"/>). A right-click without dragging opens the actions window.</summary>
    public CameraDragAction RightDragAction { get; set; } = CameraDragAction.Orbit;

    /// <summary>What middle-dragging does (default <see cref="CameraDragAction.Pan"/>).</summary>
    public CameraDragAction MiddleDragAction { get; set; } = CameraDragAction.Pan;

    /// <summary>Orbit speed for drags, in degrees per pixel (default 0.3).</summary>
    public float OrbitDegreesPerPixel { get; set; } = 0.3f;

    /// <summary>Zoom factor applied per wheel notch.</summary>
    public float ZoomStepFactor { get; set; } = 1.15f;

    /// <summary>Movement (pixels) below which a press/release counts as a click rather than a drag.</summary>
    public float ClickTolerancePixels { get; set; } = 5f;

    /// <summary>
    /// Distance an arrow/WASD key press pans, in meters (default 10). With <see cref="KeyboardPanScalesWithDistance"/>
    /// on, this is the step when the camera is <see cref="KeyboardPanReferenceDistance"/> from its target, and the step
    /// grows and shrinks with the zoom.
    /// </summary>
    public float KeyboardPanMeters { get; set; } = 10f;

    /// <summary>
    /// Scale the arrow-key step with how far the camera is from its target (default true), so a press moves the
    /// picture by about the same share of the view zoomed in on one berth as zoomed out over the whole marina.
    /// </summary>
    public bool KeyboardPanScalesWithDistance { get; set; } = true;

    /// <summary>Camera distance at which an arrow key pans exactly <see cref="KeyboardPanMeters"/>.</summary>
    public const float KeyboardPanReferenceDistance = 100f;

    /// <summary>Angle a Shift+arrow or PageUp/PageDown press orbits or tilts, in degrees (default 10).</summary>
    public float KeyboardOrbitDegrees { get; set; } = 10f;

    /// <summary>Hit-test on pointer move to highlight the berth under the cursor.</summary>
    public bool HoverEnabled { get; set; } = true;

    /// <summary>True while a button is held and the pointer has moved past <see cref="ClickTolerancePixels"/>.</summary>
    public bool IsDragging => _dragging;

    /// <summary>Forward a mouse/pointer press. Coordinates are view pixels, origin top-left.</summary>
    /// <param name="x">Pointer X.</param>
    /// <param name="y">Pointer Y.</param>
    /// <param name="button">Pressed button.</param>
    /// <param name="modifiers">Modifier keys held.</param>
    /// <remarks>
    /// A press while another is still recorded — its release was lost to another window, a dialog, a touch
    /// cancellation — ends that one without a click and starts afresh, so a missed release never leaves the view stuck.
    /// </remarks>
    public void PointerDown(float x, float y, PointerButton button, InputModifiers modifiers = InputModifiers.None)
    {
        if (button == PointerButton.None) return;
        if (_activeButton != PointerButton.None) CancelPointer();
        _activeButton = button;
        _activeModifiers = modifiers;
        _downPosition = _lastPosition = new Vector2(x, y);
        _dragging = false;
        _designerDrag = _marina.Designer.CapturesDrag(button);
        if (_designerDrag) _marina.Designer.BeginImageDrag(x, y);
    }

    /// <summary>Forward pointer movement: hovers when no button is held, otherwise pans or orbits.</summary>
    /// <param name="x">Pointer X in view pixels.</param>
    /// <param name="y">Pointer Y in view pixels.</param>
    /// <param name="modifiers">Modifier keys held.</param>
    public void PointerMove(float x, float y, InputModifiers modifiers = InputModifiers.None)
    {
        var position = new Vector2(x, y);
        if (_activeButton == PointerButton.None)
        {
            if (_marina.Designer.IsActive) _marina.Designer.HandlePointerMove(x, y, modifiers);
            else if (HoverEnabled) _marina.HandlePointerHover(x, y);
            return;
        }

        var delta = position - _lastPosition;
        _lastPosition = position;

        if (!_dragging && Vector2.Distance(position, _downPosition) > ClickTolerancePixels)
        {
            _dragging = true;
            delta = position - _downPosition; // Don't lose the movement inside the tolerance radius.
        }

        if (!_dragging) return;

        if (_designerDrag)
        {
            _marina.Designer.DragImage(x, y);
            return;
        }

        switch (ResolveDragAction(_activeButton, _activeModifiers))
        {
            case CameraDragAction.Pan:
                // The ground that was under the pointer stays under it, wherever on an oblique view it is.
                _marina.Camera.Pan(position - delta, position, _marina.ViewportSize.X, _marina.ViewportSize.Y);
                break;
            case CameraDragAction.Orbit:
                _marina.Camera.Orbit(-delta.X * OrbitDegreesPerPixel, delta.Y * OrbitDegreesPerPixel);
                break;
        }
    }

    /// <summary>Forward a button release. Without a drag this is a click: selection, tooltip or actions window.</summary>
    /// <param name="x">Pointer X in view pixels.</param>
    /// <param name="y">Pointer Y in view pixels.</param>
    /// <param name="button">Released button.</param>
    /// <param name="modifiers">Modifier keys held (Ctrl adds to the selection).</param>
    public void PointerUp(float x, float y, PointerButton button, InputModifiers modifiers = InputModifiers.None)
    {
        if (button != _activeButton) return;
        var wasDragging = _dragging;
        var pressModifiers = _activeModifiers;
        _activeButton = PointerButton.None;
        _dragging = false;
        if (_designerDrag)
        {
            _designerDrag = false;
            _marina.Designer.EndImageDrag();
        }

        if (wasDragging) return;

        // Modifiers held at press or release both count, so Ctrl released a moment early still multi-selects.
        if (_marina.Designer.IsActive) _marina.Designer.HandleClick(x, y, button, modifiers | pressModifiers);
        else _marina.HandleClick(x, y, button, isDoubleClick: false, modifiers | pressModifiers);
    }

    /// <summary>Forward a double-click. A left double-click on a berth focuses the camera on it (at <c>DefaultFocusAngle</c>).</summary>
    /// <param name="x">Pointer X in view pixels.</param>
    /// <param name="y">Pointer Y in view pixels.</param>
    /// <param name="button">Button.</param>
    /// <param name="modifiers">Modifier keys held.</param>
    public void DoubleClick(float x, float y, PointerButton button, InputModifiers modifiers = InputModifiers.None)
    {
        if (_marina.Designer.IsActive) _marina.Designer.HandleDoubleClick(x, y, button, modifiers);
        else _marina.HandleClick(x, y, button, isDoubleClick: true, modifiers);
    }

    /// <summary>
    /// Wheel input in notches: positive zooms in, toward what is under the pointer — a boat, a berth, raised land,
    /// or else the water.
    /// </summary>
    public void Wheel(float notches, float x, float y)
    {
        if (notches == 0f || !float.IsFinite(notches)) return;
        var focus = _marina.GetGroundPoint(x, y);
        _marina.Camera.Zoom(MathF.Pow(ZoomStepFactor, Math.Clamp(notches, -10f, 10f)), focus);
    }

    /// <summary>Forward the pointer leaving the view: ends any drag and clears the hover.</summary>
    public void PointerLeave()
    {
        CancelPointer();
        _marina.HandlePointerLeave();
        _marina.Designer.HandlePointerLeave();
    }

    /// <summary>
    /// Forgets the press in progress without treating it as a click: ends a drag where it is. For a host that loses
    /// the pointer without seeing it released (capture lost, a touch cancelled, a window taking the focus).
    /// </summary>
    public void CancelPointer()
    {
        _activeButton = PointerButton.None;
        _dragging = false;
        if (_designerDrag)
        {
            _designerDrag = false;
            _marina.Designer.EndImageDrag();
        }
    }

    /// <summary>
    /// Tells the view which modifier keys are held now, for a host that can see them go down and up. Returns true
    /// when the scene was redrawn because of it.
    /// </summary>
    /// <remarks>
    /// Modifiers otherwise only arrive with a pointer event, so a preview that depends on one — the eraser taking a
    /// whole row while Alt is held, say — would not catch up until the pointer moved again. Call this whenever a
    /// modifier key goes down or up.
    /// </remarks>
    /// <param name="modifiers">The modifier keys now held.</param>
    public bool ModifiersChanged(InputModifiers modifiers) => _marina.Designer.SetModifiers(modifiers);

    /// <summary>
    /// Forward a key press. Returns true when the key did something, so the host can mark it handled; false leaves
    /// it to the host (a dialog's Cancel or Accept button, the browser, the page).
    /// </summary>
    /// <remarks>
    /// Keys pressed with Ctrl or Alt are never handled except <see cref="MarinaKey.Undo"/> and <see cref="MarinaKey.Redo"/>: those chords belong to the
    /// host's accelerators (see <see cref="MarinaKeyMap"/>). Escape and Home only count as handled when they changed
    /// something, so an Escape with nothing to dismiss still reaches the host.
    /// </remarks>
    /// <param name="key">The key, as mapped by <see cref="MarinaKeyMap"/>.</param>
    /// <param name="modifiers">Modifier keys held (Shift orbits instead of panning).</param>
    public bool KeyDown(MarinaKey key, InputModifiers modifiers = InputModifiers.None)
    {
        if (key is not (MarinaKey.Undo or MarinaKey.Redo) && MarinaKeyMap.IsChord(modifiers)) return false;

        if (_marina.Designer.IsActive && key is MarinaKey.Enter or MarinaKey.Backspace or MarinaKey.Delete or MarinaKey.Escape or MarinaKey.Undo or MarinaKey.Redo &&
            _marina.Designer.HandleKey(key))
        {
            return true;
        }

        var camera = _marina.Camera;
        var orbit = (modifiers & InputModifiers.Shift) != 0;
        var step = KeyboardPanScalesWithDistance
            ? KeyboardPanMeters * camera.DesiredPose.Distance / KeyboardPanReferenceDistance
            : KeyboardPanMeters;
        switch (key)
        {
            case MarinaKey.Left when orbit: camera.Orbit(KeyboardOrbitDegrees, 0f); break;
            case MarinaKey.Right when orbit: camera.Orbit(-KeyboardOrbitDegrees, 0f); break;
            case MarinaKey.Up when orbit: camera.Orbit(0f, KeyboardOrbitDegrees); break;
            case MarinaKey.Down when orbit: camera.Orbit(0f, -KeyboardOrbitDegrees); break;
            case MarinaKey.Left: camera.PanWorld(-step, 0f); break;
            case MarinaKey.Right: camera.PanWorld(step, 0f); break;
            case MarinaKey.Up: camera.PanWorld(0f, step); break;
            case MarinaKey.Down: camera.PanWorld(0f, -step); break;
            case MarinaKey.PageUp: camera.Orbit(0f, KeyboardOrbitDegrees); break;
            case MarinaKey.PageDown: camera.Orbit(0f, -KeyboardOrbitDegrees); break;
            case MarinaKey.ZoomIn: camera.Zoom(ZoomStepFactor); break;
            case MarinaKey.ZoomOut: camera.Zoom(1f / ZoomStepFactor); break;
            case MarinaKey.Home:
                if (!HomeMovesCamera()) return false;
                _marina.ResetCamera();
                break;

            case MarinaKey.Escape:
                if (!HasSomethingToDismiss()) return false;
                _marina.HandleEscape();
                break;
            default: return false;
        }

        return true;
    }

    /// <summary>
    /// True when <see cref="KeyDown"/> would act on the key right now. A host asks before claiming a key its platform
    /// would otherwise give to dialog navigation — Escape to a Cancel button, Enter to an Accept button — so those
    /// still work whenever the view has nothing to do with the key.
    /// </summary>
    /// <param name="key">The key, as mapped by <see cref="MarinaKeyMap"/>.</param>
    /// <param name="modifiers">Modifier keys held.</param>
    public bool WantsKey(MarinaKey key, InputModifiers modifiers = InputModifiers.None)
    {
        if (key is not (MarinaKey.Undo or MarinaKey.Redo) && MarinaKeyMap.IsChord(modifiers)) return false;

        var designer = _marina.Designer;
        return key switch
        {
            MarinaKey.Escape => designer.WantsKey(key) || HasSomethingToDismiss(),
            MarinaKey.Enter or MarinaKey.Backspace or MarinaKey.Delete or MarinaKey.Undo or MarinaKey.Redo => designer.WantsKey(key),
            MarinaKey.Left or MarinaKey.Right or MarinaKey.Up or MarinaKey.Down or MarinaKey.PageUp or MarinaKey.PageDown
                or MarinaKey.ZoomIn or MarinaKey.ZoomOut => true,
            MarinaKey.Home => HomeMovesCamera(),
            _ => false,
        };
    }

    /// <summary>True when Home would move the camera: it is not already at, or heading for, the overview.</summary>
    private bool HomeMovesCamera() => _marina.Camera.WouldMoveTo(_marina.ResetPose());

    /// <summary>What Escape outside the designer works through: the popup, then the selection.</summary>
    private bool HasSomethingToDismiss() => _marina.ActivePopup is not null || _marina.SelectedBerth is not null;

    private CameraDragAction ResolveDragAction(PointerButton button, InputModifiers modifiers)
    {
        var action = button switch
        {
            PointerButton.Left => LeftDragAction,
            PointerButton.Right => RightDragAction,
            PointerButton.Middle => MiddleDragAction,
            _ => CameraDragAction.None,
        };

        // Shift swaps pan and orbit so single-button (touchpad) users can do both.
        if ((modifiers & InputModifiers.Shift) != 0)
        {
            action = action switch
            {
                CameraDragAction.Pan => CameraDragAction.Orbit,
                CameraDragAction.Orbit => CameraDragAction.Pan,
                _ => action,
            };
        }

        return action;
    }
}
