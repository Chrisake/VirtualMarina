using System.Numerics;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Domain;

/// <summary>
/// Fluent helper for composing a <see cref="MarinaLayout"/> with berths auto-positioned along piers.
/// </summary>
/// <example>
/// <code>
/// var layout = new MarinaLayoutBuilder("Harbor")
///     .AddPier("A", "Pier A", new Vector2(0, 0), headingDegrees: 0, length: 60, pier => pier
///         .AddBerths(PierSide.Left, count: 10, berthWidth: 5, berthLength: 12)
///         .AddBerths(PierSide.Right, count: 10, berthWidth: 5, berthLength: 12, dividers: DividerType.Piles),
///         type: PierType.Concrete)
///     .Build();
/// </code>
/// </example>
public sealed class MarinaLayoutBuilder
{
    private readonly string _name;
    private readonly List<Pier> _piers = [];
    private readonly List<Berth> _berths = [];
    private readonly List<Divider> _dividers = [];
    private readonly List<MultiBerth> _multiBerths = [];
    private readonly List<LandArea> _land = [];
    private readonly GeneratedIds _ids = new();
    private Shoreline? _shoreline;
    private MarineTraffic? _traffic;

    /// <summary>Starts an empty layout.</summary>
    /// <param name="name">Marina name.</param>
    public MarinaLayoutBuilder(string name = "Marina")
    {
        _name = name;
    }

    /// <summary>Adds a quay, breakwater or lawn and, optionally, land berths on it.</summary>
    /// <param name="landArea">The land area.</param>
    /// <param name="configure">Callback receiving a <see cref="LandAreaBuilder"/> to add land berths.</param>
    public MarinaLayoutBuilder AddLandArea(LandArea landArea, Action<LandAreaBuilder>? configure = null)
    {
        _land.Add(landArea);
        configure?.Invoke(new LandAreaBuilder(landArea, _berths, _ids));
        return this;
    }

    /// <summary>Adds a land area from its outline and, optionally, land berths on it.</summary>
    /// <param name="id">Unique land area id.</param>
    /// <param name="points">Outline in plan coordinates.</param>
    /// <param name="height">Top surface height above the water, in meters.</param>
    /// <param name="kind">Surface type.</param>
    /// <param name="configure">Callback receiving a <see cref="LandAreaBuilder"/> to add land berths.</param>
    public MarinaLayoutBuilder AddLandArea(string id, IEnumerable<Vector2> points, float height, LandKind kind = LandKind.Quay, Action<LandAreaBuilder>? configure = null) =>
        AddLandArea(new LandArea(id, points, height, kind), configure);

    /// <summary>Adds a pier and, optionally, lays out its berths and dividers.</summary>
    /// <param name="pier">The pier.</param>
    /// <param name="configure">Callback receiving a <see cref="PierBuilder"/> for this pier.</param>
    public MarinaLayoutBuilder AddPier(Pier pier, Action<PierBuilder>? configure = null)
    {
        _piers.Add(pier);
        configure?.Invoke(new PierBuilder(pier, _berths, _dividers, _ids));
        return this;
    }

    /// <summary>Adds a pier from its shore-end point, heading and size, and optionally lays out its berths and dividers.</summary>
    /// <param name="id">Unique pier id.</param>
    /// <param name="name">Display name.</param>
    /// <param name="start">Shore-end center point in plan coordinates.</param>
    /// <param name="headingDegrees">Direction along the pier (0° = +Z (south), 90° = +X (east)).</param>
    /// <param name="length">Length in meters.</param>
    /// <param name="configure">Callback receiving a <see cref="PierBuilder"/> for this pier.</param>
    /// <param name="width">Deck width in meters.</param>
    /// <param name="type">Construction type.</param>
    public MarinaLayoutBuilder AddPier(
        string id, string name, Vector2 start, float headingDegrees, float length,
        Action<PierBuilder>? configure = null, float width = 2.5f, PierType type = PierType.FloatingWooden) =>
        AddPier(new Pier(id, name, start, headingDegrees, length, width, type), configure);

    /// <summary>Adds a berth at an explicit position, size and orientation.</summary>
    public MarinaLayoutBuilder AddBerth(Berth berth)
    {
        ArgumentNullException.ThrowIfNull(berth);
        _berths.Add(berth);
        _ids.Berths.Add(berth.Id);
        return this;
    }

