using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using VirtualMarina.Blazor.Resources;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Input;

namespace VirtualMarina.Blazor;

/// <summary>
/// Blazor WebAssembly component that renders a <see cref="MarinaVisualizer"/> into a WebGL 2 canvas.
/// </summary>
/// <example>
/// <code>
/// &lt;MarinaView Marina="_marina" style="height:600px" /&gt;
/// </code>
/// </example>
/// <remarks>
/// <para>
/// The view draws only while something changes or moves (see <see cref="MarinaVisualizer.NeedsRedraw"/> and
/// <see cref="MarinaVisualizer.IsAnimating"/>), and not at all while it is scrolled out of sight or its tab is hidden, so a
/// still marina costs nothing. <see cref="ContinuousRendering"/> makes it draw every display frame regardless.
/// </para>
/// <para>
/// The popup's look comes from <c>_content/VirtualMarina.Blazor/marinaView.css</c>, which the script links into the page
/// by itself. Its rules sit in the <c>virtualmarina</c> cascade layer, so any rule of the host's for the <c>vm-popup</c>
/// classes wins over them. A page with a strict Content-Security-Policy can reference the file itself with a
/// <c>&lt;link rel="stylesheet"&gt;</c>; the script then leaves it alone.
/// </para>
/// </remarks>
public partial class MarinaView : ComponentBase, IAsyncDisposable
{
    private const string ModulePath = "./_content/VirtualMarina.Blazor/marinaWebGL.js";

    /// <summary>Frames that may throw one after another before the view stops drawing and shows the error.</summary>
    private const int MaxFrameFailures = 3;

    private ElementReference _canvas;
    private ElementReference _popupElement;
    private MarinaVisualizer? _subscribedMarina;
    private BerthPopup? _popup;
    private IJSInProcessObjectReference? _module;
    private DotNetObjectReference<MarinaView>? _selfReference;
    private WebGlSceneRenderer? _renderer;
    private int _viewId = -1;
    private double _lastTimestampMs = -1;
    private int _frameFailures;
    private string? _errorMessage;
    private bool _disposed;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    /// <summary>The visualizer to display. Required.</summary>
    [Parameter, EditorRequired]
    public MarinaVisualizer Marina { get; set; } = default!;

    /// <summary>
    /// How the marina is drawn and animated (lighting, waves, status colors, boat opacities, land and trees, piers, labels, selection,
    /// camera). Assigned to <c>Marina.Style</c> when set; leave it null to keep the visualizer's style. (Named <c>MarinaStyle</c> because
    /// <see cref="Style"/> is the element's inline CSS.)
    /// </summary>
    [Parameter]
    public Core.Rendering.MarinaStyle? MarinaStyle { get; set; }

    /// <summary>Extra CSS classes for the outer element.</summary>
    [Parameter]
    public string? CssClass { get; set; }

    /// <summary>Extra inline CSS for the outer element (it fills its parent by default).</summary>
    [Parameter]
    public string? Style { get; set; }

    /// <summary>Invoked once WebGL is running, with a description of the renderer/GPU.</summary>
    [Parameter]
    public EventCallback<string> OnRendererReady { get; set; }

    /// <summary>Invoked if WebGL cannot be initialized, or when drawing keeps failing and the view stops.</summary>
    [Parameter]
    public EventCallback<string> OnRendererError { get; set; }

    /// <summary>
    /// Draw every display frame, even when nothing changes. Off by default: the view draws only while something changes or
    /// moves, which is all a marina needs.
    /// </summary>
    [Parameter]
    public bool ContinuousRendering { get; set; }

    /// <summary>
    /// Whether the waves, floating boats, pulses and traffic move. When false the picture holds still (and the view stops
    /// drawing), while the camera and input still work. Default true.
    /// </summary>
    [Parameter]
    public bool Animate { get; set; } = true;

    /// <summary>The canvas's accessible name, read by screen readers; a short localized description when null.</summary>
    [Parameter]
    public string? AriaLabel { get; set; }

    /// <summary>Backend and GPU description once WebGL is running, otherwise null.</summary>
    public string? RendererDescription { get; private set; }

