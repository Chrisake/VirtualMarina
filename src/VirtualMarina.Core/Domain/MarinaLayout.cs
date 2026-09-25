using System.Numerics;
using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Domain;

/// <summary>
/// Complete description of a marina: piers, berths (flat list, linked by <see cref="Berth.PierId"/>),
/// dividers, multi-berths, surrounding land and land berths (linked by <see cref="Berth.LandAreaId"/>). Pass to <c>MarinaVisualizer.InitializeLayout</c>.
/// </summary>
/// <remarks>
/// Every list is the record's own read-only copy, so a layout built from a host's lists does not change when those lists do, and
/// two layouts with the same contents are equal. A list set to null reads as empty.
/// </remarks>
public sealed record MarinaLayout
{
    private readonly ValueList<Pier> _piers = ValueList<Pier>.Empty;
    private readonly ValueList<Berth> _berths = ValueList<Berth>.Empty;
    private readonly ValueList<Divider> _dividers = ValueList<Divider>.Empty;
    private readonly ValueList<MultiBerth> _multiBerths = ValueList<MultiBerth>.Empty;
    private readonly ValueList<LandArea> _landAreas = ValueList<LandArea>.Empty;

    /// <summary>Marina name (<c>IMarinaVisualizer.MarinaName</c>).</summary>
    public string Name { get; init; } = "Marina";

    /// <summary>Piers. Ids must be unique.</summary>
    public IReadOnlyList<Pier> Piers { get => _piers; init => _piers = ValueList<Pier>.From(value); }

    /// <summary>
    /// Berths, each referencing a pier in <see cref="Piers"/> or, for land berths, a land area in <see cref="LandAreas"/>. Ids must be unique.
    /// </summary>
    public IReadOnlyList<Berth> Berths { get => _berths; init => _berths = ValueList<Berth>.From(value); }

    /// <summary>Finger piers, pile rows and booms between berths.</summary>
    public IReadOnlyList<Divider> Dividers { get => _dividers; init => _dividers = ValueList<Divider>.From(value); }

    /// <summary>Boats spanning several berths. Member berths take the multi-berth's status and boat when the layout is loaded.</summary>
    public IReadOnlyList<MultiBerth> MultiBerths { get => _multiBerths; init => _multiBerths = ValueList<MultiBerth>.From(value); }

    /// <summary>Quays, breakwaters and lawns drawn around the water. Ids must be unique.</summary>
    public IReadOnlyList<LandArea> LandAreas { get => _landAreas; init => _landAreas = ValueList<LandArea>.From(value); }

    /// <summary>
    /// The mainland behind the marina, drawn beneath the <see cref="LandAreas"/>. Null for a marina standing in open
    /// water, which is how every layout written before this existed reads back.
    /// </summary>
    public Shoreline? Shoreline { get; init; }

    /// <summary>
    /// Passing traffic out at sea, or null for empty water. It is decoration rather than layout: the vessels are not
    /// berths and cannot be clicked.
    /// </summary>
    public MarineTraffic? MarineTraffic { get; init; }

    /// <summary>A layout with nothing in it.</summary>
    public static MarinaLayout Empty { get; } = new();

    /// <summary>
    /// Every element as one flat array, in dependency order: the shoreline and the passing traffic, each if there is one,
    /// then land areas, piers, dividers, berths, then multi-berths. Each entry is the immutable record itself
    /// (<see cref="Domain.Shoreline"/>, <see cref="Domain.MarineTraffic"/>, <see cref="LandArea"/>, <see cref="Pier"/>,
    /// <see cref="Divider"/>, <see cref="Berth"/>, <see cref="MultiBerth"/>), so host code can pattern-match on it.
    /// Everything in the layout but its <see cref="Name"/> is there, so <see cref="FromObjects"/> given the array and the
    /// name builds an equal layout.
    /// </summary>
    public object[] ToObjects() =>
        new object?[] { Shoreline, MarineTraffic }.OfType<object>()
            .Concat(LandAreas)
            .Concat(Piers)
            .Concat(Dividers)
            .Concat(Berths)
            .Concat(MultiBerths)
            .ToArray();