    /// <summary>Adds a divider at an explicit position, length and orientation.</summary>
    public MarinaLayoutBuilder AddDivider(Divider divider)
    {
        ArgumentNullException.ThrowIfNull(divider);
        _dividers.Add(divider);
        _ids.RegisterDivider(divider);
        return this;
    }

    /// <summary>Puts one boat across several berths added earlier.</summary>
    public MarinaLayoutBuilder AddMultiBerth(MultiBerth berth)
    {
        _multiBerths.Add(berth);
        return this;
    }

    /// <summary>Sets the mainland behind the marina, drawn beneath the land areas. Replaces any set earlier.</summary>
    public MarinaLayoutBuilder WithShoreline(Shoreline? shoreline)
    {
        _shoreline = shoreline;
        return this;
    }

    /// <summary>Sets the passing traffic out at sea. Replaces any set earlier.</summary>
    public MarinaLayoutBuilder WithMarineTraffic(MarineTraffic? traffic)
    {
        _traffic = traffic;
        return this;
    }

    /// <summary>Creates the layout. It is not validated here; <c>InitializeLayout</c> (or <see cref="MarinaLayout.Validate"/>) does that.</summary>
    public MarinaLayout Build() => new()
    {
        Name = _name,
        Piers = _piers.ToArray(),
        Berths = _berths.ToArray(),
        Dividers = _dividers.ToArray(),
        MultiBerths = _multiBerths.ToArray(),
        LandAreas = _land.ToArray(),
        Shoreline = _shoreline,
        MarineTraffic = _traffic,
    };
}

/// <summary>Adds berths and dividers to a single pier inside <see cref="MarinaLayoutBuilder"/>.</summary>
public sealed class PierBuilder
{
    private readonly List<Berth> _berths;
    private readonly List<Divider> _dividers;
    private readonly GeneratedIds _ids;
    private readonly Dictionary<PierSide, float> _nextOffset = [];

    internal PierBuilder(Pier pier, List<Berth> berths, List<Divider> dividers, GeneratedIds ids)
    {
        Pier = pier;
        _berths = berths;
        _dividers = dividers;
        _ids = ids;
    }

    /// <summary>The pier being configured.</summary>
    public Pier Pier { get; }

    /// <summary>
    /// Appends <paramref name="count"/> berths on one side, continuing after any berths previously added on that side.
    /// Ids follow <c>{PierId}-L01</c> / <c>{PierId}-R01</c>; bows point at the pier.
    /// </summary>
    /// <param name="side">Side of the pier.</param>
    /// <param name="count">Number of berths.</param>
    /// <param name="berthWidth">Width of each berth (along the pier), in meters.</param>
    /// <param name="berthLength">Length of each berth (away from the pier), in meters.</param>
    /// <param name="customize">Optional callback to set status, boat, label, flags... on each generated berth (index, berth) → berth.</param>
    /// <param name="startOffset">Distance from the pier's start to the first berth (only for the first call on this side).</param>
    /// <param name="gap">Space between consecutive berths.</param>
    /// <param name="dividers">
    /// When set, explicit dividers of this type are generated at every berth boundary and the berths'
    /// automatic finger piers are turned off.
    /// </param>
    /// <exception cref="InvalidOperationException">The pier has no berths on <paramref name="side"/> (see <see cref="Pier.BerthingSides"/>).</exception>
    public PierBuilder AddBerths(
        PierSide side, int count, float berthWidth, float berthLength,
        Func<int, Berth, Berth>? customize = null, float startOffset = 2f, float gap = 0f, DividerType? dividers = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var offset = _nextOffset.TryGetValue(side, out var existing) ? existing : startOffset;

        // Numbered on from the berths this side already has, skipping any id already taken, even one a customize callback
        // gave a berth of another row.
        var prefix = BerthGenerator.SidePrefix(Pier, side);
        var firstNumber = _ids.ReserveNumbers(prefix, count, _ids.Berths);
        var generated = BerthGenerator.AlongPier(Pier, side, count, berthWidth, berthLength, offset, gap, firstNumber);
        for (var i = 0; i < generated.Count; i++)
        {
            var berth = dividers.HasValue ? generated[i] with { HasFingerPiers = false } : generated[i];
            var added = customize is null ? berth : customize(i, berth);
            _berths.Add(added);
            _ids.Berths.Add(added.Id);
        }

        if (dividers is { } dividerType)
        {
            var dividerPrefix = BerthGenerator.DividerPrefix(Pier, side);
            foreach (var divider in BerthGenerator.DividersAlongPier(Pier, side, count, berthWidth, berthLength, dividerType, offset, gap))
            {
                // Skip a boundary shared with berths added by an earlier call on this pier.
                if (!_ids.TryClaimDividerStart(Pier.Id, divider)) continue;
                var number = _ids.ReserveNumbers(dividerPrefix, 1, _ids.Dividers);
                var numbered = divider with { Id = $"{dividerPrefix}{number:00}" };
                _dividers.Add(numbered);
                _ids.Dividers.Add(numbered.Id);
            }
        }

        _nextOffset[side] = offset + count * (berthWidth + gap);
        return this;
    }

