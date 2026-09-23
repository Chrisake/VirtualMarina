using VirtualMarina.Core.Serialization;

namespace VirtualMarina.Designer;

/// <summary>The answer to "Save the changes first?".</summary>
public enum SaveChangesChoice
{
    /// <summary>Save, then go on.</summary>
    Save = 0,

    /// <summary>Go on without saving.</summary>
    Discard = 1,

    /// <summary>Stay with the design as it is.</summary>
    Cancel = 2,
}

/// <summary>How serious a message is, for the icon a dialog shows with it.</summary>
public enum DesignerMessageKind
{
    /// <summary>Something to know.</summary>
    Information = 0,

    /// <summary>Something that did not work.</summary>
    Warning = 1,
}

/// <summary>One text field of a <see cref="DesignerPrompt"/>.</summary>
/// <param name="Label">The question above it.</param>
/// <param name="Value">What it holds when the dialog opens.</param>
/// <param name="Hint">A line of explanation under it, or null.</param>
public sealed record DesignerPromptField(string Label, string Value, string? Hint = null);

/// <summary>A dialog asking for one or more lines of text.</summary>
/// <param name="Title">The dialog's title.</param>
/// <param name="Fields">The fields, in order; at least one.</param>
public sealed record DesignerPrompt(string Title, IReadOnlyList<DesignerPromptField> Fields);

/// <summary>A design file the user picked to open.</summary>
/// <param name="Name">The file's name, as the title bar and the log show it.</param>
/// <param name="Location">Where it is, kept so Save can write back to it: a path, a browser file handle's key, or null.</param>
/// <param name="Read">Reads and parses it. May throw <see cref="MarinaFormatException"/>, <see cref="IOException"/> and the like.</param>
public sealed record DesignerOpenedFile(string Name, string? Location, Func<MarinaDocument> Read);

/// <summary>A design to be written out.</summary>
/// <param name="Document">The document, already brought up to date with the marina.</param>
/// <param name="SuggestedName">The file name to offer when asking where, extension included.</param>
/// <param name="Location">Where to write it without asking (Save), or null to ask (Save As, or never saved).</param>
public sealed record DesignerSaveRequest(MarinaDocument Document, string SuggestedName, string? Location);

/// <summary>Where a design ended up.</summary>
/// <param name="Name">The file's name.</param>
/// <param name="Location">Where it is, for the next Save; null when there is nothing to write back to (a download).</param>
public sealed record DesignerSavedFile(string Name, string? Location);

/// <summary>
/// Everything the Designer asks the user, as each app asks it: message boxes and file dialogs on the desktop, a modal
/// and the browser's file pickers in the Blazor app. <see cref="DesignerSession"/> runs the whole New / Open / Save /
/// rename workflow through these and nothing else.
/// </summary>
/// <remarks>
/// Every method may complete synchronously; the WinForms designer's do, since its dialogs are modal.
/// </remarks>
public interface IDesignerDialogs
{
    /// <summary>Asks whether to save unsaved changes before they would be lost.</summary>
    /// <param name="message">The question.</param>
    Task<SaveChangesChoice> AskToSaveChangesAsync(string message);

    /// <summary>Asks a yes-or-no question. True for yes.</summary>
    /// <param name="title">The dialog's title.</param>
    /// <param name="message">The question.</param>
    Task<bool> ConfirmAsync(string title, string message);

    /// <summary>Asks for text. Returns the fields' values in order, or null when the user cancelled.</summary>
    /// <param name="prompt">What to ask.</param>
    Task<IReadOnlyList<string>?> PromptAsync(DesignerPrompt prompt);

    /// <summary>Shows a message and waits until it is dismissed.</summary>
    /// <param name="title">The dialog's title.</param>
    /// <param name="message">The message.</param>
    /// <param name="kind">How serious it is.</param>
    Task AlertAsync(string title, string message, DesignerMessageKind kind);

    /// <summary>Asks which design to open. Null when the user cancelled.</summary>
    Task<DesignerOpenedFile?> PickDesignAsync();

    /// <summary>
    /// Writes a design, asking where first when <see cref="DesignerSaveRequest.Location"/> is null. Null when the user
    /// cancelled. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when the write fails.
    /// </summary>
    /// <param name="request">What to write, and where.</param>
    Task<DesignerSavedFile?> SaveDesignAsync(DesignerSaveRequest request);
}
