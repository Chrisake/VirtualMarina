using System.Globalization;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Design;

/// <summary>One preview boat and the berth (or pair of berths) it goes in.</summary>
/// <param name="BerthIds">The berths it takes: one, or two side by side.</param>
/// <param name="Boat">The boat, sized to the space.</param>
/// <param name="Style">How it lies; only meaningful across two berths.</param>
public readonly record struct PreviewMooring(IReadOnlyList<string> BerthIds, Boat Boat, MooringStyle Style);

/// <summary>
/// Picks the boats that fill a marina to a given share of its berths, for judging the look of a design against.
/// </summary>
/// <remarks>
/// Two things make the result worth looking at rather than merely present. Each boat is chosen to suit the berth it
/// goes in, so a twelve-metre berth does not end up with a jet ski rattling around in it; and a boat too wide for
/// one berth, a catamaran above all, is moored across two berths side by side rather than left out.
/// <para>
/// It works entirely from the Core domain model, so both the WinForms and the Blazor designers fill their preview
/// fleets from this one copy rather than each carrying its own.
/// </para>
/// </remarks>
public static class PreviewFleet
{
    /// <summary>How long a boat has to be, against the berth, to look at home in it rather than lost in it.</summary>
    private const float Snug = 0.55f;

    /// <summary>How much of two berths' combined width a boat across them may use, leaving fenders room.</summary>
    private const float PairUse = 0.92f;

    /// <summary>How often a berth with a free neighbour is offered to a wide boat. Most of them are not taken.</summary>
    private const int PairChancePercent = 25;

    /// <summary>Room for the odd centimetre, so a boat authored at exactly the berth's size still counts as fitting.</summary>
    private const float Slack = 0.05f;

    /// <summary>The smallest thing there is, for a berth too small to hold anything at all.</summary>
    private static readonly BoatType SmallestType =
        BoatTypeCatalog.All.MinBy(type => BoatTypeCatalog.GetNominalDimensions(type).Length);

    /// <summary>
    /// Chooses boats for <paramref name="wanted"/> of the berths, at random, each suited to where it goes.
    /// </summary>
    /// <param name="berths">Every berth in the marina, ashore ones included.</param>
    /// <param name="wanted">How many berths should end up full. A boat across two berths fills both.</param>
    /// <param name="random">Source of the randomness, so a caller can repeat a fleet.</param>
    public static IReadOnlyList<PreviewMooring> Plan(IReadOnlyList<Berth> berths, int wanted, Random random)
    {
        ArgumentNullException.ThrowIfNull(berths);
        ArgumentNullException.ThrowIfNull(random);
        if (wanted <= 0 || berths.Count == 0) return Array.Empty<PreviewMooring>();

        var neighbours = Neighbours(berths);
        var order = berths.ToArray();
        Shuffle(order, random);

        var plan = new List<PreviewMooring>(wanted);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var left = Math.Min(wanted, berths.Count);

        foreach (var berth in order)
        {
            if (left <= 0) break;
            if (taken.Contains(berth.Id)) continue;

            // A pair first, while there is room in the count for both halves of it.
            if (left >= 2 && random.Next(100) < PairChancePercent
                && neighbours.TryGetValue(berth.Id, out var mates)
                && TryPair(berth, mates, taken, random, plan.Count, out var pair))
            {
                plan.Add(pair);
                foreach (var id in pair.BerthIds) taken.Add(id);
                left -= 2;
                continue;
            }

            plan.Add(new PreviewMooring(new[] { berth.Id }, Single(berth, random, plan.Count), MooringStyle.Alongside));
            taken.Add(berth.Id);
            left--;
        }

        return plan;
    }

    /// <summary>A boat for one berth, chosen at random among the types that suit its size.</summary>
    private static Boat Single(Berth berth, Random random, int index)
    {
        var fits = BoatTypeCatalog.All
            .Where(type => Fits(BoatTypeCatalog.GetNominalDimensions(type), berth.Length, berth.Width))
            .ToArray();

        // Of those, the ones that actually use the berth. A jet ski fits a yacht berth and looks abandoned in it.
        var snug = fits.Where(type => BoatTypeCatalog.GetNominalDimensions(type).Length >= berth.Length * Snug).ToArray();

        var type = snug.Length > 0 ? snug[random.Next(snug.Length)]
            : fits.Length > 0 ? fits.MaxBy(candidate => BoatTypeCatalog.GetNominalDimensions(candidate).Length)
            : SmallestType;

        return Vessel(type, berth.Length, berth.Width, random, index);
    }

