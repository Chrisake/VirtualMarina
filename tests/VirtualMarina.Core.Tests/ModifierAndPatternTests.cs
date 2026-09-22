using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// Two things the designer has to do the moment it is asked, rather than at the next convenient time: show what
/// holding Alt is about to do, and rename a whole pier's berths to a pattern the user typed.
/// </summary>
public class ModifierAndPatternTests
{
    /// <summary>A pier looked at from straight above, with a row of berths down its left side.</summary>
    private static MarinaVisualizer WithARowOfBerths(out MarinaDesigner designer, out IReadOnlyList<Berth> berths)
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(1000, 800);
        marina.Camera.SetPose(new CameraPose(Vector3.Zero, 0f, 89f, 160f), immediate: true);
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));

        designer = marina.Designer;
        designer.IsActive = true;
        designer.BerthWidth = 5f;
        designer.BerthLength = 12f;
        berths = designer.CreateBerths("A", PierSide.Left, 0f, 60f);

        Assert.True(berths.Count >= 4, "the fixture needs a row worth sweeping");
        return marina;
    }

    /// <summary>How many things the frame draws, which the erase preview adds one pad to per doomed berth.</summary>
    private static int Drawn(MarinaVisualizer marina) => marina.BuildRenderFrame().Objects.Count;

    private static void PointAt(MarinaVisualizer marina, Vector2 plan)
    {
        Assert.True(marina.TryProjectToScreen(MarinaMath.ToWorld(plan), out var screen));
        marina.Input.PointerMove(screen.X, screen.Y);
    }

    [Fact]
    public void HoldingAlt_ShowsTheWholeRowAtOnce_WithoutWaitingForThePointerToMove()
    {
        var marina = WithARowOfBerths(out var designer, out var berths);
        designer.Tool = DesignTool.Erase;

        PointAt(marina, berths[0].Center);
        var one = Drawn(marina);

        // The key goes down and nothing else happens: no pointer move, no click.
        Assert.True(marina.Input.ModifiersChanged(InputModifiers.Alt), "holding Alt did not redraw");
        var row = Drawn(marina);
        Assert.True(row > one, $"the whole row was not shown: {one} drawn with one berth, {row} with Alt held");

        // Roughly a pad per berth in the row, rather than a pad for the one under the pointer.
        Assert.Equal(berths.Count - 1, row - one);

        // And letting go puts it back, again with nothing else happening.
        Assert.True(marina.Input.ModifiersChanged(InputModifiers.None), "letting Alt go did not redraw");
        Assert.Equal(one, Drawn(marina));
    }

    [Fact]
    public void TheSameModifiersTwiceOver_CostNothing()
    {
        var marina = WithARowOfBerths(out var designer, out var berths);
        designer.Tool = DesignTool.Erase;
        PointAt(marina, berths[0].Center);

        Assert.True(marina.Input.ModifiersChanged(InputModifiers.Alt));
        Assert.False(marina.Input.ModifiersChanged(InputModifiers.Alt), "the same state was treated as a change");

        // A modifier the tool in hand takes no notice of does not redraw either.
        Assert.False(marina.Input.ModifiersChanged(InputModifiers.Alt | InputModifiers.Control));
    }

    [Fact]
    public void ModifiersOutsideTheDesigner_ChangeNothing()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", Vector2.Zero, 0f, 60f));

        // Not designing at all.
        Assert.False(marina.Input.ModifiersChanged(InputModifiers.Alt));

        // Designing, but with a tool that does not care what is held.
        marina.Designer.IsActive = true;
        marina.Designer.Tool = DesignTool.Navigate;
        Assert.False(marina.Input.ModifiersChanged(InputModifiers.Alt));
    }

    [Fact]
    public void HoldingAltStillCounts_WhenTheEraserFinallyRuns()
    {
        var marina = WithARowOfBerths(out var designer, out var berths);
        designer.Tool = DesignTool.Erase;
        PointAt(marina, berths[0].Center);
        marina.Input.ModifiersChanged(InputModifiers.Alt);

        // What the preview promised is what the click does.
        Assert.True(marina.TryProjectToScreen(MarinaMath.ToWorld(berths[0].Center), out var screen));
        marina.Input.PointerDown(screen.X, screen.Y, PointerButton.Left, InputModifiers.Alt);
        marina.Input.PointerUp(screen.X, screen.Y, PointerButton.Left, InputModifiers.Alt);

        Assert.Empty(marina.GetBerthsByPier("A"));
    }

    [Fact]
    public void ThePatternOfferedForAPier_IsReadBackOutOfItsBerthNames()
    {
        var marina = WithARowOfBerths(out var designer, out _);

        string? offered = null;
        designer.ElementRenaming += (_, e) => { offered = e.BerthPattern; e.Cancel = true; };
        designer.Tool = DesignTool.Rename;
        PointAt(marina, new Vector2(0, -30));
        ClickWhereThePointerIs(marina);

        Assert.Equal("{pier}-{side}{number}", offered);
    }

    [Fact]
    public void ThePatternOffered_FollowsNamesGivenUnderAnOlderScheme()
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(1000, 800);
        marina.Camera.SetPose(new CameraPose(Vector3.Zero, 0f, 89f, 160f), immediate: true);
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        var designer = marina.Designer;
        designer.IsActive = true;

        // Named long ago, by a scheme nobody is using now: what is offered has to describe the berths as they are,
        // not the scheme that happens to be set.
        designer.BerthNaming = new BerthNamingScheme { Pattern = "QUAY.{side}.{number}", LeftSide = "P", NumberDigits = 3 };
        designer.CreateBerths("A", PierSide.Left, 0f, 60f);
        designer.BerthNaming = designer.BerthNaming with { Pattern = "{pier}-{side}{number}" };

        string? offered = null;
        designer.ElementRenaming += (_, e) => { offered = e.BerthPattern; e.Cancel = true; };
        designer.Tool = DesignTool.Rename;
        PointAt(marina, new Vector2(0, -30));
        ClickWhereThePointerIs(marina);

        Assert.Equal("QUAY.{side}.{number}", offered);
    }

    [Fact]
    public void RenamingAPierWithANewPattern_RenamesEveryBerthOnIt()
    {
        var marina = WithARowOfBerths(out var designer, out var berths);
        var before = berths.Select(berth => berth.Id).ToArray();
        Assert.All(before, id => Assert.StartsWith("A-L", id, StringComparison.Ordinal));

        designer.ElementRenaming += (_, e) =>
        {
            e.NewName = e.CurrentName;
            e.NewBerthPattern = "{pier}.{side}.{number}";
        };

        designer.Tool = DesignTool.Rename;
        PointAt(marina, new Vector2(0, -30));
        ClickWhereThePointerIs(marina);

        // Every one of them followed, keeping the number and the padding it had.
        var after = marina.GetBerthsByPier("A").Select(berth => berth.Id).ToArray();
        Assert.Equal(before.Length, after.Length);
        Assert.All(after, id => Assert.StartsWith("A.L.", id, StringComparison.Ordinal));
        Assert.Contains("A.L.01", after);
        Assert.All(before, id => Assert.Null(marina.GetBerth(id)));
    }

    [Fact]
    public void PuttingTheSamePatternBack_RenamesNothing()
    {
        var marina = WithARowOfBerths(out var designer, out var berths);
        var before = berths.Select(berth => berth.Id).ToArray();
        var steps = designer.UndoCount;

        designer.ElementRenaming += (_, e) =>
        {
            e.NewName = e.CurrentName;
            e.NewBerthPattern = e.BerthPattern;   // exactly what it offered
        };

        designer.Tool = DesignTool.Rename;
        PointAt(marina, new Vector2(0, -30));
        ClickWhereThePointerIs(marina);

        Assert.Equal(before, marina.GetBerthsByPier("A").Select(berth => berth.Id).ToArray());
        Assert.Equal(steps, designer.UndoCount);
    }

    [Fact]
    public void RenumberingToAPattern_KeepsTheNumbersAndTheirPadding()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        var designer = marina.Designer;
        designer.IsActive = true;
        designer.BerthNaming = new BerthNamingScheme { NumberDigits = 3 };
        var berths = designer.CreateBerths("A", PierSide.Left, 0f, 60f);
        Assert.Contains("A-L001", berths.Select(berth => berth.Id));

        var renamed = designer.RenumberBerths("A", "DOCK{number}");

        Assert.NotEmpty(renamed);
        Assert.Contains("DOCK001", marina.GetBerthsByPier("A").Select(berth => berth.Id));
        Assert.All(marina.GetBerthsByPier("A"), berth => Assert.StartsWith("DOCK", berth.Id, StringComparison.Ordinal));

        // One step, so one undo puts every one of them back.
        designer.Undo();
        Assert.Contains("A-L001", marina.GetBerthsByPier("A").Select(berth => berth.Id));
    }

    [Fact]
    public void APatternThatWouldGiveTwoBerthsTheSameName_RenamesNeither()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        var designer = marina.Designer;
        designer.IsActive = true;
        designer.CreateBerths("A", PierSide.Left, 0f, 60f);
        designer.CreateBerths("A", PierSide.Right, 0f, 60f);

        var left = marina.GetBerthsByPier("A").Where(berth => berth.Id.Contains("-L", StringComparison.Ordinal)).ToArray();
        var right = marina.GetBerthsByPier("A").Where(berth => berth.Id.Contains("-R", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(left);
        Assert.NotEmpty(right);

        // Dropping {side} would call a berth on each side the same thing; the second one keeps its name instead of
        // overwriting the first.
        designer.RenumberBerths("A", "{pier}-{number}");

        var names = marina.GetBerthsByPier("A").Select(berth => berth.Id).ToArray();
        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(left.Length + right.Length, names.Length);
    }

    [Fact]
    public void ABerthRename_IsNotOfferedAPattern()
    {
        var marina = WithARowOfBerths(out var designer, out var berths);

        DesignElementRenamingEventArgs? seen = null;
        designer.ElementRenaming += (_, e) => { seen = e; e.Cancel = true; };
        designer.Tool = DesignTool.Rename;
        PointAt(marina, berths[0].Center);
        ClickWhereThePointerIs(marina);

        Assert.NotNull(seen);
        Assert.NotNull(seen!.Berth);
        Assert.Null(seen.BerthPattern);
    }


    [Fact]
    public void AltOverABerth_RenamesTheWholePierByAPattern()
    {
        var marina = WithARowOfBerths(out var designer, out var berths);
        var before = berths.Select(berth => berth.Id).ToArray();

        DesignElementRenamingEventArgs? asked = null;
        designer.ElementRenaming += (_, e) =>
        {
            asked = e;
            e.NewBerthPattern = "{pier}.{number}";
        };

        designer.Tool = DesignTool.Rename;
        PointAt(marina, berths[0].Center);
        marina.Input.ModifiersChanged(InputModifiers.Alt);
        ClickWhereThePointerIs(marina, InputModifiers.Alt);

        // It asked about the pier's berths, not about the one berth that was clicked.
        Assert.NotNull(asked);
        Assert.Equal(DesignRenameScope.BerthsOfPier, asked!.Scope);
        Assert.NotNull(asked.Pier);
        Assert.Equal("{pier}-{side}{number}", asked.BerthPattern);

        // And the whole row followed, not just the one under the pointer.
        var after = marina.GetBerthsByPier("A").Select(berth => berth.Id).ToArray();
        Assert.Equal(before.Length, after.Length);
        Assert.All(after, id => Assert.StartsWith("A.", id, StringComparison.Ordinal));
        Assert.All(before, id => Assert.Null(marina.GetBerth(id)));
    }

    [Fact]
    public void AltOverABerth_LeavesThePiersOwnNameAndIdAlone()
    {
        var marina = WithARowOfBerths(out var designer, out var berths);
        var pier = marina.GetPier("A")!;

        designer.ElementRenaming += (_, e) =>
        {
            // A host that fills in everything must still only change the berths.
            e.NewName = "Something else";
            e.NewPierId = "Z";
            e.NewBerthPattern = "{pier}.{number}";
        };

        designer.Tool = DesignTool.Rename;
        PointAt(marina, berths[0].Center);
        ClickWhereThePointerIs(marina, InputModifiers.Alt);

        Assert.NotNull(marina.GetPier("A"));
        Assert.Null(marina.GetPier("Z"));
        Assert.Equal(pier.Name, marina.GetPier("A")!.Name);
        Assert.All(marina.GetBerthsByPier("A"), berth => Assert.StartsWith("A.", berth.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutAlt_ItIsStillTheOneBerthThatIsRenamed()
    {
        var marina = WithARowOfBerths(out var designer, out var berths);
        var others = marina.GetBerthsByPier("A").Where(b => b.Id != berths[0].Id).Select(b => b.Id).ToArray();

        designer.ElementRenaming += (_, e) =>
        {
            Assert.Equal(DesignRenameScope.Element, e.Scope);
            e.NewName = "VIP";
        };

        designer.Tool = DesignTool.Rename;
        PointAt(marina, berths[0].Center);
        ClickWhereThePointerIs(marina);

        Assert.NotNull(marina.GetBerth("VIP"));
        Assert.All(others, id => Assert.NotNull(marina.GetBerth(id)));
    }

    [Fact]
    public void RenamingABerth_TakesItsLabelWithIt()
    {
        var marina = WithARowOfBerths(out var designer, out var berths);
        var berth = berths[0];
        Assert.Equal(berth.Id, berth.DisplayName);

        var renamed = designer.RenameBerth(berth.Id, "VIP-01");

        // The name on the water is the new one, not the old one it was drawn with.
        Assert.Equal("VIP-01", renamed.Id);
        Assert.Equal("VIP-01", renamed.DisplayName);
        Assert.Equal("VIP-01", marina.GetBerth("VIP-01")!.DisplayName);

        // And one undo puts both back.
        designer.Undo();
        Assert.Equal(berth.Id, marina.GetBerth(berth.Id)!.DisplayName);
    }

    [Fact]
    public void ALabelSomeoneWroteIsTheirs_AndSurvivesARename()
    {
        var marina = WithARowOfBerths(out var designer, out var berths);
        var berth = berths[0];
        marina.UpdateBerth(berth with { Label = "Harbourmaster" });

        var renamed = designer.RenameBerth(berth.Id, "VIP-01");

        Assert.Equal("VIP-01", renamed.Id);
        Assert.Equal("Harbourmaster", renamed.DisplayName);
    }


    [Fact]
    public void AWholePierRename_ScrapsNamesThatFollowNoPatternAtAll()
    {
        var marina = WithARowOfBerths(out var designer, out var berths);

        // Named by hand, with nothing a pattern could be read out of: no number, no pier prefix.
        marina.Designer.RenameBerth(berths[0].Id, "HARBOURMASTER");
        marina.Designer.RenameBerth(berths[1].Id, "VISITOR");
        marina.Designer.RenameBerth(berths[2].Id, "FUEL");

        var plan = designer.PlanBerthNames("A", "{pier}-{side}{number}");
        Assert.True(plan.IsClear);
        Assert.Equal(berths.Count, plan.Renames.Count);
        Assert.Contains(plan.Renames, rename => rename.From == "HARBOURMASTER");

        designer.ApplyBerthNames(plan);

        // Every one of them, however it was named before.
        var after = marina.GetBerthsByPier("A").Select(berth => berth.Id).ToArray();
        Assert.Equal(berths.Count, after.Length);
        Assert.All(after, id => Assert.StartsWith("A-L", id, StringComparison.Ordinal));
        Assert.Contains("A-L01", after);
        Assert.Null(marina.GetBerth("HARBOURMASTER"));
    }

    [Fact]
    public void TheNamesAreCountedAlongThePier_FromTheStart()
    {
        var marina = WithARowOfBerths(out var designer, out _);
        designer.ApplyBerthNames(designer.PlanBerthNames("A", null));

        var pier = marina.GetPier("A")!;
        var ordered = marina.GetBerthsByPier("A")
            .OrderBy(berth => Vector2.Dot(berth.Center - pier.Start, pier.Direction))
            .Select(berth => berth.Id)
            .ToArray();

        // Numbered in the order they lie along the pier, not in whatever order they happen to be stored.
        Assert.Equal(ordered, ordered.OrderBy(id => id, StringComparer.Ordinal).ToArray());
        Assert.Equal("A-L01", ordered[0]);
    }

    [Fact]
    public void ThePatternOfferedComesFromThePierType()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        marina.AddPier(new Pier("E", "Pier E", new Vector2(80, -30), 0f, 60f) { BerthingSides = PierSides.Left });

        var designer = marina.Designer;
        designer.IsActive = true;

        // A pier that takes boats on one side has no other side to tell a berth apart from.
        Assert.Equal("{pier}-{side}{number}", designer.DefaultBerthPattern("A"));
        Assert.Equal("{pier}-{number}", designer.DefaultBerthPattern("E"));

        designer.CreateBerths("E", PierSide.Left, 0f, 60f);
        designer.ApplyBerthNames(designer.PlanBerthNames("E", null));

        // No side letter on a pier that has only one side.
        var named = marina.GetBerthsByPier("E").Select(berth => berth.Id).ToArray();
        Assert.All(named, id => Assert.Matches(@"^E-\d+$", id));
        Assert.Contains("E-01", named);
    }

    [Fact]
    public void APatternThatWouldRepeatAName_IsReportedRatherThanApplied()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        var designer = marina.Designer;
        designer.IsActive = true;
        designer.CreateBerths("A", PierSide.Left, 0f, 60f);
        designer.CreateBerths("A", PierSide.Right, 0f, 60f);

        // A pattern with no number gives every berth the same name.
        var plan = designer.PlanBerthNames("A", "{pier}-QUAY");
        Assert.False(plan.IsClear);
        Assert.Contains("A-QUAY", plan.Clashes);

        // Nothing is touched, and applying it anyway is refused rather than half-done.
        var before = marina.GetBerthsByPier("A").Select(berth => berth.Id).ToArray();
        Assert.Throws<InvalidOperationException>(() => designer.ApplyBerthNames(plan));
        Assert.Equal(before, marina.GetBerthsByPier("A").Select(berth => berth.Id).ToArray());

        // Dropping {side} numbers straight through instead of twice over, so it is sound.
        var straight = designer.PlanBerthNames("A", "{pier}-{number}");
        Assert.True(straight.IsClear);
        Assert.Equal(before.Length, straight.Renames.Select(rename => rename.To).Distinct().Count());
    }

    [Fact]
    public void ANameAnotherPierAlreadyHas_IsReportedToo()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        marina.AddPier(new Pier("B", "Pier B", new Vector2(80, -30), 0f, 60f));
        var designer = marina.Designer;
        designer.IsActive = true;
        designer.CreateBerths("A", PierSide.Left, 0f, 60f);
        designer.CreateBerths("B", PierSide.Left, 0f, 60f);

        // Naming pier A's berths as if they were pier B's runs straight into pier B.
        var plan = designer.PlanBerthNames("A", "B-{side}{number}");
        Assert.False(plan.IsClear);
        Assert.Contains("B-L01", plan.Clashes);

        // And the report names them, so the user can be told which.
        Assert.All(plan.Clashes, name => Assert.NotNull(marina.GetBerth(name)));
    }

    [Fact]
    public void ShufflingTheNamesAroundAPier_DoesNotCollideWithItself()
    {
        var marina = WithARowOfBerths(out var designer, out var berths);
        var count = berths.Count;

        // Every berth takes the name of the one before it, so a straight pass would collide at every step.
        var shifted = designer.PlanBerthNames("A", "{pier}-{side}{number}");
        Assert.True(shifted.IsClear);

        designer.BerthNaming = designer.BerthNaming with { StartNumber = 2 };
        var plan = designer.PlanBerthNames("A", "{pier}-{side}{number}");
        Assert.True(plan.IsClear, "moving every berth up one was reported as a clash");
        designer.ApplyBerthNames(plan);

        var after = marina.GetBerthsByPier("A").Select(berth => berth.Id).ToArray();
        Assert.Equal(count, after.Length);
        Assert.Equal(count, after.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("A-L02", after);
        Assert.DoesNotContain("A-L01", after);

        // And one undo puts the whole pier back.
        designer.Undo();
        Assert.Contains("A-L01", marina.GetBerthsByPier("A").Select(berth => berth.Id));
    }

    private static void ClickWhereThePointerIs(MarinaVisualizer marina, InputModifiers modifiers = InputModifiers.None)
    {
        var at = marina.Designer.PointerPosition;
        Assert.NotNull(at);
        Assert.True(marina.TryProjectToScreen(MarinaMath.ToWorld(at!.Value), out var screen));
        marina.Input.PointerDown(screen.X, screen.Y, PointerButton.Left, modifiers);
        marina.Input.PointerUp(screen.X, screen.Y, PointerButton.Left, modifiers);
    }
}