    /// <summary>Builds a layout from elements in any order (the inverse of <see cref="ToObjects"/>). The result is not validated.</summary>
    /// <param name="elements">
    /// A shoreline and passing traffic (at most one of each; a later one replaces an earlier one), land areas, piers,
    /// dividers, berths and multi-berths.
    /// </param>
    /// <param name="name">Marina name.</param>
    /// <exception cref="ArgumentException">An element is null or of another type.</exception>
    public static MarinaLayout FromObjects(IEnumerable<object> elements, string name = "Marina")
    {
        ArgumentNullException.ThrowIfNull(elements);
        Shoreline? shoreline = null;
        MarineTraffic? traffic = null;
        var land = new List<LandArea>();
        var piers = new List<Pier>();
        var dividers = new List<Divider>();
        var berths = new List<Berth>();
        var groups = new List<MultiBerth>();
        foreach (var element in elements)
        {
            switch (element)
            {
                case Shoreline c: shoreline = c; break;
                case MarineTraffic t: traffic = t; break;
                case LandArea l: land.Add(l); break;
                case Pier d: piers.Add(d); break;
                case Divider v: dividers.Add(v); break;
                case Berth s: berths.Add(s); break;
                case MultiBerth b: groups.Add(b); break;
                default: throw new ArgumentException(Strings.Format(Strings.ErrorUnsupportedMarinaElement, element?.GetType().Name ?? "null"), nameof(elements));
            }
        }

        return new MarinaLayout { Name = name, Shoreline = shoreline, MarineTraffic = traffic, LandAreas = land, Piers = piers, Dividers = dividers, Berths = berths, MultiBerths = groups };
    }

    /// <summary>
    /// A copy of this layout whose berths carry their <see cref="Berth.ConnectedBerthIds"/>, worked out from where the berths
    /// and dividers stand, just as a visualizer showing the layout would work them out.
    /// </summary>
    /// <remarks>
    /// A visualizer keeps the connections of what it shows up to date by itself, and a marina file carries them; this is for
    /// a layout assembled in code, or read from a file written before berths had connections, without a visualizer.
    /// </remarks>
    public MarinaLayout WithBerthConnections()
    {
        var connections = BerthConnections.Compute(Berths, Dividers);
        return this with
        {
            Berths = Berths.Select(berth => berth is null ? berth! : berth with { ConnectedBerthIds = connections.TryGetValue(berth.Id, out var found) ? found : [] }).ToArray(),
        };
    }

    /// <summary>Plan-view bounds of all piers, berths, dividers and land. Returns a default 100 m square when empty.</summary>
    public (Vector2 Min, Vector2 Max) ComputeBounds() =>
        ComputeBounds(Piers.Select(d => d.Bounds)
            .Concat(Berths.Select(s => s.Bounds))
            .Concat(Dividers.Select(d => d.Bounds)), LandAreas);

    internal static (Vector2 Min, Vector2 Max) ComputeBounds(IEnumerable<OrientedRect> rects, IEnumerable<LandArea>? land = null) =>
        ComputeBounds(rects.Select(r => r.GetAxisAlignedBounds())
            .Concat((land ?? Enumerable.Empty<LandArea>()).Where(l => l?.Points is { Count: > 0 }).Select(l => l.GetAxisAlignedBounds())));

    private static (Vector2 Min, Vector2 Max) ComputeBounds(IEnumerable<(Vector2 Min, Vector2 Max)> boxes)
    {
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        var any = false;
        foreach (var (rMin, rMax) in boxes)
        {
            min = Vector2.Min(min, rMin);
            max = Vector2.Max(max, rMax);
            any = true;
        }

        return any ? (min, max) : (new Vector2(-50f), new Vector2(50f));
    }

    /// <summary>Returns a list of problems; empty when the layout is valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var pierIds = ValidatePiers(errors);

        if (Shoreline is not null) errors.AddRange(Shoreline.Validate());
        if (MarineTraffic is not null) errors.AddRange(MarineTraffic.Validate());

