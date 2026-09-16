using System.Globalization;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Api;

/// <summary>Builds the tooltip the visualizer pre-fills before raising the selection events.</summary>
public static class DefaultPopupContent
{
    /// <summary>Maximum number of per-slip rows in a multi-selection tooltip.</summary>
    public const int MaxListedSlips = 6;

    /// <summary>
    /// The default single-slip tooltip: title = slip name, subtitle = dock, rows for status, slip size and draft, boat name,
    /// type, size, owner, registration, expected arrival/return, multi-slip berth and read-only access. Accent = status color.
    /// </summary>
    /// <param name="slip">The slip.</param>
    /// <param name="dock">Its dock, if known.</param>
    /// <param name="berth">Its multi-slip berth, if any.</param>
    /// <param name="colors">Color scheme for the accent.</param>
    public static SlipTooltip ForSlip(Slip slip, Dock? dock, MultiSlipBerth? berth, StatusColorScheme colors)
    {
        ArgumentNullException.ThrowIfNull(slip);
        ArgumentNullException.ThrowIfNull(colors);
        var c = CultureInfo.CurrentCulture;

        var tooltip = new SlipTooltip
        {
            Title = slip.DisplayName,
            Subtitle = dock?.Name,
            AccentColor = colors.Get(slip.Status),
        };

        tooltip.AddLine("Status", slip.Status.GetDisplayName(), emphasize: true);
        var size = string.Format(c, "{0:0.0} × {1:0.0} m", slip.Length, slip.Width);
        if (slip.MaxDraft is { } draft) size += string.Format(c, ", draft {0:0.0} m", draft);
        tooltip.AddLine("Slip size", size);

        if (slip.Boat is { } boat && slip.Status.CanHaveBoat())
        {
            tooltip.AddLine("Boat", string.IsNullOrWhiteSpace(boat.Name) ? boat.Id : boat.Name, emphasize: true);
            tooltip.AddLine("Boat type", boat.TypeDisplayName);
            tooltip.AddLine("Boat size", string.Format(c, "{0:0.0} × {1:0.0} m", boat.LengthMeters, boat.BeamMeters));
            if (!string.IsNullOrWhiteSpace(boat.OwnerName)) tooltip.AddLine("Owner", boat.OwnerName);
            if (!string.IsNullOrWhiteSpace(boat.RegistrationNumber)) tooltip.AddLine("Registration", boat.RegistrationNumber);
            if (boat.ExpectedArrival is { } eta && slip.Status != SlipStatus.Occupied)
            {
                tooltip.AddLine(slip.Status == SlipStatus.TemporarilyFree ? "Returns" : "Expected", eta.ToLocalTime().ToString("g", c));
            }
        }

        if (berth is not null)
        {
            var style = berth.Style == MooringStyle.Alongside ? "Alongside" : "Bow-in";
            tooltip.AddLine("Berth", $"{style} across {string.Join(", ", berth.SlipIds)}");
        }

        if (slip.IsReadOnly) tooltip.AddLine("Access", "Read-only");
        return tooltip;
    }

    /// <summary>
    /// The default multi-selection tooltip: "N slips selected", dock names, counts per status and read-only, then one row per
    /// slip (up to <see cref="MaxListedSlips"/>, with an "and N more" footer).
    /// </summary>
    /// <param name="slips">The selected slips.</param>
    /// <param name="dockLookup">Resolves dock names.</param>
    /// <param name="colors">Color scheme; the accent is set when all slips share a status.</param>
    public static SlipTooltip ForSlips(IReadOnlyList<Slip> slips, Func<string, Dock?> dockLookup, StatusColorScheme colors)
    {
        ArgumentNullException.ThrowIfNull(slips);
        ArgumentNullException.ThrowIfNull(dockLookup);
        ArgumentNullException.ThrowIfNull(colors);

        var statuses = slips.Select(s => s.Status).Distinct().ToArray();
        var docks = slips.Select(s => dockLookup(s.DockId)?.Name ?? s.DockId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var tooltip = new SlipTooltip
        {
            Title = $"{slips.Count} slips selected",
            Subtitle = string.Join(", ", docks),
            AccentColor = statuses.Length == 1 ? colors.Get(statuses[0]) : null,
        };

        foreach (var group in slips.GroupBy(s => s.Status).OrderBy(g => g.Key))
        {
            tooltip.AddLine(group.Key.GetDisplayName(), group.Count().ToString(CultureInfo.CurrentCulture), emphasize: true);
        }

        var readOnly = slips.Count(s => s.IsReadOnly);
        if (readOnly > 0) tooltip.AddLine("Read-only", readOnly.ToString(CultureInfo.CurrentCulture));

        foreach (var slip in slips.Take(MaxListedSlips))
        {
            var value = slip.Status.GetDisplayName();
            if (slip.Boat is { } boat && slip.Status.CanHaveBoat()) value += " · " + (string.IsNullOrWhiteSpace(boat.Name) ? boat.Id : boat.Name);
            tooltip.AddLine(slip.DisplayName, value);
        }

        if (slips.Count > MaxListedSlips) tooltip.Footer = $"and {slips.Count - MaxListedSlips} more";
        return tooltip;
    }
}
