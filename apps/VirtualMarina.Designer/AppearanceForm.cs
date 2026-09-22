using System.Globalization;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Rendering;
using VirtualMarina.Designer.Resources;

namespace VirtualMarina.Designer;

/// <summary>
/// Water, light and motion for this marina. Everything here is saved with the design, so the host application shows the marina the
/// way it was set up instead of carrying the settings in its own code. Changes are visible in the view while the dialog is open.
/// </summary>
internal sealed class AppearanceForm : Form
{
    private readonly MarinaVisualizer _marina;
    private readonly MarinaStyle _original;

    public AppearanceForm(MarinaVisualizer marina)
    {
        _marina = marina;
        _original = Copy(marina.Style);

        Text = Strings.AppearanceTitle;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(430, 610);
        BackColor = Theme.Background;
        Font = Theme.Body;

        var stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 14, 14, 14),
        };

        stack.Controls.Add(BuildWaterCard());
        stack.Controls.Add(BuildMotionCard());
        stack.Controls.Add(BuildLightCard());
        stack.Controls.Add(BuildStatusCard());
        foreach (Control card in stack.Controls)
        {
            card.MinimumSize = new Size(380, 0);
            card.MaximumSize = new Size(380, 0);
        }

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(10, 8, 14, 10),
            BackColor = Theme.Surface,
        };
        buttons.Controls.Add(Theme.Action(Strings.AppearanceDone, (_, _) => Close(), primary: true));
        buttons.Controls.Add(Theme.Action(Strings.AppearanceCancel, (_, _) =>
        {
            _marina.Style = _original;
            Close();
        }));
        buttons.Controls.Add(Theme.Action(Strings.AppearanceReset, (_, _) =>
        {
            _marina.Style = new MarinaStyle();
            Controls.Clear();
            Close();
        }));

        Controls.Add(stack);
        Controls.Add(buttons);
    }

    private WaterSettings Water => _marina.Style.Water;

    private Panel BuildWaterCard()
    {
        var card = Theme.Card(Strings.CardWater, out var table);
        Theme.Row(table, Strings.WaveHeight, Percent(0, 60, (int)MathF.Round(Water.WaveAmplitude * 100f), v => Water.WaveAmplitude = v / 100f, v => Meters(v / 100f)));
        Theme.Row(table, Strings.WaveLength, Percent(20, 300, (int)MathF.Round(Water.WaveFrequency * 100f), v => Water.WaveFrequency = v / 100f, v => Times(v / 100f)));
        Theme.Row(table, Strings.WaveSpeed, Percent(0, 300, (int)MathF.Round(Water.WaveSpeed * 100f), v => Water.WaveSpeed = v / 100f, v => v == 0 ? Strings.ValueStill : Times(v / 100f)));
        Theme.Row(table, Strings.Reflections, Percent(0, 100, (int)MathF.Round(Water.SkyReflection * 100f), v => Water.SkyReflection = v / 100f, Percentage));
        Theme.Row(table, Strings.Ripples, Percent(0, 200, (int)MathF.Round(Water.Ripples * 100f), v => Water.Ripples = v / 100f, Percentage));
        Theme.Row(table, Strings.SunGlints, Percent(0, 200, (int)MathF.Round(Water.SunGlints * 100f), v => Water.SunGlints = v / 100f, Percentage));
        Theme.FullRow(table, ColorRow(Strings.DeepWater, () => Vector(Water.DeepColor), c => Water.DeepColor = Value(c)));
        Theme.FullRow(table, ColorRow(Strings.ShallowWater, () => Vector(Water.ShallowColor), c => Water.ShallowColor = Value(c)));
        return card;
    }

    private Panel BuildMotionCard()
    {
        var card = Theme.Card(Strings.CardBoats, out var table);
        Theme.Row(table, Strings.BoatMovement, Percent(0, 300, (int)MathF.Round(Water.BoatMotion * 100f), v => Water.BoatMotion = v / 100f, v => v == 0 ? Strings.ValueStill : Percentage(v)));
        Theme.FullRow(table, Theme.Hint(Strings.BoatMovementHint));
        return card;
    }

    private Panel BuildLightCard()
    {
        var card = Theme.Card(Strings.CardLight, out var table);
        var lighting = _marina.Style.Lighting;
        var azimuth = 0f;
        var elevation = 40f;
        Theme.Row(table, Strings.SunDirection, Percent(0, 359, (int)azimuth, v =>
        {
            azimuth = v;
            lighting.SetSunAngles(azimuth, elevation);
            _marina.InvalidateScene();
        }, v => Strings.Format(Strings.ValueDegreesFromNorth, v)));
        Theme.Row(table, Strings.SunHeight, Percent(5, 89, (int)elevation, v =>
        {
            elevation = v;
            lighting.SetSunAngles(azimuth, elevation);
            _marina.InvalidateScene();
        }, v => Strings.Format(Strings.ValueDegreesAboveHorizon, v)));
        Theme.Row(table, Strings.Haze, Percent(0, 100, (int)MathF.Round(lighting.FogDensity * 20000f), v => lighting.FogDensity = v / 20000f, Percentage));
        Theme.FullRow(table, ColorRow(Strings.Sky, () => Vector(lighting.SkyColor), c => lighting.SkyColor = Value(c)));
        Theme.FullRow(table, ColorRow(Strings.Horizon, () => Vector(lighting.FogColor), c => lighting.FogColor = Value(c)));
        return card;
    }

    private Panel BuildStatusCard()
    {
        var card = Theme.Card(Strings.CardBerthColors, out var table);
        var status = _marina.Style.Status;
        Theme.FullRow(table, ColorRow(Strings.ColorFree, () => status.FreeColor, c => status.FreeColor = c));
        Theme.FullRow(table, ColorRow(Strings.ColorOccupied, () => status.OccupiedColor, c => status.OccupiedColor = c));
        Theme.FullRow(table, ColorRow(Strings.ColorReserved, () => status.ReservedColor, c => status.ReservedColor = c));
        Theme.FullRow(table, ColorRow(Strings.ColorOwnerAway, () => status.TemporarilyFreeColor, c => status.TemporarilyFreeColor = c));
        Theme.Row(table, Strings.PadStrength, Percent(0, 100, (int)MathF.Round(status.PadOpacity * 100f), v => status.PadOpacity = v / 100f, Percentage));
        var markers = Theme.Check(Strings.ShowStatusBuoys);
        markers.Checked = status.ShowStatusMarkers;
        markers.CheckedChanged += (_, _) => status.ShowStatusMarkers = markers.Checked;
        Theme.FullRow(table, markers);
        return card;
    }

    private static string Percentage(int value) => Strings.Format(Strings.Percent, value);

    private static string Meters(float value) => Strings.Format(Strings.ValueMeters, value);

    private static string Times(float value) => Strings.Format(Strings.ValueTimes, value);

    /// <summary>A slider that reports whole numbers and writes the value beside it.</summary>
    private static Panel Percent(int min, int max, int value, Action<int> apply, Func<int, string> format)
    {
        var bar = new TrackBar { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max) };
        bar.ValueChanged += (_, _) => apply(bar.Value);
        return Theme.Slider(bar, new Label(), format);
    }

    /// <summary>A color swatch that opens the color picker.</summary>
    private Panel ColorRow(string label, Func<ColorRgba> get, Action<ColorRgba> set)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 3, 0, 3) };
        var swatch = new Panel { Width = 42, Height = 22, BackColor = ToColor(get()), BorderStyle = BorderStyle.FixedSingle, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0) };
        var caption = new Label { Text = label, AutoSize = true, Margin = new Padding(0, 4, 0, 0), ForeColor = Theme.Text };
        swatch.Click += (_, _) =>
        {
            using var picker = new ColorDialog { Color = swatch.BackColor, FullOpen = true, AnyColor = true };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            set(FromColor(picker.Color));
            swatch.BackColor = picker.Color;
            _marina.InvalidateScene();
        };

        row.Controls.Add(swatch);
        row.Controls.Add(caption);
        return row;
    }

    private static Color ToColor(ColorRgba color) => Color.FromArgb(Byte(color.R), Byte(color.G), Byte(color.B));

    private static ColorRgba FromColor(Color color) => new(color.R / 255f, color.G / 255f, color.B / 255f);

    private static byte Byte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    private static ColorRgba Vector(System.Numerics.Vector3 color) => new(color.X, color.Y, color.Z);

    private static System.Numerics.Vector3 Value(ColorRgba color) => new(color.R, color.G, color.B);

    /// <summary>A copy of the style, so Cancel can put everything back.</summary>
    private static MarinaStyle Copy(MarinaStyle style)
    {
        var copy = new MarinaStyle
        {
            Water = new WaterSettings
            {
                Size = style.Water.Size,
                GridResolution = style.Water.GridResolution,
                DeepColor = style.Water.DeepColor,
                ShallowColor = style.Water.ShallowColor,
                WaveAmplitude = style.Water.WaveAmplitude,
                WaveFrequency = style.Water.WaveFrequency,
                WaveSpeed = style.Water.WaveSpeed,
                SkyReflection = style.Water.SkyReflection,
                Ripples = style.Water.Ripples,
                SunGlints = style.Water.SunGlints,
                BoatMotion = style.Water.BoatMotion,
            },
            Lighting = new LightingSettings
            {
                SunDirection = style.Lighting.SunDirection,
                SunColor = style.Lighting.SunColor,
                AmbientColor = style.Lighting.AmbientColor,
                SpecularStrength = style.Lighting.SpecularStrength,
                Shininess = style.Lighting.Shininess,
                SkyColor = style.Lighting.SkyColor,
                FogColor = style.Lighting.FogColor,
                FogDensity = style.Lighting.FogDensity,
            },
            View = new ViewStyle
            {
                FieldOfViewDegrees = style.View.FieldOfViewDegrees,
                CameraSmoothing = style.View.CameraSmoothing,
            },
        };

        copy.Status.FreeColor = style.Status.FreeColor;
        copy.Status.OccupiedColor = style.Status.OccupiedColor;
        copy.Status.ReservedColor = style.Status.ReservedColor;
        copy.Status.TemporarilyFreeColor = style.Status.TemporarilyFreeColor;
        copy.Status.DisabledColor = style.Status.DisabledColor;
        copy.Status.PadOpacity = style.Status.PadOpacity;
        copy.Status.ShowStatusMarkers = style.Status.ShowStatusMarkers;
        copy.Status.StatusMarkerScale = style.Status.StatusMarkerScale;
        copy.Land.ShowTrees = style.Land.ShowTrees;
        return copy;
    }
}

/// <summary>A one-line question with a text box, for renaming the marina.</summary>
internal sealed class TextInputForm : Form
{
    private readonly TextBox _box = new() { Dock = DockStyle.Top, Font = Theme.Body, BorderStyle = BorderStyle.FixedSingle, Height = 26 };

    public TextInputForm(string title, string question, string value)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(380, 130);
        BackColor = Theme.Surface;
        Font = Theme.Body;
        _box.Text = value;

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 16, 16, 8) };
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
}
