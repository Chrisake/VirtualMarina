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
/// <remarks>
/// The cards are stacked in a single column that is always the full width of the panel. A card docked into that column is
/// given its width by the layout engine, so dragging the splitter resizes the cards and the text inside them without
/// anything being measured here.
/// </remarks>
internal sealed class InspectorPanel : SidePanel
{
    private readonly DesignerSession _session;
    private readonly Action _loadImage;
    private readonly Label _toolName = new() { Font = Theme.PanelTitle, ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
    private readonly Label _toolHint = new() { Font = Theme.Body, ForeColor = Theme.TextSoft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };

    /// <summary>Marks a naming pattern the designer would refuse, with the reason on its tooltip.</summary>
    private readonly ErrorProvider _errors = new() { BlinkStyle = ErrorBlinkStyle.NeverBlink };

    // Land area
    private readonly Panel _landCard;
    private readonly ComboBox _landKind = Theme.Choice();
    private readonly NumericUpDown _landHeight = Theme.Number(DesignerRanges.LandHeight);

    // Pier
    private readonly Panel _pierCard;
    private readonly ComboBox _pierType = Theme.Choice();
    private readonly NumericUpDown _pierWidth = Theme.Number(DesignerRanges.PierWidth);
    private readonly ComboBox _pierSides = Theme.Choice();
    private readonly TextBox _pierNamePattern = Theme.Field();

    // Berths
    private readonly Panel _berthCard;
    private readonly NumericUpDown _berthWidth = Theme.Number(DesignerRanges.BerthWidth);
    private readonly NumericUpDown _berthLength = Theme.Number(DesignerRanges.BerthLength);
    private readonly NumericUpDown _berthDepth = Theme.Number(DesignerRanges.BerthDepth);
    private readonly ComboBox _separators = Theme.Choice();
    private readonly NumericUpDown _berthGap = Theme.Number(DesignerRanges.BerthGap);
    private readonly CheckBox _alignBerths = Theme.Check(Strings.AlignBerths);
    private readonly ComboBox _services = Theme.Choice();
    private readonly Label _separatorHint = Theme.Hint(string.Empty);
    private readonly TextBox _berthPattern = Theme.Field();
    private readonly NumericUpDown _berthStartNumber = Theme.Number(DesignerRanges.NamingStart);
    private readonly NumericUpDown _berthIncrement = Theme.Number(DesignerRanges.NamingIncrement);
    private readonly NumericUpDown _berthDigits = Theme.Number(DesignerRanges.NamingDigits);
    private readonly Label _berthNameExample = Theme.Hint(string.Empty);

    // Land berths
    private readonly Panel _landBerthCard;
    private readonly NumericUpDown _landBerthWidth = Theme.Number(DesignerRanges.BerthWidth);
    private readonly NumericUpDown _landBerthLength = Theme.Number(DesignerRanges.BerthLength);
    private readonly NumericUpDown _landBerthHeading = Theme.Number(DesignerRanges.LandBerthHeading);
    private readonly TextBox _landPattern = Theme.Field();
    private readonly NumericUpDown _landStartNumber = Theme.Number(DesignerRanges.NamingStart);
    private readonly NumericUpDown _landIncrement = Theme.Number(DesignerRanges.NamingIncrement);
    private readonly NumericUpDown _landDigits = Theme.Number(DesignerRanges.NamingDigits);
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
    private readonly TrackBar _treeDensity = new() { Minimum = (int)DesignerRanges.TreeDensity.Min, Maximum = (int)DesignerRanges.TreeDensity.Max };
    private readonly Label _treeDensityValue = new();

    // Erase
    private readonly Panel _eraseCard;

    // Reference image
    private readonly Panel _imageCard;
    private readonly Button _imageMove;
    private readonly Button _imageMeasure;
    private readonly TrackBar _imageOpacity = new() { Minimum = (int)DesignerRanges.ImageOpacityPercent.Min, Maximum = (int)DesignerRanges.ImageOpacityPercent.Max };
    private readonly Label _imageOpacityValue = new();
    private readonly NumericUpDown _scaleLength = Theme.Number(DesignerRanges.ScaleLength);
    private readonly Button _applyScale;
    private readonly Label _imageState = Theme.Hint(string.Empty);
    private readonly CheckBox _imageAbove = Theme.Check(Strings.ImageAbove);
    private readonly CheckBox _imageShown = Theme.Check(Strings.ImageShown);
    private Button _clearScaleLine = null!;

    // Marina summary
    private readonly Panel _summaryCard;
    private readonly Label _summary = Theme.Hint(string.Empty);

    /// <summary>True while the controls are being filled in from the designer, so they do not write back what they are given.</summary>
    private bool _updating;

    /// <summary>True while a refresh is waiting to run: however many changes arrive meanwhile, it runs once.</summary>
    private bool _syncPending;

    /// <summary>True when the waiting refresh should also put the designer's text into a field being typed in.</summary>
    private bool _overwriteFocused;

    public InspectorPanel(DesignerSession session, Action loadImage)
    {
        _session = session;
        _loadImage = loadImage;
        BackColor = Theme.Background;
        Dock = DockStyle.Fill;

        // One full-width column, so the hint under the tool's name wraps instead of being clipped.
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

        Stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        foreach (var card in new[] { _landCard, _pierCard, _berthCard, _landBerthCard, _coastCard, _treeCard, _eraseCard, _renameCard, _servicesCard, _selectCard, _imageCard, _summaryCard })
        {
            // Top, not Fill: the column still decides the width, but the height stays the card's own.
            card.Dock = DockStyle.Top;
            Stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Stack.Controls.Add(card, 0, Stack.RowCount++);
        }

        Scroller.Controls.Add(Stack);
        Controls.Add(Scroller);
        SetHeader(header);
        Controls.Add(header);

        foreach (var box in new Control[] { _pierNamePattern, _berthPattern, _landPattern, _berthIncrement, _landIncrement })
        {
            _errors.SetIconAlignment(box, ErrorIconAlignment.MiddleLeft);
        }

        Wire();
        Sync(overwriteFocused: true);
    }

    private MarinaVisualizer Marina => _session.Marina;

    private MarinaDesigner Designer => _session.Designer;

    /// <summary>
    /// Asks for the panel to be brought in line with the designer once the current burst of changes is over. A slider
    /// dragged, or an undo that changes a dozen settings, costs one refresh rather than one per change.
    /// </summary>
    /// <param name="overwriteFocused">
    /// Also put the designer's text into a field that has the focus. After an undo or a redo the field is out of date and
    /// should show what the designer holds; while typing it should be left alone.
    /// </param>
    public void RequestSync(bool overwriteFocused = false)
    {
        _overwriteFocused |= overwriteFocused;
        if (_syncPending || IsDisposed) return;
        _syncPending = true;
        if (IsHandleCreated) BeginInvoke(RunPendingSync);
        else RunPendingSync();
    }

    private void RunPendingSync()
    {
        _syncPending = false;
        var overwrite = _overwriteFocused;
        _overwriteFocused = false;
        if (!IsDisposed) Sync(overwrite);
    }

    /// <summary>Brings the panel in line with the designer: the tool's name and hint, its settings, and which cards are shown.</summary>
    /// <param name="overwriteFocused">Also refresh a text field that has the focus.</param>
    private void Sync(bool overwriteFocused)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            var designer = Designer;
            var tool = designer.Tool;
            _toolName.Text = DesignerText.ToolTitle(tool);
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
            _imageCard.Visible = tool is DesignTool.Navigate or DesignTool.MoveReferenceImage or DesignTool.MeasureScale;
            _summaryCard.Visible = tool is DesignTool.Navigate or DesignTool.Erase;

            _coastScenery.SelectedItem = Choice.Of(_coastScenery, designer.Scenery);
            _coastRemove.Enabled = Marina.Shoreline is not null;
            _coastState.Text = DesignerText.CoastState(Marina);
            _selectCount.Text = Strings.Format(Strings.SelectCount, Marina.SelectedBerths.Count);

            _landKind.SelectedItem = Choice.Of(_landKind, designer.LandKind);
            SetNumber(_landHeight, designer.LandHeight);
            _pierType.SelectedItem = Choice.Of(_pierType, designer.PierType);
            SetNumber(_pierWidth, designer.PierWidth);
            _pierSides.SelectedItem = Choice.Of(_pierSides, designer.PierBerthingSides);
            SetText(_pierNamePattern, designer.PierNamePattern, overwriteFocused);

            SetNumber(_berthWidth, designer.BerthWidth);
            SetNumber(_berthLength, designer.BerthLength);
            SetNumber(_berthDepth, designer.BerthDepth);
            _separators.SelectedItem = Choice.Of(_separators, designer.BerthSeparators);
            SetNumber(_berthGap, designer.BerthGap);
            _alignBerths.Checked = designer.AlignBerthsToExisting;
            _services.SelectedItem = Choice.Of(_services, designer.BerthServices);
            _servicesChoice.SelectedItem = Choice.Of(_servicesChoice, designer.BerthServices);
            _separatorHint.Text = DesignerText.SeparatorHint(designer.BerthSeparators, designer.BerthWidth, designer.BerthGap);

            var naming = designer.BerthNaming;
            SetText(_berthPattern, naming.Pattern, overwriteFocused);
            SetNumber(_berthStartNumber, naming.StartNumber);
            SetNumber(_berthIncrement, naming.Increment);
            SetNumber(_berthDigits, naming.NumberDigits);
            if (_errors.GetError(_berthPattern).Length == 0 || overwriteFocused) _berthNameExample.Text = DesignerText.NamingExample(Marina, naming);

            SetNumber(_landBerthWidth, designer.BerthWidth);
            SetNumber(_landBerthLength, designer.BerthLength);
            SetNumber(_landBerthHeading, designer.LandBerthHeading);
            SetText(_landPattern, naming.LandPattern ?? naming.Pattern, overwriteFocused);
            SetNumber(_landStartNumber, naming.LandStartNumber ?? naming.StartNumber);
            SetNumber(_landIncrement, naming.LandIncrement ?? naming.Increment);
            SetNumber(_landDigits, naming.LandNumberDigits ?? naming.NumberDigits);
            if (_errors.GetError(_landPattern).Length == 0 || overwriteFocused) _landNameExample.Text = DesignerText.AshoreNameExample(Marina, naming);
            if (overwriteFocused) ClearErrors();

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
            _imageOpacity.Value = Math.Clamp((int)MathF.Round(designer.ReferenceImageOpacity * 100f), _imageOpacity.Minimum, _imageOpacity.Maximum);
            _scaleLength.Enabled = designer.ScaleLine is not null;
            _applyScale.Enabled = designer.ScaleLine is not null;
            _imageState.Text = DesignerText.ImageState(designer);

            // Counting the whole marina and measuring its bounds is the dearest line on the panel; only when it shows.
            if (_summaryCard.Visible) _summary.Text = DesignerText.Summary(Marina);
        }
        finally
        {
            _updating = false;
        }

