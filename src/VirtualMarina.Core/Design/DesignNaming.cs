using System.Globalization;
using System.Numerics;
using System.Text;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Design;

/// <summary>
/// How the designer comes up with names: ids for new piers and land areas, the next free berth name a
/// <see cref="BerthNamingScheme"/> offers, and the new names of a pier's berths when the pier or the scheme changes.
/// </summary>
internal sealed class DesignNaming
{
    private readonly MarinaVisualizer _marina;

    public DesignNaming(MarinaVisualizer marina) => _marina = marina;

    /// <summary>
    /// The next free pier id: A to Z, then AA, AB, ... as spreadsheet columns go, so a big marina carries on in letters
    /// rather than switching to another form part way through.
    /// </summary>
    public string NextPierId()
    {
        var used = new HashSet<string>(_marina.GetPiers().Select(pier => pier.Id), StringComparer.OrdinalIgnoreCase);
        var index = 1;
        while (used.Contains(Letters(index))) index++;
        return Letters(index);
    }

    /// <summary>The <paramref name="index"/>th letter name, counting from 1: A … Z, AA … AZ, BA …</summary>
    internal static string Letters(int index)
    {
        var text = new StringBuilder(4);
        for (var rest = index; rest > 0; rest = (rest - 1) / 26) text.Insert(0, (char)('A' + (rest - 1) % 26));
        return text.ToString();
    }

    /// <summary>The next free id of the form prefix + number, one past the highest number already used.</summary>
    public static string NextId(string prefix, IEnumerable<string> existing)
    {
        var used = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var number = used.Select(id => ParseNumber(id, prefix)).DefaultIfEmpty(0).Max();
        string id;
        do id = $"{prefix}{++number}"; while (used.Contains(id));
        return id;
    }

    /// <summary>The number after <paramref name="prefix"/> in an id, or 0 when the id is not of that form.</summary>
    public static int ParseNumber(string id, string prefix) =>
        id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && int.TryParse(id.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;

    /// <summary>The running number at the end of a generated berth name, or null when there is none.</summary>
    public static int? NumberIn(string berthId)
    {
        var digits = berthId.Length;
        while (digits > 0 && char.IsAsciiDigit(berthId[digits - 1])) digits--;
        return digits < berthId.Length && int.TryParse(berthId[digits..], out var number) ? number : null;
    }

    /// <summary>
    /// The pattern a pier's berths are named by now, read back out of the first one that carries a running number.
    /// Null when the pier has no berths, or none of them was named from a pattern.
    /// </summary>
    /// <param name="pier">The pier to look at.</param>
    /// <param name="scheme">The scheme in use, whose side letters are looked for.</param>
    public (string Pattern, int Digits)? InferBerthPattern(Pier pier, BerthNamingScheme scheme)
    {
        foreach (var berth in _marina.GetBerthsByPier(pier.Id))
        {
            if (scheme.Infer(pier, PierGeometry.SideOf(pier, berth.Center), berth.Id) is { } found) return found;
        }

        return null;
    }

    /// <summary>
    /// The renames that name a pier's berths again from a scheme, keeping the number each one already has, as
    /// (old name, new name). Nothing is changed.
    /// </summary>
    /// <param name="pier">The pier whose berths are being renamed.</param>
    /// <param name="namedFrom">
    /// The pier id the current names were built from, so berths named after something else are left alone. Null takes
    /// every berth with a running number.
    /// </param>
    /// <param name="scheme">The scheme to name them by.</param>
    /// <param name="wholePier">True to take every numbered berth whatever it is called now, as a pattern asked for by name means.</param>
    /// <remarks>
    /// Going through the scheme rather than swapping the prefix is what drops the side letter from a pier that berths on
    /// one side only: under pier K, <c>K-R07</c> becomes <c>T-07</c> when the pier becomes T. A berth whose name was not
    /// built from the pier id, or has no running number on the end, was named by hand and is left alone. So is one whose
    /// new name another berth keeps, or that another berth of the pier would get as well.
    /// </remarks>
    public List<(string From, string To)> PlanRenamesAfterPier(Pier pier, string? namedFrom, BerthNamingScheme scheme, bool wholePier)
    {
        var renames = new List<(string From, string To)>();
        foreach (var berth in _marina.GetBerthsByPier(pier.Id))
        {
            if (!wholePier && namedFrom is not null && !berth.Id.StartsWith(namedFrom, StringComparison.OrdinalIgnoreCase)) continue;
            if (NumberIn(berth.Id) is not { } number) continue;

            var name = scheme.Format(pier, PierGeometry.SideOf(pier, berth.Center), number);
            if (!string.Equals(name, berth.Id, StringComparison.Ordinal)) renames.Add((berth.Id, name));
        }

        // A name two berths would both get goes to neither: there is no telling which of them should have it.
        var wanted = renames.GroupBy(r => r.To, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        renames.RemoveAll(r => wanted.Contains(r.To));

        // Every berth takes its new name at once, so a name can pass from one berth to another; a name kept by a berth
        // that is not moving stays with it, and the berth that wanted it keeps its own.
        while (true)
        {
            var leaving = new HashSet<string>(renames.Select(r => r.From), StringComparer.OrdinalIgnoreCase);
            var blocked = renames.FindIndex(r => _marina.GetBerth(r.To) is { } holder && !leaving.Contains(holder.Id));
            if (blocked < 0) return renames;
            renames.RemoveAt(blocked);
        }
    }

    /// <summary>
    /// What naming a pier's berths by a scheme from scratch would call each of them — down one side and then the other when
    /// the pattern tells the sides apart, straight through when it does not — and which of the names clash.
    /// </summary>
    public BerthNamePlan PlanFromScratch(Pier pier, string pattern, BerthNamingScheme scheme)
    {
        var ordered = _marina.GetBerthsByPier(pier.Id)
            .Select(berth => (Berth: berth, Side: PierGeometry.SideOf(pier, berth.Center), Along: Vector2.Dot(berth.Center - pier.Start, pier.Direction)))
            .OrderBy(entry => entry.Side == PierSide.Left ? 0 : 1)
            .ThenBy(entry => entry.Along)
            .ToArray();

        // One run of numbers when the pattern gives both sides the same name, two when it tells them apart.
        var perSide = !string.Equals(
            scheme.Format(pier, PierSide.Left, scheme.StartNumber),
            scheme.Format(pier, PierSide.Right, scheme.StartNumber),
            StringComparison.Ordinal);

        var renames = new List<(string From, string To)>(ordered.Length);
        var left = 0;
        var right = 0;
        var running = 0;
        foreach (var entry in ordered)
        {
            int index;
            if (!perSide) index = running++;
            else if (entry.Side == PierSide.Left) index = left++;
            else index = right++;
            renames.Add((entry.Berth.Id, scheme.Format(pier, entry.Side, scheme.StartNumber + index * scheme.Increment)));
        }

        // A name is a clash when two of these berths want it, or when a berth that is not one of them already has it.
        var mine = new HashSet<string>(renames.Select(rename => rename.From), StringComparer.OrdinalIgnoreCase);
        var clashes = new List<string>();
        var clashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, to) in renames)
        {
            var taken = _marina.GetBerth(to) is { } other && !mine.Contains(other.Id);
            if ((!seen.Add(to) || taken) && clashSet.Add(to)) clashes.Add(to);
        }

        return new BerthNamePlan(pier.Id, pattern, renames, clashes);
    }
}