    /// <summary>
    /// Looks for a boat too wide for one berth but at home across two: a catamaran in the pair of berths beside it.
    /// </summary>
    private static bool TryPair(Berth berth, IReadOnlyList<Berth> mates, HashSet<string> taken, Random random, int index, out PreviewMooring pair)
    {
        var order = mates.ToArray();
        Shuffle(order, random);

        foreach (var mate in order)
        {
            if (taken.Contains(mate.Id)) continue;

            var width = berth.Width + mate.Width;
            var length = MathF.Min(berth.Length, mate.Length);
            var alone = MathF.Max(berth.Width, mate.Width);

            // Only a boat that needs both berths: putting a monohull across two of them wastes one.
            var wide = BoatTypeCatalog.All.Where(type =>
            {
                var size = BoatTypeCatalog.GetNominalDimensions(type);
                return size.Beam > alone + Slack
                    && size.Beam <= width * PairUse
                    && size.Length <= length + Slack
                    && size.Length >= length * Snug;
            }).ToArray();

            if (wide.Length == 0) continue;

            pair = new PreviewMooring(
                new[] { berth.Id, mate.Id },
                Vessel(wide[random.Next(wide.Length)], length, width * PairUse, random, index),
                MooringStyle.BowIn);
            return true;
        }

        pair = default;
        return false;
    }

    /// <summary>
    /// A boat of a type, a little shorter than its nominal length so a row of them is not stamped out, and never
    /// larger than the space it goes in.
    /// </summary>
    /// <remarks>
    /// Only the length varies. The beam is what decided which berth the boat could go in, and a catamaran shrunk a
    /// tenth across the hulls is a catamaran that would have fitted one berth after all — two berths taken up to
    /// draw a boat that did not need them.
    /// </remarks>
    private static Boat Vessel(BoatType type, float length, float width, Random random, int index)
    {
        var nominal = BoatTypeCatalog.GetNominalDimensions(type);
        var shorter = nominal.Length * (0.9f + ((float)random.NextDouble() * 0.1f));
        var name = string.Create(CultureInfo.InvariantCulture, $"{BoatTypeCatalog.GetDisplayName(type)} {index + 1}");

        return new Boat(string.Create(CultureInfo.InvariantCulture, $"PREVIEW-{index + 1}"), name, type)
        {
            LengthMeters = MathF.Min(shorter, length),
            BeamMeters = MathF.Min(nominal.Beam, width),
        };
    }

    /// <summary>The berths each berth is connected to, by berth id — the pairs a wide boat can moor across.</summary>
    /// <remarks>
    /// Taken from <see cref="Berth.ConnectedBerthIds"/>, which the visualizer works out from the geometry: berths facing
    /// the same way, level with one another and side by side, with no divider or finger pier between them. So the preview
    /// never puts a boat across a divider, nor across the two sides of a double pier.
    /// </remarks>
    private static Dictionary<string, List<Berth>> Neighbours(IReadOnlyList<Berth> berths)
    {
        var byId = new Dictionary<string, Berth>(StringComparer.OrdinalIgnoreCase);
        foreach (var berth in berths) byId.TryAdd(berth.Id, berth);

        var found = new Dictionary<string, List<Berth>>(StringComparer.OrdinalIgnoreCase);
        foreach (var berth in berths)
        {
            var mates = berth.ConnectedBerthIds.Select(id => byId.TryGetValue(id, out var mate) ? mate : null).OfType<Berth>().ToList();
            if (mates.Count > 0) found.TryAdd(berth.Id, mates);
        }

        return found;
    }

    private static bool Fits(BoatDimensions size, float length, float width) =>
        size.Length <= length + Slack && size.Beam <= width + Slack;

    private static void Shuffle<T>(T[] items, Random random)
    {
        for (var i = items.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
