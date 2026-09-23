using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Design;

/// <summary>
/// Elements the designer added (or, the other way round, removed): undoing an addition removes them again, undoing a
/// removal puts them back with their dividers.
/// </summary>
internal sealed class ElementsCommand : IDesignCommand
{
    private readonly bool _added;
    private readonly LandArea[] _lands;
    private readonly Pier[] _piers;
    private readonly Divider[] _dividers;
    private readonly Berth[] _berths;

    private ElementsCommand(bool added, IEnumerable<LandArea>? lands, IEnumerable<Pier>? piers, IEnumerable<Divider>? dividers, IEnumerable<Berth>? berths)
    {
        _added = added;
        _lands = lands?.ToArray() ?? [];
        _piers = piers?.ToArray() ?? [];
        _dividers = dividers?.ToArray() ?? [];
        _berths = berths?.ToArray() ?? [];
    }

    /// <summary>Elements that have just been added, as the marina now holds them.</summary>
    public static ElementsCommand Added(IEnumerable<LandArea>? lands = null, IEnumerable<Pier>? piers = null, IEnumerable<Divider>? dividers = null, IEnumerable<Berth>? berths = null) =>
        new(added: true, lands, piers, dividers, berths);

    /// <summary>Elements that have just been removed, as they were before they went.</summary>
    public static ElementsCommand Removed(IEnumerable<LandArea>? lands = null, IEnumerable<Pier>? piers = null, IEnumerable<Divider>? dividers = null, IEnumerable<Berth>? berths = null) =>
        new(added: false, lands, piers, dividers, berths);

    public void Undo(MarinaVisualizer marina)
    {
        if (_added) Remove(marina);
        else Restore(marina);
    }

    public void Redo(MarinaVisualizer marina)
    {
        if (_added) Restore(marina);
        else Remove(marina);
    }

    /// <summary>Takes the elements out; a pier or land area only when nothing but these elements stands on it.</summary>
    private void Remove(MarinaVisualizer marina)
    {
        var mine = new HashSet<string>(_berths.Select(berth => berth.Id), StringComparer.OrdinalIgnoreCase);
        var myDividers = new HashSet<string>(_dividers.Select(divider => divider.Id), StringComparer.OrdinalIgnoreCase);

        // Checked before anything moves, so a refusal changes nothing.
        foreach (var pier in _piers)
        {
            if (marina.GetBerthsByPier(pier.Id).FirstOrDefault(berth => !mine.Contains(berth.Id)) is { } other)
            {
                throw new InvalidOperationException($"Pier '{pier.Id}' has berth '{other.Id}' on it now, which would go with it.");
            }

            if (marina.GetDividersByPier(pier.Id).FirstOrDefault(divider => !myDividers.Contains(divider.Id)) is { } separator)
            {
                throw new InvalidOperationException($"Pier '{pier.Id}' has divider '{separator.Id}' on it now, which would go with it.");
            }
        }

        foreach (var land in _lands)
        {
            if (marina.GetBerthsByLandArea(land.Id).FirstOrDefault(berth => !mine.Contains(berth.Id)) is { } other)
            {
                throw new InvalidOperationException($"Land area '{land.Id}' has berth '{other.Id}' on it now, which would go with it.");
            }
        }

        foreach (var berth in _berths) marina.RemoveBerth(berth.Id);
        foreach (var divider in _dividers) marina.RemoveDivider(divider.Id);
        foreach (var pier in _piers) marina.RemovePier(pier.Id);
        foreach (var land in _lands) marina.RemoveLandArea(land.Id);
    }

    /// <summary>Puts the elements back, containers first since berths and dividers refer to them.</summary>
    private void Restore(MarinaVisualizer marina)
    {
        foreach (var land in _lands) RequireFree(marina.GetLandArea(land.Id), "land area", land.Id);
        foreach (var pier in _piers) RequireFree(marina.GetPier(pier.Id), "pier", pier.Id);
        foreach (var divider in _dividers) RequireFree(marina.GetDivider(divider.Id), "divider", divider.Id);
        foreach (var berth in _berths) RequireFree(marina.GetBerth(berth.Id), "berth", berth.Id);

        for (var i = 0; i < _lands.Length; i++)
        {
            marina.AddLandArea(_lands[i]);
            _lands[i] = marina.GetLandArea(_lands[i].Id) ?? _lands[i];
        }

        for (var i = 0; i < _piers.Length; i++)
        {
            marina.AddPier(_piers[i]);
            _piers[i] = marina.GetPier(_piers[i].Id) ?? _piers[i];
        }

        for (var i = 0; i < _dividers.Length; i++)
        {
            marina.AddDivider(_dividers[i]);
            _dividers[i] = marina.GetDivider(_dividers[i].Id) ?? _dividers[i];
        }

        for (var i = 0; i < _berths.Length; i++)
        {
            // A restored berth is no longer part of a multi-berth: that grouping belonged to the layout it left.
            marina.AddBerth(_berths[i] with { MultiBerthId = null });
            _berths[i] = marina.GetBerth(_berths[i].Id) ?? _berths[i];
        }
    }

