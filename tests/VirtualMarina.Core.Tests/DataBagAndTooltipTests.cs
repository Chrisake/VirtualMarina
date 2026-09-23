using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// The two things a host application handles most often, and the two the library itself never looks at: the bag of
/// its own data hung off a berth, and the tooltip and action list it fills in when one is clicked.
/// </summary>
public class DataBagAndTooltipTests
{
    private sealed record Contract(string Number);

    // ---- MarinaDataBag -----------------------------------------------------------------------

    [Fact]
    public void DataBag_Get_ReturnsDefault_ForAMissingKeyOrTheWrongType()
    {
        var bag = new MarinaDataBag { ["Contract"] = new Contract("C-1") };

        Assert.Equal("C-1", bag.Get<Contract>("Contract")?.Number);
        Assert.Null(bag.Get<Contract>("Absent"));
        // Present, but not what was asked for: a default rather than an InvalidCastException.
        Assert.Null(bag.Get<string>("Contract"));
        Assert.Equal(0, bag.Get<int>("Contract"));
    }

    [Fact]
    public void DataBag_TryGet_SaysWhetherTheValueWasThereAndOfTheRightType()
    {
        var bag = new MarinaDataBag { ["Nights"] = 3, ["Name"] = "Aurora" };

        Assert.True(bag.TryGet<int>("Nights", out var nights));
        Assert.Equal(3, nights);

        Assert.False(bag.TryGet<int>("Name", out var wrongType));
        Assert.Equal(0, wrongType);

        Assert.False(bag.TryGet<int>("Absent", out _));
    }

    [Fact]
    public void DataBag_GetOrAdd_RunsTheFactoryOnce_AndKeepsWhatItMade()
    {
        var bag = new MarinaDataBag();
        var calls = 0;

        var first = bag.GetOrAdd("Contract", () => { calls++; return new Contract("C-7"); });
        var second = bag.GetOrAdd("Contract", () => { calls++; return new Contract("C-9"); });

        Assert.Same(first, second);
        Assert.Equal("C-7", second.Number);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void DataBag_Indexer_ThrowsForAMissingKey_WhichIsWhyGetExists()
    {
        var bag = new MarinaDataBag();

        Assert.Throws<KeyNotFoundException>(() => bag["Absent"]);
        Assert.Null(bag.Get<object>("Absent"));
    }

    [Fact]
    public void DataBag_KeysAreCaseSensitive()
    {
        var bag = new MarinaDataBag();
        bag.Set("Contract", 1);
        bag.Set("contract", 2);

        Assert.Equal(2, bag.Count);
        Assert.Equal(1, bag.Get<int>("Contract"));
        Assert.Equal(2, bag.Get<int>("contract"));
    }

    [Fact]
    public void DataBag_AddRemoveClear_BehaveLikeADictionary()
    {
        var bag = new MarinaDataBag(new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2 });

        Assert.Equal(2, bag.Count);
        Assert.True(bag.ContainsKey("a"));
        Assert.Contains("b", bag.Keys);
        Assert.Contains(2, bag.Values);

        bag.Add("c", 3);
        Assert.Throws<ArgumentException>(() => bag.Add("c", 4)); // Add refuses to overwrite; Set does not.
        bag.Set("c", 4);
        Assert.Equal(4, bag.Get<int>("c"));

        Assert.True(bag.Remove("a"));
        Assert.False(bag.Remove("a"));

        Assert.Equal(new[] { "b", "c" }, bag.Select(pair => pair.Key).OrderBy(key => key));

        bag.Clear();
        Assert.Empty(bag);
    }

    [Fact]
    public void DataBag_IsSharedByEverySnapshotOfTheSameBerth()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", System.Numerics.Vector2.Zero, 0f, 40f));
        marina.AddBerth(BerthGenerator.AtPier(marina.GetPier("A")!, "A-L01", PierSide.Left, 0f, 5f, 12f));

        marina.GetBerth("A-L01")!.ExternalData.Set("Contract", "C-42");
        // A status change hands out a new snapshot; the bag behind it is the same one.
        marina.SetBerthStatus("A-L01", BerthStatus.Occupied);

        Assert.Equal("C-42", marina.GetBerth("A-L01")!.ExternalData.Get<string>("Contract"));
    }

    // ---- BerthTooltip ------------------------------------------------------------------------

    [Fact]
    public void Tooltip_AddLine_AppendsEvenWhenTheLabelRepeats()
    {
        var tooltip = new BerthTooltip();

        tooltip.AddLine("Owner", "Ana").AddLine("Owner", "Bo", emphasize: true);

        Assert.Equal(2, tooltip.Lines.Count);
        Assert.Equal("Ana", tooltip.Lines[0].Value);
        Assert.False(tooltip.Lines[0].IsEmphasized);
        Assert.True(tooltip.Lines[1].IsEmphasized);
    }

    [Fact]
    public void Tooltip_SetLine_ReplacesTheFirstLineWithThatLabel_OrAddsOne()
    {
        var tooltip = new BerthTooltip();
        tooltip.AddLine("Owner", "Ana").AddLine("Boat", "Aurora");

        tooltip.SetLine("Owner", "Bo");
        Assert.Equal(2, tooltip.Lines.Count);
        Assert.Equal("Bo", tooltip.Lines[0].Value);

        tooltip.SetLine("Nights", "3");
        Assert.Equal(3, tooltip.Lines.Count);
        Assert.Equal("3", tooltip.Lines[^1].Value);
    }

    [Fact]
    public void Tooltip_RemoveLine_SaysWhetherThereWasOne()
    {
        var tooltip = new BerthTooltip();
        tooltip.AddLine("Owner", "Ana");

        Assert.True(tooltip.RemoveLine("Owner"));
        Assert.False(tooltip.RemoveLine("Owner"));
        Assert.Empty(tooltip.Lines);
    }

    [Fact]
    public void Tooltip_Clear_EmptiesTheContentButLeavesItVisible()
    {
        var tooltip = new BerthTooltip { Title = "A-L01", Subtitle = "Pier A", Footer = "ERP" };
        tooltip.AddLine("Owner", "Ana");

        tooltip.Clear();

        Assert.Empty(tooltip.Lines);
        Assert.Null(tooltip.Title);
        Assert.Null(tooltip.Subtitle);
        Assert.Null(tooltip.Footer);
        Assert.True(tooltip.IsVisible);
    }

    // ---- BerthActionCollection ---------------------------------------------------------------

    [Fact]
    public void Actions_Add_ReturnsTheActionSoItCanBeDressedUp()
    {
        var actions = new BerthActionCollection();

        var action = actions.Add("free", "Free the berth", enabled: false, icon: "broom");
        action.Description = "Releases the boat";
        action.Style = BerthActionStyle.Danger;
        action.Tag = 42;

        Assert.Single(actions);
        Assert.Equal("free", action.ActionId);
        Assert.False(action.Enabled);
        Assert.Equal("broom", action.Icon);
        Assert.True(action.Visible);
        Assert.Equal(42, action.Tag);
    }

    [Fact]
    public void Actions_FindAndRemove_WorkByActionId()
    {
        var actions = new BerthActionCollection();
        actions.Add("free", "Free");
        actions.Add("invoice", "Invoice");

        Assert.Equal("Invoice", actions.Find("invoice")?.Caption);
        Assert.Null(actions.Find("absent"));

        Assert.True(actions.Remove("free"));
        Assert.False(actions.Remove("free"));
        Assert.Single(actions);
    }
}
