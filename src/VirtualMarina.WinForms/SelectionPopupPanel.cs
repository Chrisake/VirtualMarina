using System.Drawing.Drawing2D;
using VirtualMarina.Core.Api;
using VirtualMarina.WinForms.Resources;

namespace VirtualMarina.WinForms;

/// <summary>
/// Custom-painted tooltip / actions window drawn over the 3D view, pointing at the selected berth.
/// Shaped with a window region (rounded card plus caret) because child controls can't be transparent over OpenGL.
/// </summary>
/// <remarks>
/// <para>
/// It can be used from the keyboard: Tab from the view reaches it, Up and Down (or Home and End) move between its
/// actions and its close button, Enter or Space runs the one marked, and Esc closes it. Screen readers see it as a
/// dialog whose lines are text and whose actions are menu items.
/// </para>
/// <para>
/// Its fonts are made in pixels for the monitor it is on, and made again when it moves to one with another scale, so
/// the text keeps its size relative to the card whatever the DPI.
/// </para>
/// </remarks>
internal sealed class SelectionPopupPanel : Control
{
    private static readonly Color CardColor = Color.White;
    private static readonly Color BorderColor = Color.FromArgb(196, 206, 216);
    private static readonly Color TextColor = Color.FromArgb(28, 38, 49);
    private static readonly Color MutedColor = Color.FromArgb(100, 115, 131);
    private static readonly Color SeparatorColor = Color.FromArgb(230, 235, 240);
    private static readonly Color HoverColor = Color.FromArgb(234, 241, 248);
    private static readonly Color DangerHoverColor = Color.FromArgb(253, 238, 238);
    private static readonly Color PrimaryColor = Color.FromArgb(31, 95, 209);
    private static readonly Color DangerColor = Color.FromArgb(198, 47, 40);
    private static readonly Color DefaultAccent = Color.FromArgb(91, 122, 153);

    private const TextFormatFlags SingleLine = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
    private const TextFormatFlags Wrapped = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;

    private readonly ToolTip _hint = new() { InitialDelay = 400, ShowAlways = true };
    private Font _titleFont = null!;
    private Font _textFont = null!;
    private Font _boldFont = null!;
    private Font _smallFont = null!;
    private Font _iconFont = null!;

    private BerthPopup? _popup;
    private readonly List<(BerthTooltipLine Line, Rectangle Label, Rectangle Value)> _lines = [];
    private readonly List<(BerthAction Action, Rectangle Bounds)> _actions = [];
    private readonly List<int> _separators = [];
    private Rectangle _titleRect;
    private Rectangle _subtitleRect;
    private Rectangle _closeRect;
    private Rectangle _footerRect;
    private int _cardHeight;
    private int _caretX = -1;
    private int _hoverAction = -1;
    private bool _hoverClose;
    private string? _hintText;

    /// <summary>
    /// What the keyboard has marked: an index into the actions, <see cref="_actions"/>.Count for the close button, or
    /// -1 for nothing.
    /// </summary>
    private int _focusIndex = -1;