    private static void RequireFree(object? holder, string what, string id)
    {
        if (holder is not null) throw new InvalidOperationException($"Another {what} is now called '{id}'.");
    }
}

/// <summary>
/// One property of one element set to another value: undoing sets it back, and touches nothing else about the element.
/// </summary>
/// <typeparam name="TElement">The kind of element.</typeparam>
/// <typeparam name="TValue">The type of the property.</typeparam>
internal sealed class PropertyCommand<TElement, TValue> : IDesignCommand
    where TElement : class
{
    private readonly PropertyAccess<TElement, TValue> _access;
    private readonly string _id;
    private readonly TValue _before;
    private readonly TValue _after;

    public PropertyCommand(PropertyAccess<TElement, TValue> access, string id, TValue before, TValue after)
    {
        _access = access;
        _id = id;
        _before = before;
        _after = after;
    }

    public void Undo(MarinaVisualizer marina) => Apply(marina, _after, _before);

    public void Redo(MarinaVisualizer marina) => Apply(marina, _before, _after);

    private void Apply(MarinaVisualizer marina, TValue expected, TValue value)
    {
        var current = _access.Get(marina, _id) ?? throw new KeyNotFoundException($"The {_access.What} '{_id}' no longer exists.");

        // Someone else has set it since: putting ours back would throw their change away.
        if (!_access.Comparer.Equals(_access.Read(current), expected))
        {
            throw new InvalidOperationException($"The {_access.What} of '{_id}' has been changed since.");
        }

        _access.Update(marina, _access.Write(current, value));
    }
}

/// <summary>How to find an element, read one of its properties and write it back, for <see cref="PropertyCommand{TElement, TValue}"/>.</summary>
/// <param name="What">What the property is called in a message, e.g. "trees of land area".</param>
/// <param name="Get">Looks the element up by id.</param>
/// <param name="Read">Reads the property.</param>
/// <param name="Write">Returns the element with the property set.</param>
/// <param name="Update">Stores the changed element.</param>
/// <param name="Comparer">Tells whether the property still holds a value.</param>
internal sealed record PropertyAccess<TElement, TValue>(
    string What,
    Func<MarinaVisualizer, string, TElement?> Get,
    Func<TElement, TValue> Read,
    Func<TElement, TValue, TElement> Write,
    Action<MarinaVisualizer, TElement> Update,
    IEqualityComparer<TValue> Comparer)
    where TElement : class;

/// <summary>The properties the designer changes, and the commands that record a change to one of them.</summary>
internal static class DesignProperties
{
    public static readonly PropertyAccess<LandArea, IReadOnlyList<LandTree>> LandTrees = new(
        "trees",
        (marina, id) => marina.GetLandArea(id),
        land => land.Trees,
        (land, trees) => land with { Trees = trees },
        (marina, land) => marina.UpdateLandArea(land),
        new SequenceComparer<LandTree>());

    public static readonly PropertyAccess<Pier, string> PierName = new(
        "name",
        (marina, id) => marina.GetPier(id),
        pier => pier.Name,
        (pier, name) => pier with { Name = name },
        (marina, pier) => marina.UpdatePier(pier),
        StringComparer.Ordinal);

    public static readonly PropertyAccess<Pier, PierServices> PierServices = new(
        "pedestals",
        (marina, id) => marina.GetPier(id),
        pier => pier.Services,
        (pier, services) => pier with { Services = services },
        (marina, pier) => marina.UpdatePier(pier),
        EqualityComparer<PierServices>.Default);

