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

    /// <summary>
    /// The names the rows were last built for, so an unchanged list rebuilds nothing. Null until they have been
    /// built at all, which an empty list would otherwise look exactly like.
    /// </summary>
    private string? _automaticRows;
    private string? _savedRows;

    /// <summary>The tick of each automatic view, by name, so they can be brought in line without a rebuild.</summary>
    private readonly Dictionary<string, CheckBox> _ticks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when a sync actually added or removed a row, so only then is a layout worth doing.</summary>
    private bool _rowsChanged;

    private TableLayoutPanel _header = null!;

    /// <summary>
    /// Shows or hides the panel by its height rather than by <see cref="Control.Visible"/>.
    /// </summary>
    /// <remarks>
    /// WinForms does not lay out a hidden control, so hiding one throws its layout away and showing it again works
    /// the whole tree out afresh — most of a second on a panel with a few hundred nested auto-sized controls.
    /// Collapsing to nothing leaves it laid out, and the swap becomes a resize.
    /// </remarks>
    public bool Collapsed
    {
        get => _collapsed;
        set
        {
            _collapsed = value;
            if (value) Height = 0;
            else ContentChanged();
        }
    }

    /// <summary>How tall the panel wants to be: its heading plus its cards. Measured once per width.</summary>
    private int ContentHeight
    {
        get
        {
            if (_contentHeight <= 0)
            {
                _contentHeight = _header.PreferredSize.Height + _stack.PreferredSize.Height;
                _measuredWidth = Width;
            }

            return _contentHeight;
        }
    }

    /// <summary>Measures again, after something changed how much there is to show or how wide it is shown in.</summary>
    private void ContentChanged()
    {
        _contentHeight = 0;
        if (!_collapsed) Height = ContentHeight;
    }

    /// <summary>A panel shown in a different width wraps differently, so its height has to be worked out again.</summary>
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (_collapsed || Width == _measuredWidth) return;
        ContentChanged();
    }

    private bool _collapsed;
    private int _contentHeight;
    private int _measuredWidth = -1;


    /// <summary>Creates the panel over a visualizer.</summary>
    /// <param name="marina">The marina whose views are being managed.</param>
    /// <param name="log">Where to note what happened, for the activity log.</param>
    public CamerasPanel(MarinaVisualizer marina, Action<string> log)
    {
        _marina = marina;
        _log = log;
        BackColor = Theme.Background;
        Dock = DockStyle.Fill;

        _header = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            BackColor = Theme.Background,
            Padding = new Padding(12, 12, 12, 0),
        };
        var header = _header;
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

        // The height is ours to set, so that collapsing to nothing can stand in for hiding.
        AutoSize = false;
        Height = ContentHeight;
    }

    /// <summary>Rebuilds the two lists from the marina, e.g. after a pier was added or a view saved.</summary>
    public void Sync()
    {
        if (_updating) return;
        _updating = true;

        // Suspend the whole panel, not just the tables: adding a row to a live table lays out every container above
        // it as well, which is most of the cost of putting one view in the list.
        SuspendLayout();
        _stack.SuspendLayout();
        _rowsChanged = false;
        try
        {
            var presets = _marina.CameraPresets;
            Fill(_automatic, presets.Where(preset => preset.IsBuiltIn).ToList(), automatic: true);
            Fill(_saved, presets.Where(preset => !preset.IsBuiltIn).ToList(), automatic: false);
        }
        finally
        {
            _updating = false;
            _stack.ResumeLayout(performLayout: false);

            // Nothing moved on most refreshes, and laying out anyway is the whole cost of them.
            ResumeLayout(performLayout: _rowsChanged);
            if (_rowsChanged) ContentChanged();
        }
    }

    /// <summary>
    /// One row per view: a tick for the automatic ones, a name that goes there, and a way to remove a saved one.
    /// </summary>
    /// <remarks>
    /// The rows are left alone unless the list of views has actually changed. Rebuilding them is the expensive part
    /// of this panel, and it used to happen on every refresh; now saving a view costs one new row rather than all of
    /// them.
    /// </remarks>
    private void Fill(TableLayoutPanel table, IReadOnlyList<CameraPreset> presets, bool automatic)
    {
        // What the rows would have to say. Unchanged means there is nothing to do but tick the boxes.
        var names = presets.Select(preset => preset.Name).ToList();
        var wanted = string.Join("\u001f", names);
        var built = automatic ? _automaticRows : _savedRows;
        if (built is not null && string.Equals(built, wanted, StringComparison.Ordinal))
        {
            if (automatic) UpdateTicks(presets);
            return;
        }

        // Saving a view leaves the rows already there untouched and adds one, which is the common case and the one
        // that used to cost a rebuild of the whole list.
        var keep = CommonPrefix(built ?? string.Empty, wanted);
        _rowsChanged = true;
        table.SuspendLayout();

        if (keep == 0)
        {
            while (table.Controls.Count > 0)
            {
                var old = table.Controls[0];
                table.Controls.Remove(old);
                old.Dispose();
            }

            table.RowStyles.Clear();
            table.RowCount = 0;
            if (automatic) _ticks.Clear();
        }
        else
        {
            // Drop only the tail that no longer matches.
            while (table.RowCount > keep)
            {
                var last = table.GetControlFromPosition(0, table.RowCount - 1);
                if (last is not null)
                {
                    if (automatic) _ticks.Remove(last.Tag as string ?? string.Empty);
                    table.Controls.Remove(last);
                    last.Dispose();
                }

                table.RowStyles.RemoveAt(table.RowCount - 1);
                table.RowCount--;
            }
        }

        if (automatic) _automaticRows = wanted;
        else _savedRows = wanted;

        if (presets.Count == 0)
        {
            Theme.FullRow(table, Theme.Hint(Strings.CameraNoneSaved));
            table.ResumeLayout();
            return;
        }

        foreach (var preset in presets.Skip(keep))
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
            row.Tag = preset.Name;

            var name = preset.Name;
            if (automatic)
            {
                var tick = Theme.Check(name);
                tick.Checked = preset.IsEnabled;
                tick.CheckedChanged += (_, _) => SetEnabled(name, tick.Checked);
                Theme.Tips.SetToolTip(tick, preset.Description ?? name);
                row.Controls.Add(tick, 0, 0);
                _ticks[name] = tick;
            }
            else
            {
                var label = new Label { Text = name, AutoSize = true, ForeColor = Theme.Text, Font = Theme.Body, Margin = new Padding(3, 5, 3, 3) };
                Theme.Tips.SetToolTip(label, preset.Description ?? name);
                row.Controls.Add(label, 0, 0);
            }

            var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, WrapContents = false };
            // The row asks for the view by name when it is pressed rather than carrying the one it was built from.
            // Automatic views are worked out afresh from the layout every time it changes, so a row built for an
            // earlier layout that still holds its own copy sends the camera to where the marina used to be. Asking
            // for it by name also keeps a saved view named after an automatic one distinct from it: a row in the
            // automatic list asks for the automatic one, a row in the saved list gets the saved one first.
            var goTo = automatic
                ? new EventHandler((_, _) => _marina.ApplyBuiltInCameraPreset(name))
                : new EventHandler((_, _) => _marina.ApplyCameraPreset(name));
            buttons.Controls.Add(Theme.Icon(Strings.CameraGoToGlyph, Strings.CameraGoToTip, goTo));
            if (!automatic)
            {
                buttons.Controls.Add(Theme.Icon(Strings.CameraDeleteGlyph, Strings.CameraDeleteTip, (_, _) => Delete(name), danger: true));
            }

            row.Controls.Add(buttons, 1, 0);

            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(row, 0, table.RowCount++);
            table.SetColumnSpan(row, table.ColumnCount);
        }

        table.ResumeLayout();
    }

    /// <summary>
    /// How many rows at the start of the list are already right, comparing the names the rows were built for with
    /// the names wanted now. Both are the names joined by a separator that cannot appear in one.
    /// </summary>
    private static int CommonPrefix(string built, string wanted)
    {
        if (built.Length == 0 || wanted.Length == 0) return 0;

        var before = built.Split('\u001f');
        var after = wanted.Split('\u001f');
        var shared = 0;
        while (shared < before.Length && shared < after.Length && string.Equals(before[shared], after[shared], StringComparison.Ordinal))
        {
            shared++;
        }

        return shared;
    }

    /// <summary>Brings the ticks in line with which views are offered, without touching the rows themselves.</summary>
    private void UpdateTicks(IReadOnlyList<CameraPreset> presets)
    {
        foreach (var preset in presets)
        {
            if (_ticks.TryGetValue(preset.Name, out var tick) && tick.Checked != preset.IsEnabled)
            {
                tick.Checked = preset.IsEnabled;
            }
        }
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
