using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Input;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// The scene in layers: what each kind of change rebuilds, what it leaves alone, and that grouping the instances into
/// batches for instanced drawing keeps every object the flat list had.
/// </summary>
public class LayeredSceneTests
{
    private static MarinaVisualizer CreateMarina()
    {
        var layout = new MarinaLayoutBuilder("Layers")
            .AddPier("A", "Pier A", Vector2.Zero, 0f, 80f, pier => pier.AddBerths(PierSide.Left, 6, 5f, 12f).AddBerths(PierSide.Right, 6, 5f, 12f))
            .Build();
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(layout);
        marina.BerthLabelMode = BerthLabelMode.All;
        marina.Lighting.SetSunAngles(30f, 45f);
        foreach (var berth in marina.GetBerths().Take(4)) marina.SetBerthStatus(berth.Id, BerthStatus.Occupied, new Boat("B-" + berth.Id, "Boat " + berth.Id, BoatType.MotorYacht));
        return marina;
    }

    private static RenderLayer Layer(RenderFrame frame, RenderLayerKind kind) => frame.Layers.Single(layer => layer.Kind == kind);

    private static Dictionary<RenderLayerKind, (int Layout, int Version)> Versions(RenderFrame frame) =>
        frame.Layers.ToDictionary(layer => layer.Kind, layer => (layer.LayoutVersion, layer.Version));

    private static void HoverBerth(MarinaVisualizer marina, Berth berth)
    {
        Assert.True(marina.TryProjectToScreen(new Vector3(berth.Center.X, 0f, berth.Center.Y), out var screen));
        marina.Input.PointerMove(screen.X, screen.Y, InputModifiers.None);
        Assert.Equal(berth.Id, marina.HoveredBerth?.Id);
    }

    [Fact]
    public void AFrame_HasEveryLayer_InDrawingOrder()
    {
        var frame = CreateMarina().BuildRenderFrame();

        Assert.Equal(
            [RenderLayerKind.Structure, RenderLayerKind.StructureShadows, RenderLayerKind.BerthShadows, RenderLayerKind.Berths,
             RenderLayerKind.Highlight, RenderLayerKind.Overlay, RenderLayerKind.Traffic],
            frame.Layers.Select(layer => layer.Kind));
        Assert.Equal(frame.Layers.Sum(layer => layer.Count), frame.Objects.Count);
    }

    [Fact]
    public void Batches_CoverEveryInstance_OneMeshAndPassEach_OpaqueFirst()
    {
        foreach (var layer in CreateMarina().BuildRenderFrame().Layers)
        {
            var instances = layer.Instances.Span;
            var next = 0;
            var lastPass = RenderPass.Opaque;
            foreach (var batch in layer.Batches)
            {
                Assert.Equal(next, batch.Start);
                Assert.True(batch.Pass >= lastPass, $"{layer.Kind}: {batch.Pass} after {lastPass}");
                lastPass = batch.Pass;
                for (var i = batch.Start; i < batch.Start + batch.Count; i++)
                {
                    Assert.Equal(batch.MeshId, instances[i].MeshId);
                    Assert.Equal(batch.Pass, RenderLayer.PassOf(instances[i]));
                }

                next += batch.Count;
            }

            Assert.Equal(layer.Count, next);
        }
    }

    [Fact]
    public void Instancing_DrawsEachMeshOnce_PerLayerAndPass_WithEveryObjectItHad()
    {
        var frame = CreateMarina().BuildRenderFrame();
        var structure = Layer(frame, RenderLayerKind.Structure);

        // All the boxes of the piers are one draw, however many there are.
        var boxes = structure.Batches.Where(b => b.MeshId == MeshIds.UnitBox && b.Pass == RenderPass.Opaque).ToList();
        Assert.Single(boxes);
        Assert.True(boxes[0].Count > 20, $"only {boxes[0].Count} boxes");

        // Every label character and every pad is still there, just grouped by glyph mesh.
        var berths = Layer(frame, RenderLayerKind.Berths);
        var pads = berths.Batches.Where(b => b.MeshId == MeshIds.BerthPad).Sum(b => b.Count);
        Assert.Equal(12, pads);
        var glyphs = berths.Instances.ToArray().Count(o => o.MeshId >= MeshIds.GlyphBase);
        var glyphBatches = berths.Batches.Count(b => b.MeshId >= MeshIds.GlyphBase);
        Assert.True(glyphBatches < glyphs, $"{glyphs} glyphs in {glyphBatches} draws");
    }

