using System.Globalization;
using System.Numerics;
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
    /// <summary>
    /// The cards, stacked. It sizes to its content and sits inside <see cref="_scroller"/>: a TableLayoutPanel
    /// scrolls its own content unreliably, so the scrolling is left to a plain panel around it.
    /// </summary>
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

    /// <summary>Scrolls the cards when there are more of them than fit, which a tall tool easily manages.</summary>
    private readonly Panel _scroller = new()
    {
        AutoScroll = true,
        Dock = DockStyle.Fill,
        BackColor = Theme.Background,
    };

    private readonly TrackBar _fill = new() { Minimum = 0, Maximum = 100, Value = 60 };
    private readonly Label _fillValue = new();
    private readonly Random _random = new();
    private Label? _trafficLanes;

    /// <summary>One per control: puts the value the marina holds back into it. Run by <see cref="Sync"/>.</summary>
    private readonly List<Action> _refresh = new();

    /// <summary>True while values are being read back, so the controls do not write what they are being given.</summary>
    private bool _updating;

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
    /// <param name="marina">The marina whose look is being changed.</param>
    /// <param name="log">Where to note what happened, for the activity log.</param>
    public AppearancePanel(MarinaVisualizer marina, Action<string> log)
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
        header.Controls.Add(new Label { Text = Strings.TitleLook, Font = new Font("Segoe UI Semibold", 13f), ForeColor = Theme.Text, AutoSize = true, Dock = DockStyle.Fill }, 0, 0);
        header.Controls.Add(new Label { Text = Strings.LookHint, Font = Theme.Body, ForeColor = Theme.TextSoft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) }, 0, 1);

        _stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        foreach (var card in new[] { BuildWaterCard(), BuildBoatsCard(), BuildLightCard(), BuildStatusCard(), BuildLandCard(), BuildShadowCard(), BuildTrafficCard(), BuildLabelCard(), BuildPreviewCard(), BuildResetCard() })
        {
            card.Dock = DockStyle.Top;
            _stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _stack.Controls.Add(card, 0, _stack.RowCount++);
        }

        _scroller.Controls.Add(_stack);
        Controls.Add(_scroller);
        Controls.Add(header);
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

        var markers = Check(table, Strings.ShowStatusBuoys, () => Status.ShowStatusMarkers, v => Status.ShowStatusMarkers = v);
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

        Check(table, Strings.TrafficShowLanes, () => _marina.ShowTrafficLanes, v => Changed(() => _marina.ShowTrafficLanes = v), Strings.TrafficShowLanesTip);

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

        // The letters themselves, chosen separately from their weight, so any typeface can be had bold or condensed.
        var typeface = Theme.Choice();
        typeface.Items.AddRange(new object[] { Strings.TypefaceSans, Strings.TypefaceSerif, Strings.TypefaceSlab });
        typeface.SelectedIndex = (int)Labels.Typeface;
        typeface.SelectedIndexChanged += (_, _) => Changed(() => Labels.Typeface = (LabelTypeface)typeface.SelectedIndex);
        _refresh.Add(() => typeface.SelectedIndex = (int)Labels.Typeface);
        Theme.Row(table, Strings.LabelTypeface, typeface, (_, _) =>
        {
            typeface.SelectedIndex = (int)Defaults.Labels.Typeface;
        }, Strings.LabelTypefaceTip);

        var face = Theme.Choice();
        face.Items.AddRange(new object[] { Strings.FaceRegular, Strings.FaceBold, Strings.FaceCondensed, Strings.FaceWide });
        face.SelectedIndex = (int)Labels.FontFamily;
        face.SelectedIndexChanged += (_, _) => Changed(() => Labels.FontFamily = (LabelFont)face.SelectedIndex);
        _refresh.Add(() => face.SelectedIndex = (int)Labels.FontFamily);
        Theme.Row(table, Strings.LabelFace, face, (_, _) =>
        {
            face.SelectedIndex = (int)Defaults.Labels.FontFamily;
        }, Strings.LabelFaceTip);

        Color(table, Strings.LabelColorNormal, () => Labels.Color, c => Labels.Color = c, Defaults.Labels.Color);
        Color(table, Strings.LabelColorHighlight, () => Labels.HighlightColor, c => Labels.HighlightColor = c, Defaults.Labels.HighlightColor);
        Color(table, Strings.LabelColorDisabled, () => Labels.DisabledColor, c => Labels.DisabledColor = c, Defaults.Labels.DisabledColor);
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
            _marina.SetMarineTraffic(MarineTraffic.None);
            Sync();
            _marina.InvalidateScene();
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
        _refresh.Add(() => bar.Value = Clamp(read(), min, max));
        Theme.Row(table, label, Theme.Slider(bar, new Label(), format), (_, _) => bar.Value = Clamp(fallback, min, max), tooltip);
    }

    /// <summary>A tick box that reads itself back from the marina when the panel is synced.</summary>
    private CheckBox Check(TableLayoutPanel table, string label, Func<bool> read, Action<bool> write, string? tooltip = null)
    {
        var box = Theme.Check(label);
        box.Checked = read();
        box.CheckedChanged += (_, _) => Changed(() => write(box.Checked));
        _refresh.Add(() => box.Checked = read());
        Theme.FullRow(table, box);
        if (tooltip is not null) Theme.Tips.SetToolTip(box, tooltip);
        return box;
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

        _refresh.Add(() => swatch.BackColor = ToColor(read()));
        Theme.Row(table, label, swatch, (_, _) =>
        {
            swatch.BackColor = ToColor(fallback);
            Changed(() => write(fallback));
        });
    }

    /// <summary>Applies a change and redraws, so the effect shows the moment the slider moves.</summary>
    private void Changed(Action change)
    {
        if (_updating) return;
        change();
        _marina.InvalidateScene();
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
}
