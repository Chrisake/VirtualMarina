using System.Globalization;
using System.Text;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Design;

/// <summary>
/// How the designer names the berths it draws: the pattern, the first number and how far it counts on.
/// Set it on <see cref="MarinaDesigner.BerthNaming"/>.
/// </summary>
/// <remarks>
/// <para>
/// The pattern is plain text with tokens in braces, written into every new berth's name (which is also its id, so a
/// pattern with no number in it only ever names one berth — the rest fall back to a numbered form):
/// </para>
/// <list type="table">
///   <item><term><c>{pier}</c></term><description>Id of the pier the berth belongs to, or of the land area for a berth ashore</description></item>
///   <item><term><c>{pierName}</c></term><description>The pier's display name</description></item>
///   <item><term><c>{side}</c></term><description><see cref="LeftSide"/> or <see cref="RightSide"/>; empty for a berth ashore</description></item>
///   <item><term><c>{number}</c></term><description>The running number, padded to <see cref="NumberDigits"/> digits</description></item>
/// </list>
/// <para>
/// Numbering starts at <see cref="StartNumber"/> and goes up by <see cref="Increment"/>. Names already taken are
/// skipped, so a second row on the same side carries on after the first instead of clashing with it.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // A-L01, A-L02, ... (the default)
/// designer.BerthNaming = BerthNamingScheme.Default;
///
/// // Berths 101, 103, 105 ... on both sides of every pier
/// designer.BerthNaming = new BerthNamingScheme { Pattern = "{number}", StartNumber = 101, Increment = 2, NumberDigits = 3 };
/// </code>
/// </example>
public sealed record BerthNamingScheme
{
    /// <summary>The default scheme: <c>{pier}-{side}{number}</c> from 1 upward, two digits, e.g. <c>A-L01</c>.</summary>
    public static readonly BerthNamingScheme Default = new();

    /// <summary>Pattern for a berth on a pier. Default <c>{pier}-{side}{number}</c>.</summary>
    public string Pattern { get; init; } = "{pier}-{side}{number}";

    /// <summary>Pattern for a slot ashore; null (the default) uses <see cref="Pattern"/>, where <c>{side}</c> is empty.</summary>
    /// <example><code>new BerthNamingScheme { LandPattern = "YARD-{number}" }</code></example>
    public string? LandPattern { get; init; }

    /// <summary>The first number offered to a slot ashore; null (the default) counts from <see cref="StartNumber"/>.</summary>
    public int? LandStartNumber { get; init; }

    /// <summary>Step between slots ashore; null (the default) uses <see cref="Increment"/>.</summary>
    public int? LandIncrement { get; init; }

    /// <summary>Digits a slot ashore is padded to; null (the default) uses <see cref="NumberDigits"/>.</summary>
    public int? LandNumberDigits { get; init; }

    /// <summary>The first number offered. Default 1.</summary>
    public int StartNumber { get; init; } = 1;

    /// <summary>Step from one berth to the next. Default 1.</summary>
    public int Increment { get; init; } = 1;

    /// <summary>Digits the number is padded to with leading zeros; 1 writes it as it is. Default 2, so 1 becomes <c>01</c>.</summary>
    public int NumberDigits { get; init; } = 2;

    /// <summary>What <c>{side}</c> becomes on the left-hand side of a pier. Default <c>L</c>.</summary>
    public string LeftSide { get; init; } = "L";

    /// <summary>What <c>{side}</c> becomes on the right-hand side of a pier. Default <c>R</c>.</summary>
    public string RightSide { get; init; } = "R";

    /// <summary>The name this scheme gives berth number <paramref name="number"/> on one side of a pier.</summary>
    /// <param name="pier">The pier.</param>
    /// <param name="side">Side of the pier.</param>
    /// <param name="number">The running number.</param>
    public string Format(Pier pier, PierSide side, int number)
    {
        ArgumentNullException.ThrowIfNull(pier);
        return Format(Pattern, pier.Id, pier.Name, SideToken(pier, side), number);
    }

    /// <summary>
    /// What <c>{side}</c> becomes for a berth on this pier: nothing at all when the pier takes boats on one side
    /// only, because there is no other side to tell it apart from.
    /// </summary>
    private string SideToken(Pier pier, PierSide side) =>
        pier.BerthingSides is PierSides.Left or PierSides.Right ? string.Empty
            : side == PierSide.Left ? LeftSide
            : RightSide;

    /// <summary>The name this scheme gives berth number <paramref name="number"/> on a land area.</summary>
    /// <param name="landArea">The land area the boat stands on.</param>
    /// <param name="number">The running number.</param>
    public string Format(LandArea landArea, int number)
    {
        ArgumentNullException.ThrowIfNull(landArea);
        return Format(LandPattern ?? Pattern, landArea.Id, landArea.DisplayName, string.Empty, number, LandNumberDigits ?? NumberDigits);
    }

