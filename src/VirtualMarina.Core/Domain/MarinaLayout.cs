using System.Numerics;

namespace VirtualMarina.Core.Domain;

/// <summary>
/// Complete description of a marina: piers, berths (flat list, linked by <see cref="Berth.PierId"/>),
/// dividers, multi-berths, surrounding land and land berths (linked by <see cref="Berth.LandAreaId"/>). Pass to <c>MarinaVisualizer.InitializeLayout</c>.
/// </summary>
public sealed record MarinaLayout
{
    /// <summary>Marina name (<c>IMarinaVisualizer.MarinaName</c>).</summary>
    public string Name { get; init; } = "Marina";

    /// <summary>Piers. Ids must be unique.</summary>
    public IReadOnlyList<Pier> Piers { get; init; } = Array.Empty<Pier>();

    /// <summary>
    /// Berths, each referencing a pier in <see cref="Piers"/> or, for land berths, a land area in <see cref="LandAreas"/>. Ids must be unique.
    /// </summary>
    public IReadOnlyList<Berth> Berths { get; init; } = Array.Empty<Berth>();

    /// <summary>Finger piers, pile rows and booms between berths.</summary>
    public IReadOnlyList<Divider> Dividers { get; init; } = Array.Empty<Divider>();

    /// <summary>Boats spanning several berths. Member berths take the berth's status and boat when the layout is loaded.</summary>
    public IReadOnlyList<MultiBerth> MultiBerths { get; init; } = Array.Empty<MultiBerth>();

    /// <summary>Quays, breakwaters and lawns drawn around the water. Ids must be unique.</summary>
    public IReadOnlyList<LandArea> LandAreas { get; init; } = Array.Empty<LandArea>();

    /// <summary>A layout with nothing in it.</summary>
    public static MarinaLayout Empty { get; } = new();

    /// <summary>
    /// Every element as one flat array, in dependency order: land areas, piers, dividers, berths, then multi-berths.
    /// Each entry is the immutable record itself (<see cref="LandArea"/>, <see cref="Pier"/>, <see cref="Divider"/>, <see cref="Berth"/>,
    /// <see cref="MultiBerth"/>), so host code can pattern-match on it.
    /// </summary>
    public object[] ToObjects() =>
        LandAreas.Cast<object>()
            .Concat(Piers)
            .Concat(Dividers)
            .Concat(Berths)
            .Concat(MultiBerths)
            .ToArray();

    /// <summary>Builds a layout from elements in any order (the inverse of <see cref="ToObjects"/>). The result is not validated.</summary>
    /// <param name="elements">Land areas, piers, dividers, berths and multi-berths.</param>
    /// <param name="name">Marina name.</param>
    /// <exception cref="ArgumentException">An element is null or of another type.</exception>
    public static MarinaLayout FromObjects(IEnumerable<object> elements, string name = "Marina")
    {
        ArgumentNullException.ThrowIfNull(elements);
        var land = new List<LandArea>();
        var piers = new List<Pier>();
        var dividers = new List<Divider>();
        var berths = new List<Berth>();
        var groups = new List<MultiBerth>();
        foreach (var element in elements)
        {
            switch (element)
            {
                case LandArea l: land.Add(l); break;
                case Pier d: piers.Add(d); break;
                case Divider v: dividers.Add(v); break;
                case Berth s: berths.Add(s); break;
                case MultiBerth b: groups.Add(b); break;
                default: throw new ArgumentException($"Unsupported marina element: {element?.GetType().Name ?? "null"}.", nameof(elements));
            }
        }

        return new MarinaLayout { Name = name, LandAreas = land, Piers = piers, Dividers = dividers, Berths = berths, MultiBerths = groups };
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
        var pierIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pier in Piers)
        {
            if (pier is null)
            {
                errors.Add("Piers contains a null entry.");
                continue;
            }

            errors.AddRange(pier.Validate());
            if (!pierIds.Add(pier.Id)) errors.Add($"Duplicate pier id '{pier.Id}'.");
        }

        var landIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var land in LandAreas)
        {
            if (land is null)
            {
                errors.Add("LandAreas contains a null entry.");
                continue;
            }

            errors.AddRange(land.Validate());
            if (!landIds.Add(land.Id)) errors.Add($"Duplicate land area id '{land.Id}'.");
        }

        var berthIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var berth in Berths)
        {
            if (berth is null)
            {
                errors.Add("Berths contains a null entry.");
                continue;
            }

            errors.AddRange(berth.Validate());
            if (!berthIds.Add(berth.Id)) errors.Add($"Duplicate berth id '{berth.Id}'.");
            if (berth.PierId is not null && !pierIds.Contains(berth.PierId)) errors.Add($"Berth '{berth.Id}' references unknown pier '{berth.PierId}'.");
            if (berth.LandAreaId is not null && !landIds.Contains(berth.LandAreaId)) errors.Add($"Berth '{berth.Id}' references unknown land area '{berth.LandAreaId}'.");
        }

        var dividerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var divider in Dividers)
        {
            if (divider is null)
            {
                errors.Add("Dividers contains a null entry.");
                continue;
            }

            errors.AddRange(divider.Validate());
            if (!dividerIds.Add(divider.Id)) errors.Add($"Duplicate divider id '{divider.Id}'.");
            if (divider.PierId is not null && !pierIds.Contains(divider.PierId)) errors.Add($"Divider '{divider.Id}' references unknown pier '{divider.PierId}'.");
        }

        var multiBerthIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var berthsInMultiBerths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var berth in MultiBerths)
        {
            if (berth is null)
            {
                errors.Add("MultiBerths contains a null entry.");
                continue;
            }

            errors.AddRange(berth.Validate());
            if (!multiBerthIds.Add(berth.Id)) errors.Add($"Duplicate berth id '{berth.Id}'.");
            foreach (var berthId in berth.BerthIds ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(berthId)) continue;
                if (!berthIds.Contains(berthId)) errors.Add($"Berth '{berth.Id}' references unknown berth '{berthId}'.");
                if (berthsInMultiBerths.TryGetValue(berthId, out var other) && !string.Equals(other, berth.Id, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Berth '{berthId}' belongs to both berth '{other}' and berth '{berth.Id}'.");
                }

                berthsInMultiBerths[berthId] = berth.Id;
            }
        }

        return errors;
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

    /// <summary>Every problem found (the message joins them).</summary>
    public IReadOnlyList<string> Errors { get; }
}
