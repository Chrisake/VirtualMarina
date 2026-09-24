using VirtualMarina.Core.Api;
using VirtualMarina.Core.Design;
using VirtualMarina.Designer.Resources;

namespace VirtualMarina.Designer;

/// <summary>
/// What the rename tool was clicked on, copied out of <see cref="DesignElementRenamingEventArgs"/> so the questions can
/// be asked after the designer's own event has returned.
/// </summary>
/// <param name="Scope">One berth or pier, or every berth of a pier by a pattern.</param>
/// <param name="BerthId">The berth clicked, or null for a pier.</param>
/// <param name="PierId">The pier clicked, or the pier whose berths are renamed; null for a berth ashore.</param>
/// <param name="CurrentName">The berth's name or the pier's display name.</param>
/// <param name="BerthPattern">The pattern the pier's berths follow now, or null when they follow none.</param>
/// <param name="BerthCount">How many berths the pier has, for the whole-row question.</param>
public sealed record RenameRequest(
    DesignRenameScope Scope,
    string? BerthId,
    string? PierId,
    string CurrentName,
    string? BerthPattern,
    int BerthCount)
{
    /// <summary>True when one berth is being renamed.</summary>
    public bool IsBerth => Scope == DesignRenameScope.Element && BerthId is not null;

    /// <summary>True when a pier is renamed on its own, which also asks for its id and its berths' pattern.</summary>
    public bool IsPier => Scope == DesignRenameScope.Element && BerthId is null && PierId is not null;

    /// <summary>True when a pier's berths are renamed by one pattern.</summary>
    public bool IsWholeRow => Scope == DesignRenameScope.BerthsOfPier && PierId is not null;

    /// <summary>True when the pier question also offers its berths' pattern.</summary>
    public bool AsksPattern => IsPier && BerthPattern is not null;

    /// <summary>Copies what a rename needs out of the designer's event.</summary>
    /// <param name="e">The event.</param>
    /// <param name="marina">The marina, for the number of berths on the pier.</param>
    public static RenameRequest From(DesignElementRenamingEventArgs e, IMarinaVisualizer marina)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(marina);
        var pierId = e.Pier?.Id;
        var berthId = e.Scope == DesignRenameScope.Element ? e.Berth?.Id : null;
        return new RenameRequest(
            e.Scope,
            berthId,
            pierId,
            e.CurrentName,
            e.BerthPattern,
            pierId is null ? 0 : marina.GetBerthsByPier(pierId).Count);
    }
}

/// <summary>What the user typed into the rename dialog, trimmed.</summary>
/// <param name="Name">The new name (the pattern, for a whole row).</param>
/// <param name="PierId">The pier's new id; empty keeps it.</param>
/// <param name="Pattern">The pier's new berth pattern; empty keeps it.</param>
public sealed record RenameAnswer(string Name, string PierId, string Pattern);

/// <summary>Why a rename answer cannot be applied as it stands.</summary>
/// <param name="Title">The title of the message.</param>
/// <param name="Message">What to tell the user.</param>
/// <param name="LogMessage">What the activity log says.</param>
public sealed record RenameProblem(string Title, string Message, string LogMessage);

/// <summary>What a rename changed, so the log can say so.</summary>
/// <param name="Messages">One line per change; empty when nothing changed.</param>
public sealed record RenameOutcome(IReadOnlyList<string> Messages)
{
    /// <summary>True when anything changed.</summary>
    public bool Changed => Messages.Count > 0;
}

/// <summary>
/// The rename questions and their checks, shared by both apps: what to ask for a berth, a pier or a whole row of
/// berths, whether the answer collides with a name already taken, and how to apply it as one undoable step.
/// </summary>
public static class RenamePlanner
{
    /// <summary>How many clashing names a message lists before it says "and N more".</summary>
    public const int ClashesShown = 8;