    /// <summary>
    /// Reads a pattern back out of a berth's name: what pattern, applied to this pier and side, would have produced
    /// it. Null when the name has no running number on the end, which means it was written by hand.
    /// </summary>
    /// <remarks>
    /// Worked out from the name rather than simply handing back <see cref="Pattern"/>, because the berths on a pier
    /// may well have been named under an older scheme, and the point of showing it is to say what they look like
    /// now. The number of digits comes back too, so putting the pattern straight back leaves the names untouched.
    /// </remarks>
    /// <remarks>
    /// <see cref="LeftSide"/> and <see cref="RightSide"/> are still read from this scheme, since there is nothing in
    /// a name to say which letter in it stood for the side. A berth named under a scheme whose side letters differed
    /// comes back with that letter as plain text, which still reproduces the name it has.
    /// </remarks>
    /// <param name="pier">The pier the berth is on.</param>
    /// <param name="side">Which side of it the berth lies on.</param>
    /// <param name="berthId">The berth's name.</param>
    internal (string Pattern, int Digits)? Infer(Pier pier, PierSide side, string berthId)
    {
        ArgumentNullException.ThrowIfNull(pier);
        if (string.IsNullOrEmpty(berthId)) return null;

        // The running number is the digits on the end; without one there is nothing to count on from.
        var start = berthId.Length;
        while (start > 0 && char.IsAsciiDigit(berthId[start - 1])) start--;
        if (start == berthId.Length) return null;

        var head = berthId[..start];
        var digits = berthId.Length - start;

        // The side token sits next to the number when it is there at all — "A-L01", "QUAY.P.001" — so it is looked
        // for only in the last couple of characters. Searching the whole name would turn the L of a berth called
        // PLAZA-01 into a side token.
        var token = SideToken(pier, side);
        if (token.Length > 0)
        {
            var from = Math.Max(0, head.Length - token.Length - 2);
            var at = head.LastIndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (at >= from) head = head[..at] + "{side}" + head[(at + token.Length)..];
        }

        // The pier id is the front of the name, and only the front: a later match is part of the name proper.
        if (pier.Id.Length > 0 && head.StartsWith(pier.Id, StringComparison.OrdinalIgnoreCase))
        {
            head = "{pier}" + head[pier.Id.Length..];
        }

        return (head + "{number}", digits);
    }

    /// <summary>Where the numbering of the slots ashore starts, and the step between them.</summary>
    internal (int Start, int Step) AshoreNumbering => (LandStartNumber ?? StartNumber, LandIncrement ?? Increment);

    /// <summary>Problems that would stop the scheme from naming anything, empty when it is sound.</summary>
    public IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Pattern)) yield return "The berth naming pattern must not be empty.";
        if (LandPattern is not null && string.IsNullOrWhiteSpace(LandPattern)) yield return "The land berth naming pattern must not be empty.";
        if (Increment == 0) yield return "The berth numbering increment must not be 0.";
        if (NumberDigits is < 1 or > 9) yield return "The berth numbering must be padded to between 1 and 9 digits.";
        if (LandIncrement == 0) yield return "The numbering increment of the slots ashore must not be 0.";
        if (LandNumberDigits is < 1 or > 9) yield return "The slots ashore must be padded to between 1 and 9 digits.";
    }

    private string Format(string pattern, string pierId, string pierName, string side, int number) =>
        Format(pattern, pierId, pierName, side, number, NumberDigits);

    private static string Format(string pattern, string pierId, string pierName, string side, int number, int digits)
    {
        var numberFormat = new string('0', Math.Clamp(digits, 1, 9));
        var text = new StringBuilder(pattern.Length + 8);
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] != '{')
            {
                text.Append(pattern[i]);
                continue;
            }

            var close = pattern.IndexOf('}', i + 1);
            if (close < 0)
            {
                text.Append(pattern.AsSpan(i));
                break;
            }

            var token = pattern.AsSpan(i + 1, close - i - 1);
            // An unknown token is written out as it stands, so a stray brace never swallows part of the name.
            if (token.Equals("pier", StringComparison.OrdinalIgnoreCase)) text.Append(pierId);
            else if (token.Equals("pierName", StringComparison.OrdinalIgnoreCase)) text.Append(pierName);
            else if (token.Equals("side", StringComparison.OrdinalIgnoreCase)) text.Append(side);
            else if (token.Equals("number", StringComparison.OrdinalIgnoreCase)) text.Append(number.ToString(numberFormat, CultureInfo.InvariantCulture));
            else text.Append(pattern.AsSpan(i, close - i + 1));

            i = close;
        }

        var name = text.ToString().Trim();
        return name.Length == 0 ? number.ToString(numberFormat, CultureInfo.InvariantCulture) : name;
    }
}
