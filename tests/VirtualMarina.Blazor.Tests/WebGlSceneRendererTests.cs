using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using VirtualMarina.Blazor;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Blazor.Tests;

/// <summary>
/// The WebGL renderer's half of the bargain with marinaWebGL.js: what crosses the interop boundary, in which layout,
/// and, as much as what is sent, what is not sent again. Every call is a copy in WebAssembly, so a mesh or a layer
/// sent twice is a frame-rate problem nobody would see in a unit test of the scene itself.
/// </summary>
public class WebGlSceneRendererTests
{
    private const int ViewId = 7;
    private const int FrameLength = 82;
    private const int ImageSlot = 70;
    private const int AnchorSlot = 79;

    private static WebGlSceneRenderer Create(FakeJsModule module) =>
        // The constructor is internal: MarinaView creates the renderer once the browser has made the WebGL context.
        (WebGlSceneRenderer)Activator.CreateInstance(
            typeof(WebGlSceneRenderer), BindingFlags.Instance | BindingFlags.NonPublic, binder: null, args: [module, ViewId], culture: null)!;

    private static MeshData Mesh(int id) => new(id, $"mesh {id}", new float[MeshData.VertexStride * 3], [0u, 1u, 2u]);

    private static MeshLibrary Library(params int[] ids)
    {
        var library = new MeshLibrary();
        foreach (var id in ids) library.Register(Mesh(id));
        return library;
    }

    private static RenderFrame Frame(
        MeshLibrary meshes, int sceneVersion = 1, IReadOnlyList<RenderObject>? objects = null, ReferenceImageLayer? image = null, WaterSettings? water = null,
        float waterDetailRadius = 700f) => new()
        {
            View = Matrix4x4.CreateTranslation(1f, 2f, 3f),
            Projection = Matrix4x4.CreateScale(4f),
            CameraPosition = new Vector3(5f, 6f, 7f),
            Time = 8.5f,
            Lighting = new LightingSettings(),
            Water = water ?? new WaterSettings(),
            Objects = objects ?? [],
            SceneVersion = sceneVersion,
            Meshes = meshes,
            WaterCenter = new Vector2(9f, 10f),
            WaterDetailRadius = waterDetailRadius,
            ReferenceImage = image,
        };

    /// <summary>Calls one of the renderer's internal methods, which MarinaView uses and the script's contract depends on.</summary>
    private static void Call(WebGlSceneRenderer renderer, string method, params object?[] args) =>
        typeof(WebGlSceneRenderer).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(renderer, args);

    private static float[] Floats(object? bytes) => MemoryMarshal.Cast<byte, float>(Assert.IsType<byte[]>(bytes)).ToArray();

    private static int[] Ints(object? bytes) => MemoryMarshal.Cast<byte, int>(Assert.IsType<byte[]>(bytes)).ToArray();

    /// <summary>The per-frame buffer sent with the last renderFrame.</summary>
    private static float[] LastFrame(FakeJsModule module) => Floats(module.CallsTo("renderFrame")[^1].Args[1]);

    private static MarinaVisualizer Marina(bool traffic = false)
    {
        var layout = new MarinaLayoutBuilder("Test")
            .AddPier("A", "Pier A", Vector2.Zero, 0f, 60f, pier => pier.AddBerths(PierSide.Left, 5, 5f, 12f))
            .Build();
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(layout);
        if (traffic) marina.SetMarineTraffic(MarineTraffic.None with { IsEnabled = true, Clearance = 300f, Seed = 3 });
        return marina;
    }

    // ---- Start-up ------------------------------------------------------------------------------------