    /// <summary>The answer the dialog starts with: everything as it is now.</summary>
    /// <param name="request">What is being renamed.</param>
    public static RenameAnswer Initial(RenameRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.IsWholeRow
            ? new RenameAnswer(request.BerthPattern ?? string.Empty, request.PierId ?? string.Empty, request.BerthPattern ?? string.Empty)
            : new RenameAnswer(request.CurrentName, request.PierId ?? string.Empty, request.BerthPattern ?? string.Empty);
    }

    /// <summary>The dialog to show: one field for a berth or a row, name, id and pattern for a pier.</summary>
    /// <param name="request">What is being renamed.</param>
    /// <param name="answer">What the fields hold: <see cref="Initial"/>, or what was typed before a refusal.</param>
    public static DesignerPrompt Prompt(RenameRequest request, RenameAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(answer);
        if (request.IsWholeRow)
        {
            return new DesignerPrompt(
                Strings.Format(Strings.RenameBerthsTitle, request.PierId),
                [new DesignerPromptField(Strings.Format(Strings.RenameBerthsQuestion, request.BerthCount, request.PierId), answer.Name, Strings.RenamePatternHint)]);
        }

        if (request.IsBerth)
        {
            return new DesignerPrompt(Strings.RenameBerthTitle, [new DesignerPromptField(Strings.RenameBerthQuestion, answer.Name)]);
        }

        List<DesignerPromptField> fields =
        [
            new(Strings.RenamePierQuestion, answer.Name),
            new(Strings.RenamePierIdQuestion, answer.PierId),
        ];
        if (request.AsksPattern) fields.Add(new DesignerPromptField(Strings.RenamePatternQuestion, answer.Pattern, Strings.RenamePatternHint));
        return new DesignerPrompt(Strings.RenamePierTitle, fields);
    }

    /// <summary>Reads the dialog's fields back. Null when it was cancelled or the first field was left empty.</summary>
    /// <param name="request">What is being renamed.</param>
    /// <param name="values">The fields' values, in the order <see cref="Prompt"/> gave them; null for a cancelled dialog.</param>
    public static RenameAnswer? Read(RenameRequest request, IReadOnlyList<string>? values)
    {
        ArgumentNullException.ThrowIfNull(request);
        IReadOnlyList<string> fields = values ?? [];
        if (fields.Count == 0 || string.IsNullOrWhiteSpace(fields[0])) return null;
        var name = fields[0].Trim();
        if (request.IsWholeRow) return new RenameAnswer(name, request.PierId ?? string.Empty, name);
        if (request.IsBerth) return new RenameAnswer(name, string.Empty, string.Empty);

        var pierId = fields.Count > 1 ? fields[1].Trim() : string.Empty;
        var pattern = request.AsksPattern && fields.Count > 2 ? fields[2].Trim() : string.Empty;
        return new RenameAnswer(name, pierId, pattern);
    }

    /// <summary>Why the answer cannot be applied, or null when it can.</summary>
    /// <param name="designer">The designer, which knows which names are taken.</param>
    /// <param name="request">What is being renamed.</param>
    /// <param name="answer">What was typed.</param>
    public static RenameProblem? Check(MarinaDesigner designer, RenameRequest request, RenameAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(designer);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(answer);

        if (request.IsWholeRow || (request.IsPier && answer.Pattern.Length > 0 && !string.Equals(answer.Pattern, request.BerthPattern, StringComparison.Ordinal)))
        {
            // A pier whose id moves as well has its berths renamed under the new id, which the plan cannot see yet;
            // the designer refuses those as one step if they clash, and says so through ActionFailed.
            var plan = designer.PlanBerthNames(request.PierId!, request.IsWholeRow ? answer.Name : answer.Pattern);
            if (!plan.IsClear && !IdMoves(request, answer))
            {
                var clashes = string.Join(", ", plan.Clashes.Take(ClashesShown));
                if (plan.Clashes.Count > ClashesShown) clashes += Strings.Format(Strings.RenameClashMore, plan.Clashes.Count - ClashesShown);
                return new RenameProblem(
                    Strings.RenameClashTitle,
                    Strings.Format(Strings.RenameClashBody, clashes),
                    Strings.Format(Strings.LogRenameClash, request.PierId, clashes));
            }
        }

        string? taken = null;
        if (request.IsBerth && !designer.IsBerthNameAvailable(answer.Name, request.BerthId)) taken = answer.Name;
        else if (request.IsPier && answer.PierId.Length > 0 && !designer.IsPierIdAvailable(answer.PierId, request.PierId)) taken = answer.PierId;

        return taken is null
            ? null
            : new RenameProblem(
                Strings.RenameTakenTitle,
                Strings.Format(Strings.RenameTakenBody, taken),
                Strings.Format(Strings.LogRenameRefused, taken, request.IsBerth ? request.CurrentName : request.PierId));
    }

