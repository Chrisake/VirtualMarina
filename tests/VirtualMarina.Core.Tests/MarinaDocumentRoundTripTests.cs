using System.Numerics;
using System.Text.Json.Nodes;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Rendering;
using VirtualMarina.Core.Serialization;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// Opening a file, working on it and saving it again: what the file carried must come back out, what it no longer says must
/// not creep back in, and a file that can't be opened must leave the current design alone.
/// </summary>
public class MarinaDocumentRoundTripTests
{
    private static MarinaLayout SmallLayout(string name = "Harbor") =>
        new MarinaLayoutBuilder(name)
            .AddLandArea(new LandArea("quay", new[] { new Vector2(-10, -10), new Vector2(10, -10), new Vector2(10, 10) }, 1.5f)
            {
                Trees = new[] { new LandTree(new Vector2(2, -5), 6f, 2f, TreeShape.Conifer) },
            })
            .AddPier("A", "Pier A", Vector2.Zero, 0f, 40f, pier => pier.AddBerths(PierSide.Left, 3, 5f, 12f))
            .Build();

    private static MarinaVisualizer Showing(MarinaLayout layout)
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(layout);
        marina.SetViewportSize(800, 600);
        return marina;
    }

    private static ReferenceImage Picture() =>
        new(2, 2, new byte[2 * 2 * 4], new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3 }, "image/png");

    /// <summary>A file as a newer version (or another application) might write it: the same marina with things this build doesn't know.</summary>
    private static string FileWithForeignProperties()
    {
        var marina = Showing(SmallLayout());
        marina.SaveCameraPreset("Fuel dock");
        marina.Designer.SetReferenceImage(Picture(), metersPerPixel: 0.5f);
        var berthId = marina.GetBerths()[0].Id;
        marina.AssignBoatToBerths(
            new[] { marina.GetBerths()[1].Id, marina.GetBerths()[2].Id },
            new Boat("B1", "Aurora", BoatType.MotorYacht) { LengthMeters = 14f, BeamMeters = 4f },
            BerthStatus.Occupied,
            multiBerthId: "VIP");

        var root = JsonNode.Parse(MarinaDocument.FromVisualizer(marina).ToJson())!.AsObject();
        root["marina"]!["description"] = "The old harbor, as surveyed in 2024";
        root["erp"] = new JsonObject { ["siteId"] = 42 };
        var layout = root["layout"]!;
        layout["berths"]!.AsArray().First(b => (string?)b!["id"] == berthId)!["tariffZone"] = "north";
        layout["piers"]![0]!["deckMaterial"] = "teak";
        layout["landAreas"]![0]!["trees"]![0]!["species"] = "olive";
        layout["multiBerths"]![0]!["boat"]!["mmsi"] = "237000000";
        root["presentation"]!["shadows"]!["softness"] = 0.3;
        root["referenceImage"]!["source"] = "survey.png";
        root["cameraPresets"]![0]!["hotkey"] = "F1";
        root["designer"]!["berthNaming"]!["suffix"] = "b";
        return root.ToJsonString();
    }

    private static JsonObject Reparse(MarinaDocument document) => JsonNode.Parse(document.ToJson())!.AsObject();

    [Fact]
    public void SavingAnOpenedDesign_KeepsTheDescriptionExtensionsAndUnknownElementProperties()
    {
        var document = MarinaDocument.Parse(FileWithForeignProperties());
        var marina = new MarinaVisualizer();
        document.ApplyTo(marina);

        // The user works on it: moves the first berth and adds a pier.
        var first = marina.GetBerths()[0];
        marina.UpdateBerth(first with { Label = "Renamed" });
        marina.AddPier(new Pier("B", "Pier B", new Vector2(30, 0), 0f, 20f));

        document.UpdateFrom(marina, generator: "tests");
        var saved = Reparse(document);

        Assert.Equal("The old harbor, as surveyed in 2024", (string?)saved["marina"]!["description"]);
        Assert.Equal(42, (int)saved["erp"]!["siteId"]!);
        Assert.Equal("tests", (string?)saved["generator"]);
        var berth = saved["layout"]!["berths"]!.AsArray().First(b => (string?)b!["id"] == first.Id)!;
        Assert.Equal("north", (string?)berth["tariffZone"]);
        Assert.Equal("Renamed", (string?)berth["label"]);
        Assert.Equal("teak", (string?)saved["layout"]!["piers"]![0]!["deckMaterial"]);
        Assert.Equal(2, saved["layout"]!["piers"]!.AsArray().Count);
        Assert.Equal("olive", (string?)saved["layout"]!["landAreas"]![0]!["trees"]![0]!["species"]);
        Assert.Equal("237000000", (string?)saved["layout"]!["multiBerths"]![0]!["boat"]!["mmsi"]);
        Assert.Equal(0.3, (double)saved["presentation"]!["shadows"]!["softness"]!, 3);
        Assert.Equal("survey.png", (string?)saved["referenceImage"]!["source"]);
        Assert.Equal("F1", (string?)saved["cameraPresets"]![0]!["hotkey"]);
        Assert.Equal("b", (string?)saved["designer"]!["berthNaming"]!["suffix"]);
    }

    [Fact]
    public void UnknownPropertiesOfADeletedElement_AreNotWritten()
    {
        var document = MarinaDocument.Parse(FileWithForeignProperties());
        var marina = new MarinaVisualizer();
        document.ApplyTo(marina);
        var tagged = document.Layout.Berths[0].Id;

        marina.RemoveBerth(tagged);
        document.UpdateFrom(marina);

        Assert.DoesNotContain("tariffZone", document.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void FromVisualizer_StartsAFreshDocument()
    {
        var marina = Showing(SmallLayout());

        var document = MarinaDocument.FromVisualizer(marina, generator: "tests");

        Assert.Equal("tests", document.Generator);
        Assert.Null(document.Description);
        Assert.Empty(document.Extensions);
        Assert.Equal(3, document.Layout.Berths.Count);
    }

    [Fact]
    public void OpeningADesign_ReplacesTheSavedViewsAndTracingImageOfThePreviousOne()
    {
        var marina = Showing(SmallLayout());
        marina.SaveCameraPreset("Old view");
        Assert.True(marina.SetCameraPresetEnabled("Top Down", enabled: false));
        marina.Designer.SetReferenceImage(Picture(), metersPerPixel: 0.5f);

        var other = Showing(SmallLayout("Other"));
        other.SaveCameraPreset("New view");
        Assert.True(other.SetCameraPresetEnabled("North", enabled: false));
        MarinaDocument.Parse(MarinaDocument.FromVisualizer(other).ToJson()).ApplyTo(marina);

        var saved = marina.CameraPresets.Where(p => !p.IsBuiltIn).Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "New view" }, saved);
        Assert.True(marina.CameraPresets.Single(p => p.Name == "Top Down").IsEnabled);
        Assert.False(marina.CameraPresets.Single(p => p.Name == "North").IsEnabled);
        Assert.Null(marina.Designer.ReferenceImage);
    }

    [Fact]
    public void OpeningADesign_WithoutApplyingItsCamera_LeavesTheSavedViewsAlone()
    {
        var marina = Showing(SmallLayout());
        marina.SaveCameraPreset("Old view");
        marina.Designer.SetReferenceImage(Picture(), metersPerPixel: 0.5f);

        MarinaDocument.FromVisualizer(Showing(SmallLayout("Other"))).ApplyTo(marina, applyCamera: false, applyReferenceImage: false);

        Assert.Contains(marina.CameraPresets, p => p.Name == "Old view");
        Assert.NotNull(marina.Designer.ReferenceImage);
    }

    [Fact]
    public void AFileThatCannotBeOpened_LeavesTheVisualizerAsItWas()
    {
        var marina = Showing(SmallLayout());
        marina.Style.Water.WaveAmplitude = 0.2f;
        var style = marina.Style;

        var broken = new MarinaDocument
        {
            Name = "Broken",
            Layout = new MarinaLayout
            {
                Berths = new[] { new Berth("X-1", "missing-pier", Vector2.Zero, 0f, 12f, 5f) },
            },
        };
        broken.Style.Water.WaveAmplitude = 0.01f;

        Assert.Throws<MarinaLayoutException>(() => broken.ApplyTo(marina));
        Assert.Same(style, marina.Style);
        Assert.Equal(0.2f, marina.Style.Water.WaveAmplitude);
        Assert.Equal("Harbor", marina.MarinaName);
        Assert.Equal(3, marina.GetBerths().Count);
    }

    [Fact]
    public void TwoVisualizersLoadedFromOneDocument_DoNotShareTheirStyle()
    {
        var document = MarinaDocument.FromVisualizer(Showing(SmallLayout()));
        var first = new MarinaVisualizer();
        var second = new MarinaVisualizer();
        document.ApplyTo(first);
        document.ApplyTo(second);

        first.Style.Status.FreeColor = ColorRgba.FromHex("#123456");
        first.Style.Water.WaveAmplitude = 0.5f;

        Assert.NotSame(first.Style, second.Style);
        Assert.NotSame(document.Style, first.Style);
        Assert.Equal(StatusColorScheme.DefaultFree, second.Style.Status.FreeColor);
        Assert.Equal(StatusColorScheme.DefaultFree, document.Style.Status.FreeColor);
        Assert.NotEqual(0.5f, second.Style.Water.WaveAmplitude);
    }

    [Fact]
    public void ACapturedDocument_DoesNotFollowLaterStyleChanges()
    {
        var marina = Showing(SmallLayout());
        var document = MarinaDocument.FromVisualizer(marina);

        marina.Style.Land.RoofColor = ColorRgba.FromHex("#101010");

        Assert.NotSame(marina.Style, document.Style);
        Assert.NotEqual(marina.Style.Land.RoofColor, document.Style.Land.RoofColor);
    }

    [Fact]
    public void Clone_CopiesEverySection()
    {
        var style = new MarinaStyle();
        style.Lighting.FogDensity = 0.01f;
        style.Water.Size = 900f;
        style.Status.Set(BerthStatus.Reserved, ColorRgba.FromHex("#AABBCC"));
        style.Status.PadOpacity = 0.2f;
        style.Land.BuildingColor = ColorRgba.FromHex("#112233");
        style.Piers.WoodColor = ColorRgba.FromHex("#445566");
        style.Labels.Typeface = LabelTypeface.Serif;
        style.Selection.MarkerScale = 2f;
        style.View.FieldOfViewDegrees = 30f;
        style.Shadows.Strength = 0.1f;

        var copy = style.Clone();

        Assert.NotSame(style.Status, copy.Status);
        Assert.NotSame(style.Water, copy.Water);
        Assert.Equal(0.01f, copy.Lighting.FogDensity);
        Assert.Equal(900f, copy.Water.Size);
        Assert.Equal("#AABBCC", copy.Status.ReservedColor.ToHex());
        Assert.Equal(0.2f, copy.Status.PadOpacity);
        Assert.Equal("#112233", copy.Land.BuildingColor.ToHex());
        Assert.Equal("#445566", copy.Piers.WoodColor.ToHex());
        Assert.Equal(LabelTypeface.Serif, copy.Labels.Typeface);
        Assert.Equal(2f, copy.Selection.MarkerScale);
        Assert.Equal(30f, copy.View.FieldOfViewDegrees);
        Assert.Equal(0.1f, copy.Shadows.Strength);
    }

    [Fact]
    public void BuildingAndRoofColors_RoundTrip_AndFallBackToTheDefaultsWhenMissing()
    {
        var document = new MarinaDocument { Layout = SmallLayout() };
        document.Style.Land.BuildingColor = ColorRgba.FromHex("#E0D0C0");
        document.Style.Land.RoofColor = ColorRgba.FromHex("#803020");

        var reloaded = MarinaDocument.Parse(document.ToJson());
        Assert.Equal("#E0D0C0", reloaded.Style.Land.BuildingColor.ToHex());
        Assert.Equal("#803020", reloaded.Style.Land.RoofColor.ToHex());

        var root = JsonNode.Parse(document.ToJson())!.AsObject();
        var land = root["presentation"]!["land"]!.AsObject();
        land.Remove("building");
        land.Remove("roof");
        var older = MarinaDocument.Parse(root.ToJsonString());
        Assert.Equal(new LandStyle().BuildingColor, older.Style.Land.BuildingColor);
        Assert.Equal(new LandStyle().RoofColor, older.Style.Land.RoofColor);
    }

    [Fact]
    public void AFormat1File_SavedAgain_DoesNotLetItsOldNamesOverrideTheNewValues()
    {
        const string json = """
            {
              "format": "virtualmarina.marina",
              "formatVersion": "1.0",
              "layout": {
                "docks": [ { "id": "A", "start": [0, 0], "headingDegrees": 0, "length": 40, "width": 3 } ],
                "slips": [
                  { "id": "A-1", "dockId": "A", "center": [6, 10], "headingDegrees": -90, "length": 12, "width": 5 },
                  { "id": "A-2", "dockId": "A", "center": [6, 16], "headingDegrees": -90, "length": 12, "width": 5 }
                ],
                "dividers": [ { "id": "A-D1", "dockId": "A", "start": [1.5, 8], "headingDegrees": 90, "length": 9 } ],
                "berths": [ { "id": "VIP", "slipIds": ["A-1", "A-2"], "style": "BowIn", "status": "Occupied",
                              "boat": { "id": "B-1", "name": "Aurora", "type": "MotorYacht" } } ]
              },
              "presentation": { "slipLabels": "All" },
              "designer": { "dockWidth": 4, "slipWidth": 6.5 }
            }
            """;

        var document = MarinaDocument.Parse(json);
        Assert.Equal(BerthLabelMode.All, document.BerthLabels);
        Assert.Equal(4f, document.Designer!.PierWidth);

        document.BerthLabels = BerthLabelMode.None;
        document.Designer = document.Designer with { PierWidth = 2f, BerthWidth = 5f };
        var saved = document.ToJson();

        foreach (var old in new[] { "slipLabels", "dockWidth", "slipWidth", "dockId", "\"docks\"", "\"slips\"", "slipIds" })
        {
            Assert.DoesNotContain(old, saved, StringComparison.Ordinal);
        }

        // Written once, under the current name only.
        var group = JsonNode.Parse(saved)!["layout"]!["multiBerths"]![0]!.AsObject();
        Assert.Equal(1, group.Count(property => string.Equals(property.Key, "style", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("BowIn", (string?)group["style"]);

        var again = MarinaDocument.Parse(saved);
        Assert.Equal(BerthLabelMode.None, again.BerthLabels);
        Assert.Equal(2f, again.Designer!.PierWidth);
        Assert.Equal(5f, again.Designer.BerthWidth);
        Assert.Equal("A", again.Layout.Dividers[0].PierId);
        Assert.Equal(new[] { "A-1", "A-2" }, Assert.Single(again.Layout.MultiBerths).BerthIds);
    }

    [Theory]
    [InlineData("{ \"r\": 1, \"g\": 0, \"b\": 0 }")]
    [InlineData("\"not a color\"")]
    [InlineData("\"#12\"")]
    [InlineData("null")]
    [InlineData("true")]
    public void AColorThatCannotBeRead_KeepsItsDefault_AndTheRestOfTheFileLoads(string value)
    {
        var json = """
            {
              "format": "virtualmarina.marina",
              "formatVersion": "2.0",
              "presentation": {
                "status": { "free": VALUE, "occupied": "#FF0000" },
                "land": { "quay": VALUE },
                "labels": { "color": VALUE },
                "selection": { "markerTint": VALUE },
                "structures": { "wood": VALUE }
              },
              "marina": { "name": "After the color" }
            }
            """.Replace("VALUE", value, StringComparison.Ordinal);

        var document = MarinaDocument.Parse(json);

        Assert.Equal(StatusColorScheme.DefaultFree, document.Style.Status.FreeColor);
        Assert.Equal("#FF0000", document.Style.Status.OccupiedColor.ToHex());
        Assert.Equal(new LandStyle().QuayColor, document.Style.Land.QuayColor);
        Assert.Equal(new LabelStyle().Color, document.Style.Labels.Color);
        Assert.Equal(new SelectionStyle().MarkerTint, document.Style.Selection.MarkerTint);
        Assert.Equal(new StructureStyle().WoodColor, document.Style.Piers.WoodColor);
        Assert.Equal("After the color", document.Name);
    }

    [Fact]
    public void AColorGivenAsAnArray_IsStillRead()
    {
        const string json = """
            { "format": "virtualmarina.marina", "presentation": { "status": { "free": [0, 0, 1] } } }
            """;

        Assert.Equal("#0000FF", MarinaDocument.Parse(json).Style.Status.FreeColor.ToHex());
    }

    [Fact]
    public void AWaterSectionWithoutASize_UsesTheCurrentDefault()
    {
        const string json = """
            { "format": "virtualmarina.marina", "presentation": { "water": { "waveAmplitude": 0.02 } } }
            """;

        var document = MarinaDocument.Parse(json);

        Assert.Equal(new WaterSettings().Size, document.Style.Water.Size);
        Assert.Equal(0.02f, document.Style.Water.WaveAmplitude, 4);
    }

    [Fact]
    public void ARejectedBerthUpdate_LeavesTheExternalDataAlone()
    {
        var marina = Showing(SmallLayout());
        var berthId = marina.GetBerths()[0].Id;

        var result = marina.BatchUpdate(new[]
        {
            new BerthUpdate(berthId) { Length = -1f, ExternalData = new Dictionary<string, object?> { ["contract"] = "C-1" } },
        });

        Assert.NotEmpty(result.Errors);
        Assert.False(marina.GetBerth(berthId)!.ExternalData.ContainsKey("contract"));
        Assert.Throws<MarinaLayoutException>(() =>
            marina.UpdateBerth(new BerthUpdate(berthId) { Width = -1f, ExternalData = new Dictionary<string, object?> { ["contract"] = "C-2" } }));
        Assert.False(marina.GetBerth(berthId)!.ExternalData.ContainsKey("contract"));

        marina.UpdateBerth(new BerthUpdate(berthId) { ExternalData = new Dictionary<string, object?> { ["contract"] = "C-3" } });
        Assert.Equal("C-3", marina.GetBerth(berthId)!.ExternalData["contract"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReplacingOrClearingTheLayout_TellsListenersTheHoverIsGone(bool clear)
    {
        var marina = Showing(SmallLayout());
        var berth = marina.GetBerths()[0];
        marina.Camera.SetPose(new CameraPose(new Vector3(berth.Center.X, 0, berth.Center.Y), 0f, 89f, 40f), immediate: true);
        marina.Input.PointerMove(400, 300);
        Assert.Equal(berth.Id, marina.HoveredBerth?.Id);

        var events = new List<Berth?>();
        marina.BerthHoverChanged += (_, e) => events.Add(e.Berth);
        if (clear) marina.ClearLayout();
        else marina.InitializeLayout(SmallLayout());

        Assert.Null(marina.HoveredBerth);
        Assert.Null(Assert.Single(events));
    }

    [Fact]
    public void AnEmptyDocument_ClearsEveryViewChoiceOfThePreviousDesign_EvenOnesNamingViewsThatAreGone()
    {
        var marina = Showing(SmallLayout());
        new MarinaDocument { Layout = SmallLayout(), DisabledCameraPresets = new[] { "Pier Z", "Top Down" } }.ApplyTo(marina);
        marina.SaveCameraPreset("Old view");
        marina.Designer.SetReferenceImage(Picture(), metersPerPixel: 0.5f);

        // What the designers' File > New does.
        new MarinaDocument { Name = "New marina" }.ApplyTo(marina, applyDesignerSettings: false);

        var captured = MarinaDocument.FromVisualizer(marina);
        Assert.Empty(captured.CameraPresets);
        Assert.Empty(captured.DisabledCameraPresets);
        Assert.All(marina.CameraPresets, preset => Assert.True(preset.IsBuiltIn && preset.IsEnabled));
        Assert.Null(marina.Designer.ReferenceImage);
        Assert.Equal("New marina", marina.MarinaName);
    }

    [Fact]
    public void AFileWithRepeatedPoints_StillOpens_WithTheRepeatsDropped()
    {
        var layout = SmallLayout() with
        {
            Shoreline = new Shoreline(new[] { new Vector2(-200, 60), new Vector2(0, 60), new Vector2(200, 60) }, landOnLeft: true),
        };
        var root = JsonNode.Parse(MarinaDocument.FromVisualizer(Showing(layout)).ToJson())!.AsObject();

        // Written before validation refused them: a doubled first point, a point a few millimetres from the next,
        // and an outline that repeats its first point at the end.
        var line = root["layout"]!["shoreline"]!["line"]!.AsArray();
        line.Insert(0, line[0]!.DeepClone());
        line.Insert(2, new JsonArray(0.004, 60.003));
        var outline = root["layout"]!["landAreas"]![0]!["outline"]!.AsArray();
        outline.Add(outline[0]!.DeepClone());
        outline.Insert(1, outline[1]!.DeepClone());

        var document = MarinaDocument.Parse(root.ToJsonString());
        Assert.Empty(document.Validate());

        var marina = new MarinaVisualizer();
        document.ApplyTo(marina);
        Assert.Equal(3, marina.Shoreline!.Points.Count);
        Assert.Equal(3, marina.GetLayout().LandAreas[0].Points.Count);
    }

    [Fact]
    public void AShorelineThatCollapsesToOnePoint_ReadsAsNoShoreline()
    {
        var layout = SmallLayout() with
        {
            Shoreline = new Shoreline(new[] { new Vector2(-200, 60), new Vector2(200, 60) }, landOnLeft: true),
        };
        var root = JsonNode.Parse(MarinaDocument.FromVisualizer(Showing(layout)).ToJson())!.AsObject();
        root["layout"]!["shoreline"]!["line"] = new JsonArray(new JsonArray(5, 5), new JsonArray(5, 5));

        Assert.Null(MarinaDocument.Parse(root.ToJsonString()).Layout.Shoreline);
    }
}
