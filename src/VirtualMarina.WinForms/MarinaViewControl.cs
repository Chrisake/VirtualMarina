using System.ComponentModel;
using System.Diagnostics;
using OpenTK.Windowing.Common;
using OpenTK.GLControl;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Input;
using VirtualMarina.Rendering.OpenGL;

namespace VirtualMarina.WinForms;

/// <summary>
/// Drop-in WinForms control that renders a <see cref="MarinaVisualizer"/> with OpenGL, and shows the
/// selection tooltip / actions window above the selected slip.
/// </summary>
/// <remarks>
/// <code>
/// var view = new MarinaViewControl { Dock = DockStyle.Fill };
/// form.Controls.Add(view);
/// view.Marina.InitializeLayout(layout);
/// view.Marina.SlipSelected += (s, e) => e.Actions.Add("checkin", "Check in");
/// view.Marina.SlipActionInvoked += (s, e) => Erp.Run(e.ActionId, e.Slips);
/// </code>
/// </remarks>
[ToolboxItem(true)]
[Description("Interactive 3D marina view.")]
[DefaultEvent(nameof(SlipSelected))]
public sealed class MarinaViewControl : UserControl
{
    private readonly System.Windows.Forms.Timer _frameTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly SelectionPopupPanel _popupPanel = new();
    private GLControl? _glControl;
    private OpenGlSceneRenderer? _renderer;
    private MarinaVisualizer _marina;
    private double _lastFrameSeconds;
    private bool _renderFailed;
    private bool _animate = true;

    /// <summary>Creates the control with its own empty <see cref="MarinaVisualizer"/> (available as <see cref="Marina"/>).</summary>
    public MarinaViewControl()
        : this(new MarinaVisualizer())
    {
    }

    /// <summary>Creates the control for an existing visualizer (e.g. one configured before the form is shown).</summary>
    /// <param name="marina">The visualizer to display.</param>
    public MarinaViewControl(MarinaVisualizer marina)
    {
        _marina = marina ?? throw new ArgumentNullException(nameof(marina));
        BackColor = Color.FromArgb(194, 217, 235);
        DoubleBuffered = false;

        _frameTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _frameTimer.Tick += (_, _) =>
        {
            _glControl?.Invalidate();
            UpdatePopupPosition();
        };

        _popupPanel.ActionClicked += (_, actionId) => _marina.InvokeSlipAction(actionId);
        _popupPanel.CloseClicked += (_, _) => _marina.ClosePopup();
        AttachMarina(_marina);

        // The OpenGL surface is created in OnHandleCreated, where design mode can be detected reliably:
        // the Visual Studio designer must never create it (GLFW would throw "can only be called from the main thread").
        SetStyle(ControlStyles.ResizeRedraw, true);
        Controls.Add(_popupPanel);
        _popupPanel.BringToFront();
    }

    /// <summary>Raised if OpenGL initialization or rendering fails (e.g. no OpenGL 3.3 driver).</summary>
    [Category("Marina")]
    public event EventHandler<ThreadExceptionEventArgs>? RenderError;

    // ---- Marina events, forwarded from Marina so they can be wired in the Visual Studio designer ----------
    // The sender is this control; the event data is the same as on MarinaVisualizer.

    /// <inheritdoc cref="IMarinaVisualizer.SlipClicked"/>
    [Category("Marina")]
    [Description("A slip or its boat was clicked or double-clicked.")]
    public event EventHandler<SlipEventArgs>? SlipClicked;

    /// <inheritdoc cref="IMarinaVisualizer.SlipSelected"/>
    [Category("Marina")]
    [Description("A slip was selected. Fill e.Tooltip and e.Actions to control the popup.")]
    public event EventHandler<SlipSelectedEventArgs>? SlipSelected;

    /// <inheritdoc cref="IMarinaVisualizer.MultiSlipSelected"/>
    [Category("Marina")]
    [Description("Two or more slips were selected (Ctrl+click or Shift+click). Fill e.Tooltip and e.Actions for the selection.")]
    public event EventHandler<MultiSlipSelectedEventArgs>? MultiSlipSelected;

    /// <inheritdoc cref="IMarinaVisualizer.SelectionChanged"/>
    [Category("Marina")]
    [Description("The selected slips changed, including the selection being cleared.")]
    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    /// <inheritdoc cref="IMarinaVisualizer.SelectionCleared"/>
    [Category("Marina")]
    [Description("The selection became empty.")]
    public event EventHandler? SelectionCleared;

    /// <inheritdoc cref="IMarinaVisualizer.SlipActionInvoked"/>
    [Category("Marina")]
    [Description("The user clicked an action in the actions window (e.ActionId, e.Slips).")]
    public event EventHandler<SlipActionInvokedEventArgs>? SlipActionInvoked;

