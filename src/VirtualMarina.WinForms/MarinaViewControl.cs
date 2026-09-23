using System.ComponentModel;
using System.Diagnostics;
using OpenTK.GLControl;
using OpenTK.Windowing.Common;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Input;
using VirtualMarina.Core.Rendering;
using VirtualMarina.Rendering.OpenGL;
using VirtualMarina.WinForms.Resources;

namespace VirtualMarina.WinForms;

/// <summary>
/// Drop-in WinForms control that renders a <see cref="MarinaVisualizer"/> with OpenGL, and shows the
/// selection tooltip / actions window above the selected berth.
/// </summary>
/// <remarks>
/// <code>
/// var view = new MarinaViewControl { Dock = DockStyle.Fill };
/// form.Controls.Add(view);
/// view.Marina.InitializeLayout(layout);
/// view.Marina.BerthSelected += (s, e) => e.Actions.Add("checkin", "Check in");
/// view.Marina.BerthActionInvoked += (s, e) => Erp.Run(e.ActionId, e.Berths);
/// </code>
/// </remarks>
[ToolboxItem(true)]
[Description("Interactive 3D marina view.")]
[DefaultEvent(nameof(BerthSelected))]
public sealed class MarinaViewControl : UserControl, IMessageFilter
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

        _popupPanel.ActionClicked += (_, actionId) => _marina.InvokeBerthAction(actionId);
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

    /// <inheritdoc cref="IMarinaVisualizer.BerthClicked"/>
    [Category("Marina")]
    [Description("A berth or its boat was clicked or double-clicked.")]
    public event EventHandler<BerthEventArgs>? BerthClicked;

    /// <inheritdoc cref="IMarinaVisualizer.BerthSelected"/>
    [Category("Marina")]
    [Description("A berth was selected. Fill e.Tooltip and e.Actions to control the popup.")]
    public event EventHandler<BerthSelectedEventArgs>? BerthSelected;

    /// <inheritdoc cref="IMarinaVisualizer.MultiBerthSelected"/>
    [Category("Marina")]
    [Description("Two or more berths were selected (Ctrl+click or Shift+click). Fill e.Tooltip and e.Actions for the selection.")]
    public event EventHandler<MultiBerthSelectedEventArgs>? MultiBerthSelected;

    /// <inheritdoc cref="IMarinaVisualizer.SelectionChanged"/>
    [Category("Marina")]
    [Description("The selected berths changed, including the selection being cleared.")]
    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    /// <inheritdoc cref="IMarinaVisualizer.SelectionCleared"/>
    [Category("Marina")]
    [Description("The selection became empty.")]
    public event EventHandler? SelectionCleared;

    /// <inheritdoc cref="IMarinaVisualizer.BerthActionInvoked"/>
    [Category("Marina")]
    [Description("The user clicked an action in the actions window (e.ActionId, e.Berths).")]
    public event EventHandler<BerthActionInvokedEventArgs>? BerthActionInvoked;

    /// <inheritdoc cref="IMarinaVisualizer.PopupChanged"/>
    [Category("Marina")]
    [Description("The tooltip or actions window opened, closed or changed.")]
    public event EventHandler<BerthPopupChangedEventArgs>? PopupChanged;

    /// <inheritdoc cref="IMarinaVisualizer.BerthHoverChanged"/>
    [Category("Marina")]
    [Description("The berth under the mouse changed.")]
    public event EventHandler<BerthHoverEventArgs>? BerthHoverChanged;

    /// <inheritdoc cref="IMarinaVisualizer.BerthStatusChanged"/>
    [Category("Marina")]
    [Description("A berth's status or boat changed.")]
    public event EventHandler<BerthStatusChangedEventArgs>? BerthStatusChanged;

    /// <inheritdoc cref="IMarinaVisualizer.LayoutChanged"/>
    [Category("Marina")]
    [Description("Piers, berths, dividers or berths were added, changed or removed.")]
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
        marina.BerthClicked += OnMarinaBerthClicked;
        marina.BerthSelected += OnMarinaBerthSelected;
        marina.MultiBerthSelected += OnMarinaMultiBerthSelected;
        marina.SelectionChanged += OnMarinaSelectionChanged;
        marina.SelectionCleared += OnMarinaSelectionCleared;
        marina.BerthActionInvoked += OnMarinaBerthActionInvoked;
        marina.BerthHoverChanged += OnMarinaBerthHoverChanged;
        marina.BerthStatusChanged += OnMarinaBerthStatusChanged;
        marina.LayoutChanged += OnMarinaLayoutChanged;
    }

    private void DetachMarina(MarinaVisualizer marina)
    {
        marina.PopupChanged -= OnPopupChanged;
        marina.BerthClicked -= OnMarinaBerthClicked;
        marina.BerthSelected -= OnMarinaBerthSelected;
        marina.MultiBerthSelected -= OnMarinaMultiBerthSelected;
        marina.SelectionChanged -= OnMarinaSelectionChanged;
        marina.SelectionCleared -= OnMarinaSelectionCleared;
        marina.BerthActionInvoked -= OnMarinaBerthActionInvoked;
        marina.BerthHoverChanged -= OnMarinaBerthHoverChanged;
        marina.BerthStatusChanged -= OnMarinaBerthStatusChanged;
        marina.LayoutChanged -= OnMarinaLayoutChanged;
    }

    private void OnMarinaBerthClicked(object? sender, BerthEventArgs e) => BerthClicked?.Invoke(this, e);

    private void OnMarinaBerthSelected(object? sender, BerthSelectedEventArgs e) => BerthSelected?.Invoke(this, e);

    private void OnMarinaMultiBerthSelected(object? sender, MultiBerthSelectedEventArgs e) => MultiBerthSelected?.Invoke(this, e);

    private void OnMarinaSelectionChanged(object? sender, SelectionChangedEventArgs e) => SelectionChanged?.Invoke(this, e);

    private void OnMarinaSelectionCleared(object? sender, EventArgs e) => SelectionCleared?.Invoke(this, e);

    private void OnMarinaBerthActionInvoked(object? sender, BerthActionInvokedEventArgs e) => BerthActionInvoked?.Invoke(this, e);

    private void OnMarinaBerthHoverChanged(object? sender, BerthHoverEventArgs e) => BerthHoverChanged?.Invoke(this, e);

    private void OnMarinaBerthStatusChanged(object? sender, BerthStatusChangedEventArgs e) => BerthStatusChanged?.Invoke(this, e);

    private void OnMarinaLayoutChanged(object? sender, LayoutChangedEventArgs e) => LayoutChanged?.Invoke(this, e);

    /// <summary>
    /// How the marina is drawn and animated: lighting, waves, status colors, boat opacities, land and trees, piers, labels, selection and
    /// camera. The same object as <c>Marina.Style</c>; change its properties or assign a new <see cref="MarinaStyle"/>.
    /// </summary>
    /// <example><code>marinaView.Style.Water.WaveAmplitude = 0.02f; marinaView.Style.Status.ReservedBoatOpacity = 0.7f;</code></example>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public MarinaStyle Style
    {
        get => _marina.Style;
        set => _marina.Style = value ?? throw new ArgumentNullException(nameof(value));
    }

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
        _renderer?.DeviceDescription is { } device ? $"{_renderer.BackendName} | {device}" : Strings.RendererNotInitialized;

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

        // Watched application-wide rather than on the GL control, because Alt does not stay with it: pressing Alt
        // hands the keyboard to the window's menu bar, so the key-up would never arrive here and the preview would
        // stay stuck on "whole row" until the pointer moved again.
        Application.AddMessageFilter(this);
    }

    /// <summary>In the designer (or if OpenGL is unavailable) draws a placeholder describing the 3D render area.</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
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

        TextRenderer.DrawText(e.Graphics, Strings.DesignTimeTitle, titleFont, titleRect, Color.FromArgb(40, 70, 95), flags);
        TextRenderer.DrawText(e.Graphics, Strings.DesignTimeSubtitle, Font, subtitleRect, Color.FromArgb(70, 95, 120), flags);
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
            Application.RemoveMessageFilter(this);
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

        // Letting go of the view entirely is the same as letting go of every key: nothing is held any more.
        _glControl.LostFocus += (_, _) => _marina.Input.ModifiersChanged(InputModifiers.None);

        Controls.Add(_glControl);
    }

    private void OnPopupChanged(object? sender, BerthPopupChangedEventArgs e)
    {
        ShowPopup(e.Current);
        PopupChanged?.Invoke(this, e);
    }

    private void ShowPopup(BerthPopup? popup)
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

    /// <summary>Keeps the popup pointing at its berth while the camera moves.</summary>
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

    private void OnGlMouseMove(object? sender, MouseEventArgs e)
    {
        _marina.Input.PointerMove(e.X, e.Y, CurrentModifiers());
        UpdateCursor();
    }

    private void OnGlMouseUp(object? sender, MouseEventArgs e)
    {
        _marina.Input.PointerUp(e.X, e.Y, MapButton(e.Button), CurrentModifiers());
        UpdateCursor();
    }

    /// <summary>
    /// Hand cursor over a selectable berth or boat (hover uses the exact boat shapes); default otherwise and while dragging.
    /// In the designer: a cross for drawing and erasing, a move cursor for moving the reference image.
    /// </summary>
    private void UpdateCursor()
    {
        if (_glControl is null) return;
        var designer = _marina.Designer;
        var cursor = designer.IsActive
            ? designer.Tool switch
            {
                DesignTool.Navigate => Cursors.Default,
                DesignTool.MoveReferenceImage => designer.ReferenceImage is null ? Cursors.No : Cursors.SizeAll,
                _ => Cursors.Cross,
            }
            : !_marina.Input.IsDragging && _marina.HoveredBerth is not null ? Cursors.Hand : Cursors.Default;
        if (_glControl.Cursor != cursor) _glControl.Cursor = cursor;
    }

    private void OnGlMouseDoubleClick(object? sender, MouseEventArgs e) =>
        _marina.Input.DoubleClick(e.X, e.Y, MapButton(e.Button), CurrentModifiers());

    /// <summary>
    /// Loads an image file (PNG, JPEG, BMP, GIF or TIFF) and shows it as the designer's reference image, north at the top.
    /// </summary>
    /// <param name="path">The image file.</param>
    /// <param name="metersPerPixel">Known ground size of a pixel; null sizes it to the layout until calibrated.</param>
    /// <returns>The loaded image.</returns>
    public ReferenceImage LoadReferenceImage(string path, float? metersPerPixel = null)
    {
        var image = ReferenceImageLoader.FromFile(path);
        _marina.Designer.SetReferenceImage(image, metersPerPixel);
        return image;
    }

    private void OnGlMouseWheel(object? sender, MouseEventArgs e) =>
        _marina.Input.Wheel(e.Delta / (float)SystemInformation.MouseWheelScrollDelta, e.X, e.Y);

    // WM_KEYDOWN / WM_KEYUP, and their WM_SYS- forms, which is how Alt arrives.
    private const int KeyDownMessage = 0x0100;
    private const int KeyUpMessage = 0x0101;
    private const int SystemKeyDownMessage = 0x0104;
    private const int SystemKeyUpMessage = 0x0105;

    /// <summary>
    /// Keeps the view's idea of the modifier keys up to date the moment one goes down or up, so a preview that
    /// depends on one redraws straight away instead of waiting for the pointer to move.
    /// </summary>
    /// <param name="m">The message about to be dispatched.</param>
    /// <returns>Always false: this only watches, and never swallows a key.</returns>
    bool IMessageFilter.PreFilterMessage(ref Message m)
    {
        if (m.Msg is not (KeyDownMessage or KeyUpMessage or SystemKeyDownMessage or SystemKeyUpMessage)) return false;

        var down = m.Msg is KeyDownMessage or SystemKeyDownMessage;
        var held = CurrentModifiers();

        // Read the key out of the message rather than trusting ModifierKeys, which still has the bit set while the
        // key-up that clears it is being delivered.
        var changed = ((Keys)(int)m.WParam) switch
        {
            Keys.Menu or Keys.LMenu or Keys.RMenu => InputModifiers.Alt,
            Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey => InputModifiers.Shift,
            Keys.ControlKey or Keys.LControlKey or Keys.RControlKey => InputModifiers.Control,
            _ => InputModifiers.None,
        };

        if (changed == InputModifiers.None) return false;
        _marina.Input.ModifiersChanged(down ? held | changed : held & ~changed);
        return false;
    }

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
        Keys.Enter => MarinaKey.Enter,
        Keys.Back => MarinaKey.Backspace,
        Keys.Delete => MarinaKey.Delete,
        Keys.Z when (ModifierKeys & Keys.Control) != 0 => MarinaKey.Undo,
        _ => null,
    };
}