    /// <summary>Adds a single berth on one side of the pier at an explicit distance from the pier's start.</summary>
    /// <param name="id">Unique berth id.</param>
    /// <param name="side">Side of the pier.</param>
    /// <param name="offsetAlong">Distance from the pier's start to the berth's near edge.</param>
    /// <param name="berthWidth">Width along the pier, in meters.</param>
    /// <param name="berthLength">Length away from the pier, in meters.</param>
    /// <param name="customize">Optional callback to adjust the generated berth.</param>
    /// <exception cref="InvalidOperationException">The pier has no berths on <paramref name="side"/> (see <see cref="Pier.BerthingSides"/>).</exception>
    public PierBuilder AddBerth(string id, PierSide side, float offsetAlong, float berthWidth, float berthLength, Func<Berth, Berth>? customize = null)
    {
        var berth = BerthGenerator.AtPier(Pier, id, side, offsetAlong, berthWidth, berthLength);
        var added = customize is null ? berth : customize(berth);
        _berths.Add(added);
        _ids.Berths.Add(added.Id);
        return this;
    }

    /// <summary>Adds a berth defined by absolute position (its <see cref="Berth.PierId"/> is set to this pier).</summary>
    public PierBuilder AddBerth(Berth berth)
    {
        ArgumentNullException.ThrowIfNull(berth);
        _berths.Add(berth.PierId == Pier.Id ? berth : berth with { PierId = Pier.Id });
        _ids.Berths.Add(berth.Id);
        return this;
    }

    /// <summary>Adds a divider defined by absolute position (its <see cref="Divider.PierId"/> is set to this pier).</summary>
    public PierBuilder AddDivider(Divider divider)
    {
        ArgumentNullException.ThrowIfNull(divider);
        var added = divider.PierId == Pier.Id ? divider : divider with { PierId = Pier.Id };
        _dividers.Add(added);
        _ids.RegisterDivider(added);
        return this;
    }
}

/// <summary>Adds land berths to a single <see cref="LandArea"/> inside <see cref="MarinaLayoutBuilder"/>.</summary>
public sealed class LandAreaBuilder
{
    private readonly List<Berth> _berths;
    private readonly GeneratedIds _ids;

    internal LandAreaBuilder(LandArea landArea, List<Berth> berths, GeneratedIds ids)
    {
        LandArea = landArea;
        _berths = berths;
        _ids = ids;
    }

    /// <summary>The land area being configured.</summary>
    public LandArea LandArea { get; }

    /// <summary>Adds a land berth centered at <paramref name="position"/> (see <see cref="Berth.OnLand"/>).</summary>
    /// <param name="id">Unique berth id.</param>
    /// <param name="position">Center of the spot in plan coordinates.</param>
    /// <param name="headingDegrees">Direction the stored boat's bow points.</param>
    /// <param name="length">Length along the heading, in meters.</param>
    /// <param name="width">Width across the heading, in meters.</param>
    /// <param name="customize">Optional callback to set status, boat, label, flags... on the berth.</param>
    public LandAreaBuilder AddBerth(string id, Vector2 position, float headingDegrees = 0f, float length = 12f, float width = 5f, Func<Berth, Berth>? customize = null)
    {
        var berth = Berth.OnLand(id, LandArea.Id, position, headingDegrees, length, width);
        var added = customize is null ? berth : customize(berth);
        _berths.Add(added);
        _ids.Berths.Add(added.Id);
        return this;
    }

