using System.Globalization;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Api;

/// <summary>Builds the tooltip the visualizer pre-fills before raising the selection events.</summary>
public static class DefaultPopupContent
{
    /// <summary>Maximum number of per-berth rows in a multi-selection tooltip.</summary>
    public const int MaxListedBerths = 6;

    /// <summary>
    /// The default single-berth tooltip: title = berth name, subtitle = pier (or land area for a land berth), rows for status, berth size and draft, boat name,
    /// type, size, owner, registration, expected arrival/return, multi-berth and read-only access. Accent = status color.
    /// </summary>
    /// <param name="berth">The berth.</param>
    /// <param name="pier">Its pier, if known.</param>
    /// <param name="multiBerth">Its multi-berth, if any.</param>
    /// <param name="colors">Color scheme for the accent.</param>
    /// <param name="landArea">The land area of a land berth, if known.</param>
    public static BerthTooltip ForBerth(Berth berth, Pier? pier, MultiBerth? multiBerth, StatusColorScheme colors, LandArea? landArea = null)
    {
        ArgumentNullException.ThrowIfNull(berth);
        ArgumentNullException.ThrowIfNull(colors);
        var c = CultureInfo.CurrentCulture;

        var tooltip = new BerthTooltip
        {
            Title = berth.DisplayName,
            Subtitle = pier?.Name ?? (berth.IsOnLand ? Strings.Format(Strings.TooltipOnLand, landArea?.DisplayName ?? berth.LandAreaId) : null),
            AccentColor = colors.Get(berth.Status),
        };

        tooltip.AddLine(Strings.TooltipStatus, berth.Status.GetDisplayName(), emphasize: true);
        var size = string.Format(c, Strings.TooltipSize, berth.Length, berth.Width);
        if (berth.MaxDraft is { } draft) size += string.Format(c, Strings.TooltipDraftSuffix, draft);
        tooltip.AddLine(Strings.TooltipBerthSize, size);

        if (berth.Boat is { } boat && berth.Status.CanHaveBoat()) AddBoatLines(tooltip, berth.Status, boat, c);

        if (multiBerth is not null)
        {
            var style = multiBerth.Style == MooringStyle.Alongside ? Strings.MooringAlongside : Strings.MooringBowIn;
            tooltip.AddLine(Strings.TooltipBoatLies, Strings.Format(Strings.TooltipAcrossBerths, style, string.Join(", ", multiBerth.BerthIds)));
        }

        if (berth.IsReadOnly) tooltip.AddLine(Strings.TooltipAccess, Strings.TooltipReadOnly);
        return tooltip;
    }

    /// <summary>The rows about the boat in a berth: name, type, size, owner, registration and when it is due.</summary>
    private static void AddBoatLines(BerthTooltip tooltip, BerthStatus status, Boat boat, CultureInfo c)
    {
        tooltip.AddLine(Strings.TooltipBoat, string.IsNullOrWhiteSpace(boat.Name) ? boat.Id : boat.Name, emphasize: true);
        tooltip.AddLine(Strings.TooltipBoatType, boat.TypeDisplayName);
        tooltip.AddLine(Strings.TooltipBoatSize, string.Format(c, Strings.TooltipSize, boat.LengthMeters, boat.BeamMeters));
        if (!string.IsNullOrWhiteSpace(boat.OwnerName)) tooltip.AddLine(Strings.TooltipOwner, boat.OwnerName);
        if (!string.IsNullOrWhiteSpace(boat.RegistrationNumber)) tooltip.AddLine(Strings.TooltipRegistration, boat.RegistrationNumber);
        if (boat.ExpectedArrival is { } eta && status != BerthStatus.Occupied)
        {
            tooltip.AddLine(status == BerthStatus.TemporarilyFree ? Strings.TooltipReturns : Strings.TooltipExpected, eta.ToLocalTime().ToString("g", c));
        }
    }

    /// <summary>The name of the pier or land area a berth belongs to, its id when it cannot be looked up, or null for neither.</summary>
    private static string? PlaceName(Berth berth, Func<string, Pier?> pierLookup, Func<string, LandArea?>? landLookup)
    {
        if (berth.PierId is { } pierId) return pierLookup(pierId)?.Name ?? pierId;
        if (berth.LandAreaId is { } landId) return landLookup?.Invoke(landId)?.DisplayName ?? landId;
        return null;
    }

    /// <summary>
    /// The default multi-selection tooltip: "N berths selected", pier and land area names, counts per status and read-only, then one row per
    /// berth (up to <see cref="MaxListedBerths"/>, with an "and N more" footer).
    /// </summary>
    /// <param name="berths">The selected berths.</param>
    /// <param name="pierLookup">Resolves pier names.</param>
    /// <param name="colors">Color scheme; the accent is set when all berths share a status.</param>
    /// <param name="landLookup">Resolves land area names for land berths; ids are shown when null.</param>
    public static BerthTooltip ForBerths(IReadOnlyList<Berth> berths, Func<string, Pier?> pierLookup, StatusColorScheme colors, Func<string, LandArea?>? landLookup = null)
    {
        ArgumentNullException.ThrowIfNull(berths);
        ArgumentNullException.ThrowIfNull(pierLookup);
        ArgumentNullException.ThrowIfNull(colors);

        var statuses = berths.Select(s => s.Status).Distinct().ToArray();
        var piers = berths
            .Select(s => PlaceName(s, pierLookup, landLookup))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var tooltip = new BerthTooltip
        {
            Title = Strings.Plural("TooltipBerthsSelected", berths.Count, berths.Count),
            Subtitle = string.Join(", ", piers),
            AccentColor = statuses.Length == 1 ? colors.Get(statuses[0]) : null,
        };

        foreach (var group in berths.GroupBy(s => s.Status).OrderBy(g => g.Key))
        {
            tooltip.AddLine(group.Key.GetDisplayName(), group.Count().ToString(CultureInfo.CurrentCulture), emphasize: true);
        }

        var readOnly = berths.Count(s => s.IsReadOnly);
        if (readOnly > 0) tooltip.AddLine(Strings.TooltipReadOnly, readOnly.ToString(CultureInfo.CurrentCulture));

        foreach (var berth in berths.Take(MaxListedBerths))
        {
            var value = berth.Status.GetDisplayName();
            if (berth.Boat is { } boat && berth.Status.CanHaveBoat()) value = Strings.Format(Strings.TooltipBerthAndBoat, value, string.IsNullOrWhiteSpace(boat.Name) ? boat.Id : boat.Name);
            tooltip.AddLine(berth.DisplayName, value);
        }

        if (berths.Count > MaxListedBerths) tooltip.Footer = Strings.Format(Strings.TooltipAndMore, berths.Count - MaxListedBerths);
        return tooltip;
    }
}
