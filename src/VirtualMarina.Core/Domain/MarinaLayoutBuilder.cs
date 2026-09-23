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
        configure?.Invoke(new LandAreaBuilder(landArea, _berths));
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
        configure?.Invoke(new PierBuilder(pier, _berths, _dividers));
        return this;
    }

    /// <summary>Adds a pier from its shore-end point, heading and size, and optionally lays out its berths and dividers.</summary>
    /// <param name="id">Unique pier id.</param>
    /// <param name="name">Display name.</param>
    /// <param name="start">Shore-end center point in plan coordinates.</param>
    /// <param name="headingDegrees">Direction along the pier (0° = +Z, 90° = +X).</param>
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
        _berths.Add(berth);
        return this;
    }

    /// <summary>Adds a divider at an explicit position, length and orientation.</summary>
    public MarinaLayoutBuilder AddDivider(Divider divider)
    {
        _dividers.Add(divider);
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
    private readonly Dictionary<PierSide, float> _nextOffset = [];

    internal PierBuilder(Pier pier, List<Berth> berths, List<Divider> dividers)
    {
        Pier = pier;
        _berths = berths;
        _dividers = dividers;
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
        var offset = _nextOffset.TryGetValue(side, out var existing) ? existing : startOffset;
        var existingCount = _berths.Count(s => s.PierId == Pier.Id && s.Id.StartsWith(BerthGenerator.SidePrefix(Pier, side), StringComparison.Ordinal));
        var generated = BerthGenerator.AlongPier(Pier, side, count, berthWidth, berthLength, offset, gap, existingCount + 1);
        for (var i = 0; i < generated.Count; i++)
        {
            var berth = dividers.HasValue ? generated[i] with { HasFingerPiers = false } : generated[i];
            _berths.Add(customize is null ? berth : customize(i, berth));
        }

        if (dividers is { } dividerType)
        {
            foreach (var divider in BerthGenerator.DividersAlongPier(Pier, side, count, berthWidth, berthLength, dividerType, offset, gap))
            {
                // Skip a boundary shared with berths added by an earlier call.
                if (_dividers.Any(d => Vector2.DistanceSquared(d.Start, divider.Start) < 0.01f && d.Type == divider.Type)) continue;
                var number = _dividers.Count(d => d.Id.StartsWith(BerthGenerator.DividerPrefix(Pier, side), StringComparison.Ordinal)) + 1;
                _dividers.Add(divider with { Id = $"{BerthGenerator.DividerPrefix(Pier, side)}{number:00}" });
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
        _berths.Add(customize is null ? berth : customize(berth));
        return this;
    }

    /// <summary>Adds a berth defined by absolute position (its <see cref="Berth.PierId"/> is set to this pier).</summary>
    public PierBuilder AddBerth(Berth berth)
    {
        ArgumentNullException.ThrowIfNull(berth);
        _berths.Add(berth.PierId == Pier.Id ? berth : berth with { PierId = Pier.Id });
        return this;
    }

    /// <summary>Adds a divider defined by absolute position (its <see cref="Divider.PierId"/> is set to this pier).</summary>
    public PierBuilder AddDivider(Divider divider)
    {
        ArgumentNullException.ThrowIfNull(divider);
        _dividers.Add(divider.PierId == Pier.Id ? divider : divider with { PierId = Pier.Id });
        return this;
    }
}

/// <summary>Adds land berths to a single <see cref="LandArea"/> inside <see cref="MarinaLayoutBuilder"/>.</summary>
public sealed class LandAreaBuilder
{
    private readonly List<Berth> _berths;

    internal LandAreaBuilder(LandArea landArea, List<Berth> berths)
    {
        LandArea = landArea;
        _berths = berths;
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
        _berths.Add(customize is null ? berth : customize(berth));
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
        var existing = _berths.Count(s => s.Id.StartsWith(idPrefix, StringComparison.Ordinal));
        for (var i = 0; i < count; i++)
        {
            var berth = Berth.OnLand($"{idPrefix}{existing + i + 1:00}", LandArea.Id, firstPosition + direction * (i * (berthWidth + gap)), heading, berthLength, berthWidth);
            _berths.Add(customize is null ? berth : customize(i, berth));
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

    internal static string DividerPrefix(Pier pier, PierSide side) => $"{SidePrefix(pier, side)}-D";

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
    /// <param name="id">Berth id (also used as its label).</param>
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
        return new Berth(id, pier.Id, center, MarinaMath.DirectionToHeading(-outward), berthLength, berthWidth) { Label = id };
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
        var length = type == DividerType.FingerPier ? berthLength * 0.75f : berthLength;
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
                    Width = type == DividerType.FingerPier ? 0.9f : type == DividerType.Boom ? 0.35f : 0.4f,
                    Spacing = type == DividerType.Piles ? MathF.Max(3f, length / 3f) : 1.6f,
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
        var length = type == DividerType.FingerPier ? berthLength * 0.75f : berthLength;
        return new Divider(id, pier.Start + pier.Direction * offsetAlong + outward * (pier.Width * 0.5f), MarinaMath.DirectionToHeading(outward), length, type)
        {
            PierId = pier.Id,
            Width = type == DividerType.FingerPier ? 0.9f : type == DividerType.Boom ? 0.35f : 0.4f,
            Spacing = type == DividerType.Piles ? MathF.Max(3f, length / 3f) : 1.6f,
        };
    }

    private static void ThrowIfNoBerths(Pier pier, PierSide side)
    {
        if (!pier.HasBerthsOn(side))
        {
            throw new InvalidOperationException($"Pier '{pier.Id}' is single-sided ({pier.BerthingSides}); it has no berths on the {side} side.");
        }
    }

    private static Vector2 Outward(Pier pier, PierSide side) => pier.Right * (side == PierSide.Right ? 1f : -1f);
}
