namespace VirtualMarina.Core.Design;

/// <summary>
/// Groups everything the designer changes while it is open into one step of <see cref="MarinaDesigner.Undo"/>. Get one
/// from <see cref="MarinaDesigner.BeginAction"/>, call <see cref="Complete"/> when the whole change has been made, and
/// dispose it. Disposing it without completing it takes back every change made inside it.
/// </summary>
/// <remarks>
/// Scopes nest: one opened inside another adds its changes to the outer one, and taking the inner one back (disposing it
/// without completing it) only takes back its own. Close them in the reverse order they were opened, as <c>using</c>
/// does.
/// </remarks>
/// <example>
/// <code>
/// using (var action = designer.BeginAction("Rebuild pier A"))
/// {
///     designer.EraseBerthsOfPier("A");
///     designer.CreateBerths("A", PierSide.Left, 0f, 60f);
///     action.Complete();
/// }   // one Undo takes both back; an exception before Complete takes them back at once
/// </code>
/// </example>
public sealed class DesignActionScope : IDisposable
{
    private readonly DesignHistory _history;
    private readonly Action? _onClosed;
    private readonly List<IDesignCommand> _commands = [];
    private bool _completed;
    private bool _closed;

    internal DesignActionScope(DesignHistory history, DesignActionScope? parent, string? description, Action? onClosed)
    {
        _history = history;
        Parent = parent;
        Description = description;
        _onClosed = onClosed;
    }

    /// <summary>What the step is called on the history, e.g. "Rebuild pier A".</summary>
    public string? Description { get; private set; }

    /// <summary>True once <see cref="Complete"/> has been called.</summary>
    public bool IsCompleted => _completed;

    internal DesignActionScope? Parent { get; }

    internal IReadOnlyList<IDesignCommand> Commands => _commands;

    /// <summary>Marks the change as made, so disposing the scope keeps it rather than taking it back.</summary>
    /// <exception cref="ObjectDisposedException">The scope has already been closed.</exception>
    public void Complete()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        _completed = true;
    }

    /// <summary>
    /// Closes the scope: a completed one becomes a step on the history (or part of the scope around it); one that was not
    /// completed takes back everything changed inside it.
    /// </summary>
    /// <exception cref="InvalidOperationException">A scope opened inside this one is still open.</exception>
    public void Dispose()
    {
        if (_closed) return;
        _history.Close(this, _completed);
        _closed = true;
        _onClosed?.Invoke();
    }

    internal void Add(IDesignCommand command) => _commands.Add(command);

    /// <summary>Takes the name of the first change made inside a scope opened without one.</summary>
    internal void NameIfUnnamed(string? description) => Description ??= description;
}
