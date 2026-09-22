using System.Globalization;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Designer.Resources;

namespace VirtualMarina.Designer;

/// <summary>
/// The panel beside the view: the name of the tool in use, what to do with it, and only the settings that tool needs. Everything
/// else stays out of the way, so the window never has to be scrolled to reach a control.
/// </summary>
internal sealed class InspectorPanel : Panel
{
    private readonly MarinaVisualizer _marina;
    private readonly Action _loadImage;
    private readonly Label _toolName = new() { Font = new Font("Segoe UI Semibold", 13f), ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
    private readonly Label _toolHint = new() { Font = Theme.Body, ForeColor = Theme.TextSoft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
    /// <summary>
    /// The cards, stacked in a single column that is always the full width of the panel. A card docked into that
    /// column is given its width by the layout engine, so dragging the splitter resizes the cards and the text
    /// inside them without anything being measured here.
    /// </summary>
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

    // Land area
    private readonly Panel _landCard;
    private readonly ComboBox _landKind = Theme.Choice();
    private readonly NumericUpDown _landHeight = Theme.Number(0m, 50m, 0.25m);

    // Pier
    private readonly Panel _pierCard;
    private readonly ComboBox _pierType = Theme.Choice();
    private readonly NumericUpDown _pierWidth = Theme.Number(0.5m, 30m, 0.25m);
    private readonly ComboBox _pierSides = Theme.Choice();
    private readonly TextBox _pierNamePattern = Theme.Field();

    // Berths
    private readonly Panel _berthCard;
    private readonly NumericUpDown _berthWidth = Theme.Number(1m, 50m, 0.25m);
    private readonly NumericUpDown _berthLength = Theme.Number(1m, 150m, 0.5m);
    private readonly NumericUpDown _berthDepth = Theme.Number(0.1m, 50m, 0.1m);
    private readonly ComboBox _separators = Theme.Choice();
    private readonly NumericUpDown _berthGap = Theme.Number(0m, 20m, 0.1m);
    private readonly CheckBox _alignBerths = Theme.Check(Strings.AlignBerths);
    private readonly ComboBox _services = Theme.Choice();
    private readonly Label _separatorHint = Theme.Hint(string.Empty);
    private readonly TextBox _berthPattern = Theme.Field();
    private readonly NumericUpDown _berthStartNumber = Theme.Number(-99999m, 99999m, 1m, 0);
    private readonly NumericUpDown _berthIncrement = Theme.Number(-999m, 999m, 1m, 0);
    private readonly NumericUpDown _berthDigits = Theme.Number(1m, 9m, 1m, 0);
    private readonly Label _berthNameExample = Theme.Hint(string.Empty);

    // Land berths
    private readonly Panel _landBerthCard;
    private readonly NumericUpDown _landBerthWidth = Theme.Number(1m, 50m, 0.25m);
    private readonly NumericUpDown _landBerthLength = Theme.Number(1m, 150m, 0.5m);
    private readonly NumericUpDown _landBerthHeading = Theme.Number(-180m, 180m, 15m, 0);
    private readonly TextBox _landPattern = Theme.Field();
    private readonly NumericUpDown _landStartNumber = Theme.Number(-99999m, 99999m, 1m, 0);
    private readonly NumericUpDown _landIncrement = Theme.Number(-999m, 999m, 1m, 0);
    private readonly NumericUpDown _landDigits = Theme.Number(1m, 9m, 1m, 0);
    private readonly Label _landNameExample = Theme.Hint(string.Empty);

    // Rename
    private readonly Panel _renameCard;

    // Select
    private readonly Panel _selectCard;
    private readonly Label _selectCount = Theme.Hint(string.Empty);

    // Pedestals
    private readonly Panel _servicesCard;
    private readonly ComboBox _servicesChoice = Theme.Choice();

    // The mainland
    private readonly Panel _coastCard;
    private readonly ComboBox _coastScenery = Theme.Choice();
    private readonly Button _coastRemove;
    private readonly Label _coastState = Theme.Hint(string.Empty);

    // Trees
    private readonly Panel _treeCard;
    private readonly TrackBar _treeDensity = new() { Minimum = 0, Maximum = 60 };
    private readonly Label _treeDensityValue = new();

    // Erase
    private readonly Panel _eraseCard;

    // Reference image
    private readonly Panel _imageCard;
    private readonly Button _imageMove;
    private readonly Button _imageMeasure;
    private readonly TrackBar _imageOpacity = new() { Minimum = 0, Maximum = 100 };
    private readonly Label _imageOpacityValue = new();
    private readonly NumericUpDown _scaleLength = Theme.Number(0.01m, 100000m, 1m, 1);
    private readonly Button _applyScale;
    private readonly Label _imageState = Theme.Hint(string.Empty);
    private readonly CheckBox _imageAbove = Theme.Check(Strings.ImageAbove);
    private readonly CheckBox _imageShown = Theme.Check(Strings.ImageShown);
    private Button _clearScaleLine = null!;

    // Marina summary
    private readonly Panel _summaryCard;
    private readonly Label _summary = Theme.Hint(string.Empty);

    private bool _updating;
    private TableLayoutPanel _header = null!;

    /// <summary>
    /// How tall this panel would like to be: its heading plus the cards the current tool shows. The window uses it
    /// to sit the tool settings above the look or camera settings without leaving a gap.
    /// </summary>
    public int PreferredPanelHeight => _header.PreferredSize.Height + _stack.PreferredSize.Height;

    public InspectorPanel(MarinaVisualizer marina, Action loadImage)
    {
        _marina = marina;
        _loadImage = loadImage;
        BackColor = Theme.Background;
        Dock = DockStyle.Fill;

        // One full-width column again, so the hint under the tool's name wraps instead of being clipped.
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
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.Controls.Add(_toolName, 0, 0);
        header.Controls.Add(_toolHint, 0, 1);

        _landCard = BuildLandCard();
        _pierCard = BuildPierCard();
        _berthCard = BuildBerthCard();
        _landBerthCard = BuildLandBerthCard();
        _coastRemove = Theme.Action(Strings.CoastRemove, (_, _) => RemoveShoreline());
        _coastCard = BuildCoastCard();
        _treeCard = BuildTreeCard();
        _eraseCard = BuildEraseCard();
        _renameCard = BuildRenameCard();
        _servicesCard = BuildServicesCard();
        _selectCard = BuildSelectCard();
        _imageCard = BuildImageCard(out _imageMove, out _imageMeasure, out _applyScale);
        _summaryCard = BuildSummaryCard();

        _stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        foreach (var card in new[] { _landCard, _pierCard, _berthCard, _landBerthCard, _coastCard, _treeCard, _eraseCard, _renameCard, _servicesCard, _selectCard, _imageCard, _summaryCard })
        {
            // Top, not Fill: the column still decides the width, but the height stays the card's own.
            card.Dock = DockStyle.Top;
            _stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _stack.Controls.Add(card, 0, _stack.RowCount++);
        }

        // A last row that soaks up the space left over, so the cards stay at the top instead of spreading out.
        _scroller.Controls.Add(_stack);
        Controls.Add(_scroller);
        _header = header;
        Controls.Add(header);

        Wire();
        Sync(force: true);
    }

    private MarinaDesigner Designer => _marina.Designer;

    /// <summary>Brings the panel in line with the designer: the tool's name and hint, its settings, and which cards are shown.</summary>
    public void Sync(bool force = false)
    {
        if (_updating && !force) return;
        _updating = true;
        try
        {
            var designer = Designer;
            var tool = designer.Tool;
            _toolName.Text = ToolTitle(tool);
            _toolHint.Text = designer.ToolHint;

            _landCard.Visible = tool == DesignTool.DrawLandArea;
            _pierCard.Visible = tool == DesignTool.DrawPier;
            _berthCard.Visible = tool == DesignTool.AddBerths;
            _landBerthCard.Visible = tool == DesignTool.AddLandBerths;
            _treeCard.Visible = tool == DesignTool.PlantTrees || (tool == DesignTool.DrawLandArea && designer.LandKind == LandKind.Grass);
            _eraseCard.Visible = tool == DesignTool.Erase;
            _renameCard.Visible = tool == DesignTool.Rename;
            _servicesCard.Visible = tool == DesignTool.EditServices;
            _selectCard.Visible = tool == DesignTool.SelectArea;
            _coastCard.Visible = tool == DesignTool.DrawShoreline;
            _coastScenery.SelectedItem = Choice.Of(_coastScenery, designer.Scenery);
            _coastRemove.Enabled = _marina.Shoreline is not null;
            _coastState.Text = _marina.Shoreline is { } shore
                ? Strings.Format(Strings.CoastPresent, shore.Points.Count, shore.Scenery.GetDisplayName())
                : Strings.CoastNone;
            _selectCount.Text = Strings.Format(Strings.SelectCount, _marina.SelectedBerths.Count);
            _imageCard.Visible = tool is DesignTool.Navigate or DesignTool.MoveReferenceImage or DesignTool.MeasureScale;
            _summaryCard.Visible = tool is DesignTool.Navigate or DesignTool.Erase;

            _landKind.SelectedItem = Choice.Of(_landKind, designer.LandKind);
            SetNumber(_landHeight, designer.LandHeight);
            _pierType.SelectedItem = Choice.Of(_pierType, designer.PierType);
            SetNumber(_pierWidth, designer.PierWidth);
            _pierSides.SelectedItem = Choice.Of(_pierSides, designer.PierBerthingSides);
            SetText(_pierNamePattern, designer.PierNamePattern);

            SetNumber(_berthWidth, designer.BerthWidth);
            SetNumber(_berthLength, designer.BerthLength);
            SetNumber(_berthDepth, designer.BerthDepth);
            _separators.SelectedItem = Choice.Of(_separators, designer.BerthSeparators);
            SetNumber(_berthGap, designer.BerthGap);
            _alignBerths.Checked = designer.AlignBerthsToExisting;
            _services.SelectedItem = Choice.Of(_services, designer.BerthServices);
            _servicesChoice.SelectedItem = Choice.Of(_servicesChoice, designer.BerthServices);
            _separatorHint.Text = SeparatorHint(designer.BerthSeparators, designer.BerthWidth, designer.BerthGap);

            var naming = designer.BerthNaming;
            SetText(_berthPattern, naming.Pattern);
            SetNumber(_berthStartNumber, naming.StartNumber);
            SetNumber(_berthIncrement, naming.Increment);
            SetNumber(_berthDigits, naming.NumberDigits);
            _berthNameExample.Text = NamingExample(naming);

            SetNumber(_landBerthWidth, designer.BerthWidth);
            SetNumber(_landBerthLength, designer.BerthLength);
            SetNumber(_landBerthHeading, designer.LandBerthHeading);

            var ashore = designer.BerthNaming;
            SetText(_landPattern, ashore.LandPattern ?? ashore.Pattern);
            SetNumber(_landStartNumber, ashore.LandStartNumber ?? ashore.StartNumber);
            SetNumber(_landIncrement, ashore.LandIncrement ?? ashore.Increment);
            SetNumber(_landDigits, ashore.LandNumberDigits ?? ashore.NumberDigits);
            _landNameExample.Text = AshoreNameExample(ashore);
            _treeDensity.Value = Math.Clamp((int)MathF.Round(designer.TreeDensity), _treeDensity.Minimum, _treeDensity.Maximum);

            var image = designer.ReferenceImage;
            _imageMove.Enabled = image is not null;
            _imageMeasure.Enabled = image is not null;
            _imageOpacity.Enabled = image is not null;
            _imageAbove.Enabled = image is not null;
            _imageAbove.Checked = designer.ReferenceImageAboveScene;
            _imageShown.Enabled = image is not null;
            _imageShown.Checked = designer.ReferenceImageVisible;
            _clearScaleLine.Enabled = designer.ScaleLine is not null;
            _imageOpacity.Value = (int)MathF.Round(designer.ReferenceImageOpacity * 100f);
            _scaleLength.Enabled = designer.ScaleLine is not null;
            _applyScale.Enabled = designer.ScaleLine is not null;
            _imageState.Text = image is null
                ? Strings.ImageStateEmpty
                : designer.ScaleLine is { } line
                    ? string.Format(CultureInfo.CurrentCulture, Strings.ImageStateLine, System.Numerics.Vector2.Distance(line.Start, line.End))
                    : string.Format(CultureInfo.CurrentCulture, Strings.ImageStateReady, designer.ReferenceImageSize.X, designer.ReferenceImageSize.Y, designer.ReferenceImageMetersPerPixel);

            _summary.Text = Summary();
        }
        finally
        {
            _updating = false;
        }
    }

    private string Summary()
    {
        var berths = _marina.GetBerths();
        var piers = _marina.GetPiers();
        var land = _marina.GetLandAreas();
        var water = berths.Count(s => !s.IsOnLand);
        var ashore = berths.Count - water;
        var trees = land.Sum(l => l.Trees.Count);
        var (min, max) = _marina.GetLayout().ComputeBounds();
        var size = max - min;
        return string.Format(
            CultureInfo.CurrentCulture,
            Strings.Summary,
            berths.Count, water, ashore, piers.Count, land.Count, _marina.GetDividers().Count, trees, size.X, size.Y);
    }

    /// <summary>The library calls a berth a berth; this window calls it a berth everywhere, including in the tool hints.</summary>
    private static string ToolTitle(DesignTool tool) => tool switch
    {
        DesignTool.DrawLandArea => Strings.TitleLandArea,
        DesignTool.DrawPier => Strings.TitlePier,
        DesignTool.AddBerths => Strings.TitleBerths,
        DesignTool.AddLandBerths => Strings.TitleStorageAshore,
        DesignTool.PlantTrees => Strings.TitleTrees,
        DesignTool.Erase => Strings.TitleErase,
        DesignTool.Rename => Strings.TitleRename,
        DesignTool.EditServices => Strings.TitleServices,
        DesignTool.SelectArea => Strings.TitleSelect,
        DesignTool.DrawShoreline => Strings.TitleCoast,
        DesignTool.MoveReferenceImage => Strings.TitleMoveImage,
        DesignTool.MeasureScale => Strings.TitleMeasureScale,
        _ => Strings.TitleNavigate,
    };

    private static string SeparatorHint(BerthSeparator separator, float width, float gap) => separator switch
    {
        BerthSeparator.FingerPiers => Strings.SeparatorHintFingerPiers,
        BerthSeparator.None => Strings.SeparatorHintNone,
        BerthSeparator.FingerPier => Strings.SeparatorHintFingerPier,
        BerthSeparator.Piles => Strings.SeparatorHintPiles,
        BerthSeparator.Boom => Strings.SeparatorHintBoom,
        BerthSeparator.PairedFingerPiers => Strings.SeparatorHintPaired,
        BerthSeparator.SinglePile => Strings.SeparatorHintSinglePile,
        _ => string.Empty,
    } + (gap > 0f ? string.Format(CultureInfo.CurrentCulture, Strings.SeparatorHintSpacing, gap, width + gap) : string.Empty);

    // ---- Cards ----------------------------------------------------------------------------------

    private Panel BuildLandCard()
    {
        var card = Theme.Card(Strings.CardLandArea, out var table);
        Choice.Fill(_landKind, LandKind.Quay, LandKind.Grass, LandKind.Breakwater);
        Theme.Row(table, Strings.LandSurface, _landKind, Strings.LandSurfaceTip);
        Theme.Row(table, Strings.LandHeight, _landHeight, Strings.LandHeightTip);
        Theme.FullRow(table, Theme.Hint(Strings.LandHint));
        return card;
    }

    private Panel BuildPierCard()
    {
        var card = Theme.Card(Strings.CardPier, out var table);
        Choice.Fill(_pierType, Enum.GetValues<PierType>());
        Choice.Fill(_pierSides, PierSides.Both, PierSides.Left, PierSides.Right);
        Theme.Row(table, Strings.PierConstruction, _pierType, Strings.PierConstructionTip);
        Theme.Row(table, Strings.PierWidth, _pierWidth);
        Theme.Row(table, Strings.PierBerths, _pierSides, Strings.PierBerthsTip);
        Theme.Row(table, Strings.PierName, _pierNamePattern, Strings.PierNameTip);
        Theme.FullRow(table, Theme.Hint(Strings.PierHint));
        return card;
    }

    private Panel BuildBerthCard()
    {
        var card = Theme.Card(Strings.CardBerths, out var table);
        Choice.Fill(
            _separators,
            BerthSeparator.FingerPiers,
            BerthSeparator.PairedFingerPiers,
            BerthSeparator.FingerPier,
            BerthSeparator.Piles,
            BerthSeparator.SinglePile,
            BerthSeparator.Boom,
            BerthSeparator.None);
        Choice.Fill(_services, PierServices.None, PierServices.PowerAndWater, PierServices.Power, PierServices.Water);

        Theme.Row(table, Strings.BerthWidth, _berthWidth, Strings.BerthWidthTip);
        Theme.Row(table, Strings.BerthLength, _berthLength, Strings.BerthLengthTip);
        Theme.Row(table, Strings.BerthDepth, _berthDepth, Strings.BerthDepthTip);
        Theme.Row(table, Strings.BerthSeparator, _separators);
        Theme.Row(table, Strings.BerthGap, _berthGap, Strings.BerthGapTip);
        Theme.FullRow(table, _separatorHint);
        Theme.Row(table, Strings.BerthServices, _services, Strings.BerthServicesTip);
        Theme.FullRow(table, _alignBerths);
        Theme.FullRow(table, Theme.Hint(Strings.BerthHint));

        Theme.Section(table, Strings.BerthNamingHeading);
        Theme.Row(table, Strings.BerthNamePattern, _berthPattern, Strings.BerthNamePatternTip);
        Theme.Row(table, Strings.BerthStartNumber, _berthStartNumber, Strings.BerthStartNumberTip);
        Theme.Row(table, Strings.BerthIncrement, _berthIncrement, Strings.BerthIncrementTip);
        Theme.Row(table, Strings.BerthNumberDigits, _berthDigits, Strings.BerthNumberDigitsTip);
        Theme.FullRow(table, _berthNameExample);
        return card;
    }

    private Panel BuildLandBerthCard()
    {
        var card = Theme.Card(Strings.CardStorageAshore, out var table);
        Theme.Row(table, Strings.LandBerthWidth, _landBerthWidth);
        Theme.Row(table, Strings.LandBerthLength, _landBerthLength);
        Theme.Row(table, Strings.LandBerthHeading, _landBerthHeading, Strings.LandBerthHeadingTip);

        var compass = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        foreach (var (text, heading) in new[] { ("N", 0f), ("E", 90f), ("S", 180f), ("W", -90f) })
        {
            compass.Controls.Add(Theme.Action(text, (_, _) => Apply(d => d.LandBerthHeading = heading)));
        }

        Theme.FullRow(table, compass);
        Theme.FullRow(table, Theme.Hint(Strings.LandBerthHint));

        Theme.Section(table, Strings.BerthNamingHeading);
        Theme.Row(table, Strings.BerthNamePattern, _landPattern, Strings.LandNamePatternTip);
        Theme.Row(table, Strings.BerthStartNumber, _landStartNumber, Strings.LandStartNumberTip);
        Theme.Row(table, Strings.BerthIncrement, _landIncrement, Strings.BerthIncrementTip);
        Theme.Row(table, Strings.BerthNumberDigits, _landDigits, Strings.BerthNumberDigitsTip);
        Theme.FullRow(table, _landNameExample);
        return card;
    }

    private Panel BuildCoastCard()
    {
        var card = Theme.Card(Strings.CardCoast, out var table);
        Choice.Fill(_coastScenery, HinterlandScenery.Countryside, HinterlandScenery.Fields, HinterlandScenery.Town, HinterlandScenery.None);
        Theme.Row(table, Strings.CoastScenery, _coastScenery, Strings.CoastSceneryTip);
        Theme.FullRow(table, Theme.Hint(Strings.CoastHint));
        Theme.FullRow(table, Theme.Hint(Strings.CoastEndlessHint));
        Theme.FullRow(table, _coastState);
        Theme.FullRow(table, _coastRemove);
        return card;
    }

    private Panel BuildTreeCard()
    {
        var card = Theme.Card(Strings.CardTrees, out var table);
        Theme.FullRow(table, Theme.Slider(_treeDensity, _treeDensityValue, v => v == 0 ? Strings.TreeNone : v.ToString(CultureInfo.CurrentCulture)));
        Theme.FullRow(table, Theme.Hint(Strings.TreeHint));
        return card;
    }

    private Panel BuildEraseCard()
    {
        var card = Theme.Card(Strings.CardErase, out var table);
        Theme.FullRow(table, Theme.Hint(Strings.EraseHint));
        Theme.FullRow(table, Theme.Hint(Strings.EraseUndoHint));
        return card;
    }

    private Panel BuildSelectCard()
    {
        var card = Theme.Card(Strings.CardSelect, out var table);
        Theme.FullRow(table, Theme.Hint(Strings.SelectHint));
        Theme.FullRow(table, _selectCount);
        return card;
    }

    private Panel BuildServicesCard()
    {
        var card = Theme.Card(Strings.CardServices, out var table);
        Choice.Fill(_servicesChoice, PierServices.None, PierServices.PowerAndWater, PierServices.Power, PierServices.Water);
        Theme.Row(table, Strings.BerthServices, _servicesChoice, Strings.BerthServicesTip);
        Theme.FullRow(table, Theme.Hint(Strings.ServicesHint));
        Theme.FullRow(table, Theme.Hint(Strings.ServicesInheritHint));
        return card;
    }

    private Panel BuildRenameCard()
    {
        var card = Theme.Card(Strings.CardRename, out var table);
        Theme.FullRow(table, Theme.Hint(Strings.RenameHint));
        Theme.FullRow(table, Theme.Hint(Strings.RenameUndoHint));
        return card;
    }

    private Panel BuildImageCard(out Button move, out Button measure, out Button apply)
    {
        var card = Theme.Card(Strings.CardReferenceImage, out var table);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(262, 0), Margin = Padding.Empty };
        buttons.Controls.Add(Theme.Action(Strings.ImageLoad, (_, _) => _loadImage()));
        buttons.Controls.Add(Theme.Action(Strings.ImageFit, (_, _) => Apply(d => d.FocusReferenceImage())));
        buttons.Controls.Add(Theme.Action(Strings.ImageRemove, (_, _) => Apply(d => d.ClearReferenceImage())));
        Theme.FullRow(table, buttons);
        Theme.FullRow(table, _imageState);

        var tools = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(262, 0), Margin = Padding.Empty };
        move = Theme.Action(Strings.ImageMove, (_, _) => Apply(d => d.Tool = DesignTool.MoveReferenceImage));
        measure = Theme.Action(Strings.ImageMeasure, (_, _) => Apply(d => d.Tool = DesignTool.MeasureScale), primary: true);
        tools.Controls.Add(measure);
        tools.Controls.Add(move);
        Theme.FullRow(table, tools);

        var calibrate = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = Padding.Empty, MaximumSize = new Size(180, 0) };
        _scaleLength.Width = 84;
        apply = Theme.Action(Strings.ImageApply, (_, _) => Apply(d => d.CalibrateReferenceImage((float)_scaleLength.Value)));
        calibrate.Controls.Add(_scaleLength);
        calibrate.Controls.Add(apply);
        Theme.Row(table, Strings.ImageRealLength, calibrate);