        // Each tool shows a different set of cards, so how tall the panel wants to be changes with it.
        ContentChanged();
    }

    // ---- Cards ----------------------------------------------------------------------------------

    private Panel BuildLandCard()
    {
        var card = Theme.Card(Strings.CardLandArea, out var table);
        Choice.Fill(_landKind, DesignerChoices.LandKinds);
        Theme.Row(table, Strings.LandSurface, _landKind, Strings.LandSurfaceTip);
        Theme.Row(table, Strings.LandHeight, _landHeight, Strings.LandHeightTip);
        Theme.FullRow(table, Theme.Hint(Strings.LandHint));
        return card;
    }

    private Panel BuildPierCard()
    {
        var card = Theme.Card(Strings.CardPier, out var table);
        Choice.Fill(_pierType, DesignerChoices.PierTypes);
        Choice.Fill(_pierSides, DesignerChoices.BerthingSides);
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
        Choice.Fill(_separators, DesignerChoices.Separators);
        Choice.Fill(_services, DesignerChoices.Services);

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
        foreach (var (text, heading) in DesignerChoices.CompassHeadings)
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
        Choice.Fill(_coastScenery, DesignerChoices.Sceneries);
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

    private static Panel BuildEraseCard()
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
        Choice.Fill(_servicesChoice, DesignerChoices.Services);
        Theme.Row(table, Strings.BerthServices, _servicesChoice, Strings.BerthServicesTip);
        Theme.FullRow(table, Theme.Hint(Strings.ServicesHint));
        Theme.FullRow(table, Theme.Hint(Strings.ServicesInheritHint));
        return card;
    }

    private static Panel BuildRenameCard()
    {
        var card = Theme.Card(Strings.CardRename, out var table);
        Theme.FullRow(table, Theme.Hint(Strings.RenameHint));
        Theme.FullRow(table, Theme.Hint(Strings.RenameUndoHint));
        return card;
    }

    private Panel BuildImageCard(out Button move, out Button measure, out Button apply)
    {
        var card = Theme.Card(Strings.CardReferenceImage, out var table);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        buttons.Controls.Add(Theme.Action(Strings.ImageLoad, (_, _) => _loadImage()));
        buttons.Controls.Add(Theme.Action(Strings.ImageFit, (_, _) => Apply(d => d.FocusReferenceImage())));
        buttons.Controls.Add(Theme.Action(Strings.ImageRemove, (_, _) => Apply(d => d.ClearReferenceImage())));
        Theme.FullRow(table, buttons);
        Theme.FullRow(table, _imageState);

        var tools = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        move = Theme.Action(Strings.ImageMove, (_, _) => Apply(d => d.Tool = DesignTool.MoveReferenceImage));
        measure = Theme.Action(Strings.ImageMeasure, (_, _) => Apply(d => d.Tool = DesignTool.MeasureScale), primary: true);
        tools.Controls.Add(measure);
        tools.Controls.Add(move);
        Theme.FullRow(table, tools);

        var calibrate = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = Padding.Empty, Dock = DockStyle.Fill };
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
        _pierNamePattern.TextChanged += (_, _) => ApplyPierPattern();

        _berthWidth.ValueChanged += (_, _) => Apply(d => d.BerthWidth = (float)_berthWidth.Value);
        _berthLength.ValueChanged += (_, _) => Apply(d => d.BerthLength = (float)_berthLength.Value);
        _berthDepth.ValueChanged += (_, _) => Apply(d => d.BerthDepth = (float)_berthDepth.Value);
        _separators.SelectedIndexChanged += (_, _) => Apply(d => d.BerthSeparators = Choice.Value<BerthSeparator>(_separators));
        _berthGap.ValueChanged += (_, _) => Apply(d => d.BerthGap = (float)_berthGap.Value);
        _alignBerths.CheckedChanged += (_, _) => Apply(d => d.AlignBerthsToExisting = _alignBerths.Checked);
        _services.SelectedIndexChanged += (_, _) => Apply(d => d.BerthServices = Choice.Value<PierServices>(_services));
        _servicesChoice.SelectedIndexChanged += (_, _) => Apply(d => d.BerthServices = Choice.Value<PierServices>(_servicesChoice));
        _berthPattern.TextChanged += (_, _) => ApplyNaming(_berthPattern, _berthNameExample, n => n with { Pattern = _berthPattern.Text });
        _berthStartNumber.ValueChanged += (_, _) => ApplyNaming(_berthStartNumber, _berthNameExample, n => n with { StartNumber = (int)_berthStartNumber.Value });
        _berthIncrement.ValueChanged += (_, _) => ApplyNaming(_berthIncrement, _berthNameExample, n => n with { Increment = (int)_berthIncrement.Value });
        _berthDigits.ValueChanged += (_, _) => ApplyNaming(_berthDigits, _berthNameExample, n => n with { NumberDigits = (int)_berthDigits.Value });

        _landBerthWidth.ValueChanged += (_, _) => Apply(d => d.BerthWidth = (float)_landBerthWidth.Value);
        _landBerthLength.ValueChanged += (_, _) => Apply(d => d.BerthLength = (float)_landBerthLength.Value);
        _landBerthHeading.ValueChanged += (_, _) => Apply(d => d.LandBerthHeading = (float)_landBerthHeading.Value);
        _landPattern.TextChanged += (_, _) => ApplyNaming(_landPattern, _landNameExample, n => n with { LandPattern = _landPattern.Text });
        _landStartNumber.ValueChanged += (_, _) => ApplyNaming(_landStartNumber, _landNameExample, n => n with { LandStartNumber = (int)_landStartNumber.Value });
        _landIncrement.ValueChanged += (_, _) => ApplyNaming(_landIncrement, _landNameExample, n => n with { LandIncrement = (int)_landIncrement.Value });
        _landDigits.ValueChanged += (_, _) => ApplyNaming(_landDigits, _landNameExample, n => n with { LandNumberDigits = (int)_landDigits.Value });
        _treeDensity.ValueChanged += (_, _) => Apply(d => d.TreeDensity = _treeDensity.Value);

        _imageOpacity.ValueChanged += (_, _) => Apply(d => d.ReferenceImageOpacity = _imageOpacity.Value / 100f);
        _imageAbove.CheckedChanged += (_, _) => Apply(d => d.ReferenceImageAboveScene = _imageAbove.Checked);
        _imageShown.CheckedChanged += (_, _) => Apply(d => d.ReferenceImageVisible = _imageShown.Checked);
    }

    /// <summary>Takes the mainland away, leaving the marina in open water. Ctrl+Z brings it back.</summary>
    private void RemoveShoreline()
    {
        if (_updating) return;
        _session.RemoveShoreline();
        RequestSync();
    }

    /// <summary>Runs a change on the designer, unless the controls are being filled in from it.</summary>
    private void Apply(Action<MarinaDesigner> change)
    {
        if (_updating) return;
        try
        {
            change(Designer);
        }
        catch (ArgumentException)
        {
            // The control ranges match the designer's; a value it still refuses is put back by the refresh below.
        }

        // The designer's StateChanged asks for the same refresh; both land in the one that runs.
        RequestSync();
    }

    /// <summary>A pier name pattern takes effect once it is not blank; a blank one is marked and left unapplied.</summary>
    private void ApplyPierPattern()
    {
        if (_updating) return;
        var text = _pierNamePattern.Text;
        _errors.SetError(_pierNamePattern, string.IsNullOrWhiteSpace(text) ? Strings.PierNameTip : string.Empty);
        if (!string.IsNullOrWhiteSpace(text)) Apply(d => d.PierNamePattern = text);
    }

    /// <summary>
    /// Changes the berth naming scheme. A combination the designer would refuse (a blank pattern, counting by zero) is
    /// not applied: the field is marked, the reason is on its tooltip and in place of the example underneath.
    /// </summary>
    private void ApplyNaming(Control source, Label example, Func<BerthNamingScheme, BerthNamingScheme> change)
    {
        if (_updating) return;
        var naming = change(Designer.BerthNaming);
        var problem = DesignerText.NamingProblem(naming);
        _errors.SetError(source, problem ?? string.Empty);
        if (problem is not null)
        {
            example.Text = problem;
            return;
        }

        Apply(d => d.BerthNaming = naming);
    }

    private void ClearErrors()
    {
        foreach (var box in new Control[] { _pierNamePattern, _berthPattern, _landPattern, _berthStartNumber, _berthIncrement, _berthDigits, _landStartNumber, _landIncrement, _landDigits })
        {
            _errors.SetError(box, string.Empty);
        }
    }

    private static void SetText(TextBox box, string value, bool overwriteFocused)
    {
        if (box.Text == value || (box.Focused && !overwriteFocused)) return;
        box.Text = value;
    }

    private static void SetNumber(NumericUpDown control, float value)
    {
        var rounded = Math.Clamp(Math.Round((decimal)value, control.DecimalPlaces), control.Minimum, control.Maximum);
        if (control.Value != rounded) control.Value = rounded;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _errors.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>Combo box entries that show friendly text and carry a value.</summary>
    private sealed record Choice<T>(string Text, T Value)
    {
        public override string ToString() => Text;
    }

    private static class Choice
    {
        /// <summary>Fills a combo box with values, each shown under the name the core library gives it.</summary>
        public static void Fill<T>(ComboBox combo, IEnumerable<T> values)
            where T : struct, Enum
        {
            combo.Items.Clear();
            foreach (var value in values) combo.Items.Add(new Choice<T>(DesignerChoices.Describe(value), value));
            combo.SelectedIndex = 0;
        }

        public static Choice<T>? Of<T>(ComboBox combo, T value) => combo.Items.Cast<Choice<T>>().FirstOrDefault(c => Equals(c.Value, value));

        public static T Value<T>(ComboBox combo) => ((Choice<T>)combo.SelectedItem!).Value;
    }
}
