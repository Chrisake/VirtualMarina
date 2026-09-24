using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using VirtualMarina.Core.Serialization;
using VirtualMarina.Designer.Resources;

namespace VirtualMarina.Designer.Blazor;

/// <summary>What kind of question the modal is asking.</summary>
public enum ModalKind
{
    /// <summary>Save, Don't save, Cancel.</summary>
    SaveChanges = 0,

    /// <summary>Yes, No.</summary>
    Confirm = 1,

    /// <summary>Text fields, OK, Cancel.</summary>
    Prompt = 2,

    /// <summary>A message and OK.</summary>
    Alert = 3,
}

/// <summary>Which button closed the modal.</summary>
public enum ModalButton
{
    /// <summary>Save, Yes or OK.</summary>
    Accept = 0,

    /// <summary>Don't save, or No.</summary>
    Decline = 1,

    /// <summary>Cancel, Esc, or the modal was dismissed.</summary>
    Cancel = 2,
}

/// <summary>One question on the modal, and the answer it is waiting for.</summary>
public sealed class ModalRequest(ModalKind kind, string title, string message, IReadOnlyList<DesignerPromptField> fields, DesignerMessageKind tone, bool preformatted = false)
{
    private readonly TaskCompletionSource<ModalButton> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ModalKind Kind { get; } = kind;

    public string Title { get; } = title;

    public string Message { get; } = message;

    public DesignerMessageKind Tone { get; } = tone;

    /// <summary>True for text laid out in columns (the shortcuts, the About box), shown in a fixed-width font.</summary>
    public bool Preformatted { get; } = preformatted;

    public IReadOnlyList<DesignerPromptField> Fields { get; } = fields;

    /// <summary>What the fields hold now; the modal's inputs are bound to it.</summary>
    public IList<string> Values { get; } = fields.Select(field => field.Value).ToList();

    public Task<ModalButton> Answer => _answer.Task;

    public void Resolve(ModalButton button) => _answer.TrySetResult(button);
}

/// <summary>
/// The Blazor designer's way of asking: one modal at a time for questions and messages, and the browser's file pickers
/// for designs — the File System Access API where the browser has it, so Save goes back to the same file, and an
/// ordinary file input and a download where it does not.
/// </summary>
public sealed class BlazorDialogs(IJSRuntime js) : IDesignerDialogs, IDisposable
{
    private readonly SemaphoreSlim _modal = new(1, 1);

    /// <summary>The modal to show, or null.</summary>
    public ModalRequest? Current { get; private set; }

    /// <summary>The modal opened, or closed.</summary>
    public event EventHandler? Changed;

    public async Task<SaveChangesChoice> AskToSaveChangesAsync(string message) =>
        await AskAsync(ModalKind.SaveChanges, Strings.AppName, message, [], DesignerMessageKind.Information) switch
        {
            (ModalButton.Accept, _) => SaveChangesChoice.Save,
            (ModalButton.Decline, _) => SaveChangesChoice.Discard,
            _ => SaveChangesChoice.Cancel,
        };

    public async Task<bool> ConfirmAsync(string title, string message) =>
        (await AskAsync(ModalKind.Confirm, title, message, [], DesignerMessageKind.Information)).Button == ModalButton.Accept;

    public async Task<IReadOnlyList<string>?> PromptAsync(DesignerPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var (button, values) = await AskAsync(ModalKind.Prompt, prompt.Title, string.Empty, prompt.Fields, DesignerMessageKind.Information);
        return button == ModalButton.Accept ? values : null;
    }

    public async Task AlertAsync(string title, string message, DesignerMessageKind kind) =>
        await AskAsync(ModalKind.Alert, title, message, [], kind);

    /// <summary>Shows text laid out in columns, such as the shortcuts, and waits until it is dismissed.</summary>
    /// <param name="title">The dialog's title.</param>
    /// <param name="text">The text, its columns lined up with spaces.</param>
    public async Task ShowTextAsync(string title, string text) =>
        await AskAsync(ModalKind.Alert, title, text, [], DesignerMessageKind.Information, preformatted: true);

    public async Task<DesignerOpenedFile?> PickDesignAsync()
    {
        var picked = await js.InvokeAsync<PickedDesignFile?>("vmDesigner.openDesign");
        if (picked is null) return null;
        var text = picked.Text;
        return new DesignerOpenedFile(picked.Name, picked.Key, () => MarinaDocument.Parse(text));
    }

    public async Task<DesignerSavedFile?> SaveDesignAsync(DesignerSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        SavedDesignFile? saved;
        try
        {
            saved = await js.InvokeAsync<SavedDesignFile?>("vmDesigner.saveDesign", request.SuggestedName, request.Document.ToJson(), request.Location);
        }
        catch (JSException ex)
        {
            // Permission refused, the disk full, the file locked: the session tells the user and keeps the changes.
            throw new IOException(ex.Message, ex);
        }

        return saved is null ? null : new DesignerSavedFile(saved.Name, saved.Key);
    }

    public void Dispose() => _modal.Dispose();

    /// <summary>Shows a question on the modal and waits for its answer. Questions asked meanwhile wait their turn.</summary>
    private async Task<(ModalButton Button, IReadOnlyList<string> Values)> AskAsync(
        ModalKind kind, string title, string message, IReadOnlyList<DesignerPromptField> fields, DesignerMessageKind tone, bool preformatted = false)
    {
        await _modal.WaitAsync();
        try
        {
            var request = new ModalRequest(kind, title, message, fields, tone, preformatted);
            Current = request;
            Changed?.Invoke(this, EventArgs.Empty);
            var button = await request.Answer;
            return (button, request.Values.ToList());
        }
        finally
        {
            Current = null;
            Changed?.Invoke(this, EventArgs.Empty);
            _modal.Release();
        }
    }
}

/// <summary>A design the script read: its name, a key to its file handle (or null), and its text.</summary>
public sealed record PickedDesignFile(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("key")] string? Key,
    [property: JsonPropertyName("text")] string Text);

/// <summary>Where the script wrote a design: its name, and a key to its file handle (null for a download).</summary>
public sealed record SavedDesignFile(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("key")] string? Key);