    [Fact]
    public void Initialize_CompilesTheInstancedWebGl2Shaders_AndReadsTheDeviceDescription()
    {
        using var module = new FakeJsModule((name, _) => name == "getDeviceDescription" ? "WebGL 2.0 | Test GPU" : null);
        var renderer = Create(module);

        renderer.Initialize();

        var init = Assert.Single(module.CallsTo("initRenderer"));
        Assert.Equal(ViewId, init.Args[0]);
        var shaders = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(init.Args[1]);
        Assert.Equal(ShaderSources.InstancedModelVertex(ShaderDialect.WebGL2), shaders["modelVertex"]);
        Assert.Equal(ShaderSources.InstancedModelFragment(ShaderDialect.WebGL2), shaders["modelFragment"]);
        Assert.Equal(ShaderSources.WaterVertex(ShaderDialect.WebGL2), shaders["waterVertex"]);
        Assert.Equal(ShaderSources.WaterFragment(ShaderDialect.WebGL2), shaders["waterFragment"]);
        Assert.Equal(ShaderSources.ImageVertex(ShaderDialect.WebGL2), shaders["imageVertex"]);
        Assert.Equal(ShaderSources.ImageFragment(ShaderDialect.WebGL2), shaders["imageFragment"]);
        Assert.Equal(ShaderSources.ImageQuadCorners, Assert.IsType<float[]>(init.Args[2]));
        Assert.Equal("WebGL 2.0 | Test GPU", renderer.DeviceDescription);
        Assert.Equal("WebGL 2 (Blazor WebAssembly)", renderer.BackendName);
    }

    [Fact]
    public void Initialize_ReportsAShaderTheBrowserCouldNotCompile()
    {
        using var module = new FakeJsModule((name, _) => name == "initRenderer" ? "ERROR: 0:12: 'vPos' undeclared" : null);

        var error = Assert.Throws<InvalidOperationException>(() => Create(module).Initialize());

        Assert.Contains("'vPos' undeclared", error.Message, StringComparison.Ordinal);
        Assert.Empty(module.CallsTo("getDeviceDescription"));
    }

    // ---- Meshes --------------------------------------------------------------------------------------

    [Fact]
    public void Meshes_AreUploadedOnceAsRawBytes_ThenOnlyWhenTheLibraryChanges()
    {
        using var module = new FakeJsModule();
        var renderer = Create(module);
        var library = Library(1, 2, 3);

        renderer.Render(Frame(library));
        renderer.Render(Frame(library));

        Assert.Equal([1, 2, 3], module.CallsTo("uploadMesh").Select(c => (int)c.Args[1]!).Order());

        // Bytes, not Base64 text: Blazor hands a byte[] to the script as a Uint8Array.
        var upload = module.CallsTo("uploadMesh").First(c => (int)c.Args[1]! == 1);
        Assert.Equal(ViewId, upload.Args[0]);
        Assert.Equal(MeshData.VertexStride * 3, Floats(upload.Args[2]).Length);
        Assert.Equal([0, 1, 2], Ints(upload.Args[3]));
        Assert.False((bool)upload.Args[4]!); // not the water grid
    }

    [Fact]
    public void AChangedLibrary_SendsOnlyTheReplacedMesh_AndDeletesTheRemovedOne()
    {
        using var module = new FakeJsModule();
        var renderer = Create(module);
        var library = Library(1, 2, 3);
        renderer.Render(Frame(library));
        module.Calls.Clear();

        library.Register(Mesh(2)); // replaced: a new object under the same id
        library.Unregister(3);
        renderer.Render(Frame(library));

        var upload = Assert.Single(module.CallsTo("uploadMesh"));
        Assert.Equal(2, upload.Args[1]);
        var delete = Assert.Single(module.CallsTo("deleteMesh"));
        Assert.Equal(3, delete.Args[1]);
    }

    // ---- Layers --------------------------------------------------------------------------------------

    [Fact]
    public void AFlatFrame_IsSentAsOneSceneLayer_OnlyWhenItsVersionChanges()
    {
        using var module = new FakeJsModule();
        var renderer = Create(module);
        var library = Library(1);
        RenderObject[] one = [new(1, Matrix4x4.Identity, Vector4.One)];
        RenderObject[] two = [new(1, Matrix4x4.Identity, Vector4.One), new(1, Matrix4x4.Identity, Vector4.One)];

        renderer.Render(Frame(library, sceneVersion: 1, objects: one));
        renderer.Render(Frame(library, sceneVersion: 1, objects: one));
        renderer.Render(Frame(library, sceneVersion: 2, objects: two));

        var sent = module.CallsTo("setLayer");
        Assert.Equal(2, sent.Length);
        Assert.All(sent, call => Assert.Equal((int)RenderLayerKind.Scene, call.Args[1]));
        Assert.Equal(1 * InstanceData.Stride, Floats(sent[0].Args[2]).Length);
        Assert.Equal(2 * InstanceData.Stride, Floats(sent[1].Args[2]).Length);
        Assert.Equal(3, module.CallsTo("renderFrame").Length);
    }

