using System.Numerics;
using System.Text.Json;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Rendering;
using VirtualMarina.Core.Serialization;
using VirtualMarina.SampleData;

namespace VirtualMarina.Core.Tests;

/// <summary>The marina file format: what the designer writes and the host application loads.</summary>
public class MarinaDocumentTests
{
    [Fact]
    public void Document_RoundTripsAWholeMarina_ThroughJson()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());
        marina.BerthLabelMode = BerthLabelMode.OnlyFree;
        marina.Style.Water.WaveAmplitude = 0.03f;
        marina.Style.Water.BoatMotion = 0.25f;
        marina.Style.Status.FreeColor = ColorRgba.FromHex("#00C853");
        marina.Style.Land.ShowTrees = false;
        marina.Camera.SetPose(new CameraPose(new Vector3(12, 0, -40), 33f, 41f, 180f), immediate: true);
        marina.Designer.BerthSeparators = BerthSeparator.PairedFingerPiers;
        marina.Designer.BerthGap = 0.6f;
        marina.Designer.BerthServices = PierServices.PowerAndWater;

        var json = MarinaDocument.FromVisualizer(marina, generator: "tests").ToJson();
        Assert.Contains("\"format\": \"virtualmarina.marina\"", json);
        Assert.Contains("\"formatVersion\": \"2.0\"", json);

        var reloaded = MarinaDocument.Parse(json);
        var copy = new MarinaVisualizer();
        reloaded.ApplyTo(copy);

        Assert.Equal(marina.MarinaName, copy.MarinaName);
        Assert.Equal(marina.GetBerths().Count, copy.GetBerths().Count);
        Assert.Equal(marina.GetPiers().Select(d => d.Id), copy.GetPiers().Select(d => d.Id));
        Assert.Equal(marina.GetDividers().Select(d => d.Id), copy.GetDividers().Select(d => d.Id));
        Assert.Equal(marina.GetLandAreas().Sum(l => l.Trees.Count), copy.GetLandAreas().Sum(l => l.Trees.Count));
        Assert.Equal(marina.GetMultiBerths().Select(b => b.Id), copy.GetMultiBerths().Select(b => b.Id));
        Assert.Equal(BerthLabelMode.OnlyFree, copy.BerthLabelMode);
        Assert.Equal(0.03f, copy.Style.Water.WaveAmplitude, 4);
        Assert.Equal(0.25f, copy.Style.Water.BoatMotion, 4);
        Assert.Equal("#00C853", copy.Style.Status.FreeColor.ToHex());
        Assert.False(copy.Style.Land.ShowTrees);
        Assert.Equal(180f, copy.Camera.DesiredPose.Distance, 2);
        Assert.Equal(BerthSeparator.PairedFingerPiers, copy.Designer.BerthSeparators);
        Assert.Equal(0.6f, copy.Designer.BerthGap, 3);
        Assert.Equal(PierServices.PowerAndWater, copy.Designer.BerthServices);

        // Everything that matters for a berth desk survives: status, boat, labels, land berths and pedestals.
        var occupied = marina.GetBerthsByStatus(BerthStatus.Occupied)[0];
        var loaded = copy.GetBerth(occupied.Id)!;
        Assert.Equal(occupied.Boat!.Name, loaded.Boat!.Name);
        Assert.Equal(occupied.Boat.Type, loaded.Boat.Type);
        Assert.Equal(occupied.Boat.LengthMeters, loaded.Boat.LengthMeters, 3);
        Assert.Equal(occupied.MaxDraft, loaded.MaxDraft);
        Assert.Equal(occupied.Metadata.Count, loaded.Metadata.Count);
        Assert.Equal(marina.GetBerthsByLandArea(MockMarinaFactory.BoatyardId).Count, copy.GetBerthsByLandArea(MockMarinaFactory.BoatyardId).Count);
        Assert.Equal(marina.GetPier("E")!.Services, copy.GetPier("E")!.Services);
    }

    [Fact]
    public void Document_WritesReadableJson_WithCompactPointsAndHexColors()
    {
        var document = new MarinaDocument
        {
            Name = "Harbor",
            Layout = new MarinaLayoutBuilder("Harbor")
                .AddLandArea(new LandArea("quay", new[] { new Vector2(-10, -10), new Vector2(10, -10), new Vector2(10, 10) }, 1.5f))
                .AddPier("A", "Pier A", new Vector2(0, 0), 90f, 30f, pier => pier.AddBerths(PierSide.Left, 2, 5f, 12f))
                .Build(),
        };

        var json = document.ToJson();
        Assert.Contains("\"outline\": [", json);
        Assert.Contains("[-10, -10]", json);
        Assert.Contains("\"kind\": \"Quay\"", json);
        Assert.Contains("\"free\": \"#", json);
        Assert.DoesNotContain("\"multiBerths\"", json); // empty collections are left out

        var reloaded = MarinaDocument.Parse(json);
        Assert.Equal("Harbor", reloaded.Name);
        Assert.Equal(1.5f, reloaded.Layout.LandAreas[0].Height);
        Assert.Equal(3, reloaded.Layout.LandAreas[0].Points.Count);
        Assert.Equal(2, reloaded.Layout.Berths.Count);
        Assert.Empty(reloaded.Validate());
    }

    [Fact]
    public void Document_FromFormat1_ReadsDocksAsPiersAndSlipsAsBerths()
    {
        // Written before piers and berths had their names: docks, slips, and "berths" for the multi-berth groups.
        const string json = """
            {
              "format": "virtualmarina.marina",
              "formatVersion": "1.0",
              "marina": { "name": "Old Harbor" },
              "layout": {
                "docks": [ { "id": "A", "name": "Dock A", "start": [0, 0], "headingDegrees": 0, "length": 40, "width": 3, "services": "PowerAndWater" } ],
                "slips": [
                  { "id": "A-1", "dockId": "A", "center": [6, 10], "headingDegrees": -90, "length": 12, "width": 5, "status": "Occupied",
                    "boat": { "id": "B-1", "name": "Aurora", "type": "MotorYacht" } },
                  { "id": "A-2", "dockId": "A", "center": [6, 16], "headingDegrees": -90, "length": 12, "width": 5, "status": "Occupied",
                    "boat": { "id": "B-1", "name": "Aurora", "type": "MotorYacht" } }
                ],
                "dividers": [ { "id": "A-D1", "dockId": "A", "start": [1.5, 8], "headingDegrees": 90, "length": 9, "type": "Piles" } ],
                "berths": [ { "id": "VIP", "slipIds": ["A-1", "A-2"], "style": "Alongside", "status": "Occupied",
                              "boat": { "id": "B-1", "name": "Aurora", "type": "MotorYacht" } } ]
              },
              "presentation": { "slipLabels": "All" },
              "designer": { "slipWidth": 6.5, "slipSeparators": "Piles", "dockWidth": 4 }
            }
            """;

        var document = MarinaDocument.Parse(json);
        Assert.Equal(new Version(1, 0), document.Version);
        Assert.Equal("Dock A", document.Layout.Piers[0].Name);
        Assert.Equal(PierServices.PowerAndWater, document.Layout.Piers[0].Services);
        Assert.Equal(2, document.Layout.Berths.Count);
        Assert.Equal("A", document.Layout.Berths[0].PierId);
        Assert.Equal("A", document.Layout.Dividers[0].PierId);
        Assert.Equal(new[] { "A-1", "A-2" }, Assert.Single(document.Layout.MultiBerths).BerthIds);
        Assert.Equal(BerthLabelMode.All, document.BerthLabels);
        Assert.Equal(6.5f, document.Designer!.BerthWidth);
        Assert.Equal(BerthSeparator.Piles, document.Designer.BerthSeparators);
        Assert.Equal(4f, document.Designer.PierWidth);
        Assert.Empty(document.Validate());

        var marina = new MarinaVisualizer();
        document.ApplyTo(marina);
        Assert.Equal(2, marina.GetBerths().Count);
        Assert.Single(marina.GetMultiBerths());

        // Saving it again writes the current names, and that file reads back the same way.
        var again = MarinaDocument.Parse(document.ToJson());
        Assert.Equal(MarinaDocument.CurrentVersion, again.Version);
        Assert.Equal(2, again.Layout.Berths.Count);
        Assert.Single(again.Layout.MultiBerths);
        Assert.Equal("A", again.Layout.Berths[0].PierId);
    }

    [Fact]
    public void Document_WithOnlyTheBareMinimum_LoadsWithDefaults()
    {
        const string json = """
            {
              "format": "virtualmarina.marina",
              "formatVersion": "2.0",
              "marina": { "name": "Old Harbor" },
              "layout": {
                "piers": [ { "id": "A", "start": [0, 0], "headingDegrees": 0, "length": 40, "width": 3 } ],
                "berths": [ { "id": "A-1", "pierId": "A", "center": [6, 10], "headingDegrees": -90, "length": 12, "width": 5 } ]
              }
            }
            """;

        var document = MarinaDocument.Parse(json);
        Assert.Equal(new Version(2, 0), document.Version);
        Assert.False(document.IsFromNewerVersion);
        Assert.Equal("Old Harbor", document.Name);
        Assert.Equal(PierType.FloatingWooden, document.Layout.Piers[0].Type);
        Assert.Equal(PierSides.Both, document.Layout.Piers[0].BerthingSides);
        Assert.Equal(PierServices.None, document.Layout.Piers[0].Services);
        Assert.Equal(BerthStatus.Free, document.Layout.Berths[0].Status);
        Assert.True(document.Layout.Berths[0].IsVisible);
        Assert.Equal(0.08f, document.Style.Water.WaveAmplitude, 4);
        Assert.Null(document.Camera);
        Assert.Empty(document.Validate());

        var marina = new MarinaVisualizer();
        document.ApplyTo(marina);
        Assert.Single(marina.GetBerths());
    }

    [Fact]
    public void Document_FromANewerVersion_KeepsWhatItCannotUnderstand()
    {
        const string json = """
            {
              "format": "virtualmarina.marina",
              "formatVersion": "2.7",
              "marina": { "name": "Future Harbor" },
              "tideSimulation": { "rangeMeters": 1.8, "periodHours": 12.4 },
              "layout": {
                "piers": [ {
                  "id": "A", "start": [0, 0], "headingDegrees": 0, "length": 40, "width": 3,
                  "type": "FloatingWooden", "services": "PowerAndWaterAndFuel",
                  "solarLighting": true
                } ],
                "berths": [ { "id": "A-1", "pierId": "A", "center": [6, 10], "headingDegrees": -90, "length": 12, "width": 5, "hullCleaning": "monthly" } ]
              },
              "presentation": { "water": { "waveAmplitude": 0.2, "seaweed": 0.5 } }
            }
            """;

        var document = MarinaDocument.Parse(json);
        Assert.True(document.IsFromNewerVersion);
        Assert.Equal(new Version(2, 7), document.Version);

        // An unknown enum name falls back to the default instead of refusing the file.
        Assert.Equal(PierServices.None, document.Layout.Piers[0].Services);
        Assert.Equal(0.2f, document.Style.Water.WaveAmplitude, 4);

        // ... and everything unknown is still there after a save, so an older application does not silently drop it.
        var rewritten = document.ToJson();
        Assert.Contains("\"tideSimulation\"", rewritten);
        Assert.Contains("\"rangeMeters\"", rewritten);
        Assert.Contains("\"solarLighting\"", rewritten);
        Assert.Contains("\"hullCleaning\"", rewritten);
        Assert.Contains("\"seaweed\"", rewritten);

        var again = MarinaDocument.Parse(rewritten);
        Assert.Equal("Future Harbor", again.Name);
        Assert.Equal(1.8f, again.Extensions["tideSimulation"].GetProperty("rangeMeters").GetSingle(), 3);
    }

    [Fact]
    public void Document_FromANewerMajorVersion_IsRefusedUnlessForced()
    {
        const string json = """{ "format": "virtualmarina.marina", "formatVersion": "3.0", "marina": { "name": "Next" } }""";

        var error = Assert.Throws<MarinaFormatException>(() => MarinaDocument.Parse(json));
        Assert.Contains("3.0", error.Message);

        var forced = MarinaDocument.Parse(json, allowNewerVersion: true);
        Assert.Equal("Next", forced.Name);
    }

    [Fact]
    public void Document_RefusesFilesThatAreNotMarinaDesigns()
    {
        Assert.Throws<MarinaFormatException>(() => MarinaDocument.Parse("not json at all"));
        Assert.Throws<MarinaFormatException>(() => MarinaDocument.Parse("""{ "format": "something.else", "layout": {} }"""));

        // A JSON document without a format marker is accepted: it may be hand-written.
        Assert.Equal("Marina", MarinaDocument.Parse("{}").Name);
    }

    [Fact]
    public void Document_StoresHostDataUnderItsOwnKey()
    {
        var document = new MarinaDocument { Name = "Harbor" };
        document.SetExtension("acme.erp", new Dictionary<string, string> { ["site"] = "42", ["tariff"] = "summer" });

        var reloaded = MarinaDocument.Parse(document.ToJson());
        var data = reloaded.GetExtension<Dictionary<string, string>>("acme.erp")!;
        Assert.Equal("42", data["site"]);
        Assert.Equal("summer", data["tariff"]);
        Assert.Null(reloaded.GetExtension<Dictionary<string, string>>("missing"));
    }

    [Fact]
    public void Document_SavesAndLoadsAFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vm-{Guid.NewGuid():N}{MarinaDocument.FileExtension}");
        try
        {
            var marina = new MarinaVisualizer();
            marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());
            MarinaDocument.FromVisualizer(marina, generator: "tests").Save(path);

            var document = MarinaDocument.Load(path);
            Assert.Equal("tests", document.Generator);
            Assert.NotNull(document.SavedUtc);
            Assert.Equal(marina.GetBerths().Count, document.Layout.Berths.Count);
            Assert.Equal(MarinaDocument.CurrentVersion, document.Version);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DesignerSettings_RoundTripAndClampOutOfRangeValues()
    {
        var marina = new MarinaVisualizer();
        var settings = DesignerSettings.FromDesigner(marina.Designer) with
        {
            BerthWidth = 9f,
            BerthLength = 28f,
            BerthDepth = 4.5f,
            BerthSeparators = BerthSeparator.SinglePile,
            BerthGap = 1.2f,
            AlignBerthsToExisting = false,
            BerthServices = PierServices.Power,
            LandKind = LandKind.Grass,
            TreeDensity = 42f,
        };

        settings.ApplyTo(marina.Designer);
        Assert.Equal(9f, marina.Designer.BerthWidth);
        Assert.Equal(BerthSeparator.SinglePile, marina.Designer.BerthSeparators);
        Assert.False(marina.Designer.AlignBerthsToExisting);
        Assert.Equal(LandKind.Grass, marina.Designer.LandKind);
        Assert.Equal(settings, DesignerSettings.FromDesigner(marina.Designer));

        // A file with nonsense in it is clamped instead of throwing into the application.
        (settings with { BerthWidth = 5000f, TreeDensity = -3f, LandBerthHeading = float.NaN }).ApplyTo(marina.Designer);
        Assert.Equal(50f, marina.Designer.BerthWidth);
        Assert.Equal(0f, marina.Designer.TreeDensity);
        Assert.Equal(0f, marina.Designer.LandBerthHeading);
    }
}
