using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Rendering;
using VirtualMarina.Core.Serialization;

namespace VirtualMarina.Core.Tests;

/// <summary>Host data for the typed extension overloads, serialized through <see cref="FormatTestJsonContext"/>.</summary>
/// <param name="Site">A site code.</param>
/// <param name="Tariff">A tariff number.</param>
public sealed record ErpSettings(string Site, int Tariff);

/// <summary>A host's own source-generated context, as a trimmed application would have.</summary>
[JsonSerializable(typeof(ErpSettings))]
internal sealed partial class FormatTestJsonContext : JsonSerializerContext
{
}

/// <summary>
/// How a marina file is read and written: defaults owned by the domain, the migration of older files, the tracing picture as
/// bytes, the UTF-8 and stream entry points, safe saving, the JSON options, and files holding nulls where they should not.
/// </summary>
public class MarinaDocumentFormatTests
{
    private const string Header = "\"format\": \"virtualmarina.marina\", \"formatVersion\": \"2.0\"";

    [Fact]
    public void SettingsAFileLeavesOut_TakeTheDomainsDefaults()
    {
        var document = MarinaDocument.Parse($$"""
            {
              {{Header}},
              "layout": {
                "shoreline": { "line": [[-100, 0], [100, 0]] },
                "piers": [ { "id": "A", "start": [0, 0], "length": 10 } ],
                "dividers": [ { "id": "D", "start": [0, 0], "length": 4 } ],
                "marineTraffic": { "enabled": true }
              },
              "presentation": { "water": {}, "lighting": {}, "status": {}, "land": {}, "structures": {}, "labels": {}, "selection": {}, "view": {}, "shadows": {} },
              "designer": {}
            }
            """);

        StyleProperties.AssertSame(new MarinaStyle(), document.Style);

        var shore = document.Layout.Shoreline!;
        var fresh = new Shoreline(shore.Points, shore.LandOnLeft);
        Assert.Equal(fresh.Kind, shore.Kind);
        Assert.Equal(fresh.Scenery, shore.Scenery);
        Assert.Equal(fresh.Height, shore.Height);

        var pier = document.Layout.Piers[0];
        Assert.Equal(new Pier("A", "A", Vector2.Zero, 0f, 10f).PilingSpacing, pier.PilingSpacing);
        Assert.Equal(PierSides.Both, pier.BerthingSides);
        Assert.Equal(new Divider("D", Vector2.Zero, 0f, 4f).Spacing, document.Layout.Dividers[0].Spacing);

        var traffic = document.Layout.MarineTraffic!;
        Assert.Equal(MarineTraffic.None with { IsEnabled = true }, traffic);
        Assert.Equal(new DesignerSettings(), document.Designer);
    }

