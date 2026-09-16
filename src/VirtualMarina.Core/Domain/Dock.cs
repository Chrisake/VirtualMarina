using System.Numerics;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Domain;

/// <summary>Construction of a dock. Controls how it is drawn and its default deck height.</summary>
public enum DockType
{
    /// <summary>Wooden deck on pontoon floats, held by guide piles. Default deck height 0.5 m.</summary>
    FloatingWooden = 0,

    /// <summary>Monolithic concrete pontoon with rubber fenders, held by steel guide piles. Default deck height 0.55 m.</summary>
    FloatingConcrete = 1,

    /// <summary>Fixed concrete pier on columns, with curbs and bollards. Default deck height 1.1 m.</summary>
    Concrete = 2,
}

/// <summary>
/// A straight pier. It starts at <see cref="Start"/> (usually the shore end) and runs
/// <see cref="Length"/> meters along <see cref="HeadingDegrees"/>.
/// </summary>
/// <remarks>
/// Use the constructor when you know the shore end, or <see cref="FromCenter"/> when you store the
/// dock's center point, size and orientation. Slips and dividers keep absolute positions, so moving a
/// dock does not move them.
/// </remarks>
public sealed record Dock
{
    private readonly float? _deckHeight;

    /// <summary>Creates a dock from its shore-end point, heading, length and width.</summary>
    /// <param name="id">Unique id (case-insensitive).</param>
    /// <param name="name">Display name (camera preset "Dock: {name}"); the id is used when null.</param>
    /// <param name="start">Center of the shore end of the deck, in plan coordinates (X = world X, Y = world Z).</param>
    /// <param name="headingDegrees">Direction from the start along the dock (0° = +Z, 90° = +X).</param>
    /// <param name="length">Length along the heading, in meters.</param>
    /// <param name="width">Deck width across the heading, in meters.</param>
    /// <param name="type">Construction; controls rendering and the default <see cref="DeckHeight"/>.</param>
    /// <example><code>new Dock("A", "Dock A", new Vector2(80, -6), headingDegrees: 0, length: 72, width: 3.5f, DockType.Concrete)</code></example>
    public Dock(string id, string name, Vector2 start, float headingDegrees, float length, float width = 2.5f, DockType type = DockType.FloatingWooden)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        Name = name ?? id;
        Start = start;
        HeadingDegrees = headingDegrees;
        Length = length;
        Width = width;
        Type = type;
    }

    /// <summary>Creates a dock from its center point, size and orientation.</summary>
    /// <param name="center">Center of the deck in plan coordinates (X = world X, Y = world Z).</param>
    /// <param name="length">Length along <paramref name="headingDegrees"/>.</param>
    /// <param name="width">Width across the heading.</param>
    /// <param name="headingDegrees">Direction of the dock's long axis (0° = +Z, 90° = +X).</param>
    /// <param name="id">Unique id (case-insensitive).</param>
    /// <param name="name">Display name.</param>
    /// <param name="type">Construction; controls rendering and the default <see cref="DeckHeight"/>.</param>
    public static Dock FromCenter(string id, string name, Vector2 center, float length, float width, float headingDegrees, DockType type = DockType.FloatingWooden) =>
        new(id, name, center - MarinaMath.HeadingToDirection(headingDegrees) * (length * 0.5f), headingDegrees, length, width, type);

    /// <summary>Unique id (case-insensitive). Slips and dividers reference it.</summary>
    public string Id { get; init; }

    /// <summary>Display name, used in tooltips and the dock camera preset.</summary>
    public string Name { get; init; }

    /// <summary>Shore-end center point in plan coordinates.</summary>
    public Vector2 Start { get; init; }

    /// <summary>Direction from <see cref="Start"/> along the dock, in degrees (0° = +Z, 90° = +X).</summary>
    public float HeadingDegrees { get; init; }

    /// <summary>Length along the heading, in meters.</summary>
    public float Length { get; init; }

    /// <summary>Deck width across the heading, in meters.</summary>
    public float Width { get; init; }

    /// <summary>Construction type; controls rendering and the default deck height.</summary>
    public DockType Type { get; init; }

    /// <summary>Height of the deck surface above the water, in meters. Defaults depend on <see cref="Type"/>.</summary>
    public float DeckHeight
    {
        get => _deckHeight ?? GetDefaultDeckHeight(Type);
        init => _deckHeight = value;
    }

    /// <summary>
    /// Distance between supports along each edge: columns for <see cref="DockType.Concrete"/>,
    /// guide piles (every second interval) for floating docks.
    /// </summary>
    public float PilingSpacing { get; init; } = 6f;

    /// <summary>Unit plan-view vector along the dock (from start to end).</summary>
    public Vector2 Direction => MarinaMath.HeadingToDirection(HeadingDegrees);

    /// <summary>
    /// Unit plan-view vector across the dock toward <see cref="DockSide.Right"/>: the heading's local +X axis, <c>(cos h, −sin h)</c>.
    /// For a dock with heading 0° (running along +Z) it points to +X.
    /// </summary>
    public Vector2 Right => MarinaMath.HeadingToRight(HeadingDegrees);

    /// <summary>Seaward-end center point.</summary>
    public Vector2 End => Start + Direction * Length;

    /// <summary>Center of the deck.</summary>
    public Vector2 Center => Start + Direction * (Length * 0.5f);

    /// <summary>Deck footprint.</summary>
    public OrientedRect Bounds => new(Center, new Vector2(Width, Length), HeadingDegrees);

    /// <summary>True for <see cref="DockType.FloatingWooden"/> and <see cref="DockType.FloatingConcrete"/>.</summary>
    public bool IsFloating => Type is DockType.FloatingWooden or DockType.FloatingConcrete;

    /// <summary>Deck height used when <see cref="DeckHeight"/> isn't set: 0.5 m wooden, 0.55 m floating concrete, 1.1 m fixed concrete.</summary>
    public static float GetDefaultDeckHeight(DockType type) => type switch
    {
        DockType.Concrete => 1.1f,
        DockType.FloatingConcrete => 0.55f,
        _ => 0.5f,
    };

    /// <summary>Human-readable name of a dock type, e.g. "Floating (wooden)".</summary>
    public static string GetDisplayName(DockType type) => type switch
    {
        DockType.FloatingWooden => "Floating (wooden)",
        DockType.FloatingConcrete => "Floating (concrete)",
        DockType.Concrete => "Fixed concrete",
        _ => type.ToString(),
    };

    /// <summary>Returns a copy moved so its center is at <paramref name="center"/>.</summary>
    public Dock WithCenter(Vector2 center) => this with { Start = center - Direction * (Length * 0.5f) };

    internal IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) yield return "Dock id must not be empty.";
        if (!(Length > 0f)) yield return $"Dock '{Id}' must have a positive length.";
        if (!(Width > 0f)) yield return $"Dock '{Id}' must have a positive width.";
        if (!(PilingSpacing > 0.5f)) yield return $"Dock '{Id}' piling spacing must be greater than 0.5 m.";
        if (!float.IsFinite(Start.X) || !float.IsFinite(Start.Y) || !float.IsFinite(HeadingDegrees)) yield return $"Dock '{Id}' has a non-finite position or heading.";
        if (!Enum.IsDefined(Type)) yield return $"Dock '{Id}' has an unknown type '{Type}'.";
        if (!(DeckHeight >= 0f) || DeckHeight > 5f) yield return $"Dock '{Id}' deck height must be between 0 and 5 m.";
    }
}

/// <summary>Which side of a dock slips and dividers are generated on, relative to <see cref="Dock.Right"/>.</summary>
public enum DockSide
{
    /// <summary>The side opposite <see cref="Dock.Right"/> (−X for a dock with heading 0°). Generated ids use "L": <c>{DockId}-L01</c>.</summary>
    Left,

    /// <summary>The side <see cref="Dock.Right"/> points to (+X for a dock with heading 0°). Generated ids use "R": <c>{DockId}-R01</c>.</summary>
    Right,
}
