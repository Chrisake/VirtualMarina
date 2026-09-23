using System.Numerics;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Domain;

/// <summary>Construction of a pier. Controls how it is drawn and its default deck height.</summary>
public enum PierType
{
    /// <summary>Wooden deck on pontoon floats. Default deck height 0.5 m.</summary>
    FloatingWooden = 0,

    /// <summary>Monolithic concrete pontoon with rubber fenders and cleats. Default deck height 0.55 m.</summary>
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
/// pier's center point, size and orientation. Berths and dividers keep absolute positions, so moving a
/// pier does not move them.
/// </remarks>
public sealed record Pier
{
    private readonly float? _deckHeight;
    private readonly ValueDictionary _metadata = ValueDictionary.Empty;

    /// <summary>Creates a pier from its shore-end point, heading, length and width.</summary>
    /// <param name="id">Unique id (case-insensitive).</param>
    /// <param name="name">Display name (camera preset "Pier: {name}"); the id is used when null.</param>
    /// <param name="start">Center of the shore end of the deck, in plan coordinates (X = world X, Y = world Z).</param>
    /// <param name="headingDegrees">Direction from the start along the pier (0° = +Z (south), 90° = +X (east)).</param>
    /// <param name="length">Length along the heading, in meters.</param>
    /// <param name="width">Deck width across the heading, in meters.</param>
    /// <param name="type">Construction; controls rendering and the default <see cref="DeckHeight"/>.</param>
    /// <example><code>new Pier("A", "Pier A", new Vector2(80, -6), headingDegrees: 0, length: 72, width: 3.5f, PierType.Concrete)</code></example>
    public Pier(string id, string name, Vector2 start, float headingDegrees, float length, float width = 2.5f, PierType type = PierType.FloatingWooden)
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

    /// <summary>Creates a pier from its center point, size and orientation.</summary>
    /// <param name="center">Center of the deck in plan coordinates (X = world X, Y = world Z).</param>
    /// <param name="length">Length along <paramref name="headingDegrees"/>.</param>
    /// <param name="width">Width across the heading.</param>
    /// <param name="headingDegrees">Direction of the pier's long axis (0° = +Z (south), 90° = +X (east)).</param>
    /// <param name="id">Unique id (case-insensitive).</param>
    /// <param name="name">Display name.</param>
    /// <param name="type">Construction; controls rendering and the default <see cref="DeckHeight"/>.</param>
    public static Pier FromCenter(string id, string name, Vector2 center, float length, float width, float headingDegrees, PierType type = PierType.FloatingWooden) =>
        new(id, name, center - MarinaMath.HeadingToDirection(headingDegrees) * (length * 0.5f), headingDegrees, length, width, type);

    /// <summary>Unique id (case-insensitive). Berths and dividers reference it.</summary>
    public string Id { get; init; }

    /// <summary>Display name, used in tooltips and the pier camera preset.</summary>
    public string Name { get; init; }

    /// <summary>Shore-end center point in plan coordinates.</summary>
    public Vector2 Start { get; init; }

    /// <summary>Direction from <see cref="Start"/> along the pier, in degrees (0° = +Z (south), 90° = +X (east)).</summary>
    public float HeadingDegrees { get; init; }

    /// <summary>Length along the heading, in meters.</summary>
    public float Length { get; init; }

    /// <summary>Deck width across the heading, in meters.</summary>
    public float Width { get; init; }

    /// <summary>Construction type; controls rendering and the default deck height.</summary>
    public PierType Type { get; init; }

    /// <summary>Height of the deck surface above the water, in meters. Defaults depend on <see cref="Type"/>.</summary>
    public float DeckHeight
    {
        get => _deckHeight ?? GetDefaultDeckHeight(Type);
        init => _deckHeight = value;
    }

    /// <summary>True when <see cref="DeckHeight"/> was set rather than taken from the type.</summary>
    internal bool HasCustomDeckHeight => _deckHeight.HasValue;

    /// <summary>Distance between columns (<see cref="PierType.Concrete"/>) or cleats (<see cref="PierType.FloatingConcrete"/>) along the pier.</summary>
    public float PilingSpacing { get; init; } = 6f;