    [Fact]
    public void Hovering_RewritesAFewBerthInstancesInPlace_AndLeavesEverythingElse()
    {
        var marina = CreateMarina();
        var before = Versions(marina.BuildRenderFrame());
        var berths = Layer(marina.BuildRenderFrame(), RenderLayerKind.Berths);
        var version = berths.Version;

        HoverBerth(marina, marina.GetBerths()[7]);
        var frame = marina.BuildRenderFrame();
        var after = Versions(frame);

        foreach (var kind in new[] { RenderLayerKind.Structure, RenderLayerKind.StructureShadows, RenderLayerKind.BerthShadows, RenderLayerKind.Overlay })
        {
            Assert.Equal(before[kind], after[kind]);
        }

        // Same layout (no full upload), a new version, and a short list of what changed.
        Assert.Equal(before[RenderLayerKind.Berths].Layout, after[RenderLayerKind.Berths].Layout);
        Assert.NotEqual(before[RenderLayerKind.Berths].Version, after[RenderLayerKind.Berths].Version);
        var changes = new List<InstanceRange>();
        Assert.True(berths.TryGetChangesSince(version, changes));
        Assert.InRange(changes.Sum(c => c.Count), 1, 20);
    }

    [Fact]
    public void Selecting_RewritesTheBerthInPlace_AndAddsItsMarker()
    {
        var marina = CreateMarina();
        var before = Versions(marina.BuildRenderFrame());

        marina.SelectBerth(marina.GetBerths()[0].Id);
        var frame = marina.BuildRenderFrame();

        Assert.Equal(before[RenderLayerKind.Structure], Versions(frame)[RenderLayerKind.Structure]);
        Assert.Equal(before[RenderLayerKind.Berths].Layout, Versions(frame)[RenderLayerKind.Berths].Layout);
        Assert.Single(Layer(frame, RenderLayerKind.Highlight).Instances.ToArray(), o => o.MeshId == MeshIds.SelectionMarker);
        Assert.True(marina.IsAnimating, "a spinning marker moves by itself");
    }

    [Fact]
    public void AStatusUpdate_RebuildsTheBerths_ButNotThePiers()
    {
        var marina = CreateMarina();
        var before = Versions(marina.BuildRenderFrame());

        marina.ReserveBerth(marina.GetBerths()[8].Id);
        var after = Versions(marina.BuildRenderFrame());

        Assert.Equal(before[RenderLayerKind.Structure], after[RenderLayerKind.Structure]);
        Assert.Equal(before[RenderLayerKind.StructureShadows], after[RenderLayerKind.StructureShadows]);
        Assert.NotEqual(before[RenderLayerKind.Berths], after[RenderLayerKind.Berths]);
    }

    [Fact]
    public void MovingTheSun_RecastsTheShadows_AndNothingElse()
    {
        var marina = CreateMarina();
        var before = Versions(marina.BuildRenderFrame());

        marina.Lighting.SetSunAngles(120f, 50f);
        var after = Versions(marina.BuildRenderFrame());

        Assert.Equal(before[RenderLayerKind.Structure], after[RenderLayerKind.Structure]);
        Assert.Equal(before[RenderLayerKind.Berths], after[RenderLayerKind.Berths]);
        Assert.NotEqual(before[RenderLayerKind.StructureShadows], after[RenderLayerKind.StructureShadows]);
    }

    [Fact]
    public void ShadowsAreDrawnUnlit_InTheirOwnPass()
    {
        var shadows = Layer(CreateMarina().BuildRenderFrame(), RenderLayerKind.StructureShadows);

        Assert.NotEqual(0, shadows.Count);
        Assert.All(shadows.Instances.ToArray(), o => Assert.NotEqual(RenderAnimation.None, o.Animation & RenderAnimation.Unlit));
        Assert.All(shadows.Batches, b => Assert.Equal(RenderPass.Shadow, b.Pass));
    }

    [Fact]
    public void PassingTraffic_ChangesOnlyTheTrafficLayer_FromFrameToFrame()
    {
        var marina = CreateMarina();
        marina.SetMarineTraffic(MarineTraffic.None with { IsEnabled = true, Clearance = 300f, Seed = 5 });
        marina.Update(1d);
        var before = Versions(marina.BuildRenderFrame());

        marina.Update(0.1d);
        var after = Versions(marina.BuildRenderFrame());

        foreach (var kind in before.Keys.Where(kind => kind != RenderLayerKind.Traffic)) Assert.Equal(before[kind], after[kind]);
        Assert.NotEqual(before[RenderLayerKind.Traffic], after[RenderLayerKind.Traffic]);
        Assert.True(marina.IsAnimating);
    }

