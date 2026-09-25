using System.Numerics;
using System.Text.Json.Nodes;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Serialization;
using VirtualMarina.SampleData;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// A design is a layout: it is saved with every berth Free and enabled and no multi-berths, unless asked to keep who is where.
/// </summary>
public class MarinaDocumentOccupancyTests
{
    /// <summary>The sample marina: occupied, reserved and disabled berths, and multi-berths.</summary>
    private static MarinaDocument FullMarina(out MarinaVisualizer marina)
    {
        marina = new MarinaVisualizer();
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());
        Assert.NotEmpty(marina.GetMultiBerths());
        Assert.NotEmpty(marina.GetBerthsByStatus(BerthStatus.Occupied));
        Assert.Contains(marina.GetBerths(), berth => berth.IsDisabled);
        return MarinaDocument.FromVisualizer(marina, generator: "tests");
    }

    private static void AssertEmptyLayout(MarinaDocument reread)
    {
        Assert.Empty(reread.Layout.MultiBerths);
        Assert.All(reread.Layout.Berths, berth =>
        {
            Assert.Equal(BerthStatus.Free, berth.Status);
            Assert.Null(berth.Boat);
            Assert.True(berth.IsVisible);
            Assert.False(berth.IsDisabled);
            Assert.False(berth.IsReadOnly);
        });
    }

    [Fact]
    public async Task EveryWayOfSaving_WritesTheMarinaEmpty_ByDefault_AndLeavesTheDocumentAsItWas()
    {
        var document = FullMarina(out _);
        var berthsBefore = document.Layout.Berths;
        var groupsBefore = document.Layout.MultiBerths;

        var json = JsonNode.Parse(document.ToJson())!["layout"]!;
        Assert.Null(json["multiBerths"]);
        Assert.All(json["berths"]!.AsArray(), berth =>
        {
            Assert.Null(berth!["status"]);
            Assert.Null(berth["boat"]);
            Assert.Null(berth["isDisabled"]);
            Assert.Null(berth["isVisible"]);
            Assert.Null(berth["isReadOnly"]);
        });

        AssertEmptyLayout(MarinaDocument.Parse(document.ToUtf8Bytes()));
        AssertEmptyLayout(MarinaDocument.Load(document.Save()));
        using (var stream = new MemoryStream())
        {
            await document.SaveAsync(stream);
            stream.Position = 0;
            AssertEmptyLayout(await MarinaDocument.LoadAsync(stream));
        }

        var path = Path.Combine(Path.GetTempPath(), $"vm-occupancy-{Guid.NewGuid():N}.marina.json");
        try
        {
            document.Save(path);
            AssertEmptyLayout(MarinaDocument.Load(path));
        }
        finally
        {
            File.Delete(path);
        }

        // Only the file is empty: the document still knows who is where.
        Assert.Equal(berthsBefore, document.Layout.Berths);
        Assert.Equal(groupsBefore, document.Layout.MultiBerths);
    }

    [Fact]
    public void SavingWithOccupancyKept_WritesStatusesBoatsFlagsAndMultiBerths()
    {
        var document = FullMarina(out var marina);
        var copy = new MarinaVisualizer();
        MarinaDocument.Load(document.Save(stripOccupancy: false)).ApplyTo(copy);

        Assert.Equal(marina.GetMultiBerths().Select(b => b.Id), copy.GetMultiBerths().Select(b => b.Id));
        foreach (var berth in marina.GetBerths())
        {
            var loaded = copy.GetBerth(berth.Id)!;
            Assert.Equal(berth.Status, loaded.Status);
            Assert.Equal(berth.Boat, loaded.Boat);
            Assert.Equal(berth.IsDisabled, loaded.IsDisabled);
            Assert.Equal(berth.MultiBerthId, loaded.MultiBerthId);
        }
    }

    [Fact]
    public void ConnectedBerths_AreWrittenToTheFile_AndReadBack()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "A", new Vector2(0, -25), 0f, 50f, 2f));
        marina.Designer.BerthWidth = 5f;
        marina.Designer.CreateBerths("A", PierSide.Right, 0f, 15f);
        marina.Designer.PlaceDividers("A", PierSide.Right, 10f);

        var json = MarinaDocument.FromVisualizer(marina).ToJson();
        var berths = JsonNode.Parse(json)!["layout"]!["berths"]!.AsArray();
        Assert.Equal(["A-R02"], berths[0]!["connectedBerthIds"]!.AsArray().Select(id => (string?)id));
        Assert.Null(berths[2]!["connectedBerthIds"]); // parted from A-R02 by the divider: nothing to write

        var reread = MarinaDocument.Parse(json).Layout;
        Assert.Equal(marina.GetBerths().Select(b => b.ConnectedBerthIds), reread.Berths.Select(b => b.ConnectedBerthIds));
    }

    [Theory]
    [InlineData("\"PairedFingerPiers\"", DividerType.FingerPier, 2)]
    [InlineData("\"FingerPier\"", DividerType.FingerPier, 1)]
    [InlineData("\"Piles\"", DividerType.Piles, 1)]
    [InlineData("\"Boom\"", DividerType.Boom, 1)]
    [InlineData("\"SinglePile\"", DividerType.SinglePile, 1)]
    [InlineData("5", DividerType.SinglePile, 1)]
    [InlineData("\"FingerPiers\"", DividerType.FingerPier, 1)]
    [InlineData("\"None\"", DividerType.FingerPier, 1)]
    [InlineData("{}", DividerType.FingerPier, 1)]
    public void AnOldSeparatorSetting_SetsTheDividerToolUpTheSameWay(string written, DividerType type, int interval)
    {
        var json = $$"""{ "format": "virtualmarina.marina", "formatVersion": "2.0", "designer": { "berthSeparators": {{written}} } }""";

        var designer = MarinaDocument.Parse(json).Designer!;
        Assert.Equal(type, designer.DividerType);
        Assert.Equal(interval, designer.DividerInterval);

        // It is not written back: the divider tool's own settings are.
        var again = JsonNode.Parse(MarinaDocument.Parse(json).ToJson())!["designer"]!;
        Assert.Null(again["berthSeparators"]);
        Assert.Equal(type.ToString(), (string?)again["dividerType"]);
    }
}