    /// <summary>
    /// Sides where boats berth. Default <see cref="PierSides.Both"/>. A single-sided pier (e.g. one running along the edge of a
    /// <see cref="LandArea"/>) only takes berths on its open side, and its mooring points (bollards, cleats and
    /// fenders) are drawn on that side only.
    /// </summary>
    /// <example><code>new Pier("Q", "Quay pontoon", new Vector2(-40, 0), headingDegrees: 90, length: 60) { BerthingSides = PierSides.Left }</code></example>
    public PierSides BerthingSides { get; init; } = PierSides.Both;

    /// <summary>
    /// Power and water pedestals drawn along the pier. Default <see cref="PierServices.None"/>. They are placed on the berthing
    /// sides only, and only next to the berths that exist, so a pier without berths shows none.
    /// </summary>
    /// <example><code>new Pier("A", "Pier A", start, 0f, 60f) { Services = PierServices.PowerAndWater }</code></example>
    public PierServices Services { get; init; } = PierServices.None;

    /// <summary>
    /// Read-only string attributes the host application attaches to this pier, e.g. its own key or a contract
    /// reference. Saved to and loaded from a marina file, and never read by the visualizer.
    /// </summary>
    /// <example><code>pier with { Metadata = new Dictionary&lt;string, string&gt; { ["erpId"] = "PONT-07" } }</code></example>
    public IReadOnlyDictionary<string, string> Metadata { get => _metadata; init => _metadata = ValueDictionary.From(value); }

    /// <summary>True when boats can berth on <paramref name="side"/> (see <see cref="BerthingSides"/>).</summary>
    public bool HasBerthsOn(PierSide side) => (BerthingSides & (side == PierSide.Left ? PierSides.Left : PierSides.Right)) != 0;

    /// <summary>Unit plan-view vector along the pier (from start to end).</summary>
    public Vector2 Direction => MarinaMath.HeadingToDirection(HeadingDegrees);

    /// <summary>
    /// Unit plan-view vector across the pier toward <see cref="PierSide.Right"/>: the right-hand side of someone standing at
    /// <see cref="Start"/> and looking toward <see cref="End"/>, <c>(−cos h, sin h)</c>. For a pier with heading 0° (running along +Z,
    /// south) it points to −X (west); for heading 180° (running north, toward −Z) it points to +X (east).
    /// </summary>
    /// <remarks>
    /// This is <em>not</em> the same axis as <see cref="Berth.Right"/>, <see cref="Divider.Right"/> and <see cref="OrientedRect.Right"/>,
    /// which are the heading's local +X axis and point the opposite way for the same heading: <c>Pier.Right == −Pier.LocalX</c>.
    /// Both names are kept because files and hosts depend on them; use <see cref="SideNormal"/> or <see cref="LocalX"/> when the
    /// direction matters. See Docs/20-coordinate-conventions.md.
    /// </remarks>
    public Vector2 Right => -MarinaMath.HeadingToRight(HeadingDegrees);

    /// <summary>
    /// The heading's local +X axis, <c>(cos h, −sin h)</c>: +X (east) for heading 0°. It points toward <see cref="PierSide.Left"/>
    /// and is the axis <see cref="Berth.LocalX"/>, <see cref="Divider.LocalX"/> and <see cref="OrientedRect.LocalX"/> use. Equal to
    /// <c>−<see cref="Right"/></c>.
    /// </summary>
    public Vector2 LocalX => MarinaMath.HeadingToRight(HeadingDegrees);

    /// <summary>
    /// Unit plan-view vector from the pier's center line out across <paramref name="side"/>: where the berths of that side lie.
    /// <see cref="PierSide.Left"/> gives <see cref="LocalX"/> (+X for heading 0°), <see cref="PierSide.Right"/> gives
    /// <see cref="Right"/> (−X for heading 0°).
    /// </summary>
    /// <param name="side">The side of the pier.</param>
    /// <example><code>var berthCenter = pier.Center + pier.SideNormal(PierSide.Left) * (pier.Width / 2 + berthLength / 2);</code></example>
    public Vector2 SideNormal(PierSide side) => side == PierSide.Right ? Right : LocalX;

    /// <summary>Seaward-end center point.</summary>
    public Vector2 End => Start + Direction * Length;

    /// <summary>Center of the deck.</summary>
    public Vector2 Center => Start + Direction * (Length * 0.5f);

    /// <summary>Deck footprint.</summary>
    public OrientedRect Bounds => new(Center, new Vector2(Width, Length), HeadingDegrees);

    /// <summary>True for <see cref="PierType.FloatingWooden"/> and <see cref="PierType.FloatingConcrete"/>.</summary>
    public bool IsFloating => Type is PierType.FloatingWooden or PierType.FloatingConcrete;

