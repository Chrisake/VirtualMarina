using System.Drawing.Drawing2D;
using VirtualMarina.Core.Api;
using VirtualMarina.WinForms.Resources;

namespace VirtualMarina.WinForms;

/// <summary>
/// Custom-painted tooltip / actions window drawn over the 3D view, pointing at the selected berth.
/// Shaped with a window region (rounded card plus caret) because child controls can't be transparent over OpenGL.
/// </summary>
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

    private readonly Font _titleFont = new("Segoe UI Semibold", 10.5f);
    private readonly Font _textFont = new("Segoe UI", 9f);
    private readonly Font _boldFont = new("Segoe UI", 9f, FontStyle.Bold);
    private readonly Font _smallFont = new("Segoe UI", 8f);
    private readonly Font _iconFont = new("Segoe UI Emoji", 9f);
    private readonly ToolTip _hint = new() { InitialDelay = 400, ShowAlways = true };

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

    public SelectionPopupPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Visible = false;
        TabStop = false;
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
        LayoutContent(popup);
        _caretX = -1;
        Invalidate();
    }

    /// <summary>
    /// Positions the popup so its caret points at <paramref name="anchor"/>, kept inside <paramref name="container"/>.
    /// Returns false when the anchor is off-screen (the caller hides the popup).
    /// </summary>
    public bool PositionAt(Point anchor, Size container)
    {
        const int slack = 20;
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
            if (_hoverClose)
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

        using var separatorPen = new Pen(SeparatorColor);
        foreach (var y in _separators) g.DrawLine(separatorPen, S(10), y, Width - S(10), y);

        for (var i = 0; i < _actions.Count; i++)
        {
            var (action, bounds) = _actions[i];
            if (i == _hoverAction && action.Enabled)
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
        if (_popup is not null) Present(_popup);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _textFont.Dispose();
            _boldFont.Dispose();
            _smallFont.Dispose();
            _iconFont.Dispose();
            _hint.Dispose();
        }

        base.Dispose(disposing);
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
}
