namespace VirtualMarina.Core.Input;

/// <summary>
/// The one table every host view uses to turn a native key press into a <see cref="MarinaKey"/>, modifiers included,
/// so the WinForms and Blazor views answer the same keys in the same way.
/// </summary>
/// <remarks>
/// <para>
/// The contract is that the host's own accelerators come first. The view only takes keys nobody else would want:
/// arrows, W/A/S/D, +/−/=, PageUp/PageDown, Home, Escape, Enter, Backspace and Delete, pressed on their own or with
/// Shift (which turns panning into orbiting). A chord with Ctrl, Alt or Cmd maps to nothing, so Ctrl+S, Ctrl+Shift+S,
/// Alt+N or the browser's Ctrl+/Ctrl− zoom always reach the application or the browser, even while the view has the focus.
/// </para>
/// <para>
/// The exceptions are Undo and Redo. Ctrl+Z (Cmd+Z in a browser, which hosts report as <see cref="InputModifiers.Control"/>)
/// maps to <see cref="MarinaKey.Undo"/>; Ctrl+Shift+Z (Cmd+Shift+Z) and Ctrl+Y map to <see cref="MarinaKey.Redo"/>. They are
/// a fallback for a host that has no Undo or Redo shortcut of its own: a host that does (an Edit ▸ Undo menu item, say) sees
/// the key first and the view never gets it.
/// </para>
/// <para>
/// Movement letters are matched by where they sit on the keyboard, not by what they type, so W/A/S/D is the same
/// square on an AZERTY or a Cyrillic layout. Undo and Redo follow the letter printed on the key, as every application's
/// Ctrl+Z does, falling back to the key's position only on a layout without Latin letters.
/// </para>
/// </remarks>
public static class MarinaKeyMap
{
    // Windows virtual-key codes, which is what System.Windows.Forms.Keys holds in its low 16 bits.
    private const int VkBack = 0x08;
    private const int VkReturn = 0x0D;
    private const int VkEscape = 0x1B;
    private const int VkPrior = 0x21;
    private const int VkNext = 0x22;
    private const int VkHome = 0x24;
    private const int VkLeft = 0x25;
    private const int VkUp = 0x26;
    private const int VkRight = 0x27;
    private const int VkDown = 0x28;
    private const int VkDelete = 0x2E;
    private const int VkA = 0x41;
    private const int VkD = 0x44;
    private const int VkS = 0x53;
    private const int VkW = 0x57;
    private const int VkY = 0x59;
    private const int VkZ = 0x5A;
    private const int VkAdd = 0x6B;
    private const int VkSubtract = 0x6D;
    private const int VkOemPlus = 0xBB;
    private const int VkOemMinus = 0xBD;

    /// <summary>
    /// True when the modifiers make the press a chord (Ctrl, Alt or Cmd held), which the view leaves to the host.
    /// Shift alone is not a chord: Shift+arrow orbits.
    /// </summary>
    /// <param name="modifiers">The modifier keys held.</param>
    public static bool IsChord(InputModifiers modifiers) => (modifiers & (InputModifiers.Control | InputModifiers.Alt)) != 0;

