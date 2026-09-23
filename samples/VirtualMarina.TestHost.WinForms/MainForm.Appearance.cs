using System.Globalization;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.TestHost.WinForms;

/// <summary>The "Appearance" tab: live controls for <c>marinaView.Style</c> (waves, reflections, opacities, colors, trees, ...).</summary>
public partial class MainForm
{
    private readonly List<Action> _refreshAppearance = [];
    private bool _updatingAppearance;

    private TabPage CreateAppearancePage()
    {
        var page = new TabPage("Appearance") { Padding = new Padding(3), AutoScroll = true };
        var table = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(4) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48f));

        MarinaStyle Style() => marinaView.Style;

        Header(table, "Water and waves");
        Slider(table, "Wave height (m)", 0f, 0.4f, () => Style().Water.WaveAmplitude, v => Style().Water.WaveAmplitude = v);
        Slider(table, "Wave frequency", 0.2f, 3f, () => Style().Water.WaveFrequency, v => Style().Water.WaveFrequency = v);
        Slider(table, "Wave speed", 0f, 3f, () => Style().Water.WaveSpeed, v => Style().Water.WaveSpeed = v);
        Slider(table, "Sky reflections", 0f, 1f, () => Style().Water.SkyReflection, v => Style().Water.SkyReflection = v);
        Slider(table, "Ripples", 0f, 2f, () => Style().Water.Ripples, v => Style().Water.Ripples = v);
        Slider(table, "Sun glints", 0f, 2f, () => Style().Water.SunGlints, v => Style().Water.SunGlints = v);
        Slider(table, "Boat motion", 0f, 3f, () => Style().Water.BoatMotion, v => Style().Water.BoatMotion = v);
        Slider(table, "Fog", 0f, 0.006f, () => Style().Lighting.FogDensity, v => Style().Lighting.FogDensity = v, "0.0000");

        Header(table, "Berths and boats");
        Slider(table, "Pad opacity", 0f, 1f, () => Style().Status.PadOpacity, v => Style().Status.PadOpacity = v);
        Slider(table, "Occupied boats", 0f, 1f, () => Style().Status.OccupiedBoatOpacity, v => Style().Status.OccupiedBoatOpacity = v);
        Slider(table, "Reserved boats", 0f, 1f, () => Style().Status.ReservedBoatOpacity, v => Style().Status.ReservedBoatOpacity = v);
        Slider(table, "Temp. free boats", 0f, 1f, () => Style().Status.TemporarilyFreeBoatOpacity, v => Style().Status.TemporarilyFreeBoatOpacity = v);
        Slider(table, "Marker size", 0.2f, 3f, () => Style().Status.StatusMarkerScale, v => Style().Status.StatusMarkerScale = v);

        var colors = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill };
        foreach (var status in Enum.GetValues<BerthStatus>()) colors.Controls.Add(ColorButton(status));
        FullRow(table, colors);

        var checks = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill };
        checks.Controls.Add(Check("Status buoys", () => Style().Status.ShowStatusMarkers, v => Style().Status.ShowStatusMarkers = v));
        checks.Controls.Add(Check("Selection marker", () => Style().Selection.ShowMarker, v => Style().Selection.ShowMarker = v));
        checks.Controls.Add(Check("Pulse selection", () => Style().Selection.Pulse, v => Style().Selection.Pulse = v));
        checks.Controls.Add(Check("Trees", () => Style().Land.ShowTrees, v => Style().Land.ShowTrees = v));
        FullRow(table, checks);

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill };
        buttons.Controls.Add(CreateButton("Calm, clear water", (_, _) =>
        {
            var water = Style().Water;
            water.WaveAmplitude = 0.02f;
            water.SkyReflection = 0.25f;
            water.Ripples = 0.3f;
            water.SunGlints = 0.3f;
            water.BoatMotion = 0.3f;
            RefreshAppearance();
        }));
        buttons.Controls.Add(CreateButton("Reset style", (_, _) =>
        {
            marinaView.Style = new MarinaStyle();
            RefreshAppearance();
        }));
        FullRow(table, buttons);

        page.Controls.Add(table);
        RefreshAppearance();
        return page;
    }

    private void RefreshAppearance()
    {
        _updatingAppearance = true;
        try
        {
            foreach (var refresh in _refreshAppearance) refresh();
        }
        finally
        {
            _updatingAppearance = false;
        }
    }

    private static void Header(TableLayoutPanel table, string text)
    {
        var label = new Label { Text = text, AutoSize = true, Font = new Font(table.Font, FontStyle.Bold), Margin = new Padding(0, 10, 0, 2) };
        FullRow(table, label);
    }

    private static void FullRow(TableLayoutPanel table, Control control)
    {
        var row = table.RowCount++;
        table.Controls.Add(control, 0, row);
        table.SetColumnSpan(control, 3);
    }

    private void Slider(TableLayoutPanel table, string caption, float min, float max, Func<float> get, Action<float> set, string format = "0.00")
    {
        const int steps = 100;
        var track = new TrackBar { Minimum = 0, Maximum = steps, TickStyle = TickStyle.None, AutoSize = false, Height = 26, Dock = DockStyle.Fill };
        var value = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 0) };
        track.ValueChanged += (_, _) =>
        {
            var v = min + (max - min) * track.Value / steps;
            value.Text = v.ToString(format, CultureInfo.CurrentCulture);
            if (!_updatingAppearance) set(v);
        };
        _refreshAppearance.Add(() =>
        {
            track.Value = Math.Clamp((int)MathF.Round((get() - min) / (max - min) * steps), 0, steps);
            value.Text = get().ToString(format, CultureInfo.CurrentCulture);
        });

        var row = table.RowCount++;
        table.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) }, 0, row);
        table.Controls.Add(track, 1, row);
        table.Controls.Add(value, 2, row);
    }

    private CheckBox Check(string caption, Func<bool> get, Action<bool> set)
    {
        var box = new CheckBox { Text = caption, AutoSize = true };
        box.CheckedChanged += (_, _) =>
        {
            if (!_updatingAppearance) set(box.Checked);
        };
        _refreshAppearance.Add(() => box.Checked = get());
        return box;
    }

    private Button ColorButton(BerthStatus status)
    {
        var button = new Button { Text = status.GetDisplayName(), AutoSize = true, FlatStyle = FlatStyle.Flat };
        void Paint()
        {
            var c = marinaView.Style.Status.Get(status);
            button.FlatAppearance.BorderColor = Color.FromArgb((int)(c.R * 255), (int)(c.G * 255), (int)(c.B * 255));
            button.FlatAppearance.BorderSize = 3;
        }

        button.Click += (_, _) =>
        {
            var current = marinaView.Style.Status.Get(status);
            using var dialog = new ColorDialog { Color = Color.FromArgb((int)(current.R * 255), (int)(current.G * 255), (int)(current.B * 255)), FullOpen = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            marinaView.Style.Status.Set(status, ColorRgba.FromBytes(dialog.Color.R, dialog.Color.G, dialog.Color.B));
            Paint();
        };
        _refreshAppearance.Add(Paint);
        return button;
    }
}
