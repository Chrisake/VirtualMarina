using VirtualMarina.Core.Design;
using VirtualMarina.Designer.Resources;

namespace VirtualMarina.Designer;

/// <summary>Every command either Designer offers from a menu, a toolbar or the keyboard.</summary>
public enum DesignerCommandId
{
    /// <summary>File ▸ New.</summary>
    New = 0,

    /// <summary>File ▸ Open.</summary>
    Open,

    /// <summary>File ▸ Save.</summary>
    Save,

    /// <summary>File ▸ Save As.</summary>
    SaveAs,

    /// <summary>File ▸ Reference Image.</summary>
    LoadImage,

    /// <summary>File ▸ Exit (desktop only).</summary>
    Exit,

    /// <summary>Edit ▸ Undo.</summary>
    Undo,

    /// <summary>Edit ▸ Redo.</summary>
    Redo,

    /// <summary>Edit ▸ Cancel Drawing.</summary>
    CancelDraft,

    /// <summary>Edit ▸ Rename: picks the rename tool.</summary>
    Rename,

    /// <summary>Edit ▸ Properties.</summary>
    MarinaProperties,

    /// <summary>View ▸ Top View.</summary>
    TopView,

    /// <summary>View ▸ Fit Marina.</summary>
    FitMarina,

    /// <summary>View ▸ Fit Image.</summary>
    FitImage,

    /// <summary>View ▸ Activity Log.</summary>
    ShowLog,

    /// <summary>Marina ▸ Appearance: the look settings beside the view.</summary>
    Appearance,

    /// <summary>Marina ▸ Cameras: the saved and automatic views beside the view.</summary>
    Cameras,

    /// <summary>Marina ▸ Berth Labels.</summary>
    BerthLabels,

    /// <summary>Help ▸ Shortcuts.</summary>
    Shortcuts,

    /// <summary>Help ▸ About.</summary>
    About,

    /// <summary>Esc outside the view: closes a menu, drops the drawing, goes back to Navigate.</summary>
    Escape,

    /// <summary>A drawing tool, picked by its letter while the view has the focus.</summary>
    Tool,
}

/// <summary>Modifier keys of a <see cref="KeyGesture"/>.</summary>
[Flags]
public enum KeyModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>Ctrl, or Cmd on a Mac keyboard in the browser.</summary>
    Control = 1,

    /// <summary>Shift.</summary>
    Shift = 2,

    /// <summary>Alt, or Option on a Mac keyboard.</summary>
    Alt = 4,
}

/// <summary>The two kinds of window the Designer runs in. They differ only where a browser keeps a key for itself.</summary>
public enum DesignerPlatform
{
    /// <summary>The native Windows designer.</summary>
    Desktop = 0,

    /// <summary>The Blazor designer, in a browser tab or the launcher's app window.</summary>
    Browser = 1,
}

/// <summary>
/// One key combination: a key name with its modifiers. Key names are those of WinForms' <c>Keys</c> and of a
/// browser's <c>KeyboardEvent.key</c> alike: a capital letter ("S"), a function key ("F2") or "Escape".
/// </summary>
/// <param name="Key">The key: "A" to "Z", "F1" to "F12", or "Escape".</param>
/// <param name="Modifiers">The modifiers held with it.</param>
public sealed record KeyGesture(string Key, KeyModifiers Modifiers = KeyModifiers.None)
{
    /// <summary>True for a letter key, which is matched by the letter it types rather than where it sits.</summary>
    public bool IsLetter => Key.Length == 1 && char.IsAsciiLetterUpper(Key[0]);

