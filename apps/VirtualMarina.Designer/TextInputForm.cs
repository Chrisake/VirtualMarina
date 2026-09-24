using VirtualMarina.Designer.Resources;

namespace VirtualMarina.Designer;

/// <summary>
/// A small dialog asking for one or more lines of text: the marina's name, or a new name, id and berth pattern for
/// something being renamed. It sizes itself to what it asks, so it reads the same at any display scaling.
/// </summary>
internal sealed class TextInputForm : Form
{
    private readonly List<TextBox> _boxes = [];

    /// <summary>Builds the dialog for a prompt.</summary>
    /// <param name="prompt">The title and the fields to ask for.</param>
    public TextInputForm(DesignerPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = prompt.Title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Theme.Surface;
        Font = Theme.Body;

        var content = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 16, 16, 8),
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 368f));

        foreach (var field in prompt.Fields)
        {
            content.Controls.Add(new Label
            {
                Text = field.Label,
                AutoSize = true,
                MaximumSize = new Size(368, 0),
                ForeColor = Theme.TextSoft,
                Margin = new Padding(0, _boxes.Count == 0 ? 0 : 10, 0, 6),
            });

            var box = new TextBox { Text = field.Value, Dock = DockStyle.Top, BorderStyle = BorderStyle.FixedSingle, Margin = Padding.Empty };
            box.AccessibleName = field.Label;
            _boxes.Add(box);
            content.Controls.Add(box);

            if (field.Hint is not null)
            {
                content.Controls.Add(new Label
                {
                    Text = field.Hint,
                    AutoSize = true,
                    MaximumSize = new Size(368, 0),
                    ForeColor = Theme.TextSoft,
                    Font = Theme.Small,
                    Margin = new Padding(0, 4, 0, 0),
                });
            }
        }

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 6, 14, 10),
        };
        var ok = Theme.Action(Strings.DialogOk, (_, _) => Close(), primary: true);
        ok.DialogResult = DialogResult.OK;
        var cancel = Theme.Action(Strings.DialogCancel, (_, _) => Close());
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        Controls.Add(content);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    /// <summary>What the fields hold, in the order they were asked.</summary>
    public IReadOnlyList<string> Values => _boxes.Select(box => box.Text).ToList();

    /// <summary>Starts with the first field's text selected, ready to be typed over.</summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_boxes.Count == 0) return;
        _boxes[0].Focus();
        _boxes[0].SelectAll();
    }
}
