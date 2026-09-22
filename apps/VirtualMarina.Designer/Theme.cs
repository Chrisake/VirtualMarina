using System.Drawing.Drawing2D;

namespace VirtualMarina.Designer;

/// <summary>Colors, fonts and the small building blocks the designer's panels are made of, so every screen looks the same.</summary>
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(0xF4, 0xF6, 0xF8);
    public static readonly Color Surface = Color.White;
    public static readonly Color Border = Color.FromArgb(0xDD, 0xE2, 0xE8);
    public static readonly Color Accent = Color.FromArgb(0x16, 0x5D, 0xC4);
    public static readonly Color AccentSoft = Color.FromArgb(0xE4, 0xEE, 0xFB);
    public static readonly Color Text = Color.FromArgb(0x1C, 0x25, 0x30);
    public static readonly Color TextSoft = Color.FromArgb(0x5C, 0x6B, 0x7A);
    public static readonly Color Danger = Color.FromArgb(0xC0, 0x39, 0x2B);

    public static readonly Font Title = new("Segoe UI Semibold", 11f);
    public static readonly Font Body = new("Segoe UI", 9f);
    public static readonly Font Small = new("Segoe UI", 8.25f);
    public static readonly Font Caption = new("Segoe UI Semibold", 8.25f);

    /// <summary>A titled white card: the panels of the inspector are all made of these.</summary>
    public static Panel Card(string title, out TableLayoutPanel content)
    {
        var card = new Panel
        {
            BackColor = Surface,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 10, 12, 12),
            Margin = new Padding(0, 0, 0, 10),
        };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };

        content = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104f));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var header = new Label
        {
            Text = title.ToUpperInvariant(),
            Font = Caption,
            ForeColor = TextSoft,
            AutoSize = true,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 6),
            Padding = new Padding(0, 0, 0, 6),
        };

        card.Controls.Add(content);
        card.Controls.Add(header);
        return card;
    }

    /// <summary>Adds a labelled row to a card's table.</summary>
    public static T Row<T>(TableLayoutPanel table, string label, T control, string? tooltip = null)
        where T : Control
    {
        var row = table.RowCount++;
        var caption = new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = Text,
            Font = Body,
            Margin = new Padding(0, 7, 8, 4),
        };

        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        control.Margin = new Padding(0, 4, 0, 4);
        table.Controls.Add(caption, 0, row);
        table.Controls.Add(control, 1, row);
        if (tooltip is not null) Tips.SetToolTip(control, tooltip);
        return control;
    }

    /// <summary>Adds a control that spans both columns.</summary>
    public static T FullRow<T>(TableLayoutPanel table, T control)
        where T : Control
    {
        var row = table.RowCount++;
        control.Margin = new Padding(0, 4, 0, 4);
        table.Controls.Add(control, 0, row);
        table.SetColumnSpan(control, 2);
        return control;
    }

    /// <summary>
    /// Explanatory line inside a card. Docked, so the cell it sits in decides how wide it is and the text
    /// wraps to whatever width the panel has been dragged to.
    /// </summary>
    public static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Dock = DockStyle.Fill,
        ForeColor = TextSoft,
        Font = Small,
        Margin = new Padding(0, 2, 0, 2),
    };

    public static NumericUpDown Number(decimal min, decimal max, decimal step, int decimals = 2) => new()
    {
        Minimum = min,
        Maximum = max,
        Increment = step,
        DecimalPlaces = decimals,
        TextAlign = HorizontalAlignment.Right,
        Font = Body,
        BorderStyle = BorderStyle.FixedSingle,
        Height = 24,
    };

    public static ComboBox Choice() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Font = Body,
        FlatStyle = FlatStyle.Flat,
        Height = 24,
    };

    /// <summary>A single-line text field, for names and patterns.</summary>
    public static TextBox Field() => new()
    {
        Font = Body,
        BorderStyle = BorderStyle.FixedSingle,
        Height = 24,
    };

    /// <summary>Adds a heading that divides one card into sections, spanning both columns.</summary>
    public static Label Section(TableLayoutPanel table, string text)
    {
        var label = FullRow(table, new Label
        {
            Text = text.ToUpperInvariant(),
            AutoSize = true,
            Font = Caption,
            ForeColor = TextSoft,
        });

        label.Margin = new Padding(0, 14, 0, 2);
        return label;
    }

    public static CheckBox Check(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Body,
        ForeColor = Text,
    };

    public static Button Action(string text, EventHandler onClick, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            Font = Body,
            BackColor = primary ? Accent : Surface,
            ForeColor = primary ? Color.White : Text,
            Padding = new Padding(10, 5, 10, 5),
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.Click += onClick;
        button.EnabledChanged += (_, _) =>
        {
            button.BackColor = button.Enabled ? (primary ? Accent : Surface) : Background;
            button.ForeColor = button.Enabled ? (primary ? Color.White : Text) : TextSoft;
            button.FlatAppearance.BorderColor = button.Enabled && primary ? Accent : Border;
        };
        return button;
    }

    /// <summary>A slider with the value written beside it; <paramref name="format"/> turns the value into that text.</summary>
    public static Panel Slider(TrackBar bar, Label value, Func<int, string> format)
    {
        var host = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Margin = Padding.Empty };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62f));
        bar.Dock = DockStyle.Fill;
        bar.AutoSize = false;
        bar.Height = 26;
        bar.TickStyle = TickStyle.None;
        value.AutoSize = false;
        value.Dock = DockStyle.Fill;
        value.TextAlign = ContentAlignment.MiddleRight;
        value.Font = Body;
        value.ForeColor = TextSoft;
        bar.ValueChanged += (_, _) => value.Text = format(bar.Value);
        value.Text = format(bar.Value);
        host.Controls.Add(bar, 0, 0);
        host.Controls.Add(value, 1, 0);
        return host;
    }

    public static readonly ToolTip Tips = new() { AutoPopDelay = 12000, InitialDelay = 350, ReshowDelay = 120 };

    /// <summary>Flat, light look for the top toolbar with a clear "this tool is active" state.</summary>
    public sealed class ToolbarRenderer : ToolStripProfessionalRenderer
    {
        public ToolbarRenderer()
            : base(new Colors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Surface);
            using var pen = new Pen(Border);
            e.Graphics.DrawLine(pen, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item is not ToolStripButton button)
            {
                base.OnRenderButtonBackground(e);
                return;
            }

            var bounds = new Rectangle(1, 1, e.Item.Width - 2, e.Item.Height - 2);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (button.Checked)
            {
                using var fill = new SolidBrush(Accent);
                g.FillRectangle(fill, bounds);
            }
            else if (button.Selected && button.Enabled)
            {
                using var fill = new SolidBrush(AccentSoft);
                g.FillRectangle(fill, bounds);
            }
        }

        private sealed class Colors : ProfessionalColorTable
        {
            public override Color ToolStripBorder => Border;

            public override Color SeparatorDark => Border;

            public override Color SeparatorLight => Border;
        }
    }
}