    /// <summary>How a menu shows it, e.g. "Ctrl+Shift+S" or "Esc".</summary>
    public string DisplayText
    {
        get
        {
            var parts = new List<string>(4);
            if (Modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
            parts.Add(Key == "Escape" ? "Esc" : Key);
            return string.Join("+", parts);
        }
    }

    /// <summary>Reads "Ctrl+Shift+S", "Alt+N", "F2" or "Esc".</summary>
    /// <param name="text">The gesture as a menu would show it.</param>
    /// <exception cref="FormatException">The text names no key.</exception>
    public static KeyGesture Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var modifiers = KeyModifiers.None;
        string? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL" or "CMD": modifiers |= KeyModifiers.Control; break;
                case "SHIFT": modifiers |= KeyModifiers.Shift; break;
                case "ALT" or "OPTION": modifiers |= KeyModifiers.Alt; break;
                case "ESC" or "ESCAPE": key = "Escape"; break;
                default: key = part.Length == 1 ? part.ToUpperInvariant() : part; break;
            }
        }

        return key is null ? throw new FormatException($"'{text}' names no key.") : new KeyGesture(key, modifiers);
    }

    /// <summary>True when a key press is this gesture.</summary>
    /// <param name="key">The key pressed, named as <see cref="Key"/> is (letters in either case).</param>
    /// <param name="modifiers">The modifiers held.</param>
    public bool Matches(string? key, KeyModifiers modifiers) =>
        modifiers == Modifiers && string.Equals(key, Key, IsLetter ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => DisplayText;
}

/// <summary>One entry of the command table: what it is called and which keys run it on each platform.</summary>
public sealed class DesignerCommand
{
    private readonly Func<string> _label;

    internal DesignerCommand(
        DesignerCommandId id,
        string name,
        Func<string> label,
        IReadOnlyList<KeyGesture> desktop,
        IReadOnlyList<KeyGesture>? browser = null,
        bool yieldsToTextFields = false,
        bool repeats = false)
    {
        Id = id;
        Name = name;
        _label = label;
        DesktopGestures = desktop;
        BrowserGestures = browser ?? desktop;
        YieldsToTextFields = yieldsToTextFields;
        Repeats = repeats;
    }

    /// <summary>Which command this is. Every tool shares <see cref="DesignerCommandId.Tool"/>; <see cref="Tool"/> tells them apart.</summary>
    public DesignerCommandId Id { get; }

    /// <summary>The command's name where it has to be a string, e.g. between the Blazor app and its script: "saveAs".</summary>
    public string Name { get; }

    /// <summary>The menu text in the current language, with its <c>&amp;</c> accelerator.</summary>
    public string Label => _label();

    /// <summary>The menu text without the accelerator, for the browser and for help text.</summary>
    public string PlainLabel => DesignerText.StripMnemonic(Label);

    /// <summary>The keys that run it in the desktop designer; the first is the one its menu shows.</summary>
    public IReadOnlyList<KeyGesture> DesktopGestures { get; }

    /// <summary>The keys that run it in the browser, where Chrome keeps Ctrl+N and Ctrl+T for itself.</summary>
    public IReadOnlyList<KeyGesture> BrowserGestures { get; }

    /// <summary>
    /// True when a field being typed in keeps these keys: Ctrl+Z undoes the typing rather than the last design step,
    /// and Esc and F2 leave the tool alone.
    /// </summary>
    public bool YieldsToTextFields { get; }

    /// <summary>True when holding the key down runs the command again with every repeat, as holding Ctrl+Z undoes step after step.</summary>
    public bool Repeats { get; }

    /// <summary>
    /// The tool a single-letter key picks, or null for a menu command. Tool keys only work while the 3D view has the
    /// focus, never while a field is being typed in or the focus is anywhere else.
    /// </summary>
    public DesignTool? Tool { get; internal init; }

    /// <summary>True for a tool's single-letter key, which only the 3D view listens for.</summary>
    public bool ViewOnly => Tool is not null;

    /// <summary>The keys that run it on a platform.</summary>
    /// <param name="platform">Desktop or browser.</param>
    public IReadOnlyList<KeyGesture> Gestures(DesignerPlatform platform) =>
        platform == DesignerPlatform.Browser ? BrowserGestures : DesktopGestures;

    /// <summary>The key a menu shows for it on a platform, or null when it has none.</summary>
    /// <param name="platform">Desktop or browser.</param>
    public KeyGesture? PrimaryGesture(DesignerPlatform platform) => Gestures(platform) is { Count: > 0 } gestures ? gestures[0] : null;

    /// <summary>The shortcut text a menu shows for it on a platform, or null.</summary>
    /// <param name="platform">Desktop or browser.</param>
    public string? ShortcutText(DesignerPlatform platform) => PrimaryGesture(platform)?.DisplayText;
}