    [Fact]
    public void Instances_ArePackedInTheLayoutTheShaderReads_WithTheirBatches()
    {
        using var module = new FakeJsModule();
        var renderer = Create(module);
        var world = Matrix4x4.CreateScale(2f) * Matrix4x4.CreateTranslation(10f, 20f, 30f);
        RenderObject[] objects =
        [
            new(4, world, new Vector4(0.1f, 0.2f, 0.3f, 0.4f), Emissive: 0.5f, Animation: RenderAnimation.FloatOnWater | RenderAnimation.Pulse, Phase: 1.25f, Desaturation: 0.75f),
        ];

        renderer.Render(Frame(Library(4), objects: objects));

        var call = Assert.Single(module.CallsTo("setLayer"));
        var packed = Floats(call.Args[2]);
        Assert.Equal(InstanceData.Stride, packed.Length);
        Assert.Equal([2f, 0f, 0f, 10f], packed[0..4]);   // first column of the model matrix, then the translation's x
        Assert.Equal([0f, 2f, 0f, 20f], packed[4..8]);
        Assert.Equal([0f, 0f, 2f, 30f], packed[8..12]);
        Assert.Equal([0.1f, 0.2f, 0.3f, 0.4f], packed[12..16]);
        Assert.Equal([0.5f, 5f, 1.25f, 0.75f], packed[16..20]); // emissive, FloatOnWater | Pulse as flag bits, phase, desaturation

        // One transparent batch of mesh 4: [mesh, pass, start, count].
        Assert.Equal([4, (int)RenderPass.Transparent, 0, 1], Ints(call.Args[3]));
    }

    [Fact]
    public void AMarina_IsSentLayerByLayer_AndASecondFrameSendsNothing()
    {
        using var module = new FakeJsModule();
        var renderer = Create(module);
        var marina = Marina();

        renderer.Render(marina.BuildRenderFrame());
        var kinds = module.CallsTo("setLayer").Select(c => (RenderLayerKind)(int)c.Args[1]!).ToList();
        Assert.Contains(RenderLayerKind.Structure, kinds);
        Assert.Contains(RenderLayerKind.Berths, kinds);

        module.Calls.Clear();
        renderer.Render(marina.BuildRenderFrame());

        Assert.Empty(module.CallsTo("setLayer"));
        Assert.Empty(module.CallsTo("patchLayer"));
        Assert.Empty(module.CallsTo("uploadMesh"));
        Assert.Single(module.CallsTo("renderFrame"));
    }

    [Fact]
    public void Hovering_PatchesAFewBerthInstances_AndNeverResendsThePiers()
    {
        using var module = new FakeJsModule();
        var renderer = Create(module);
        var marina = Marina();
        marina.BerthLabelMode = BerthLabelMode.All;
        renderer.Render(marina.BuildRenderFrame());
        module.Calls.Clear();

        var berth = marina.GetBerths()[2];
        var screen = marina.TryProjectToScreen(new Vector3(berth.Center.X, 0f, berth.Center.Y), out var point) ? point : default;
        marina.Input.PointerMove(screen.X, screen.Y, Core.Input.InputModifiers.None);
        Assert.Equal(berth.Id, marina.HoveredBerth?.Id);
        renderer.Render(marina.BuildRenderFrame());

        // The hovered berth's pad, boat and label are rewritten where they are; nothing else crosses.
        Assert.DoesNotContain(module.CallsTo("setLayer"), c => (int)c.Args[1]! != (int)RenderLayerKind.Highlight);
        var patch = Assert.Single(module.CallsTo("patchLayer"));
        Assert.Equal((int)RenderLayerKind.Berths, patch.Args[1]);
        var ranges = Ints(patch.Args[2]);
        var instances = Floats(patch.Args[3]).Length / InstanceData.Stride;
        Assert.Equal(instances, Enumerable.Range(0, ranges.Length / 2).Sum(i => ranges[(i * 2) + 1]));
        Assert.InRange(instances, 1, 20);
    }