    /// <summary>
    /// Adds a row of <paramref name="count"/> land berths side by side, starting at <paramref name="firstPosition"/> and
    /// continuing along <paramref name="rowHeadingDegrees"/>. Ids follow <c>{idPrefix}01</c>, continuing after berths
    /// already added with the same prefix.
    /// </summary>
    /// <param name="idPrefix">Id prefix, e.g. "Y-".</param>
    /// <param name="firstPosition">Center of the first berth.</param>
    /// <param name="rowHeadingDegrees">Direction the row runs in.</param>
    /// <param name="count">Number of berths.</param>
    /// <param name="berthWidth">Width of each berth (along the row), in meters.</param>
    /// <param name="berthLength">Length of each berth (across the row), in meters.</param>
    /// <param name="customize">Optional callback (index, berth) → berth.</param>
    /// <param name="gap">Space between neighbouring berths.</param>
    /// <param name="boatHeadingDegrees">Bow direction; defaults to <paramref name="rowHeadingDegrees"/> − 90° (boats parallel, across the row).</param>
    public LandAreaBuilder AddBerths(
        string idPrefix, Vector2 firstPosition, float rowHeadingDegrees, int count, float berthWidth, float berthLength,
        Func<int, Berth, Berth>? customize = null, float gap = 0.5f, float? boatHeadingDegrees = null)
    {
        ArgumentNullException.ThrowIfNull(idPrefix);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(berthWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(berthLength);

        var direction = MarinaMath.HeadingToDirection(rowHeadingDegrees);
        var heading = boatHeadingDegrees ?? rowHeadingDegrees - 90f;
        var first = _ids.ReserveNumbers(idPrefix, count, _ids.Berths);
        for (var i = 0; i < count; i++)
        {
            var berth = Berth.OnLand($"{idPrefix}{first + i:00}", LandArea.Id, firstPosition + direction * (i * (berthWidth + gap)), heading, berthLength, berthWidth);
            var added = customize is null ? berth : customize(i, berth);
            _berths.Add(added);
            _ids.Berths.Add(added.Id);
        }

        return this;
    }
}

/// <summary>Computes berth and divider geometry relative to a pier. Useful to ERP code that stores only berth numbers.</summary>
public static class BerthGenerator
{
    internal static string SidePrefix(Pier pier, PierSide side) =>
        // A pier that berths boats on one side only has no left and right to distinguish.
        pier.BerthingSides is PierSides.Left or PierSides.Right
            ? $"{pier.Id}-"
            : $"{pier.Id}-{(side == PierSide.Left ? "L" : "R")}";

    /// <summary>
    /// Id prefix of generated dividers: <c>{PierId}-L-D</c> / <c>{PierId}-R-D</c> on a pier with berths on both sides, and
    /// <c>{PierId}-D</c> on a single-sided one (which has no left and right to tell apart).
    /// </summary>
    internal static string DividerPrefix(Pier pier, PierSide side) =>
        pier.BerthingSides is PierSides.Left or PierSides.Right
            ? $"{pier.Id}-D"
            : $"{pier.Id}-{(side == PierSide.Left ? "L" : "R")}-D";

