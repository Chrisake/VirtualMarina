using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// Undo and redo as commands that hold only what they changed: redo, steps grouped into one, a step that happens whole or
/// not at all, and the host's own changes left alone.
/// </summary>
public class DesignerHistoryTests
{
    private const InputModifiers Ctrl = InputModifiers.Control;

    private static MarinaVisualizer WithPier(out MarinaDesigner designer)
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(1000, 800);
        marina.Camera.SetPose(new CameraPose(Vector3.Zero, 0f, 89f, 160f), immediate: true);
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        designer = marina.Designer;
        designer.IsActive = true;
        designer.BerthWidth = 5f;
        designer.BerthLength = 12f;
        return marina;
    }

    private static MarinaVisualizer WithLawn(out MarinaDesigner designer)
    {
        var marina = new MarinaVisualizer();
        marina.AddLandArea(new LandArea("lawn", new[] { new Vector2(0, 0), new Vector2(60, 0), new Vector2(60, 40), new Vector2(0, 40) }, 1f, LandKind.Grass));
        designer = marina.Designer;
        designer.SetRandomSeed(7);
        return marina;
    }

    // ---- Redo -----------------------------------------------------------------------------------

    [Fact]
    public void Redo_MakesAnUndoneChangeAgain_AndRaisesItsOwnEvent()
    {
        var marina = WithPier(out var designer);
        var redone = new List<DesignActionRedoneEventArgs>();
        designer.ActionRedone += (_, e) => redone.Add(e);
        var added = designer.CreateBerths("A", PierSide.Left, 0f, 15f).Select(berth => berth.Id).ToArray();
        Assert.False(designer.CanRedo);
        Assert.Null(designer.RedoDescription);

        Assert.True(designer.Undo());
        Assert.Empty(marina.GetBerths());
        Assert.True(designer.CanRedo);
        Assert.Equal(1, designer.RedoCount);
        Assert.Equal("Add 3 berths", designer.RedoDescription);

        Assert.True(designer.Redo());
        Assert.Equal(added, marina.GetBerths().Select(berth => berth.Id));
        Assert.False(designer.CanRedo);
        Assert.Equal("Add 3 berths", designer.UndoDescription);
        var e = Assert.Single(redone);
        Assert.Equal("Add 3 berths", e.Description);
        Assert.Equal(0, e.RemainingSteps);
        Assert.False(designer.Redo());
    }

    [Fact]
    public void ANewChange_ForgetsWhatCouldHaveBeenRedone()
    {
        WithPier(out var designer);
        designer.CreateBerths("A", PierSide.Left, 0f, 5f);
        designer.Undo();
        Assert.True(designer.CanRedo);

        designer.CreateBerths("A", PierSide.Right, 0f, 5f);
        Assert.False(designer.CanRedo);
    }

    [Fact]
    public void UndoAndRedo_WalkBackAndForthThroughErasures_AndRenames()
    {
        var marina = WithPier(out var designer);
        designer.DividerType = DividerType.Piles;
        designer.CreateBerths("A", PierSide.Left, 0f, 15f);
        designer.PlaceDividers("A", PierSide.Left, 0f, wholeRow: true);
        var dividers = marina.GetDividers().Count;
        designer.Erase(marina.GetBerth("A-L02")!);
        designer.RenameBerth("A-L01", "Visitor");
        designer.RenamePier("A", "West pontoon");

        for (var round = 0; round < 2; round++)
        {
            Assert.True(designer.Undo());
            Assert.Equal("Pier A", marina.GetPier("A")!.Name);
            Assert.True(designer.Undo());
            Assert.NotNull(marina.GetBerth("A-L01"));
            Assert.True(designer.Undo());
            Assert.NotNull(marina.GetBerth("A-L02"));
            Assert.Equal(dividers, marina.GetDividers().Count);

            Assert.True(designer.Redo());
            Assert.Null(marina.GetBerth("A-L02"));
            Assert.True(designer.Redo());
            Assert.NotNull(marina.GetBerth("Visitor"));
            Assert.True(designer.Redo());
            Assert.Equal("West pontoon", marina.GetPier("A")!.Name);
        }
    }

    [Fact]
    public void CtrlShiftZ_AndCtrlY_Redo_FromTheKeyboard()
    {
        var marina = WithPier(out var designer);
        designer.CreateBerths("A", PierSide.Left, 0f, 5f);
        Assert.False(marina.Input.WantsKey(MarinaKey.Redo, Ctrl | InputModifiers.Shift));

        Assert.True(marina.Input.KeyDown(MarinaKey.Undo, Ctrl));
        Assert.Empty(marina.GetBerths());
        Assert.True(marina.Input.WantsKey(MarinaKey.Redo, Ctrl | InputModifiers.Shift));

        Assert.Equal(MarinaKey.Redo, MarinaKeyMap.FromVirtualKey(0x59, Ctrl));
        Assert.True(marina.Input.KeyDown(MarinaKey.Redo, Ctrl));
        Assert.Single(marina.GetBerths());
    }

    // ---- Only what was changed ------------------------------------------------------------------

    [Fact]
    public void UndoingTrees_PutsBackOnlyTheTrees_AndKeepsWhatTheHostChangedSince()
    {
        var marina = WithLawn(out var designer);
        var before = marina.GetLandArea("lawn")!;
        designer.PlantTrees("lawn");
        Assert.NotEmpty(marina.GetLandArea("lawn")!.Trees);

        // The host renames the lawn and raises it after the trees went in.
        marina.UpdateLandArea(marina.GetLandArea("lawn")! with { Name = "Park", Height = 2f });

        Assert.True(designer.Undo());
        var after = marina.GetLandArea("lawn")!;
        Assert.Equal(before.Trees, after.Trees);
        Assert.Equal("Park", after.Name);
        Assert.Equal(2f, after.Height);
    }

    [Fact]
    public void AnUndoOfTreesTheHostHasReplacedSince_IsRefused_AndChangesNothing()
    {
        var marina = WithLawn(out var designer);
        designer.PlantTrees("lawn");
        var hosts = new[] { new LandTree(new Vector2(10, 10), 6f, 2f) };
        marina.UpdateLandArea(marina.GetLandArea("lawn")! with { Trees = hosts });

        Assert.Throws<MarinaLayoutException>(() => designer.Undo());
        Assert.Equal(hosts, marina.GetLandArea("lawn")!.Trees);
        Assert.True(designer.CanUndo);
    }

    [Fact]
    public void UndoingAPier_TheHostHasPutBerthsOnSince_IsRefused_RatherThanTakingTheirBerthsWithIt()
    {
        var marina = new MarinaVisualizer();
        var designer = marina.Designer;
        designer.CreatePier(new Vector2(0, 0), new Vector2(0, 40));
        marina.AddBerth("HOST-1", "A", new Vector2(-5, 10), 0f, 10f, 4f);

        var error = Assert.Throws<MarinaLayoutException>(() => designer.Undo());
        Assert.Contains("HOST-1", error.Message, StringComparison.Ordinal);
        Assert.NotNull(marina.GetPier("A"));
        Assert.NotNull(marina.GetBerth("HOST-1"));
    }

    [Fact]
    public void AStepThatFailsPartWay_IsPutBackWhole()
    {
        var marina = WithLawn(out var designer);
        marina.AddPier(new Pier("A", "Pier A", new Vector2(100, -30), 0f, 60f));
        using (var action = designer.BeginAction("Trees and berths"))
        {
            designer.PlantTrees("lawn");
            designer.CreateBerths("A", PierSide.Left, 0f, 10f);
            action.Complete();
        }

        // The host replaces the trees since: that part of the step can no longer be taken back.
        var hosts = new[] { new LandTree(new Vector2(10, 10), 6f, 2f) };
        marina.UpdateLandArea(marina.GetLandArea("lawn")! with { Trees = hosts });
        var berths = marina.GetBerths().Select(berth => berth.Id).ToArray();

        Assert.Throws<MarinaLayoutException>(() => designer.Undo());

        // The berths are undone first, newest first, and had gone when the trees refused; they are back.
        Assert.Equal(berths, marina.GetBerths().Select(berth => berth.Id));
        Assert.Equal(hosts, marina.GetLandArea("lawn")!.Trees);
        Assert.True(designer.CanUndo);
    }

    [Fact]
    public void FromTheKeyboard_ARefusedUndo_IsReported_NotThrown()
    {
        var marina = WithLawn(out var designer);
        designer.IsActive = true;
        designer.PlantTrees("lawn");
        marina.UpdateLandArea(marina.GetLandArea("lawn")! with { Trees = Array.Empty<LandTree>() });
        var failures = new List<DesignActionFailedEventArgs>();
        designer.ActionFailed += (_, e) => failures.Add(e);

        Assert.False(marina.Input.KeyDown(MarinaKey.Undo, Ctrl));

        var failure = Assert.Single(failures);
        Assert.IsType<MarinaLayoutException>(failure.Exception);
        Assert.StartsWith("Undo ", failure.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void TryUndo_ARefusedUndo_IsReported_NotThrown_AndStaysOnTheHistory()
    {
        var marina = WithLawn(out var designer);
        designer.PlantTrees("lawn");
        marina.UpdateLandArea(marina.GetLandArea("lawn")! with { Trees = Array.Empty<LandTree>() });
        var failures = new List<DesignActionFailedEventArgs>();
        designer.ActionFailed += (_, e) => failures.Add(e);

        Assert.False(designer.TryUndo());

        var failure = Assert.Single(failures);
        Assert.IsType<MarinaLayoutException>(failure.Exception);
        Assert.StartsWith("Undo ", failure.Description, StringComparison.Ordinal);
        Assert.True(designer.CanUndo);
    }

    [Fact]
    public void TryUndoAndTryRedo_InsideAnOpenAction_AreReported_NotThrown()
    {
        WithPier(out var designer);
        designer.CreateBerths("A", PierSide.Left, 0f, 5f);
        designer.CreateBerths("A", PierSide.Right, 0f, 5f);
        Assert.True(designer.Undo());
        Assert.True(designer.CanRedo);
        var failures = new List<DesignActionFailedEventArgs>();
        designer.ActionFailed += (_, e) => failures.Add(e);

        using (designer.BeginAction("Open"))
        {
            Assert.False(designer.TryRedo());
            Assert.False(designer.TryUndo());
        }

        Assert.Equal(2, failures.Count);
        Assert.All(failures, failure => Assert.IsType<InvalidOperationException>(failure.Exception));
    }

    // ---- One step for several changes -----------------------------------------------------------

    [Fact]
    public void BeginAction_MakesEverythingInsideItOneStep()
    {
        var marina = WithPier(out var designer);
        designer.CreateBerths("A", PierSide.Left, 0f, 20f);
        var steps = designer.UndoCount;
        var states = 0;
        designer.StateChanged += (_, _) => states++;

        using (var action = designer.BeginAction("Clear two berths"))
        {
            designer.Erase(marina.GetBerth("A-L01")!);
            designer.Erase(marina.GetBerth("A-L02")!);
            action.Complete();
        }

        Assert.Equal(steps + 1, designer.UndoCount);
        Assert.Equal("Clear two berths", designer.UndoDescription);
        Assert.Equal(1, states);

        Assert.True(designer.Undo());
        Assert.NotNull(marina.GetBerth("A-L01"));
        Assert.NotNull(marina.GetBerth("A-L02"));
    }

    [Fact]
    public void AnActionNotCompleted_TakesBackEverythingDoneInsideIt()
    {
        var marina = WithPier(out var designer);
        designer.CreateBerths("A", PierSide.Left, 0f, 20f);
        var ids = marina.GetBerths().Select(berth => berth.Id).ToArray();
        var steps = designer.UndoCount;

        Action rebuild = () =>
        {
            using var action = designer.BeginAction("Rebuild");
            designer.EraseBerthsOfPier("A");
            designer.CreateBerths("A", PierSide.Right, 0f, 10f);
            throw new InvalidOperationException("the host gave up half way");
        };
        Assert.Throws<InvalidOperationException>(rebuild);

        Assert.Equal(ids, marina.GetBerths().Select(berth => berth.Id));
        Assert.Equal(steps, designer.UndoCount);
    }

    [Fact]
    public void AnInnerActionTakenBack_LeavesTheOuterOneAlone()
    {
        var marina = WithPier(out var designer);
        using (var outer = designer.BeginAction("Outer"))
        {
            designer.CreateBerths("A", PierSide.Left, 0f, 5f);
            using (designer.BeginAction("Inner"))
            {
                designer.CreateBerths("A", PierSide.Right, 0f, 5f);
            }

            outer.Complete();
        }

        Assert.Equal("A-L01", Assert.Single(marina.GetBerths()).Id);
        Assert.Equal("Outer", designer.UndoDescription);
        Assert.Equal(1, designer.UndoCount);
    }

    [Fact]
    public void UndoInsideAnOpenAction_IsRefused()
    {
        WithPier(out var designer);
        designer.CreateBerths("A", PierSide.Left, 0f, 5f);
        using var action = designer.BeginAction("Open");
        Assert.Throws<InvalidOperationException>(() => designer.Undo());
    }

    [Fact]
    public void RenamingAPierFromTheRenameTool_IsOneStep_AndOneUndoPutsItAllBack()
    {
        var marina = WithPier(out var designer);
        designer.CreateBerths("A", PierSide.Left, 0f, 10f);
        var steps = designer.UndoCount;
        designer.Tool = DesignTool.Rename;
        designer.ElementRenaming += (_, e) =>
        {
            e.NewPierId = "B";
            e.NewName = "Bravo";
            e.NewBerthPattern = "{pier}.{number}";
        };

        Assert.True(marina.TryProjectToScreen(new Vector3(0f, 0.5f, 0f), out var onPier));
        marina.Input.PointerMove(onPier.X, onPier.Y);
        marina.Input.PointerDown(onPier.X, onPier.Y, PointerButton.Left);
        marina.Input.PointerUp(onPier.X, onPier.Y, PointerButton.Left);

        Assert.Equal("Bravo", marina.GetPier("B")!.Name);
        Assert.Equal(new[] { "B.01", "B.02" }, marina.GetBerths().Select(berth => berth.Id));
        Assert.Equal(steps + 1, designer.UndoCount);

        Assert.True(designer.Undo());
        Assert.Equal("Pier A", marina.GetPier("A")!.Name);
        Assert.Null(marina.GetPier("B"));
        Assert.Equal(new[] { "A-L01", "A-L02" }, marina.GetBerths().Select(berth => berth.Id));
    }

    [Fact]
    public void ARenameThatCannotBeDone_ChangesNothing_AndSaysWhy()
    {
        var marina = WithPier(out var designer);
        marina.AddPier(new Pier("B", "Pier B", new Vector2(40, -30), 0f, 60f));
        designer.CreateBerths("A", PierSide.Left, 0f, 10f);
        var steps = designer.UndoCount;
        var failures = new List<DesignActionFailedEventArgs>();
        designer.ActionFailed += (_, e) => failures.Add(e);
        designer.Tool = DesignTool.Rename;
        designer.ElementRenaming += (_, e) =>
        {
            // A new name, and an id another pier already has: the name must not go through on its own.
            e.NewName = "Alpha";
            e.NewPierId = "B";
        };

        Assert.True(marina.TryProjectToScreen(new Vector3(0f, 0.5f, 0f), out var onPier));
        marina.Input.PointerMove(onPier.X, onPier.Y);
        marina.Input.PointerDown(onPier.X, onPier.Y, PointerButton.Left);
        marina.Input.PointerUp(onPier.X, onPier.Y, PointerButton.Left);

        Assert.Equal("Pier A", marina.GetPier("A")!.Name);
        Assert.Equal(new[] { "A-L01", "A-L02" }, marina.GetBerthsByPier("A").Select(berth => berth.Id));
        Assert.Equal(steps, designer.UndoCount);
        Assert.IsType<InvalidOperationException>(Assert.Single(failures).Exception);
    }

    [Fact]
    public void ChangingAPierId_IsOneStep_WithItsBerthsAndItsGeneratedName()
    {
        var marina = WithPier(out var designer);
        designer.CreateBerths("A", PierSide.Left, 0f, 10f);
        var steps = designer.UndoCount;

        designer.ChangePierId("A", "C");
        Assert.Equal("Pier C", marina.GetPier("C")!.Name);
        Assert.Equal(new[] { "C-L01", "C-L02" }, marina.GetBerths().Select(berth => berth.Id));
        Assert.Equal(steps + 1, designer.UndoCount);

        Assert.True(designer.Undo());
        Assert.Equal("Pier A", marina.GetPier("A")!.Name);
        Assert.Equal(new[] { "A-L01", "A-L02" }, marina.GetBerths().Select(berth => berth.Id));

        Assert.True(designer.Redo());
        Assert.Equal(new[] { "C-L01", "C-L02" }, marina.GetBerths().Select(berth => berth.Id));
    }
}