    [Fact]
    public void Traffic_ResendsOnlyTheTrafficLayer()
    {
        using var module = new FakeJsModule();
        var renderer = Create(module);
        var marina = Marina(traffic: true);
        marina.Update(1d);
        renderer.Render(marina.BuildRenderFrame());
        module.Calls.Clear();

        marina.Update(0.2d);
        renderer.Render(marina.BuildRenderFrame());

        var kinds = module.CallsTo("setLayer").Concat(module.CallsTo("patchLayer")).Select(c => (RenderLayerKind)(int)c.Args[1]!).Distinct().ToList();
        Assert.Equal([RenderLayerKind.Traffic], kinds);
    }

    [Fact]
    public void ALayerTheFrameNoLongerHas_IsDeleted()
    {
        using var module = new FakeJsModule();
        var renderer = Create(module);
        renderer.Render(Frame(Library(1), objects: [new(1, Matrix4x4.Identity, Vector4.One)]));
        module.Calls.Clear();

        renderer.Render(Marina().BuildRenderFrame());

        Assert.Equal((int)RenderLayerKind.Scene, Assert.Single(module.CallsTo("deleteLayer")).Args[1]);
    }

    // ---- Per-frame buffer ----------------------------------------------------------------------------

    [Fact]
    public void EveryFrame_SendsTheCameraLightAndWaterUniforms_InTheirSlots_AsOneReusedBuffer()
    {
        using var module = new FakeJsModule();
        var renderer = Create(module);
        var water = new WaterSettings { SkyReflection = 5f, Ripples = -1f, SunGlints = 1.5f, BoatMotion = 9f };

        renderer.Render(Frame(Library(1), water: water));
        renderer.Render(Frame(Library(1)));

        var calls = module.CallsTo("renderFrame");
        Assert.Equal(2, calls.Length);
        var uniforms = Floats(calls[0].Args[1]);
        Assert.Equal(FrameLength, uniforms.Length);
        Assert.Equal([1f, 2f, 3f], uniforms[12..15]); // view translation
        Assert.Equal(4f, uniforms[16]); // projection scale
        Assert.Equal([5f, 6f, 7f, 8.5f], uniforms[32..36]); // camera position, time
        Assert.Equal([1f, 0f, 1.5f, 3f], uniforms[63..67]); // reflection, ripples, glints, boat motion: clamped to what the shader handles
        Assert.Equal([9f, 10f, 700f], uniforms[67..70]); // water centre and detail radius
    }

    [Fact]
    public void ATinyWaterDetailRadius_IsSentAsAtLeastOneMetre()
    {
        using var module = new FakeJsModule();
        var renderer = Create(module);

        renderer.Render(Frame(Library(1), waterDetailRadius: 0.01f));

        Assert.Equal(1f, LastFrame(module)[69]);
    }

    [Fact]
    public void ThePopupAnchor_RidesInTheFrame()
    {
        using var module = new FakeJsModule();
        var renderer = Create(module);

        renderer.Render(Frame(Library(1)));
        Assert.Equal([0f, 0f, 0f], LastFrame(module)[AnchorSlot..]);

        Call(renderer, "SetPopupAnchor", new Vector2(120f, 45f));
        renderer.Render(Frame(Library(1)));
        Assert.Equal([1f, 120f, 45f], LastFrame(module)[AnchorSlot..]);
    }

    // ---- Reference image -----------------------------------------------------------------------------