    [Fact]
    public void AnUnchangedScene_KeepsEveryVersion()
    {
        var marina = CreateMarina();
        var first = Versions(marina.BuildRenderFrame());

        Assert.Equal(first, Versions(marina.BuildRenderFrame()));
    }

    // ---- Redraw on demand ----------------------------------------------------------------------------

    [Fact]
    public void AStillMarina_NeedsNoRedraw_UntilSomethingChanges()
    {
        var marina = CreateMarina();
        marina.Water.WaveSpeed = 0f;
        var requests = 0;
        marina.RedrawRequested += (_, _) => requests++;

        Assert.True(marina.NeedsRedraw); // never drawn yet
        marina.BuildRenderFrame();
        Assert.False(marina.NeedsRedraw);
        Assert.False(marina.IsAnimating);

        marina.ReserveBerth(marina.GetBerths()[5].Id);
        marina.ReserveBerth(marina.GetBerths()[6].Id);
        Assert.True(marina.NeedsRedraw);
        Assert.Equal(1, requests); // once until the next frame, however much changes

        marina.BuildRenderFrame();
        Assert.False(marina.NeedsRedraw);

        // Changes that raise no event are still seen: the camera moved by code, the lighting, the view size.
        marina.Camera.Orbit(10f, 0f);
        Assert.True(marina.NeedsRedraw);
        marina.Camera.Update(10f);
        marina.BuildRenderFrame();
        Assert.False(marina.NeedsRedraw);

        marina.Lighting.FogDensity = 0.01f;
        Assert.True(marina.NeedsRedraw);
        marina.BuildRenderFrame();

        marina.SetViewportSize(640f, 480f);
        Assert.True(marina.NeedsRedraw);
    }

    [Fact]
    public void MovingWater_KeepsTheViewAnimating()
    {
        var marina = CreateMarina();
        Assert.True(marina.IsAnimating);

        marina.Water.WaveSpeed = 0f;
        Assert.False(marina.IsAnimating);
    }

    [Fact]
    public void UpdatingWithoutAnimation_LeavesTimeStill_ButMovesTheCamera()
    {
        var marina = CreateMarina();
        marina.Camera.Orbit(40f, 0f);
        var time = marina.Time;

        marina.Update(0.1d, animate: false);

        Assert.Equal(time, marina.Time);
        Assert.NotEqual(marina.Camera.DesiredPose.YawDegrees - 40f, marina.Camera.Pose.YawDegrees, 3);
    }

    // ---- Upload bookkeeping --------------------------------------------------------------------------

    [Fact]
    public void TheUploadTracker_AsksForEverythingOnce_ThenOnlyTheChanges()
    {
        var marina = CreateMarina();
        var tracker = new LayerUploadTracker();
        var changes = new List<InstanceRange>();
        var frame = marina.BuildRenderFrame();

        Assert.All(frame.Layers, layer => Assert.Equal(LayerUpload.Full, tracker.Check(layer, changes)));
        foreach (var layer in frame.Layers) tracker.Uploaded(layer);
        Assert.All(marina.BuildRenderFrame().Layers, layer => Assert.Equal(LayerUpload.None, tracker.Check(layer, changes)));

        HoverBerth(marina, marina.GetBerths()[9]);
        var berths = Layer(marina.BuildRenderFrame(), RenderLayerKind.Berths);
        Assert.Equal(LayerUpload.Changes, tracker.Check(berths, changes));
        Assert.NotEmpty(changes);

        // A layer of another visualizer is never taken for one already uploaded.
        Assert.Equal(LayerUpload.Full, tracker.Check(Layer(CreateMarina().BuildRenderFrame(), RenderLayerKind.Structure), changes));
    }

    [Fact]
    public void TheSorter_PutsTransparentInstancesFarthestFirst_AndSortsOnlyWhenSomethingMoved()
    {
        RenderObject At(float z, int mesh) => new(mesh, Matrix4x4.CreateTranslation(0f, 0f, z), new Vector4(1f, 1f, 1f, 0.5f));
        var layer = RenderLayer.FromObjects(RenderLayerKind.Scene, [At(1f, 4), At(10f, 4), At(5f, 6), new(9, Matrix4x4.Identity, Vector4.One)], 1);
        var sorter = new TransparentSorter();

        Assert.True(sorter.Sort([layer], Vector3.Zero, 1));
        Assert.Equal([10f, 5f, 1f], sorter.Sorted.ToArray().Select(o => o.World.M43));
        Assert.Equal([4, 6, 4], sorter.Runs.Select(r => r.MeshId));
        Assert.False(sorter.Sort([layer], Vector3.Zero, 1));
        Assert.True(sorter.Sort([layer], new Vector3(0f, 0f, 20f), 1));
        Assert.Equal([1f, 5f, 10f], sorter.Sorted.ToArray().Select(o => o.World.M43));
    }

