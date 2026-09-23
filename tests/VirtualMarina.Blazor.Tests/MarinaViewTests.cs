using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using VirtualMarina.Blazor;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Blazor.Tests;

/// <summary>
/// MarinaView without a browser: its start-up against a scripted marinaWebGL.js, the frame loop, the popup it renders
/// and the calls the script makes back into it.
/// </summary>
public class MarinaViewTests
{
    private const int ViewId = 3;

    private static MarinaVisualizer CreateMarina(bool calm = false)
    {
        var layout = new MarinaLayoutBuilder("Test")
            .AddPier("A", "Pier A", Vector2.Zero, 0f, 40f, pier => pier.AddBerths(PierSide.Left, 3, 5f, 12f))
            .Build();
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(layout);

        // Still water: nothing moves by itself, so the view has no reason to draw unless something changes.
        if (calm) marina.Water.WaveSpeed = 0f;
        return marina;
    }

    /// <summary>The popup anchor [shown, x, y] carried by the last frame drawn.</summary>
    private static float[] LastAnchor(FakeJsModule module) =>
        MemoryMarshal.Cast<byte, float>(Assert.IsType<byte[]>(module.CallsTo("renderFrame")[^1].Args[1])).ToArray()[79..82];

    /// <summary>A script that behaves like a browser with WebGL 2: a view is created and the shaders compile.</summary>
    private static FakeJsModule WorkingWebGl() => new((name, _) => name switch
    {
        "createView" => ViewId,
        "getDeviceDescription" => "Test GPU",
        _ => null,
    });

    private sealed class Harness : IDisposable
    {
        public required Rendered<MarinaView> Rendered { get; init; }

        public required List<string> Ready { get; init; }

        public required List<string> Errors { get; init; }

        public MarinaView View => Rendered.Component;

        public TestRenderer Renderer => Rendered.Renderer;

        public FakeJsModule Module => Rendered.Module;

        public FakeJsRuntime Js => Rendered.Js;

        public void Dispose() => Rendered.Dispose();
    }

    /// <summary>Renders a view of the marina, in Blazor WebAssembly unless <paramref name="webAssembly"/> says otherwise.</summary>
    private static async Task<Harness> StartAsync(
        MarinaVisualizer marina, FakeJsModule module, bool webAssembly = true, MarinaStyle? style = null, bool continuous = false)
    {
        var ready = new List<string>();
        var errors = new List<string>();
        var receiver = new object();
        var js = webAssembly ? new FakeWebAssemblyJsRuntime(module) : new FakeJsRuntime(module);
        var rendered = await TestRenderer.RenderAsync<MarinaView>(js, new Dictionary<string, object?>
        {
            [nameof(MarinaView.Marina)] = marina,
            [nameof(MarinaView.MarinaStyle)] = style,
            [nameof(MarinaView.ContinuousRendering)] = continuous,
            [nameof(MarinaView.OnRendererReady)] = EventCallback.Factory.Create<string>(receiver, ready.Add),
            [nameof(MarinaView.OnRendererError)] = EventCallback.Factory.Create<string>(receiver, errors.Add),
        });

        return new Harness { Rendered = rendered, Ready = ready, Errors = errors };
    }

    // ---- Start-up ------------------------------------------------------------------------------------

    [Fact]
    public async Task InWebAssembly_StartsWebGl_AndReportsTheRenderer()
    {
        using var module = WorkingWebGl();

        using var harness = await StartAsync(CreateMarina(), module);

        Assert.Equal("./_content/VirtualMarina.Blazor/marinaWebGL.js", Assert.Single(harness.Js.Calls, c => c.Identifier == "import").Args[0]);
        Assert.Single(harness.Module.CallsTo("initRenderer"));
        Assert.Equal(ViewId, Assert.Single(harness.Module.CallsTo("start")).Args[0]);
        Assert.Equal("WebGL 2 (Blazor WebAssembly) | Test GPU", harness.View.RendererDescription);
        Assert.Equal([harness.View.RendererDescription!], harness.Ready);
        Assert.Empty(harness.Errors);
    }