    [Fact]
    public void AReferenceImage_IsUploadedOncePerImage_AndPlacedEveryFrame()
    {
        using var module = new FakeJsModule();
        var renderer = Create(module);
        var library = Library(1);
        var image = ReferenceImage.FromEncoded([1, 2, 3], 100, 50, "image/webp");
        var layer = new ReferenceImageLayer(image, new Vector2(-40f, -25f), new Vector2(60f, 25f), 0.2f, 0.6f, AboveScene: true);

        renderer.Render(Frame(library, image: layer));
        renderer.Render(Frame(library, image: layer with { Opacity = 0.3f }));

        var upload = Assert.Single(module.CallsTo("setReferenceImageEncoded"));
        Assert.Equal(image.Key, upload.Args[1]);
        Assert.Equal("image/webp", upload.Args[3]);

        var placement = LastFrame(module);
        Assert.Equal(1f, placement[ImageSlot]);
        Assert.Equal(image.Key, MemoryMarshal.Cast<float, int>(placement.AsSpan())[ImageSlot + 1]);
        Assert.Equal([-40f, -25f, 60f, 25f], placement[(ImageSlot + 2)..(ImageSlot + 6)]);
        Assert.Equal(0.3f, placement[ImageSlot + 7]);
        Assert.Equal(1f, placement[ImageSlot + 8]);

        var replacement = ReferenceImage.FromEncoded([4, 5, 6], 10, 10);
        renderer.Render(Frame(library, image: layer with { Image = replacement }));
        Assert.Equal(2, module.CallsTo("setReferenceImageEncoded").Length);
    }

    [Fact]
    public void AnImageHiddenAndShownAgain_IsSentAgain_SinceTheScriptFreedItsTexture()
    {
        using var module = new FakeJsModule();
        var renderer = Create(module);
        var image = ReferenceImage.FromEncoded([1, 2, 3], 100, 50);
        var layer = new ReferenceImageLayer(image, Vector2.Zero, Vector2.One, 0.2f, 1f, AboveScene: false);

        renderer.Render(Frame(Library(1), image: layer));
        renderer.Render(Frame(Library(1)));
        renderer.Render(Frame(Library(1), image: layer));

        Assert.Equal(2, module.CallsTo("setReferenceImageEncoded").Length);
    }

    [Fact]
    public void AnImageTheScriptAlreadyDecoded_IsNotSentAgain()
    {
        using var module = new FakeJsModule();
        var renderer = Create(module);
        var image = ReferenceImage.FromEncoded([1, 2, 3], 100, 50);

        Call(renderer, "ReferenceImageUploaded", image.Key);
        renderer.Render(Frame(Library(1), image: new ReferenceImageLayer(image, Vector2.Zero, Vector2.One, 0.2f, 1f, AboveScene: false)));

        Assert.Empty(module.CallsTo("setReferenceImageEncoded"));
    }

    [Fact]
    public void NoReferenceImage_SendsNoPlacement()
    {
        using var module = new FakeJsModule();

        Create(module).Render(Frame(Library(1)));

        Assert.Equal(0f, LastFrame(module)[ImageSlot]);
        Assert.Empty(module.CallsTo("setReferenceImageEncoded"));
        Assert.Empty(module.CallsTo("setReferenceImageRgba"));
    }

    // ---- Context loss --------------------------------------------------------------------------------

    // ---- Transparency --------------------------------------------------------------------------------

    private static RenderFrame At(MeshLibrary meshes, Vector3 camera, int sceneVersion, IReadOnlyList<RenderObject> objects) => new()
    {
        View = Matrix4x4.Identity,
        Projection = Matrix4x4.Identity,
        CameraPosition = camera,
        Time = 0f,
        Lighting = new LightingSettings(),
        Water = new WaterSettings(),
        Objects = objects,
        SceneVersion = sceneVersion,
        Meshes = meshes,
    };

    private static RenderObject Glass(int mesh, float x) => new(mesh, Matrix4x4.CreateTranslation(x, 0f, 0f), new Vector4(1f, 1f, 1f, 0.5f));

    /// <summary>The x of each instance sent with the last setTransparent, in drawing order.</summary>
    private static float[] SentOrder(FakeJsModule module)
    {
        var packed = Floats(module.CallsTo("setTransparent")[^1].Args[1]);
        return Enumerable.Range(0, packed.Length / InstanceData.Stride).Select(i => packed[(i * InstanceData.Stride) + 3]).ToArray();
    }