    /// <summary>Deck height used when <see cref="DeckHeight"/> isn't set: 0.5 m wooden, 0.55 m floating concrete, 1.1 m fixed concrete.</summary>
    public static float GetDefaultDeckHeight(PierType type) => type switch
    {
        PierType.Concrete => 1.1f,
        PierType.FloatingConcrete => 0.55f,
        _ => 0.5f,
    };

    /// <summary>Human-readable name of a pier type, e.g. "Floating (wooden)".</summary>
    public static string GetDisplayName(PierType type) => type switch
    {
        PierType.FloatingWooden => Strings.PierTypeFloatingWooden,
        PierType.FloatingConcrete => Strings.PierTypeFloatingConcrete,
        PierType.Concrete => Strings.PierTypeConcrete,
        _ => type.ToString(),
    };

    /// <summary>Returns a copy moved so its center is at <paramref name="center"/>.</summary>
    public Pier WithCenter(Vector2 center) => this with { Start = center - Direction * (Length * 0.5f) };

    internal IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) yield return Strings.ErrorPierIdEmpty;
        if (!(Length > 0f && float.IsFinite(Length))) yield return Strings.Format(Strings.ErrorPierLength, Id);
        if (!(Width > 0f && float.IsFinite(Width))) yield return Strings.Format(Strings.ErrorPierWidth, Id);
        if (!(PilingSpacing > 0.5f && float.IsFinite(PilingSpacing))) yield return Strings.Format(Strings.ErrorPierPilingSpacing, Id);
        if (!float.IsFinite(Start.X) || !float.IsFinite(Start.Y) || !float.IsFinite(HeadingDegrees)) yield return Strings.Format(Strings.ErrorPierPosition, Id);
        if (!Enum.IsDefined(Type)) yield return Strings.Format(Strings.ErrorPierUnknownType, Id, Type);
        if (BerthingSides is not (PierSides.Left or PierSides.Right or PierSides.Both)) yield return Strings.Format(Strings.ErrorPierSides, Id);
        if ((Services & ~PierServices.PowerAndWater) != 0) yield return Strings.Format(Strings.ErrorPierUnknownServices, Id, Services);
        if (!(DeckHeight >= 0f) || DeckHeight > 5f) yield return Strings.Format(Strings.ErrorPierDeckHeight, Id);
    }
}

/// <summary>
/// Which side of a pier berths and dividers are generated on, as seen from the pier's <see cref="Pier.Start"/> (usually the shore)
/// looking toward its <see cref="Pier.End"/> (the sea), standing upright on the deck (world +Y up).
/// </summary>
/// <remarks><see cref="Pier.SideNormal"/> gives the plan-view direction of each side.</remarks>
public enum PierSide
{
    /// <summary>
    /// The left-hand side looking from start to end, where <see cref="Pier.LocalX"/> points: +X (east) for a pier with heading 0°
    /// (running south), −X (west) for heading 180°. Generated ids use "L": <c>{PierId}-L01</c>.
    /// </summary>
    Left = 0,

    /// <summary>
    /// The right-hand side looking from start to end, where <see cref="Pier.Right"/> points: −X (west) for a pier with heading 0°
    /// (running south), +X (east) for heading 180°. Generated ids use "R": <c>{PierId}-R01</c>.
    /// </summary>
    Right = 1,
}

/// <summary>Supplies offered at a pier's berths, drawn as pedestals beside the berths (<see cref="Pier.Services"/>).</summary>
[Flags]
public enum PierServices
{
    /// <summary>No pedestals (default).</summary>
    None = 0,

    /// <summary>Electricity: a pedestal with a yellow top.</summary>
    Power = 1,

    /// <summary>Fresh water: a pedestal with a blue top.</summary>
    Water = 2,

    /// <summary>One pedestal supplying both, with a yellow top and a blue band.</summary>
    PowerAndWater = Power | Water,
}

/// <summary>The sides of a pier where boats berth (<see cref="Pier.BerthingSides"/>).</summary>
[Flags]
public enum PierSides
{
    /// <summary>Only the <see cref="PierSide.Left"/> side (left looking from start to end).</summary>
    Left = 1,

    /// <summary>Only the <see cref="PierSide.Right"/> side (right looking from start to end).</summary>
    Right = 2,

    /// <summary>Both sides (default).</summary>
    Both = Left | Right,
}
