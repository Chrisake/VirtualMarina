using System.Numerics;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Domain;

/// <summary>
/// Fluent helper for composing a <see cref="MarinaLayout"/> with slips auto-positioned along docks.
/// </summary>
/// <example>
/// <code>
/// var layout = new MarinaLayoutBuilder("Harbor")
///     .AddDock("A", "Dock A", new Vector2(0, 0), headingDegrees: 0, length: 60, dock => dock
///         .AddSlips(DockSide.Left, count: 10, slipWidth: 5, slipLength: 12)
///         .AddSlips(DockSide.Right, count: 10, slipWidth: 5, slipLength: 12, dividers: DividerType.Piles),
///         type: DockType.Concrete)
///     .Build();
/// </code>
/// </example>
public sealed class MarinaLayoutBuilder
{
    private readonly string _name;
    private readonly List<Dock> _docks = new();
    private readonly List<Slip> _slips = new();
    private readonly List<Divider> _dividers = new();
    private readonly List<MultiSlipBerth> _berths = new();
    private readonly List<LandArea> _land = new();

    /// <summary>Starts an empty layout.</summary>
    /// <param name="name">Marina name.</param>
    public MarinaLayoutBuilder(string name = "Marina")
    {
        _name = name;
    }

    /// <summary>Adds a quay, breakwater or lawn.</summary>
    public MarinaLayoutBuilder AddLandArea(LandArea landArea)
    {
        _land.Add(landArea);
        return this;
    }

    /// <summary>Adds a dock and, optionally, lays out its slips and dividers.</summary>
    /// <param name="dock">The dock.</param>
    /// <param name="configure">Callback receiving a <see cref="DockBuilder"/> for this dock.</param>
    public MarinaLayoutBuilder AddDock(Dock dock, Action<DockBuilder>? configure = null)
    {
        _docks.Add(dock);
        configure?.Invoke(new DockBuilder(dock, _slips, _dividers));
        return this;
    }

    /// <summary>Adds a dock from its shore-end point, heading and size, and optionally lays out its slips and dividers.</summary>
    /// <param name="id">Unique dock id.</param>
    /// <param name="name">Display name.</param>
    /// <param name="start">Shore-end center point in plan coordinates.</param>
    /// <param name="headingDegrees">Direction along the dock (0° = +Z, 90° = +X).</param>
    /// <param name="length">Length in meters.</param>
    /// <param name="configure">Callback receiving a <see cref="DockBuilder"/> for this dock.</param>
    /// <param name="width">Deck width in meters.</param>
    /// <param name="type">Construction type.</param>
    public MarinaLayoutBuilder AddDock(
        string id, string name, Vector2 start, float headingDegrees, float length,
        Action<DockBuilder>? configure = null, float width = 2.5f, DockType type = DockType.FloatingWooden) =>
        AddDock(new Dock(id, name, start, headingDegrees, length, width, type), configure);

    /// <summary>Adds a slip at an explicit position, size and orientation.</summary>
    public MarinaLayoutBuilder AddSlip(Slip slip)
    {
        _slips.Add(slip);
        return this;
    }

    /// <summary>Adds a divider at an explicit position, length and orientation.</summary>
    public MarinaLayoutBuilder AddDivider(Divider divider)
    {
        _dividers.Add(divider);
        return this;
    }

    /// <summary>Puts one boat across several slips added earlier.</summary>
    public MarinaLayoutBuilder AddMultiSlipBerth(MultiSlipBerth berth)
    {
        _berths.Add(berth);
        return this;
    }

    /// <summary>Creates the layout. It is not validated here; <c>InitializeLayout</c> (or <see cref="MarinaLayout.Validate"/>) does that.</summary>
    public MarinaLayout Build() => new()
    {
        Name = _name,
        Docks = _docks.ToArray(),
        Slips = _slips.ToArray(),
        Dividers = _dividers.ToArray(),
        MultiSlipBerths = _berths.ToArray(),
        LandAreas = _land.ToArray(),
    };
}

/// <summary>Adds slips and dividers to a single dock inside <see cref="MarinaLayoutBuilder"/>.</summary>
public sealed class DockBuilder
{
    private readonly List<Slip> _slips;
    private readonly List<Divider> _dividers;
    private readonly Dictionary<DockSide, float> _nextOffset = new();

    internal DockBuilder(Dock dock, List<Slip> slips, List<Divider> dividers)
    {
        Dock = dock;
        _slips = slips;
        _dividers = dividers;
    }

    /// <summary>The dock being configured.</summary>
    public Dock Dock { get; }

