using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Design;

/// <summary>Undo, redo and grouping changes into one step.</summary>
public sealed partial class MarinaDesigner
{
    /// <summary>True when <see cref="Undo"/> would revert something.</summary>
    public bool CanUndo => _history.UndoCount > 0;

    /// <summary>What the next <see cref="Undo"/> would revert (e.g. "Add 4 berths"), or null when there is nothing to undo.</summary>
    public string? UndoDescription => _history.UndoDescription;

    /// <summary>How many changes can still be undone (at most <see cref="MaxUndoSteps"/>).</summary>
    public int UndoCount => _history.UndoCount;

    /// <summary>True when <see cref="Redo"/> would make a change again.</summary>
    public bool CanRedo => _history.RedoCount > 0;

    /// <summary>What the next <see cref="Redo"/> would make again, or null when there is nothing to redo.</summary>
    public string? RedoDescription => _history.RedoDescription;

    /// <summary>How many undone changes can be made again. A new change empties it.</summary>
    public int RedoCount => _history.RedoCount;

    /// <summary>
    /// Reverts the last change the designer made: a drawn land area, pier, berth row or land berth is removed again, erased elements
    /// come back with their dividers, and trees, names and pedestals are put back. Raises <see cref="ActionUndone"/> (and the usual
    /// <c>LayoutChanged</c> notifications). Returns false when there is nothing to undo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only what the designer changed is put back — the trees it planted, not the rest of the lawn — so everything the host changed
    /// through the normal API in between stays as it is. A change the host has since built on, or changed again itself (a berth
    /// added to the pier being taken away, trees replaced since), is not overwritten: the undo is refused instead, and nothing of
    /// it is done. Loading or clearing a layout empties the history. A restored berth is no longer part of a multi-berth.
    /// </para>
    /// <para>
    /// Ctrl+Z in the views takes back the last point placed while a drawing is in progress (see <see cref="RemoveLastPoint"/>),
    /// and only undoes the last change when there is no drawing; <see cref="TryUndo"/> does the same for a host's own Undo command.
    /// </para>
    /// </remarks>
    /// <exception cref="MarinaLayoutException">
    /// The change can no longer be put back because of something the host did since. Nothing was changed, and the change stays on
    /// the history.
    /// </exception>
    /// <exception cref="InvalidOperationException">An action from <see cref="BeginAction"/> is still open.</exception>
    public bool Undo()
    {
        using (BeginUpdate())
        {
            var description = _history.Undo();
            if (description is null) return false;

            ErasedUnderPointer();
            ActionUndone?.Invoke(this, new DesignActionUndoneEventArgs(description, _history.UndoCount));
            RaiseStateChanged();
            return true;
        }
    }

    /// <summary>
    /// Makes the last undone change again, as it was made. Raises <see cref="ActionRedone"/>. Returns false when there is nothing to
    /// redo; any new change forgets what could have been redone.
    /// </summary>
    /// <exception cref="MarinaLayoutException">
    /// The change can no longer be made because of something the host did since. Nothing was changed, and the change stays ready to
    /// redo.
    /// </exception>
    /// <exception cref="InvalidOperationException">An action from <see cref="BeginAction"/> is still open.</exception>
    public bool Redo()
    {
        using (BeginUpdate())
        {
            var description = _history.Redo();
            if (description is null) return false;

            ErasedUnderPointer();
            ActionRedone?.Invoke(this, new DesignActionRedoneEventArgs(description, _history.RedoCount));
            RaiseStateChanged();
            return true;
        }
    }

    /// <summary>
    /// Undo the way Ctrl+Z in the views does it, for a host's own Undo command (menu item, toolbar button, shortcut): while a
    /// drawing is in progress the last point placed is taken back (see <see cref="RemoveLastPoint"/>); otherwise the last change is
    /// undone. An undo that is refused, or asked for while an action from <see cref="BeginAction"/> is open, does not throw:
    /// nothing changes and <see cref="ActionFailed"/> says why. Returns true when something was taken back.
    /// </summary>
    /// <remarks><see cref="Undo"/> stays the call for code that wants to handle a refusal itself.</remarks>
    public bool TryUndo() => HasDraft ? RemoveLastPoint() : TryHistory(undo: true);

    /// <summary>
    /// Redo the way Ctrl+Shift+Z or Ctrl+Y in the views does it, for a host's own Redo command: makes the last undone change again,
    /// and when that is refused, or asked for while an action from <see cref="BeginAction"/> is open, raises
    /// <see cref="ActionFailed"/> instead of throwing. Returns true when a change was made again.
    /// </summary>
    /// <remarks><see cref="Redo"/> stays the call for code that wants to handle a refusal itself.</remarks>
    public bool TryRedo() => TryHistory(undo: false);

    /// <summary>
    /// Forgets every recorded change, so <see cref="Undo"/> and <see cref="Redo"/> do nothing until the designer changes something
    /// again.
    /// </summary>
    public void ClearHistory() => _history.Clear();

    /// <summary>
    /// Starts one step for <see cref="Undo"/> that everything the designer changes until the returned scope is disposed goes into:
    /// several erasures, a rename and a renumber, whatever belongs together. Call <see cref="DesignActionScope.Complete"/> when all of
    /// it is done; disposing the scope without that takes all of it back. <see cref="StateChanged"/> is raised once, when it closes.
    /// </summary>
    /// <param name="description">What the step is called in the history, e.g. "Rebuild pier A".</param>
    /// <exception cref="ArgumentException">The description is empty.</exception>
    public DesignActionScope BeginAction(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        return BeginStep(description);
    }

    /// <summary>Opens a step, taking its name from the first change made inside it when none is given.</summary>
    private DesignActionScope BeginStep(string? description)
    {
        // Held back like BeginUpdate does, and let go when the step closes.
        _updateDepth++;
        return _history.Begin(description, EndUpdate);
    }
}