    public SelectionPopupPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, true);
        Visible = false;
        TabStop = true;
        AccessibleRole = AccessibleRole.Dialog;
        CreateFonts();
    }

    /// <summary>The user clicked an enabled action.</summary>
    public event EventHandler<string>? ActionClicked;

    public event EventHandler? CloseClicked;

    public BerthPopup? Popup => _popup;

    private float DpiScale => DeviceDpi / 96f;

    private int S(float value) => (int)MathF.Round(value * DpiScale);

    private int CaretHeight => S(8);

    /// <summary>Lays out new popup content and resizes the control.</summary>
    public void Present(BerthPopup popup)
    {
        _popup = popup;
        _hoverAction = -1;
        _hoverClose = false;
        _focusIndex = Focused ? FirstFocusable() : -1;
        LayoutContent(popup);
        _caretX = -1;
        AccessibleName = TitleText(popup);
        Invalidate();
        AccessibilityNotifyClients(AccessibleEvents.Reorder, -1);
    }

    /// <summary>
    /// Positions the popup so its caret points at <paramref name="anchor"/>, kept inside <paramref name="container"/>.
    /// Returns false when the anchor is off-screen (the caller hides the popup).
    /// </summary>
    public bool PositionAt(Point anchor, Size container)
    {
        var slack = S(20);
        if (anchor.X < -slack || anchor.X > container.Width + slack || anchor.Y < -slack || anchor.Y > container.Height + S(60)) return false;

        var margin = S(8);
        var gap = S(4);
        var left = Math.Clamp(anchor.X - Width / 2, margin, Math.Max(margin, container.Width - Width - margin));
        var desiredTop = anchor.Y - Height - gap;
        var top = Math.Max(desiredTop, margin);
        var caret = top == desiredTop ? Math.Clamp(anchor.X - left, S(18), Width - S(18)) : 0;

        if (Left != left || Top != top) Location = new Point(left, top);
        if (caret != _caretX)
        {
            _caretX = caret;
            UpdateRegion();
            Invalidate();
        }

        return true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_popup is not { } popup) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(CardColor);

        var accent = popup.Tooltip.AccentColor is { } c
            ? Color.FromArgb(255, ToByte(c.R), ToByte(c.G), ToByte(c.B))
            : DefaultAccent;

        // Accent bar along the top edge (the rounded region clips its corners).
        using (var accentBrush = new SolidBrush(accent)) g.FillRectangle(accentBrush, 0, 0, Width, S(4));

        // Border following the card outline and caret.
        using (var path = CreateOutline(inset: 0.5f))
        using (var pen = new Pen(BorderColor, 1f))
        {
            g.DrawPath(pen, path);
        }

        g.SmoothingMode = SmoothingMode.None;
        var tooltip = popup.Tooltip;
        if (_titleRect.Width > 0) TextRenderer.DrawText(g, TitleText(popup), _titleFont, _titleRect, TextColor, SingleLine);
        if (_subtitleRect.Width > 0) TextRenderer.DrawText(g, tooltip.Subtitle, _smallFont, _subtitleRect, MutedColor, SingleLine);
        if (_closeRect.Width > 0)
        {
            if (_hoverClose || (Focused && _focusIndex == _actions.Count))
            {
                using var hover = new SolidBrush(HoverColor);
                g.FillRectangle(hover, _closeRect);
            }

            TextRenderer.DrawText(g, "✕", _smallFont, _closeRect, _hoverClose ? TextColor : MutedColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        foreach (var (line, labelRect, valueRect) in _lines)
        {
            if (labelRect.Width > 0) TextRenderer.DrawText(g, line.Label, _textFont, labelRect, MutedColor, SingleLine);
            TextRenderer.DrawText(g, line.Value ?? string.Empty, line.IsEmphasized ? _boldFont : _textFont, valueRect, TextColor, Wrapped);
        }

        if (_footerRect.Width > 0) TextRenderer.DrawText(g, tooltip.Footer, _smallFont, _footerRect, MutedColor, Wrapped);

        if (Focused && FocusBounds() is { IsEmpty: false } focus) ControlPaint.DrawFocusRectangle(g, focus);

        using var separatorPen = new Pen(SeparatorColor);
        foreach (var y in _separators) g.DrawLine(separatorPen, S(10), y, Width - S(10), y);

        for (var i = 0; i < _actions.Count; i++)
        {
            var (action, bounds) = _actions[i];
            if ((i == _hoverAction || (Focused && i == _focusIndex)) && action.Enabled)
            {
                using var hover = new SolidBrush(action.Style == BerthActionStyle.Danger ? DangerHoverColor : HoverColor);
                g.FillRectangle(hover, bounds);
            }

            var color = action.Style switch
            {
                BerthActionStyle.Primary => PrimaryColor,
                BerthActionStyle.Danger => DangerColor,
                _ => TextColor,
            };
            if (!action.Enabled) color = Blend(color, CardColor, 0.55f);

            var iconRect = new Rectangle(bounds.X + S(6), bounds.Y, S(20), bounds.Height);
            if (!string.IsNullOrEmpty(action.Icon))
            {
                TextRenderer.DrawText(g, action.Icon, _iconFont, iconRect, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            var shortcutWidth = string.IsNullOrEmpty(action.ShortcutText) ? 0 : TextRenderer.MeasureText(action.ShortcutText, _smallFont).Width;
            var captionRect = Rectangle.FromLTRB(iconRect.Right + S(6), bounds.Y, bounds.Right - S(8) - shortcutWidth, bounds.Bottom);
            TextRenderer.DrawText(g, action.Caption, action.Style == BerthActionStyle.Primary ? _boldFont : _textFont, captionRect, color, SingleLine | TextFormatFlags.VerticalCenter);
            if (shortcutWidth > 0)
            {
                var shortcutRect = Rectangle.FromLTRB(bounds.Right - S(8) - shortcutWidth, bounds.Y, bounds.Right - S(8), bounds.Bottom);
                TextRenderer.DrawText(g, action.ShortcutText, _smallFont, shortcutRect, MutedColor, SingleLine | TextFormatFlags.VerticalCenter);
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = _actions.FindIndex(a => a.Bounds.Contains(e.Location));
        var overClose = _closeRect.Contains(e.Location);
        if (index != _hoverAction || overClose != _hoverClose)
        {
            _hoverAction = index;
            _hoverClose = overClose;
            Invalidate();
        }

        var clickable = overClose || (index >= 0 && _actions[index].Action.Enabled);
        Cursor = clickable ? Cursors.Hand : Cursors.Default;

        var hint = index >= 0 ? _actions[index].Action.Description : overClose ? Strings.PopupClose : null;
        if (hint != _hintText)
        {
            _hintText = hint;
            _hint.SetToolTip(this, hint);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverAction >= 0 || _hoverClose)
        {
            _hoverAction = -1;
            _hoverClose = false;
            Invalidate();
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;

        if (_closeRect.Contains(e.Location))
        {
            CloseClicked?.Invoke(this, EventArgs.Empty);
            return;
        }

        var index = _actions.FindIndex(a => a.Bounds.Contains(e.Location));
        if (index >= 0 && _actions[index].Action.Enabled) ActionClicked?.Invoke(this, _actions[index].Action.ActionId);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        CreateFonts();
        if (_popup is not null) Present(_popup);
    }

    /// <summary>The handle knows the monitor's DPI, which the constructor had to guess.</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        CreateFonts();
        if (_popup is not null) Present(_popup);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeFonts();
            _hint.Dispose();
        }

        base.Dispose(disposing);
    }

    // ---- Keyboard -----------------------------------------------------------------------------------

    /// <summary>The keys the popup answers itself rather than letting the form move the focus with them.</summary>
    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.Enter or Keys.Space or Keys.Escape
        && (keyData & (Keys.Control | Keys.Alt)) == 0
        || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Control || e.Alt) return;

        switch (e.KeyCode)
        {
            case Keys.Down: MoveFocus(+1); break;
            case Keys.Up: MoveFocus(-1); break;
            case Keys.Home: SetFocusIndex(FirstFocusable()); break;
            case Keys.End: SetFocusIndex(LastFocusable()); break;
            case Keys.Enter or Keys.Space: Activate(_focusIndex); break;
            case Keys.Escape: CloseClicked?.Invoke(this, EventArgs.Empty); break;
            default: return;
        }

        e.Handled = true;
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        if (_focusIndex < 0) _focusIndex = FirstFocusable();
        Invalidate();
        if (_focusIndex >= 0) AccessibilityNotifyClients(AccessibleEvents.Focus, AccessibleChildIndex(_focusIndex));
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new PopupAccessibleObject(this);

    /// <summary>Runs an action (or the close button) by its index, as a click on it would.</summary>
    private void Activate(int index)
    {
        if (index == _actions.Count && _closeRect.Width > 0) CloseClicked?.Invoke(this, EventArgs.Empty);
        else if (index >= 0 && index < _actions.Count && _actions[index].Action.Enabled) ActionClicked?.Invoke(this, _actions[index].Action.ActionId);
    }

    private bool IsFocusable(int index) =>
        (index >= 0 && index < _actions.Count && _actions[index].Action.Enabled) || (index == _actions.Count && _closeRect.Width > 0);

    private int FirstFocusable() => Enumerable.Range(0, _actions.Count + 1).FirstOrDefault(IsFocusable, -1);

    private int LastFocusable() => Enumerable.Range(0, _actions.Count + 1).Reverse().FirstOrDefault(IsFocusable, -1);

    /// <summary>Marks the next (or previous) enabled action or the close button, wrapping round at the ends.</summary>
    private void MoveFocus(int step)
    {
        var count = _actions.Count + 1;
        var index = _focusIndex;
        for (var tried = 0; tried < count; tried++)
        {
            index = ((index < 0 ? (step > 0 ? -1 : 0) : index) + step + count) % count;
            if (!IsFocusable(index)) continue;
            SetFocusIndex(index);
            return;
        }
    }

    private void SetFocusIndex(int index)
    {
        if (index == _focusIndex) return;
        _focusIndex = index;
        Invalidate();
        if (index >= 0) AccessibilityNotifyClients(AccessibleEvents.Focus, AccessibleChildIndex(index));
    }

    /// <summary>Where the keyboard's mark is drawn: round the marked action, or the close button.</summary>
    private Rectangle FocusBounds()
    {
        if (_focusIndex >= 0 && _focusIndex < _actions.Count) return Rectangle.Inflate(_actions[_focusIndex].Bounds, -1, -1);
        return _focusIndex == _actions.Count ? _closeRect : Rectangle.Empty;
    }

    /// <summary>Where the close button is, empty when there is none.</summary>
    private Rectangle CloseBounds => _closeRect;

    /// <summary>The index of an action (or the close button) among the popup's accessible children, which start with its lines.</summary>
    private int AccessibleChildIndex(int focusIndex) => _lines.Count + focusIndex;

    // ---- Fonts --------------------------------------------------------------------------------------

    /// <summary>
    /// Makes the fonts in pixels for the current DPI. Point sizes would be turned into pixels once, for whichever monitor
    /// the process started on, and stay that size on every other one.
    /// </summary>
    private void CreateFonts()
    {
        DisposeFonts();
        _titleFont = PixelFont("Segoe UI Semibold", 10.5f);
        _textFont = PixelFont("Segoe UI", 9f);
        _boldFont = PixelFont("Segoe UI", 9f, FontStyle.Bold);
        _smallFont = PixelFont("Segoe UI", 8f);
        _iconFont = PixelFont("Segoe UI Emoji", 9f);
    }

    private Font PixelFont(string family, float points, FontStyle style = FontStyle.Regular) =>
        new(family, points * DeviceDpi / 72f, style, GraphicsUnit.Pixel);

    private void DisposeFonts()
    {
        _titleFont?.Dispose();
        _textFont?.Dispose();
        _boldFont?.Dispose();
        _smallFont?.Dispose();
        _iconFont?.Dispose();
    }

    // ---- Layout -------------------------------------------------------------------------------------

    private void LayoutContent(BerthPopup popup)
    {
        _lines.Clear();
        _actions.Clear();
        _separators.Clear();
        _titleRect = _subtitleRect = _closeRect = _footerRect = Rectangle.Empty;

        var tooltip = popup.Tooltip;
        var showTooltip = tooltip.IsVisible;
        var pad = S(12);
        var minWidth = S(230);
        var maxWidth = S(340);
        var gap = S(12);

        // Width: widest of title row, label + value pairs and action captions.
        var labelWidth = showTooltip
            ? tooltip.Lines.Where(l => !string.IsNullOrEmpty(l.Label)).Select(l => TextRenderer.MeasureText(l.Label, _textFont).Width).DefaultIfEmpty(0).Max()
            : 0;
        var wanted = minWidth;
        var title = TitleText(popup);
        var hasHead = popup.Kind == BerthPopupKind.Actions || (showTooltip && (!string.IsNullOrWhiteSpace(tooltip.Title) || !string.IsNullOrWhiteSpace(tooltip.Subtitle)));
        if (hasHead)
        {
            wanted = Math.Max(wanted, pad * 2 + TextRenderer.MeasureText(title, _titleFont).Width + S(28));
            if (showTooltip && !string.IsNullOrWhiteSpace(tooltip.Subtitle)) wanted = Math.Max(wanted, pad * 2 + TextRenderer.MeasureText(tooltip.Subtitle, _smallFont).Width + S(28));
        }

        if (showTooltip)
        {
            foreach (var line in tooltip.Lines)
            {
                var font = line.IsEmphasized ? _boldFont : _textFont;
                var valueWidth = TextRenderer.MeasureText(line.Value ?? string.Empty, font).Width;
                wanted = Math.Max(wanted, pad * 2 + (string.IsNullOrEmpty(line.Label) ? 0 : labelWidth + gap) + valueWidth);
            }
        }

        foreach (var action in popup.Actions)
        {
            var shortcut = string.IsNullOrEmpty(action.ShortcutText) ? 0 : TextRenderer.MeasureText(action.ShortcutText, _smallFont).Width + S(12);
            wanted = Math.Max(wanted, S(4) * 2 + S(6) + S(20) + S(6) + TextRenderer.MeasureText(action.Caption, _boldFont).Width + shortcut + S(12));
        }

        var width = Math.Clamp(wanted, minWidth, maxWidth);
        var y = S(4);

        if (hasHead)
        {
            y += S(9);
            var titleHeight = TextRenderer.MeasureText("Ag", _titleFont).Height;
            _closeRect = new Rectangle(width - S(8) - S(22), y - S(2), S(22), S(22));
            _titleRect = new Rectangle(pad, y, _closeRect.Left - S(4) - pad, titleHeight);
            y += titleHeight;
            if (showTooltip && !string.IsNullOrWhiteSpace(tooltip.Subtitle))
            {
                var subtitleHeight = TextRenderer.MeasureText("Ag", _smallFont).Height;
                _subtitleRect = new Rectangle(pad, y, width - pad * 2, subtitleHeight);
                y += subtitleHeight;
            }
        }
        else
        {
            y += S(6);
        }

        if (showTooltip && tooltip.Lines.Count > 0)
        {
            y += S(8);
            var valueLeft = pad + (labelWidth > 0 ? labelWidth + gap : 0);
            foreach (var line in tooltip.Lines)
            {
                var font = line.IsEmphasized ? _boldFont : _textFont;
                var wide = string.IsNullOrEmpty(line.Label);
                var left = wide ? pad : valueLeft;
                var valueWidth = Math.Max(S(40), width - pad - left);
                var size = TextRenderer.MeasureText(line.Value ?? " ", font, new Size(valueWidth, int.MaxValue), Wrapped);
                var height = Math.Max(size.Height, TextRenderer.MeasureText("Ag", _textFont).Height);
                _lines.Add((line, wide ? Rectangle.Empty : new Rectangle(pad, y, labelWidth, height), new Rectangle(left, y, valueWidth, height)));
                y += height + S(3);
            }

            y -= S(3);
        }

        if (showTooltip && !string.IsNullOrWhiteSpace(tooltip.Footer))
        {
            y += S(6);
            var size = TextRenderer.MeasureText(tooltip.Footer, _smallFont, new Size(width - pad * 2, int.MaxValue), Wrapped);
            _footerRect = new Rectangle(pad, y, width - pad * 2, size.Height);
            y += size.Height;
        }

        if (popup.Kind == BerthPopupKind.Actions && popup.Actions.Count > 0)
        {
            y += S(8);
            _separators.Add(y);
            y += S(4);
            var rowHeight = S(28);
            for (var i = 0; i < popup.Actions.Count; i++)
            {
                var action = popup.Actions[i];
                if (action.BeginGroup && i > 0)
                {
                    y += S(4);
                    _separators.Add(y);
                    y += S(4);
                }

                _actions.Add((action, new Rectangle(S(4), y, width - S(8), rowHeight)));
                y += rowHeight;
            }

            y += S(4);
        }
        else
        {
            y += S(10);
        }

        _cardHeight = y;
        Size = new Size(width, _cardHeight + CaretHeight);
        UpdateRegion();
    }

    private static string TitleText(BerthPopup popup) =>
        popup.Tooltip.IsVisible && !string.IsNullOrWhiteSpace(popup.Tooltip.Title) ? popup.Tooltip.Title : popup.PrimaryBerth.DisplayName;

    private void UpdateRegion()
    {
        using var path = CreateOutline(inset: 0f);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    /// <summary>Rounded card with a downward caret at <see cref="_caretX"/> (none when 0).</summary>
    private GraphicsPath CreateOutline(float inset)
    {
        var r = S(8) * 2f;
        var left = inset;
        var top = inset;
        var right = Width - inset - (inset > 0 ? 1f : 0f);
        var bottom = _cardHeight - inset - (inset > 0 ? 1f : 0f);
        var caretHalf = S(8);
        var path = new GraphicsPath();
        path.AddArc(left, top, r, r, 180, 90);
        path.AddArc(right - r, top, r, r, 270, 90);
        path.AddArc(right - r, bottom - r, r, r, 0, 90);
        if (_caretX > 0)
        {
            path.AddLine(_caretX + caretHalf, bottom, _caretX, bottom + CaretHeight - (inset > 0 ? 1f : 0f));
            path.AddLine(_caretX, bottom + CaretHeight - (inset > 0 ? 1f : 0f), _caretX - caretHalf, bottom);
        }

        path.AddArc(left, bottom - r, r, r, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static int ToByte(float v) => Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    private static Color Blend(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    // ---- Accessibility ------------------------------------------------------------------------------

    /// <summary>The popup as a screen reader sees it: a dialog named after the berth, its lines as text, its actions as menu items.</summary>
    private sealed class PopupAccessibleObject(SelectionPopupPanel owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.Dialog;

        public override string? Name => owner._popup is { } popup ? TitleText(popup) : base.Name;

        public override string? Description => owner._popup?.Tooltip.Subtitle;

        public override int GetChildCount() => owner._lines.Count + owner._actions.Count + (owner.CloseBounds.Width > 0 ? 1 : 0);

        public override AccessibleObject? GetChild(int index)
        {
            if (index < 0) return null;
            if (index < owner._lines.Count) return new LineAccessibleObject(this, owner, index);
            index -= owner._lines.Count;
            if (index < owner._actions.Count || (index == owner._actions.Count && owner.CloseBounds.Width > 0))
            {
                return new ItemAccessibleObject(this, owner, index);
            }

            return null;
        }

        public override AccessibleObject? GetFocused() =>
            owner.Focused && owner._focusIndex >= 0 ? GetChild(owner.AccessibleChildIndex(owner._focusIndex)) : base.GetFocused();
    }

    /// <summary>One line of the tooltip: its label and value, read as text.</summary>
    private sealed class LineAccessibleObject(AccessibleObject parent, SelectionPopupPanel owner, int index) : AccessibleObject
    {
        public override AccessibleObject Parent => parent;

        public override AccessibleRole Role => AccessibleRole.StaticText;

        public override string? Name
        {
            get
            {
                var line = owner._lines[index].Line;
                return string.IsNullOrEmpty(line.Label) ? line.Value : $"{line.Label}: {line.Value}";
            }
        }

        public override Rectangle Bounds => owner.RectangleToScreen(owner._lines[index].Value);

        public override AccessibleStates State => AccessibleStates.ReadOnly;
    }

    /// <summary>An action (a menu item), or the close button after the last of them.</summary>
    private sealed class ItemAccessibleObject(AccessibleObject parent, SelectionPopupPanel owner, int index) : AccessibleObject
    {
        private bool IsClose => index == owner._actions.Count;

        public override AccessibleObject Parent => parent;

        public override AccessibleRole Role => IsClose ? AccessibleRole.PushButton : AccessibleRole.MenuItem;

        public override string? Name => IsClose ? Strings.PopupClose : owner._actions[index].Action.Caption;

        public override string? Description => IsClose ? null : owner._actions[index].Action.Description;

        public override string? KeyboardShortcut => IsClose ? null : owner._actions[index].Action.ShortcutText;

        public override string DefaultAction => Strings.AccessibleActionPress;

        public override Rectangle Bounds => owner.RectangleToScreen(IsClose ? owner.CloseBounds : owner._actions[index].Bounds);

        public override AccessibleStates State
        {
            get
            {
                var state = AccessibleStates.Focusable | AccessibleStates.Selectable;
                if (!IsClose && !owner._actions[index].Action.Enabled) state = AccessibleStates.Unavailable;
                if (owner.Focused && owner._focusIndex == index) state |= AccessibleStates.Focused | AccessibleStates.Selected;
                return state;
            }
        }

        public override void DoDefaultAction() => owner.Activate(index);

        public override void Select(AccessibleSelection flags)
        {
            if ((flags & AccessibleSelection.TakeFocus) != 0) owner.Focus();
            if ((flags & (AccessibleSelection.TakeFocus | AccessibleSelection.TakeSelection)) != 0) owner.SetFocusIndex(index);
        }
    }
}
