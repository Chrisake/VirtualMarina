using System.Globalization;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Rendering;
using VirtualMarina.Designer.Resources;

namespace VirtualMarina.Designer;

/// <summary>
/// How the marina is drawn: water, light, colours and labels. It lives beside the view rather than in a dialog, so
/// the marina can be turned and zoomed while the settings are being changed and the effect is visible at once.
/// </summary>
/// <remarks>
/// Every setting has a small reset button of its own, next to the full reset at the bottom, so one colour can be put
/// back without losing the rest. The preview boats exist only to judge the settings against; they are never saved.
/// </remarks>
internal sealed class AppearancePanel : UserControl
{
    /// <summary>A fresh style, read whenever a single setting is put back to its default.</summary>
    private static readonly MarinaStyle Defaults = new();

    private readonly MarinaVisualizer _marina;
    private readonly Action<string> _log;
    private readonly TableLayoutPanel _stack = new()
    {
        ColumnCount = 1,
        AutoScroll = true,
        Dock = DockStyle.Fill,
        BackColor = Theme.Background,
        Padding = new Padding(12, 12, 12, 12),
        GrowStyle = TableLayoutPanelGrowStyle.AddRows,
    };

    private readonly TrackBar _fill = new() { Minimum = 0, Maximum = 100, Value = 60 };
    private readonly Label _fillValue = new();
    private readonly Random _random = new();

