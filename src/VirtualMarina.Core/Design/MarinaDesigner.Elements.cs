using System.Globalization;
using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Design;

/// <summary>Creating, changing and erasing elements, from the tools or from code.</summary>
public sealed partial class MarinaDesigner
{
    /// <summary>
    /// Adds a land area with the given outline and the current <see cref="LandKind"/> and <see cref="LandHeight"/>, raising
    /// <see cref="ElementCreating"/> and <see cref="ElementCreated"/>. Returns null when a handler cancels.
    /// </summary>
    /// <remarks>
    /// A lawn gets trees scattered over it at <see cref="TreeDensity"/>, which a handler of <see cref="ElementCreating"/> sees and
    /// may replace. A handler that changes the outline (or the kind) but leaves those trees alone gets trees for the land it
    /// actually made, so none stand outside it.
    /// </remarks>
    /// <exception cref="MarinaLayoutException">The outline is invalid (e.g. its edges cross).</exception>
    public LandArea? CreateLandArea(IReadOnlyList<Vector2> outline)
    {
        ArgumentNullException.ThrowIfNull(outline);
        using (BeginUpdate())
        {
            var prefix = _landKind switch { LandKind.Grass => "lawn", LandKind.Breakwater => "breakwater", _ => "quay" };
            var id = DesignNaming.NextId(prefix + "-", Marina.GetLandAreas().Select(l => l.Id));
            var trees = TreesFor(outline, _landKind);
            var land = new LandArea(id, outline, _landHeight, _landKind)
            {
                Name = $"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(prefix)} {id[(prefix.Length + 1)..]}",
                Trees = trees,
            };

            var args = new DesignElementCreatingEventArgs(DesignTool.DrawLandArea, land, null, Array.Empty<Berth>(), Array.Empty<Divider>());
            ElementCreating?.Invoke(this, args);
            if (args.Cancel || args.LandArea is not { } wanted)
            {
                FinishDraft(DesignDraftChange.Canceled);
                return null;
            }

            // The trees were scattered over the outline drawn; land the handler reshaped gets trees of its own.
            if (ReferenceEquals(wanted.Trees, land.Trees) && (wanted.Kind != _landKind || !wanted.Points.SequenceEqual(outline)))
            {
                wanted = wanted with { Trees = TreesFor(wanted.Points, wanted.Kind) };
            }

            Marina.AddLandArea(wanted);
            var added = Marina.GetLandArea(wanted.Id)!;
            _history.Record(ElementsCommand.Added(lands: [added]), Strings.Format(Strings.UndoDrawLandArea, added.DisplayName));
            FinishDraft(DesignDraftChange.Completed);
            ElementCreated?.Invoke(this, new DesignElementCreatedEventArgs(DesignTool.DrawLandArea, added, null, Array.Empty<Berth>(), Array.Empty<Divider>()));
            if (added.Trees.Count > 0) TreesPlanted?.Invoke(this, new DesignTreesPlantedEventArgs(added, 0));
            return added;
        }
    }

    /// <summary>
    /// Sets the mainland behind the marina from a drawn coast and the side of it that is land, using the current
    /// <see cref="Scenery"/>. Whatever mainland was there is replaced. Returns null when a handler cancels.
    /// </summary>
    /// <param name="line">The coast, at least two points. Its first and last segments run on without end.</param>
    /// <param name="landOnLeft">True when the land is to the left of the line walked from the first point to the last.</param>
    /// <exception cref="MarinaLayoutException">The line's endless segments cross, so neither side of it is the land.</exception>
    public Domain.Shoreline? CreateShoreline(IReadOnlyList<Vector2> line, bool landOnLeft)
    {
        ArgumentNullException.ThrowIfNull(line);
        using (BeginUpdate())
        {
            var previous = Marina.Shoreline;
            var shoreline = new Domain.Shoreline(line, landOnLeft)
            {
                // The coast keeps the look of the land already drawn, so the two read as one piece of ground.
                Height = previous?.Height ?? _landHeight,
                Kind = previous?.Kind ?? LandKind.Grass,
                Scenery = _scenery,
                ScenerySeed = _random.Next(1, int.MaxValue),
            };

            var args = new DesignElementCreatingEventArgs(DesignTool.DrawShoreline, null, null, Array.Empty<Berth>(), Array.Empty<Divider>()) { Shoreline = shoreline };
            ElementCreating?.Invoke(this, args);
            if (args.Cancel || args.Shoreline is null)
            {
                FinishDraft(DesignDraftChange.Canceled);
                return null;
            }

            Marina.SetShoreline(args.Shoreline);
            _history.Record(new ShorelineCommand(previous, Marina.Shoreline), Strings.UndoDrawShoreline);
            FinishDraft(DesignDraftChange.Completed);
            ElementCreated?.Invoke(this, new DesignElementCreatedEventArgs(DesignTool.DrawShoreline, null, null, Array.Empty<Berth>(), Array.Empty<Divider>()) { Shoreline = args.Shoreline });
            return args.Shoreline;
        }
    }