    /// <summary>
    /// Shows PNG, JPEG or WebP bytes as the designer's reference image (north at the top). The browser decodes the image, so no .NET
    /// image library is needed. Returns null when the bytes can't be decoded or the view isn't running yet.
    /// </summary>
    /// <param name="data">The file contents, e.g. from an <c>InputFile</c>.</param>
    /// <param name="contentType">MIME type, e.g. "image/png".</param>
    /// <param name="metersPerPixel">Known ground size of a pixel; null sizes it to the layout until calibrated.</param>
    public async Task<ReferenceImage?> LoadReferenceImageAsync(byte[] data, string contentType, float? metersPerPixel = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (_module is null || _viewId < 0 || data.Length == 0) return null;

        // The browser decodes the picture once: the size comes back, and the decoded picture stays with the view to become
        // the texture, so the bytes are neither sent nor decoded a second time when the image is first drawn.
        var decoded = await _module.InvokeAsync<double[]?>("decodeImage", _viewId, data, contentType);
        if (decoded is not { Length: 3 } || decoded[0] < 1d || decoded[1] < 1d) return null;

        var image = ReferenceImage.FromEncoded(data, (int)decoded[0], (int)decoded[1], contentType);
        _module.InvokeVoid("adoptImage", _viewId, (int)decoded[2], image.Key);
        _renderer?.ReferenceImageUploaded(image.Key);
        Marina.Designer.SetReferenceImage(image, metersPerPixel);
        return image;
    }

    /// <summary>Subscribes to the visualizer's popup changes.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Marina"/> is not set.</exception>
    protected override void OnParametersSet()
    {
        if (Marina is null) throw new InvalidOperationException($"{nameof(MarinaView)} requires the {nameof(Marina)} parameter.");
        if (MarinaStyle is not null && !ReferenceEquals(Marina.Style, MarinaStyle)) Marina.Style = MarinaStyle;
        if (ReferenceEquals(_subscribedMarina, Marina)) return;

        if (_subscribedMarina is not null)
        {
            _subscribedMarina.PopupChanged -= OnPopupChanged;
            _subscribedMarina.RedrawRequested -= OnRedrawRequested;

            // Meshes and the like are tracked by what was uploaded for the old visualizer; start the new one from nothing.
            _renderer?.Reset();
        }

        _subscribedMarina = Marina;
        Marina.PopupChanged += OnPopupChanged;
        Marina.RedrawRequested += OnRedrawRequested;
        _popup = Marina.ActivePopup;
    }

    /// <summary>Wakes the frame loop for a new parameter (<see cref="ContinuousRendering"/>, <see cref="Animate"/>, a new marina).</summary>
    protected override void OnAfterRender(bool firstRender) => Wake();

    private void OnPopupChanged(object? sender, BerthPopupChangedEventArgs e)
    {
        _popup = e.Current;
        StateHasChanged();
    }

    /// <summary>Something changed in the marina: a view that stopped drawing, because nothing moved, starts again.</summary>
    private void OnRedrawRequested(object? sender, EventArgs e) => Wake();

    private void Wake()
    {
        if (_module is null || _renderer is null || _disposed) return;
        try
        {
            _module.InvokeVoid("wakeView", _viewId);
        }
        catch (JSDisconnectedException)
        {
            // Page is being torn down.
        }
    }

    /// <summary>The popup's accessible name: the title it shows.</summary>
    private string? PopupLabel => _popup is { } popup
        ? popup.Tooltip.IsVisible && !string.IsNullOrWhiteSpace(popup.Tooltip.Title) ? popup.Tooltip.Title : popup.PrimaryBerth.DisplayName
        : null;

    private string? PopupKindClass => _popup?.Kind switch
    {
        BerthPopupKind.Actions => "vm-popup--actions",
        BerthPopupKind.Tooltip => "vm-popup--tooltip",
        _ => null,
    };

    private string? AccentStyle => _popup?.Tooltip.AccentColor is { } accent ? $"--vm-accent:{accent.ToHex()}" : null;

    private static string? ActionStyleClass(BerthAction action) => action.Style switch
    {
        BerthActionStyle.Primary => "vm-popup__action--primary",
        BerthActionStyle.Danger => "vm-popup__action--danger",
        _ => null,
    };

