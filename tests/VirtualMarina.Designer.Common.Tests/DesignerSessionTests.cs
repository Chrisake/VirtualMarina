using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Serialization;
using VirtualMarina.Designer.Resources;

namespace VirtualMarina.Designer.Common.Tests;

/// <summary>The file, dirty and title workflow both Designer apps run on.</summary>
public class DesignerSessionTests
{
    private static DesignerSession NewSession(out FakeDialogs dialogs, out MarinaVisualizer marina)
    {
        marina = new MarinaVisualizer();
        dialogs = new FakeDialogs();
        var session = new DesignerSession(marina, dialogs, "Designer tests 1.0", new ManualClock());
        Assert.True(session.NewAsync(askToSave: false).Result);
        return session;
    }

    private static Pier DrawPier(MarinaVisualizer marina) =>
        marina.Designer.CreatePier(new Vector2(0, 0), new Vector2(0, 60)) ?? throw new InvalidOperationException("No pier drawn.");

    [Fact]
    public void A_new_design_is_clean_and_titled_after_the_marina()
    {
        using var session = NewSession(out _, out var marina);

        Assert.False(session.IsDirty);
        Assert.Null(session.FileName);
        Assert.Null(session.Document);
        Assert.Equal(Strings.NewMarinaName, marina.MarinaName);
        Assert.Equal(DesignerText.WindowTitle(false, Strings.NewMarinaName), session.Title);
        Assert.Equal(Strings.LogNewMarina, session.Log.Latest?.Message);
        Assert.Equal(DesignTool.Navigate, marina.Designer.Tool);
        Assert.False(marina.Designer.CanUndo);
    }

    [Fact]
    public void Drawing_marks_the_design_dirty_once_and_logs_it()
    {
        using var session = NewSession(out _, out var marina);
        var titles = 0;
        session.TitleChanged += (_, _) => titles++;

        var pier = DrawPier(marina);

        Assert.True(session.IsDirty);
        Assert.StartsWith(Strings.UnsavedMarker, session.Title, StringComparison.Ordinal);
        Assert.Equal(1, titles);
        Assert.Contains(session.Log.Entries, entry => entry.Message == Strings.Format(Strings.LogAddedPier, pier.Id, pier.Length, pier.Width));
    }

    [Fact]
    public void Drawing_a_coast_is_logged_as_a_coast_not_as_zero_berths()
    {
        using var session = NewSession(out _, out var marina);

        var shore = marina.Designer.CreateShoreline([new Vector2(-100, 0), new Vector2(100, 0)], landOnLeft: true);

        Assert.NotNull(shore);
        Assert.True(session.IsDirty);
        Assert.Equal(Strings.Format(Strings.LogCoastDrawn, shore.Points.Count), session.Log.Latest?.Message);
        Assert.DoesNotContain(session.Log.Entries, entry => entry.Message.StartsWith("Added 0", StringComparison.Ordinal));
    }

    [Fact]
    public void Removing_the_coast_is_logged_and_undoable()
    {
        using var session = NewSession(out _, out var marina);
        marina.Designer.CreateShoreline([new Vector2(-100, 0), new Vector2(100, 0)], landOnLeft: true);

        Assert.True(session.RemoveShoreline());
        Assert.Null(marina.Shoreline);
        Assert.Equal(Strings.LogCoastRemoved, session.Log.Latest?.Message);
        Assert.False(session.RemoveShoreline());

        Assert.True(marina.Designer.Undo());
        Assert.NotNull(marina.Shoreline);
    }

