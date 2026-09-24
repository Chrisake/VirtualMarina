using VirtualMarina.Core.Design;

namespace VirtualMarina.Designer.Common.Tests;

/// <summary>The one command table both apps take their menus, keys and help from.</summary>
public class DesignerCommandsTests
{
    [Fact]
    public void Every_command_has_a_distinct_name_and_a_label()
    {
        var names = DesignerCommands.All.Select(command => command.Name).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(DesignerCommands.All, command => Assert.False(string.IsNullOrWhiteSpace(command.PlainLabel)));
        Assert.All(DesignerCommands.All, command => Assert.DoesNotContain("&", command.PlainLabel, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(DesignerPlatform.Desktop)]
    [InlineData(DesignerPlatform.Browser)]
    public void No_key_runs_two_commands(DesignerPlatform platform)
    {
        var gestures = DesignerCommands.All.SelectMany(command => command.Gestures(platform)).ToList();

        Assert.Equal(gestures.Count, gestures.Distinct().Count());
    }

    [Fact]
    public void Redo_answers_both_ctrl_y_and_ctrl_shift_z_on_both_platforms()
    {
        var redo = DesignerCommands.Get(DesignerCommandId.Redo);

        foreach (var platform in new[] { DesignerPlatform.Desktop, DesignerPlatform.Browser })
        {
            Assert.Same(redo, DesignerCommands.Match(platform, "Y", KeyModifiers.Control));
            Assert.Same(redo, DesignerCommands.Match(platform, "z", KeyModifiers.Control | KeyModifiers.Shift));
            Assert.Equal("Ctrl+Y", redo.ShortcutText(platform));
        }

        Assert.True(redo.YieldsToTextFields);
        Assert.True(redo.Repeats);
        Assert.Same(DesignerCommands.Get(DesignerCommandId.Undo), DesignerCommands.Match(DesignerPlatform.Browser, "Z", KeyModifiers.Control));
    }

    [Fact]
    public void The_browser_moves_new_and_top_view_to_alt_because_chrome_keeps_ctrl_n_and_ctrl_t()
    {
        Assert.Equal("Ctrl+N", DesignerCommands.Get(DesignerCommandId.New).ShortcutText(DesignerPlatform.Desktop));
        Assert.Equal("Alt+N", DesignerCommands.Get(DesignerCommandId.New).ShortcutText(DesignerPlatform.Browser));
        Assert.Equal("Alt+T", DesignerCommands.Get(DesignerCommandId.TopView).ShortcutText(DesignerPlatform.Browser));
        Assert.Null(DesignerCommands.Match(DesignerPlatform.Browser, "N", KeyModifiers.Control));
        Assert.Null(DesignerCommands.Get(DesignerCommandId.Exit).ShortcutText(DesignerPlatform.Browser));
    }

    [Fact]
    public void Tool_letters_stay_clear_of_the_views_own_keys()
    {
        var viewKeys = new[] { "W", "A", "S", "D" };
        var tools = DesignerCommands.All.Where(command => command.ViewOnly).ToList();

        Assert.NotEmpty(tools);
        Assert.All(tools, command =>
        {
            var gesture = Assert.Single(command.DesktopGestures);
            Assert.True(gesture.IsLetter);
            Assert.Equal(KeyModifiers.None, gesture.Modifiers);
            Assert.DoesNotContain(gesture.Key, viewKeys);
            Assert.NotNull(command.Tool);
            Assert.Same(command, DesignerCommands.ForTool(command.Tool.Value));
        });
        Assert.Null(DesignerCommands.ForTool(DesignTool.MeasureScale));
    }

    [Fact]
    public void The_browser_table_has_one_entry_per_key_with_its_rules()
    {
        var table = DesignerCommands.ForBrowser();

        Assert.Equal(DesignerCommands.All.Sum(command => command.BrowserGestures.Count), table.Count);
        var undo = Assert.Single(table, entry => entry.Name == "undo");
        Assert.Equal(new BrowserShortcut("undo", "Z", true, false, false, Editing: false, Repeats: true, ViewOnly: false), undo);
        var save = Assert.Single(table, entry => entry.Name == "save");
        Assert.True(save.Editing);
        Assert.False(save.Repeats);
        Assert.Equal(2, table.Count(entry => entry.Name == "redo"));
        Assert.Contains(table, entry => entry is { Name: "toolErase", Key: "E", ViewOnly: true });
        Assert.DoesNotContain(table, entry => entry.Name == "exit");
    }

    [Theory]
    [InlineData("Ctrl+Shift+S", "S", KeyModifiers.Control | KeyModifiers.Shift, "Ctrl+Shift+S")]
    [InlineData("alt+n", "N", KeyModifiers.Alt, "Alt+N")]
    [InlineData("Esc", "Escape", KeyModifiers.None, "Esc")]
    [InlineData("F2", "F2", KeyModifiers.None, "F2")]
    [InlineData("Cmd+Z", "Z", KeyModifiers.Control, "Ctrl+Z")]
    public void Gestures_parse_and_print(string text, string key, KeyModifiers modifiers, string display)
    {
        var gesture = KeyGesture.Parse(text);

        Assert.Equal(new KeyGesture(key, modifiers), gesture);
        Assert.Equal(display, gesture.DisplayText);
        Assert.Equal(display, gesture.ToString());
    }

    [Fact]
    public void A_gesture_without_a_key_is_refused()
    {
        Assert.Throws<FormatException>(() => KeyGesture.Parse("Ctrl+Shift"));
        Assert.Throws<ArgumentException>(() => DesignerCommands.Get(DesignerCommandId.Tool));
    }

    [Fact]
    public void Letters_match_in_either_case_and_other_keys_exactly()
    {
        Assert.True(new KeyGesture("S", KeyModifiers.Control).Matches("s", KeyModifiers.Control));
        Assert.False(new KeyGesture("S", KeyModifiers.Control).Matches("s", KeyModifiers.Control | KeyModifiers.Shift));
        Assert.False(new KeyGesture("Escape").Matches("escape", KeyModifiers.None));
        Assert.True(new KeyGesture("F1").Matches("F1", KeyModifiers.None));
    }

    [Fact]
    public void Find_looks_commands_up_by_name()
    {
        Assert.Equal(DesignerCommandId.SaveAs, DesignerCommands.Find("saveAs")?.Id);
        Assert.Null(DesignerCommands.Find("SaveAs"));
        Assert.Null(DesignerCommands.Find(null));
    }

    [Theory]
    [InlineData(DesignerPlatform.Desktop, "Ctrl+Y, Ctrl+Shift+Z", "Ctrl+N")]
    [InlineData(DesignerPlatform.Browser, "Ctrl+Y, Ctrl+Shift+Z", "Alt+N")]
    public void The_shortcuts_help_lists_the_commands_the_camera_keys_and_the_tools(DesignerPlatform platform, string redo, string newDesign)
    {
        var help = ShortcutHelp.Build(platform);

        Assert.Contains(redo, help, StringComparison.Ordinal);
        Assert.Contains(newDesign, help, StringComparison.Ordinal);
        Assert.Contains("Home", help, StringComparison.Ordinal);
        Assert.Contains("W A S D", help, StringComparison.Ordinal);
        foreach (var tool in DesignerCommands.All.Where(command => command.ViewOnly))
        {
            Assert.Contains(tool.PlainLabel, help, StringComparison.Ordinal);
        }
    }
}