    /// <summary>Takes the mainland away, leaving the marina in open water. Returns false when there was none.</summary>
    public bool DeleteShoreline()
    {
        var previous = Marina.Shoreline;
        if (previous is null) return false;

        using (BeginUpdate())
        {
            Marina.SetShoreline(null);
            _history.Record(new ShorelineCommand(previous, null), Strings.UndoRemoveShoreline);
            RaiseStateChanged();
            return true;
        }
    }

    /// <summary>
    /// Adds a pier from <paramref name="start"/> (shore end) to <paramref name="end"/> with the current <see cref="PierType"/>,
    /// <see cref="PierWidth"/> and <see cref="PierBerthingSides"/>. Returns null when a handler cancels.
    /// </summary>
    /// <exception cref="ArgumentException">The ends are closer than <see cref="MinimumPierLength"/>.</exception>
    public Pier? CreatePier(Vector2 start, Vector2 end)
    {
        var length = Vector2.Distance(start, end);
        if (!(length >= MinimumPierLength)) throw new ArgumentException($"A pier must be at least {MinimumPierLength} m long.", nameof(end));

        using (BeginUpdate())
        {
            var id = Naming.NextPierId();
            var pier = new Pier(id, GeneratedPierName(id), start, MarinaMath.DirectionToHeading(end - start), length, _pierWidth, _pierType)
            {
                BerthingSides = _pierSides,
                Services = _berthServices,
            };

            var args = new DesignElementCreatingEventArgs(DesignTool.DrawPier, null, pier, Array.Empty<Berth>(), Array.Empty<Divider>());
            ElementCreating?.Invoke(this, args);
            if (args.Cancel || args.Pier is null)
            {
                FinishDraft(DesignDraftChange.Canceled);
                return null;
            }

            Marina.AddPier(args.Pier);
            var added = Marina.GetPier(args.Pier.Id)!;
            _history.Record(ElementsCommand.Added(piers: [added]), Strings.Format(Strings.UndoDrawPier, added.Id));
            FinishDraft(DesignDraftChange.Completed);
            ElementCreated?.Invoke(this, new DesignElementCreatedEventArgs(DesignTool.DrawPier, null, added, Array.Empty<Berth>(), Array.Empty<Divider>()));
            return added;
        }
    }