    [Fact]
    public async Task OutsideWebAssembly_ShowsWhy_InsteadOfAMarina()
    {
        using var module = WorkingWebGl();

        using var harness = await StartAsync(CreateMarina(), module, webAssembly: false);

        var error = Assert.Single(harness.Errors);
        Assert.Contains("WebAssembly", error, StringComparison.Ordinal);
        Assert.Contains(harness.Renderer.Elements(harness.View), e => e.Name == "div" && e.Text.Trim() == error);
        Assert.Empty(harness.Js.Calls);
        Assert.Null(harness.View.RendererDescription);
    }

    [Fact]
    public async Task WithoutWebGl2_ReportsIt_AndStartsNothing()
    {
        using var module = new FakeJsModule((name, _) => name == "createView" ? -1 : null);

        using var harness = await StartAsync(CreateMarina(), module);

        Assert.Contains("WebGL 2", Assert.Single(harness.Errors), StringComparison.Ordinal);
        Assert.Empty(harness.Module.CallsTo("initRenderer"));
        Assert.Empty(harness.Module.CallsTo("start"));
        Assert.Empty(harness.Ready);
    }

    [Fact]
    public async Task AShaderThatWillNotCompile_IsReported_NotThrown()
    {
        using var module = new FakeJsModule((name, _) => name switch
        {
            "createView" => ViewId,
            "initRenderer" => "link failed",
            _ => null,
        });

        using var harness = await StartAsync(CreateMarina(), module);

        Assert.Contains("link failed", Assert.Single(harness.Errors), StringComparison.Ordinal);
        Assert.Empty(harness.Module.CallsTo("start"));
    }

    [Fact]
    public async Task WithoutAMarina_TheViewRefusesToRender()
    {
        using var module = new FakeJsModule();
        using var renderer = new TestRenderer(new FakeJsRuntime(module));

        await Assert.ThrowsAsync<InvalidOperationException>(() => renderer.RenderAsync<MarinaView>(new Dictionary<string, object?>()));
    }

    [Fact]
    public async Task TheCanvas_IsAnApplicationWithAnAccessibleName()
    {
        using var module = WorkingWebGl();
        using var harness = await StartAsync(CreateMarina(), module);

        var canvas = Assert.Single(harness.Renderer.Elements(harness.View), e => e.Name == "canvas");
        Assert.Equal("application", canvas.Attribute("role"));
        Assert.False(string.IsNullOrWhiteSpace(canvas.Attribute("aria-label")));
        Assert.DoesNotContain("outline:none", canvas.Attribute("style") ?? "", StringComparison.Ordinal); // the focus ring shows
    }