    public static readonly PropertyAccess<Berth, PierServices?> BerthServices = new(
        "pedestals",
        (marina, id) => marina.GetBerth(id),
        berth => berth.Services,
        (berth, services) => berth with { Services = services },
        (marina, berth) => marina.UpdateBerth(berth),
        EqualityComparer<PierServices?>.Default);

    /// <summary>A command recording that <paramref name="access"/> of element <paramref name="id"/> went from one value to another.</summary>
    public static PropertyCommand<TElement, TValue> Changed<TElement, TValue>(PropertyAccess<TElement, TValue> access, string id, TValue before, TValue after)
        where TElement : class =>
        new(access, id, before, after);

    /// <summary>Lists compared item by item, so a copy of the same trees still counts as the same trees.</summary>
    private sealed class SequenceComparer<T> : IEqualityComparer<IReadOnlyList<T>>
    {
        public bool Equals(IReadOnlyList<T>? x, IReadOnlyList<T>? y) =>
            ReferenceEquals(x, y) || (x is not null && y is not null && x.SequenceEqual(y));

        public int GetHashCode(IReadOnlyList<T> obj) => obj.Count;
    }
}

/// <summary>
/// Berths that were given other names, as (old name, new name). Undoing names them back, all at once, since a renumber
/// moves names along a chain (A-L02 to A-L01, A-L03 to A-L02) and one by one each would land on the name the next still holds.
/// </summary>
internal sealed class RenameBerthsCommand : IDesignCommand
{
    private readonly (string From, string To)[] _renames;
    private (string From, string To)[] _reverted;

    public RenameBerthsCommand(IEnumerable<(string From, string To)> renames)
    {
        _renames = renames.Where(r => !string.Equals(r.From, r.To, StringComparison.Ordinal)).ToArray();
        _reverted = _renames;
    }

    public void Undo(MarinaVisualizer marina)
    {
        var reverts = RenamesToRevert(marina);
        marina.RenameBerths(reverts);

        // Only what was actually named back is named forward again by a redo.
        _reverted = reverts.Select(r => (From: r.To, To: r.From)).ToArray();
    }

    public void Redo(MarinaVisualizer marina) => marina.RenameBerths(_reverted);

    /// <summary>
    /// The renames that put these back, for the berths that still carry their new name. One whose old name has since been
    /// taken by a berth that stays put is left where it is, since overwriting that one is not the undo's to do; leaving it
    /// can in turn keep another where it is, so this runs until nothing more drops out.
    /// </summary>
    private List<(string From, string To)> RenamesToRevert(MarinaVisualizer marina)
    {
        var reverts = _renames
            .Where(r => marina.GetBerth(r.To) is not null)
            .Select(r => (From: r.To, To: r.From))
            .ToList();

        while (true)
        {
            var leaving = new HashSet<string>(reverts.Select(r => r.From), StringComparer.OrdinalIgnoreCase);
            var blocked = reverts.FindIndex(r => marina.GetBerth(r.To) is { } holder && !leaving.Contains(holder.Id));
            if (blocked < 0) return reverts;
            reverts.RemoveAt(blocked);
        }
    }
}

/// <summary>A pier given another id (its berths and dividers follow it). Undoing moves it back.</summary>
internal sealed class ChangePierIdCommand : IDesignCommand
{
    private readonly string _from;
    private readonly string _to;

    public ChangePierIdCommand(string from, string to)
    {
        _from = from;
        _to = to;
    }

    public void Undo(MarinaVisualizer marina) => marina.ChangePierId(_to, _from);

    public void Redo(MarinaVisualizer marina) => marina.ChangePierId(_from, _to);
}

/// <summary>The mainland set, replaced or removed. Undoing puts back the one there was before, or none.</summary>
internal sealed class ShorelineCommand : IDesignCommand
{
    private readonly Shoreline? _before;
    private Shoreline? _after;

    public ShorelineCommand(Shoreline? before, Shoreline? after)
    {
        _before = before;
        _after = after;
    }

    public void Undo(MarinaVisualizer marina)
    {
        Require(marina, _after);
        marina.SetShoreline(_before);
    }

    public void Redo(MarinaVisualizer marina)
    {
        Require(marina, _before);
        marina.SetShoreline(_after);
        _after = marina.Shoreline;
    }

    private static void Require(MarinaVisualizer marina, Shoreline? expected)
    {
        if (!ReferenceEquals(marina.Shoreline, expected)) throw new InvalidOperationException("The mainland has been changed since.");
    }
}