/// <summary>
/// One key of the command table as the Blazor designer's script receives it. Serialized to camelCase JSON by the
/// JS interop, so the script reads <c>name</c>, <c>key</c>, <c>ctrl</c> and so on.
/// </summary>
/// <param name="Name">The command it runs, <see cref="DesignerCommand.Name"/>.</param>
/// <param name="Key">The key, named as <c>KeyboardEvent.key</c> names it; letters in capitals.</param>
/// <param name="Ctrl">Ctrl (or Cmd) held.</param>
/// <param name="Shift">Shift held.</param>
/// <param name="Alt">Alt (or Option) held.</param>
/// <param name="Editing">False when a field being typed in keeps the key for itself.</param>
/// <param name="Repeats">True when a held key runs the command on every repeat.</param>
/// <param name="ViewOnly">True for a tool's letter, which only counts while the 3D view has the focus.</param>
public sealed record BrowserShortcut(string Name, string Key, bool Ctrl, bool Shift, bool Alt, bool Editing, bool Repeats, bool ViewOnly);

/// <summary>
/// The one table of the Designer's commands and their keys. The WinForms designer builds its menu shortcuts from it,
/// the Blazor designer hands it to its script, and the Shortcuts help is written from it, so none of them can drift.
/// </summary>
public static class DesignerCommands
{
    private static readonly KeyGesture[] None = [];

    /// <summary>Every command, menu commands first and then the tools, in menu order.</summary>
    public static IReadOnlyList<DesignerCommand> All { get; } =
    [
        Command(DesignerCommandId.New, "new", () => Strings.MenuNew, ["Ctrl+N"], browser: ["Alt+N"]),
        Command(DesignerCommandId.Open, "open", () => Strings.MenuOpen, ["Ctrl+O"]),
        Command(DesignerCommandId.Save, "save", () => Strings.MenuSave, ["Ctrl+S"]),
        Command(DesignerCommandId.SaveAs, "saveAs", () => Strings.MenuSaveAs, ["Ctrl+Shift+S"]),
        Command(DesignerCommandId.LoadImage, "loadImage", () => Strings.MenuLoadImage, ["Ctrl+I"]),
        Command(DesignerCommandId.Exit, "exit", () => Strings.MenuExit, ["Alt+F4"], browser: []),
        Command(DesignerCommandId.Undo, "undo", () => Strings.MenuUndo, ["Ctrl+Z"], yieldsToTextFields: true, repeats: true),
        Command(DesignerCommandId.Redo, "redo", () => Strings.MenuRedo, ["Ctrl+Y", "Ctrl+Shift+Z"], yieldsToTextFields: true, repeats: true),
        Command(DesignerCommandId.CancelDraft, "cancelDraft", () => Strings.MenuCancelDraft, []),
        Command(DesignerCommandId.Rename, "rename", () => Strings.MenuRename, ["F2"], yieldsToTextFields: true),
        Command(DesignerCommandId.MarinaProperties, "properties", () => Strings.MenuMarinaProperties, []),
        Command(DesignerCommandId.TopView, "topView", () => Strings.MenuTopView, ["Ctrl+T"], browser: ["Alt+T"]),
        Command(DesignerCommandId.FitMarina, "fitMarina", () => Strings.MenuFitMarina, ["Ctrl+F"]),
        Command(DesignerCommandId.FitImage, "fitImage", () => Strings.MenuFitImage, []),
        Command(DesignerCommandId.ShowLog, "showLog", () => Strings.MenuShowLog, []),
        Command(DesignerCommandId.Appearance, "appearance", () => Strings.MenuAppearance, []),
        Command(DesignerCommandId.Cameras, "cameras", () => Strings.MenuCameras, []),
        Command(DesignerCommandId.BerthLabels, "berthLabels", () => Strings.MenuBerthLabels, []),
        Command(DesignerCommandId.Shortcuts, "shortcuts", () => Strings.MenuShortcuts, ["F1"]),
        Command(DesignerCommandId.About, "about", () => Strings.MenuAbout, []),
        Command(DesignerCommandId.Escape, "escape", () => Strings.ToolNavigate, ["Escape"], yieldsToTextFields: true),

        // The tools, on letters that none of the view's own keys (W A S D for moving about) use.
        Tool(DesignTool.Navigate, "N", () => Strings.ToolNavigate),
        Tool(DesignTool.SelectArea, "X", () => Strings.ToolSelect),
        Tool(DesignTool.DrawShoreline, "C", () => Strings.ToolCoast),
        Tool(DesignTool.DrawLandArea, "L", () => Strings.ToolLand),
        Tool(DesignTool.DrawPier, "P", () => Strings.ToolPier),
        Tool(DesignTool.AddBerths, "B", () => Strings.ToolBerths),
        Tool(DesignTool.AddLandBerths, "Y", () => Strings.ToolAshore),
        Tool(DesignTool.PlantTrees, "T", () => Strings.ToolTrees),
        Tool(DesignTool.EditServices, "U", () => Strings.ToolServices),
        Tool(DesignTool.Rename, "R", () => Strings.ToolRename),
        Tool(DesignTool.Erase, "E", () => Strings.ToolErase),
    ];

