using VirtualMarina.Core.Serialization;

namespace VirtualMarina.Designer.Common.Tests;

/// <summary>A clock that only moves when told to.</summary>
internal sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 5, 1, 9, 30, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>
/// Answers the session's questions from queues the test fills, and records what was asked. An empty queue answers
/// the way closing the dialog would: cancel.
/// </summary>
internal sealed class FakeDialogs : IDesignerDialogs
{
    public Queue<SaveChangesChoice> SaveChoices { get; } = new();

    public Queue<bool> Confirms { get; } = new();

    public Queue<IReadOnlyList<string>?> Answers { get; } = new();

    public Queue<DesignerOpenedFile?> Picks { get; } = new();

    /// <summary>What a save should do: return a file, return null (cancelled) or throw.</summary>
    public Queue<Func<DesignerSaveRequest, DesignerSavedFile?>> Saves { get; } = new();

    public List<string> Asked { get; } = [];

    public List<DesignerPrompt> Prompts { get; } = [];

    public List<(string Title, string Message, DesignerMessageKind Kind)> Alerts { get; } = [];

    public List<DesignerSaveRequest> SaveRequests { get; } = [];

    public Task<SaveChangesChoice> AskToSaveChangesAsync(string message)
    {
        Asked.Add(message);
        return Task.FromResult(SaveChoices.Count > 0 ? SaveChoices.Dequeue() : SaveChangesChoice.Cancel);
    }

    public Task<bool> ConfirmAsync(string title, string message)
    {
        Asked.Add(message);
        return Task.FromResult(Confirms.Count > 0 && Confirms.Dequeue());
    }

    public Task<IReadOnlyList<string>?> PromptAsync(DesignerPrompt prompt)
    {
        Prompts.Add(prompt);
        return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : null);
    }

    public Task AlertAsync(string title, string message, DesignerMessageKind kind)
    {
        Alerts.Add((title, message, kind));
        return Task.CompletedTask;
    }

    public Task<DesignerOpenedFile?> PickDesignAsync() => Task.FromResult(Picks.Count > 0 ? Picks.Dequeue() : null);

    public Task<DesignerSavedFile?> SaveDesignAsync(DesignerSaveRequest request)
    {
        SaveRequests.Add(request);
        return Task.FromResult(Saves.Count > 0 ? Saves.Dequeue()(request) : null);
    }

    /// <summary>A save that succeeds, writing the JSON nowhere but keeping it for the test to read.</summary>
    public static Func<DesignerSaveRequest, DesignerSavedFile?> SavesAs(string name, string? location, List<string>? written = null) =>
        request =>
        {
            written?.Add(request.Document.ToJson());
            return new DesignerSavedFile(name, location);
        };

    /// <summary>A file to open that holds this document.</summary>
    public static DesignerOpenedFile File(MarinaDocument document, string name = "harbour.marina.json", string? location = "/designs/harbour.marina.json")
    {
        var json = document.ToJson();
        return new DesignerOpenedFile(name, location, () => MarinaDocument.Parse(json));
    }
}
