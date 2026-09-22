using System.ComponentModel;
using System.Globalization;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.WinForms.Resources;

namespace VirtualMarina.WinForms;

/// <summary>
/// Ready-made tool panel for <see cref="MarinaDesigner"/>: design mode, tools (land area, pier, berths, land berths, trees, erase),
/// their settings, undo, and the reference image (load, opacity, move, scale calibration). Put it next to a
/// <see cref="MarinaViewControl"/> and set <see cref="Marina"/>.
/// </summary>
/// <example>
/// <code>
/// var panel = new MarinaDesignerPanel { Dock = DockStyle.Right, Width = 320, Marina = marinaView.Marina };
/// form.Controls.Add(panel);
/// marinaView.Marina.Designer.ElementCreated += (s, e) => SaveToErp(marinaView.Marina.ExportObjects());
/// </code>
/// </example>
[ToolboxItem(true)]
[Description("Tools and settings for designing a marina layout in a MarinaViewControl.")]
public sealed class MarinaDesignerPanel : UserControl
{
    private readonly CheckBox _chkActive = new() { Text = Strings.DesignMode, AutoSize = true };
    private readonly Dictionary<DesignTool, CheckBox> _toolButtons = new();
    private readonly Label _lblHint = new() { AutoSize = true, MaximumSize = new Size(280, 0), ForeColor = SystemColors.GrayText, Padding = new Padding(0, 4, 0, 4) };

