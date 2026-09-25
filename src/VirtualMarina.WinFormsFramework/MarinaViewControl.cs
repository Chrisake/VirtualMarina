using System.ComponentModel;
using System.Diagnostics;
using OpenTK;
using OpenTK.Graphics;
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
/// <para>
/// The view draws only while something changes or moves (see <see cref="MarinaVisualizer.NeedsRedraw"/> and
/// <see cref="MarinaVisualizer.IsAnimating"/>), in step with the display, and not at all while its window is minimized or
/// hidden; <see cref="ContinuousRendering"/> makes it draw every frame regardless.
/// </para>
/// <para>
/// The visualizer is not thread-safe: call <see cref="Marina"/>'s members on the UI thread. If a status update does arrive
/// on another thread anyway, the control marshals what it does in response (redrawing, the popup, the events it forwards)
/// onto its own thread.
/// </para>
/// </remarks>
[ToolboxItem(true)]
[Description("Interactive 3D marina view.")]
[DefaultEvent(nameof(BerthSelected))]
public sealed class MarinaViewControl : UserControl
{
    /// <summary>How often a view with nothing to draw checks whether that is still so (a camera moved by code, say).</summary>
    private const int IdleIntervalMilliseconds = 100;

    private static readonly Color ViewBackColor = Color.FromArgb(194, 217, 235);

    private readonly System.Windows.Forms.Timer _frameTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly SelectionPopupPanel _popupPanel = new();
    private readonly ModifierWatcher _modifierWatcher;
    private readonly Dictionary<int, ReferenceImage> _decodedImages = [];
    private GLControl? _glControl;
    private OpenGlSceneRenderer? _renderer;
    private MarinaVisualizer _marina;
    private double _lastFrameSeconds;
    private bool _renderFailed;
    private string? _renderFailure;
    private bool _animate = true;
    private int _frameInterval = 15;
    private int _samples = 4;
    private (Point Anchor, Size View, bool Shown) _popupPlacement;

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
        BackColor = ViewBackColor;
        DoubleBuffered = false;
        _modifierWatcher = new ModifierWatcher(this);

        // The timer only asks whether there is anything to draw; a frame is drawn when there is, in step with the display
        // (the swap waits for it). With nothing to draw it slows to a check every so often.
        _frameTimer = new System.Windows.Forms.Timer { Interval = _frameInterval };
        _frameTimer.Tick += (_, _) => OnFrameTick();

        _popupPanel.ActionClicked += (_, actionId) => _marina.InvokeBerthAction(actionId);
        _popupPanel.CloseClicked += (_, _) => _marina.ClosePopup();
        AttachMarina(_marina);

