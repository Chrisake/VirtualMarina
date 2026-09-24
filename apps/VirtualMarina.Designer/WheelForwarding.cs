namespace VirtualMarina.Designer;

/// <summary>
/// Makes the mouse wheel scroll the settings column rather than turn whatever slider, number field or drop-down
/// happens to pass under the pointer on the way down. A control that has the focus keeps the wheel, so a value can
/// still be dialled in deliberately: click it first, then scroll.
/// </summary>
internal static class WheelForwarding
{
    /// <summary>Wheel notches Windows reports per click, and the step it scrolls a line by.</summary>
    private const int WheelDelta = 120;

    /// <summary>How far one line of scrolling moves the column, at 96 DPI.</summary>
    private const int LineHeight = 16;

    /// <summary>
    /// Watches every control inside <paramref name="scroller"/>, now and whenever one is added later, and sends the
    /// wheel of the ones that would otherwise take it to the column instead.
    /// </summary>
    /// <param name="scroller">The scrolling column.</param>
    public static void Attach(ScrollableControl scroller)
    {
        ArgumentNullException.ThrowIfNull(scroller);
        Watch(scroller, scroller);
    }

    private static void Watch(Control control, ScrollableControl scroller)
    {
        if (TakesTheWheel(control)) control.MouseWheel += (_, e) => Forward(control, scroller, e);

        foreach (Control child in control.Controls) Watch(child, scroller);
        control.ControlAdded += (_, e) =>
        {
            if (e.Control is { } added) Watch(added, scroller);
        };
    }

    /// <summary>The controls whose value the wheel changes: sliders, number fields (their edit box too) and drop-downs.</summary>
    private static bool TakesTheWheel(Control control) =>
        control is TrackBar or UpDownBase or ComboBox || control.Parent is UpDownBase;

    private static void Forward(Control control, ScrollableControl scroller, MouseEventArgs e)
    {
        // The control being worked with keeps the wheel; so does an open drop-down list.
        var owner = control.Parent as UpDownBase ?? control;
        if (owner.Focused || control.Focused || owner is ComboBox { DroppedDown: true }) return;
        if (e is HandledMouseEventArgs handled) handled.Handled = true;

        var lines = SystemInformation.MouseWheelScrollLines;
        var step = lines < 0 ? scroller.ClientSize.Height : scroller.LogicalToDeviceUnits(LineHeight) * lines;
        var top = -scroller.AutoScrollPosition.Y - e.Delta * step / WheelDelta;
        scroller.AutoScrollPosition = new Point(-scroller.AutoScrollPosition.X, Math.Max(0, top));
    }
}