    /// <summary>
    /// Applies the answer through the designer's public API, as one step for Undo: a berth's new name; a pier's new id,
    /// name and berth pattern; or a row of berths renamed by a pattern. Nothing happens when nothing changed.
    /// </summary>
    /// <param name="designer">The designer.</param>
    /// <param name="request">What is being renamed.</param>
    /// <param name="answer">What was typed; <see cref="Check"/> it first.</param>
    /// <exception cref="InvalidOperationException">A name is taken after all; nothing was changed.</exception>
    /// <exception cref="KeyNotFoundException">The berth or pier has gone since the question was asked.</exception>
    public static RenameOutcome Apply(MarinaDesigner designer, RenameRequest request, RenameAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(designer);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(answer);
        List<string> messages = [];

        if (request.IsWholeRow)
        {
            var plan = designer.PlanBerthNames(request.PierId!, answer.Name);
            if (!plan.IsClear) throw new InvalidOperationException(Strings.Format(Strings.RenameClashBody, string.Join(", ", plan.Clashes)));
            designer.ApplyBerthNames(plan);
            messages.Add(Strings.Format(Strings.LogBerthPattern, request.PierId, answer.Name));
            return new RenameOutcome(messages);
        }

        if (request.IsBerth)
        {
            if (string.Equals(answer.Name, request.CurrentName, StringComparison.Ordinal)) return new RenameOutcome(messages);
            designer.RenameBerth(request.BerthId!, answer.Name);
            messages.Add(Strings.Format(Strings.LogRenamed, request.CurrentName, answer.Name));
            return new RenameOutcome(messages);
        }

        if (!request.IsPier) return new RenameOutcome(messages);

        var pierId = request.PierId!;
        var moving = IdMoves(request, answer);
        var nameChanged = !string.Equals(answer.Name, request.CurrentName, StringComparison.Ordinal);
        var patternChanged = request.AsksPattern && answer.Pattern.Length > 0 && !string.Equals(answer.Pattern, request.BerthPattern, StringComparison.Ordinal);
        if (!moving && !nameChanged && !patternChanged) return new RenameOutcome(messages);

        // The same order as the designer's own rename: the id moves first (its berths come along), then the display
        // name goes on under the new id, then the berths are named again.
        using (var step = designer.BeginAction(Strings.Format(Strings.RenameStep, request.CurrentName)))
        {
            var current = moving ? designer.ChangePierId(pierId, answer.PierId).Id : pierId;
            if (nameChanged) designer.RenamePier(current, answer.Name);
            if (patternChanged) designer.RenumberBerths(current, answer.Pattern);
            else if (!moving) designer.RenumberBerths(current);
            step.Complete();

            if (moving) messages.Add(Strings.Format(Strings.LogPierIdChanged, pierId, current));
            if (patternChanged) messages.Add(Strings.Format(Strings.LogBerthPattern, current, answer.Pattern));
            if (nameChanged) messages.Add(Strings.Format(Strings.LogRenamed, request.CurrentName, answer.Name));
        }

        return new RenameOutcome(messages);
    }

    private static bool IdMoves(RenameRequest request, RenameAnswer answer) =>
        request.IsPier && answer.PierId.Length > 0 && !string.Equals(answer.PierId, request.PierId, StringComparison.Ordinal);
}
