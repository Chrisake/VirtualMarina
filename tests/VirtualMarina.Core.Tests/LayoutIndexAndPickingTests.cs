using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Picking;
using VirtualMarina.Core.Rendering;
using VirtualMarina.Core.Resources;
using VirtualMarina.SampleData;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// The indexes behind the layout (order, berths by pier and land area, the selection) stay in step with every change;
/// the cached pick set answers exactly as picking everything would; and the smaller API additions behave as documented.
/// </summary>
public class LayoutIndexAndPickingTests
{
    private static MarinaVisualizer Sample()
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(1280f, 720f);
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());
        return marina;
    }

    private static MarinaVisualizer TwoPiers()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        marina.AddPier(new Pier("B", "Pier B", new Vector2(60, -30), 0f, 60f));
        foreach (var pierId in new[] { "A", "B" })
        {
            var pier = marina.GetPier(pierId)!;
            marina.AddBerths(Enumerable.Range(0, 4).Select(i => BerthGenerator.AtPier(pier, $"{pierId}-{i}", PierSide.Left, i * 6f, 5f, 12f)));
        }

        return marina;
    }

    // ---- The ordered store ---------------------------------------------------------------------

    [Fact]
    public void TheStore_KeepsTheOrder_ThroughRemovalsReplacementsAndShuffledIds()
    {
        var store = new OrderedStore<string>(StringComparer.OrdinalIgnoreCase, value => value.Split(':')[0]);
        foreach (var value in new[] { "p:a", "q:b", "p:c", "q:d" }) store.Put(value.Split(':')[1], value);

        store.Remove("B");
        store.Put("c", "q:c");                          // replaced where it stands, moved to the other index
        store.Rekey([("a", "d2", "p:a"), ("d", "a", "q:d")]);

        Assert.Equal(new[] { "p:a", "q:c", "q:d" }, store.Values);
        Assert.Equal(new[] { "p:a" }, store.WithKey(0, "P"));
        Assert.Equal(new[] { "q:c", "q:d" }, store.WithKey(0, "q"));
        Assert.Equal(2, store.CountWithKey(0, "q"));
        Assert.True(store.ContainsKey("D2") && store.ContainsKey("a") && !store.ContainsKey("d"));
    }

    [Fact]
    public void BerthsByPier_FollowEveryChange_InLayoutOrder()
    {
        var marina = TwoPiers();

        // Moved to the other pier, renamed, removed and added: each list is still the pier's berths in layout order.
        marina.UpdateBerth(marina.GetBerth("A-1")! with { PierId = "B" });
        marina.RenameBerth("B-2", "B-TWO");
        marina.RemoveBerth("A-3");
        marina.AddBerth(BerthGenerator.AtPier(marina.GetPier("A")!, "A-9", PierSide.Right, 10f, 5f, 12f));
        marina.ChangePierId("B", "BB");

        foreach (var pierId in new[] { "A", "BB" })
        {
            var expected = marina.GetBerths().Where(b => b.PierId == pierId).Select(b => b.Id);
            Assert.Equal(expected, marina.GetBerthsByPier(pierId.ToLowerInvariant()).Select(b => b.Id));
        }

        Assert.Empty(marina.GetBerthsByPier("B"));
        Assert.Equal(new[] { "A-0", "A-2", "A-9" }, marina.GetBerthsByPier("A").Select(b => b.Id));
    }

    [Fact]
    public void RemovingAPier_TakesItsBerthsAndDividers_AndLeavesTheRestInOrder()
    {
        var marina = TwoPiers();
        marina.AddDivider(new Divider("DA", new Vector2(0, 0), 90f, 5f) { PierId = "A" });
        marina.AddDivider(new Divider("DB", new Vector2(60, 0), 90f, 5f) { PierId = "B" });

        Assert.True(marina.RemovePier("A"));

        Assert.Equal(new[] { "B-0", "B-1", "B-2", "B-3" }, marina.GetBerths().Select(b => b.Id));
        Assert.Equal(new[] { "DB" }, marina.GetDividers().Select(d => d.Id));
        Assert.Equal(new[] { "DB" }, marina.GetDividersByPier("B").Select(d => d.Id));
    }

    // ---- Selection -----------------------------------------------------------------------------

    [Fact]
    public void TheSelection_IsOneSnapshotPerChange_AndFollowsRenames()
    {
        var marina = TwoPiers();
        marina.SetSelection("A-0", "a-1", "A-0", "B-2");

        var first = marina.SelectedBerths;
        Assert.Same(first, marina.SelectedBerths);
        Assert.True(((ICollection<Berth>)first).IsReadOnly);
        Assert.Equal(new[] { "A-0", "A-1", "B-2" }, first.Select(b => b.Id));
        Assert.True(marina.IsBerthSelected("b-2"));

        marina.RenameBerth("A-1", "A-ONE");
        Assert.True(marina.IsBerthSelected("A-ONE"));
        Assert.False(marina.IsBerthSelected("A-1"));
        Assert.Equal(new[] { "A-0", "A-ONE", "B-2" }, marina.SelectedBerths.Select(b => b.Id));

        marina.SetBerthStatus("A-0", BerthStatus.Reserved);
        Assert.Equal(BerthStatus.Reserved, marina.SelectedBerths[0].Status);
    }

    // ---- Picking -------------------------------------------------------------------------------

    [Theory]
    [InlineData(200f, 42f)]
    [InlineData(20f, 70f)]
    [InlineData(135f, 12f)]
    public void TheCachedPickSet_AnswersExactlyAsPickingEverything(float yaw, float pitch)
    {
        var marina = Sample();
        marina.Camera.SetPose(marina.Camera.DesiredPose with { YawDegrees = yaw, PitchDegrees = pitch }, immediate: true);
        var shown = marina.GetBerths().Where(b => marina.IsBerthVisible(b.Id)).ToList();
        float? Ground(Berth berth) => SceneBuilder.GroundHeight(berth, marina.GetLandArea);

        var hits = 0;
        for (var y = 10f; y < 720f; y += 23f)
        {
            for (var x = 10f; x < 1280f; x += 29f)
            {
                var ray = marina.Camera.ScreenPointToRay(x, y, 1280f, 720f);
                var boats = BerthPlacement.EnumerateBoats(shown, marina.GetBerth, marina.GetMultiBerth, marina.StatusFilter, Ground, marina.Meshes);
                var expected = ScenePicker.Pick(ray, shown, boats, marina.Meshes, Ground);
                var actual = marina.HitTest(x, y);

                Assert.Equal(expected?.BerthId, actual?.BerthId);
                Assert.Equal(expected?.HitBoat, actual?.HitBoat);
                if (expected is { } e) Assert.Equal(e.Distance, actual!.Value.Distance, 3);
                if (actual is not null) hits++;
            }
        }

        Assert.True(hits > 50, $"only {hits} probes hit anything");
    }

    [Fact]
    public void ThePickSet_IsBuiltAgainWhenWhatCanBeHitChanges()
    {
        var marina = TwoPiers();
        marina.SetViewportSize(1000f, 800f);
        var berth = marina.GetBerth("A-1")!;
        marina.Camera.SetPose(new CameraPose(MarinaMath.ToWorld(berth.Center), 0f, 89f, 60f), immediate: true);

        Assert.Equal("A-1", marina.HitTest(500f, 400f)?.BerthId);

        marina.SetBerthVisible("A-1", false);
        Assert.NotEqual("A-1", marina.HitTest(500f, 400f)?.BerthId);

        marina.SetBerthVisible("A-1", true);
        marina.SetStatusFilter(BerthStatus.Occupied);
        Assert.Null(marina.HitTest(500f, 400f));

        marina.ShowAllStatuses();
        marina.AssignBoat("A-1", new Boat("B1", "Aurora", BoatType.MotorYacht) { LengthMeters = 10f, BeamMeters = 3.5f });
        Assert.True(marina.HitTest(500f, 400f)?.HitBoat);
    }

    // ---- Batches -------------------------------------------------------------------------------

    [Fact]
    public void ABatch_ListsEveryChangeItMade_InOrder()
    {
        var marina = TwoPiers();
        var events = new List<LayoutChangedEventArgs>();
        marina.LayoutChanged += (_, e) => events.Add(e);

        using (marina.BeginUpdate())
        {
            marina.SetBerthStatus("A-0", BerthStatus.Reserved);
            marina.RemoveBerth("B-3");
            marina.AddPier(new Pier("C", "Pier C", new Vector2(120, -30), 0f, 30f));
        }

        var batch = Assert.Single(events);
        Assert.Equal(LayoutChangeKind.BatchUpdated, batch.Kind);
        Assert.Equal(
            new[] { (LayoutChangeKind.BerthUpdated, "A-0"), (LayoutChangeKind.BerthRemoved, "B-3"), (LayoutChangeKind.PierAdded, (string?)null) },
            batch.Changes.Select(c => (c.Kind, c.BerthId)));
        Assert.Equal("C", batch.Changes[2].PierId);

        marina.RemoveBerth("A-0");
        var single = Assert.Single(events[1].Changes);
        Assert.Equal(new LayoutChange(LayoutChangeKind.BerthRemoved, "A", "A-0"), single);
    }

    // ---- External data -------------------------------------------------------------------------

    [Fact]
    public void ExternalDataRemovals_TakeKeysOut_BeforeTheNewEntriesGoIn()
    {
        var marina = TwoPiers();
        marina.UpdateBerth(new BerthUpdate("A-0") { ExternalData = new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2, ["c"] = 3 } });

        marina.UpdateBerth(new BerthUpdate("A-0")
        {
            ExternalDataRemovals = new[] { "a", "c", "missing" },
            ExternalData = new Dictionary<string, object?> { ["c"] = 30 },
        });

        var data = marina.GetBerth("A-0")!.ExternalData;
        Assert.False(data.ContainsKey("a"));
        Assert.Equal(2, data["b"]);
        Assert.Equal(30, data["c"]);
    }

    // ---- Multi-berths --------------------------------------------------------------------------

    [Fact]
    public void AMultiBerthAcrossTwoPiers_IsRefused()
    {
        var marina = TwoPiers();
        var boat = new Boat("SY", "Superyacht", BoatType.MotorYacht) { LengthMeters = 30f, BeamMeters = 7f };

        var error = Assert.Throws<MarinaLayoutException>(() => marina.AssignBoatToBerths(new[] { "A-0", "B-0" }, boat));
        Assert.Contains("different piers", error.Message);
        Assert.Null(marina.GetBerth("A-0")!.MultiBerthId);
    }

    [Fact]
    public void AMultiBerthAndABerth_CannotShareAnId_WhicheverComesFirst()
    {
        var marina = TwoPiers();
        var boat = new Boat("SY", "Superyacht", BoatType.MotorYacht) { LengthMeters = 12f, BeamMeters = 7f };

        Assert.Throws<MarinaLayoutException>(() => marina.AssignBoatToBerths(new[] { "A-0", "A-1" }, boat, multiBerthId: "B-3"));

        marina.AssignBoatToBerths(new[] { "A-0", "A-1" }, boat, multiBerthId: "MB");
        var error = Assert.Throws<MarinaLayoutException>(() => marina.AddBerth(BerthGenerator.AtPier(marina.GetPier("B")!, "mb", PierSide.Right, 5f, 5f, 12f)));
        Assert.Contains("multi-berth", error.Message);
    }

    // ---- Land ----------------------------------------------------------------------------------

    [Fact]
    public void PlantingTrees_KeepsTheGroundMesh_ButMovingTheOutlineRebuildsIt()
    {
        var marina = new MarinaVisualizer();
        var outline = new[] { new Vector2(0, 0), new Vector2(40, 0), new Vector2(40, 30), new Vector2(0, 30) };
        marina.AddLandArea(new LandArea("L", outline, 2f));
        var ground = marina.Meshes.Get(MeshIds.ForLand(0));
        _ = marina.CameraPresets;
        var rebuilds = marina.PresetRebuildCount;

        marina.UpdateLandArea(marina.GetLandArea("L")! with { Trees = new[] { new LandTree(new Vector2(10, 10), 6f, 2f) }, Name = "Park" });
        Assert.Same(ground, marina.Meshes.Get(MeshIds.ForLand(0)));
        _ = marina.CameraPresets;
        Assert.Equal(rebuilds, marina.PresetRebuildCount);

        marina.UpdateLandArea(marina.GetLandArea("L")! with { Points = outline.Select(p => p * 2f).ToArray() });
        Assert.NotSame(ground, marina.Meshes.Get(MeshIds.ForLand(0)));

        // A changed outline is still checked.
        var crossed = new[] { new Vector2(0, 0), new Vector2(40, 30), new Vector2(40, 0), new Vector2(0, 30) };
        Assert.Throws<MarinaLayoutException>(() => marina.UpdateLandArea(marina.GetLandArea("L")! with { Points = crossed }));
    }

    // ---- Style ---------------------------------------------------------------------------------

    [Fact]
    public void AClonedStyle_CarriesEveryStatusSetting()
    {
        var style = new MarinaStyle();
        style.Status.OccupiedColor = new ColorRgba(0.1f, 0.2f, 0.3f);
        style.Status.DisabledColor = new ColorRgba(0.4f, 0.4f, 0.4f);
        style.Status.PadOpacity = 0.7f;
        style.Status.OccupiedBoatOpacity = 0.9f;
        style.Status.ReservedBoatOpacity = 0.2f;
        style.Status.TemporarilyFreeBoatOpacity = 0.3f;
        style.Status.GhostBoatTint = 0.1f;
        style.Status.ShowStatusMarkers = false;
        style.Status.StatusMarkerScale = 2f;

        var copy = style.Clone().Status;

        Assert.Equal(style.Status.OccupiedColor, copy.OccupiedColor);
        Assert.Equal(style.Status.DisabledColor, copy.DisabledColor);
        Assert.Equal(0.7f, copy.PadOpacity);
        Assert.Equal(0.9f, copy.OccupiedBoatOpacity);
        Assert.Equal(0.2f, copy.ReservedBoatOpacity);
        Assert.Equal(0.3f, copy.TemporarilyFreeBoatOpacity);
        Assert.Equal(0.1f, copy.GhostBoatTint);
        Assert.False(copy.ShowStatusMarkers);
        Assert.Equal(2f, copy.StatusMarkerScale);
    }

    [Fact]
    public void ChangingTheLightingOrTheWater_AsksForAFrame_AndANewWaterSizeTakesEffect()
    {
        var marina = new MarinaVisualizer();
        marina.BuildRenderFrame();
        var requests = 0;
        marina.RedrawRequested += (_, _) => requests++;

        marina.Lighting.SunDirection = new Vector3(0.2f, 1f, 0.1f);
        Assert.Equal(1, requests);

        marina.BuildRenderFrame();
        marina.Water.Size = 900f;
        Assert.Equal(2, requests);
        Assert.Equal(450f, marina.BuildRenderFrame().WaterDetailRadius, 0);

        // A replaced style is listened to instead, and the old one no longer is.
        var old = marina.Style;
        marina.Style = new MarinaStyle();
        marina.BuildRenderFrame();
        requests = 0;
        old.Lighting.SunDirection = Vector3.UnitX;
        Assert.Equal(0, requests);
        marina.Style.Water.WaveAmplitude = 0.5f;
        Assert.Equal(1, requests);
    }

    // ---- Text ----------------------------------------------------------------------------------

    [Fact]
    public void CountedText_TakesTheSingularForOne()
    {
        Assert.Equal("Add 1 berth", Strings.Plural("UndoAddBerths", 1, 1));
        Assert.Equal("Add 3 berths", Strings.Plural("UndoAddBerths", 3, 3));
        Assert.Equal("Erase 1 berth from Pier A", Strings.Plural("UndoEraseBerthsOfPier", 1, 1, "Pier A"));
        Assert.Equal("Set power on 1 berth", Strings.Plural("UndoSetServices", 1, "power", 1));
    }

    [Fact]
    public void ValidationMessages_ComeFromTheResourceFile()
    {
        var errors = new MarinaLayout { Berths = new[] { new Berth("X", "nowhere", Vector2.Zero, 0f, 10f, 4f) } }.Validate();
        Assert.Contains(Strings.Format(Strings.ErrorBerthUnknownPier, "X", "nowhere"), errors);
        Assert.Equal("Berth 'X' references unknown pier 'nowhere'.", Strings.Format(Strings.ErrorBerthUnknownPier, "X", "nowhere"));
    }
}