    /// <summary>On first render: loads the JS module, creates the WebGL context and starts the animation loop.</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        if (JS is not IJSInProcessRuntime)
        {
            await FailAsync("MarinaView requires Blazor WebAssembly (synchronous JS interop).");
            return;
        }

        try
        {
            var module = await JS.InvokeAsync<IJSInProcessObjectReference>("import", ModulePath);
            if (_disposed)
            {
                // Disposed while the module was loading: DisposeAsync found nothing to release, so nothing may start now.
                await DisposeModuleAsync(module);
                return;
            }

            _module = module;
            _selfReference = DotNetObjectReference.Create(this);
            _viewId = _module.Invoke<int>("createView", _canvas, _selfReference, _popupElement);
            if (_viewId < 0)
            {
                await FailAsync("WebGL 2 is not available in this browser.");
                return;
            }

            _renderer = new WebGlSceneRenderer(_module, _viewId);
            _renderer.Initialize();
            RendererDescription = $"{_renderer.BackendName} | {_renderer.DeviceDescription}";
            _module.InvokeVoid("start", _viewId);
            await OnRendererReady.InvokeAsync(RendererDescription);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            await FailAsync(ex.Message);
        }
    }

    // ---- Called from marinaWebGL.js ----------------------------------------------------------------

    /// <summary>
    /// Called by marinaWebGL.js once per display frame while the view is awake: draws a frame (positioning the popup with it)
    /// if anything changed or moves. Returns false when nothing did, and the script then stops asking until it is woken.
    /// Not for direct use.
    /// </summary>
    [JSInvokable]
    public bool OnAnimationFrame(double timestampMs, double cssWidth, double cssHeight)
    {
        if (_renderer is null || _errorMessage is not null) return false;

        Marina.SetViewportSize((float)cssWidth, (float)cssHeight);
        if (!NeedsFrame())
        {
            // Asleep from here on; the first frame after waking starts the clock again rather than jumping.
            _lastTimestampMs = -1;
            return false;
        }

        var deltaSeconds = _lastTimestampMs < 0 ? 0d : (timestampMs - _lastTimestampMs) / 1000d;
        _lastTimestampMs = timestampMs;

        try
        {
            Marina.Update(deltaSeconds, Animate);
            _renderer.SetPopupAnchor(_popup is not null && Marina.TryGetPopupAnchor(out var anchor) ? anchor : null);
            _renderer.Render(Marina.BuildRenderFrame());
            _frameFailures = 0;
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or ArgumentException)
        {
            // One bad frame is retried; a frame that keeps failing stops the view and says why, rather than failing sixty
            // times a second with nothing on screen to show for it.
            if (++_frameFailures < MaxFrameFailures) return true;
            _ = InvokeAsync(() => FailAsync(Strings.Format(Strings.RenderingStopped, ex.Message)));
            return false;
        }

        return true;
    }

    /// <summary>
    /// Called by marinaWebGL.js while the view sleeps: true when there is something to draw after all (the camera or the
    /// lighting was changed without the visualizer announcing it). Not for direct use.
    /// </summary>
    [JSInvokable]
    public bool NeedsFrame() =>
        _renderer is not null && _errorMessage is null && (ContinuousRendering || Marina.NeedsRedraw || (Animate && Marina.IsAnimating));

    /// <summary>
    /// Called by marinaWebGL.js when its frame loop gave up after frames failing one after another: shows the error and
    /// reports it through <see cref="OnRendererError"/>. Not for direct use.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    [JSInvokable]
    public Task OnRenderLoopFailed(string message) => InvokeAsync(() => FailAsync(Strings.Format(Strings.RenderingStopped, message)));

    /// <summary>Called by marinaWebGL.js; forwards to <see cref="MarinaInputController.PointerDown"/>. Not for direct use.</summary>
    [JSInvokable]
    public void OnPointerDown(double x, double y, int button, int modifiers) =>
        Marina.Input.PointerDown((float)x, (float)y, MapButton(button), (InputModifiers)modifiers);

    /// <summary>
    /// Called by marinaWebGL.js; forwards to <see cref="MarinaInputController.PointerMove"/>. Returns the CSS cursor to show when no
    /// button is held: "pointer" over a selectable berth or boat, "crosshair" or "move" for designer tools, otherwise "grab".
    /// Not for direct use.
    /// </summary>
    [JSInvokable]
    public string OnPointerMove(double x, double y, int modifiers)
    {
        Marina.Input.PointerMove((float)x, (float)y, (InputModifiers)modifiers);
        var designer = Marina.Designer;
        if (designer.IsActive)
        {
            return designer.Tool switch
            {
                DesignTool.Navigate => "grab",
                DesignTool.MoveReferenceImage => designer.ReferenceImage is null ? "not-allowed" : "move",
                _ => "crosshair",
            };
        }

        return !Marina.Input.IsDragging && Marina.HoveredBerth is not null ? "pointer" : "grab";
    }

    /// <summary>Called by marinaWebGL.js; forwards to <see cref="MarinaInputController.PointerUp"/>. Not for direct use.</summary>
    [JSInvokable]
    public void OnPointerUp(double x, double y, int button, int modifiers) =>
        Marina.Input.PointerUp((float)x, (float)y, MapButton(button), (InputModifiers)modifiers);

    /// <summary>Called by marinaWebGL.js; forwards to <see cref="MarinaInputController.DoubleClick"/>. Not for direct use.</summary>
    [JSInvokable]
    public void OnDoubleClick(double x, double y, int button, int modifiers) =>
        Marina.Input.DoubleClick((float)x, (float)y, MapButton(button), (InputModifiers)modifiers);

    /// <summary>Called by marinaWebGL.js; forwards to <see cref="MarinaInputController.Wheel"/>. Not for direct use.</summary>
    [JSInvokable]
    public void OnWheel(double notches, double x, double y) =>
        Marina.Input.Wheel((float)notches, (float)x, (float)y);

    /// <summary>Called by marinaWebGL.js; forwards to <see cref="MarinaInputController.PointerLeave"/>. Not for direct use.</summary>
    [JSInvokable]
    public void OnPointerLeave() => Marina.Input.PointerLeave();

    /// <summary>
    /// Called by marinaWebGL.js; maps the key through <see cref="MarinaKeyMap.FromDomKey"/> and forwards it to
    /// <see cref="MarinaInputController.KeyDown"/>. Returns true when handled, and only then does the script stop the
    /// key going any further: Ctrl, Alt and Cmd chords (other than the undo and redo chords Ctrl+Z, Ctrl+Shift+Z and
    /// Ctrl+Y), and keys the view has no use for right now, carry on to the page and the browser. Not for direct use.
    /// </summary>
    /// <param name="code"><c>KeyboardEvent.code</c>: the physical key.</param>
    /// <param name="key"><c>KeyboardEvent.key</c>: what the key types.</param>
    /// <param name="modifiers">The modifiers held, as <see cref="InputModifiers"/> flags (Cmd counts as Control).</param>
    [JSInvokable]
    public bool OnKeyDown(string? code, string? key, int modifiers)
    {
        var held = (InputModifiers)modifiers;
        return MarinaKeyMap.FromDomKey(code, key, held) is { } mapped && Marina.Input.KeyDown(mapped, held);
    }

    /// <summary>Stops the animation loop, releases WebGL resources and unsubscribes from the visualizer.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_subscribedMarina is not null)
        {
            _subscribedMarina.PopupChanged -= OnPopupChanged;
            _subscribedMarina.RedrawRequested -= OnRedrawRequested;
            _subscribedMarina = null;
        }

        if (_module is not null)
        {
            try
            {
                if (_viewId >= 0) _module.InvokeVoid("destroyView", _viewId);
            }
            catch (JSDisconnectedException)
            {
                // Page is being torn down.
            }

            await DisposeModuleAsync(_module);
        }

        _renderer?.Dispose();
        _selfReference?.Dispose();
        GC.SuppressFinalize(this);
    }

    private static PointerButton MapButton(int domButton) => domButton switch
    {
        0 => PointerButton.Left,
        1 => PointerButton.Middle,
        2 => PointerButton.Right,
        _ => PointerButton.None,
    };

    private static async ValueTask DisposeModuleAsync(IJSInProcessObjectReference module)
    {
        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // Page is being torn down.
        }
    }

    private async Task FailAsync(string message)
    {
        if (_disposed) return;
        _errorMessage = message;
        StateHasChanged();
        await OnRendererError.InvokeAsync(message);
    }
}
