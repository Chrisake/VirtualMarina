using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using VirtualMarina.Core.Api;
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
public partial class MarinaView : ComponentBase, IAsyncDisposable
{
    private const string ModulePath = "./_content/VirtualMarina.Blazor/marinaWebGL.js";

    private static readonly double[] HiddenAnchor = { 0d, 0d, 0d };

    private ElementReference _canvas;
    private ElementReference _popupElement;
    private MarinaVisualizer? _subscribedMarina;
    private SlipPopup? _popup;
    private IJSInProcessObjectReference? _module;
    private DotNetObjectReference<MarinaView>? _selfReference;
    private WebGlSceneRenderer? _renderer;
    private int _viewId = -1;
    private double _lastTimestampMs = -1;
    private string? _errorMessage;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    /// <summary>The visualizer to display. Required.</summary>
    [Parameter, EditorRequired]
    public MarinaVisualizer Marina { get; set; } = default!;

    /// <summary>Extra CSS classes for the outer element.</summary>
    [Parameter]
    public string? CssClass { get; set; }

    /// <summary>Extra inline CSS for the outer element (it fills its parent by default).</summary>
    [Parameter]
    public string? Style { get; set; }

    /// <summary>Invoked once WebGL is running, with a description of the renderer/GPU.</summary>
    [Parameter]
    public EventCallback<string> OnRendererReady { get; set; }

    /// <summary>Invoked if WebGL cannot be initialized.</summary>
    [Parameter]
    public EventCallback<string> OnRendererError { get; set; }

    /// <summary>Backend and GPU description once WebGL is running, otherwise null.</summary>
    public string? RendererDescription { get; private set; }

    /// <summary>Subscribes to the visualizer's popup changes.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Marina"/> is not set.</exception>
    protected override void OnParametersSet()
    {
        if (Marina is null) throw new InvalidOperationException($"{nameof(MarinaView)} requires the {nameof(Marina)} parameter.");
        if (ReferenceEquals(_subscribedMarina, Marina)) return;

        if (_subscribedMarina is not null) _subscribedMarina.PopupChanged -= OnPopupChanged;
        _subscribedMarina = Marina;
        Marina.PopupChanged += OnPopupChanged;
        _popup = Marina.ActivePopup;
    }

    private void OnPopupChanged(object? sender, SlipPopupChangedEventArgs e)
    {
        _popup = e.Current;
        StateHasChanged();
    }

    private string? PopupKindClass => _popup?.Kind switch
    {
        SlipPopupKind.Actions => "vm-popup--actions",
        SlipPopupKind.Tooltip => "vm-popup--tooltip",
        _ => null,
    };

    private string? AccentStyle => _popup?.Tooltip.AccentColor is { } accent ? $"--vm-accent:{accent.ToHex()}" : null;

    private static string? ActionStyleClass(SlipAction action) => action.Style switch
    {
        SlipActionStyle.Primary => "vm-popup__action--primary",
        SlipActionStyle.Danger => "vm-popup__action--danger",
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
            _module = await JS.InvokeAsync<IJSInProcessObjectReference>("import", ModulePath);
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

    /// <summary>Renders a frame and returns the popup anchor as [visible (0/1), x, y] in CSS pixels.</summary>
    [JSInvokable]
    public double[] OnAnimationFrame(double timestampMs, double cssWidth, double cssHeight)
    {
        if (_renderer is null) return HiddenAnchor;

        var deltaSeconds = _lastTimestampMs < 0 ? 0d : (timestampMs - _lastTimestampMs) / 1000d;
        _lastTimestampMs = timestampMs;

        Marina.SetViewportSize((float)cssWidth, (float)cssHeight);
        Marina.Update(deltaSeconds);
        _renderer.Render(Marina.BuildRenderFrame());

        return _popup is not null && Marina.TryGetPopupAnchor(out var anchor)
            ? new double[] { 1d, anchor.X, anchor.Y }
            : HiddenAnchor;
    }

    /// <summary>Called by marinaWebGL.js; forwards to <see cref="MarinaInputController.PointerDown"/>. Not for direct use.</summary>
    [JSInvokable]
    public void OnPointerDown(double x, double y, int button, int modifiers) =>
        Marina.Input.PointerDown((float)x, (float)y, MapButton(button), (InputModifiers)modifiers);

    /// <summary>
    /// Called by marinaWebGL.js; forwards to <see cref="MarinaInputController.PointerMove"/>. Returns true when the pointer is over a
    /// selectable slip or boat (the JS side shows a pointer cursor). Not for direct use.
    /// </summary>
    [JSInvokable]
    public bool OnPointerMove(double x, double y, int modifiers)
    {
        Marina.Input.PointerMove((float)x, (float)y, (InputModifiers)modifiers);
        return !Marina.Input.IsDragging && Marina.HoveredSlip is not null;
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

    /// <summary>Called by marinaWebGL.js; maps DOM keys to <see cref="MarinaKey"/>. Returns true when handled. Not for direct use.</summary>
    [JSInvokable]
    public bool OnKeyDown(string key, int modifiers)
    {
        MarinaKey? mapped = key switch
        {
            "ArrowLeft" or "a" or "A" => MarinaKey.Left,
            "ArrowRight" or "d" or "D" => MarinaKey.Right,
            "ArrowUp" or "w" or "W" => MarinaKey.Up,
            "ArrowDown" or "s" or "S" => MarinaKey.Down,
            "PageUp" => MarinaKey.PageUp,
            "PageDown" => MarinaKey.PageDown,
            "+" or "=" => MarinaKey.ZoomIn,
            "-" or "_" => MarinaKey.ZoomOut,
            "Home" => MarinaKey.Home,
            "Escape" => MarinaKey.Escape,
            _ => null,
        };
        return mapped is { } k && Marina.Input.KeyDown(k, (InputModifiers)modifiers);
    }

    /// <summary>Stops the animation loop, releases WebGL resources and unsubscribes from the visualizer.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_subscribedMarina is not null)
        {
            _subscribedMarina.PopupChanged -= OnPopupChanged;
            _subscribedMarina = null;
        }

        if (_module is not null)
        {
            try
            {
                if (_viewId >= 0) _module.InvokeVoid("destroyView", _viewId);
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // Page is being torn down.
            }
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

    private async Task FailAsync(string message)
    {
        _errorMessage = message;
        StateHasChanged();
        await OnRendererError.InvokeAsync(message);
    }
}
