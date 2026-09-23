using System.Collections.ObjectModel;

namespace VirtualMarina.Core.Domain;

/// <summary>
/// A vessel assigned to (or expected at) a berth. Immutable: use <c>with</c> expressions to derive changes.
/// </summary>
public sealed record Boat
{
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
        var nominal = BoatTypeCatalog.GetNominalDimensions(type);
        LengthMeters = nominal.Length;
        BeamMeters = nominal.Beam;
    }

    /// <summary>ERP identifier of the vessel.</summary>
    public string Id { get; init; }

    /// <summary>Boat name shown in tooltips.</summary>
    public string Name { get; init; }

    /// <summary>Category; selects the 3D model, which is scaled to <see cref="LengthMeters"/> × <see cref="BeamMeters"/>.</summary>
    public BoatType Type { get; init; }

    /// <summary>Length overall in meters. Defaults to the type's nominal length.</summary>
    public float LengthMeters { get; init; }

    /// <summary>Beam in meters. Defaults to the type's nominal beam.</summary>
    public float BeamMeters { get; init; }

    /// <summary>Owner shown in the default tooltip.</summary>
    public string? OwnerName { get; init; }

    /// <summary>Registration number shown in the default tooltip.</summary>
    public string? RegistrationNumber { get; init; }

    /// <summary>For reserved berths: when the boat is expected to arrive. For temporarily free berths: when it returns.</summary>
    public DateTimeOffset? ExpectedArrival { get; init; }

    /// <summary>Free-form ERP attributes carried through to events.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>Human-readable type name, e.g. "Motor Yacht".</summary>
    public string TypeDisplayName => BoatTypeCatalog.GetDisplayName(Type);

    internal IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) yield return "Boat id must not be empty.";
        if (!Enum.IsDefined(Type)) yield return $"Boat '{Id}' has an unknown type '{Type}'.";
        if (!(LengthMeters > 0f && float.IsFinite(LengthMeters))) yield return $"Boat '{Id}' must have a positive, finite length.";
        if (!(BeamMeters > 0f && float.IsFinite(BeamMeters))) yield return $"Boat '{Id}' must have a positive, finite beam.";
    }
}
