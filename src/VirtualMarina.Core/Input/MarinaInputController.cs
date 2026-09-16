using System.Numerics;
using VirtualMarina.Core.Api;

namespace VirtualMarina.Core.Input;

/// <summary>
/// Turns raw, platform-neutral pointer and keyboard input into camera moves, hover, clicks and selection.
/// Every host view (WinForms, Blazor, ...) just forwards its native events here.
/// </summary>
/// <remarks>
/// Defaults: left-drag pans (map-style), right-drag orbits, middle-drag pans, Shift+left-drag orbits,
/// wheel zooms toward the cursor, click selects and shows the tooltip, Ctrl+click or Shift+click adds/removes slips,
/// right-click opens the actions window, double-click focuses a slip, Escape closes the popup and then
/// clears the selection, Home resets the view. The popup stays anchored above its slip while the camera moves.
/// </remarks>
public sealed class MarinaInputController
{
    private readonly MarinaVisualizer _marina;
    private PointerButton _activeButton;
    private InputModifiers _activeModifiers;
    private Vector2 _downPosition;
    private Vector2 _lastPosition;
    private bool _dragging;

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

    /// <summary>Distance an arrow/WASD key press pans, in meters (default 10).</summary>
    public float KeyboardPanMeters { get; set; } = 10f;

    /// <summary>Angle a Shift+arrow or PageUp/PageDown press orbits or tilts, in degrees (default 10).</summary>
    public float KeyboardOrbitDegrees { get; set; } = 10f;

    /// <summary>Hit-test on pointer move to highlight the slip under the cursor.</summary>
    public bool HoverEnabled { get; set; } = true;

    /// <summary>True while a button is held and the pointer has moved past <see cref="ClickTolerancePixels"/>.</summary>
    public bool IsDragging => _dragging;

    /// <summary>Forward a mouse/pointer press. Coordinates are view pixels, origin top-left.</summary>
    /// <param name="x">Pointer X.</param>
    /// <param name="y">Pointer Y.</param>
    /// <param name="button">Pressed button.</param>
    /// <param name="modifiers">Modifier keys held.</param>
    public void PointerDown(float x, float y, PointerButton button, InputModifiers modifiers = InputModifiers.None)
    {
        if (button == PointerButton.None || _activeButton != PointerButton.None) return;
        _activeButton = button;
        _activeModifiers = modifiers;
        _downPosition = _lastPosition = new Vector2(x, y);
        _dragging = false;
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
            if (HoverEnabled) _marina.HandlePointerHover(x, y);
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

        switch (ResolveDragAction(_activeButton, _activeModifiers))
        {
            case CameraDragAction.Pan:
                _marina.Camera.Pan(delta.X, delta.Y, _marina.ViewportSize.Y);
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

        // Modifiers held at press or release both count, so Ctrl released a moment early still multi-selects.
        if (!wasDragging) _marina.HandleClick(x, y, button, isDoubleClick: false, modifiers | pressModifiers);
    }

    /// <summary>Forward a double-click. A left double-click on a slip focuses the camera on it (at <c>DefaultFocusAngle</c>).</summary>
    /// <param name="x">Pointer X in view pixels.</param>
    /// <param name="y">Pointer Y in view pixels.</param>
    /// <param name="button">Button.</param>
    /// <param name="modifiers">Modifier keys held.</param>
    public void DoubleClick(float x, float y, PointerButton button, InputModifiers modifiers = InputModifiers.None) =>
        _marina.HandleClick(x, y, button, isDoubleClick: true, modifiers);

    /// <summary>Wheel input in notches: positive zooms in.</summary>
    public void Wheel(float notches, float x, float y)
    {
        if (notches == 0f || !float.IsFinite(notches)) return;
        var focus = _marina.GetWaterPoint(x, y);
        _marina.Camera.Zoom(MathF.Pow(ZoomStepFactor, Math.Clamp(notches, -10f, 10f)), focus);
    }

    /// <summary>Forward the pointer leaving the view: ends any drag and clears the hover.</summary>
    public void PointerLeave()
    {
        _activeButton = PointerButton.None;
        _dragging = false;
        _marina.HandlePointerLeave();
    }

    /// <summary>Returns true when the key was handled.</summary>
    public bool KeyDown(MarinaKey key, InputModifiers modifiers = InputModifiers.None)
    {
        var camera = _marina.Camera;
        var orbit = (modifiers & InputModifiers.Shift) != 0;
        switch (key)
        {
            case MarinaKey.Left when orbit: camera.Orbit(KeyboardOrbitDegrees, 0f); break;
            case MarinaKey.Right when orbit: camera.Orbit(-KeyboardOrbitDegrees, 0f); break;
            case MarinaKey.Up when orbit: camera.Orbit(0f, KeyboardOrbitDegrees); break;
            case MarinaKey.Down when orbit: camera.Orbit(0f, -KeyboardOrbitDegrees); break;
            case MarinaKey.Left: camera.PanWorld(-KeyboardPanMeters, 0f); break;
            case MarinaKey.Right: camera.PanWorld(KeyboardPanMeters, 0f); break;
            case MarinaKey.Up: camera.PanWorld(0f, KeyboardPanMeters); break;
            case MarinaKey.Down: camera.PanWorld(0f, -KeyboardPanMeters); break;
            case MarinaKey.PageUp: camera.Orbit(0f, KeyboardOrbitDegrees); break;
            case MarinaKey.PageDown: camera.Orbit(0f, -KeyboardOrbitDegrees); break;
            case MarinaKey.ZoomIn: camera.Zoom(ZoomStepFactor); break;
            case MarinaKey.ZoomOut: camera.Zoom(1f / ZoomStepFactor); break;
            case MarinaKey.Home: _marina.ResetCamera(); break;
            case MarinaKey.Escape: _marina.HandleEscape(); break;
            default: return false;
        }

        return true;
    }

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