    /// <summary>
    /// Appends <paramref name="count"/> slips on one side, continuing after any slips previously added on that side.
    /// Ids follow <c>{DockId}-L01</c> / <c>{DockId}-R01</c>; bows point at the dock.
    /// </summary>
    /// <param name="side">Side of the dock.</param>
    /// <param name="count">Number of slips.</param>
    /// <param name="slipWidth">Width of each slip (along the dock), in meters.</param>
    /// <param name="slipLength">Length of each slip (away from the dock), in meters.</param>
    /// <param name="customize">Optional callback to set status, boat, label, flags... on each generated slip (index, slip) → slip.</param>
    /// <param name="startOffset">Distance from the dock's start to the first slip (only for the first call on this side).</param>
    /// <param name="gap">Space between consecutive slips.</param>
    /// <param name="dividers">
    /// When set, explicit dividers of this type are generated at every slip boundary and the slips'
    /// automatic finger piers are turned off.
    /// </param>
    public DockBuilder AddSlips(
        DockSide side, int count, float slipWidth, float slipLength,
        Func<int, Slip, Slip>? customize = null, float startOffset = 2f, float gap = 0f, DividerType? dividers = null)
    {
        var offset = _nextOffset.TryGetValue(side, out var existing) ? existing : startOffset;
        var existingCount = _slips.Count(s => s.DockId == Dock.Id && s.Id.StartsWith(SlipGenerator.SidePrefix(Dock, side), StringComparison.Ordinal));
        var generated = SlipGenerator.AlongDock(Dock, side, count, slipWidth, slipLength, offset, gap, existingCount + 1);
        for (var i = 0; i < generated.Count; i++)
        {
            var slip = dividers.HasValue ? generated[i] with { HasFingerPiers = false } : generated[i];
            _slips.Add(customize is null ? slip : customize(i, slip));
        }

        if (dividers is { } dividerType)
        {
            foreach (var divider in SlipGenerator.DividersAlongDock(Dock, side, count, slipWidth, slipLength, dividerType, offset, gap))
            {
                // Skip a boundary shared with slips added by an earlier call.
                if (_dividers.Any(d => Vector2.DistanceSquared(d.Start, divider.Start) < 0.01f && d.Type == divider.Type)) continue;
                var number = _dividers.Count(d => d.Id.StartsWith(SlipGenerator.DividerPrefix(Dock, side), StringComparison.Ordinal)) + 1;
                _dividers.Add(divider with { Id = $"{SlipGenerator.DividerPrefix(Dock, side)}{number:00}" });
            }
        }

        _nextOffset[side] = offset + count * (slipWidth + gap);
        return this;
    }

    /// <summary>Adds a single slip on one side of the dock at an explicit distance from the dock's start.</summary>
    /// <param name="id">Unique slip id.</param>
    /// <param name="side">Side of the dock.</param>
    /// <param name="offsetAlong">Distance from the dock's start to the slip's near edge.</param>
    /// <param name="slipWidth">Width along the dock, in meters.</param>
    /// <param name="slipLength">Length away from the dock, in meters.</param>
    /// <param name="customize">Optional callback to adjust the generated slip.</param>
    public DockBuilder AddSlip(string id, DockSide side, float offsetAlong, float slipWidth, float slipLength, Func<Slip, Slip>? customize = null)
    {
        var slip = SlipGenerator.AtDock(Dock, id, side, offsetAlong, slipWidth, slipLength);
        _slips.Add(customize is null ? slip : customize(slip));
        return this;
    }

    /// <summary>Adds a slip defined by absolute position (its <see cref="Slip.DockId"/> is set to this dock).</summary>
    public DockBuilder AddSlip(Slip slip)
    {
        _slips.Add(slip.DockId == Dock.Id ? slip : slip with { DockId = Dock.Id });
        return this;
    }

    /// <summary>Adds a divider defined by absolute position (its <see cref="Divider.DockId"/> is set to this dock).</summary>
    public DockBuilder AddDivider(Divider divider)
    {
        _dividers.Add(divider.DockId == Dock.Id ? divider : divider with { DockId = Dock.Id });
        return this;
    }
}

/// <summary>Computes slip and divider geometry relative to a dock. Useful to ERP code that stores only slip numbers.</summary>
public static class SlipGenerator
{
    internal static string SidePrefix(Dock dock, DockSide side) => $"{dock.Id}-{(side == DockSide.Left ? "L" : "R")}";

    internal static string DividerPrefix(Dock dock, DockSide side) => $"{SidePrefix(dock, side)}-D";