    /// <summary>
    /// Adds a row of berths of the current <see cref="BerthWidth"/>, <see cref="BerthLength"/> and <see cref="BerthDepth"/> on one side of a
    /// pier, covering the stretch between two distances from the pier's start (equal distances add one berth), separated by
    /// <see cref="BerthSeparators"/> and <see cref="BerthGap"/>. With <see cref="AlignBerthsToExisting"/> the row lines up with existing
    /// berths on that side; otherwise it starts exactly at <paramref name="fromAlong"/>. Places already taken are skipped, and
    /// <see cref="BerthServices"/> switches the pier's pedestals on. Returns the berths added (empty when none fit or a handler cancels).
    /// </summary>
    /// <param name="pierId">The pier.</param>
    /// <param name="side">Side of the pier.</param>
    /// <param name="fromAlong">Distance from the pier's start where the row begins.</param>
    /// <param name="toAlong">Distance from the pier's start where the row ends.</param>
    /// <exception cref="KeyNotFoundException">No pier has this id.</exception>
    /// <exception cref="InvalidOperationException">The pier has no berths on <paramref name="side"/>.</exception>
    public IReadOnlyList<Berth> CreateBerths(string pierId, PierSide side, float fromAlong, float toAlong)
    {
        ArgumentNullException.ThrowIfNull(pierId);
        var pier = Marina.GetPier(pierId) ?? throw new KeyNotFoundException($"Pier '{pierId}' does not exist.");
        if (!pier.HasBerthsOn(side)) throw new InvalidOperationException($"Pier '{pier.Id}' has no berths on the {side} side.");

        using (BeginUpdate())
        {
            var (berths, dividers) = Planner.Plan(pier, side, fromAlong, toAlong, RowSettings, preview: false);
            if (berths.Count == 0)
            {
                FinishDraft(DesignDraftChange.Canceled);
                return Array.Empty<Berth>();
            }

            var args = new DesignElementCreatingEventArgs(DesignTool.AddBerths, null, null, berths, dividers);
            ElementCreating?.Invoke(this, args);
            if (args.Cancel || args.Berths.Count == 0)
            {
                FinishDraft(DesignDraftChange.Canceled);
                return Array.Empty<Berth>();
            }

            // Pedestals belong to the pier, so switching them on is part of the same undoable step.
            var services = _berthServices != PierServices.None && (pier.Services & _berthServices) != _berthServices
                ? pier.Services | _berthServices
                : (PierServices?)null;

            using (Marina.BeginUpdate())
            {
                Marina.AddBerths(args.Berths);
                Marina.AddDividers(args.Dividers ?? Array.Empty<Divider>());
                if (services is { } switchedOn) Marina.UpdatePier(pier with { Services = switchedOn });
            }

            var added = args.Berths.Select(s => Marina.GetBerth(s.Id)!).ToArray();
            var addedDividers = (args.Dividers ?? Array.Empty<Divider>()).Select(d => Marina.GetDivider(d.Id)!).ToArray();
            var description = added.Length == 1
                ? Strings.Format(Strings.UndoAddBerth, added[0].Id)
                : Strings.Plural("UndoAddBerths", added.Length, added.Length);

            var commands = new List<IDesignCommand> { ElementsCommand.Added(dividers: addedDividers, berths: added) };
            if (services is { } after) commands.Add(DesignProperties.Changed(DesignProperties.PierServices, pier.Id, pier.Services, after));
            RecordStep(description, commands);

            FinishDraft(DesignDraftChange.Completed);
            ElementCreated?.Invoke(this, new DesignElementCreatedEventArgs(DesignTool.AddBerths, null, null, added, addedDividers));
            return added;
        }
    }

    /// <summary>
    /// Adds a land berth (<see cref="Berth.OnLand"/>) of the current <see cref="BerthWidth"/> and <see cref="BerthLength"/> to a land area,
    /// where a boat is stored or worked on ashore. Raises <see cref="ElementCreating"/> and <see cref="ElementCreated"/>; returns null
    /// when a handler cancels.
    /// </summary>
    /// <param name="landAreaId">The land area the berth stands on.</param>
    /// <param name="position">Center of the spot, in plan coordinates.</param>
    /// <param name="headingDegrees">Direction the stored boat's bow points; null uses <see cref="LandBerthHeading"/>.</param>
    /// <exception cref="KeyNotFoundException">No land area has this id.</exception>
    public Berth? CreateLandBerth(string landAreaId, Vector2 position, float? headingDegrees = null)
    {
        ArgumentNullException.ThrowIfNull(landAreaId);
        var land = Marina.GetLandArea(landAreaId) ?? throw new KeyNotFoundException($"Land area '{landAreaId}' does not exist.");

        using (BeginUpdate())
        {
            var ashore = _berthNaming.AshoreNumbering;
            var id = new BerthNames(_berthNaming, Marina.GetBerths().Select(existing => existing.Id), ashore.Start, ashore.Step, _berthNaming.LandNumberDigits)
                .Next(number => _berthNaming.Format(land, number));

            var berth = Berth.OnLand(id, land.Id, position, headingDegrees ?? _landBerthHeading, _berthLength, _berthWidth);
            var args = new DesignElementCreatingEventArgs(DesignTool.AddLandBerths, null, null, new[] { berth }, Array.Empty<Divider>());
            ElementCreating?.Invoke(this, args);
            if (args.Cancel || args.Berths.Count == 0)
            {
                FinishDraft(DesignDraftChange.Canceled);
                return null;
            }

            Marina.AddBerths(args.Berths);
            var added = args.Berths.Select(created => Marina.GetBerth(created.Id)!).ToArray();
            _history.Record(ElementsCommand.Added(berths: added), Strings.Format(Strings.UndoAddLandBerth, added[0].Id));
            FinishDraft(DesignDraftChange.Completed);
            ElementCreated?.Invoke(this, new DesignElementCreatedEventArgs(DesignTool.AddLandBerths, null, null, added, Array.Empty<Divider>()));
            return added[0];
        }
    }