    // ---- Meshes --------------------------------------------------------------------------------------

    [Fact]
    public void AFlatFace_SharesItsVertices()
    {
        var box = MarinaMeshFactory.CreateUnitBox(1);

        Assert.Equal(24, box.VertexCount); // six faces of four corners, instead of three per triangle
        Assert.Equal(12, box.TriangleCount);
    }

    [Fact]
    public void Scenery_IsInstancesOfAFewSharedMeshes()
    {
        var lawn = new LandArea("lawn", new[] { new Vector2(-40, -40), new Vector2(40, -40), new Vector2(40, 40), new Vector2(-40, 40) }, 1f, LandKind.Grass);
        lawn = lawn with { Trees = LandArea.GenerateTrees(lawn.Points, 8f, new Random(4)) };
        var marina = new MarinaVisualizer();
        marina.AddLandArea(lawn);

        var structure = Layer(marina.BuildRenderFrame(), RenderLayerKind.Structure);
        var trunks = structure.Batches.Where(b => b.MeshId == MeshIds.TreeTrunk).ToList();
        Assert.Equal(lawn.Trees.Count, Assert.Single(trunks).Count);
        Assert.True(marina.Meshes.Get(MeshIds.ForLand(0)).TriangleCount < 50, "the ground mesh still carries the trees");
    }

    [Fact]
    public void LandSlots_AreReused_SoAddingAndRemovingNeverRunsOutOfIds()
    {
        var marina = new MarinaVisualizer();
        var square = new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10) };
        for (var i = 0; i < 50; i++)
        {
            marina.AddLandArea(new LandArea($"l{i}", square, 1f));
            marina.RemoveLandArea($"l{i}");
        }

        marina.AddLandArea(new LandArea("last", square, 1f));
        Assert.True(marina.Meshes.TryGet(MeshIds.ForLand(0), out _));
        Assert.False(marina.Meshes.TryGet(MeshIds.ForLand(1), out _));
    }

    // ---- Traffic -------------------------------------------------------------------------------------

    [Fact]
    public void ChangingTheLayout_KeepsTheTrafficWhereItIs()
    {
        var marina = CreateMarina();
        marina.SetMarineTraffic(MarineTraffic.None with { IsEnabled = true, Clearance = 300f, Seed = 5 });
        marina.GetTrafficVessels();
        for (var i = 0; i < 40; i++) marina.Update(0.25d);
        var before = marina.GetTrafficVessels();

        marina.AddPier(new Pier("B", "Pier B", new Vector2(40f, 0f), 0f, 30f));
        var after = marina.GetTrafficVessels();

        // The same vessels, of the same kinds, in the same order: carried over onto the new lanes rather than started over
        // from the seed (which would put them back at their starting places, the jump this avoids).
        Assert.Equal(before.Count, after.Count);
        Assert.Equal(before.Select(v => v.Type), after.Select(v => v.Type));
        var restarted = MarinaVisualizerFor(marina);
        Assert.NotEqual(restarted.Select(v => v.Position), after.Select(v => v.Position));
    }

    /// <summary>Where the traffic would be on a fresh visualizer with the same layout: started from its seed.</summary>
    private static IReadOnlyList<TrafficVessel> MarinaVisualizerFor(MarinaVisualizer marina)
    {
        var fresh = new MarinaVisualizer();
        fresh.InitializeLayout(marina.GetLayout());
        fresh.SetMarineTraffic(marina.MarineTraffic);
        return fresh.GetTrafficVessels();
    }

    [Fact]
    public void ALane_TurnsSmoothlyThroughABend()
    {
        var lane = new TrafficLane([new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 100)]);

        var (_, beforeBend) = lane.At(0.45f);
        var (atBend, throughBend) = lane.At(0.5f);
        var (_, afterBend) = lane.At(0.55f);

        Assert.Equal(new Vector2(100, 0), atBend);
        Assert.InRange(MathF.Atan2(throughBend.Y, throughBend.X), 0.6f, 1f); // halfway round, not snapped to either leg
        Assert.True(beforeBend.Y < throughBend.Y && throughBend.Y < afterBend.Y);
        Assert.Equal(Vector2.UnitX, lane.At(0.1f).Direction);
    }
}
