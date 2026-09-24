using System.Text;
using VirtualMarina.Designer.Resources;

namespace VirtualMarina.Designer;

/// <summary>
/// The text of Help ▸ Shortcuts. The mouse, camera and drawing keys are written out in the resources; the menu
/// commands and the tool letters are listed from <see cref="DesignerCommands"/>, so the help cannot fall behind them.
/// </summary>
public static class ShortcutHelp
{
    /// <summary>Width of the key column; longer keys push their description along.</summary>
    private const int KeyColumn = 22;

    /// <summary>The whole help text for a platform, sections separated by blank lines.</summary>
    /// <param name="platform">Desktop or browser, which differ in a few keys.</param>
    public static string Build(DesignerPlatform platform)
    {
        var text = new StringBuilder();
        text.AppendLine(Strings.ShortcutsMouse.TrimEnd());
        text.AppendLine();
        text.AppendLine(Strings.ShortcutsCamera.TrimEnd());
        text.AppendLine();
        text.AppendLine(Strings.ShortcutsDrawing.TrimEnd());

        text.AppendLine();
        text.AppendLine(Strings.ShortcutsTools);
        foreach (var command in DesignerCommands.All.Where(command => command.ViewOnly))
        {
            Line(text, command.ShortcutText(platform), command.PlainLabel);
        }

        text.AppendLine();
        text.AppendLine(Strings.ShortcutsCommands);
        foreach (var command in DesignerCommands.All.Where(command => !command.ViewOnly && command.Id != DesignerCommandId.Escape))
        {
            var gestures = command.Gestures(platform);
            if (gestures.Count == 0) continue;
            Line(text, string.Join(", ", gestures.Select(gesture => gesture.DisplayText)), command.PlainLabel.TrimEnd('…', '.'));
        }

        return text.ToString().TrimEnd();
    }

    private static void Line(StringBuilder text, string? keys, string description) =>
        text.Append("  ").Append((keys ?? string.Empty).PadRight(KeyColumn - 2)).Append(' ').AppendLine(description);
}