    [Fact]
    public void Undo_and_redo_mark_dirty_and_are_logged()
    {
        using var session = NewSession(out _, out var marina);
        DrawPier(marina);

        Assert.True(marina.Designer.Undo());
        Assert.StartsWith(Strings.Format(Strings.LogUndone, string.Empty), session.Log.Latest!.Message, StringComparison.Ordinal);
        Assert.True(marina.Designer.Redo());
        Assert.Equal(Strings.Format(Strings.LogRedone, marina.Designer.UndoDescription), session.Log.Latest.Message);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void A_refused_change_is_reported_as_a_notice()
    {
        using var session = NewSession(out _, out var marina);
        var notices = new List<string>();
        session.Notice += (_, e) => notices.Add(e.Message);
        var failed = new DesignActionFailedEventArgs("Rename pier A", new InvalidOperationException("taken"));

        session.Report(DesignerLogText.Failed(failed));

        Assert.Single(notices);
        Assert.Contains("taken", notices[0], StringComparison.Ordinal);
        Assert.Equal(notices[0], session.Log.Latest?.Message);
        Assert.NotNull(marina);
    }

    [Fact]
    public async Task Save_writes_through_the_dialogs_and_marks_clean()
    {
        using var session = NewSession(out var dialogs, out var marina);
        DrawPier(marina);
        var written = new List<string>();
        dialogs.Saves.Enqueue(FakeDialogs.SavesAs("west.marina.json", "/tmp/west.marina.json", written));

        Assert.True(await session.SaveAsync());

        Assert.False(session.IsDirty);
        Assert.Equal("west.marina.json", session.FileName);
        Assert.Equal("/tmp/west.marina.json", session.FileLocation);
        Assert.NotNull(session.Document);
        Assert.Null(dialogs.SaveRequests[0].Location);
        Assert.Equal("New marina" + MarinaDocument.FileExtension, dialogs.SaveRequests[0].SuggestedName);
        Assert.Single(MarinaDocument.Parse(written[0]).Layout.Piers);
        Assert.Equal(DesignerText.WindowTitle(false, "west.marina.json"), session.Title);
    }

    [Fact]
    public async Task Save_again_goes_back_to_the_same_file_and_save_as_asks_again()
    {
        using var session = NewSession(out var dialogs, out var marina);
        dialogs.Saves.Enqueue(FakeDialogs.SavesAs("a.marina.json", "/a"));
        dialogs.Saves.Enqueue(FakeDialogs.SavesAs("a.marina.json", "/a"));
        dialogs.Saves.Enqueue(FakeDialogs.SavesAs("b.marina.json", "/b"));
        await session.SaveAsync();

        DrawPier(marina);
        await session.SaveAsync();
        await session.SaveAsync(saveAs: true);

        Assert.Equal("/a", dialogs.SaveRequests[1].Location);
        Assert.Null(dialogs.SaveRequests[2].Location);
        Assert.Equal("a.marina.json", dialogs.SaveRequests[2].SuggestedName);
        Assert.Equal("/b", session.FileLocation);
    }

    [Fact]
    public async Task A_cancelled_save_leaves_the_design_dirty()
    {
        using var session = NewSession(out var dialogs, out var marina);
        DrawPier(marina);
        dialogs.Saves.Enqueue(_ => null);

        Assert.False(await session.SaveAsync());

        Assert.True(session.IsDirty);
        Assert.Null(session.FileName);
    }

    [Fact]
    public async Task A_failed_save_warns_and_leaves_the_design_dirty()
    {
        using var session = NewSession(out var dialogs, out var marina);
        DrawPier(marina);
        dialogs.Saves.Enqueue(_ => throw new UnauthorizedAccessException("read-only"));

        Assert.False(await session.SaveAsync());

        Assert.True(session.IsDirty);
        var alert = Assert.Single(dialogs.Alerts);
        Assert.Equal(DesignerMessageKind.Warning, alert.Kind);
        Assert.Contains("read-only", alert.Message, StringComparison.Ordinal);
        Assert.Contains(Strings.SaveFailed, session.Log.Latest!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Open_keeps_the_document_so_what_this_version_does_not_know_is_saved_again()
    {
        using var session = NewSession(out var dialogs, out var marina);
        var original = MarinaDocument.FromVisualizer(new MarinaVisualizer { MarinaName = "Porto" });
        original.Description = "Summer layout";
        using (var erp = System.Text.Json.JsonDocument.Parse("{\"site\":42}")) original.Extensions["erp"] = erp.RootElement.Clone();
        dialogs.Picks.Enqueue(FakeDialogs.File(original, "porto.marina.json", "/p"));
        var replaced = 0;
        session.DocumentReplaced += (_, _) => replaced++;

        Assert.True(await session.OpenAsync());
        Assert.Equal(1, replaced);
        Assert.Equal("Porto", marina.MarinaName);
        Assert.Equal("porto.marina.json", session.FileName);
        Assert.False(session.IsDirty);

        DrawPier(marina);
        var written = new List<string>();
        dialogs.Saves.Enqueue(FakeDialogs.SavesAs("porto.marina.json", "/p", written));
        Assert.True(await session.SaveAsync());

        Assert.Equal("/p", dialogs.SaveRequests[0].Location);
        var saved = MarinaDocument.Parse(written[0]);
        Assert.Equal("Summer layout", saved.Description);
        Assert.True(saved.Extensions.ContainsKey("erp"));
        Assert.Single(saved.Layout.Piers);
        Assert.Equal("Designer tests 1.0", saved.Generator);
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_warns_and_changes_nothing()
    {
        using var session = NewSession(out var dialogs, out var marina);
        DrawPier(marina);
        dialogs.SaveChoices.Enqueue(SaveChangesChoice.Discard);
        dialogs.Picks.Enqueue(new DesignerOpenedFile("bad.json", "/bad", () => MarinaDocument.Parse("{ not json")));

        Assert.False(await session.OpenAsync());

        Assert.Single(dialogs.Alerts);
        Assert.Single(marina.GetPiers());
        Assert.Null(session.FileName);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public async Task Unsaved_changes_are_asked_about_and_cancel_keeps_them()
    {
        using var session = NewSession(out var dialogs, out var marina);
        DrawPier(marina);
        dialogs.SaveChoices.Enqueue(SaveChangesChoice.Cancel);

        Assert.False(await session.NewAsync());

        Assert.Single(dialogs.Asked);
        Assert.Contains(Strings.NewMarinaName, dialogs.Asked[0], StringComparison.Ordinal);
        Assert.Single(marina.GetPiers());
        Assert.False(session.IsBusy);
    }

    [Fact]
    public async Task Discard_goes_on_without_saving()
    {
        using var session = NewSession(out var dialogs, out var marina);
        DrawPier(marina);
        dialogs.SaveChoices.Enqueue(SaveChangesChoice.Discard);

        Assert.True(await session.NewAsync());

        Assert.Empty(marina.GetPiers());
        Assert.Empty(dialogs.SaveRequests);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public async Task Save_first_saves_and_then_goes_on_but_not_when_the_save_is_cancelled()
    {
        using var session = NewSession(out var dialogs, out var marina);
        DrawPier(marina);
        dialogs.SaveChoices.Enqueue(SaveChangesChoice.Save);
        dialogs.Saves.Enqueue(_ => null);

        Assert.False(await session.ConfirmDiscardChangesAsync());
        Assert.Single(marina.GetPiers());

        dialogs.SaveChoices.Enqueue(SaveChangesChoice.Save);
        dialogs.Saves.Enqueue(FakeDialogs.SavesAs("x.marina.json", "/x"));
        Assert.True(await session.NewAsync());
        Assert.Empty(marina.GetPiers());
    }

    [Fact]
    public async Task Nothing_is_asked_when_there_is_nothing_to_lose()
    {
        using var session = NewSession(out var dialogs, out _);

        Assert.True(await session.ConfirmDiscardChangesAsync());
        Assert.Empty(dialogs.Asked);
    }

    [Fact]
    public async Task Renaming_the_marina_marks_dirty_and_retitles()
    {
        using var session = NewSession(out var dialogs, out var marina);
        dialogs.Answers.Enqueue(["  Porto Vecchio "]);

        Assert.True(await session.EditMarinaPropertiesAsync());

        Assert.Equal("Porto Vecchio", marina.MarinaName);
        Assert.True(session.IsDirty);
        Assert.Contains("Porto Vecchio", session.Title, StringComparison.Ordinal);
        Assert.False(await session.EditMarinaPropertiesAsync());
    }

    [Fact]
    public void Berth_labels_toggle_is_saved_with_the_design()
    {
        using var session = NewSession(out _, out var marina);
        var before = marina.BerthLabelMode;

        session.ToggleBerthLabels();

        Assert.NotEqual(before, marina.BerthLabelMode);
        Assert.True(session.IsDirty);
        session.ToggleBerthLabels();
        Assert.Equal(before, marina.BerthLabelMode);
    }

    [Fact]
    public void The_look_panel_draws_full_haze_and_the_others_damp_it()
    {
        using var session = NewSession(out _, out var marina);
        var changes = 0;
        session.SidePanelChanged += (_, _) => changes++;

        session.ShowSidePanel(DesignerSidePanel.Look);
        Assert.Equal(1f, marina.Designer.FogFactor);
        Assert.Equal(DesignerSidePanel.Look, session.SidePanel);

        session.ToggleSidePanel(DesignerSidePanel.Look);
        Assert.Equal(DesignerSession.DesigningFogFactor, marina.Designer.FogFactor);
        Assert.Equal(DesignerSidePanel.Tools, session.SidePanel);

        session.ToggleSidePanel(DesignerSidePanel.Cameras);
        Assert.Equal(DesignerSidePanel.Cameras, session.SidePanel);
        Assert.Equal(3, changes);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void The_log_keeps_the_newest_first_and_no_more_than_its_capacity()
    {
        var clock = new ManualClock();
        var log = new ActivityLog(clock);
        var added = 0;
        log.Added += (_, _) => added++;

        for (var i = 0; i < ActivityLog.Capacity + 10; i++) log.Add($"line {i}");
        log.Add("   ");
        log.Add(null);

        Assert.Equal(ActivityLog.Capacity, log.Count);
        Assert.Equal(ActivityLog.Capacity + 10, added);
        Assert.Equal($"line {ActivityLog.Capacity + 9}", log.Latest?.Message);
        Assert.Equal("line 10", log.Entries.Last().Message);
        Assert.StartsWith("09:30:00", log.Latest!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Disposing_stops_listening()
    {
        var session = NewSession(out _, out var marina);
        session.Dispose();

        DrawPier(marina);

        Assert.False(session.IsDirty);
    }

    private static void ClickAt(MarinaVisualizer marina, Vector2 plan, float height)
    {
        Assert.True(marina.TryProjectToScreen(MarinaMath.ToWorld(plan, height), out var screen));
        marina.Input.PointerMove(screen.X, screen.Y);
        marina.Input.PointerDown(screen.X, screen.Y, PointerButton.Left);
        marina.Input.PointerUp(screen.X, screen.Y, PointerButton.Left);
    }

    [Fact]
    public void Undo_while_drawing_takes_back_the_last_point_like_ctrl_z_in_the_view()
    {
        using var session = NewSession(out _, out var marina);
        marina.SetViewportSize(1000, 800);
        marina.Camera.SetPose(new CameraPose(Vector3.Zero, 0f, 89f, 150f), immediate: true);
        DrawPier(marina);
        var designer = marina.Designer;
        designer.IsActive = true;
        designer.Tool = DesignTool.DrawLandArea;
        ClickAt(marina, new Vector2(-40, 0), designer.LandHeight);
        ClickAt(marina, new Vector2(-20, 0), designer.LandHeight);
        Assert.True(session.CanUndo);
        Assert.Equal(Strings.UndoPointTip, DesignerText.UndoTip(designer));

        Assert.True(session.Undo());

        Assert.Single(designer.DraftPoints);
        Assert.Single(marina.GetPiers());
        Assert.True(session.Undo());
        Assert.False(designer.HasDraft);
        Assert.NotEqual(Strings.UndoPointTip, DesignerText.UndoTip(designer));
        Assert.True(session.Undo());
        Assert.Empty(marina.GetPiers());
        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);
        Assert.True(session.Redo());
        Assert.Single(marina.GetPiers());
    }

    [Fact]
    public void An_undo_that_is_refused_is_logged_not_thrown()
    {
        using var session = NewSession(out _, out var marina);
        marina.AddLandArea(new LandArea("lawn", [new Vector2(0, 0), new Vector2(60, 0), new Vector2(60, 40), new Vector2(0, 40)], 1f, LandKind.Grass));
        marina.Designer.PlantTrees("lawn");
        marina.UpdateLandArea(marina.GetLandArea("lawn")! with { Trees = [] });
        var notices = new List<string>();
        session.Notice += (_, e) => notices.Add(e.Message);

        Assert.False(session.Undo());

        Assert.Single(notices);
        Assert.Equal(notices[0], session.Log.Latest?.Message);
        Assert.True(marina.Designer.CanUndo);
    }
}