    [Fact]
    public void TransparentInstances_AreSentFarthestFirst_WithTheirRunsOfOneMesh()
    {
        using var module = new FakeJsModule();
        var renderer = Create(module);
        RenderObject[] objects = [Glass(1, 10f), Glass(2, 30f), Glass(2, 20f), new(1, Matrix4x4.Identity, Vector4.One)];

        renderer.Render(At(Library(1, 2), Vector3.Zero, 1, objects));

        var call = Assert.Single(module.CallsTo("setTransparent"));
        Assert.Equal(ViewId, call.Args[0]);
        Assert.Equal([30f, 20f, 10f], SentOrder(module)); // the opaque one is not among them
        Assert.Equal([2, 0, 2, 1, 2, 1], Ints(call.Args[2])); // [mesh, start, count]: the two of mesh 2, then mesh 1
        Assert.True(module.Calls.FindIndex(c => c.Identifier == "setTransparent") < module.Calls.FindIndex(c => c.Identifier == "renderFrame"));
    }

    [Fact]
    public void TheOrder_IsSentAgainOnlyWhenTheCameraOrSceneChangesIt()
    {
        using var module = new FakeJsModule();
        var renderer = Create(module);
        var library = Library(1);
        RenderObject[] objects = [Glass(1, -10f), Glass(1, 10f)];

        renderer.Render(At(library, new Vector3(-100f, 0f, 0f), 1, objects));
        Assert.Equal([10f, -10f], SentOrder(module));

        renderer.Render(At(library, new Vector3(-100f, 0f, 0f), 1, objects)); // nothing moved
        renderer.Render(At(library, new Vector3(-90f, 5f, 0f), 1, objects));  // moved, same order
        Assert.Single(module.CallsTo("setTransparent"));

        renderer.Render(At(library, new Vector3(100f, 0f, 0f), 1, objects));   // round the other side
        Assert.Equal(2, module.CallsTo("setTransparent").Length);
        Assert.Equal([-10f, 10f], SentOrder(module));

        renderer.Render(At(library, new Vector3(100f, 0f, 0f), 2, [Glass(1, -10f)])); // one fewer
        Assert.Equal([-10f], SentOrder(module));
    }

    [Fact]
    public void AMarinasTransparentInstances_AreSentOnce_ForAStillCamera()
    {
        using var module = new FakeJsModule();
        var renderer = Create(module);
        var marina = Marina();
        marina.ReserveBerth(marina.GetBerths()[0].Id, new Boat("ghost", "Ghost", BoatType.MonohullSailboat));

        renderer.Render(marina.BuildRenderFrame());
        Assert.NotEmpty(SentOrder(module));
        renderer.Render(marina.BuildRenderFrame());

        Assert.Single(module.CallsTo("setTransparent"));
    }

    [Fact]
    public void AfterTheContextComesBack_TheTransparentOrderIsSentAgain()
    {
        var frames = 0;
        using var module = new FakeJsModule((name, _) => name == "renderFrame" && ++frames == 1);
        var renderer = Create(module);
        var library = Library(1);
        RenderObject[] objects = [Glass(1, -10f), Glass(1, 10f)];

        renderer.Render(At(library, Vector3.Zero, 1, objects));
        renderer.Render(At(library, Vector3.Zero, 1, objects));

        Assert.Equal(2, module.CallsTo("setTransparent").Length);
    }

    [Fact]
    public void AfterTheContextComesBack_EverythingIsSentAgain()
    {
        var frames = 0;
        using var module = new FakeJsModule((name, _) => name == "renderFrame" && ++frames == 1);
        var renderer = Create(module);
        var marina = Marina();

        renderer.Render(marina.BuildRenderFrame());
        var layers = module.CallsTo("setLayer").Length;
        module.Calls.Clear();
        renderer.Render(marina.BuildRenderFrame());

        Assert.Equal(layers, module.CallsTo("setLayer").Length);
        Assert.NotEmpty(module.CallsTo("uploadMesh"));
    }

    // ---- Misuse --------------------------------------------------------------------------------------

    [Fact]
    public void Render_RefusesANullFrame_AndAnyFrameAfterDispose()
    {
        using var module = new FakeJsModule();
        var renderer = Create(module);

        Assert.Throws<ArgumentNullException>(() => renderer.Render(null!));

        renderer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => renderer.Render(Frame(Library(1))));
        Assert.Empty(module.Calls);
    }

    [Fact]
    public void Resize_IsLeftToTheBrowser()
    {
        using var module = new FakeJsModule();

        Create(module).Resize(1920, 1080);

        Assert.Empty(module.Calls);
    }
}
