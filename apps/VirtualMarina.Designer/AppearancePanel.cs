using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Design;
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
internal sealed class AppearancePanel : SidePanel
{
    /// <summary>A fresh style, read whenever a single setting is put back to its default.</summary>
    private static readonly MarinaStyle Defaults = new();

    private readonly MarinaVisualizer _marina;
    private readonly Action<string> _log;
    private readonly Action _changed;
    private readonly Func<string, string, bool> _confirm;

    /// <summary>
    /// Fonts already captured this session, by family and weight. Capturing reads every glyph's outline, which takes a
    /// noticeable moment, so going back to a font chosen before costs nothing.
    /// </summary>
    private readonly Dictionary<(string Family, bool Bold), LabelFontDefinition?> _capturedFonts = [];

    private readonly TrackBar _fill = new() { Minimum = 0, Maximum = 100, Value = 60 };
    private readonly Label _fillValue = new();
    private readonly Random _random = new();
    private Label? _trafficLanes;

    /// <summary>One per control: puts the value the marina holds back into it. Run by <see cref="Sync"/>.</summary>
    private readonly List<Action> _refresh = [];

    /// <summary>True while values are being read back, so the controls do not write what they are being given.</summary>
    private bool _updating;

    /// <summary>Creates the panel over a visualizer.</summary>
    /// <param name="marina">The marina whose look is being changed.</param>
    /// <param name="log">Where to note what happened, for the activity log.</param>
    /// <param name="changed">Called after every change to what the design saves: the style, the traffic, the label font.</param>
    /// <param name="confirm">Asks a yes-or-no question (title, message); true for yes.</param>
    public AppearancePanel(MarinaVisualizer marina, Action<string> log, Action changed, Func<string, string, bool> confirm)
    {
        _marina = marina;
        _log = log;
        _changed = changed;
        _confirm = confirm;
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
        SetHeader(header);
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        header.Controls.Add(new Label { Text = Strings.TitleLook, Font = Theme.PanelTitle, ForeColor = Theme.Text, AutoSize = true, Dock = DockStyle.Fill }, 0, 0);
        header.Controls.Add(new Label { Text = Strings.LookHint, Font = Theme.Body, ForeColor = Theme.TextSoft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) }, 0, 1);

        Stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        foreach (var card in new[] { BuildWaterCard(), BuildBoatsCard(), BuildLightCard(), BuildStatusCard(), BuildLandCard(), BuildShadowCard(), BuildTrafficCard(), BuildLabelCard(), BuildPreviewCard(), BuildResetCard() })
        {
            card.Dock = DockStyle.Top;
            Stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Stack.Controls.Add(card, 0, Stack.RowCount++);
        }