    /// <summary>
    /// Replaces the trees of a lawn with new, randomly placed ones (kept clear of its land berths) and raises <see cref="TreesPlanted"/>.
    /// Returns the updated land area, or null when it doesn't exist or is not a <see cref="LandKind.Grass"/> area.
    /// </summary>
    /// <param name="landAreaId">The lawn.</param>
    /// <param name="treesPer1000SquareMeters">Coverage; null uses <see cref="TreeDensity"/>, 0 removes the trees.</param>
    public LandArea? PlantTrees(string landAreaId, float? treesPer1000SquareMeters = null)
    {
        ArgumentNullException.ThrowIfNull(landAreaId);
        if (Marina.GetLandArea(landAreaId) is not { Kind: LandKind.Grass } land) return null;

        var density = treesPer1000SquareMeters is { } d ? DesignerDefaults.TreeDensity.Require(d, nameof(treesPer1000SquareMeters)) : _treeDensity;
        var keepClear = Marina.GetBerthsByLandArea(land.Id).Select(landBerth => landBerth.Bounds);
        return SetTrees(land, LandArea.GenerateTrees(land.Points, density, _random, keepClear), density > 0f ? Strings.UndoPlantTrees : Strings.UndoRemoveTrees);
    }

    /// <summary>
    /// Removes every tree from a land area (of any kind) and raises <see cref="TreesPlanted"/> with an empty
    /// <see cref="LandArea.Trees"/>. Returns the updated land area, or null when it doesn't exist or has no trees.
    /// </summary>
    /// <param name="landAreaId">The land area.</param>
    public LandArea? RemoveTrees(string landAreaId)
    {
        ArgumentNullException.ThrowIfNull(landAreaId);
        return Marina.GetLandArea(landAreaId) is { Trees.Count: > 0 } land ? SetTrees(land, Array.Empty<LandTree>(), Strings.UndoRemoveTrees) : null;
    }

    /// <summary>
    /// Gives berths the pedestals in <see cref="BerthServices"/>, and records one step for <see cref="Undo"/>.
    /// This is what <see cref="DesignTool.EditServices"/> does when a berth is clicked.
    /// </summary>
    /// <param name="berthId">The berth clicked.</param>
    /// <param name="wholeSide">
    /// True to change every berth down that side of the pier, false for the one berth.
    /// </param>
    /// <returns>The berths that changed; empty when the id is unknown or they already had these pedestals.</returns>
    public IReadOnlyList<Berth> SetBerthServices(string berthId, bool wholeSide = false)
    {
        ArgumentNullException.ThrowIfNull(berthId);
        return Marina.GetBerth(berthId) is { } berth ? SetBerthServices(berth, wholeSide) : Array.Empty<Berth>();
    }

