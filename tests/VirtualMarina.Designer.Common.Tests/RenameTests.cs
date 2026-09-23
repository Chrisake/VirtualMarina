using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Designer.Resources;

namespace VirtualMarina.Designer.Common.Tests;

/// <summary>The rename questions, their checks, and applying the answer as one step.</summary>
public class RenameTests
{
    private static (DesignerSession Session, FakeDialogs Dialogs, MarinaVisualizer Marina) TwoPiers()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        marina.AddPier(new Pier("B", "Pier B", new Vector2(40, -30), 0f, 60f));
        marina.Designer.CreateBerths("A", PierSide.Left, 0f, 15f);
        marina.Designer.CreateBerths("B", PierSide.Left, 0f, 15f);
        var dialogs = new FakeDialogs();
        return (new DesignerSession(marina, dialogs, "tests", new ManualClock()), dialogs, marina);
    }

    private static RenameRequest BerthRequest(MarinaVisualizer marina, string berthId) =>
        new(DesignRenameScope.Element, berthId, marina.GetBerth(berthId)!.PierId, berthId, null, 0);

    private static RenameRequest PierRequest(MarinaVisualizer marina, string pierId, string? pattern = "{pier}-{number}") =>
        new(DesignRenameScope.Element, null, pierId, marina.GetPier(pierId)!.Name, pattern, marina.GetBerthsByPier(pierId).Count);

    [Fact]
    public async Task A_berth_takes_its_new_name_as_one_undoable_step()
    {
        var (session, dialogs, marina) = TwoPiers();
        using var _ = session;
        var first = marina.GetBerthsByPier("A")[0].Id;
        dialogs.Answers.Enqueue(["  Visitor 1 "]);

        Assert.True(await session.RenameAsync(BerthRequest(marina, first)));

        Assert.NotNull(marina.GetBerth("Visitor 1"));
        Assert.Null(marina.GetBerth(first));
        Assert.True(session.IsDirty);
        Assert.Equal(Strings.Format(Strings.LogRenamed, first, "Visitor 1"), session.Log.Latest?.Message);
        Assert.Equal(Strings.RenameBerthTitle, Assert.Single(dialogs.Prompts).Title);

        Assert.True(marina.Designer.Undo());
        Assert.NotNull(marina.GetBerth(first));
    }

    [Fact]
    public async Task A_name_already_taken_is_refused_and_asked_again_with_what_was_typed()
    {
        var (session, dialogs, marina) = TwoPiers();
        using var _ = session;
        var berths = marina.GetBerthsByPier("A");
        dialogs.Answers.Enqueue([berths[1].Id]);
        dialogs.Answers.Enqueue(null);

        Assert.False(await session.RenameAsync(BerthRequest(marina, berths[0].Id)));

        var alert = Assert.Single(dialogs.Alerts);
        Assert.Equal(Strings.RenameTakenTitle, alert.Title);
        Assert.Equal(2, dialogs.Prompts.Count);
        Assert.Equal(berths[1].Id, dialogs.Prompts[1].Fields[0].Value);
        Assert.Contains(session.Log.Entries, entry => entry.Message == Strings.Format(Strings.LogRenameRefused, berths[1].Id, berths[0].Id));
        Assert.NotNull(marina.GetBerth(berths[0].Id));
        Assert.False(session.IsDirty);
    }

    [Fact]
    public async Task Cancelling_or_leaving_the_name_blank_changes_nothing()
    {
        var (session, dialogs, marina) = TwoPiers();
        using var _ = session;
        var first = marina.GetBerthsByPier("A")[0].Id;
        dialogs.Answers.Enqueue(["   "]);

        Assert.False(await session.RenameAsync(BerthRequest(marina, first)));
        Assert.False(await session.RenameAsync(BerthRequest(marina, first)));
        Assert.False(marina.Designer.CanUndo && marina.Designer.UndoDescription!.Contains("Rename", StringComparison.Ordinal));
        Assert.NotNull(marina.GetBerth(first));
    }

    [Fact]
    public async Task A_pier_asks_for_its_name_id_and_pattern_and_applies_them_together()
    {
        var (session, dialogs, marina) = TwoPiers();
        using var _ = session;
        var request = PierRequest(marina, "A");
        dialogs.Answers.Enqueue(["West pontoon", "W", "{pier}.{number}"]);

        Assert.True(await session.RenameAsync(request));

        var prompt = Assert.Single(dialogs.Prompts);
        Assert.Equal(3, prompt.Fields.Count);
        Assert.Equal("Pier A", prompt.Fields[0].Value);
        Assert.Equal("A", prompt.Fields[1].Value);
        Assert.Null(marina.GetPier("A"));
        var pier = marina.GetPier("W");
        Assert.NotNull(pier);
        Assert.Equal("West pontoon", pier.Name);
        Assert.All(marina.GetBerthsByPier("W"), berth => Assert.StartsWith("W.", berth.Id, StringComparison.Ordinal));
        Assert.Contains(session.Log.Entries, entry => entry.Message == Strings.Format(Strings.LogPierIdChanged, "A", "W"));

        // One step takes all of it back.
        Assert.True(marina.Designer.Undo());
        Assert.Equal("Pier A", marina.GetPier("A")?.Name);
        Assert.Null(marina.GetPier("W"));
    }

    [Fact]
    public async Task A_pier_id_already_used_is_refused()
    {
        var (session, dialogs, marina) = TwoPiers();
        using var _ = session;
        dialogs.Answers.Enqueue(["Pier A", "B", string.Empty]);

        Assert.False(await session.RenameAsync(PierRequest(marina, "A")));

        Assert.Equal(Strings.Format(Strings.RenameTakenBody, "B"), Assert.Single(dialogs.Alerts).Message);
        Assert.NotNull(marina.GetPier("A"));
    }

    [Fact]
    public async Task A_pier_without_a_pattern_to_offer_is_asked_for_name_and_id_only()
    {
        var (session, dialogs, marina) = TwoPiers();
        using var _ = session;
        dialogs.Answers.Enqueue(["North", string.Empty]);

        Assert.True(await session.RenameAsync(PierRequest(marina, "A", pattern: null)));

        Assert.Equal(2, Assert.Single(dialogs.Prompts).Fields.Count);
        Assert.Equal("North", marina.GetPier("A")?.Name);
    }

    [Fact]
    public async Task A_whole_row_is_renamed_by_a_pattern_unless_it_clashes()
    {
        var (session, dialogs, marina) = TwoPiers();
        using var _ = session;
        var takenElsewhere = marina.GetBerthsByPier("B")[0].Id;
        var request = new RenameRequest(DesignRenameScope.BerthsOfPier, null, "A", "Pier A", "{pier}-{number}", 3);
        dialogs.Answers.Enqueue([takenElsewhere]);       // every berth would get the same, already taken, name
        dialogs.Answers.Enqueue(["Z{number}"]);

        Assert.True(await session.RenameAsync(request));

        Assert.Equal(Strings.RenameClashTitle, Assert.Single(dialogs.Alerts).Title);
        Assert.Equal(Strings.Format(Strings.RenameBerthsTitle, "A"), dialogs.Prompts[0].Title);
        Assert.All(marina.GetBerthsByPier("A"), berth => Assert.StartsWith("Z", berth.Id, StringComparison.Ordinal));
        Assert.Equal(Strings.Format(Strings.LogBerthPattern, "A", "Z{number}"), session.Log.Latest?.Message);
    }

    [Fact]
    public void Requests_are_copied_out_of_the_designers_event()
    {
        var (session, _, marina) = TwoPiers();
        using var _ = session;
        var pier = marina.GetPier("A")!;
        var berth = marina.GetBerthsByPier("A")[0];

        var one = RenameRequest.From(new DesignElementRenamingEventArgs(berth, null, berth.Id), marina);
        var row = RenameRequest.From(new DesignElementRenamingEventArgs(berth, pier, pier.Name, "{pier}{number}", DesignRenameScope.BerthsOfPier), marina);

        Assert.True(one.IsBerth);
        Assert.False(one.IsPier);
        Assert.True(row.IsWholeRow);
        Assert.Null(row.BerthId);
        Assert.Equal(3, row.BerthCount);
        Assert.Equal("{pier}{number}", RenamePlanner.Initial(row).Name);
    }

    [Fact]
    public void A_clash_list_is_cut_short_after_a_few_names()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 200f));
        marina.AddPier(new Pier("B", "Pier B", new Vector2(40, -30), 0f, 200f));
        marina.Designer.CreateBerths("A", PierSide.Left, 0f, 100f);
        marina.Designer.CreateBerths("B", PierSide.Left, 0f, 100f);

        // Pier A named the way pier B already is: every name is taken.
        var pattern = marina.Designer.DefaultBerthPattern("B").Replace("{pier}", "B", StringComparison.Ordinal);
        var clashes = marina.Designer.PlanBerthNames("A", pattern).Clashes.Count;
        Assert.True(clashes > RenamePlanner.ClashesShown);
        var request = new RenameRequest(DesignRenameScope.BerthsOfPier, null, "A", "Pier A", null, marina.GetBerthsByPier("A").Count);

        var problem = RenamePlanner.Check(marina.Designer, request, new RenameAnswer(pattern, "A", pattern));

        Assert.NotNull(problem);
        Assert.Contains(Strings.Format(Strings.RenameClashMore, clashes - RenamePlanner.ClashesShown), problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_designers_own_rename_request_is_let_go_and_asked_after_the_click()
    {
        var (session, dialogs, marina) = TwoPiers();
        using var _ = session;
        var berth = marina.GetBerthsByPier("A")[0];
        var args = new DesignElementRenamingEventArgs(berth, null, berth.Id);
        dialogs.Answers.Enqueue(["Later"]);

        // What the designer would do: raise the event and act on it straight away.
        typeof(MarinaDesigner).GetField("ElementRenaming", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(marina.Designer)
            .As<EventHandler<DesignElementRenamingEventArgs>>()
            .Invoke(marina.Designer, args);

        Assert.True(args.Cancel);
        for (var i = 0; i < 100 && marina.GetBerth("Later") is null; i++) await Task.Delay(10);
        Assert.NotNull(marina.GetBerth("Later"));
    }
}

internal static class CastExtensions
{
    public static T As<T>(this object? value) => (T)value!;
}