/// <summary>Hands out the next free name a <see cref="BerthNamingScheme"/> offers, skipping the ones already taken.</summary>
internal sealed class BerthNames
{
    private readonly HashSet<string> _used;
    private readonly int _increment;
    private readonly int _digits;
    private int _number;

    /// <param name="scheme">The naming scheme in force.</param>
    /// <param name="existingIds">Names already taken, which are skipped.</param>
    /// <param name="start">First number to offer; null counts from the scheme's own start.</param>
    /// <param name="step">Step between names; null uses the scheme's own increment.</param>
    /// <param name="digits">Digits a fallback number is padded to; null uses the scheme's own.</param>
    public BerthNames(BerthNamingScheme scheme, IEnumerable<string> existingIds, int? start = null, int? step = null, int? digits = null)
    {
        _used = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);
        var increment = step ?? scheme.Increment;
        _increment = increment == 0 ? 1 : increment;
        _number = start ?? scheme.StartNumber;
        _digits = Math.Clamp(digits ?? scheme.NumberDigits, 1, 9);
    }

    /// <summary>The next free name.</summary>
    /// <param name="format">Turns a running number into a name, by the scheme.</param>
    public string Next(Func<int, string> format)
    {
        // A pattern without {number} gives the same name every time; after a few tries number that name instead, so the
        // names still read as the scheme's ("A-L" gives "A-L-02", "A-L-03" ...).
        var first = format(_number);
        for (var attempt = 0; attempt < 10000; attempt++)
        {
            var name = attempt == 0 ? first : format(_number);
            _number += _increment;
            if (_used.Add(name)) return name;
            if (attempt >= 2 && string.Equals(name, first, StringComparison.Ordinal)) break;
        }

        var numberFormat = new string('0', _digits);
        string unique;
        var suffix = 2;
        do unique = $"{first}-{(suffix++).ToString(numberFormat, CultureInfo.InvariantCulture)}"; while (!_used.Add(unique));
        return unique;
    }
}

/// <summary>Which side of a pier a point lies on, worked out one way everywhere.</summary>
internal static class PierGeometry
{
    /// <summary>How far a point lies to the right of the pier's center line (negative: to the left), in meters.</summary>
    public static float Lateral(Pier pier, Vector2 point) => Vector2.Dot(point - pier.Center, pier.Right);

    /// <summary>The side of the pier <paramref name="point"/> lies on; a point on the center line counts as the right.</summary>
    public static PierSide SideOf(Pier pier, Vector2 point) => Lateral(pier, point) < 0f ? PierSide.Left : PierSide.Right;

    /// <summary>How far along the pier from its start a point lies, clamped to the pier.</summary>
    public static float Along(Pier pier, Vector2 point) => Math.Clamp(Vector2.Dot(point - pier.Start, pier.Direction), 0f, pier.Length);
}