    [Fact]
    public async Task TheCanvasName_DescribesTheDefaultMouseMapping()
    {
        using var module = WorkingWebGl();
        var marina = CreateMarina();
        using var harness = await StartAsync(marina, module);

        var label = Assert.Single(harness.Renderer.Elements(harness.View), e => e.Name == "canvas").Attribute("aria-label") ?? "";
        Assert.Equal(CameraDragAction.Pan, marina.Input.LeftDragAction);
        Assert.Equal(CameraDragAction.Orbit, marina.Input.RightDragAction);
        Assert.Contains("Drag to pan", label, StringComparison.Ordinal);
        Assert.Contains("right-drag or Shift+drag to orbit", label, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMarinaStyleParameter_IsAppliedToTheMarina()
    {
        var marina = CreateMarina();
        var style = new MarinaStyle();

        using var module = new FakeJsModule();
        using var harness = await StartAsync(marina, module, webAssembly: false, style);

        Assert.Same(style, marina.Style);
    }

    // ---- The frame loop ------------------------------------------------------------------------------

    [Fact]
    public async Task EachAnimationFrame_AdvancesTheMarina_AndDrawsIt_WhileTheWaterMoves()
    {
        var marina = CreateMarina();
        using var module = WorkingWebGl();
        using var harness = await StartAsync(marina, module);

        Assert.True(harness.View.OnAnimationFrame(1000d, 800d, 600d));
        Assert.True(harness.View.OnAnimationFrame(1016d, 800d, 600d));

        Assert.Equal([0f, 0f, 0f], LastAnchor(harness.Module)); // no popup open
        Assert.Equal(new Vector2(800f, 600f), marina.ViewportSize);
        Assert.Equal(2, harness.Module.CallsTo("renderFrame").Length);
        Assert.NotEmpty(harness.Module.CallsTo("uploadMesh"));
        Assert.True(marina.Time > 0d, "the waves did not move on");
    }

    [Fact]
    public async Task AStillMarina_IsDrawnOnce_ThenTheLoopSleeps_UntilSomethingChanges()
    {
        var marina = CreateMarina(calm: true);
        using var module = WorkingWebGl();
        using var harness = await StartAsync(marina, module);

        Assert.True(harness.View.OnAnimationFrame(1000d, 800d, 600d));
        Assert.False(harness.View.OnAnimationFrame(1016d, 800d, 600d)); // nothing changed: nothing drawn, the loop sleeps
        Assert.Single(harness.Module.CallsTo("renderFrame"));
        Assert.False(harness.View.NeedsFrame());

        // A status arriving from outside wakes the script at once, and the next frame draws it.
        harness.Module.Calls.Clear();
        marina.ReserveBerth("A-L01");
        Assert.Equal(ViewId, Assert.Single(harness.Module.CallsTo("wakeView")).Args[0]);
        Assert.True(harness.View.NeedsFrame());
        Assert.True(harness.View.OnAnimationFrame(2000d, 800d, 600d));
        Assert.Single(harness.Module.CallsTo("renderFrame"));

        // The camera moved by code, which the visualizer does not announce, is found by the script's slow poll.
        Assert.False(harness.View.OnAnimationFrame(2016d, 800d, 600d));
        marina.Camera.Orbit(20f, 0f);
        Assert.True(harness.View.NeedsFrame());
    }

    [Fact]
    public async Task ContinuousRendering_DrawsEveryFrame_EvenWhenNothingChanges()
    {
        using var module = WorkingWebGl();
        using var harness = await StartAsync(CreateMarina(calm: true), module, continuous: true);

        for (var i = 0; i < 4; i++) Assert.True(harness.View.OnAnimationFrame(1000d + i * 16d, 800d, 600d));

        Assert.Equal(4, harness.Module.CallsTo("renderFrame").Length);
    }

    [Fact]
    public async Task AFrameThatKeepsFailing_StopsTheLoop_AndIsReported()
    {
        using var module = new FakeJsModule((name, _) => name switch
        {
            "createView" => ViewId,
            "renderFrame" => throw new JSException("GL_INVALID_OPERATION"),
            _ => null,
        });
        using var harness = await StartAsync(CreateMarina(), module);

        Assert.True(harness.View.OnAnimationFrame(1000d, 800d, 600d));   // one bad frame is retried
        Assert.True(harness.View.OnAnimationFrame(1016d, 800d, 600d));
        Assert.False(harness.View.OnAnimationFrame(1032d, 800d, 600d));  // the third in a row stops the loop
        await harness.Renderer.InvokeAsync(() => { });

        var error = Assert.Single(harness.Errors);
        Assert.Contains("GL_INVALID_OPERATION", error, StringComparison.Ordinal);
        Assert.Contains(harness.Renderer.Elements(harness.View), e => e.HasClass("vm-marina-view__error") && e.Text.Contains("GL_INVALID_OPERATION", StringComparison.Ordinal));
        Assert.False(harness.View.OnAnimationFrame(1048d, 800d, 600d));
        Assert.False(harness.View.NeedsFrame());
    }

    [Fact]
    public async Task ALoopTheScriptGaveUpOn_IsReported()
    {
        using var module = WorkingWebGl();
        using var harness = await StartAsync(CreateMarina(), module);

        await harness.View.OnRenderLoopFailed("context gone");

        Assert.Contains("context gone", Assert.Single(harness.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BeforeWebGlIsRunning_AnimationFramesDrawNothing()
    {
        using var module = WorkingWebGl();
        using var harness = await StartAsync(CreateMarina(), module, webAssembly: false);

        Assert.False(harness.View.OnAnimationFrame(1000d, 800d, 600d));
        Assert.Empty(harness.Module.Calls);
    }

    // ---- The popup -----------------------------------------------------------------------------------

    [Fact]
    public async Task AnOpenTooltip_IsRenderedAndAnchored()
    {
        var marina = CreateMarina();
        using var module = WorkingWebGl();
        using var harness = await StartAsync(marina, module);

        await harness.Renderer.InvokeAsync(() =>
        {
            marina.SelectBerth("A-L02");
            marina.ShowTooltip();
        });

        var elements = harness.Renderer.Elements(harness.View);
        Assert.Contains(elements, e => e.HasClass("vm-popup--tooltip"));
        Assert.Equal("A-L02", Assert.Single(elements, e => e.HasClass("vm-popup__title")).Text);
        var popup = Assert.Single(elements, e => e.HasClass("vm-popup"));
        Assert.Equal("status", popup.Attribute("role"));
        Assert.Equal("polite", popup.Attribute("aria-live"));
        Assert.Equal("A-L02", popup.Attribute("aria-label"));
        Assert.Equal("Close (Esc)", Assert.Single(elements, e => e.HasClass("vm-popup__close")).Attribute("title"));

        Assert.True(harness.View.OnAnimationFrame(1000d, 800d, 600d));
        Assert.Equal(1f, LastAnchor(harness.Module)[0]);

        await harness.Renderer.ClickAsync(Assert.Single(harness.Renderer.Elements(harness.View), e => e.HasClass("vm-popup__close")));

        Assert.Null(marina.ActivePopup);
        Assert.DoesNotContain(harness.Renderer.Elements(harness.View), e => e.HasClass("vm-popup__title"));
    }

    [Fact]
    public async Task TheActionsWindow_InvokesTheActionThatWasClicked()
    {
        var marina = CreateMarina();
        marina.BerthSelected += (_, e) =>
        {
            e.Actions.Add("checkin", "Check in");
            e.Actions.Add("later", "Not now", enabled: false);
        };
        string? invoked = null;
        marina.BerthActionInvoked += (_, e) => invoked = e.ActionId;
        using var module = WorkingWebGl();
        using var harness = await StartAsync(marina, module);

        await harness.Renderer.InvokeAsync(() =>
        {
            marina.SelectBerth("A-L01");
            marina.ShowActions();
        });

        var actions = harness.Renderer.Elements(harness.View).Where(e => e.HasClass("vm-popup__action")).ToList();
        Assert.Equal(["Check in", "Not now"], actions.Select(a => a.Text.Trim()));
        Assert.True(actions[1].Attributes["disabled"] is true);

        await harness.Renderer.ClickAsync(actions[0]);

        Assert.Equal("checkin", invoked);
    }

    // ---- Calls from marinaWebGL.js -------------------------------------------------------------------

    [Theory]
    [InlineData("KeyQ", "q", 0, false)]
    [InlineData("ArrowLeft", "ArrowLeft", 0, true)]
    [InlineData("KeyD", "D", 1, true)]            // Shift+D orbits
    [InlineData("Equal", "+", 1, true)]
    [InlineData("Home", "Home", 0, false)]        // already at the overview: nothing to do, so not handled
    [InlineData("KeyS", "s", 2, false)]           // Ctrl+S is the page's Save, not a pan
    [InlineData("Equal", "=", 2, false)]          // Ctrl+= is the browser's zoom
    [InlineData("KeyW", "w", 4, false)]           // Alt+W
    [InlineData("Escape", "Escape", 0, false)]    // nothing to dismiss
    public async Task Keys_AreMappedToMarinaKeys(string code, string key, int modifiers, bool handled)
    {
        var marina = CreateMarina();
        marina.ResetCamera(immediate: true);
        using var module = new FakeJsModule();
        using var harness = await StartAsync(marina, module, webAssembly: false);

        Assert.Equal(handled, harness.View.OnKeyDown(code, key, modifiers));
    }

    [Fact]
    public async Task TheMovementKeys_FollowThePhysicalKey_NotTheLetterItTypes()
    {
        var marina = CreateMarina();
        using var module = new FakeJsModule();
        using var harness = await StartAsync(marina, module, webAssembly: false);
        var before = marina.Camera.DesiredPose;

        // The key where W sits on a QWERTY keyboard types z on an AZERTY one: it still pans forward.
        Assert.True(harness.View.OnKeyDown("KeyW", "z", 0));
        Assert.NotEqual(before.Target, marina.Camera.DesiredPose.Target);
    }

    [Fact]
    public async Task CtrlZ_UndoesTheLastDesignStep()
    {
        var marina = CreateMarina();
        marina.Designer.IsActive = true;
        marina.Designer.CreateBerths("A", PierSide.Right, 2f, 2f);
        var berths = marina.GetBerths().Count;
        using var module = new FakeJsModule();
        using var harness = await StartAsync(marina, module, webAssembly: false);

        Assert.False(harness.View.OnKeyDown("KeyZ", "z", 0)); // no Control: not a key the marina uses
        Assert.False(harness.View.OnKeyDown("KeyZ", "Z", (int)(Core.Input.InputModifiers.Control | Core.Input.InputModifiers.Shift))); // Redo, elsewhere
        Assert.True(harness.View.OnKeyDown("KeyZ", "z", (int)Core.Input.InputModifiers.Control));

        Assert.Equal(berths - 1, marina.GetBerths().Count);
    }

    [Fact]
    public async Task ThePointerCursor_FollowsTheDesignerTool()
    {
        var marina = CreateMarina();
        using var module = new FakeJsModule();
        using var harness = await StartAsync(marina, module, webAssembly: false);

        Assert.Equal("grab", harness.View.OnPointerMove(10, 10, 0));

        marina.Designer.IsActive = true;
        marina.Designer.Tool = DesignTool.AddBerths;
        Assert.Equal("crosshair", harness.View.OnPointerMove(10, 10, 0));

        marina.Designer.Tool = DesignTool.MoveReferenceImage;
        Assert.Equal("not-allowed", harness.View.OnPointerMove(10, 10, 0));

        marina.Designer.SetReferenceImage(ReferenceImage.FromEncoded([1, 2, 3], 100, 50), 0.5f);
        Assert.Equal("move", harness.View.OnPointerMove(10, 10, 0));

        marina.Designer.Tool = DesignTool.Navigate;
        Assert.Equal("grab", harness.View.OnPointerMove(10, 10, 0));
    }

    // ---- Reference images ----------------------------------------------------------------------------

    [Fact]
    public async Task AReferenceImage_IsDecodedOnceByTheBrowser_AndGivenToTheDesigner()
    {
        var marina = CreateMarina();
        using var module = new FakeJsModule((name, _) => name switch
        {
            "createView" => ViewId,
            "decodeImage" => new[] { 640d, 480d, 5d },
            _ => null,
        });
        using var harness = await StartAsync(marina, module);

        var image = await harness.View.LoadReferenceImageAsync([1, 2, 3], "image/jpeg", 0.25f);

        Assert.NotNull(image);
        Assert.Same(image, marina.Designer.ReferenceImage);
        Assert.Equal((640, 480), (image.PixelWidth, image.PixelHeight));
        Assert.Equal("image/jpeg", image.ContentType);
        Assert.Equal(0.25f, marina.Designer.ReferenceImageMetersPerPixel);

        // The picture the browser decoded to measure becomes the texture: the bytes are not sent or decoded again.
        var adopt = Assert.Single(harness.Module.CallsTo("adoptImage"));
        Assert.Equal([ViewId, 5, image.Key], adopt.Args);
        harness.View.OnAnimationFrame(1000d, 800d, 600d);
        Assert.Empty(harness.Module.CallsTo("setReferenceImageEncoded"));
    }

    [Fact]
    public async Task AnImageTheBrowserCannotDecode_IsRefused()
    {
        var marina = CreateMarina();
        using var module = WorkingWebGl(); // decodeImage answers null
        using var harness = await StartAsync(marina, module);

        Assert.Null(await harness.View.LoadReferenceImageAsync([1, 2, 3], "image/png"));
        Assert.Null(await harness.View.LoadReferenceImageAsync([], "image/png"));
        Assert.Null(marina.Designer.ReferenceImage);
    }

    [Fact]
    public async Task BeforeTheModuleIsLoaded_NoImageCanBeLoaded()
    {
        using var module = WorkingWebGl();
        using var harness = await StartAsync(CreateMarina(), module, webAssembly: false);

        Assert.Null(await harness.View.LoadReferenceImageAsync([1, 2, 3], "image/png"));
    }

    // ---- Disposal ------------------------------------------------------------------------------------

    [Fact]
    public async Task Disposing_DestroysTheWebGlView_AndStopsFollowingTheMarina()
    {
        var marina = CreateMarina();
        using var module = WorkingWebGl();
        using var harness = await StartAsync(marina, module);

        await harness.View.DisposeAsync();

        Assert.Equal(ViewId, Assert.Single(harness.Module.CallsTo("destroyView")).Args[0]);
        Assert.True(harness.Module.IsDisposed);

        var renders = harness.Renderer.Renders;
        await harness.Renderer.InvokeAsync(() =>
        {
            marina.SelectBerth("A-L01");
            marina.ShowTooltip();
        });
        Assert.Equal(renders, harness.Renderer.Renders);
    }
}