    /// <summary>
    /// Maps a browser <c>KeyboardEvent</c> to the key the view acts on, or null when the view should leave it alone.
    /// </summary>
    /// <param name="code">
    /// <c>KeyboardEvent.code</c>, the physical key ("KeyW", "ArrowLeft", "NumpadAdd"). May be null or empty for a
    /// synthetic event, in which case the letters are matched by <paramref name="key"/> instead.
    /// </param>
    /// <param name="key"><c>KeyboardEvent.key</c>, what the key types on the current layout ("w", "+", "Escape").</param>
    /// <param name="modifiers">The modifiers held, with Cmd reported as <see cref="InputModifiers.Control"/>.</param>
    public static MarinaKey? FromDomKey(string? code, string? key, InputModifiers modifiers)
    {
        if (IsChord(modifiers)) return HistoryChord(modifiers, IsDomLetter(code, key, 'z'), IsDomLetter(code, key, 'y'));

        var byCode = code switch
        {
            "ArrowLeft" or "KeyA" => MarinaKey.Left,
            "ArrowRight" or "KeyD" => MarinaKey.Right,
            "ArrowUp" or "KeyW" => MarinaKey.Up,
            "ArrowDown" or "KeyS" => MarinaKey.Down,
            "PageUp" => MarinaKey.PageUp,
            "PageDown" => MarinaKey.PageDown,
            "NumpadAdd" => MarinaKey.ZoomIn,
            "NumpadSubtract" => MarinaKey.ZoomOut,
            "Home" => MarinaKey.Home,
            "Escape" => MarinaKey.Escape,
            "Enter" or "NumpadEnter" => MarinaKey.Enter,
            "Backspace" => MarinaKey.Backspace,
            "Delete" => MarinaKey.Delete,
            _ => (MarinaKey?)null,
        };
        if (byCode is not null) return byCode;

        // Symbols follow the character, wherever the layout puts it (+ has a key of its own on a German keyboard).
        // The letters only fall back to the character when there is no physical key to go by.
        var hasCode = !string.IsNullOrEmpty(code);
        return key switch
        {
            "+" or "=" => MarinaKey.ZoomIn,
            "-" or "_" => MarinaKey.ZoomOut,
            "ArrowLeft" => MarinaKey.Left,
            "ArrowRight" => MarinaKey.Right,
            "ArrowUp" => MarinaKey.Up,
            "ArrowDown" => MarinaKey.Down,
            "PageUp" => MarinaKey.PageUp,
            "PageDown" => MarinaKey.PageDown,
            "Home" => MarinaKey.Home,
            "Escape" => MarinaKey.Escape,
            "Enter" => MarinaKey.Enter,
            "Backspace" => MarinaKey.Backspace,
            "Delete" => MarinaKey.Delete,
            "a" or "A" when !hasCode => MarinaKey.Left,
            "d" or "D" when !hasCode => MarinaKey.Right,
            "w" or "W" when !hasCode => MarinaKey.Up,
            "s" or "S" when !hasCode => MarinaKey.Down,
            _ => null,
        };
    }

    /// <summary>
    /// Maps a Windows virtual-key code (the <c>KeyCode</c> part of a WinForms <c>Keys</c> value) to the key the view
    /// acts on, or null when the view should leave it alone.
    /// </summary>
    /// <param name="virtualKey">The virtual-key code, without modifier bits.</param>
    /// <param name="modifiers">The modifiers held.</param>
    public static MarinaKey? FromVirtualKey(int virtualKey, InputModifiers modifiers)
    {
        if (IsChord(modifiers)) return HistoryChord(modifiers, virtualKey == VkZ, virtualKey == VkY);

        return virtualKey switch
        {
            VkLeft or VkA => MarinaKey.Left,
            VkRight or VkD => MarinaKey.Right,
            VkUp or VkW => MarinaKey.Up,
            VkDown or VkS => MarinaKey.Down,
            VkPrior => MarinaKey.PageUp,
            VkNext => MarinaKey.PageDown,
            VkAdd or VkOemPlus => MarinaKey.ZoomIn,
            VkSubtract or VkOemMinus => MarinaKey.ZoomOut,
            VkHome => MarinaKey.Home,
            VkEscape => MarinaKey.Escape,
            VkReturn => MarinaKey.Enter,
            VkBack => MarinaKey.Backspace,
            VkDelete => MarinaKey.Delete,
            _ => null,
        };
    }

    /// <summary>
    /// The one kind of chord the view takes: Ctrl+Z is Undo, Ctrl+Shift+Z and Ctrl+Y are Redo. Ctrl+Alt is AltGr on many
    /// layouts, so a chord with Alt in it is never taken.
    /// </summary>
    private static MarinaKey? HistoryChord(InputModifiers modifiers, bool isZ, bool isY) => modifiers switch
    {
        InputModifiers.Control when isZ => MarinaKey.Undo,
        InputModifiers.Control | InputModifiers.Shift when isZ => MarinaKey.Redo,
        InputModifiers.Control when isY => MarinaKey.Redo,
        _ => null,
    };

    /// <summary>The key that types this letter; on a layout without Latin letters, the key where it sits.</summary>
    private static bool IsDomLetter(string? code, string? key, char letter)
    {
        if (key is { Length: 1 } typed && char.IsAsciiLetter(typed[0])) return char.ToLowerInvariant(typed[0]) == letter;
        return string.Equals(code, "Key" + char.ToUpperInvariant(letter), StringComparison.Ordinal);
    }
}
