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
        _marina.PopupChanged += OnPopupChanged;

        if (LicenseManager.UsageMode != LicenseUsageMode.Designtime)
        {
            CreateGlControl();
        }

        Controls.Add(_popupPanel);
        _popupPanel.BringToFront();
    }

    /// <summary>Raised if OpenGL initialization or rendering fails (e.g. no OpenGL 3.3 driver).</summary>
    public event EventHandler<ThreadExceptionEventArgs>? RenderError;

    /// <summary>The visualizer this control displays. Can be swapped at runtime.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public MarinaVisualizer Marina
    {
        get => _marina;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(value, _marina)) return;
            _marina.PopupChanged -= OnPopupChanged;
            _marina = value;
            _marina.PopupChanged += OnPopupChanged;
            ShowPopup(_marina.ActivePopup);
        }
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
        get => _frameTimer.Enabled;
        set => _frameTimer.Enabled = value && _glControl is not null;
    }

    /// <summary>Backend and GPU description after the first frame, e.g. for a status bar.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string RendererDescription =>
        _renderer?.DeviceDescription is { } device ? $"{_renderer.BackendName} | {device}" : "OpenGL (not initialized)";

    /// <summary>Starts the render loop once the window handle exists.</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_glControl is not null) _frameTimer.Start();
    }

    /// <summary>Draws a placeholder in the designer (the 3D view is drawn by the embedded GL control).</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_glControl is null)
        {
            TextRenderer.DrawText(e.Graphics, "VirtualMarina 3D view", Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Stops rendering and releases GPU resources, the GL control and the popup.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _marina.PopupChanged -= OnPopupChanged;
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

    private void OnPopupChanged(object? sender, SlipPopupChangedEventArgs e) => ShowPopup(e.Current);

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
