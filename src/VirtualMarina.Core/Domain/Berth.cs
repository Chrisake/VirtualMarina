using System.Numerics;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Domain;

/// <summary>
/// A single berth: a water berth along a pier (<see cref="PierId"/>), or a land berth on a <see cref="LandArea"/>
/// (<see cref="LandAreaId"/>) where a boat is stored or maintained ashore. Immutable: the visualizer stores snapshots and hands them out in events,
/// so host code can never change marina state without going through the API.
/// </summary>
/// <remarks>
/// The one deliberate exception is <see cref="ExternalData"/>, a mutable bag shared by every snapshot
/// of the same berth, where host code can keep its own objects. Equality compares everything else by value (the
/// <see cref="Metadata"/> by its entries) and the bag by reference, so two snapshots of the same berth are equal while
/// two berths built separately, each with a bag of its own, are not.
/// </remarks>
public sealed record Berth
{
    private readonly ValueDictionary _metadata = ValueDictionary.Empty;
    private readonly ValueList<string> _connectedBerthIds = ValueList<string>.Empty;

    /// <summary>Creates a Free berth at an explicit position, orientation and size.</summary>
    /// <param name="id">Unique id (case-insensitive), typically the ERP berth number.</param>
    /// <param name="pierId">Id of the pier the berth belongs to.</param>
    /// <param name="center">Center of the berth's water area in plan coordinates (X = world X, Y = world Z).</param>
    /// <param name="headingDegrees">Direction a moored boat's bow points, normally toward the pier (0° = +Z (south), 90° = +X (east)).</param>
    /// <param name="length">Length along the heading, in meters.</param>
    /// <param name="width">Width across the heading, in meters.</param>
    /// <remarks>To place berths relative to a pier, use <see cref="BerthGenerator.AtPier"/> or <see cref="PierBuilder.AddBerths"/>.</remarks>
    /// <example>
    /// <code>
    /// // Left of a 2.5 m wide pier "A" that starts at (0, 0) with heading 0° (the +X side): bow points −X, toward the pier.
    /// var berth = new Berth("A-L01", "A", new Vector2(7.75f, 4.75f), headingDegrees: -90, length: 13, width: 5.5f)
    /// {
    ///     Label = "A-1",
    ///     Status = BerthStatus.Occupied,
    ///     Boat = new Boat("B-1", "Aurora", BoatType.MonohullSailboat),
    /// };
    /// </code>
    /// </example>
    public Berth(string id, string pierId, Vector2 center, float headingDegrees, float length, float width)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(pierId);
        Id = id;
        PierId = pierId;
        Center = center;
        HeadingDegrees = headingDegrees;
        Length = length;
        Width = width;
    }

    private Berth(string id, Vector2 center, float headingDegrees, float length, float width)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        Center = center;
        HeadingDegrees = headingDegrees;
        Length = length;
        Width = width;
        HasFingerPiers = false;
    }

    /// <summary>
    /// Creates a Free land berth: a spot on a <see cref="LandArea"/> where a boat is stored or maintained ashore
    /// (boatyard, hard standing, dry stack). It is drawn on the land's surface, its boat rests on cradle stands, and it is
    /// selected, colored and updated like any other berth.
    /// </summary>
    /// <param name="id">Unique id (case-insensitive), shared with water berths.</param>
    /// <param name="landAreaId">Id of the land area the berth is on.</param>
    /// <param name="position">Center of the spot in plan coordinates (X = world X, Y = world Z).</param>
    /// <param name="headingDegrees">Direction the stored boat's bow points (0° = +Z (south), 90° = +X (east)).</param>
    /// <param name="length">Length along the heading, in meters.</param>
    /// <param name="width">Width across the heading, in meters.</param>
    /// <example><code>Berth.OnLand("Y-01", "boatyard", new Vector2(40, -20), headingDegrees: 0, length: 12, width: 5) with { Status = BerthStatus.Occupied, Boat = boat }</code></example>
    public static Berth OnLand(string id, string landAreaId, Vector2 position, float headingDegrees = 0f, float length = 12f, float width = 5f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(landAreaId);
        return new Berth(id, position, headingDegrees, length, width) { LandAreaId = landAreaId };
    }

    /// <summary>ERP identifier (unique within the marina, case-insensitive).</summary>
    public string Id { get; init; }

    /// <summary>Id of the pier this berth belongs to; null for a land berth.</summary>
    public string? PierId { get; init; }

    /// <summary>Id of the <see cref="LandArea"/> a land berth is on; null for a water berth along a pier.</summary>
    public string? LandAreaId { get; init; }

    /// <summary>
    /// Human-readable label, e.g. "A-12", or null to show the <see cref="Id"/> (see <see cref="DisplayName"/>). The generators
    /// (<see cref="OnLand"/>, <see cref="BerthGenerator"/>) leave it null, so a later <c>berth with { Id = ... }</c> shows the new id.
    /// </summary>
    public string? Label { get; init; }

    /// <summary>Center of the berth's water area (or land spot) in plan coordinates (X = world X, Y = world Z).</summary>
    public Vector2 Center { get; init; }

    /// <summary>Direction a moored boat's bow points (normally toward the pier).</summary>
    public float HeadingDegrees { get; init; }

    /// <summary>Usable length in meters, along the heading.</summary>
    public float Length { get; init; }

    /// <summary>Usable width in meters, across the heading.</summary>
    public float Width { get; init; }

    /// <summary>Maximum boat draft in meters, if known (shown in the default tooltip).</summary>
    public float? MaxDraft { get; init; }

    /// <summary>Occupancy status; controls the pad, buoy and boat rendering. Default <see cref="BerthStatus.Free"/>.</summary>
    public BerthStatus Status { get; init; } = BerthStatus.Free;

    /// <summary>
    /// The moored boat (Occupied), expected boat (Reserved) or away boat (TemporarilyFree). Null when Free.
    /// For a berth in a <see cref="MultiBerth"/>, every member berth carries the multi-berth's boat.
    /// </summary>
    /// <remarks>
    /// A boat on a Free berth is not an error: the visualizer drops it when the berth is loaded, added or updated, so what it
    /// hands back always has none. <see cref="MarinaLayout.Validate"/> does not report it.
    /// </remarks>
    public Boat? Boat { get; init; }

    /// <summary>Draw narrow finger piers along both long sides of the berth. Ignored for land berths.</summary>
    public bool HasFingerPiers { get; init; } = true;

    /// <summary>When false the berth is not drawn at all (not even its finger piers) and cannot be interacted with.</summary>
    public bool IsVisible { get; init; } = true;

    /// <summary>
    /// When true the berth is drawn in gray (its boat desaturated) and cannot be hovered, selected,
    /// right-clicked or acted on.
    /// </summary>
    public bool IsDisabled { get; init; }

    /// <summary>When true the berth looks normal and can be selected and show its tooltip, but its actions window does not open.</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>
    /// Power and water at this berth, overriding <see cref="Pier.Services"/>. Null (the default) takes whatever the
    /// pier offers, which is what most berths do; set it where one stretch of a pier was upgraded and the rest was not.
    /// </summary>
    /// <remarks>
    /// A pedestal stands between every two berths, so it is drawn when either of the pair asks for it, and offers
    /// what the two of them together ask for.
    /// </remarks>
    /// <example><code>berth with { Services = PierServices.PowerAndWater }</code></example>
    public PierServices? Services { get; init; }

    /// <summary>Id of the <see cref="MultiBerth"/> this berth belongs to, if any. Managed by the visualizer.</summary>
    public string? MultiBerthId { get; internal init; }

    /// <summary>
    /// Ids of the berths right beside this one that a single boat can share it with: the neighbours whose long side faces
    /// this berth's across open water, with no <see cref="Divider"/> (and no finger pier of their own,
    /// <see cref="HasFingerPiers"/>) in between. These are the berths a <see cref="MultiBerth"/> can join to this one.
    /// Managed by the visualizer, which works them out again whenever the berths or dividers change; empty when the berth
    /// has no such neighbour.
    /// </summary>
    /// <remarks>
    /// Connections always come in pairs: when B is in A's list, A is in B's. A marina file carries them too, so an
    /// application that reads the file without a visualizer can offer the same joins; <see cref="MarinaLayout.WithBerthConnections"/>
    /// works them out for a layout built in code. See Docs/05-multi-berths.md for the exact rules.
    /// </remarks>
    public IReadOnlyList<string> ConnectedBerthIds { get => _connectedBerthIds; internal init => _connectedBerthIds = ValueList<string>.From(value); }

    /// <summary>Read-only string attributes supplied with the berth definition (e.g. power, water). For mutable host objects use <see cref="ExternalData"/>.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get => _metadata; init => _metadata = ValueDictionary.From(value); }

    /// <summary>
    /// Host-owned objects attached to this berth (contract ids, cached ERP records, ...).
    /// The same instance is shared by every snapshot of the berth, so values written from an event handler
    /// are visible in later events and in <c>GetBerth</c>. The visualizer never reads it.
    /// </summary>
    public MarinaDataBag ExternalData { get; init; } = [];

    /// <summary><see cref="Label"/> when set, otherwise <see cref="Id"/>. Used for tooltips and water labels.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Id : Label;

    /// <summary>Visible and not disabled: can be hovered, selected and show a tooltip.</summary>
    public bool IsInteractive => IsVisible && !IsDisabled;

    /// <summary>Interactive and not read-only: its actions window can open.</summary>
    public bool AllowsActions => IsInteractive && !IsReadOnly;

    /// <summary>True for a land berth (<see cref="LandAreaId"/> is set).</summary>
    public bool IsOnLand => LandAreaId is not null;

    /// <summary>True when the berth is part of a <see cref="MultiBerth"/>.</summary>
    public bool IsInMultiBerth => MultiBerthId is not null;

    /// <summary>Spatial boundary of the berth's water area.</summary>
    public OrientedRect Bounds => new(Center, new Vector2(Width, Length), HeadingDegrees);

    /// <summary>Unit plan-view vector a moored boat's bow points along (toward the pier end of the berth).</summary>
    public Vector2 Forward => MarinaMath.HeadingToDirection(HeadingDegrees);

    /// <summary>
    /// Unit plan-view vector across the berth: the heading's local +X axis, <c>(cos h, −sin h)</c>, the same as <see cref="LocalX"/>.
    /// For heading 0° it is +X (east).
    /// </summary>
    /// <remarks>
    /// Despite the name this is the <em>left</em>-hand side of someone standing in the berth facing along <see cref="Forward"/>
    /// (world +Y up), and it points the opposite way to <see cref="Pier.Right"/> for the same heading. The name is kept for
    /// compatibility; prefer <see cref="LocalX"/>. See Docs/20-coordinate-conventions.md.
    /// </remarks>
    public Vector2 Right => MarinaMath.HeadingToRight(HeadingDegrees);

    /// <summary>The heading's local +X axis, <c>(cos h, −sin h)</c>: +X (east) for heading 0°. Equal to <see cref="Right"/>.</summary>
    public Vector2 LocalX => MarinaMath.HeadingToRight(HeadingDegrees);

    internal IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) yield return Strings.ErrorBerthIdEmpty;
        foreach (var error in ValidatePlace()) yield return error;

        if (!(Length > 0f && float.IsFinite(Length))) yield return Strings.Format(Strings.ErrorBerthLength, Id);
        if (!(Width > 0f && float.IsFinite(Width))) yield return Strings.Format(Strings.ErrorBerthWidth, Id);
        if (!float.IsFinite(Center.X) || !float.IsFinite(Center.Y)) yield return Strings.Format(Strings.ErrorBerthPosition, Id);
        if (!float.IsFinite(HeadingDegrees)) yield return Strings.Format(Strings.ErrorBerthHeading, Id);
        if (!Enum.IsDefined(Status)) yield return Strings.Format(Strings.ErrorBerthUnknownStatus, Id, Status);
        if (ExternalData is null) yield return Strings.Format(Strings.ErrorBerthNoExternalData, Id);
        if (Boat is not null)
        {
            foreach (var error in Boat.Validate()) yield return Strings.Format(Strings.ErrorBerthBoat, Id, error);
        }
    }

    /// <summary>A berth belongs to exactly one place: a pier, or a land area.</summary>
    private IEnumerable<string> ValidatePlace()
    {
        if (LandAreaId is not null)
        {
            if (string.IsNullOrWhiteSpace(LandAreaId)) yield return Strings.Format(Strings.ErrorBerthEmptyLandAreaId, Id);
            if (PierId is not null) yield return Strings.Format(Strings.ErrorBerthPierAndLand, Id);
        }
        else if (string.IsNullOrWhiteSpace(PierId))
        {
            yield return Strings.Format(Strings.ErrorBerthNoPlace, Id);
        }
    }
}