    /// <summary>Creates the panel over a visualizer.</summary>
    /// <param name="marina">The marina whose look is being changed.</param>
    /// <param name="log">Where to note what happened, for the activity log.</param>
    public AppearancePanel(MarinaVisualizer marina, Action<string> log)
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
        header.Controls.Add(new Label { Text = Strings.TitleLook, Font = new Font("Segoe UI Semibold", 13f), ForeColor = Theme.Text, AutoSize = true, Dock = DockStyle.Fill }, 0, 0);
        header.Controls.Add(new Label { Text = Strings.LookHint, Font = Theme.Body, ForeColor = Theme.TextSoft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) }, 0, 1);

        _stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        foreach (var card in new[] { BuildWaterCard(), BuildBoatsCard(), BuildLightCard(), BuildStatusCard(), BuildLandCard(), BuildLabelCard(), BuildPreviewCard(), BuildResetCard() })
        {
            card.Dock = DockStyle.Top;
            _stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _stack.Controls.Add(card, 0, _stack.RowCount++);
        }

        _stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _stack.RowCount++;

        Controls.Add(_stack);
        Controls.Add(header);
    }

    private WaterSettings Water => _marina.Style.Water;

    private LightingSettings Lighting => _marina.Style.Lighting;

    private LandStyle Land => _marina.Style.Land;

    // ---- Cards --------------------------------------------------------------------------------

    private Panel BuildWaterCard()
    {
        var card = Theme.Card(Strings.CardWater, out var table);
        Percent(table, Strings.WaveHeight, 0, 60, () => Water.WaveAmplitude * 100f, v => Water.WaveAmplitude = v / 100f, Defaults.Water.WaveAmplitude * 100f, v => Meters(v / 100f));
        Percent(table, Strings.WaveLength, 20, 300, () => Water.WaveFrequency * 100f, v => Water.WaveFrequency = v / 100f, Defaults.Water.WaveFrequency * 100f, v => Times(v / 100f));
        Percent(table, Strings.WaveSpeed, 0, 300, () => Water.WaveSpeed * 100f, v => Water.WaveSpeed = v / 100f, Defaults.Water.WaveSpeed * 100f, v => v == 0 ? Strings.ValueStill : Times(v / 100f));
        Percent(table, Strings.Reflections, 0, 100, () => Water.SkyReflection * 100f, v => Water.SkyReflection = v / 100f, Defaults.Water.SkyReflection * 100f, Percentage);
        Percent(table, Strings.Ripples, 0, 200, () => Water.Ripples * 100f, v => Water.Ripples = v / 100f, Defaults.Water.Ripples * 100f, Percentage);
        Percent(table, Strings.SunGlints, 0, 200, () => Water.SunGlints * 100f, v => Water.SunGlints = v / 100f, Defaults.Water.SunGlints * 100f, Percentage);
        Color(table, Strings.DeepWater, () => Vector(Water.DeepColor), c => Water.DeepColor = Value(c), Vector(Defaults.Water.DeepColor));
        Color(table, Strings.ShallowWater, () => Vector(Water.ShallowColor), c => Water.ShallowColor = Value(c), Vector(Defaults.Water.ShallowColor));
        return card;
    }

    private Panel BuildBoatsCard()
    {
        var card = Theme.Card(Strings.CardBoats, out var table);
        Percent(table, Strings.BoatMovement, 0, 300, () => Water.BoatMotion * 100f, v => Water.BoatMotion = v / 100f, Defaults.Water.BoatMotion * 100f, v => v == 0 ? Strings.ValueStill : Percentage(v));
        Theme.FullRow(table, Theme.Hint(Strings.BoatMovementHint));
        return card;
    }

    private Panel BuildLightCard()
    {
        var card = Theme.Card(Strings.CardLight, out var table);
        var azimuth = 0f;
        var elevation = 40f;

        Percent(table, Strings.SunDirection, 0, 359, () => azimuth, v =>
        {
            azimuth = v;
            Lighting.SetSunAngles(azimuth, elevation);
        }, 0f, v => Strings.Format(Strings.ValueDegreesFromNorth, v));

        Percent(table, Strings.SunHeight, 5, 89, () => elevation, v =>
        {
            elevation = v;
            Lighting.SetSunAngles(azimuth, elevation);
        }, 40f, v => Strings.Format(Strings.ValueDegreesAboveHorizon, v));

        Percent(table, Strings.Haze, 0, 100, () => Lighting.FogDensity * 20000f, v => Lighting.FogDensity = v / 20000f, Defaults.Lighting.FogDensity * 20000f, Percentage);
        Color(table, Strings.Sky, () => Vector(Lighting.SkyColor), c => Lighting.SkyColor = Value(c), Vector(Defaults.Lighting.SkyColor));
        Color(table, Strings.Horizon, () => Vector(Lighting.FogColor), c => Lighting.FogColor = Value(c), Vector(Defaults.Lighting.FogColor));
        return card;
    }

    private Panel BuildStatusCard()
    {
        var card = Theme.Card(Strings.CardBerthColors, out var table);
        var status = _marina.Style.Status;
        Color(table, Strings.ColorFree, () => status.FreeColor, c => status.FreeColor = c, Defaults.Status.FreeColor);
        Color(table, Strings.ColorOccupied, () => status.OccupiedColor, c => status.OccupiedColor = c, Defaults.Status.OccupiedColor);
        Color(table, Strings.ColorReserved, () => status.ReservedColor, c => status.ReservedColor = c, Defaults.Status.ReservedColor);
        Color(table, Strings.ColorOwnerAway, () => status.TemporarilyFreeColor, c => status.TemporarilyFreeColor = c, Defaults.Status.TemporarilyFreeColor);
        Percent(table, Strings.PadStrength, 0, 100, () => status.PadOpacity * 100f, v => status.PadOpacity = v / 100f, Defaults.Status.PadOpacity * 100f, Percentage);

        var markers = Theme.Check(Strings.ShowStatusBuoys);
        markers.Checked = status.ShowStatusMarkers;
        markers.CheckedChanged += (_, _) => Changed(() => status.ShowStatusMarkers = markers.Checked);
        Theme.FullRow(table, markers);
        return card;
    }

    private Panel BuildLandCard()
    {
        var card = Theme.Card(Strings.CardLand, out var table);
        Color(table, Strings.LandQuay, () => Land.QuayColor, c => Land.QuayColor = c, Defaults.Land.QuayColor);
        Color(table, Strings.LandQuayWall, () => Land.QuayWallColor, c => Land.QuayWallColor = c, Defaults.Land.QuayWallColor);
        Color(table, Strings.LandGrass, () => Land.GrassColor, c => Land.GrassColor = c, Defaults.Land.GrassColor);
        Color(table, Strings.LandGrassBank, () => Land.GrassBankColor, c => Land.GrassBankColor = c, Defaults.Land.GrassBankColor);
        Color(table, Strings.LandRock, () => Land.RockColor, c => Land.RockColor = c, Defaults.Land.RockColor);
        Percent(table, Strings.LandRockVariation, 0, 50, () => Land.RockColorVariation * 100f, v => Land.RockColorVariation = v / 100f, Defaults.Land.RockColorVariation * 100f, Percentage, Strings.LandRockVariationTip);

        Theme.Section(table, Strings.CardTrees);
        Color(table, Strings.LandFoliage, () => Land.FoliageColor, c => Land.FoliageColor = c, Defaults.Land.FoliageColor);
        Color(table, Strings.LandConifer, () => Land.ConiferColor, c => Land.ConiferColor = c, Defaults.Land.ConiferColor);
        Color(table, Strings.LandPalm, () => Land.PalmColor, c => Land.PalmColor = c, Defaults.Land.PalmColor);
        Color(table, Strings.LandBlossom, () => Land.BlossomColor, c => Land.BlossomColor = c, Defaults.Land.BlossomColor);
        Color(table, Strings.LandTrunk, () => Land.TrunkColor, c => Land.TrunkColor = c, Defaults.Land.TrunkColor);

        var trees = Theme.Check(Strings.LandShowTrees);
        trees.Checked = Land.ShowTrees;
        trees.CheckedChanged += (_, _) => Changed(() => Land.ShowTrees = trees.Checked);
        Theme.FullRow(table, trees);
        return card;
    }

    private Panel BuildLabelCard()
    {
        var card = Theme.Card(Strings.CardLabels, out var table);
        var labels = _marina.Style.Labels;

        var face = Theme.Choice();
        face.Items.AddRange(new object[] { Strings.FaceRegular, Strings.FaceBold, Strings.FaceCondensed, Strings.FaceWide });
        face.SelectedIndex = (int)labels.FontFamily;
        face.SelectedIndexChanged += (_, _) => Changed(() => labels.FontFamily = (LabelFont)face.SelectedIndex);
        Theme.Row(table, Strings.LabelFace, face, (_, _) =>
        {
            face.SelectedIndex = (int)Defaults.Labels.FontFamily;
        }, Strings.LabelFaceTip);

        Color(table, Strings.LabelColorNormal, () => labels.Color, c => labels.Color = c, Defaults.Labels.Color);
        Color(table, Strings.LabelColorHighlight, () => labels.HighlightColor, c => labels.HighlightColor = c, Defaults.Labels.HighlightColor);
        Color(table, Strings.LabelColorDisabled, () => labels.DisabledColor, c => labels.DisabledColor = c, Defaults.Labels.DisabledColor);
        return card;
    }

    private Panel BuildPreviewCard()
    {
        var card = Theme.Card(Strings.CardPreview, out var table);
        Theme.Row(table, Strings.PreviewFill, Theme.Slider(_fill, _fillValue, Percentage), Strings.PreviewFillTip);

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        buttons.Controls.Add(Theme.Action(Strings.PreviewAdd, (_, _) => FillWithBoats(), primary: true));
        buttons.Controls.Add(Theme.Action(Strings.PreviewClear, (_, _) => ClearBoats()));
        Theme.FullRow(table, buttons);
        Theme.FullRow(table, Theme.Hint(Strings.PreviewHint));
        return card;
    }

    private Panel BuildResetCard()
    {
        var card = Theme.Card(Strings.AppearanceReset, out var table);
        Theme.FullRow(table, Theme.Action(Strings.AppearanceReset, (_, _) =>
        {
            _marina.Style = new MarinaStyle();
            Rebuild();
        }));

        return card;
    }

    // ---- Preview boats ------------------------------------------------------------------------

    /// <summary>
    /// Puts boats in a share of the free berths, to judge the colours and the motion against. They are ordinary
    /// boats as far as the visualizer is concerned; the design file simply never stores occupancy.
    /// </summary>
    private void FillWithBoats()
    {
        var free = _marina.GetBerths().Where(berth => berth.Status == BerthStatus.Free && !berth.IsOnLand).ToList();
        var wanted = (int)MathF.Round(free.Count * _fill.Value / 100f);
        if (wanted == 0) return;

        // Shuffle, so the boats are scattered rather than filling the first pier.
        for (var i = free.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (free[i], free[j]) = (free[j], free[i]);
        }

        var types = Enum.GetValues<BoatType>();
        using (_marina.BeginUpdate())
        {
            for (var i = 0; i < wanted; i++)
            {
                var berth = free[i];
                var type = types[_random.Next(types.Length)];
                var size = BoatTypeCatalog.GetNominalDimensions(type);

                // A boat that would not fit is not worth drawing; leave the berth empty.
                if (size.Length > berth.Length || size.Beam > berth.Width) continue;
                _marina.AssignBoat(berth.Id, new Boat($"PREVIEW-{i}", BoatName(type, i), type));
            }
        }

        _log(Strings.Format(Strings.LogPreviewBoats, wanted));
    }

    private void ClearBoats()
    {
        using (_marina.BeginUpdate())
        {
            foreach (var berth in _marina.GetBerths().Where(b => b.Boat is not null))
            {
                _marina.SetBerthStatus(berth.Id, BerthStatus.Free);
            }
        }

        _log(Strings.LogPreviewCleared);
    }

    private static string BoatName(BoatType type, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{BoatTypeCatalog.GetDisplayName(type)} {index + 1}");

    // ---- Row helpers --------------------------------------------------------------------------

    /// <summary>A slider row that can be put back to its default on its own.</summary>
    private void Percent(TableLayoutPanel table, string label, int min, int max, Func<float> read, Action<float> write, float fallback, Func<int, string> format, string? tooltip = null)
    {
        var bar = new TrackBar { Minimum = min, Maximum = max, Value = Clamp(read(), min, max) };
        bar.ValueChanged += (_, _) => Changed(() => write(bar.Value));
        Theme.Row(table, label, Theme.Slider(bar, new Label(), format), (_, _) => bar.Value = Clamp(fallback, min, max), tooltip);
    }

    /// <summary>A colour swatch row that can be put back to its default on its own.</summary>
    private void Color(TableLayoutPanel table, string label, Func<ColorRgba> read, Action<ColorRgba> write, ColorRgba fallback)
    {
        var swatch = new Panel
        {
            Height = 22,
            BackColor = ToColor(read()),
            BorderStyle = BorderStyle.FixedSingle,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 4, 0, 4),
        };

        swatch.Click += (_, _) =>
        {
            using var picker = new ColorDialog { Color = swatch.BackColor, FullOpen = true, AnyColor = true };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            swatch.BackColor = picker.Color;
            Changed(() => write(FromColor(picker.Color)));
        };

        Theme.Row(table, label, swatch, (_, _) =>
        {
            swatch.BackColor = ToColor(fallback);
            Changed(() => write(fallback));
        });
    }

    /// <summary>Applies a change and redraws, so the effect shows the moment the slider moves.</summary>
    private void Changed(Action change)
    {
        change();
        _marina.InvalidateScene();
    }

    /// <summary>Builds the cards again, after the whole style was replaced.</summary>
    private void Rebuild()
    {
        var replacement = new AppearancePanel(_marina, _log) { Dock = Dock, Bounds = Bounds };
        if (Parent is { } parent)
        {
            var index = parent.Controls.GetChildIndex(this);
            parent.Controls.Remove(this);
            parent.Controls.Add(replacement);
            parent.Controls.SetChildIndex(replacement, index);
        }

        _marina.InvalidateScene();
        Dispose();
    }

    private static int Clamp(float value, int min, int max) => Math.Clamp((int)MathF.Round(value), min, max);

    private static string Percentage(int value) => Strings.Format(Strings.Percent, value);

    private static string Meters(float value) => Strings.Format(Strings.ValueMeters, value);

    private static string Times(float value) => Strings.Format(Strings.ValueTimes, value);

    private static Color ToColor(ColorRgba color) => System.Drawing.Color.FromArgb(Byte(color.R), Byte(color.G), Byte(color.B));

    private static ColorRgba FromColor(Color color) => new(color.R / 255f, color.G / 255f, color.B / 255f);

    private static byte Byte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    private static ColorRgba Vector(System.Numerics.Vector3 color) => new(color.X, color.Y, color.Z);

    private static System.Numerics.Vector3 Value(ColorRgba color) => new(color.R, color.G, color.B);
}
