using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Design;

/// <summary>Names and ids: of piers, of berths, and of whole rows of berths at once.</summary>
public sealed partial class MarinaDesigner
{
    /// <summary>
    /// True when a pier could be given this id: nothing is using it, or the pier using it is the one asking. Ids are
    /// compared without regard to case, and an empty one is never free.
    /// </summary>
    /// <param name="pierId">The id being asked for.</param>
    /// <param name="forPierId">The pier that wants it, so keeping its own id counts as free. Null for a new pier.</param>
    public bool IsPierIdAvailable(string? pierId, string? forPierId = null)
    {
        if (string.IsNullOrWhiteSpace(pierId)) return false;
        var wanted = pierId.Trim();
        if (forPierId is not null && string.Equals(wanted, forPierId, StringComparison.OrdinalIgnoreCase)) return true;
        return Marina.GetPier(wanted) is null;
    }

    /// <summary>
    /// True when a berth could be given this name. A berth's name is also its id, so it has to be free across the
    /// whole marina.
    /// </summary>
    /// <param name="berthName">The name being asked for.</param>
    /// <param name="forBerthId">The berth that wants it, so keeping its own name counts as free.</param>
    public bool IsBerthNameAvailable(string? berthName, string? forBerthId = null)
    {
        if (string.IsNullOrWhiteSpace(berthName)) return false;
        var wanted = berthName.Trim();
        if (forBerthId is not null && string.Equals(wanted, forBerthId, StringComparison.OrdinalIgnoreCase)) return true;
        return Marina.GetBerth(wanted) is null;
    }