        // The OpenGL surface is created in OnHandleCreated, where design mode can be detected reliably:
        // the Visual Studio designer must never create it (GLFW would throw "can only be called from the main thread").
        SetStyle(ControlStyles.ResizeRedraw, true);
        Controls.Add(_popupPanel);
        _popupPanel.BringToFront();
    }

    /// <summary>
    /// Raised if OpenGL initialization or rendering fails (e.g. no OpenGL 3.3 driver, or a remote desktop without one). The
    /// view then shows a placeholder with the error instead of the marina; <see cref="RetryRendering"/> tries again.
    /// </summary>
    [Category("Marina")]
    [Description("OpenGL could not be started or a frame failed to draw; the view shows a placeholder until RetryRendering.")]
    public event EventHandler<ThreadExceptionEventArgs>? RenderError;

    /// <summary>Raised after <see cref="Marina"/> was given a different visualizer.</summary>
    [Category("Marina")]
    [Description("The Marina property was set to a different visualizer.")]
    public event EventHandler? MarinaChanged;

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

    /// <inheritdoc cref="IMarinaVisualizer.CameraPresetsChanged"/>
    [Category("Marina")]
    [Description("The list of camera views may have changed; read Marina.CameraPresets again.")]
    public event EventHandler? CameraPresetsChanged;

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

            // The renderer skips uploads whose mesh-library version it has already seen, but versions count per visualizer:
            // the new one's could match and leave the old marina's meshes on screen. A fresh renderer starts from nothing.
            ReleaseRenderer();
            _decodedImages.Clear();
            ShowPopup(_marina.ActivePopup);
            RequestFrame();
            MarinaChanged?.Invoke(this, EventArgs.Empty);
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
        marina.CameraPresetsChanged += OnMarinaCameraPresetsChanged;
        marina.RedrawRequested += OnMarinaRedrawRequested;
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
        marina.CameraPresetsChanged -= OnMarinaCameraPresetsChanged;
        marina.RedrawRequested -= OnMarinaRedrawRequested;
    }

    // Events whose handlers fill in the event data (the popup's content) are marshalled synchronously; the rest, which
    // only report something, are posted, so a background thread never waits on the UI thread.
    private void OnMarinaBerthClicked(object? sender, BerthEventArgs e) => Post(() => BerthClicked?.Invoke(this, e));

    private void OnMarinaBerthSelected(object? sender, BerthSelectedEventArgs e) => Send(() => BerthSelected?.Invoke(this, e));

    private void OnMarinaMultiBerthSelected(object? sender, MultiBerthSelectedEventArgs e) => Send(() => MultiBerthSelected?.Invoke(this, e));

    private void OnMarinaSelectionChanged(object? sender, SelectionChangedEventArgs e) => Post(() => SelectionChanged?.Invoke(this, e));

    private void OnMarinaSelectionCleared(object? sender, EventArgs e) => Post(() => SelectionCleared?.Invoke(this, e));

    private void OnMarinaBerthActionInvoked(object? sender, BerthActionInvokedEventArgs e) => Post(() => BerthActionInvoked?.Invoke(this, e));

    private void OnMarinaBerthHoverChanged(object? sender, BerthHoverEventArgs e) => Post(() => BerthHoverChanged?.Invoke(this, e));

    private void OnMarinaBerthStatusChanged(object? sender, BerthStatusChangedEventArgs e) => Post(() => BerthStatusChanged?.Invoke(this, e));

    private void OnMarinaLayoutChanged(object? sender, LayoutChangedEventArgs e) => Post(() => LayoutChanged?.Invoke(this, e));

    private void OnMarinaCameraPresetsChanged(object? sender, EventArgs e) => Post(() => CameraPresetsChanged?.Invoke(this, e));

    /// <summary>Something changed in the marina: draw it now rather than at the next idle check.</summary>
    private void OnMarinaRedrawRequested(object? sender, EventArgs e) => Post(RequestFrame);

    /// <summary>Runs <paramref name="action"/> on the UI thread: at once when already on it, otherwise posted to it.</summary>
    private void Post(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired && IsHandleCreated) BeginInvoke(action);
        else action();
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread and waits for it, for events whose data the handler fills in.</summary>
    private void Send(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired && IsHandleCreated) Invoke(action);
        else action();
    }

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

    /// <summary>
    /// Shortest delay between frames in milliseconds while something moves (the Windows timer resolution is about 15 ms).
    /// Frames also wait for the display, so the rate never exceeds its refresh rate.
    /// </summary>
    [DefaultValue(15)]
    [Category("Behavior")]
    [Description("Shortest delay between frames in milliseconds while something moves.")]
    public int FrameIntervalMilliseconds
    {
        get => _frameInterval;
        set
        {
            _frameInterval = Math.Max(1, value);
            _frameTimer.Interval = _frameInterval;
        }
    }

    /// <summary>
    /// Whether the waves, floating boats, pulses and traffic move. When false the picture holds still and the view stops
    /// drawing until something changes; the camera and input still work, and every change is drawn.
    /// </summary>
    [DefaultValue(true)]
    [Category("Behavior")]
    [Description("Animate the water, boats, pulses and traffic. When off, the view still redraws on input and changes.")]
    public bool Animate
    {
        get => _animate;
        set
        {
            _animate = value;
            RequestFrame();
        }
    }

    /// <summary>
    /// Draw every frame, even when nothing changes. Off by default: the view draws only while something changes or moves,
    /// which is all a marina needs.
    /// </summary>
    [DefaultValue(false)]
    [Category("Behavior")]
    [Description("Draw every frame even when nothing changes.")]
    public bool ContinuousRendering { get; set; }

    /// <summary>
    /// Multisample anti-aliasing samples asked of the graphics driver (0 for none). Where the driver refuses them (some
    /// virtual machines and remote desktops), the view falls back to none by itself. Changing it restarts the OpenGL surface.
    /// </summary>
    [DefaultValue(4)]
    [Category("Appearance")]
    [Description("Multisample anti-aliasing samples (0 = none). Falls back to none where the driver refuses them.")]
    public int Samples
    {
        get => _samples;
        set
        {
            var samples = Math.Clamp(value, 0, 16);
            if (samples == _samples) return;
            _samples = samples;
            if (_glControl is not null) RecreateGlControl();
        }
    }

    /// <summary>The view's background: what shows before the first frame and around the placeholder.</summary>
    public override Color BackColor
    {
        get => base.BackColor;
        set => base.BackColor = value;
    }

    /// <summary>Puts <see cref="BackColor"/> back to the view's own default.</summary>
    public override void ResetBackColor() => BackColor = ViewBackColor;

    /// <summary>Tells the forms designer to store <see cref="BackColor"/> only when it differs from the view's default.</summary>