    /// <inheritdoc cref="IMarinaVisualizer.PopupChanged"/>
    [Category("Marina")]
    [Description("The tooltip or actions window opened, closed or changed.")]
    public event EventHandler<SlipPopupChangedEventArgs>? PopupChanged;

    /// <inheritdoc cref="IMarinaVisualizer.SlipHoverChanged"/>
    [Category("Marina")]
    [Description("The slip under the mouse changed.")]
    public event EventHandler<SlipHoverEventArgs>? SlipHoverChanged;

    /// <inheritdoc cref="IMarinaVisualizer.SlipStatusChanged"/>
    [Category("Marina")]
    [Description("A slip's status or boat changed.")]
    public event EventHandler<SlipStatusChangedEventArgs>? SlipStatusChanged;

    /// <inheritdoc cref="IMarinaVisualizer.LayoutChanged"/>
    [Category("Marina")]
    [Description("Docks, slips, dividers or berths were added, changed or removed.")]
    public event EventHandler<LayoutChangedEventArgs>? LayoutChanged;

    /// <summary>
    /// The visualizer this control displays. Can be swapped at runtime; the control's Marina events follow the new instance.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public MarinaVisualizer Marina
    {
        get => _marina;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(value, _marina)) return;
            DetachMarina(_marina);
            _marina = value;
            AttachMarina(_marina);
            ShowPopup(_marina.ActivePopup);
        }
    }

    private void AttachMarina(MarinaVisualizer marina)
    {
        marina.PopupChanged += OnPopupChanged;
        marina.SlipClicked += OnMarinaSlipClicked;
        marina.SlipSelected += OnMarinaSlipSelected;
        marina.MultiSlipSelected += OnMarinaMultiSlipSelected;
        marina.SelectionChanged += OnMarinaSelectionChanged;
        marina.SelectionCleared += OnMarinaSelectionCleared;
        marina.SlipActionInvoked += OnMarinaSlipActionInvoked;
        marina.SlipHoverChanged += OnMarinaSlipHoverChanged;
        marina.SlipStatusChanged += OnMarinaSlipStatusChanged;
        marina.LayoutChanged += OnMarinaLayoutChanged;
    }

    private void DetachMarina(MarinaVisualizer marina)
    {
        marina.PopupChanged -= OnPopupChanged;
        marina.SlipClicked -= OnMarinaSlipClicked;
        marina.SlipSelected -= OnMarinaSlipSelected;
        marina.MultiSlipSelected -= OnMarinaMultiSlipSelected;
        marina.SelectionChanged -= OnMarinaSelectionChanged;
        marina.SelectionCleared -= OnMarinaSelectionCleared;
        marina.SlipActionInvoked -= OnMarinaSlipActionInvoked;
        marina.SlipHoverChanged -= OnMarinaSlipHoverChanged;
        marina.SlipStatusChanged -= OnMarinaSlipStatusChanged;
        marina.LayoutChanged -= OnMarinaLayoutChanged;
    }

    private void OnMarinaSlipClicked(object? sender, SlipEventArgs e) => SlipClicked?.Invoke(this, e);

    private void OnMarinaSlipSelected(object? sender, SlipSelectedEventArgs e) => SlipSelected?.Invoke(this, e);

    private void OnMarinaMultiSlipSelected(object? sender, MultiSlipSelectedEventArgs e) => MultiSlipSelected?.Invoke(this, e);

    private void OnMarinaSelectionChanged(object? sender, SelectionChangedEventArgs e) => SelectionChanged?.Invoke(this, e);

    private void OnMarinaSelectionCleared(object? sender, EventArgs e) => SelectionCleared?.Invoke(this, e);

    private void OnMarinaSlipActionInvoked(object? sender, SlipActionInvokedEventArgs e) => SlipActionInvoked?.Invoke(this, e);

    private void OnMarinaSlipHoverChanged(object? sender, SlipHoverEventArgs e) => SlipHoverChanged?.Invoke(this, e);

    private void OnMarinaSlipStatusChanged(object? sender, SlipStatusChangedEventArgs e) => SlipStatusChanged?.Invoke(this, e);

    private void OnMarinaLayoutChanged(object? sender, LayoutChangedEventArgs e) => LayoutChanged?.Invoke(this, e);

    /// <summary>Delay between frames in milliseconds (the Windows timer resolution is about 15 ms).</summary>
    [DefaultValue(15)]
    public int FrameIntervalMilliseconds
    {
        get => _frameTimer.Interval;
        set => _frameTimer.Interval = Math.Max(1, value);
    }

    /// <summary>Pauses or resumes the render loop.</summary>
    [DefaultValue(true)]
    public bool Animate
    {
        get => _animate;
        set
        {
            _animate = value;
            _frameTimer.Enabled = value && _glControl is not null;
        }
    }

    /// <summary>Backend and GPU description after the first frame, e.g. for a status bar.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string RendererDescription =>
        _renderer?.DeviceDescription is { } device ? $"{_renderer.BackendName} | {device}" : "OpenGL (not initialized)";

    /// <summary>Creates the OpenGL surface and starts the render loop once the window handle exists (not in the designer).</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (IsInDesigner()) return;

        if (_glControl is null)
        {
            CreateGlControl();
            _popupPanel.BringToFront();
        }

        if (_animate) _frameTimer.Start();
    }

    /// <summary>In the designer (or if OpenGL is unavailable) draws a placeholder describing the 3D render area.</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_glControl is not null) return;

        var bounds = ClientRectangle;
        using (var border = new Pen(Color.FromArgb(120, 150, 175)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
        {
            e.Graphics.DrawRectangle(border, 0, 0, bounds.Width - 1, bounds.Height - 1);
        }

        using var titleFont = new Font(Font.FontFamily, Font.Size * 1.4f, FontStyle.Bold);
        var titleHeight = TextRenderer.MeasureText("Ag", titleFont).Height;
        var titleRect = new Rectangle(0, bounds.Height / 2 - titleHeight, bounds.Width, titleHeight);
        var subtitleRect = new Rectangle(0, bounds.Height / 2 + 4, bounds.Width, titleHeight * 2);
        const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak;

        TextRenderer.DrawText(e.Graphics, "VirtualMarina 3D view", titleFont, titleRect, Color.FromArgb(40, 70, 95), flags);
        TextRenderer.DrawText(e.Graphics, "The marina is rendered here at runtime (OpenGL 3.3).", Font, subtitleRect, Color.FromArgb(70, 95, 120), flags);
    }

    /// <summary>
    /// True inside a forms designer. Checks the site of this control and its parents (set by the designer before the
    /// handle is created), the license context, and the Visual Studio designer host processes.
    /// </summary>
    private bool IsInDesigner()
    {
        if (LicenseManager.UsageMode == LicenseUsageMode.Designtime) return true;

        for (Control? control = this; control is not null; control = control.Parent)
        {
            if (control.Site?.DesignMode == true) return true;
        }

        var process = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        return process.Equals("DesignToolsServer", StringComparison.OrdinalIgnoreCase)
            || process.Equals("devenv", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Stops rendering and releases GPU resources, the GL control and the popup.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DetachMarina(_marina);
            _frameTimer.Stop();
            _frameTimer.Dispose();
            if (_renderer is not null && _glControl is { IsHandleCreated: true, IsDisposed: false })
            {
                _glControl.MakeCurrent();
                _renderer.Dispose();
            }

            _renderer = null;
            _glControl?.Dispose();
            _glControl = null;
            _popupPanel.Dispose();
        }

        base.Dispose(disposing);
    }

    private void CreateGlControl()
    {
        _glControl = new GLControl(new GLControlSettings
        {
            API = ContextAPI.OpenGL,
            APIVersion = new Version(3, 3),
            Profile = ContextProfile.Core,
            Flags = ContextFlags.ForwardCompatible,
            NumberOfSamples = 4,
        })
        {
            Dock = DockStyle.Fill,
            TabStop = true,
        };

        _glControl.Paint += OnGlPaint;
        _glControl.MouseDown += OnGlMouseDown;
        _glControl.MouseMove += OnGlMouseMove;
        _glControl.MouseUp += OnGlMouseUp;
        _glControl.MouseDoubleClick += OnGlMouseDoubleClick;
        _glControl.MouseWheel += OnGlMouseWheel;
        _glControl.MouseLeave += (_, _) => _marina.Input.PointerLeave();
        _glControl.PreviewKeyDown += (_, e) => { if (MapKey(e.KeyCode) is not null) e.IsInputKey = true; };
        _glControl.KeyDown += OnGlKeyDown;

        Controls.Add(_glControl);
    }

    private void OnPopupChanged(object? sender, SlipPopupChangedEventArgs e)
    {
        ShowPopup(e.Current);
        PopupChanged?.Invoke(this, e);
    }

    private void ShowPopup(SlipPopup? popup)
    {
        if (IsDisposed) return;
        if (popup is null)
        {
            _popupPanel.Visible = false;
            return;
        }

        _popupPanel.Present(popup);
        UpdatePopupPosition();
    }

    /// <summary>Keeps the popup pointing at its slip while the camera moves.</summary>
    private void UpdatePopupPosition()
    {
        if (_popupPanel.Popup is null || _marina.ActivePopup is null)
        {
            if (_popupPanel.Visible) _popupPanel.Visible = false;
            return;
        }

        var size = _glControl?.ClientSize ?? ClientSize;
        if (size.Width > 0 && size.Height > 0) _marina.SetViewportSize(size.Width, size.Height);

        var visible = _marina.TryGetPopupAnchor(out var anchor) &&
            _popupPanel.PositionAt(new Point((int)MathF.Round(anchor.X), (int)MathF.Round(anchor.Y)), size);
        if (_popupPanel.Visible != visible) _popupPanel.Visible = visible;
        if (visible) _popupPanel.BringToFront();
    }

    private void OnGlPaint(object? sender, PaintEventArgs e)
    {
        var gl = _glControl;
        if (gl is null || _renderFailed || gl.ClientSize.Width <= 0 || gl.ClientSize.Height <= 0) return;

        try
        {
            gl.MakeCurrent();
            if (_renderer is null)
            {
                _renderer = new OpenGlSceneRenderer();
                _renderer.Initialize();
                _lastFrameSeconds = _clock.Elapsed.TotalSeconds;
            }

            var now = _clock.Elapsed.TotalSeconds;
            var delta = now - _lastFrameSeconds;
            _lastFrameSeconds = now;

            var size = gl.ClientSize;
            _marina.SetViewportSize(size.Width, size.Height);
            _marina.Update(delta);
            _renderer.Resize(size.Width, size.Height);
            _renderer.Render(_marina.BuildRenderFrame());
            gl.SwapBuffers();
        }
        catch (Exception ex)
        {
            _renderFailed = true;
            _frameTimer.Stop();
            if (RenderError is null) throw;
            RenderError.Invoke(this, new ThreadExceptionEventArgs(ex));
        }
    }

    private void OnGlMouseDown(object? sender, MouseEventArgs e)
    {
        _glControl?.Focus();
        _marina.Input.PointerDown(e.X, e.Y, MapButton(e.Button), CurrentModifiers());
    }

    private void OnGlMouseMove(object? sender, MouseEventArgs e) =>
        _marina.Input.PointerMove(e.X, e.Y, CurrentModifiers());

    private void OnGlMouseUp(object? sender, MouseEventArgs e) =>
        _marina.Input.PointerUp(e.X, e.Y, MapButton(e.Button), CurrentModifiers());

    private void OnGlMouseDoubleClick(object? sender, MouseEventArgs e) =>
        _marina.Input.DoubleClick(e.X, e.Y, MapButton(e.Button), CurrentModifiers());

    private void OnGlMouseWheel(object? sender, MouseEventArgs e) =>
        _marina.Input.Wheel(e.Delta / (float)SystemInformation.MouseWheelScrollDelta, e.X, e.Y);

    private void OnGlKeyDown(object? sender, KeyEventArgs e)
    {
        if (MapKey(e.KeyCode) is { } key && _marina.Input.KeyDown(key, CurrentModifiers()))
        {
            e.Handled = true;
        }
    }

    private static PointerButton MapButton(MouseButtons button) => button switch
    {
        MouseButtons.Left => PointerButton.Left,
        MouseButtons.Right => PointerButton.Right,
        MouseButtons.Middle => PointerButton.Middle,
        _ => PointerButton.None,
    };

    private static InputModifiers CurrentModifiers()
    {
        var keys = ModifierKeys;
        var result = InputModifiers.None;
        if ((keys & Keys.Shift) != 0) result |= InputModifiers.Shift;
        if ((keys & Keys.Control) != 0) result |= InputModifiers.Control;
        if ((keys & Keys.Alt) != 0) result |= InputModifiers.Alt;
        return result;
    }

    private static MarinaKey? MapKey(Keys key) => key switch
    {
        Keys.Left or Keys.A => MarinaKey.Left,
        Keys.Right or Keys.D => MarinaKey.Right,
        Keys.Up or Keys.W => MarinaKey.Up,
        Keys.Down or Keys.S => MarinaKey.Down,
        Keys.PageUp => MarinaKey.PageUp,
        Keys.PageDown => MarinaKey.PageDown,
        Keys.Add or Keys.Oemplus => MarinaKey.ZoomIn,
        Keys.Subtract or Keys.OemMinus => MarinaKey.ZoomOut,
        Keys.Home => MarinaKey.Home,
        Keys.Escape => MarinaKey.Escape,
        _ => null,
    };
}
