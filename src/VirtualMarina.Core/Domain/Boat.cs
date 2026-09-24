using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Domain;

/// <summary>
/// A vessel assigned to (or expected at) a berth. Immutable: use <c>with</c> expressions to derive changes.
/// </summary>
public sealed record Boat
{
    private readonly float? _length;
    private readonly float? _beam;
    private readonly ValueDictionary _metadata = ValueDictionary.Empty;

    /// <summary>Creates a boat with the type's nominal length and beam (see <see cref="BoatTypeCatalog.GetNominalDimensions"/>).</summary>
    /// <param name="id">ERP identifier of the vessel.</param>
    /// <param name="name">Boat name shown in tooltips.</param>
    /// <param name="type">Category; selects the 3D model.</param>
    /// <example><code>new Boat("B-77", "Aurora", BoatType.MotorYacht) { LengthMeters = 18, BeamMeters = 5, OwnerName = "M. Rossi" }</code></example>
    public Boat(string id, string name, BoatType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        Name = name ?? string.Empty;
        Type = type;
    }

    /// <summary>ERP identifier of the vessel.</summary>
    public string Id { get; init; }

    /// <summary>Boat name shown in tooltips.</summary>
    public string Name { get; init; }

    /// <summary>Category; selects the 3D model, which is scaled to <see cref="LengthMeters"/> × <see cref="BeamMeters"/>.</summary>
    public BoatType Type { get; init; }

    /// <summary>
    /// Length overall in meters. Unless set, it is the nominal length of the current <see cref="Type"/>, so
    /// <c>boat with { Type = BoatType.JetSki }</c> shrinks a boat whose length was never given.
    /// </summary>
    public float LengthMeters
    {
        get => _length ?? BoatTypeCatalog.GetNominalDimensions(Type).Length;
        init => _length = value;
    }

    /// <summary>Beam in meters. Unless set, it is the nominal beam of the current <see cref="Type"/> (see <see cref="LengthMeters"/>).</summary>
    public float BeamMeters
    {
        get => _beam ?? BoatTypeCatalog.GetNominalDimensions(Type).Beam;
        init => _beam = value;
    }

    /// <summary>True when <see cref="LengthMeters"/> was set rather than taken from the type.</summary>
    public bool HasCustomLength => _length.HasValue;

    /// <summary>True when <see cref="BeamMeters"/> was set rather than taken from the type.</summary>
    public bool HasCustomBeam => _beam.HasValue;

    /// <summary>Owner shown in the default tooltip.</summary>
    public string? OwnerName { get; init; }

    /// <summary>Registration number shown in the default tooltip.</summary>
    public string? RegistrationNumber { get; init; }

    /// <summary>For reserved berths: when the boat is expected to arrive. For temporarily free berths: when it returns.</summary>
    public DateTimeOffset? ExpectedArrival { get; init; }

    /// <summary>Free-form ERP attributes carried through to events.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get => _metadata; init => _metadata = ValueDictionary.From(value); }

    /// <summary>Human-readable type name, e.g. "Motor Yacht".</summary>
    public string TypeDisplayName => BoatTypeCatalog.GetDisplayName(Type);

    internal IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) yield return Strings.ErrorBoatIdEmpty;
        if (!Enum.IsDefined(Type)) yield return Strings.Format(Strings.ErrorBoatUnknownType, Id, Type);
        if (!(LengthMeters > 0f && float.IsFinite(LengthMeters))) yield return Strings.Format(Strings.ErrorBoatLength, Id);
        if (!(BeamMeters > 0f && float.IsFinite(BeamMeters))) yield return Strings.Format(Strings.ErrorBoatBeam, Id);
    }
}