    /// <summary>
    /// Generates slips perpendicular to a dock, bows pointing at the dock.
    /// Ids follow the pattern <c>{DockId}-L01</c> / <c>{DockId}-R01</c>.
    /// </summary>
    /// <param name="dock">The dock.</param>
    /// <param name="side">Side of the dock.</param>
    /// <param name="count">Number of slips.</param>
    /// <param name="slipWidth">Width of each slip along the dock, in meters.</param>
    /// <param name="slipLength">Length of each slip away from the dock, in meters.</param>
    /// <param name="startOffset">Distance from the dock's start to the edge of the first slip.</param>
    /// <param name="gap">Extra spacing between consecutive slips.</param>
    /// <param name="firstNumber">Number used for the first generated slip id.</param>
    /// <example><code>marina.AddSlips(SlipGenerator.AlongDock(marina.GetDock("E")!, DockSide.Left, count: 10, slipWidth: 5, slipLength: 12));</code></example>
    public static IReadOnlyList<Slip> AlongDock(
        Dock dock, DockSide side, int count, float slipWidth, float slipLength,
        float startOffset = 2f, float gap = 0f, int firstNumber = 1)
    {
        ArgumentNullException.ThrowIfNull(dock);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slipWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slipLength);

        var prefix = SidePrefix(dock, side);
        var result = new List<Slip>(count);
        for (var i = 0; i < count; i++)
        {
            var id = $"{prefix}{firstNumber + i:00}";
            result.Add(AtDock(dock, id, side, startOffset + i * (slipWidth + gap), slipWidth, slipLength));
        }

        return result;
    }

    /// <summary>A single slip perpendicular to the dock, bow toward it.</summary>
    /// <param name="dock">The dock.</param>
    /// <param name="id">Slip id (also used as its label).</param>
    /// <param name="side">Side of the dock.</param>
    /// <param name="offsetAlong">Distance from the dock's start to the slip's near edge.</param>
    /// <param name="slipWidth">Width along the dock, in meters.</param>
    /// <param name="slipLength">Length away from the dock, in meters.</param>
    public static Slip AtDock(Dock dock, string id, DockSide side, float offsetAlong, float slipWidth, float slipLength)
    {
        ArgumentNullException.ThrowIfNull(dock);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slipWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slipLength);

        var outward = Outward(dock, side);
        var along = offsetAlong + slipWidth * 0.5f;
        var center = dock.Start + dock.Direction * along + outward * (dock.Width * 0.5f + slipLength * 0.5f);
        return new Slip(id, dock.Id, center, MarinaMath.DirectionToHeading(-outward), slipLength, slipWidth) { Label = id };
    }

    /// <summary>
    /// Dividers at the <paramref name="count"/> + 1 boundaries of slips laid out like <see cref="AlongDock"/>.
    /// Finger piers are 75% of the slip length; piles and booms run the full length.
    /// </summary>
    /// <param name="dock">The dock.</param>
    /// <param name="side">Side of the dock.</param>
    /// <param name="count">Number of slips (count + 1 dividers are produced, more when <paramref name="gap"/> &gt; 0).</param>
    /// <param name="slipWidth">Width of each slip along the dock.</param>
    /// <param name="slipLength">Length of each slip away from the dock.</param>
    /// <param name="type">Divider type.</param>
    /// <param name="startOffset">Distance from the dock's start to the first slip.</param>
    /// <param name="gap">Space between slips; each slip then gets its own pair of dividers.</param>
    public static IReadOnlyList<Divider> DividersAlongDock(
        Dock dock, DockSide side, int count, float slipWidth, float slipLength, DividerType type,
        float startOffset = 2f, float gap = 0f)
    {
        ArgumentNullException.ThrowIfNull(dock);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var outward = Outward(dock, side);
        var heading = MarinaMath.DirectionToHeading(outward);
        var length = type == DividerType.FingerPier ? slipLength * 0.75f : slipLength;
        var prefix = DividerPrefix(dock, side);
        var result = new List<Divider>();
        if (count == 0) return result;

        for (var i = 0; i <= count; i++)
        {
            // With a gap, each slip gets its own pair of dividers.
            var edges = gap > 0f && i > 0 && i < count
                ? new[] { startOffset + i * (slipWidth + gap) - gap, startOffset + i * (slipWidth + gap) }
                : new[] { startOffset + i * (slipWidth + gap) - (i == count ? gap : 0f) };
            foreach (var edge in edges)
            {
                var start = dock.Start + dock.Direction * edge + outward * (dock.Width * 0.5f);
                result.Add(new Divider($"{prefix}{result.Count + 1:00}", start, heading, length, type)
                {
                    DockId = dock.Id,
                    Width = type == DividerType.FingerPier ? 0.9f : type == DividerType.Boom ? 0.35f : 0.4f,
                    Spacing = type == DividerType.Piles ? MathF.Max(3f, length / 3f) : 1.6f,
                });
            }
        }

        return result;
    }

    private static Vector2 Outward(Dock dock, DockSide side) => dock.Right * (side == DockSide.Right ? 1f : -1f);
}
