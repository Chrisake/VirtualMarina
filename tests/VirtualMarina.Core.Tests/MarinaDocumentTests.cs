using System.Numerics;
using System.Text.Json;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
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
        marina.Designer.DividerType = DividerType.Boom;
        marina.Designer.DividerInterval = 3;
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
        Assert.NotEmpty(marina.GetMultiBerths());
        Assert.Empty(copy.GetMultiBerths()); // a design is saved empty: who is where is the host application's to set
        Assert.Equal(BerthLabelMode.OnlyFree, copy.BerthLabelMode);
        Assert.Equal(0.03f, copy.Style.Water.WaveAmplitude, 4);
        Assert.Equal(0.25f, copy.Style.Water.BoatMotion, 4);
        Assert.Equal("#00C853", copy.Style.Status.FreeColor.ToHex());
        Assert.False(copy.Style.Land.ShowTrees);
        Assert.Equal(180f, copy.Camera.DesiredPose.Distance, 2);
        Assert.Equal(DividerType.Boom, copy.Designer.DividerType);
        Assert.Equal(3, copy.Designer.DividerInterval);
        Assert.Equal(0.6f, copy.Designer.BerthGap, 3);
        Assert.Equal(PierServices.PowerAndWater, copy.Designer.BerthServices);

        // The shape of a berth survives; who is in it does not, because that is the host application's to set.
        var occupied = marina.GetBerthsByStatus(BerthStatus.Occupied)[0];
        var loaded = copy.GetBerth(occupied.Id)!;
        Assert.Equal(occupied.MaxDraft, loaded.MaxDraft);
        Assert.Equal(occupied.Metadata.Count, loaded.Metadata.Count);
        Assert.Equal(occupied.Center, loaded.Center);
        Assert.Equal(BerthStatus.Free, loaded.Status);
        Assert.Null(loaded.Boat);
        // isVisible belongs to berths alone, so its absence shows the runtime flags were left out. (A multi-berth
        // still writes its status and boat: it is the boat spanning the berths, and is invalid without one.)
        Assert.DoesNotContain("\"isVisible\"", json);
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
        Assert.Equal(DividerType.Piles, document.Designer.DividerType); // the old separator setting carries over to the divider tool
        Assert.Equal(4f, document.Designer.PierWidth);
        Assert.Empty(document.Validate());

        var marina = new MarinaVisualizer();
        document.ApplyTo(marina);
        Assert.Equal(2, marina.GetBerths().Count);
        Assert.Single(marina.GetMultiBerths());

        // Saving it again (with who is where) writes the current names, and that file reads back the same way.
        var again = MarinaDocument.Parse(document.ToJson(stripOccupancy: false));
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
    public void Document_SavesToBytes_AndLoadsThemBack()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());
        var original = MarinaDocument.FromVisualizer(marina, generator: "tests");
        Assert.Null(original.SavedUtc);

        var bytes = original.Save(indented: false);

        Assert.NotNull(original.SavedUtc);
        var document = MarinaDocument.Load(bytes);
        Assert.Equal("tests", document.Generator);
        Assert.Equal(original.SavedUtc, document.SavedUtc);
        Assert.Equal(marina.GetBerths().Count, document.Layout.Berths.Count);
        Assert.Equal(MarinaDocument.CurrentVersion, document.Version);
    }

    [Fact]
    public void LoadFromBytes_ReadsUtf16WithItsByteOrderMark_AsLoadingAFileDoes()
    {
        var json = new MarinaDocument { Name = "Sixteen" }.ToJson();
        var utf16 = System.Text.Encoding.Unicode.GetPreamble().Concat(System.Text.Encoding.Unicode.GetBytes(json)).ToArray();

        Assert.Equal("Sixteen", MarinaDocument.Load(utf16).Name);
    }

    [Fact]
    public void LoadFromBytes_KeepsTheNewerVersionGate()
    {
        var newer = System.Text.Encoding.UTF8.GetBytes(
            new MarinaDocument().ToJson().Replace($"\"{MarinaDocument.CurrentVersion}\"", "\"99.0\"", StringComparison.Ordinal));

        Assert.Throws<MarinaFormatException>(() => MarinaDocument.Load(newer));
        Assert.True(MarinaDocument.Load(newer, allowNewerVersion: true).IsFromNewerVersion);
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
            DividerType = DividerType.SinglePile,
            DividerInterval = 4,
            BerthGap = 1.2f,
            AlignBerthsToExisting = false,
            BerthServices = PierServices.Power,
            LandKind = LandKind.Grass,
            TreeDensity = 42f,
        };

        settings.ApplyTo(marina.Designer);
        Assert.Equal(9f, marina.Designer.BerthWidth);
        Assert.Equal(DividerType.SinglePile, marina.Designer.DividerType);
        Assert.Equal(4, marina.Designer.DividerInterval);
        Assert.False(marina.Designer.AlignBerthsToExisting);
        Assert.Equal(LandKind.Grass, marina.Designer.LandKind);
        Assert.Equal(settings, DesignerSettings.FromDesigner(marina.Designer));

        // A file with nonsense in it is clamped instead of throwing into the application.
        (settings with { BerthWidth = 5000f, TreeDensity = -3f, LandBerthHeading = float.NaN }).ApplyTo(marina.Designer);
        Assert.Equal(50f, marina.Designer.BerthWidth);
        Assert.Equal(0f, marina.Designer.TreeDensity);
        Assert.Equal(0f, marina.Designer.LandBerthHeading);
    }

    [Fact]
    public void HostMetadata_OnEveryElement_SurvivesASaveAndLoad()
    {
        var marina = new MarinaVisualizer();
        marina.AddLandArea(new LandArea("quay", new[] { new Vector2(0, 0), new Vector2(40, 0), new Vector2(40, 20), new Vector2(0, 20) }, 1.5f, LandKind.Quay)
        {
            Metadata = new Dictionary<string, string> { ["zone"] = "winter storage" },
        });
        marina.AddPier(new Pier("A", "Pier A", new Vector2(10, 20), 0f, 40f)
        {
            Metadata = new Dictionary<string, string> { ["erpId"] = "PONT-07" },
        });
        marina.AddDivider(new Divider("A-D1", new Vector2(8, 22), 90f, 9f, DividerType.Piles)
        {
            PierId = "A",
            Metadata = new Dictionary<string, string> { ["asset"] = "PILE-3" },
        });
        marina.AddBerth(new Berth("A-L01", "A", new Vector2(6, 26), -90f, 12f, 5f)
        {
            Metadata = new Dictionary<string, string> { ["contract"] = "2026-114" },
        });
        marina.AddBerth(new Berth("A-L02", "A", new Vector2(6, 32), -90f, 12f, 5f));
        marina.MoorAlongside(new[] { "A-L01", "A-L02" }, new Boat("B-1", "Meltemi", BoatType.MotorYacht));
        marina.UpdateMultiBerth(marina.GetMultiBerths()[0] with
        {
            Metadata = new Dictionary<string, string> { ["invoice"] = "INV-9" },
        });

        var reloaded = new MarinaVisualizer();
        MarinaDocument.Parse(MarinaDocument.FromVisualizer(marina).ToJson(stripOccupancy: false)).ApplyTo(reloaded);

        Assert.Equal("winter storage", reloaded.GetLandArea("quay")!.Metadata["zone"]);
        Assert.Equal("PONT-07", reloaded.GetPier("A")!.Metadata["erpId"]);
        Assert.Equal("PILE-3", reloaded.GetDivider("A-D1")!.Metadata["asset"]);
        Assert.Equal("2026-114", reloaded.GetBerth("A-L01")!.Metadata["contract"]);
        Assert.Equal("INV-9", reloaded.GetMultiBerths()[0].Metadata["invoice"]);

        // An element without metadata keeps an empty dictionary rather than null, and writes nothing to the file.
        Assert.Empty(reloaded.GetBerth("A-L02")!.Metadata);
        Assert.DoesNotContain("\"metadata\": {}", MarinaDocument.FromVisualizer(reloaded).ToJson());
    }

    [Fact]
    public void TheTracingImage_TravelsInTheFile_ButTheMeasuringLineDoesNot()
    {
        var marina = new MarinaVisualizer();
        var designer = marina.Designer;

        // A picture that still knows the file it came from is the kind that can be stored.
        var png = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3 };
        var image = new ReferenceImage(2, 2, new byte[2 * 2 * 4], png, "image/png");
        designer.SetReferenceImage(image, metersPerPixel: 0.25f, center: new Vector2(12, -34));
        designer.ReferenceImageOpacity = 0.4f;
        designer.ReferenceImageAboveScene = false;
        designer.CalibrateReferenceImage(new Vector2(0, 0), new Vector2(10, 0), 20f);
        Assert.NotNull(designer.ScaleLine);

        var reloaded = MarinaDocument.Parse(MarinaDocument.FromVisualizer(marina).ToJson());
        var copy = new MarinaVisualizer();
        reloaded.ApplyTo(copy);

        var stored = copy.Designer.ReferenceImage!;
        Assert.Equal(png, stored.EncodedData);
        Assert.Equal("image/png", stored.ContentType);
        Assert.Equal(2, stored.PixelWidth);
        Assert.Equal(designer.ReferenceImageMetersPerPixel, copy.Designer.ReferenceImageMetersPerPixel, 5);
        Assert.Equal(designer.ReferenceImageCenter, copy.Designer.ReferenceImageCenter);
        Assert.Equal(0.4f, copy.Designer.ReferenceImageOpacity, 3);
        Assert.False(copy.Designer.ReferenceImageAboveScene);

        // Scaffolding for calibrating, not part of the design.
        Assert.Null(copy.Designer.ScaleLine);
        Assert.DoesNotContain("scaleLine", MarinaDocument.FromVisualizer(marina).ToJson());
    }

    [Fact]
    public void APictureWithNoFileBehindIt_IsLeftOutRatherThanStoredAsPixels()
    {
        var marina = new MarinaVisualizer();
        marina.Designer.SetReferenceImage(new ReferenceImage(4, 4, new byte[4 * 4 * 4]), metersPerPixel: 1f);

        Assert.Null(MarinaDocument.FromVisualizer(marina).ReferenceImage);
    }

    [Fact]
    public void TheMeasuringLine_MovesWithThePictureAndCanBeCleared()
    {
        var marina = new MarinaVisualizer();
        var designer = marina.Designer;
        designer.SetReferenceImage(new ReferenceImage(4, 4, new byte[4 * 4 * 4]), metersPerPixel: 1f, center: Vector2.Zero);
        designer.CalibrateReferenceImage(new Vector2(0, 0), new Vector2(10, 0), 10f);

        var before = designer.ScaleLine!.Value;
        designer.ReferenceImageCenter += new Vector2(25, -8);

        var after = designer.ScaleLine.Value;
        Assert.Equal(before.Start + new Vector2(25, -8), after.Start);
        Assert.Equal(before.End + new Vector2(25, -8), after.End);

        Assert.True(designer.ClearScaleLine());
        Assert.Null(designer.ScaleLine);
        Assert.False(designer.ClearScaleLine());
    }

    [Fact]
    public void SavedViewpoints_TravelInTheFile_ButGeneratedOnesAreRebuilt()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -20), 0f, 40f));

        marina.Camera.SetPose(new CameraPose(new Vector3(10, 0, -5), 33f, 41f, 180f), immediate: true);
        var saved = marina.SaveCameraPreset("Entrance", "Looking in from the sea");
        Assert.Equal("Entrance", saved.Name);

        var generated = marina.CameraPresets.Count(p => p.IsBuiltIn);
        Assert.True(generated > 0);

        var document = MarinaDocument.Parse(MarinaDocument.FromVisualizer(marina).ToJson());

        // Only what a person saved is stored; the rest is rebuilt from the piers.
        var stored = Assert.Single(document.CameraPresets);
        Assert.Equal("Entrance", stored.Name);
        Assert.Equal("Looking in from the sea", stored.Description);
        Assert.Equal(33f, stored.Pose.YawDegrees, 2);

        var copy = new MarinaVisualizer();
        document.ApplyTo(copy);
        Assert.Contains(copy.CameraPresets, p => p is { Name: "Entrance", IsBuiltIn: false });
        Assert.Equal(generated, copy.CameraPresets.Count(p => p.IsBuiltIn));
        Assert.True(copy.ApplyCameraPreset("Entrance", immediate: true));
    }

    [Fact]
    public void FocusPier_FramesTheWholePierAndItsBerths()
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(1000, 800);
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        var designer = marina.Designer;
        designer.IsActive = true;
        var berths = designer.CreateBerths("A", PierSide.Left, 0f, 40f);
        Assert.NotEmpty(berths);

        Assert.True(marina.FocusPier("A", CameraAngle.TopDown, immediate: true));

        var pose = marina.Camera.DesiredPose;
        Assert.Equal(89f, pose.PitchDegrees, 1);

        // The target sits on the pier, and the view is wide enough to hold the pier and the berths beside it.
        Assert.InRange(pose.Target.Z, -32f, 32f);
        Assert.True(pose.Distance > 60f, $"expected the whole pier framed, got {pose.Distance:0} m");

        Assert.False(marina.FocusPier("nope", CameraAngle.TopDown));
    }

    [Fact]
    public void ScatteredTrees_AreMostlyOrdinary_WithTheOccasionalCherry()
    {
        // About 800 trees on 4 ha: a large enough sample for the proportions below, loose enough that the generator
        // is not spending most of its attempts looking for the last free gaps (the spacing check is quadratic).
        var outline = new[] { new Vector2(0, 0), new Vector2(200, 0), new Vector2(200, 200), new Vector2(0, 200) };
        var trees = LandArea.GenerateTrees(outline, 20f, new Random(7));
        Assert.True(trees.Count > 500, $"expected a decent sample, got {trees.Count}");

        var kinds = trees.GroupBy(t => t.Shape).ToDictionary(g => g.Key, g => g.Count());
        Assert.True(kinds.GetValueOrDefault(TreeShape.Broadleaf) > kinds.GetValueOrDefault(TreeShape.Conifer));
        Assert.Contains(TreeShape.Cypress, kinds.Keys);
        Assert.Contains(TreeShape.Palm, kinds.Keys);

        // The cherry is meant to be a surprise, not a feature of the landscape.
        var cherries = kinds.GetValueOrDefault(TreeShape.Cherry);
        Assert.True(cherries * 20 < trees.Count, $"{cherries} cherries out of {trees.Count} is not rare");
    }

    [Fact]
    public void EveryLabelFace_HasItsOwnGlyphsAndWidth()
    {
        var ids = new HashSet<int>();
        foreach (var font in Enum.GetValues<LabelFont>())
        {
            Assert.True(GlyphFont.TryGetMeshId('A', font, out var meshId));
            Assert.True(ids.Add(meshId), $"{font} shares its glyphs with another face");
        }

        // Condensed runs narrower than regular, wide runs broader.
        var regular = GlyphFont.MeasureWidth(5, LabelFont.Regular);
        Assert.True(GlyphFont.MeasureWidth(5, LabelFont.Condensed) < regular);
        Assert.True(GlyphFont.MeasureWidth(5, LabelFont.Wide) > regular);

        // Every face's glyphs are actually built.
        var built = GlyphFont.CreateAll().Select(mesh => mesh.Id).ToHashSet();
        Assert.True(GlyphFont.TryGetMeshId('A', LabelFont.Bold, out var bold));
        Assert.Contains(bold, built);
    }

    [Fact]
    public void TheLabelFace_AndTheNewTreeColours_SurviveASaveAndLoad()
    {
        var marina = new MarinaVisualizer();
        marina.Style.Labels.FontFamily = LabelFont.Condensed;
        marina.Style.Land.BlossomColor = new ColorRgba(0.9f, 0.5f, 0.6f);

        var copy = new MarinaVisualizer();
        MarinaDocument.Parse(MarinaDocument.FromVisualizer(marina).ToJson()).ApplyTo(copy);

        Assert.Equal(LabelFont.Condensed, copy.Style.Labels.FontFamily);
        Assert.Equal("#E68099", copy.Style.Land.BlossomColor.ToHex());
    }

    [Fact]
    public void WaterSettings_SurviveASaveAndLoad_AndTheFrameCarriesWhereTheWaterIs()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(140, 60), 0f, 40f));
        marina.Style.Water.Ripples = 0.8f;
        marina.Style.Water.SunGlints = 1.4f;

        // The water shader needs to know where the detailed grid is, so it can flatten the sea beyond it; the middle of
        // the marina is there for custom backends.
        var frame = marina.BuildRenderFrame();
        Assert.Equal(new Vector2(140, 80), frame.MarinaCenter);
        Assert.True(frame.WaterDetailRadius > 0f, "the water has no detail radius to fade over");
        Assert.Equal(marina.Water.Size * 0.5f, frame.WaterDetailRadius, 1);

        var copy = new MarinaVisualizer();
        MarinaDocument.Parse(MarinaDocument.FromVisualizer(marina).ToJson()).ApplyTo(copy);

        Assert.Equal(0.8f, copy.Style.Water.Ripples, 3);
        Assert.Equal(1.4f, copy.Style.Water.SunGlints, 3);
    }
}
