using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Design;

/// <summary>
/// One reversible change to the layout, holding only what it changed: the elements it added or removed, or the one
/// property of an element it set, before and after. Undoing puts that property back and leaves everything else about the
/// element — whatever the host has changed since — as it is.
/// </summary>
/// <remarks>
/// A command throws when the layout no longer allows it (the element it changed has gone, or someone else has changed the
/// same property since). <see cref="DesignHistory"/> then puts back whatever else of the same step it had already done, so
/// a step is never left half undone.
/// </remarks>
internal interface IDesignCommand
{
    /// <summary>Takes the change back.</summary>
    /// <param name="marina">The marina to change.</param>
    void Undo(MarinaVisualizer marina);

    /// <summary>Makes the change again, after <see cref="Undo"/>.</summary>
    /// <param name="marina">The marina to change.</param>
    void Redo(MarinaVisualizer marina);
}

/// <summary>
/// The designer's undo and redo stacks. Every change the designer makes is recorded here as commands inside an action
/// (<see cref="Begin"/>); an action becomes one step, however many commands it holds.
/// </summary>
internal sealed class DesignHistory
{
    private readonly MarinaVisualizer _marina;
    private readonly List<Entry> _undo = [];
    private readonly List<Entry> _redo = [];
    private readonly Stack<DesignActionScope> _open = new();

    public DesignHistory(MarinaVisualizer marina, int maxSteps)
    {
        _marina = marina;
        MaxSteps = maxSteps;
    }

    /// <summary>Raised when the stacks changed: a step recorded, undone, redone or forgotten.</summary>
    public event EventHandler? Changed;

    public int MaxSteps { get; }

    public int UndoCount => _undo.Count;

    public int RedoCount => _redo.Count;

    public string? UndoDescription => _undo.Count > 0 ? _undo[^1].Description : null;

    public string? RedoDescription => _redo.Count > 0 ? _redo[^1].Description : null;

    /// <summary>Opens an action; commands recorded until it is closed become one step.</summary>
    /// <param name="description">What the step is called; null takes the name of the first action opened inside it.</param>
    /// <param name="onClosed">Called when the scope closes, whether it was completed or rolled back.</param>
    public DesignActionScope Begin(string? description, Action? onClosed = null)
    {
        var scope = new DesignActionScope(this, _open.Count > 0 ? _open.Peek() : null, description, onClosed);
        _open.Push(scope);
        return scope;
    }

    /// <summary>Records a change that has just been made: into the open action, or as a step of its own.</summary>
    /// <param name="command">The change.</param>
    /// <param name="description">The step's name when no action is open.</param>
    public void Record(IDesignCommand command, string description)
    {
        if (_open.Count > 0)
        {
            _open.Peek().Add(command);
            _open.Peek().NameIfUnnamed(description);
            return;
        }

        Push(new Entry(description, [command]));
    }

    /// <summary>Takes the last step back. Returns its description, or null when there was none.</summary>
    /// <exception cref="MarinaLayoutException">The step can no longer be taken back; nothing was changed and it stays on the history.</exception>
    public string? Undo()
    {
        RequireClosed();
        if (_undo.Count == 0) return null;

        var entry = _undo[^1];
        Replay(entry, undo: true);
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(entry);
        Changed?.Invoke(this, EventArgs.Empty);
        return entry.Description;
    }

    /// <summary>Makes the last undone step again. Returns its description, or null when there was none.</summary>
    /// <exception cref="MarinaLayoutException">The step can no longer be made; nothing was changed and it stays on the redo stack.</exception>
    public string? Redo()
    {
        RequireClosed();
        if (_redo.Count == 0) return null;

        var entry = _redo[^1];
        Replay(entry, undo: false);
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(entry);
        Changed?.Invoke(this, EventArgs.Empty);
        return entry.Description;
    }

    /// <summary>Forgets every step, done and undone. Returns false when there was nothing to forget.</summary>
    public bool Clear()
    {
        if (_undo.Count == 0 && _redo.Count == 0) return false;
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Called by a scope that closes: hands its commands on to the one around it, or records them as a step.</summary>
    internal void Close(DesignActionScope scope, bool completed)
    {
        if (_open.Count == 0 || !ReferenceEquals(scope, _open.Peek())) throw new InvalidOperationException("Design actions must be closed in the reverse order they were opened.");
        _open.Pop();

        if (!completed)
        {
            Rollback(scope.Commands);
            return;
        }

        if (scope.Commands.Count == 0) return;
        if (scope.Parent is { } parent)
        {
            foreach (var command in scope.Commands) parent.Add(command);
            parent.NameIfUnnamed(scope.Description);
            return;
        }

        Push(new Entry(scope.Description ?? string.Empty, [.. scope.Commands]));
    }

    private void Push(Entry entry)
    {
        _undo.Add(entry);
        if (_undo.Count > MaxSteps) _undo.RemoveAt(0);

        // A new change starts a new line of history: what was undone before it cannot be redone on top of it.
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Runs a step's commands backwards (undo) or forwards (redo) as one batch. When one of them fails, the ones already
    /// run are run the other way again, so the step either happens whole or not at all.
    /// </summary>
    private void Replay(Entry entry, bool undo)
    {
        var done = 0;
        try
        {
            using (_marina.BeginUpdate())
            {
                for (; done < entry.Commands.Count; done++) Run(CommandAt(entry, done, undo), undo);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException or MarinaLayoutException)
        {
            Unwind(entry, done, undo);
            var verb = undo ? "undone" : "redone";
            throw new MarinaLayoutException($"'{entry.Description}' could not be {verb}: {ex.Message}", ex);
        }
    }

    /// <summary>Takes back the first <paramref name="done"/> commands of a step that failed part-way, last first.</summary>
    private void Unwind(Entry entry, int done, bool undo)
    {
        using (_marina.BeginUpdate())
        {
            for (var i = done - 1; i >= 0; i--)
            {
                var command = CommandAt(entry, i, undo);
                TryRun(() => Run(command, !undo));
            }
        }
    }

    /// <summary>The <paramref name="index"/>th command to run: counted from the end when undoing, from the start when redoing.</summary>
    private static IDesignCommand CommandAt(Entry entry, int index, bool undo) =>
        entry.Commands[undo ? entry.Commands.Count - 1 - index : index];

    private void Run(IDesignCommand command, bool undo)
    {
        if (undo) command.Undo(_marina);
        else command.Redo(_marina);
    }

    /// <summary>Takes back the commands of an action that was abandoned, newest first.</summary>
    private void Rollback(IReadOnlyList<IDesignCommand> commands)
    {
        if (commands.Count == 0) return;
        using (_marina.BeginUpdate())
        {
            for (var i = commands.Count - 1; i >= 0; i--)
            {
                var command = commands[i];
                TryRun(() => command.Undo(_marina));
            }
        }
    }

    /// <summary>
    /// Runs one step of putting things back after a failure. Those steps only reverse what was done a moment ago, so they
    /// do not fail in practice; if one does, the rest still run rather than leave more of the change in place.
    /// </summary>
    private static void TryRun(Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException or MarinaLayoutException)
        {
            // Nothing better to do than carry on with the rest; the original failure is what gets reported.
        }
    }

    private void RequireClosed()
    {
        if (_open.Count > 0) throw new InvalidOperationException("A design action is still open; complete or dispose it first.");
    }

    /// <summary>One step on the history: what it is called and the commands it is made of, in the order they were done.</summary>
    private sealed record Entry(string Description, IReadOnlyList<IDesignCommand> Commands);
}