    /// <summary>The menu command with this id. For the tools, use <see cref="ForTool"/>.</summary>
    /// <param name="id">The command.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is <see cref="DesignerCommandId.Tool"/>.</exception>
    public static DesignerCommand Get(DesignerCommandId id) =>
        id == DesignerCommandId.Tool
            ? throw new ArgumentException("Every tool has a command of its own; use ForTool.", nameof(id))
            : All.First(command => command.Id == id);

    /// <summary>The command that picks a tool, or null for a tool without a key (moving or scaling the picture).</summary>
    /// <param name="tool">The tool.</param>
    public static DesignerCommand? ForTool(DesignTool tool) => All.FirstOrDefault(command => command.Tool == tool);

    /// <summary>The command with this <see cref="DesignerCommand.Name"/>, or null.</summary>
    /// <param name="name">The name, e.g. "saveAs".</param>
    public static DesignerCommand? Find(string? name) =>
        All.FirstOrDefault(command => string.Equals(command.Name, name, StringComparison.Ordinal));

    /// <summary>The command a key press runs on a platform, or null when it runs none.</summary>
    /// <param name="platform">Desktop or browser.</param>
    /// <param name="key">The key, named as <see cref="KeyGesture.Key"/> is.</param>
    /// <param name="modifiers">The modifiers held.</param>
    public static DesignerCommand? Match(DesignerPlatform platform, string? key, KeyModifiers modifiers) =>
        All.FirstOrDefault(command => command.Gestures(platform).Any(gesture => gesture.Matches(key, modifiers)));

    /// <summary>The whole table as the Blazor designer's script wants it: one entry per key, the most specific chords first.</summary>
    public static IReadOnlyList<BrowserShortcut> ForBrowser() =>
        All.SelectMany(command => command.BrowserGestures.Select(gesture => new BrowserShortcut(
                command.Name,
                gesture.Key,
                gesture.Modifiers.HasFlag(KeyModifiers.Control),
                gesture.Modifiers.HasFlag(KeyModifiers.Shift),
                gesture.Modifiers.HasFlag(KeyModifiers.Alt),
                Editing: !command.YieldsToTextFields,
                command.Repeats,
                command.ViewOnly)))
            .ToList();

    private static DesignerCommand Command(
        DesignerCommandId id,
        string name,
        Func<string> label,
        string[] desktop,
        string[]? browser = null,
        bool yieldsToTextFields = false,
        bool repeats = false) =>
        new(id, name, label, Parse(desktop), browser is null ? null : Parse(browser), yieldsToTextFields, repeats);

    private static DesignerCommand Tool(DesignTool tool, string key, Func<string> label) =>
        new(
            DesignerCommandId.Tool,
            "tool" + tool.ToString(),
            label,
            [new KeyGesture(key)],
            yieldsToTextFields: true)
        {
            Tool = tool,
        };

    private static KeyGesture[] Parse(string[] gestures) =>
        gestures.Length == 0 ? None : gestures.Select(KeyGesture.Parse).ToArray();
}