    /// <summary>
    /// Generates berths perpendicular to a pier, bows pointing at the pier.
    /// Ids follow the pattern <c>{PierId}-L01</c> / <c>{PierId}-R01</c>.
    /// </summary>
    /// <param name="pier">The pier.</param>
    /// <param name="side">Side of the pier.</param>
    /// <param name="count">Number of berths.</param>
    /// <param name="berthWidth">Width of each berth along the pier, in meters.</param>
    /// <param name="berthLength">Length of each berth away from the pier, in meters.</param>
    /// <param name="startOffset">Distance from the pier's start to the edge of the first berth.</param>
    /// <param name="gap">Extra spacing between consecutive berths.</param>
    /// <param name="firstNumber">Number used for the first generated berth id.</param>
    /// <example><code>marina.AddBerths(BerthGenerator.AlongPier(marina.GetPier("E")!, PierSide.Left, count: 10, berthWidth: 5, berthLength: 12));</code></example>
    /// <exception cref="InvalidOperationException">The pier has no berths on <paramref name="side"/> (see <see cref="Pier.BerthingSides"/>).</exception>
    public static IReadOnlyList<Berth> AlongPier(
        Pier pier, PierSide side, int count, float berthWidth, float berthLength,
        float startOffset = 2f, float gap = 0f, int firstNumber = 1)
    {
        ArgumentNullException.ThrowIfNull(pier);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(berthWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(berthLength);

        var prefix = SidePrefix(pier, side);
        var result = new List<Berth>(count);
        for (var i = 0; i < count; i++)
        {
            var id = $"{prefix}{firstNumber + i:00}";
            result.Add(AtPier(pier, id, side, startOffset + i * (berthWidth + gap), berthWidth, berthLength));
        }

        return result;
    }

    /// <summary>A single berth perpendicular to the pier, bow toward it.</summary>
    /// <param name="pier">The pier.</param>
    /// <param name="id">Berth id. The berth's <see cref="Berth.Label"/> is left null, so it shows this id until you give it one.</param>
    /// <param name="side">Side of the pier.</param>
    /// <param name="offsetAlong">Distance from the pier's start to the berth's near edge.</param>
    /// <param name="berthWidth">Width along the pier, in meters.</param>
    /// <param name="berthLength">Length away from the pier, in meters.</param>
    /// <exception cref="InvalidOperationException">The pier has no berths on <paramref name="side"/> (see <see cref="Pier.BerthingSides"/>).</exception>
    public static Berth AtPier(Pier pier, string id, PierSide side, float offsetAlong, float berthWidth, float berthLength)
    {
        ArgumentNullException.ThrowIfNull(pier);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(berthWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(berthLength);

        ThrowIfNoBerths(pier, side);

        var outward = Outward(pier, side);
        var along = offsetAlong + berthWidth * 0.5f;
        var center = pier.Start + pier.Direction * along + outward * (pier.Width * 0.5f + berthLength * 0.5f);
        return new Berth(id, pier.Id, center, MarinaMath.DirectionToHeading(-outward), berthLength, berthWidth);
    }

    /// <summary>
    /// Dividers at the <paramref name="count"/> + 1 boundaries of berths laid out like <see cref="AlongPier"/>.
    /// Finger piers are 75% of the berth length; piles and booms run the full length.
    /// </summary>
    /// <param name="pier">The pier.</param>
    /// <param name="side">Side of the pier.</param>
    /// <param name="count">Number of berths (count + 1 dividers are produced, more when <paramref name="gap"/> &gt; 0).</param>
    /// <param name="berthWidth">Width of each berth along the pier.</param>
    /// <param name="berthLength">Length of each berth away from the pier.</param>
    /// <param name="type">Divider type.</param>
    /// <param name="startOffset">Distance from the pier's start to the first berth.</param>
    /// <param name="gap">Space between berths; each berth then gets its own pair of dividers.</param>
    /// <exception cref="InvalidOperationException">The pier has no berths on <paramref name="side"/> (see <see cref="Pier.BerthingSides"/>).</exception>
    public static IReadOnlyList<Divider> DividersAlongPier(
        Pier pier, PierSide side, int count, float berthWidth, float berthLength, DividerType type,
        float startOffset = 2f, float gap = 0f)
    {
        ArgumentNullException.ThrowIfNull(pier);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ThrowIfNoBerths(pier, side);

        var outward = Outward(pier, side);
        var heading = MarinaMath.DirectionToHeading(outward);
        var length = DividerDefaults.Length(type, berthLength);
        var prefix = DividerPrefix(pier, side);
        var result = new List<Divider>();
        if (count == 0) return result;

        for (var i = 0; i <= count; i++)
        {
            // With a gap, each berth gets its own pair of dividers.
            var edges = gap > 0f && i > 0 && i < count
                ? new[] { startOffset + i * (berthWidth + gap) - gap, startOffset + i * (berthWidth + gap) }
                : new[] { startOffset + i * (berthWidth + gap) - (i == count ? gap : 0f) };
            foreach (var edge in edges)
            {
                var start = pier.Start + pier.Direction * edge + outward * (pier.Width * 0.5f);
                result.Add(new Divider($"{prefix}{result.Count + 1:00}", start, heading, length, type)
                {
                    PierId = pier.Id,
                    Width = DividerDefaults.Width(type),
                    Spacing = DividerDefaults.Spacing(type, length),
                });
            }
        }

        return result;
    }

    /// <summary>
    /// One divider standing at <paramref name="offsetAlong"/> meters from the pier's start, running away from the pier on
    /// <paramref name="side"/>: the boundary between two berths. Finger piers are 75% of the berth length, everything else runs the
    /// full length.
    /// </summary>
    /// <param name="pier">The pier.</param>
    /// <param name="id">Divider id.</param>
    /// <param name="side">Side of the pier.</param>
    /// <param name="offsetAlong">Distance from the pier's start.</param>
    /// <param name="berthLength">Length of the berths it separates.</param>
    /// <param name="type">Divider type.</param>
    /// <exception cref="InvalidOperationException">The pier has no berths on <paramref name="side"/> (see <see cref="Pier.BerthingSides"/>).</exception>
    public static Divider DividerAtPier(Pier pier, string id, PierSide side, float offsetAlong, float berthLength, DividerType type)
    {
        ArgumentNullException.ThrowIfNull(pier);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ThrowIfNoBerths(pier, side);

        var outward = Outward(pier, side);
        var length = DividerDefaults.Length(type, berthLength);
        return new Divider(id, pier.Start + pier.Direction * offsetAlong + outward * (pier.Width * 0.5f), MarinaMath.DirectionToHeading(outward), length, type)
        {
            PierId = pier.Id,
            Width = DividerDefaults.Width(type),
            Spacing = DividerDefaults.Spacing(type, length),
        };
    }

    private static void ThrowIfNoBerths(Pier pier, PierSide side)
    {
        if (!pier.HasBerthsOn(side))
        {
            throw new InvalidOperationException($"Pier '{pier.Id}' is single-sided ({pier.BerthingSides}); it has no berths on the {side} side.");
        }
    }

    private static Vector2 Outward(Pier pier, PierSide side) => pier.SideNormal(side);
}

/// <summary>
/// The ids and divider positions a <see cref="MarinaLayoutBuilder"/> has handed out, so each generated row numbers on from the
/// last one on the same side without scanning every berth, and a boundary two rows share gets one divider rather than two.
/// </summary>
internal sealed class GeneratedIds
{
    /// <summary>Divider starts are compared on a grid this fine (10 cm): two in the same or neighbouring cells are the same boundary.</summary>
    private const float SameStart = 0.1f;

    private readonly Dictionary<string, int> _nextNumber = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<(DividerType Type, int X, int Y)>> _dividerStarts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every berth id added so far, however it was added.</summary>
    public HashSet<string> Berths { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every divider id added so far.</summary>
    public HashSet<string> Dividers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The first of <paramref name="count"/> consecutive numbers for ids <c>{prefix}NN</c>, carrying on from the last ones handed
    /// out with that prefix and past any id already in <paramref name="taken"/>.
    /// </summary>
    public int ReserveNumbers(string prefix, int count, HashSet<string> taken)
    {
        var first = _nextNumber.TryGetValue(prefix, out var next) ? next : 1;
        while (FirstTaken(prefix, first, count, taken) is { } clash) first = clash + 1;

        _nextNumber[prefix] = first + count;
        return first;
    }

    /// <summary>Remembers a divider added by hand, so a generated one is not put on top of it.</summary>
    public void RegisterDivider(Divider divider)
    {
        Dividers.Add(divider.Id);
        if (divider.PierId is { } pierId) Starts(pierId).Add(Key(divider));
    }

    /// <summary>
    /// Claims the boundary a generated divider stands on, along its own pier: false when one of the same type already stands
    /// there (from an earlier row, or added by hand).
    /// </summary>
    public bool TryClaimDividerStart(string pierId, Divider divider)
    {
        var starts = Starts(pierId);
        var (type, x, y) = Key(divider);

        // Look at the neighbouring cells too, so two starts either side of a cell edge still count as one.
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (starts.Contains((type, x + dx, y + dy))) return false;
            }
        }

        starts.Add((type, x, y));
        return true;
    }

    /// <summary>The first number of <paramref name="count"/> from <paramref name="first"/> whose id is taken, or null.</summary>
    private static int? FirstTaken(string prefix, int first, int count, HashSet<string> taken)
    {
        for (var number = first; number < first + count; number++)
        {
            if (taken.Contains($"{prefix}{number:00}")) return number;
        }

        return null;
    }

    private static (DividerType Type, int X, int Y) Key(Divider divider) =>
        (divider.Type, (int)MathF.Round(divider.Start.X / SameStart), (int)MathF.Round(divider.Start.Y / SameStart));

    private HashSet<(DividerType Type, int X, int Y)> Starts(string pierId)
    {
        if (!_dividerStarts.TryGetValue(pierId, out var starts)) _dividerStarts[pierId] = starts = [];
        return starts;
    }
}