    /// <summary>The name <see cref="PierNamePattern"/> gives a pier with this id, e.g. "Pier C".</summary>
    /// <param name="pierId">The pier id to put in the pattern.</param>
    public string GeneratedPierName(string pierId)
    {
        ArgumentNullException.ThrowIfNull(pierId);
        return _pierNamePattern.Replace("{pier}", pierId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gives a pier another id, which its berths and dividers follow, and records the change for <see cref="Undo"/>.
    /// Returns the pier under its new id.
    /// </summary>
    /// <param name="pierId">The pier to move.</param>
    /// <param name="newPierId">Its new id. Leading and trailing spaces are dropped.</param>
    /// <exception cref="KeyNotFoundException">No pier has this id.</exception>
    /// <exception cref="InvalidOperationException">Another pier already has the new id.</exception>
    /// <remarks>
    /// The berths named after the pier come too: a berth called <c>A-L01</c> on pier <c>A</c> becomes <c>B-L01</c>
    /// when the pier becomes <c>B</c>, which is what the names are for. A berth whose name was typed by hand, and so
    /// does not start with the pier id, is left alone, as is one whose new name is already taken. The whole move is
    /// one step for <see cref="Undo"/>, and a host tracking berths by id hears about every one that moved.
    /// </remarks>
    public Pier ChangePierId(string pierId, string newPierId)
    {
        ArgumentNullException.ThrowIfNull(pierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPierId);
        var before = Marina.GetPier(pierId)?.Id ?? throw new KeyNotFoundException($"Pier '{pierId}' does not exist.");

        using (BeginUpdate())
        using (var step = BeginStep(Strings.Format(Strings.UndoChangePierId, before, newPierId.Trim())))
        {
            var moved = Marina.ChangePierId(before, newPierId);
            if (string.Equals(before, moved.Id, StringComparison.Ordinal))
            {
                step.Complete();
                return moved;
            }

            _history.Record(new ChangePierIdCommand(before, moved.Id), step.Description!);
            RenameBerths(Naming.PlanRenamesAfterPier(moved, namedFrom: before, _berthNaming, wholePier: false), step.Description!);

            // A pier still carrying the name generated for its old id gets the one for its new id.
            var generated = GeneratedPierName(moved.Id);
            if (string.Equals(moved.Name, GeneratedPierName(before), StringComparison.Ordinal) && !string.Equals(moved.Name, generated, StringComparison.Ordinal))
            {
                Marina.UpdatePier(moved with { Name = generated });
                _history.Record(DesignProperties.Changed(DesignProperties.PierName, moved.Id, moved.Name, generated), step.Description!);
                moved = Marina.GetPier(moved.Id)!;
            }

            step.Complete();
            RaiseStateChanged();
            return moved;
        }
    }

    /// <summary>
    /// Names a pier's berths again from <see cref="BerthNaming"/>, keeping the number each one already has, and
    /// records it for <see cref="Undo"/>. Returns the berths that were renamed, as (old name, new name).
    /// </summary>
    /// <param name="pierId">The pier whose berths to put right.</param>
    /// <remarks>
    /// This is the repair for berths whose names no longer match the pier: one that used to take boats on both sides
    /// and now takes them on one still has the side letter in its berth names, and this takes it out. A berth named
    /// by hand keeps its name.
    /// </remarks>
    /// <exception cref="KeyNotFoundException">No pier has this id.</exception>
    /// <example><code>designer.RenumberBerths("E");   // E-R01 becomes E-01 on a pier that berths to one side</code></example>
    public IReadOnlyList<(string From, string To)> RenumberBerths(string pierId) => RenumberBerths(pierId, null);

    /// <summary>
    /// Names a pier's berths again from a pattern of your own, keeping the number each one already has, and records
    /// it for <see cref="Undo"/>. Returns the berths that were renamed, as (old name, new name).
    /// </summary>
    /// <param name="pierId">The pier whose berths to rename.</param>
    /// <param name="pattern">
    /// The pattern to name them by, in the form <see cref="BerthNamingScheme.Pattern"/> takes; null uses
    /// <see cref="BerthNaming"/> as it stands. The padding follows the names the berths already have.
    /// </param>
    /// <remarks>
    /// A berth whose new name is already taken is left alone rather than overwritten, so a pattern that would give
    /// two berths the same name — dropping <c>{side}</c> from a pier that berths on both — renames neither.
    /// </remarks>
    /// <exception cref="KeyNotFoundException">No pier has this id.</exception>
    /// <example><code>designer.RenumberBerths("E", "{pier}.{number}");   // E-R01 becomes E.01</code></example>
    public IReadOnlyList<(string From, string To)> RenumberBerths(string pierId, string? pattern)
    {
        ArgumentNullException.ThrowIfNull(pierId);
        var pier = Marina.GetPier(pierId) ?? throw new KeyNotFoundException($"Pier '{pierId}' does not exist.");

        // Asked for outright, this puts every numbered berth right, including ones still carrying a prefix from an
        // id the pier had long ago. Only a berth named without a number on the end is taken to be named by hand.
        var scheme = SchemeFor(pier, pattern);
        var renamed = Naming.PlanRenamesAfterPier(pier, namedFrom: null, scheme ?? _berthNaming, wholePier: scheme is not null);
        if (renamed.Count == 0) return renamed;

        using (BeginUpdate())
        {
            RenameBerths(renamed, Strings.Plural("UndoRenumberBerths", renamed.Count, renamed.Count, pier.Name));
            RaiseStateChanged();
        }

        return renamed;
    }

    /// <summary>
    /// The pattern a pier's berths are named by when nothing else is asked for: <c>{pier}-{side}{number}</c>, or
    /// <c>{pier}-{number}</c> on a pier that takes boats on one side only, where there is no other side to tell a
    /// berth apart from.
    /// </summary>
    /// <param name="pierId">The pier.</param>
    /// <exception cref="KeyNotFoundException">No pier has this id.</exception>
    public string DefaultBerthPattern(string pierId)
    {
        ArgumentNullException.ThrowIfNull(pierId);
        var pier = Marina.GetPier(pierId) ?? throw new KeyNotFoundException($"Pier '{pierId}' does not exist.");
        return pier.BerthingSides is PierSides.Left or PierSides.Right ? "{pier}-{number}" : "{pier}-{side}{number}";
    }

    /// <summary>
    /// Works out what naming a pier's berths by a pattern would call each of them, and what would go wrong, without
    /// changing anything.
    /// </summary>
    /// <param name="pierId">The pier whose berths to name.</param>
    /// <param name="pattern">
    /// The pattern, in the form <see cref="BerthNamingScheme.Pattern"/> takes. Null or blank uses
    /// <see cref="DefaultBerthPattern"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// Unlike <see cref="RenumberBerths(string, string?)"/>, which keeps the number each berth already has, this
    /// throws the old names away and counts from <see cref="BerthNamingScheme.StartNumber"/> along the pier. A berth
    /// named by hand is renamed with the rest: asking for a whole pier to be named by a pattern means all of it.
    /// </para>
    /// <para>
    /// Berths are numbered down one side and then the other when the pattern tells the sides apart, and straight
    /// through when it does not, so <c>{pier}-{number}</c> on a pier that berths both sides gives one run of
    /// numbers rather than two sets of the same ones.
    /// </para>
    /// </remarks>
    /// <exception cref="KeyNotFoundException">No pier has this id.</exception>
    /// <example><code>
    /// var plan = designer.PlanBerthNames("A", "{pier}.{side}{number}");
    /// if (!plan.IsClear) Warn(string.Join(", ", plan.Clashes));
    /// else designer.ApplyBerthNames(plan);
    /// </code></example>
    public BerthNamePlan PlanBerthNames(string pierId, string? pattern)
    {
        ArgumentNullException.ThrowIfNull(pierId);
        var pier = Marina.GetPier(pierId) ?? throw new KeyNotFoundException($"Pier '{pierId}' does not exist.");
        var wanted = string.IsNullOrWhiteSpace(pattern) ? DefaultBerthPattern(pier.Id) : pattern.Trim();
        return Naming.PlanFromScratch(pier, wanted, _berthNaming with { Pattern = wanted });
    }

    /// <summary>
    /// Applies a plan from <see cref="PlanBerthNames"/>, renaming every berth on the pier in one undoable step.
    /// </summary>
    /// <param name="plan">The plan. It must be clear of clashes.</param>
    /// <remarks>
    /// Every berth takes its new name at once, so a pattern that shuffles names around a pier — every berth moving
    /// up one — does not collide with itself half way through. One <c>BerthRenamed</c> is raised per berth renamed.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The plan has clashes, or the pier has gone.</exception>
    /// <returns>Every berth that actually changed name, as (old name, new name).</returns>
    public IReadOnlyList<(string From, string To)> ApplyBerthNames(BerthNamePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.IsClear) throw new InvalidOperationException($"The names {string.Join(", ", plan.Clashes)} are already taken.");
        if (Marina.GetPier(plan.PierId) is null) throw new InvalidOperationException($"Pier '{plan.PierId}' does not exist.");

        var moving = plan.Renames.Where(rename => !string.Equals(rename.From, rename.To, StringComparison.Ordinal)).ToArray();
        if (moving.Length == 0) return Array.Empty<(string, string)>();

        using (BeginUpdate())
        {
            RenameBerths(moving, Strings.Plural("UndoRenumberBerths", moving.Length, moving.Length, plan.PierId));
            RaiseStateChanged();
        }

        return moving;
    }

    /// <summary>
    /// Gives one berth another name, keeping everything else about it, and records the change for <see cref="Undo"/>.
    /// Returns the renamed berth.
    /// </summary>
    /// <param name="berthId">The berth to rename.</param>
    /// <param name="newBerthId">Its new name. Leading and trailing spaces are dropped.</param>
    /// <exception cref="KeyNotFoundException">No berth has this id.</exception>
    /// <exception cref="InvalidOperationException">Another berth already has the new name.</exception>
    /// <remarks>
    /// The name is also the berth's id, which a host application may store against a contract; see
    /// <see cref="IMarinaVisualizer.RenameBerth"/>.
    /// </remarks>
    public Berth RenameBerth(string berthId, string newBerthId)
    {
        ArgumentNullException.ThrowIfNull(berthId);
        var before = Marina.GetBerth(berthId)?.Id ?? throw new KeyNotFoundException($"Berth '{berthId}' does not exist.");
        var renamed = Marina.RenameBerth(before, newBerthId);
        if (!string.Equals(before, renamed.Id, StringComparison.Ordinal))
        {
            using (BeginUpdate())
            {
                _history.Record(new RenameBerthsCommand([(before, renamed.Id)]), Strings.Format(Strings.UndoRenameBerth, before, renamed.Id));
                RaiseStateChanged();
            }
        }

        return renamed;
    }

    /// <summary>
    /// Gives a pier a display name (<see cref="Pier.Name"/>), the one shown in tooltips and the camera preset, and records
    /// the change for <see cref="Undo"/>. The pier's id, and the berth names built from it, stay as they are.
    /// Returns the renamed pier.
    /// </summary>
    /// <param name="pierId">The pier to name.</param>
    /// <param name="name">Its new name; names need not be unique. Leading and trailing spaces are dropped.</param>
    /// <exception cref="KeyNotFoundException">No pier has this id.</exception>
    /// <example><code>designer.RenamePier("A", "West pontoon");</code></example>
    public Pier RenamePier(string pierId, string name)
    {
        ArgumentNullException.ThrowIfNull(pierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var existing = Marina.GetPier(pierId) ?? throw new KeyNotFoundException($"Pier '{pierId}' does not exist.");
        name = name.Trim();
        if (string.Equals(existing.Name, name, StringComparison.Ordinal)) return existing;

        using (BeginUpdate())
        {
            Marina.UpdatePier(existing with { Name = name });
            var renamed = Marina.GetPier(existing.Id)!;
            _history.Record(
                DesignProperties.Changed(DesignProperties.PierName, existing.Id, existing.Name, renamed.Name),
                Strings.Format(Strings.UndoRenamePier, existing.Name, renamed.Name));
            RaiseStateChanged();
            return renamed;
        }
    }

    /// <summary>
    /// Asks the host for a new name through <see cref="ElementRenaming"/> and applies it. Used by
    /// <see cref="DesignTool.Rename"/>; does nothing without a handler, or when the handler cancels or leaves
    /// everything as it was.
    /// </summary>
    /// <param name="element">A <see cref="Domain.Berth"/> or a <see cref="Domain.Pier"/>; anything else is ignored.</param>
    /// <param name="wholeRow">
    /// True to rename every berth on the clicked berth's pier by a pattern, rather than the one berth. Ignored when
    /// a pier was clicked, which always asks about the pier.
    /// </param>
    /// <remarks>
    /// Everything the answer asks for — a new id, a new name, berths named again — is one step for <see cref="Undo"/>, and
    /// happens whole or not at all: a name that turns out to be taken takes back what was already done, and is reported
    /// through <see cref="ActionFailed"/> so the host can say why.
    /// </remarks>
    internal void AskToRename(object element, bool wholeRow)
    {
        if (ElementRenaming is null) return;

        // A berth with Alt held means its whole row: one pattern for the pier rather than one name for one berth.
        if (wholeRow && element is Berth swept && swept.PierId is { } sweptPier && Marina.GetPier(sweptPier) is { } sweptOwner)
        {
            element = sweptOwner;
        }
        else
        {
            wholeRow = false;
        }

        var args = element switch
        {
            Berth berth => new DesignElementRenamingEventArgs(berth, null, berth.Id),
            Pier pier => new DesignElementRenamingEventArgs(
                wholeRow ? FirstOrNull(Marina.GetBerthsByPier(pier.Id)) : null,
                pier,
                pier.Name,
                wholeRow ? DefaultBerthPattern(pier.Id) : Naming.InferBerthPattern(pier, _berthNaming)?.Pattern ?? _berthNaming.Pattern,
                wholeRow ? DesignRenameScope.BerthsOfPier : DesignRenameScope.Element),
            _ => null,
        };

        if (args is null) return;
        ElementRenaming(this, args);

        var failure = args.Scope == DesignRenameScope.BerthsOfPier ? RenameRow(args) : RenameOne(args);
        if (failure is not null) ReportFailure(DescribeElement(element), failure);
    }

    /// <summary>
    /// A whole-row rename touches nothing but the berth names, so the pier keeps its own name and id. The host is expected to
    /// have checked the plan; one that still clashes is refused rather than half-applied.
    /// </summary>
    private InvalidOperationException? RenameRow(DesignElementRenamingEventArgs args)
    {
        var pattern = args.NewBerthPattern?.Trim();
        if (args.Cancel || args.Pier is not { } target || string.IsNullOrEmpty(pattern)) return null;

        var plan = PlanBerthNames(target.Id, pattern);
        if (!plan.IsClear) return new InvalidOperationException($"The names {string.Join(", ", plan.Clashes)} are already taken.");
        ApplyBerthNames(plan);
        return null;
    }

    /// <summary>Applies a new name to one berth, or a new name, id and berth pattern to a pier, as one step.</summary>
    private Exception? RenameOne(DesignElementRenamingEventArgs args)
    {
        var nameChanged = !string.IsNullOrWhiteSpace(args.NewName) && !string.Equals(args.NewName.Trim(), args.CurrentName, StringComparison.Ordinal);
        var idChanged = args.Pier is { } owner && args.NewPierId is { } wanted && !string.Equals(wanted.Trim(), owner.Id, StringComparison.Ordinal);
        var patternChanged = args.Pier is not null && !string.IsNullOrWhiteSpace(args.NewBerthPattern)
            && !string.Equals(args.NewBerthPattern.Trim(), args.BerthPattern, StringComparison.Ordinal);
        if (args.Cancel || (!nameChanged && !idChanged && !patternChanged)) return null;
        if (!nameChanged) args.NewName = args.CurrentName;

        using (BeginUpdate())
        using (var step = BeginStep(null))
        {
            try
            {
                if (args.Berth is { } target)
                {
                    RenameBerth(target.Id, args.NewName);
                }
                else if (args.Pier is { } pier)
                {
                    // The id moves first, so the display name is applied to the pier under its new id.
                    var wantedId = args.NewPierId?.Trim();
                    var moving = !string.IsNullOrEmpty(wantedId) && !string.Equals(wantedId, pier.Id, StringComparison.Ordinal);
                    var current = moving ? ChangePierId(pier.Id, wantedId!).Id : pier.Id;
                    RenamePier(current, args.NewName);

                    // A pattern of the host's own goes over the whole pier, including the berths the id move just
                    // renamed under the old one. Otherwise moving the id has already put the names right.
                    if (patternChanged) RenumberBerths(current, args.NewBerthPattern!.Trim());
                    else if (!moving) RenumberBerths(current);
                }

                step.Complete();
                return null;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
            {
                // The name or id is taken, or empty: the step is taken back as it closes, and the host told why.
                return ex;
            }
        }
    }

    /// <summary>
    /// The naming scheme a pattern asked for by the host stands for: the one in use, with that pattern and with the
    /// padding the pier's berths already have, so putting the pattern back unchanged renames nothing.
    /// </summary>
    /// <param name="pier">The pier being renamed.</param>
    /// <param name="pattern">The pattern wanted, or null to use the scheme as it stands.</param>
    private BerthNamingScheme? SchemeFor(Pier pier, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        var digits = Naming.InferBerthPattern(pier, _berthNaming)?.Digits ?? _berthNaming.NumberDigits;
        return _berthNaming with { Pattern = pattern, NumberDigits = digits };
    }

    /// <summary>Renames berths all at once and records it; nothing when the list is empty.</summary>
    private void RenameBerths(IReadOnlyList<(string From, string To)> renames, string description)
    {
        if (renames.Count == 0) return;
        Marina.RenameBerths(renames);
        _history.Record(new RenameBerthsCommand(renames), description);
    }

    /// <summary>
    /// First element or null. The lookups return <see cref="IReadOnlyList{T}"/>, so indexing costs
    /// nothing where <c>FirstOrDefault</c> would allocate an enumerator on every call (CA1826).
    /// </summary>
    private static T? FirstOrNull<T>(IReadOnlyList<T> items) where T : class =>
        items.Count > 0 ? items[0] : null;
}