    private readonly Button _btnUndo = new() { Text = Strings.Undo, AutoSize = true };
    private readonly Label _lblUndo = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(6, 6, 0, 0) };

    private readonly ComboBox _cmbLandKind = CreateCombo();
    private readonly NumericUpDown _nudLandHeight = CreateNumber(0m, 50m, 0.1m, 2);
    private readonly TrackBar _trkTreeDensity = new() { Minimum = 0, Maximum = 100, TickFrequency = 10, Dock = DockStyle.Fill, AutoSize = false, Height = 28 };
    private readonly Label _lblTreeDensity = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    private readonly ComboBox _cmbPierType = CreateCombo();
    private readonly NumericUpDown _nudPierWidth = CreateNumber(0.5m, 30m, 0.1m, 2);
    private readonly ComboBox _cmbPierSides = CreateCombo();

    private readonly NumericUpDown _nudBerthWidth = CreateNumber(1m, 50m, 0.1m, 2);
    private readonly NumericUpDown _nudBerthLength = CreateNumber(1m, 150m, 0.5m, 2);
    private readonly NumericUpDown _nudBerthDepth = CreateNumber(0.1m, 50m, 0.1m, 2);
    private readonly ComboBox _cmbBerthSeparators = CreateCombo();
    private readonly NumericUpDown _nudBerthGap = CreateNumber(0m, 20m, 0.1m, 2);
    private readonly CheckBox _chkAlignBerths = new() { Text = Strings.AlignBerths, AutoSize = true };
    private readonly ComboBox _cmbBerthServices = CreateCombo();
    private readonly NumericUpDown _nudLandBerthHeading = CreateNumber(-180m, 180m, 15m, 0);

    private readonly Button _btnLoadImage = new() { Text = Strings.LoadImage, AutoSize = true };
    private readonly Button _btnClearImage = new() { Text = Strings.RemoveImage, AutoSize = true };
    private readonly Button _btnFitImage = new() { Text = Strings.ViewImage, AutoSize = true };
    private readonly Button _btnTopDown = new() { Text = Strings.TopViewNorthUp, AutoSize = true };
    private readonly CheckBox _chkImageVisible = new() { Text = Strings.ShowImage, AutoSize = true };
    private readonly CheckBox _chkImageAbove = new() { Text = Strings.ImageAboveScene, AutoSize = true };
    private readonly TrackBar _trkOpacity = new() { Minimum = 0, Maximum = 100, TickFrequency = 10, Dock = DockStyle.Fill, AutoSize = false, Height = 28 };
    private readonly NumericUpDown _nudMetersPerPixel = CreateNumber(0.0001m, 1000m, 0.01m, 4);
    private readonly NumericUpDown _nudScaleLength = CreateNumber(0.01m, 100000m, 1m, 2);
    private readonly Button _btnCalibrate = new() { Text = Strings.ApplyScale, AutoSize = true };
    private readonly Label _lblScaleLine = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    private MarinaVisualizer? _marina;
    private bool _updating;

    /// <summary>Creates the panel. Set <see cref="Marina"/> to connect it.</summary>
    public MarinaDesignerPanel()
    {
        AutoScroll = true;
        Padding = new Padding(4);

        var stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
        };

        // Designer and tools
        _chkActive.Font = new Font(Font, FontStyle.Bold);
        var tools = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(290, 0), Margin = new Padding(0, 4, 0, 0) };
        foreach (var tool in new[] { DesignTool.Navigate, DesignTool.DrawLandArea, DesignTool.DrawPier, DesignTool.AddBerths, DesignTool.AddLandBerths, DesignTool.PlantTrees, DesignTool.Erase })
        {
            AddTool(tools, tool);
        }
        var undo = new FlowLayoutPanel { AutoSize = true, WrapContents = false, MaximumSize = new Size(290, 0), Margin = Padding.Empty };
        undo.Controls.Add(_btnUndo);
        undo.Controls.Add(_lblUndo);
        var mode = Section(Strings.SectionDesigner, out var modeTable);
        modeTable.Controls.Add(_chkActive, 0, 0);
        modeTable.SetColumnSpan(_chkActive, 2);
        modeTable.Controls.Add(tools, 0, 1);
        modeTable.SetColumnSpan(tools, 2);
        modeTable.Controls.Add(_lblHint, 0, 2);
        modeTable.SetColumnSpan(_lblHint, 2);
        modeTable.Controls.Add(undo, 0, 3);
        modeTable.SetColumnSpan(undo, 2);
        stack.Controls.Add(mode);

        // Land
        foreach (var kind in Enum.GetValues<LandKind>()) _cmbLandKind.Items.Add(new Choice<LandKind>(kind, kind.GetDisplayName()));
        var trees = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        trees.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        trees.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        trees.Controls.Add(_trkTreeDensity, 0, 0);
        trees.Controls.Add(_lblTreeDensity, 1, 0);
        var land = Section(Strings.SectionLandArea, out var landTable);
        Row(landTable, Strings.LandType, _cmbLandKind);
        Row(landTable, Strings.LandHeight, _nudLandHeight);
        Row(landTable, Strings.TreeCoverage, trees);
        stack.Controls.Add(land);

        // Pier
        foreach (var type in Enum.GetValues<PierType>()) _cmbPierType.Items.Add(new Choice<PierType>(type, Core.Domain.Pier.GetDisplayName(type)));
        foreach (var sides in new[] { PierSides.Both, PierSides.Left, PierSides.Right }) _cmbPierSides.Items.Add(new Choice<PierSides>(sides, sides.GetDisplayName()));
        var pier = Section(Strings.SectionPier, out var pierTable);
        Row(pierTable, Strings.PierType, _cmbPierType);
        Row(pierTable, Strings.PierWidth, _nudPierWidth);
        Row(pierTable, Strings.PierBerths, _cmbPierSides);
        stack.Controls.Add(pier);

        // Berths
        var separators = new[]
        {
            BerthSeparator.FingerPiers, BerthSeparator.PairedFingerPiers, BerthSeparator.FingerPier,
            BerthSeparator.Piles, BerthSeparator.SinglePile, BerthSeparator.Boom, BerthSeparator.None,
        };
        foreach (var separator in separators) _cmbBerthSeparators.Items.Add(new Choice<BerthSeparator>(separator, separator.GetDisplayName()));
        foreach (var services in new[] { PierServices.None, PierServices.PowerAndWater, PierServices.Power, PierServices.Water })
        {
            _cmbBerthServices.Items.Add(new Choice<PierServices>(services, services.GetDisplayName()));
        }

        var berths = Section(Strings.SectionBerths, out var berthTable);
        Row(berthTable, Strings.BerthWidth, _nudBerthWidth);
        Row(berthTable, Strings.BerthLength, _nudBerthLength);
        Row(berthTable, Strings.BerthDepth, _nudBerthDepth);
        Row(berthTable, Strings.BerthSeparators, _cmbBerthSeparators);
        Row(berthTable, Strings.BerthGap, _nudBerthGap);
        FullRow(berthTable, _chkAlignBerths);
        Row(berthTable, Strings.BerthServices, _cmbBerthServices);
        Row(berthTable, Strings.LandBerthHeading, _nudLandBerthHeading);
        stack.Controls.Add(berths);

        // Reference image
        var imageTools = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(290, 0) };
        imageTools.Controls.Add(_btnLoadImage);
        imageTools.Controls.Add(_btnClearImage);
        imageTools.Controls.Add(_btnFitImage);
        imageTools.Controls.Add(_btnTopDown);
        var imageModes = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(290, 0) };
        AddTool(imageModes, DesignTool.MoveReferenceImage);
        AddTool(imageModes, DesignTool.MeasureScale);
        var checks = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        checks.Controls.Add(_chkImageVisible);
        checks.Controls.Add(_chkImageAbove);
        var calibrate = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        _nudScaleLength.Width = 90;
        calibrate.Controls.Add(_nudScaleLength);
        calibrate.Controls.Add(_btnCalibrate);

        var image = Section(Strings.SectionReferenceImage, out var imageTable);
        FullRow(imageTable, imageTools);
        FullRow(imageTable, checks);
        Row(imageTable, Strings.ImageOpacity, _trkOpacity);
        FullRow(imageTable, imageModes);
        FullRow(imageTable, _lblScaleLine);
        Row(imageTable, Strings.ScaleBarLength, calibrate);
        Row(imageTable, Strings.MetersPerPixel, _nudMetersPerPixel);
        stack.Controls.Add(image);

        Controls.Add(stack);
        WireControls();
        RefreshControls();
    }

    /// <summary>The visualizer whose <see cref="MarinaVisualizer.Designer"/> this panel drives.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public MarinaVisualizer? Marina
    {
        get => _marina;
        set
        {
            if (ReferenceEquals(value, _marina)) return;
            if (_marina is not null)
            {
                _marina.Designer.StateChanged -= OnDesignerStateChanged;
                _marina.Designer.ScaleLineDrawn -= OnScaleLineDrawn;
            }

            _marina = value;
            if (_marina is not null)
            {
                _marina.Designer.StateChanged += OnDesignerStateChanged;
                _marina.Designer.ScaleLineDrawn += OnScaleLineDrawn;
            }

            RefreshControls();
        }
    }

    /// <summary>Raised when loading an image file fails (the default shows a message box).</summary>
    [Category("Marina")]
    public event EventHandler<ThreadExceptionEventArgs>? ImageLoadFailed;

    /// <summary>Unsubscribes from the designer.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing) Marina = null;
        base.Dispose(disposing);
    }

    private MarinaDesigner? Designer => _marina?.Designer;

    private void WireControls()
    {
        _chkActive.CheckedChanged += (_, _) => Apply(d => d.IsActive = _chkActive.Checked);
        _cmbLandKind.SelectedIndexChanged += (_, _) => Apply(d => d.LandKind = (LandKind)_cmbLandKind.SelectedItem!);
        _nudLandHeight.ValueChanged += (_, _) => Apply(d => d.LandHeight = (float)_nudLandHeight.Value);
        _trkTreeDensity.ValueChanged += (_, _) => Apply(d => d.TreeDensity = _trkTreeDensity.Value);
        _btnUndo.Click += (_, _) => Apply(d => d.Undo());
        _cmbPierType.SelectedIndexChanged += (_, _) => Apply(d => d.PierType = ((Choice<PierType>)_cmbPierType.SelectedItem!).Value);
        _nudPierWidth.ValueChanged += (_, _) => Apply(d => d.PierWidth = (float)_nudPierWidth.Value);
        _cmbPierSides.SelectedIndexChanged += (_, _) => Apply(d => d.PierBerthingSides = ((Choice<PierSides>)_cmbPierSides.SelectedItem!).Value);
        _nudBerthWidth.ValueChanged += (_, _) => Apply(d => d.BerthWidth = (float)_nudBerthWidth.Value);
        _nudBerthLength.ValueChanged += (_, _) => Apply(d => d.BerthLength = (float)_nudBerthLength.Value);
        _nudBerthDepth.ValueChanged += (_, _) => Apply(d => d.BerthDepth = (float)_nudBerthDepth.Value);
        _cmbBerthSeparators.SelectedIndexChanged += (_, _) => Apply(d => d.BerthSeparators = ((Choice<BerthSeparator>)_cmbBerthSeparators.SelectedItem!).Value);
        _nudBerthGap.ValueChanged += (_, _) => Apply(d => d.BerthGap = (float)_nudBerthGap.Value);
        _chkAlignBerths.CheckedChanged += (_, _) => Apply(d => d.AlignBerthsToExisting = _chkAlignBerths.Checked);
        _cmbBerthServices.SelectedIndexChanged += (_, _) => Apply(d => d.BerthServices = ((Choice<PierServices>)_cmbBerthServices.SelectedItem!).Value);
        _nudLandBerthHeading.ValueChanged += (_, _) => Apply(d => d.LandBerthHeading = (float)_nudLandBerthHeading.Value);

        _btnLoadImage.Click += (_, _) => LoadImage();
        _btnClearImage.Click += (_, _) => Apply(d => d.ClearReferenceImage());
        _btnFitImage.Click += (_, _) => Apply(d => d.FocusReferenceImage());
        _btnTopDown.Click += (_, _) => Apply(d => d.ViewTopDown());
        _chkImageVisible.CheckedChanged += (_, _) => Apply(d => d.ReferenceImageVisible = _chkImageVisible.Checked);
        _chkImageAbove.CheckedChanged += (_, _) => Apply(d => d.ReferenceImageAboveScene = _chkImageAbove.Checked);
        _trkOpacity.ValueChanged += (_, _) => Apply(d => d.ReferenceImageOpacity = _trkOpacity.Value / 100f);
        _nudMetersPerPixel.ValueChanged += (_, _) => Apply(d => d.ReferenceImageMetersPerPixel = (float)_nudMetersPerPixel.Value);
        _btnCalibrate.Click += (_, _) => Apply(d => d.CalibrateReferenceImage((float)_nudScaleLength.Value));
    }

    private void AddTool(FlowLayoutPanel host, DesignTool tool)
    {
        var button = new CheckBox
        {
            Text = tool.GetDisplayName(),
            Appearance = Appearance.Button,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            MinimumSize = new Size(64, 26),
        };
        button.Click += (_, _) => Apply(d =>
        {
            d.IsActive = true;
            d.Tool = tool;
        });
        _toolButtons[tool] = button;
        host.Controls.Add(button);
    }

    /// <summary>Runs a change on the designer unless the controls are being refreshed from it.</summary>
    private void Apply(Action<MarinaDesigner> change)
    {
        if (_updating || Designer is not { } designer) return;
        try
        {
            change(designer);
        }
        catch (ArgumentOutOfRangeException)
        {
            // The numeric ranges match the designer's; ignore values typed past them.
        }

        RefreshControls();
    }

    private void LoadImage()
    {
        if (Designer is not { } designer) return;
        using var dialog = new OpenFileDialog { Filter = ReferenceImageLoader.FileDialogFilter, Title = Strings.OpenImageTitle };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            designer.IsActive = true;
            designer.SetReferenceImage(ReferenceImageLoader.FromFile(dialog.FileName));
            designer.FocusReferenceImage();
            designer.Tool = DesignTool.MeasureScale;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or OutOfMemoryException or UnauthorizedAccessException)
        {
            if (ImageLoadFailed is not null) ImageLoadFailed(this, new ThreadExceptionEventArgs(ex));
            else MessageBox.Show(this, Strings.Format(Strings.ImageLoadFailed, ex.Message), Strings.ImageLoadFailedTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnDesignerStateChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(RefreshControls);
        else RefreshControls();
    }

    private void OnScaleLineDrawn(object? sender, ScaleLineDrawnEventArgs e)
    {
        if (IsDisposed) return;
        _nudScaleLength.Select(0, _nudScaleLength.Text.Length);
        _nudScaleLength.Focus();
    }

    private void RefreshControls()
    {
        _updating = true;
        try
        {
            var designer = Designer;
            var enabled = designer is not null;
            foreach (Control control in Controls) control.Enabled = enabled;
            if (designer is null) return;

            _chkActive.Checked = designer.IsActive;
            foreach (var (tool, button) in _toolButtons) button.Checked = designer.IsActive && designer.Tool == tool;
            _lblHint.Text = designer.IsActive ? designer.ToolHint : Strings.TurnOnDesignMode;

            _cmbLandKind.SelectedItem = _cmbLandKind.Items.Cast<Choice<LandKind>>().First(c => c.Value == designer.LandKind);
            SetNumber(_nudLandHeight, designer.LandHeight);
            _trkTreeDensity.Value = Math.Clamp((int)MathF.Round(designer.TreeDensity), _trkTreeDensity.Minimum, _trkTreeDensity.Maximum);
            _lblTreeDensity.Text = designer.TreeDensity > 0f
                ? string.Format(CultureInfo.CurrentCulture, Strings.TreeDensity, designer.TreeDensity)
                : Strings.NoTrees;
            _btnUndo.Enabled = designer.CanUndo;
            _lblUndo.Text = designer.UndoDescription ?? Strings.NothingToUndo;
            _cmbPierType.SelectedItem = _cmbPierType.Items.Cast<Choice<PierType>>().First(c => c.Value == designer.PierType);
            SetNumber(_nudPierWidth, designer.PierWidth);
            _cmbPierSides.SelectedItem = _cmbPierSides.Items.Cast<Choice<PierSides>>().First(c => c.Value == designer.PierBerthingSides);
            SetNumber(_nudBerthWidth, designer.BerthWidth);
            SetNumber(_nudBerthLength, designer.BerthLength);
            SetNumber(_nudBerthDepth, designer.BerthDepth);
            _cmbBerthSeparators.SelectedItem = _cmbBerthSeparators.Items.Cast<Choice<BerthSeparator>>().First(c => c.Value == designer.BerthSeparators);
            SetNumber(_nudBerthGap, designer.BerthGap);
            _chkAlignBerths.Checked = designer.AlignBerthsToExisting;
            _cmbBerthServices.SelectedItem = _cmbBerthServices.Items.Cast<Choice<PierServices>>().First(c => c.Value == designer.BerthServices);
            SetNumber(_nudLandBerthHeading, designer.LandBerthHeading);

            var hasImage = designer.ReferenceImage is not null;
            _btnClearImage.Enabled = hasImage;
            _btnFitImage.Enabled = hasImage;
            _chkImageVisible.Enabled = hasImage;
            _chkImageAbove.Enabled = hasImage;
            _trkOpacity.Enabled = hasImage;
            _nudMetersPerPixel.Enabled = hasImage;
            _toolButtons[DesignTool.MoveReferenceImage].Enabled = hasImage;
            _toolButtons[DesignTool.MeasureScale].Enabled = hasImage;
            _chkImageVisible.Checked = designer.ReferenceImageVisible;
            _chkImageAbove.Checked = designer.ReferenceImageAboveScene;
            _trkOpacity.Value = (int)MathF.Round(designer.ReferenceImageOpacity * 100f);
            SetNumber(_nudMetersPerPixel, designer.ReferenceImageMetersPerPixel);

            var line = designer.ScaleLine;
            _btnCalibrate.Enabled = hasImage && line is not null;
            _nudScaleLength.Enabled = hasImage && line is not null;
            _lblScaleLine.Text = !hasImage
                ? Strings.NoImageYet
                : line is { } l
                    ? string.Format(CultureInfo.CurrentCulture, Strings.ScaleLineDrawn, System.Numerics.Vector2.Distance(l.Start, l.End))
                    : string.Format(CultureInfo.CurrentCulture, Strings.ImageSizeHint, designer.ReferenceImageSize.X, designer.ReferenceImageSize.Y);
        }
        finally
        {
            _updating = false;
        }
    }

    private static GroupBox Section(string title, out TableLayoutPanel table)
    {
        table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(2) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        var group = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(300, 0), Padding = new Padding(6) };
        group.Controls.Add(table);
        return group;
    }

    private static void Row(TableLayoutPanel table, string label, Control control)
    {
        var row = table.RowCount++;
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) }, 0, row);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        table.Controls.Add(control, 1, row);
    }

    private static void FullRow(TableLayoutPanel table, Control control)
    {
        var row = table.RowCount++;
        table.Controls.Add(control, 0, row);
        table.SetColumnSpan(control, 2);
    }

    private static ComboBox CreateCombo() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };

    private static NumericUpDown CreateNumber(decimal min, decimal max, decimal increment, int decimals) =>
        new() { Minimum = min, Maximum = max, Increment = increment, DecimalPlaces = decimals, Width = 90, TextAlign = HorizontalAlignment.Right };

    private static void SetNumber(NumericUpDown control, float value)
    {
        var rounded = Math.Clamp(Math.Round((decimal)value, control.DecimalPlaces), control.Minimum, control.Maximum);
        if (control.Value != rounded) control.Value = rounded;
    }

    private sealed record Choice<T>(T Value, string Caption)
    {
        public override string ToString() => Caption;
    }
}
