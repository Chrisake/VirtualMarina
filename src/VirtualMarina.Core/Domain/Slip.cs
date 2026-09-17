using System.Collections.ObjectModel;
using System.Numerics;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Domain;

/// <summary>
/// A single berth: a water slip along a dock (<see cref="DockId"/>), or a land slip on a <see cref="LandArea"/>
/// (<see cref="LandAreaId"/>) where a boat is stored or maintained ashore. Immutable: the visualizer stores snapshots and hands them out in events,
/// so host code can never change marina state without going through the API.
/// </summary>
/// <remarks>
/// The one deliberate exception is <see cref="ExternalData"/>, a mutable bag shared by every snapshot
/// of the same slip, where host code can keep its own objects.
/// </remarks>
public sealed record Slip
{
    /// <summary>Creates a Free slip at an explicit position, orientation and size.</summary>
    /// <param name="id">Unique id (case-insensitive), typically the ERP berth number.</param>
    /// <param name="dockId">Id of the dock the slip belongs to.</param>
    /// <param name="center">Center of the slip's water area in plan coordinates (X = world X, Y = world Z).</param>
    /// <param name="headingDegrees">Direction a moored boat's bow points, normally toward the dock (0° = +Z, 90° = +X).</param>
    /// <param name="length">Length along the heading, in meters.</param>
    /// <param name="width">Width across the heading, in meters.</param>
    /// <remarks>To place slips relative to a dock, use <see cref="SlipGenerator.AtDock"/> or <see cref="DockBuilder.AddSlips"/>.</remarks>
    /// <example>
    /// <code>
    /// // Left of a 2.5 m wide dock "A" that starts at (0, 0) with heading 0°: bow points +X, toward the dock.
    /// var slip = new Slip("A-L01", "A", new Vector2(-7.75f, 4.75f), headingDegrees: 90, length: 13, width: 5.5f)
    /// {
    ///     Label = "A-1",
    ///     Status = SlipStatus.Occupied,
    ///     Boat = new Boat("B-1", "Aurora", BoatType.MonohullSailboat),
    /// };
    /// </code>
    /// </example>
    public Slip(string id, string dockId, Vector2 center, float headingDegrees, float length, float width)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(dockId);
        Id = id;
        DockId = dockId;
        Center = center;
        HeadingDegrees = headingDegrees;
        Length = length;
        Width = width;
    }

    private Slip(string id, Vector2 center, float headingDegrees, float length, float width)
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
    /// Creates a Free land slip: a spot on a <see cref="LandArea"/> where a boat is stored or maintained ashore
    /// (boatyard, hard standing, dry stack). It is drawn on the land's surface, its boat rests on cradle stands, and it is
    /// selected, colored and updated like any other slip.
    /// </summary>
    /// <param name="id">Unique id (case-insensitive), shared with water slips.</param>
    /// <param name="landAreaId">Id of the land area the slip is on.</param>
    /// <param name="position">Center of the spot in plan coordinates (X = world X, Y = world Z).</param>
    /// <param name="headingDegrees">Direction the stored boat's bow points (0° = +Z, 90° = +X).</param>
    /// <param name="length">Length along the heading, in meters.</param>
    /// <param name="width">Width across the heading, in meters.</param>
    /// <example><code>Slip.OnLand("Y-01", "boatyard", new Vector2(40, -20), headingDegrees: 0, length: 12, width: 5) with { Status = SlipStatus.Occupied, Boat = boat }</code></example>
    public static Slip OnLand(string id, string landAreaId, Vector2 position, float headingDegrees = 0f, float length = 12f, float width = 5f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(landAreaId);
        return new Slip(id, position, headingDegrees, length, width) { LandAreaId = landAreaId, Label = id };
    }

    /// <summary>ERP identifier (unique within the marina, case-insensitive).</summary>
    public string Id { get; init; }

    /// <summary>Id of the dock this slip belongs to; null for a land slip.</summary>
    public string? DockId { get; init; }

    /// <summary>Id of the <see cref="LandArea"/> a land slip is on; null for a water slip along a dock.</summary>
    public string? LandAreaId { get; init; }

    /// <summary>Human-readable label, e.g. "A-12". Falls back to <see cref="Id"/>.</summary>
    public string? Label { get; init; }

    /// <summary>Center of the slip's water area (or land spot) in plan coordinates (X = world X, Y = world Z).</summary>
    public Vector2 Center { get; init; }

    /// <summary>Direction a moored boat's bow points (normally toward the dock).</summary>
    public float HeadingDegrees { get; init; }

    /// <summary>Usable length in meters, along the heading.</summary>
    public float Length { get; init; }

    /// <summary>Usable width in meters, across the heading.</summary>
    public float Width { get; init; }

    /// <summary>Maximum boat draft in meters, if known (shown in the default tooltip).</summary>
    public float? MaxDraft { get; init; }

    /// <summary>Occupancy status; controls the pad, buoy and boat rendering. Default <see cref="SlipStatus.Free"/>.</summary>
    public SlipStatus Status { get; init; } = SlipStatus.Free;

    /// <summary>
    /// The moored boat (Occupied), expected boat (Reserved) or away boat (TemporarilyFree). Always null when Free.
    /// For a slip in a <see cref="MultiSlipBerth"/>, every member slip carries the berth's boat.
    /// </summary>
    public Boat? Boat { get; init; }

    /// <summary>Draw narrow finger piers along both long sides of the slip. Ignored for land slips.</summary>
    public bool HasFingerPiers { get; init; } = true;

    /// <summary>When false the slip is not drawn at all (not even its finger piers) and cannot be interacted with.</summary>
    public bool IsVisible { get; init; } = true;

    /// <summary>
    /// When true the slip is drawn in gray (its boat desaturated) and cannot be hovered, selected,
    /// right-clicked or acted on.
    /// </summary>
    public bool IsDisabled { get; init; }

    /// <summary>When true the slip looks normal and can be selected and show its tooltip, but its actions window does not open.</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>Id of the <see cref="MultiSlipBerth"/> this slip belongs to, if any. Managed by the visualizer.</summary>
    public string? BerthId { get; internal init; }

    /// <summary>Read-only string attributes supplied with the slip definition (e.g. power, water). For mutable host objects use <see cref="ExternalData"/>.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>
    /// Host-owned objects attached to this slip (contract ids, cached ERP records, ...).
    /// The same instance is shared by every snapshot of the slip, so values written from an event handler
    /// are visible in later events and in <c>GetSlip</c>. The visualizer never reads it.
    /// </summary>
    public SlipDataBag ExternalData { get; init; } = new();

    /// <summary><see cref="Label"/> when set, otherwise <see cref="Id"/>. Used for tooltips and water labels.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Id : Label!;

    /// <summary>Visible and not disabled: can be hovered, selected and show a tooltip.</summary>
    public bool IsInteractive => IsVisible && !IsDisabled;

    /// <summary>Interactive and not read-only: its actions window can open.</summary>
    public bool AllowsActions => IsInteractive && !IsReadOnly;

    /// <summary>True for a land slip (<see cref="LandAreaId"/> is set).</summary>
    public bool IsOnLand => LandAreaId is not null;

    /// <summary>True when the slip is part of a <see cref="MultiSlipBerth"/>.</summary>
    public bool IsInMultiSlipBerth => BerthId is not null;

    /// <summary>Spatial boundary of the slip's water area.</summary>
    public OrientedRect Bounds => new(Center, new Vector2(Width, Length), HeadingDegrees);

    /// <summary>Unit plan-view vector a moored boat's bow points along (toward the dock end of the slip).</summary>
    public Vector2 Forward => MarinaMath.HeadingToDirection(HeadingDegrees);

    /// <summary>Unit plan-view vector across the slip: the heading's local +X axis, <c>(cos h, −sin h)</c>. For heading 0° it is +X.</summary>
    public Vector2 Right => MarinaMath.HeadingToRight(HeadingDegrees);

    internal IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) yield return "Slip id must not be empty.";
        if (LandAreaId is not null)
        {
            if (string.IsNullOrWhiteSpace(LandAreaId)) yield return $"Slip '{Id}' has an empty land area id.";
            if (DockId is not null) yield return $"Slip '{Id}' cannot belong to both a dock and a land area.";
        }
        else if (string.IsNullOrWhiteSpace(DockId))
        {
            yield return $"Slip '{Id}' must reference a dock or a land area.";
        }

        if (!(Length > 0f)) yield return $"Slip '{Id}' must have a positive length.";
        if (!(Width > 0f)) yield return $"Slip '{Id}' must have a positive width.";
        if (!float.IsFinite(Center.X) || !float.IsFinite(Center.Y)) yield return $"Slip '{Id}' has a non-finite position.";
        if (!float.IsFinite(HeadingDegrees)) yield return $"Slip '{Id}' has a non-finite heading.";
        if (!Enum.IsDefined(Status)) yield return $"Slip '{Id}' has an unknown status '{Status}'.";
        if (ExternalData is null) yield return $"Slip '{Id}' must have an ExternalData dictionary.";
        if (Boat is not null)
        {
            foreach (var error in Boat.Validate()) yield return $"Slip '{Id}': {error}";
        }
    }
}