        _clearScaleLine = Theme.Action(Strings.ImageClearScaleLine, (_, _) => Apply(d => d.ClearScaleLine()));
        Theme.Tips.SetToolTip(_clearScaleLine, Strings.ImageClearScaleLineTip);
        Theme.FullRow(table, _clearScaleLine);

        Theme.Row(table, Strings.ImageOpacity, Theme.Slider(_imageOpacity, _imageOpacityValue, v => Strings.Format(Strings.Percent, v)));
        Theme.FullRow(table, _imageShown);
        Theme.FullRow(table, _imageAbove);
        return card;
    }

    private Panel BuildSummaryCard()
    {
        var card = Theme.Card(Strings.CardSummary, out var table);
        Theme.FullRow(table, _summary);
        return card;
    }

    // ---- Wiring ---------------------------------------------------------------------------------

    private void Wire()
    {
        _landKind.SelectedIndexChanged += (_, _) => Apply(d => d.LandKind = Choice.Value<LandKind>(_landKind));
        _coastScenery.SelectedIndexChanged += (_, _) => Apply(d => d.Scenery = Choice.Value<HinterlandScenery>(_coastScenery));
        _landHeight.ValueChanged += (_, _) => Apply(d => d.LandHeight = (float)_landHeight.Value);
        _pierType.SelectedIndexChanged += (_, _) => Apply(d => d.PierType = Choice.Value<PierType>(_pierType));
        _pierWidth.ValueChanged += (_, _) => Apply(d => d.PierWidth = (float)_pierWidth.Value);
        _pierSides.SelectedIndexChanged += (_, _) => Apply(d => d.PierBerthingSides = Choice.Value<PierSides>(_pierSides));
        _pierNamePattern.TextChanged += (_, _) => Apply(d =>
        {
            if (!string.IsNullOrWhiteSpace(_pierNamePattern.Text)) d.PierNamePattern = _pierNamePattern.Text;
        });

        _berthWidth.ValueChanged += (_, _) => Apply(d => d.BerthWidth = (float)_berthWidth.Value);
        _berthLength.ValueChanged += (_, _) => Apply(d => d.BerthLength = (float)_berthLength.Value);
        _berthDepth.ValueChanged += (_, _) => Apply(d => d.BerthDepth = (float)_berthDepth.Value);
        _separators.SelectedIndexChanged += (_, _) => Apply(d => d.BerthSeparators = Choice.Value<BerthSeparator>(_separators));
        _berthGap.ValueChanged += (_, _) => Apply(d => d.BerthGap = (float)_berthGap.Value);
        _alignBerths.CheckedChanged += (_, _) => Apply(d => d.AlignBerthsToExisting = _alignBerths.Checked);
        _services.SelectedIndexChanged += (_, _) => Apply(d => d.BerthServices = Choice.Value<PierServices>(_services));
        _servicesChoice.SelectedIndexChanged += (_, _) => Apply(d => d.BerthServices = Choice.Value<PierServices>(_servicesChoice));
        _berthPattern.TextChanged += (_, _) => ApplyNaming(n => string.IsNullOrWhiteSpace(_berthPattern.Text) ? n : n with { Pattern = _berthPattern.Text });
        _berthStartNumber.ValueChanged += (_, _) => ApplyNaming(n => n with { StartNumber = (int)_berthStartNumber.Value });
        _berthIncrement.ValueChanged += (_, _) => ApplyNaming(n => (int)_berthIncrement.Value == 0 ? n : n with { Increment = (int)_berthIncrement.Value });
        _berthDigits.ValueChanged += (_, _) => ApplyNaming(n => n with { NumberDigits = (int)_berthDigits.Value });

        _landBerthWidth.ValueChanged += (_, _) => Apply(d => d.BerthWidth = (float)_landBerthWidth.Value);
        _landBerthLength.ValueChanged += (_, _) => Apply(d => d.BerthLength = (float)_landBerthLength.Value);
        _landBerthHeading.ValueChanged += (_, _) => Apply(d => d.LandBerthHeading = (float)_landBerthHeading.Value);
        _landPattern.TextChanged += (_, _) => ApplyNaming(n => string.IsNullOrWhiteSpace(_landPattern.Text) ? n : n with { LandPattern = _landPattern.Text });
        _landStartNumber.ValueChanged += (_, _) => ApplyNaming(n => n with { LandStartNumber = (int)_landStartNumber.Value });
        _landIncrement.ValueChanged += (_, _) => ApplyNaming(n => (int)_landIncrement.Value == 0 ? n : n with { LandIncrement = (int)_landIncrement.Value });
        _landDigits.ValueChanged += (_, _) => ApplyNaming(n => n with { LandNumberDigits = (int)_landDigits.Value });
        _treeDensity.ValueChanged += (_, _) => Apply(d => d.TreeDensity = _treeDensity.Value);

        _imageOpacity.ValueChanged += (_, _) => Apply(d => d.ReferenceImageOpacity = _imageOpacity.Value / 100f);
        _imageAbove.CheckedChanged += (_, _) => Apply(d => d.ReferenceImageAboveScene = _imageAbove.Checked);
        _imageShown.CheckedChanged += (_, _) => Apply(d => d.ReferenceImageVisible = _imageShown.Checked);
    }

    /// <summary>Runs a change on the designer, unless the controls are being filled in from it.</summary>
    /// <summary>Takes the mainland away, leaving the marina in open water. Ctrl+Z brings it back.</summary>
    private void RemoveShoreline()
    {
        if (_updating) return;
        Designer.DeleteShoreline();
        Sync(force: true);
    }

    private void Apply(Action<MarinaDesigner> change)
    {
        if (_updating) return;
        try
        {
            change(Designer);
        }
        catch (ArgumentOutOfRangeException)
        {
            // The control ranges match the designer's; ignore anything typed past them.
        }

        Sync(force: true);
    }

    /// <summary>Changes the berth naming scheme, ignoring a combination the designer would refuse.</summary>
    private void ApplyNaming(Func<BerthNamingScheme, BerthNamingScheme> change) => Apply(d =>
    {
        var naming = change(d.BerthNaming);
        if (!naming.Validate().Any()) d.BerthNaming = naming;
    });

    /// <summary>The first three names the slots ashore would get, mirroring the preview on the berth card.</summary>
    private string AshoreNameExample(BerthNamingScheme naming)
    {
        var land = _marina.GetLandAreas().FirstOrDefault()
            ?? new LandArea("yard", new[] { new System.Numerics.Vector2(0, 0), new System.Numerics.Vector2(10, 0), new System.Numerics.Vector2(10, 10) }, 1f);
        var start = naming.LandStartNumber ?? naming.StartNumber;
        var step = naming.LandIncrement ?? naming.Increment;
        var numbers = Enumerable.Range(0, 3).Select(i => start + i * step);
        return Strings.Format(Strings.BerthNamingExample, string.Join(", ", numbers.Select(n => naming.Format(land, n))));
    }

    /// <summary>The first three names the scheme would give, so the effect of a pattern is visible while typing it.</summary>
    private string NamingExample(BerthNamingScheme naming)
    {
        var pier = _marina.GetPiers().FirstOrDefault() ?? new Pier("A", "Pier A", System.Numerics.Vector2.Zero, 0f, 20f);
        var numbers = Enumerable.Range(0, 3).Select(i => naming.StartNumber + i * naming.Increment);
        return Strings.Format(Strings.BerthNamingExample, string.Join(", ", numbers.Select(n => naming.Format(pier, PierSide.Left, n))));
    }

    private static void SetText(TextBox box, string value)
    {
        if (box.Text == value || box.Focused) return;
        box.Text = value;
    }

    private static void SetNumber(NumericUpDown control, float value)
    {
        var rounded = Math.Clamp(Math.Round((decimal)value, control.DecimalPlaces), control.Minimum, control.Maximum);
        if (control.Value != rounded) control.Value = rounded;
    }

    /// <summary>Combo box entries that show friendly text and carry a value.</summary>
    private sealed record Choice<T>(string Text, T Value)
    {
        public override string ToString() => Text;
    }

    private static class Choice
    {
        /// <summary>Fills a combo box with values, each shown under the name the core library gives it.</summary>
        public static void Fill<T>(ComboBox combo, params T[] values) => Fill(combo, (IEnumerable<T>)values);

        public static void Fill<T>(ComboBox combo, IEnumerable<T> values)
        {
            combo.Items.Clear();
            foreach (var value in values) combo.Items.Add(new Choice<T>(Describe(value), value));
            combo.SelectedIndex = 0;
        }

        private static string Describe<T>(T value) => value switch
        {
            LandKind kind => kind.GetDisplayName(),
            PierType type => Core.Domain.Pier.GetDisplayName(type),
            PierSides sides => sides.GetDisplayName(),
            PierServices services => services.GetDisplayName(),
            BerthSeparator separator => separator.GetDisplayName(),
            _ => value?.ToString() ?? string.Empty,
        };

        public static object? Of<T>(ComboBox combo, T value) => combo.Items.Cast<Choice<T>>().FirstOrDefault(c => Equals(c.Value, value));

        public static T Value<T>(ComboBox combo) => ((Choice<T>)combo.SelectedItem!).Value;
    }
}