        var landIds = ValidateLandAreas(errors);
        var berthIds = ValidateBerths(errors, pierIds, landIds);
        ValidateDividers(errors, pierIds);
        ValidateMultiBerths(errors, berthIds);
        return errors;
    }

    /// <summary>Checks each pier and that no two share an id. Returns the ids seen.</summary>
    private HashSet<string> ValidatePiers(List<string> errors)
    {
        var pierIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pier in Piers)
        {
            if (pier is null)
            {
                errors.Add(Strings.ErrorNullPier);
                continue;
            }

            errors.AddRange(pier.Validate());
            if (!pierIds.Add(pier.Id)) errors.Add(Strings.Format(Strings.ErrorDuplicatePier, pier.Id));
        }

        return pierIds;
    }

    /// <summary>Checks each land area and that no two share an id. Returns the ids seen.</summary>
    private HashSet<string> ValidateLandAreas(List<string> errors)
    {
        var landIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var land in LandAreas)
        {
            if (land is null)
            {
                errors.Add(Strings.ErrorNullLandArea);
                continue;
            }

            errors.AddRange(land.Validate());
            if (!landIds.Add(land.Id)) errors.Add(Strings.Format(Strings.ErrorDuplicateLandArea, land.Id));
        }

        return landIds;
    }

    /// <summary>Checks each berth, that no two share an id, and that each names a pier or land area that exists. Returns the ids seen.</summary>
    private HashSet<string> ValidateBerths(List<string> errors, HashSet<string> pierIds, HashSet<string> landIds)
    {
        var berthIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var berth in Berths)
        {
            if (berth is null)
            {
                errors.Add(Strings.ErrorNullBerth);
                continue;
            }

            errors.AddRange(berth.Validate());
            if (!berthIds.Add(berth.Id)) errors.Add(Strings.Format(Strings.ErrorDuplicateBerth, berth.Id));
            if (berth.PierId is not null && !pierIds.Contains(berth.PierId)) errors.Add(Strings.Format(Strings.ErrorBerthUnknownPier, berth.Id, berth.PierId));
            if (berth.LandAreaId is not null && !landIds.Contains(berth.LandAreaId)) errors.Add(Strings.Format(Strings.ErrorBerthUnknownLandArea, berth.Id, berth.LandAreaId));
        }

        return berthIds;
    }

    /// <summary>Checks each divider, that no two share an id, and that each names a pier that exists.</summary>
    private void ValidateDividers(List<string> errors, HashSet<string> pierIds)
    {
        var dividerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var divider in Dividers)
        {
            if (divider is null)
            {
                errors.Add(Strings.ErrorNullDivider);
                continue;
            }

            errors.AddRange(divider.Validate());
            if (!dividerIds.Add(divider.Id)) errors.Add(Strings.Format(Strings.ErrorDuplicateDivider, divider.Id));
            if (divider.PierId is not null && !pierIds.Contains(divider.PierId)) errors.Add(Strings.Format(Strings.ErrorDividerUnknownPier, divider.Id, divider.PierId));
        }
    }

    /// <summary>
    /// Checks each multi-berth, that no two share an id or share a berth, that none takes a berth's id, and that its
    /// members exist and lie together.
    /// </summary>
    private void ValidateMultiBerths(List<string> errors, HashSet<string> berthIds)
    {
        var multiBerthIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var berthsInMultiBerths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var berthsById = Berths.Where(berth => berth is not null).GroupBy(berth => berth.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var multiBerth in MultiBerths)
        {
            if (multiBerth is null)
            {
                errors.Add(Strings.ErrorNullMultiBerth);
                continue;
            }

            errors.AddRange(multiBerth.Validate());
            if (!multiBerthIds.Add(multiBerth.Id)) errors.Add(Strings.Format(Strings.ErrorDuplicateMultiBerth, multiBerth.Id));
            if (berthIds.Contains(multiBerth.Id)) errors.Add(Strings.Format(Strings.ErrorMultiBerthIdIsBerthId, multiBerth.Id));

            var members = MultiBerthMembers(multiBerth, berthsById, berthsInMultiBerths, errors);
            errors.AddRange(multiBerth.ValidateMembers(members));
        }
    }

    /// <summary>
    /// The berths a multi-berth names that exist. An unknown one, or one another multi-berth already claimed, is reported;
    /// <paramref name="claimed"/> records which multi-berth each berth was last claimed by.
    /// </summary>
    private static List<Berth> MultiBerthMembers(
        MultiBerth multiBerth, Dictionary<string, Berth> berthsById, Dictionary<string, string> claimed, List<string> errors)
    {
        var members = new List<Berth>();
        foreach (var berthId in multiBerth.BerthIds)
        {
            if (string.IsNullOrWhiteSpace(berthId)) continue;
            if (berthsById.TryGetValue(berthId, out var member)) members.Add(member);
            else errors.Add(Strings.Format(Strings.ErrorMultiBerthUnknownBerth, multiBerth.Id, berthId));

            if (claimed.TryGetValue(berthId, out var other) && !string.Equals(other, multiBerth.Id, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(Strings.Format(Strings.ErrorBerthInTwoMultiBerths, berthId, other, multiBerth.Id));
            }

            claimed[berthId] = multiBerth.Id;
        }

        return members;
    }
}

/// <summary>Thrown when a layout, pier or berth definition is invalid.</summary>
public sealed class MarinaLayoutException : Exception
{
    /// <summary>Creates the exception from a list of validation problems.</summary>
    /// <param name="errors">Human-readable problems, e.g. "Duplicate berth id 'A-L01'."</param>
    public MarinaLayoutException(IReadOnlyList<string> errors)
        : base("Invalid marina definition: " + string.Join(" ", errors))
    {
        Errors = errors;
    }

    /// <summary>Creates the exception with no problems listed.</summary>
    public MarinaLayoutException()
        : this(Array.Empty<string>())
    {
    }

    /// <summary>Creates the exception with a message of its own.</summary>
    /// <param name="message">What is wrong with the definition.</param>
    public MarinaLayoutException(string message)
        : base(message)
    {
        Errors = Array.Empty<string>();
    }

    /// <summary>Creates the exception from a lower-level failure.</summary>
    /// <param name="message">What is wrong with the definition.</param>
    /// <param name="innerException">The failure underneath.</param>
    public MarinaLayoutException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = Array.Empty<string>();
    }

    /// <summary>Every problem found (the message joins them).</summary>
    public IReadOnlyList<string> Errors { get; }
}