    /// <summary>
    /// Removes a berth, or a pier or land area with its berths, and raises <see cref="ElementErased"/>. Dividers left without a berth on
    /// either side go too (a pier takes all of its dividers). Returns false when the element doesn't exist.
    /// </summary>
    /// <param name="element">A <see cref="Berth"/>, <see cref="Pier"/> or <see cref="LandArea"/> (matched by id).</param>
    public bool Erase(object element)
    {
        ArgumentNullException.ThrowIfNull(element);
        IReadOnlyList<Berth> removedBerths;
        IReadOnlyList<Divider> removedDividers = Array.Empty<Divider>();
        ElementsCommand command;
        switch (element)
        {
            case Berth berth when Marina.GetBerth(berth.Id) is { } current:
                removedBerths = [current];
                removedDividers = Planner.OrphanedDividers(removedBerths);
                using (Marina.BeginUpdate())
                {
                    Marina.RemoveBerth(current.Id);
                    foreach (var divider in removedDividers) Marina.RemoveDivider(divider.Id);
                }

                command = ElementsCommand.Removed(dividers: removedDividers, berths: removedBerths);
                element = current;
                break;
            case Pier pier when Marina.GetPier(pier.Id) is { } current:
                removedBerths = Marina.GetBerthsByPier(current.Id);

                // The pier takes its own dividers with it; separators that belonged to no pier but only served its berths go too.
                var strays = Planner.OrphanedDividers(removedBerths)
                    .Where(divider => !string.Equals(divider.PierId, current.Id, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                removedDividers = [.. Marina.GetDividersByPier(current.Id), .. strays];
                using (Marina.BeginUpdate())
                {
                    Marina.RemovePier(current.Id); // Removes its berths and dividers too.
                    foreach (var divider in strays) Marina.RemoveDivider(divider.Id);
                }

                command = ElementsCommand.Removed(piers: [current], dividers: removedDividers, berths: removedBerths);
                element = current;
                break;
            case LandArea land when Marina.GetLandArea(land.Id) is { } current:
                removedBerths = Marina.GetBerthsByLandArea(current.Id);
                Marina.RemoveLandArea(current.Id);
                command = ElementsCommand.Removed(lands: [current], berths: removedBerths);
                element = current;
                break;
            default:
                return false;
        }

        using (BeginUpdate())
        {
            _history.Record(command, Strings.Format(Strings.UndoErase, DescribeElement(element)));
            ErasedUnderPointer();
            ElementErased?.Invoke(this, new DesignElementErasedEventArgs(element, removedBerths, removedDividers));
            RaiseStateChanged();
        }

        return true;
    }

    /// <summary>
    /// Removes every berth on a pier, and the separators that only served them, leaving the pier itself in place.
    /// This is what the eraser does when Alt is held over one of the pier's berths. Records one step for
    /// <see cref="Undo"/> and raises <see cref="ElementErased"/> with the pier as the element.
    /// </summary>
    /// <param name="pierId">The pier to clear.</param>
    /// <returns>False when no pier has this id, or it had no berths.</returns>
    public bool EraseBerthsOfPier(string pierId)
    {
        ArgumentNullException.ThrowIfNull(pierId);
        if (Marina.GetPier(pierId) is not { } pier) return false;

        var berths = Marina.GetBerthsByPier(pier.Id);
        if (berths.Count == 0) return false;

        var dividers = RemoveBerthsAndSeparators(berths);
        using (BeginUpdate())
        {
            _history.Record(ElementsCommand.Removed(dividers: dividers, berths: berths), Strings.Plural("UndoEraseBerthsOfPier", berths.Count, berths.Count, pier.Name));
            ErasedUnderPointer();
            ElementErased?.Invoke(this, new DesignElementErasedEventArgs(pier, berths, dividers));
            RaiseStateChanged();
        }

        return true;
    }

    /// <summary>
    /// Removes the berths selected in the marina (<see cref="MarinaVisualizer.SelectedBerths"/>), with the separators that only
    /// served them, as one step for <see cref="Undo"/>. This is what Delete does with the <see cref="DesignTool.SelectArea"/>
    /// tool. Raises <see cref="ElementErased"/> once for each berth, with the separators it took along.
    /// </summary>
    /// <returns>How many berths were removed; 0 when nothing was selected.</returns>
    public int EraseSelectedBerths()
    {
        var berths = Marina.SelectedBerths;
        if (berths.Count == 0) return 0;

        var dividers = RemoveBerthsAndSeparators(berths);
        using (BeginUpdate())
        {
            var description = berths.Count == 1
                ? Strings.Format(Strings.UndoErase, DescribeElement(berths[0]))
                : Strings.Plural("UndoEraseBerths", berths.Count, berths.Count);
            _history.Record(ElementsCommand.Removed(dividers: dividers, berths: berths), description);
            ErasedUnderPointer();

            // Each separator is reported with the first erased berth it stood beside.
            var unclaimed = new List<Divider>(dividers);
            foreach (var berth in berths)
            {
                var own = unclaimed.Where(divider => BerthPlanner.Separates(divider, berth)).ToArray();
                foreach (var divider in own) unclaimed.Remove(divider);
                ElementErased?.Invoke(this, new DesignElementErasedEventArgs(berth, [berth], own));
            }

            RaiseStateChanged();
        }

        return berths.Count;
    }

    /// <summary>The berths a pedestal change would touch: the one clicked, or its whole side of the pier.</summary>
    internal Berth[] ServiceTargets(Berth berth, bool wholeSide)
    {
        if (!wholeSide || berth.PierId is not { } pierId || Marina.GetPier(pierId) is not { } pier) return [berth];

        var side = PierGeometry.SideOf(pier, berth.Center);
        return Marina.GetBerthsByPier(pier.Id)
            .Where(other => PierGeometry.SideOf(pier, other.Center) == side)
            .ToArray();
    }

    internal Berth[] SetBerthServices(Berth berth, bool wholeSide)
    {
        var targets = ServiceTargets(berth, wholeSide).Where(b => b.Services != _berthServices).ToArray();
        if (targets.Length == 0) return [];

        using (BeginUpdate())
        {
            using (Marina.BeginUpdate())
            {
                foreach (var target in targets) Marina.UpdateBerth(target with { Services = _berthServices });
            }

            RecordStep(
                Strings.Plural("UndoSetServices", targets.Length, _berthServices.GetDisplayName(), targets.Length),
                targets.Select(target => DesignProperties.Changed(DesignProperties.BerthServices, target.Id, target.Services, (PierServices?)_berthServices)));
            InvalidateOverlay();
            RaiseStateChanged();
        }

        return targets;
    }

    private static string DescribeElement(object element) => element switch
    {
        Berth berth => Strings.Format(Strings.ElementBerth, berth.DisplayName),
        Pier pier => Strings.Format(Strings.ElementPier, pier.Name),
        LandArea land => land.DisplayName,
        _ => element.GetType().Name,
    };

    /// <summary>Trees scattered over a new land area of this kind: a lawn gets them at <see cref="TreeDensity"/>, other land none.</summary>
    private IReadOnlyList<LandTree> TreesFor(IReadOnlyList<Vector2> outline, LandKind kind) =>
        kind == LandKind.Grass ? LandArea.GenerateTrees(outline, _treeDensity, _random) : Array.Empty<LandTree>();

    private LandArea SetTrees(LandArea land, IReadOnlyList<LandTree> trees, string description)
    {
        using (BeginUpdate())
        {
            var previous = land.Trees.Count;
            Marina.UpdateLandArea(land with { Trees = trees });
            var updated = Marina.GetLandArea(land.Id)!;
            _history.Record(
                DesignProperties.Changed(DesignProperties.LandTrees, land.Id, land.Trees, updated.Trees),
                Strings.Format(Strings.UndoTreesOn, description, land.DisplayName));
            TreesPlanted?.Invoke(this, new DesignTreesPlantedEventArgs(updated, previous));
            RaiseStateChanged();
            return updated;
        }
    }

    /// <summary>Removes berths of the water, and the separators only they used, in one batch. Returns those separators.</summary>
    private List<Divider> RemoveBerthsAndSeparators(IReadOnlyList<Berth> berths)
    {
        var dividers = Planner.OrphanedDividers(berths);
        using (Marina.BeginUpdate())
        {
            foreach (var berth in berths) Marina.RemoveBerth(berth.Id);
            foreach (var divider in dividers) Marina.RemoveDivider(divider.Id);
        }

        return dividers;
    }

    /// <summary>What was under the pointer has gone; nothing is until the pointer moves again.</summary>
    private void ErasedUnderPointer()
    {
        _handler.ClearHover();
        InvalidateOverlay();
    }

    /// <summary>Records several commands as one step.</summary>
    private void RecordStep(string description, IEnumerable<IDesignCommand> commands)
    {
        using var action = _history.Begin(description);
        foreach (var command in commands) _history.Record(command, description);
        action.Complete();
    }
}