    [Theory]
    [InlineData("")]                                   // no version at all
    [InlineData("\"formatVersion\": \"2.0\",")]        // claims to be current, but uses the old names
    [InlineData("\"formatVersion\": \"garbled\",")]    // unreadable version
    [InlineData("\"formatVersion\": \"1.0\",")]
    public void AnOlderFile_IsRecognisedByItsShape_WhateverVersionItClaims(string version)
    {
        var document = MarinaDocument.Parse($$"""
            {
              "format": "virtualmarina.marina", {{version}}
              "layout": {
                "docks": [ { "id": "A", "start": [0, 0], "headingDegrees": 0, "length": 40, "width": 3 } ],
                "slips": [
                  { "id": "A-1", "dockId": "A", "center": [6, 10], "headingDegrees": -90, "length": 12, "width": 5 },
                  { "id": "A-2", "dockId": "A", "center": [6, 16], "headingDegrees": -90, "length": 12, "width": 5 }
                ],
                "berths": [ { "id": "VIP", "slipIds": ["A-1", "A-2"], "status": "Reserved", "boat": { "id": "B", "type": "MotorYacht" } } ]
              }
            }
            """);

        Assert.Equal(new[] { "A-1", "A-2" }, document.Layout.Berths.Select(b => b.Id));
        Assert.Equal("A", document.Layout.Berths[0].PierId);
        var group = Assert.Single(document.Layout.MultiBerths);
        Assert.Equal(new[] { "A-1", "A-2" }, group.BerthIds);
        Assert.Equal(BerthStatus.Reserved, group.Status);
        Assert.Empty(document.Validate());

        var written = document.ToJson();
        Assert.DoesNotContain("slip", written, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dock", written, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ACurrentFile_KeepsItsBerthsAsBerths()
    {
        var document = MarinaDocument.Parse($$"""
            {
              {{Header}},
              "layout": {
                "piers": [ { "id": "A", "start": [0, 0], "length": 40 } ],
                "berths": [ { "id": "A-1", "pierId": "A", "center": [6, 10], "length": 12, "width": 5 } ]
              }
            }
            """);

        Assert.Single(document.Layout.Berths);
        Assert.Empty(document.Layout.MultiBerths);
        Assert.Equal(new Version(2, 0), document.Version);
    }

    [Fact]
    public void TheMigrationSteps_RunInOrder_AndEndAtTheCurrentVersion()
    {
        var steps = MarinaMigrations.Steps;
        Assert.NotEmpty(steps);
        for (var i = 1; i < steps.Count; i++) Assert.Equal(steps[i - 1].To, steps[i].From);
        Assert.Equal(MarinaDocument.CurrentVersion, steps[^1].To);
    }

    [Fact]
    public void TheTracingPicture_TravelsAsBase64_AndIsReadStraightIntoBytes()
    {
        var bytes = Enumerable.Range(0, 3000).Select(i => (byte)(i * 7)).ToArray();
        var document = new MarinaDocument
        {
            ReferenceImage = new ReferenceImageRecord { Image = ReferenceImage.FromEncoded(bytes, 40, 30, "image/jpeg"), Opacity = 0.3f },
        };

        var json = document.ToJson();
        Assert.Contains(Convert.ToBase64String(bytes), json, StringComparison.Ordinal);
        var reread = MarinaDocument.Parse(Encoding.UTF8.GetBytes(json));
        Assert.Equal(bytes, reread.ReferenceImage!.Image.EncodedData);
        Assert.Equal(0.3f, reread.ReferenceImage.Opacity);

        // Wrapped base64, as a hand-edited file may have, still reads; garbage reads as no picture and the rest still loads.
        var node = JsonNode.Parse(json)!;
        var wrapped = Convert.ToBase64String(bytes, Base64FormattingOptions.InsertLineBreaks);
        node["referenceImage"]!["data"] = wrapped;
        Assert.Equal(bytes, MarinaDocument.Parse(node.ToJsonString()).ReferenceImage!.Image.EncodedData);

        node["referenceImage"]!["data"] = "!!not base64!!";
        node["marina"] = new JsonObject { ["name"] = "Still here" };
        var damaged = MarinaDocument.Parse(node.ToJsonString());
        Assert.Null(damaged.ReferenceImage);
        Assert.Equal("Still here", damaged.Name);
    }

    [Fact]
    public async Task BytesAndStreams_ReadAndWriteTheSameFile()
    {
        var document = Sample();
        document.SavedUtc = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        Assert.Equal(Encoding.UTF8.GetBytes(document.ToJson()), document.ToUtf8Bytes());

        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(document.ToUtf8Bytes()).ToArray();
        Assert.Equal(document.Layout.Piers, MarinaDocument.Parse(withBom).Layout.Piers);

        using var stream = new MemoryStream();
        await document.SaveAsync(stream, indented: false, stripOccupancy: false);
        Assert.True(document.SavedUtc > new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        stream.Position = 0;
        var reread = await MarinaDocument.LoadAsync(stream);
        Assert.Equal(document.Layout.Piers, reread.Layout.Piers);
        Assert.Equal(document.Layout.MultiBerths, reread.Layout.MultiBerths);
        Assert.Equal(document.SavedUtc, reread.SavedUtc);
    }

    [Fact]
    public void Save_ReplacesTheFileInOneStep_AndStampsTheTimeOnlyWhenItWorked()
    {
        var folder = Directory.CreateTempSubdirectory("marina-save-");
        try
        {
            var path = Path.Combine(folder.FullName, "harbor.marina.json");
            File.WriteAllText(path, "old contents");
            var document = Sample();
            document.Save(path);
            Assert.NotNull(document.SavedUtc);
            Assert.Equal(document.Layout.Piers, MarinaDocument.Load(path).Layout.Piers);
            Assert.Equal(new[] { path }, Directory.GetFiles(folder.FullName));

            var before = document.SavedUtc;
            Assert.ThrowsAny<IOException>(() => document.Save(Path.Combine(folder.FullName, "missing", "harbor.marina.json")));
            Assert.Equal(before, document.SavedUtc);
            Assert.Equal(new[] { path }, Directory.GetFiles(folder.FullName));

            // A file saved as UTF-16 by an editor still opens.
            File.WriteAllText(path, document.ToJson(), Encoding.Unicode);
            Assert.Equal(document.Layout.Piers, MarinaDocument.Load(path).Layout.Piers);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheSharedOptions_AreLocked_AndCreateOptions_GivesAFreshOneForHostTypes()
    {
        Assert.True(MarinaJson.Options.IsReadOnly);
        Assert.True(MarinaJson.CompactOptions.IsReadOnly);
        Assert.ThrowsAny<NotSupportedException>(() => JsonSerializer.Serialize(new ErpSettings("x", 1), MarinaJson.Options));

        var options = MarinaJson.CreateOptions(FormatTestJsonContext.Default, indented: false);
        Assert.False(options.IsReadOnly);
        Assert.NotSame(options, MarinaJson.CreateOptions(FormatTestJsonContext.Default));
        Assert.Equal("{\"site\":\"x\",\"tariff\":1}", JsonSerializer.Serialize(new ErpSettings("x", 1), options));

        var reflection = MarinaJson.CreateOptions(new DefaultJsonTypeInfoResolver());
        Assert.Equal("[1, 2]", JsonSerializer.Serialize(new Vector2(1, 2), reflection));
    }

    [Fact]
    public void Extensions_CanBeWrittenWithTheHostsOwnMetadata()
    {
        var context = new FormatTestJsonContext(MarinaJson.CreateOptions());
        var document = new MarinaDocument();
        document.SetExtension("acme.erp", new ErpSettings("S1", 4), context.ErpSettings);
        Assert.Contains("\"site\": \"S1\"", document.ToJson(), StringComparison.Ordinal);

        var reread = MarinaDocument.Parse(document.ToJson());
        Assert.Equal(new ErpSettings("S1", 4), reread.GetExtension("acme.erp", context.ErpSettings));
        Assert.Null(reread.GetExtension("missing", context.ErpSettings));
        document.SetExtension<ErpSettings>("acme.erp", null, context.ErpSettings);
        Assert.Empty(document.Extensions);
    }

    [Fact]
    public void ViewsWithTheSameName_EachKeepWhatANewerVersionStoredOnThem()
    {
        var document = MarinaDocument.Parse($$"""
            {
              {{Header}},
              "cameraPresets": [
                { "name": "View", "target": [0, 0, 0], "tilt": 1 },
                { "name": "View", "target": [5, 0, 0], "tilt": 2 }
              ]
            }
            """);

        var written = JsonNode.Parse(document.ToJson())!["cameraPresets"]!.AsArray();
        Assert.Equal(1, (int)written[0]!["tilt"]!);
        Assert.Equal(2, (int)written[1]!["tilt"]!);
    }

    [Fact]
    public void AMultiBerthCarriesItsBoat_ASingleBerthDoesNot_AndABoatsSizeIsOnlyWrittenWhenItHasOne()
    {
        var document = Sample();
        var json = JsonNode.Parse(document.ToJson(stripOccupancy: false))!;
        var layout = json["layout"]!;
        Assert.Null(layout["berths"]![0]!["boat"]);
        Assert.Null(layout["berths"]![0]!["status"]);
        var boat = layout["multiBerths"]![0]!["boat"]!;
        Assert.Equal("Reserved", (string?)layout["multiBerths"]![0]!["status"]);
        Assert.Null(boat["lengthMeters"]);
        Assert.Equal(4.5f, (float)boat["beamMeters"]!);

        var reread = MarinaDocument.Parse(document.ToJson(stripOccupancy: false)).Layout.MultiBerths[0].Boat;
        Assert.False(reread.HasCustomLength);
        Assert.True(reread.HasCustomBeam);
        Assert.Equal(document.Layout.MultiBerths[0].Boat, reread);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{ \"layout\": null, \"presentation\": null, \"cameraPresets\": null }")]
    [InlineData("{ \"layout\": { \"landAreas\": [null], \"piers\": [null], \"dividers\": [null], \"berths\": [null], \"multiBerths\": [null] } }")]
    [InlineData("{ \"layout\": { \"landAreas\": [ { \"id\": \"L\", \"outline\": [[0,0],[5,0],[5,5]], \"trees\": [null, { \"position\": [1,1] }] } ] } }")]
    [InlineData("{ \"layout\": { \"landAreas\": [ { \"id\": null, \"outline\": null } ], \"piers\": [ { \"id\": null, \"name\": null } ] } }")]
    [InlineData("{ \"layout\": { \"berths\": [ { \"id\": \" \", \"pierId\": null, \"landAreaId\": \" \" } ], \"dividers\": [ { \"id\": null } ] } }")]
    [InlineData("{ \"layout\": { \"multiBerths\": [ { \"id\": null, \"berthIds\": [null, \"a\"], \"boat\": { \"id\": null, \"name\": null } } ] } }")]
    [InlineData("{ \"cameraPresets\": [null, { \"name\": null }], \"disabledCameraPresets\": [null, \"Top Down\"] }")]
    [InlineData("{ \"presentation\": { \"labels\": { \"fontName\": \"F\", \"fontGlyphs\": [null, \"A 1\"] } } }")]
    [InlineData("{ \"layout\": { \"slips\": [null], \"docks\": [null], \"berths\": [null, { \"slipIds\": null }] } }")]
    [InlineData("{ \"layout\": { \"piers\": [ { \"id\": \"A\", \"type\": null, \"length\": null } ] } }")]
    public void AFileWithNullsWhereTheyDoNotBelong_FailsOnlyAsAFormatError(string json)
    {
        try
        {
            var document = MarinaDocument.Parse(json);
            _ = document.Validate();
            _ = document.ToJson();
        }
        catch (MarinaFormatException)
        {
            // The one exception reading may throw.
        }
    }

    [Fact]
    public void NullEntriesAreDropped_AndNullIdsReadAsMissing()
    {
        var document = MarinaDocument.Parse("""
            {
              "layout": {
                "piers": [ null, { "id": null, "start": [0, 0], "length": 20 } ],
                "berths": [ null, { "id": "A-1", "pierId": null, "center": [3, 3], "length": 10, "width": 4 } ],
                "landAreas": [ { "id": "L", "outline": [[0,0],[5,0],[5,5]], "trees": [null, { "position": [1,1], "height": 5, "crownRadius": 1 }] } ]
              },
              "cameraPresets": [ null, { "name": null, "target": [0, 0, 0] } ]
            }
            """);

        Assert.Equal("pier", Assert.Single(document.Layout.Piers).Id);
        Assert.Equal("pier", Assert.Single(document.Layout.Berths).PierId);
        Assert.Single(document.Layout.LandAreas[0].Trees);
        Assert.Equal("View", Assert.Single(document.CameraPresets).Name);
    }

    private static MarinaDocument Sample()
    {
        var layout = new MarinaLayoutBuilder("Harbor")
            .AddPier("A", "Pier A", Vector2.Zero, 0f, 40f, pier => pier.AddBerths(PierSide.Left, 2, 5f, 12f))
            .AddMultiBerth(new MultiBerth("VIP", new[] { "A-L01", "A-L02" }, new Boat("B", "Aurora", BoatType.MotorYacht) { BeamMeters = 4.5f }, BerthStatus.Reserved))
            .Build();
        return new MarinaDocument { Name = "Harbor", Layout = layout };
    }
}
