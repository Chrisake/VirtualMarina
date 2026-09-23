namespace VirtualMarina.Core.Input;

/// <summary>Platform-neutral mouse button, as forwarded by host views to <see cref="MarinaInputController"/>.</summary>
public enum PointerButton
{
    /// <summary>No button (hover, or an event raised by an API call).</summary>
    None = 0,

    /// <summary>Primary button: select, show the tooltip, pan by dragging.</summary>
    Left = 1,

    /// <summary>Middle button or wheel press: pan by dragging.</summary>
    Middle = 2,

    /// <summary>Secondary button: open the actions window, orbit by dragging.</summary>
    Right = 3,
}

/// <summary>Keyboard modifiers held during a pointer or key event.</summary>
[Flags]
public enum InputModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>Shift: click adds or removes berths from the selection (like Control); swaps pan and orbit while dragging; orbits with the arrow keys.</summary>
    Shift = 1,

    /// <summary>Control (Cmd on macOS browsers): click adds or removes berths from the selection (like Shift).</summary>
    Control = 2,

    /// <summary>Alt: in the designer, turns snapping off while held.</summary>
    Alt = 4,
}

/// <summary>Platform-neutral keys the viewer responds to. Host views map native keys to these.</summary>
public enum MarinaKey
{
    /// <summary>Pan left (orbit with Shift). WinForms/Blazor map Left arrow and A.</summary>
    Left = 0,

    /// <summary>Pan right (orbit with Shift). Mapped from Right arrow and D.</summary>
    Right = 1,

    /// <summary>Pan forward (tilt with Shift). Mapped from Up arrow and W.</summary>
    Up = 2,

    /// <summary>Pan back (tilt with Shift). Mapped from Down arrow and S.</summary>
    Down = 3,

    /// <summary>Tilt the camera up.</summary>
    PageUp = 4,

    /// <summary>Tilt the camera down.</summary>
    PageDown = 5,

    /// <summary>Zoom in. Mapped from + and =.</summary>
    ZoomIn = 6,

    /// <summary>Zoom out. Mapped from - and _.</summary>
    ZoomOut = 7,

    /// <summary>Reset the camera to the overview.</summary>
    Home = 8,

    /// <summary>
    /// Close the popup; pressed again, clear the selection. In the designer: cancel a drag or the drawing, then return to
    /// navigation (unless <c>MarinaDesigner.EscapeReturnsToNavigate</c> is off).
    /// </summary>
    Escape = 9,

    /// <summary>Designer: finish the drawing in progress.</summary>
    Enter = 10,

    /// <summary>Designer: remove the last placed point.</summary>
    Backspace = 11,

    /// <summary>Designer: with the erase tool, remove the element under the pointer; with the area tool, remove the berths selected.</summary>
    Delete = 12,

    /// <summary>
    /// Designer: undo the last change (<c>MarinaDesigner.Undo</c>), or take back the last point while a drawing is in progress.
    /// Hosts map Ctrl+Z.
    /// </summary>
    Undo = 13,

    /// <summary>Designer: make the last undone change again (<c>MarinaDesigner.Redo</c>). Hosts map Ctrl+Shift+Z and Ctrl+Y.</summary>
    Redo = 14,
}

/// <summary>What dragging with a mouse button does. Configure with <see cref="MarinaInputController.LeftDragAction"/> and related properties.</summary>
public enum CameraDragAction
{
    /// <summary>Dragging does nothing.</summary>
    None = 0,

    /// <summary>Map-style pan: the ground under the pointer follows it.</summary>
    Pan = 1,

    /// <summary>Rotate the camera around its target.</summary>
    Orbit = 2,
}
