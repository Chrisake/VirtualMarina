using System.Numerics;

namespace VirtualMarina.Core.Domain;

/// <summary>
/// Complete description of a marina: docks, slips (flat list, linked by <see cref="Slip.DockId"/>),
/// dividers, multi-slip berths, surrounding land and land slips (linked by <see cref="Slip.LandAreaId"/>). Pass to <c>MarinaVisualizer.InitializeLayout</c>.
/// </summary>
public sealed record MarinaLayout
{
    /// <summary>Marina name (<c>IMarinaVisualizer.MarinaName</c>).</summary>
    public string Name { get; init; } = "Marina";

    /// <summary>Docks. Ids must be unique.</summary>
    public IReadOnlyList<Dock> Docks { get; init; } = Array.Empty<Dock>();

    /// <summary>
    /// Slips, each referencing a dock in <see cref="Docks"/> or, for land slips, a land area in <see cref="LandAreas"/>. Ids must be unique.
    /// </summary>
    public IReadOnlyList<Slip> Slips { get; init; } = Array.Empty<Slip>();

    /// <summary>Finger piers, pile rows and booms between slips.</summary>
    public IReadOnlyList<Divider> Dividers { get; init; } = Array.Empty<Divider>();

    /// <summary>Boats spanning several slips. Member slips take the berth's status and boat when the layout is loaded.</summary>
    public IReadOnlyList<MultiSlipBerth> MultiSlipBerths { get; init; } = Array.Empty<MultiSlipBerth>();

    /// <summary>Quays, breakwaters and lawns drawn around the water. Ids must be unique.</summary>
    public IReadOnlyList<LandArea> LandAreas { get; init; } = Array.Empty<LandArea>();

    /// <summary>A layout with nothing in it.</summary>
    public static MarinaLayout Empty { get; } = new();

    /// <summary>Plan-view bounds of all docks, slips, dividers and land. Returns a default 100 m square when empty.</summary>
    public (Vector2 Min, Vector2 Max) ComputeBounds() =>
        ComputeBounds(Docks.Select(d => d.Bounds)
            .Concat(Slips.Select(s => s.Bounds))
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
        var dockIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dock in Docks)
        {
            if (dock is null)
            {
                errors.Add("Docks contains a null entry.");
                continue;
            }

            errors.AddRange(dock.Validate());
            if (!dockIds.Add(dock.Id)) errors.Add($"Duplicate dock id '{dock.Id}'.");
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

        var slipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slip in Slips)
        {
            if (slip is null)
            {
                errors.Add("Slips contains a null entry.");
                continue;
            }

            errors.AddRange(slip.Validate());
            if (!slipIds.Add(slip.Id)) errors.Add($"Duplicate slip id '{slip.Id}'.");
            if (slip.DockId is not null && !dockIds.Contains(slip.DockId)) errors.Add($"Slip '{slip.Id}' references unknown dock '{slip.DockId}'.");
            if (slip.LandAreaId is not null && !landIds.Contains(slip.LandAreaId)) errors.Add($"Slip '{slip.Id}' references unknown land area '{slip.LandAreaId}'.");
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
            if (divider.DockId is not null && !dockIds.Contains(divider.DockId)) errors.Add($"Divider '{divider.Id}' references unknown dock '{divider.DockId}'.");
        }

        var berthIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var slipsInBerths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var berth in MultiSlipBerths)
        {
            if (berth is null)
            {
                errors.Add("MultiSlipBerths contains a null entry.");
                continue;
            }

            errors.AddRange(berth.Validate());
            if (!berthIds.Add(berth.Id)) errors.Add($"Duplicate berth id '{berth.Id}'.");
            foreach (var slipId in berth.SlipIds ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(slipId)) continue;
                if (!slipIds.Contains(slipId)) errors.Add($"Berth '{berth.Id}' references unknown slip '{slipId}'.");
                if (slipsInBerths.TryGetValue(slipId, out var other) && !string.Equals(other, berth.Id, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Slip '{slipId}' belongs to both berth '{other}' and berth '{berth.Id}'.");
                }

                slipsInBerths[slipId] = berth.Id;
            }
        }

        return errors;
    }
}

/// <summary>Thrown when a layout, dock or slip definition is invalid.</summary>
public sealed class MarinaLayoutException : Exception
{
    /// <summary>Creates the exception from a list of validation problems.</summary>
    /// <param name="errors">Human-readable problems, e.g. "Duplicate slip id 'A-L01'."</param>
    public MarinaLayoutException(IReadOnlyList<string> errors)
        : base("Invalid marina definition: " + string.Join(" ", errors))
    {
        Errors = errors;
    }

    /// <summary>Every problem found (the message joins them).</summary>
    public IReadOnlyList<string> Errors { get; }
}