        Scroller.Controls.Add(Stack);
        Controls.Add(Scroller);
        Controls.Add(header);
    }

    private WaterSettings Water => _marina.Style.Water;

    private LightingSettings Lighting => _marina.Style.Lighting;

    private LandStyle Land => _marina.Style.Land;

    private MarineTraffic Traffic => _marina.MarineTraffic;

    private ShadowStyle Shadows => _marina.Style.Shadows;

    private StatusColorScheme Status => _marina.Style.Status;

    private LabelStyle Labels => _marina.Style.Labels;

    /// <summary>Where the sun is round the compass, taken back out of its direction.</summary>
    private float SunAzimuth
    {
        get
        {
            var sun = Lighting.SunDirection;
            var degrees = MathF.Atan2(sun.X, sun.Z) * 180f / MathF.PI;
            return degrees < 0f ? degrees + 360f : degrees;
        }
    }

    /// <summary>How high the sun stands, taken back out of its direction.</summary>
    private float SunElevation => MathF.Asin(Math.Clamp(Lighting.SunDirection.Y, -1f, 1f)) * 180f / MathF.PI;

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
        Percent(table, Strings.WaterArea, 800, 12000, () => Water.Size, v => Water.Size = v, Defaults.Water.Size, v => Strings.Format(Strings.ValueMetersWhole, v), Strings.WaterAreaTip);
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

        // Read from the sun itself rather than from numbers kept beside it, so an opened design moves the sliders.
        Percent(table, Strings.SunDirection, 0, 359, () => SunAzimuth,
            v => Lighting.SetSunAngles(v, SunElevation), 0f, v => Strings.Format(Strings.ValueDegreesFromNorth, v));

        Percent(table, Strings.SunHeight, 5, 89, () => SunElevation,
            v => Lighting.SetSunAngles(SunAzimuth, v), 40f, v => Strings.Format(Strings.ValueDegreesAboveHorizon, v));

        Percent(table, Strings.Haze, 0, 100, () => Lighting.FogDensity * 20000f, v => Lighting.FogDensity = v / 20000f, Defaults.Lighting.FogDensity * 20000f, Percentage);
        Color(table, Strings.Sky, () => Vector(Lighting.SkyColor), c => Lighting.SkyColor = Value(c), Vector(Defaults.Lighting.SkyColor));
        Color(table, Strings.Horizon, () => Vector(Lighting.FogColor), c => Lighting.FogColor = Value(c), Vector(Defaults.Lighting.FogColor));
        return card;
    }

    private Panel BuildStatusCard()
    {
        var card = Theme.Card(Strings.CardBerthColors, out var table);
        Color(table, Strings.ColorFree, () => Status.FreeColor, c => Status.FreeColor = c, Defaults.Status.FreeColor);
        Color(table, Strings.ColorOccupied, () => Status.OccupiedColor, c => Status.OccupiedColor = c, Defaults.Status.OccupiedColor);
        Color(table, Strings.ColorReserved, () => Status.ReservedColor, c => Status.ReservedColor = c, Defaults.Status.ReservedColor);
        Color(table, Strings.ColorOwnerAway, () => Status.TemporarilyFreeColor, c => Status.TemporarilyFreeColor = c, Defaults.Status.TemporarilyFreeColor);
        Percent(table, Strings.PadStrength, 0, 100, () => Status.PadOpacity * 100f, v => Status.PadOpacity = v / 100f, Defaults.Status.PadOpacity * 100f, Percentage);

        Check(table, Strings.ShowStatusBuoys, () => Status.ShowStatusMarkers, v => Status.ShowStatusMarkers = v);
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
        Color(table, Strings.LandBuilding, () => Land.BuildingColor, c => Land.BuildingColor = c, Defaults.Land.BuildingColor);
        Color(table, Strings.LandRoof, () => Land.RoofColor, c => Land.RoofColor = c, Defaults.Land.RoofColor);

        Check(table, Strings.LandShowTrees, () => Land.ShowTrees, v => Land.ShowTrees = v);
        return card;
    }

    /// <summary>Shadows cast by the boats and the piers, and how dark they are.</summary>
    private Panel BuildShadowCard()
    {
        var card = Theme.Card(Strings.CardShadows, out var table);

        Check(table, Strings.ShadowsShow, () => Shadows.IsEnabled, v => Shadows.IsEnabled = v, Strings.ShadowsShowTip);

        Percent(table, Strings.ShadowStrength, 0, 100, () => Shadows.Strength * 100f,
            v => Shadows.Strength = v / 100f, Defaults.Shadows.Strength * 100f, Percentage, Strings.ShadowStrengthTip);
        Theme.FullRow(table, Theme.Hint(Strings.ShadowHint));
        return card;
    }

    /// <summary>
    /// The traffic out at sea. Unlike the rest of this panel these are not style settings but part of the marina, so
    /// each one is written back through the visualizer rather than onto a style object.
    /// </summary>
    private Panel BuildTrafficCard()
    {
        var card = Theme.Card(Strings.CardTraffic, out var table);
        var where = Theme.Hint(string.Empty);

        Check(table, Strings.TrafficShow, () => Traffic.IsEnabled, v => SetTraffic(t => t with { IsEnabled = v }), Strings.TrafficShowTip);

        Percent(table, Strings.TrafficClearance, 50, 1200, () => Traffic.Clearance,
            v => SetTraffic(t => t with { Clearance = v }), MarineTraffic.None.Clearance, v => Strings.Format(Strings.ValueMetersWhole, v), Strings.TrafficClearanceTip);
        Percent(table, Strings.TrafficEdgeClearance, 50, 4000, () => Traffic.EdgeClearance,
            v => SetTraffic(t => t with { EdgeClearance = v }), MarineTraffic.None.EdgeClearance, v => Strings.Format(Strings.ValueMetersWhole, v), Strings.TrafficEdgeClearanceTip);
        Percent(table, Strings.TrafficLanes, 1, MarineTraffic.LaneLimit, () => Traffic.LaneCount,
            v => SetTraffic(t => t with { LaneCount = (int)v }), MarineTraffic.None.LaneCount, v => Strings.Format(Strings.ValueLanes, v), Strings.TrafficLanesTip);
        Percent(table, Strings.TrafficLaneSpacing, 40, 600, () => Traffic.LaneSpacing,
            v => SetTraffic(t => t with { LaneSpacing = v }), MarineTraffic.None.LaneSpacing, v => Strings.Format(Strings.ValueMetersWhole, v), Strings.TrafficLaneSpacingTip);
        Percent(table, Strings.TrafficMaximum, 1, MarineTraffic.VesselLimit, () => Traffic.MaximumVessels,
            v => SetTraffic(t => t with { MaximumVessels = (int)v }), MarineTraffic.None.MaximumVessels, v => Strings.Format(Strings.ValueVessels, v), Strings.TrafficMaximumTip);
        Percent(table, Strings.TrafficSpeed, 10, 400, () => Traffic.SpeedPercent,
            v => SetTraffic(t => t with { SpeedPercent = v }), MarineTraffic.None.SpeedPercent, Percentage, Strings.TrafficSpeedTip);
        Percent(table, Strings.TrafficSpawnDelay, 1, 180, () => Traffic.SpawnDelaySeconds,
            v => SetTraffic(t => t with { SpawnDelaySeconds = v }), MarineTraffic.None.SpawnDelaySeconds, v => Strings.Format(Strings.ValueSeconds, v), Strings.TrafficSpawnDelayTip);

        // Drawing the lanes is a designer aid, not part of the design, so it does not count as a change to save.
        Check(table, Strings.TrafficShowLanes, () => _marina.ShowTrafficLanes, v => Changed(() => _marina.ShowTrafficLanes = v, saved: false), Strings.TrafficShowLanesTip);

        Theme.FullRow(table, Theme.Hint(Strings.TrafficHint));
        Theme.FullRow(table, where);

        // Where the path ended up is only known once it has been planned, so it is refreshed after every change.
        _trafficLanes = where;
        UpdateTrafficLanes();
        return card;
    }

    /// <summary>Applies a change to the traffic and refreshes what the card says about it.</summary>
    private void SetTraffic(Func<MarineTraffic, MarineTraffic> change)
    {
        Changed(() => _marina.SetMarineTraffic(change(_marina.MarineTraffic)));
        UpdateTrafficLanes();
    }

    /// <summary>Says how near the nearest lane actually comes, which is what the clearance slider is really setting.</summary>
    private void UpdateTrafficLanes()
    {
        if (_trafficLanes is null) return;

        var lanes = _marina.TrafficLanes;
        _trafficLanes.Text = !Traffic.IsEnabled ? string.Empty
            : lanes.Count == 0 ? Strings.TrafficNoRoom
            : Strings.Format(Strings.TrafficPasses, lanes.Count, (int)MathF.Round(lanes.Min(lane => lane.DistanceTo(Center()))));
    }

    /// <summary>The middle of the marina in plan coordinates, which the traffic's clearance is measured from.</summary>
    private Vector2 Center()
    {
        var (min, max) = _marina.GetLayout().ComputeBounds();
        return (min + max) * 0.5f;
    }

    private Panel BuildLabelCard()
    {
        var card = Theme.Card(Strings.CardLabels, out var table);

        // Every font on this machine, plus the built-in lettering for a design that would rather not carry one.
        var font = Theme.Choice();
        font.Items.Add(Strings.FontBuiltIn);
        foreach (var family in FontCapture.InstalledFamilies()) font.Items.Add(family);

        var bold = Theme.Check(Strings.LabelBold);
        void UseFont()
        {
            var family = font.SelectedIndex <= 0 ? null : font.Items[font.SelectedIndex] as string;
            Changed(() =>
            {
                Labels.Font = family is null ? null : CaptureFont(family, bold.Checked);
                Labels.FontFamily = bold.Checked ? LabelFont.Bold : LabelFont.Regular;
                if (family is not null && Labels.Font is null) font.SelectedIndex = 0;
            });
        }

        void ShowFont()
        {
            var chosen = Labels.Font is null ? 0 : Math.Max(0, font.Items.IndexOf(FamilyOf(Labels.Font.Name)));
            font.SelectedIndex = chosen;
            bold.Checked = Labels.Font?.IsBold ?? Labels.FontFamily == LabelFont.Bold;
        }

        ShowFont();

        // Only a choice the user settles on is captured: not every entry the arrow keys or the wheel pass over while
        // the list is being browsed. A choice made by the reset button below is committed the same way.
        font.SelectionChangeCommitted += (_, _) => UseFont();
        bold.CheckedChanged += (_, _) => UseFont();
        _refresh.Add(ShowFont);

        Theme.Row(table, Strings.LabelFont, font, (_, _) =>
        {
            font.SelectedIndex = 0;
            UseFont();
        }, Strings.LabelFontTip);
        Theme.FullRow(table, bold);
        Theme.Tips.SetToolTip(bold, Strings.LabelBoldTip);

        Color(table, Strings.LabelColorNormal, () => Labels.Color, c => Labels.Color = c, Defaults.Labels.Color);
        Color(table, Strings.LabelColorAshore, () => Labels.AshoreColor, c => Labels.AshoreColor = c, Defaults.Labels.AshoreColor, Strings.LabelColorAshoreTip);
        Color(table, Strings.LabelColorHighlight, () => Labels.HighlightColor, c => Labels.HighlightColor = c, Defaults.Labels.HighlightColor);
        Color(table, Strings.LabelColorDisabled, () => Labels.DisabledColor, c => Labels.DisabledColor = c, Defaults.Labels.DisabledColor);
        return card;
    }

    /// <summary>A font's outlines, captured the first time it is asked for and remembered after that.</summary>
    private LabelFontDefinition? CaptureFont(string family, bool bold)
    {
        if (_capturedFonts.TryGetValue((family, bold), out var known)) return known;
        using (new CursorScope())
        {
            var captured = FontCapture.Capture(family, bold);
            _capturedFonts[(family, bold)] = captured;
            return captured;
        }
    }

    /// <summary>The family a captured font came from, without the "Bold" the capture adds to its name.</summary>
    private static string FamilyOf(string fontName) =>
        fontName.EndsWith(" Bold", StringComparison.Ordinal) ? fontName[..^5] : fontName;

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
            // Every colour, the water, the light and the traffic at once, with no undo: worth a question.
            if (!_confirm(Strings.ConfirmResetAppearanceTitle, Strings.ConfirmResetAppearance)) return;
            _marina.Style = new MarinaStyle();
            _marina.SetMarineTraffic(MarineTraffic.None);
            Sync();
            _changed();
        }));

        return card;
    }

    // ---- Preview boats ------------------------------------------------------------------------

    /// <summary>
    /// Fills the marina to the share of its berths the slider asks for, to judge the colours and the motion
    /// against. They are ordinary boats as far as the visualizer is concerned; the design file simply never stores
    /// occupancy.
    /// </summary>
    /// <remarks>
    /// The share is of the whole marina, ashore berths included, and it is where the marina ends up rather than
    /// what gets added: the boats already there are taken out first, so pressing the button again deals a fresh
    /// fleet to the same figure instead of piling more on top of a marina that is already half full. At 100% every
    /// berth has a boat in it, which is why a berth too small for any of the models still gets the smallest one,
    /// cut down to fit, rather than being quietly skipped.
    /// </remarks>
    private void FillWithBoats()
    {
        ClearBoats(log: false);

        var berths = _marina.GetBerths();
        var wanted = (int)MathF.Round(berths.Count * _fill.Value / 100f);
        if (wanted == 0)
        {
            _log(Strings.LogPreviewCleared);
            return;
        }

        var plan = PreviewFleet.Plan(berths, wanted, _random);
        var filled = 0;
        using (_marina.BeginUpdate())
        {
            foreach (var mooring in plan)
            {
                if (mooring.BerthIds.Count == 1) _marina.AssignBoat(mooring.BerthIds[0], mooring.Boat);
                else _marina.AssignBoatToBerths(mooring.BerthIds, mooring.Boat, style: mooring.Style);
                filled += mooring.BerthIds.Count;
            }
        }

        _log(Strings.Format(Strings.LogPreviewBoats, filled, berths.Count));
    }

    /// <summary>Empties every berth, whether the boat is in one of them or moored across two.</summary>
    private void ClearBoats(bool log = true)
    {
        using (_marina.BeginUpdate())
        {
            // Releasing a multi-berth frees its members, so the list is re-read rather than walked as it was.
            foreach (var berth in _marina.GetMultiBerths()) _marina.ReleaseMultiBerth(berth.Id);
            foreach (var berth in _marina.GetBerths().Where(b => b.Boat is not null))
            {
                _marina.SetBerthStatus(berth.Id, BerthStatus.Free);
            }
        }

        if (log) _log(Strings.LogPreviewCleared);
    }

    // ---- Row helpers --------------------------------------------------------------------------

    /// <summary>A slider row that can be put back to its default on its own.</summary>
    private void Percent(TableLayoutPanel table, string label, int min, int max, Func<float> read, Action<float> write, float fallback, Func<int, string> format, string? tooltip = null)
    {
        var bar = new TrackBar { Minimum = min, Maximum = max, Value = Clamp(read(), min, max) };
        bar.ValueChanged += (_, _) => Changed(() => write(bar.Value));
        _refresh.Add(() => bar.Value = Clamp(read(), min, max));
        Theme.Row(table, label, Theme.Slider(bar, new Label(), format), (_, _) => bar.Value = Clamp(fallback, min, max), tooltip);
    }

    /// <summary>A tick box that reads itself back from the marina when the panel is synced.</summary>
    private void Check(TableLayoutPanel table, string label, Func<bool> read, Action<bool> write, string? tooltip = null)
    {
        var box = Theme.Check(label);
        box.Checked = read();
        box.CheckedChanged += (_, _) => Changed(() => write(box.Checked));
        _refresh.Add(() => box.Checked = read());
        Theme.FullRow(table, box);
        if (tooltip is not null) Theme.Tips.SetToolTip(box, tooltip);
    }

    /// <summary>A colour swatch row that can be put back to its default on its own.</summary>
    private void Color(TableLayoutPanel table, string label, Func<ColorRgba> read, Action<ColorRgba> write, ColorRgba fallback, string? tooltip = null)
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

        _refresh.Add(() => swatch.BackColor = ToColor(read()));
        Theme.Row(table, label, swatch, (_, _) =>
        {
            swatch.BackColor = ToColor(fallback);
            Changed(() => write(fallback));
        }, tooltip);
    }

    /// <summary>
    /// Applies a change and reports it so the design counts as changed. The marina notices a change to any part of
    /// its style by itself, so the effect shows the moment the slider moves without anything being redrawn here.
    /// </summary>
    /// <param name="change">The change.</param>
    /// <param name="saved">False for a setting the design file does not keep.</param>
    private void Changed(Action change, bool saved = true)
    {
        if (_updating) return;
        change();
        if (saved) _changed();
    }

    /// <summary>
    /// Reads every setting back out of the marina and into the controls. Called when a design is opened, so the
    /// panel shows what the file held rather than what it was built with, and after the whole style is replaced.
    /// </summary>
    public void Sync()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            foreach (var refresh in _refresh) refresh();
            UpdateTrafficLanes();
        }
        finally
        {
            _updating = false;
        }
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

    /// <summary>Shows the wait cursor while something slow runs, and puts the old one back after.</summary>
    private readonly struct CursorScope : IDisposable
    {
        private readonly Cursor? _previous;

        public CursorScope()
        {
            _previous = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
        }

        public void Dispose() => Cursor.Current = _previous ?? Cursors.Default;
    }
}
