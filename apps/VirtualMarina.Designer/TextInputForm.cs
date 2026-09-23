using VirtualMarina.Designer.Resources;

namespace VirtualMarina.Designer;

/// <summary>A one-line question with a text box, for renaming the marina.</summary>
internal sealed class TextInputForm : Form
{
    private readonly TextBox _box = new() { Dock = DockStyle.Top, Font = Theme.Body, BorderStyle = BorderStyle.FixedSingle, Height = 26 };
    private readonly TextBox _second = new() { Dock = DockStyle.Top, Font = Theme.Body, BorderStyle = BorderStyle.FixedSingle, Height = 26 };
    private readonly TextBox _third = new() { Dock = DockStyle.Top, Font = Theme.Body, BorderStyle = BorderStyle.FixedSingle, Height = 26 };

    /// <summary>Asks for one value, or for more as the later questions are given.</summary>
    /// <param name="title">Dialog caption.</param>
    /// <param name="question">Label above the first field.</param>
    /// <param name="value">Initial text of the first field.</param>
    /// <param name="secondQuestion">Label above the second field, or null for a one-field dialog.</param>
    /// <param name="secondValue">Initial text of the second field.</param>
    /// <param name="thirdQuestion">Label above the third field, or null to leave it out.</param>
    /// <param name="thirdValue">Initial text of the third field.</param>
    /// <param name="thirdHint">A line of explanation under the last field shown, or null for none.</param>
    public TextInputForm(
        string title,
        string question,
        string value,
        string? secondQuestion = null,
        string? secondValue = null,
        string? thirdQuestion = null,
        string? thirdValue = null,
        string? thirdHint = null)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        var rows = 1 + (secondQuestion is null ? 0 : 1) + (thirdQuestion is null ? 0 : 1);
        ClientSize = new Size(400, 74 + rows * 56 + (thirdHint is null ? 0 : 34));
        BackColor = Theme.Surface;
        Font = Theme.Body;
        _box.Text = value;
        _second.Text = secondValue ?? string.Empty;
        _third.Text = thirdValue ?? string.Empty;

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 16, 16, 8) };

        // Docked top, so they stack in the reverse order they are added: the hint first, since it goes below
        // whichever field turns out to be the last one.
        if (thirdHint is not null)
        {
            content.Controls.Add(new Label { Text = thirdHint, Dock = DockStyle.Top, AutoSize = true, ForeColor = Theme.TextSoft, Margin = new Padding(0, 4, 0, 8), Height = 30, MaximumSize = new Size(360, 0) });
        }

        if (thirdQuestion is not null)
        {
            content.Controls.Add(_third);
            content.Controls.Add(new Label { Text = thirdQuestion, Dock = DockStyle.Top, AutoSize = true, ForeColor = Theme.TextSoft, Margin = new Padding(0, 8, 0, 8), Height = 22 });
        }

        if (secondQuestion is not null)
        {
            content.Controls.Add(_second);
            content.Controls.Add(new Label { Text = secondQuestion, Dock = DockStyle.Top, AutoSize = true, ForeColor = Theme.TextSoft, Margin = new Padding(0, 8, 0, 8), Height = 22 });
        }

        content.Controls.Add(_box);
        content.Controls.Add(new Label { Text = question, Dock = DockStyle.Top, AutoSize = true, ForeColor = Theme.TextSoft, Margin = new Padding(0, 0, 0, 8), Height = 22 });

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(10, 6, 14, 10) };
        var ok = Theme.Action(Strings.DialogOk, (_, _) => { DialogResult = DialogResult.OK; Close(); }, primary: true);
        var cancel = Theme.Action(Strings.DialogCancel, (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        Controls.Add(content);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string Value => _box.Text;

    /// <summary>Text of the second field, empty for a one-field dialog.</summary>
    public string SecondValue => _second.Text;

    /// <summary>Text of the third field, empty when it was left out.</summary>
    public string ThirdValue => _third.Text;
}
