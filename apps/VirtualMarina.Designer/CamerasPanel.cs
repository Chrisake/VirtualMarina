using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Designer.Resources;

namespace VirtualMarina.Designer;

/// <summary>
/// The views of the marina: the ones worked out from the layout, the ones the designer saved, and which of them the
/// host application should offer.
/// </summary>
/// <remarks>
/// The automatic views are rebuilt whenever the layout changes, so this panel rebuilds its rows from
/// <see cref="IMarinaVisualizer.CameraPresets"/> rather than holding on to them.
/// </remarks>
internal sealed class CamerasPanel : UserControl
{
    private readonly MarinaVisualizer _marina;
    private readonly Action<string> _log;

    private readonly TableLayoutPanel _stack = new()
    {
        ColumnCount = 1,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Dock = DockStyle.Top,
        BackColor = Theme.Background,
        Padding = new Padding(12, 12, 12, 12),
        GrowStyle = TableLayoutPanelGrowStyle.AddRows,
    };

    private readonly Panel _scroller = new()
    {
        AutoScroll = true,
        Dock = DockStyle.Fill,
        BackColor = Theme.Background,
    };

    private readonly TextBox _name = Theme.Field();
    private readonly TableLayoutPanel _automatic;
    private readonly TableLayoutPanel _saved;
    private readonly Label _noneSaved = Theme.Hint(Strings.CameraNoneSaved);

    private bool _updating;

    /// <summary>Creates the panel over a visualizer.</summary>
    /// <param name="marina">The marina whose views are being managed.</param>
    /// <param name="log">Where to note what happened, for the activity log.</param>
    public CamerasPanel(MarinaVisualizer marina, Action<string> log)
    {
        _marina = marina;
        _log = log;
        BackColor = Theme.Background;
        Dock = DockStyle.Fill;

        var header = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            BackColor = Theme.Background,
            Padding = new Padding(12, 12, 12, 0),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        header.Controls.Add(new Label { Text = Strings.TitleCameras, Font = new Font("Segoe UI Semibold", 13f), ForeColor = Theme.Text, AutoSize = true, Dock = DockStyle.Fill }, 0, 0);
        header.Controls.Add(new Label { Text = Strings.CamerasHint, Font = Theme.Body, ForeColor = Theme.TextSoft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) }, 0, 1);

        var saveCard = Theme.Card(Strings.CardCameraSave, out var saveTable);
        Theme.Row(saveTable, Strings.CameraName, _name, Strings.CameraNameTip);
        Theme.FullRow(saveTable, Theme.Action(Strings.CameraSave, (_, _) => Save(), primary: true));
        Theme.FullRow(saveTable, Theme.Hint(Strings.CameraSaveHint));

        var automaticCard = Theme.Card(Strings.CardCameraAutomatic, out _automatic);
        var savedCard = Theme.Card(Strings.CardCameraSaved, out _saved);

        _stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        foreach (var card in new[] { saveCard, automaticCard, savedCard })
        {
            card.Dock = DockStyle.Top;
            _stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _stack.Controls.Add(card, 0, _stack.RowCount++);
        }

        _scroller.Controls.Add(_stack);
        Controls.Add(_scroller);
        Controls.Add(header);
        Sync();
    }

    /// <summary>
    /// Hands the scrolling to a panel outside this one, so several of these can share a single scrollbar. This panel
    /// then sizes to its content instead of to the space it is given, and scrolls away with everything else.
    /// </summary>
    public void UseOuterScrolling()
    {
        // The header is added after the scroller, so it still docks above it once both are Top.
        _scroller.AutoScroll = false;
        _scroller.Dock = DockStyle.Top;
        _scroller.AutoSize = true;
        _scroller.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    /// <summary>Rebuilds the two lists from the marina, e.g. after a pier was added or a view saved.</summary>
    public void Sync()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            Fill(_automatic, _marina.CameraPresets.Where(preset => preset.IsBuiltIn).ToList(), automatic: true);
            Fill(_saved, _marina.CameraPresets.Where(preset => !preset.IsBuiltIn).ToList(), automatic: false);
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>One row per view: a tick for the automatic ones, a name that goes there, and a way to remove a saved one.</summary>
    private void Fill(TableLayoutPanel table, IReadOnlyList<CameraPreset> presets, bool automatic)
    {
        table.SuspendLayout();
        while (table.Controls.Count > 0)
        {
            var old = table.Controls[0];
            table.Controls.Remove(old);
            old.Dispose();
        }

        table.RowStyles.Clear();
        table.RowCount = 0;

        if (presets.Count == 0)
        {
            Theme.FullRow(table, Theme.Hint(Strings.CameraNoneSaved));
            table.ResumeLayout();
            return;
        }

        foreach (var preset in presets)
        {
            var row = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 1, 0, 1),
                BackColor = Theme.Surface,
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var name = preset.Name;
            if (automatic)
            {
                var tick = Theme.Check(name);
                tick.Checked = preset.IsEnabled;
                tick.CheckedChanged += (_, _) => SetEnabled(name, tick.Checked);
                Theme.Tips.SetToolTip(tick, preset.Description ?? name);
                row.Controls.Add(tick, 0, 0);
            }
            else
            {
                var label = new Label { Text = name, AutoSize = true, ForeColor = Theme.Text, Font = Theme.Body, Margin = new Padding(3, 7, 3, 3) };
                Theme.Tips.SetToolTip(label, preset.Description ?? name);
                row.Controls.Add(label, 0, 0);
            }

            var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, WrapContents = false };
            var go = Theme.Action(Strings.CameraGoTo, (_, _) => _marina.ApplyCameraPreset(name));
            Theme.Tips.SetToolTip(go, Strings.CameraGoToTip);
            buttons.Controls.Add(go);

            if (!automatic)
            {
                var remove = Theme.Action(Strings.CameraDelete, (_, _) => Delete(name));
                Theme.Tips.SetToolTip(remove, Strings.CameraDeleteTip);
                buttons.Controls.Add(remove);
            }

            row.Controls.Add(buttons, 1, 0);

            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(row, 0, table.RowCount++);
            table.SetColumnSpan(row, table.ColumnCount);
        }

        table.ResumeLayout();
    }

    private void SetEnabled(string name, bool enabled)
    {
        if (_updating) return;
        _marina.SetCameraPresetEnabled(name, enabled);
        _log(Strings.Format(enabled ? Strings.LogCameraEnabled : Strings.LogCameraDisabled, name));
    }

    /// <summary>Saves where the camera is now, under the typed name or a made-up one.</summary>
    private void Save()
    {
        var wanted = _name.Text.Trim();
        if (wanted.Length == 0)
        {
            var taken = _marina.CameraPresets.Count(preset => !preset.IsBuiltIn);
            wanted = Strings.Format(Strings.CameraDefaultName, taken + 1);
        }

        _marina.SaveCameraPreset(wanted);
        _name.Text = string.Empty;
        _log(Strings.Format(Strings.LogCameraSaved, wanted));
        Sync();
    }

    private void Delete(string name)
    {
        if (!_marina.RemoveCameraPreset(name)) return;
        _log(Strings.Format(Strings.LogCameraDeleted, name));
        Sync();
    }
}