#pragma warning disable S1144 // Found by the forms designer by its name, through reflection.
    private bool ShouldSerializeBackColor() => BackColor != ViewBackColor;
#pragma warning restore S1144

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

        if (_glControl is null && !_renderFailed) CreateGlControl();
        _frameTimer.Start();

        // Watched application-wide rather than on the GL control, because Alt does not stay with it: pressing Alt
        // hands the keyboard to the window's menu bar, so the key-up would never arrive here and the preview would
        // stay stuck on "whole row" until the pointer moved again. The handle can be created more than once (it is
        // re-created when some window styles change), so the filter is added only while none is registered.
        _modifierWatcher.Start();
    }

    /// <summary>
    /// Stops watching the keyboard while there is no window (<see cref="OnHandleCreated"/> starts again), and lets go of
    /// the renderer while the GL surface's own window is still whole: Windows destroys this window before its children.
    /// </summary>
    protected override void OnHandleDestroyed(EventArgs e)
    {
        _modifierWatcher.Stop();
        ReleaseRenderer();
        base.OnHandleDestroyed(e);
    }

    /// <summary>Draws a frame once the view is shown again (nothing is drawn while it is hidden).</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        RequestFrame();
    }

    /// <summary>
    /// Starts OpenGL again after it failed (see <see cref="RenderError"/>): a new surface and renderer, with the anti-aliasing
    /// fallback tried again. Useful once the cause is gone, e.g. after reconnecting from a remote desktop session.
    /// </summary>
    public void RetryRendering()
    {
        if (IsInDesigner()) return;
        _renderFailed = false;
        _renderFailure = null;
        if (IsHandleCreated) RecreateGlControl();
        Invalidate();
    }

    /// <summary>
    /// In the designer, or when OpenGL is unavailable or failed, draws a placeholder describing the 3D render area (and, after
    /// a failure, what went wrong).
    /// </summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPaint(e);
        if (_glControl is { Visible: true }) return;

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

        TextRenderer.DrawText(e.Graphics, _renderFailure is null ? Strings.DesignTimeTitle : Strings.RenderFailedTitle, titleFont, titleRect, Color.FromArgb(40, 70, 95), flags);
        TextRenderer.DrawText(e.Graphics, _renderFailure ?? Strings.DesignTimeSubtitle, Font, subtitleRect, Color.FromArgb(70, 95, 120), flags);
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

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return process.ProcessName.Equals("DesignToolsServer", StringComparison.OrdinalIgnoreCase)
            || process.ProcessName.Equals("devenv", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Stops rendering and releases GPU resources, the GL control and the popup.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _modifierWatcher.Stop();
            DetachMarina(_marina);
            _frameTimer.Stop();
            _frameTimer.Dispose();
            ReleaseRenderer();
            _glControl?.Dispose();
            _glControl = null;
            _popupPanel.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Creates the OpenGL surface. The context is made as the surface joins this control, which is where a machine without
    /// a usable driver (a VM, a remote desktop) fails: that is caught, anti-aliasing is dropped and tried again, and if even
    /// that fails the view shows a placeholder and raises <see cref="RenderError"/> instead of taking the form down with it.
    /// </summary>
    private void CreateGlControl()
    {
        Exception? failure = null;
        foreach (var samples in _samples > 0 ? new[] { _samples, 0 } : new[] { 0 })
        {
            var gl = NewGlControl(samples);
            try
            {
                Controls.Add(gl);
                _glControl = gl;
                _popupPanel.BringToFront();
                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failure = ex;
                Controls.Remove(gl);
                gl.Dispose();
            }
        }

        FailRendering(failure!);
    }

    /// <summary>Throws the surface away and makes a new one, e.g. for different anti-aliasing.</summary>
    private void RecreateGlControl()
    {
        ReleaseRenderer();
        if (_glControl is { } old)
        {
            _glControl = null;
            Controls.Remove(old);
            old.Dispose();
        }

        CreateGlControl();
    }

    private GLControl NewGlControl(int samples)
    {
        // OpenTK 3's GLControl: a forward-compatible 3.3 context is a core profile one, as OpenTK 4's ContextProfile.Core.
        var mode = new GraphicsMode(new ColorFormat(32), depth: 24, stencil: 8, samples: samples);
        var gl = new GLControl(mode, 3, 3, GraphicsContextFlags.ForwardCompatible)
        {
            Dock = DockStyle.Fill,
            TabStop = true,
        };

        gl.Paint += OnGlPaint;
        gl.MouseDown += OnGlMouseDown;
        gl.MouseMove += OnGlMouseMove;
        gl.MouseUp += OnGlMouseUp;
        gl.MouseDoubleClick += OnGlMouseDoubleClick;
        gl.MouseWheel += OnGlMouseWheel;
        gl.MouseLeave += (_, _) =>
        {
            _marina.Input.PointerLeave();
            RequestFrame();
        };
        gl.PreviewKeyDown += OnGlPreviewKeyDown;
        gl.KeyDown += OnGlKeyDown;

        // Letting go of the view entirely is the same as letting go of every key: nothing is held any more.
        gl.LostFocus += (_, _) => _marina.Input.ModifiersChanged(InputModifiers.None);

        // The GL context lives and dies with the GL control's window, which is re-created whenever this control's is. The
        // renderer's buffers and programs die with the old context, so it is let go here and rebuilt on the next paint.
        gl.HandleDestroyed += (_, _) => ReleaseRenderer();

        ForwardMouseEvents(gl);
        return gl;
    }

    /// <summary>
    /// Stops drawing after OpenGL failed: the surface is hidden so the placeholder shows, with the error, and
    /// <see cref="RenderError"/> is raised. <see cref="RetryRendering"/> starts again.
    /// </summary>
    private void FailRendering(Exception error)
    {
        _renderFailed = true;
        _renderFailure = error.Message;
        ReleaseRenderer();
        if (_glControl is not null) _glControl.Visible = false;
        _popupPanel.Visible = false;
        Invalidate();
        RenderError?.Invoke(this, new ThreadExceptionEventArgs(error));
    }

    /// <summary>
    /// The GL surface covers the whole control, so the control itself never sees the mouse. Raises the surface's mouse events
    /// again as this control's own, in this control's client coordinates, for hosts that listen on the control (a status bar
    /// showing the pointer position, say). The view's own input handling stays on the surface's events.
    /// </summary>
    /// <remarks>
    /// MouseWheel is raised again from <see cref="OnGlMouseWheel"/>, which marks the wheel handled so it does not also
    /// scroll the form around the view.
    /// </remarks>
    private void ForwardMouseEvents(GLControl gl)
    {
        gl.MouseDown += (_, e) => OnMouseDown(ToClient(gl, e));
        gl.MouseMove += (_, e) => OnMouseMove(ToClient(gl, e));
        gl.MouseUp += (_, e) => OnMouseUp(ToClient(gl, e));
        gl.MouseClick += (_, e) => OnMouseClick(ToClient(gl, e));
        gl.MouseDoubleClick += (_, e) => OnMouseDoubleClick(ToClient(gl, e));
        gl.Click += (_, e) => OnClick(e);
        gl.DoubleClick += (_, e) => OnDoubleClick(e);
        gl.MouseEnter += (_, e) => OnMouseEnter(e);
        gl.MouseLeave += (_, e) => OnMouseLeave(e);
        gl.MouseHover += (_, e) => OnMouseHover(e);
    }

    private static MouseEventArgs ToClient(GLControl gl, MouseEventArgs e) =>
        gl.Location == Point.Empty ? e : new MouseEventArgs(e.Button, e.Clicks, e.X + gl.Left, e.Y + gl.Top, e.Delta);

    /// <summary>Frees the renderer's GPU resources, if it has any; the next paint creates a new one.</summary>
    private void ReleaseRenderer()
    {
        var renderer = _renderer;
        if (renderer is null) return;
        _renderer = null;

        // Without a live context there is nothing to free: the resources went with it.
        if (_glControl is not { IsHandleCreated: true, IsDisposed: false } gl) return;

        try
        {
            gl.MakeCurrent();
        }
        catch (GraphicsContextException)
        {
            // The window is already being torn down (closing the form destroys it from the top), and some drivers then
            // refuse to make its context current. GLControl deletes that context straight after, and every buffer and
            // program in it goes with it, so there is nothing left to free by hand, and nothing may be called without it.
            return;
        }

        renderer.Dispose();
    }

    private void OnPopupChanged(object? sender, BerthPopupChangedEventArgs e) => Post(() =>
    {
        ShowPopup(e.Current);
        PopupChanged?.Invoke(this, e);
    });

    private void ShowPopup(BerthPopup? popup)
    {
        if (IsDisposed) return;
        if (popup is null)
        {
            _popupPanel.Visible = false;
            _popupPlacement = default;
            return;
        }

        _popupPanel.Present(popup);
        _popupPlacement = default; // new content: place it afresh
        UpdatePopupPosition();
        RequestFrame();
    }

    /// <summary>
    /// Keeps the popup pointing at its berth. Called after each frame drawn, so it follows the camera, and moves the panel
    /// only when the anchor or the view's size changed.
    /// </summary>
    private void UpdatePopupPosition()
    {
        if (_popupPanel.Popup is null || _marina.ActivePopup is null || _renderFailed)
        {
            if (_popupPanel.Visible) _popupPanel.Visible = false;
            _popupPlacement = default;
            return;
        }

        var size = _glControl?.ClientSize ?? ClientSize;
        var shown = _marina.TryGetPopupAnchor(out var anchor);
        var point = new Point((int)MathF.Round(anchor.X), (int)MathF.Round(anchor.Y));
        if ((point, size, shown) == _popupPlacement) return;
        _popupPlacement = (point, size, shown);

        var visible = shown && _popupPanel.PositionAt(point, size);
        if (_popupPanel.Visible == visible) return;
        _popupPanel.Visible = visible;
        if (visible) _popupPanel.BringToFront();
    }

    /// <summary>The timer's tick: draws a frame when there is something to draw, and otherwise slows down to an idle check.</summary>
    private void OnFrameTick()
    {
        var wanted = _glControl is { Visible: true } && !_renderFailed && IsOnScreen() && WantsFrame();
        _frameTimer.Interval = wanted ? _frameInterval : IdleIntervalMilliseconds;
        if (wanted) _glControl!.Invalidate();
    }

    /// <summary>True when a frame has to be drawn: something changed, something moves, or every frame is asked for.</summary>
    private bool WantsFrame() => ContinuousRendering || _marina.NeedsRedraw || (_animate && _marina.IsAnimating);

    /// <summary>False while the view, or the window it is in, is hidden or minimized: nothing is drawn then.</summary>
    private bool IsOnScreen() =>
        Visible && FindForm() is not { WindowState: FormWindowState.Minimized } && (FindForm()?.Visible ?? true);

    /// <summary>Draws a frame soon if there is anything new to draw (after input, or a change announced by the marina).</summary>
    private void RequestFrame()
    {
        if (_glControl is not { Visible: true } gl || _renderFailed || !WantsFrame()) return;
        gl.Invalidate();
        _frameTimer.Interval = _frameInterval;
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

                // Wait for the display when swapping, so the view never draws faster than the screen shows.
                if (gl.Context is { } context) context.SwapInterval = 1;
                _lastFrameSeconds = _clock.Elapsed.TotalSeconds;
            }

            var now = _clock.Elapsed.TotalSeconds;
            var delta = now - _lastFrameSeconds;
            _lastFrameSeconds = now;

            var size = gl.ClientSize;
            _marina.SetViewportSize(size.Width, size.Height);
            _marina.Update(delta, _animate);
            _renderer.Resize(size.Width, size.Height);
            _renderer.Render(WithDrawableImage(_marina.BuildRenderFrame()));
            gl.SwapBuffers();
            UpdatePopupPosition();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            FailRendering(ex);
        }
    }

    /// <summary>
    /// The frame, with the designer's reference image decoded if it came out of a marina file undecoded: the OpenGL renderer
    /// needs pixels, and the designer keeps the file's bytes as they are for the next save. Decoded once per image.
    /// </summary>
    private RenderFrame WithDrawableImage(RenderFrame frame)
    {
        if (frame.ReferenceImage is not { } layer || layer.Image.Rgba is not null || layer.Image.EncodedData is not { Length: > 0 }) return frame;

        if (!_decodedImages.TryGetValue(layer.Image.Key, out var decoded))
        {
            try
            {
                decoded = ReferenceImageLoader.Decoded(layer.Image);
            }
            catch (ArgumentException)
            {
                decoded = layer.Image; // not an image GDI+ can read: drawn as nothing, as before
            }

            _decodedImages.Clear(); // only the image on show is worth keeping
            _decodedImages[layer.Image.Key] = decoded;
        }

        return new RenderFrame
        {
            View = frame.View,
            Projection = frame.Projection,
            CameraPosition = frame.CameraPosition,
            Time = frame.Time,
            Lighting = frame.Lighting,
            Water = frame.Water,
            Layers = frame.Layers,

            // The marina hands the flat list over ready-made; leaving it out would have this copy work it out again.
            Objects = frame.Objects,
            SceneVersion = frame.SceneVersion,
            MarinaCenter = frame.MarinaCenter,
            WaterCenter = frame.WaterCenter,
            WaterDetailRadius = frame.WaterDetailRadius,
            Meshes = frame.Meshes,
            ReferenceImage = layer with { Image = decoded },
        };
    }

    private void OnGlMouseDown(object? sender, MouseEventArgs e)
    {
        _glControl?.Focus();
        _marina.Input.PointerDown(e.X, e.Y, MapButton(e.Button), CurrentModifiers());
        RequestFrame();
    }

    private void OnGlMouseMove(object? sender, MouseEventArgs e)
    {
        _marina.Input.PointerMove(e.X, e.Y, CurrentModifiers());
        UpdateCursor();
        RequestFrame();
    }

    private void OnGlMouseUp(object? sender, MouseEventArgs e)
    {
        _marina.Input.PointerUp(e.X, e.Y, MapButton(e.Button), CurrentModifiers());
        UpdateCursor();
        RequestFrame();
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

    private void OnGlMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        _marina.Input.DoubleClick(e.X, e.Y, MapButton(e.Button), CurrentModifiers());
        RequestFrame();
    }

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

    /// <summary>
    /// Zooms the view, and marks the wheel handled so the form around the view does not scroll as well; the control's own
    /// MouseWheel is raised for hosts listening on it.
    /// </summary>
    private void OnGlMouseWheel(object? sender, MouseEventArgs e)
    {
        _marina.Input.Wheel(e.Delta / (float)SystemInformation.MouseWheelScrollDelta, e.X, e.Y);
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
        if (_glControl is { } gl) OnMouseWheel(ToClient(gl, e));
        RequestFrame();
    }

    /// <summary>
    /// Claims the few keys that would otherwise never reach <see cref="OnGlKeyDown"/>: the arrows (which a container
    /// uses to move the focus), and Escape and Enter (which a form gives to its Cancel and Accept buttons) — and those
    /// only when unmodified and when the view has a use for them right now.
    /// </summary>
    /// <remarks>
    /// Claiming a key here makes WinForms skip <c>ProcessCmdKey</c> for it, so the form's menu shortcuts would never
    /// see it. That is why nothing else is claimed: letters, Ctrl+S, Ctrl+Z, F2 and the rest all go past the host's
    /// accelerators first and only arrive at the view's KeyDown if the host did not want them.
    /// </remarks>
    private void OnGlPreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
    {
        var modifiers = ToModifiers(e.Modifiers);
        if (MarinaKeyMap.IsChord(modifiers)) return;
        if (MarinaKeyMap.FromVirtualKey((int)e.KeyCode, modifiers) is not { } key) return;

        // The physical arrows only: W, A, S and D pan like them, but a letter is the host's to take first.
        e.IsInputKey = key switch
        {
            MarinaKey.Left or MarinaKey.Right or MarinaKey.Up or MarinaKey.Down => e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down,
            MarinaKey.Escape or MarinaKey.Enter => _marina.Input.WantsKey(key, modifiers),
            _ => e.IsInputKey,
        };
    }

    private void OnGlKeyDown(object? sender, KeyEventArgs e)
    {
        var modifiers = ToModifiers(e.Modifiers);
        if (MarinaKeyMap.FromVirtualKey((int)e.KeyCode, modifiers) is { } key && _marina.Input.KeyDown(key, modifiers))
        {
            e.Handled = true;
            RequestFrame();
        }
    }

    private static PointerButton MapButton(MouseButtons button) => button switch
    {
        MouseButtons.Left => PointerButton.Left,
        MouseButtons.Right => PointerButton.Right,
        MouseButtons.Middle => PointerButton.Middle,
        _ => PointerButton.None,
    };

    private static InputModifiers CurrentModifiers() => ToModifiers(ModifierKeys);

    private static InputModifiers ToModifiers(Keys keys)
    {
        var result = InputModifiers.None;
        if ((keys & Keys.Shift) != 0) result |= InputModifiers.Shift;
        if ((keys & Keys.Control) != 0) result |= InputModifiers.Control;
        if ((keys & Keys.Alt) != 0) result |= InputModifiers.Alt;
        return result;
    }

    /// <summary>
    /// Keeps the view's idea of the modifier keys up to date the moment one goes down or up, so a preview that depends on one
    /// redraws straight away instead of waiting for the pointer to move. Watches the application's messages, but acts only
    /// while the view's own window is the active one.
    /// </summary>
    private sealed class ModifierWatcher(MarinaViewControl owner) : IMessageFilter
    {
        // WM_KEYDOWN / WM_KEYUP, and their WM_SYS- forms, which is how Alt arrives.
        private const int KeyDownMessage = 0x0100;
        private const int KeyUpMessage = 0x0101;
        private const int SystemKeyDownMessage = 0x0104;
        private const int SystemKeyUpMessage = 0x0105;

        private bool _added;

        public void Start()
        {
            if (_added) return;
            Application.AddMessageFilter(this);
            _added = true;
        }

        public void Stop()
        {
            if (!_added) return;
            Application.RemoveMessageFilter(this);
            _added = false;
        }

        /// <summary>Watches a key message about to be dispatched.</summary>
        /// <returns>Always false: this only watches, and never swallows a key.</returns>
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg is not (KeyDownMessage or KeyUpMessage or SystemKeyDownMessage or SystemKeyUpMessage)) return false;

            // Keys typed into another window of the application are none of this view's business.
            if (owner.IsDisposed || Form.ActiveForm is not { } active || !ReferenceEquals(active, owner.FindForm())) return false;

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
            owner._marina.Input.ModifiersChanged(down ? held | changed : held & ~changed);
            owner.RequestFrame();
            return false;
        }
    }
}
