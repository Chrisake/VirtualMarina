namespace VirtualMarina.Core.Domain;

/// <summary>Boat categories the visualizer can render. Each has its own procedural model (see <see cref="Geometry.BoatMeshFactory"/>).</summary>
public enum BoatType
{
    /// <summary>Single-hull sailing yacht with mast and sails. Nominal 12 × 4 m.</summary>
    MonohullSailboat = 0,

    /// <summary>Twin-hull sailing catamaran. Nominal 12 × 7 m.</summary>
    CatamaranSailboat = 1,

    /// <summary>Small open or cuddy motor boat. Nominal 7 × 2.5 m.</summary>
    DayMotorBoat = 2,

    /// <summary>Twin-hull power catamaran. Nominal 13 × 6.5 m.</summary>
    CatamaranMotorboat = 3,

    /// <summary>Multi-deck motor yacht. Nominal 20 × 5.5 m.</summary>
    MotorYacht = 4,

    /// <summary>Fishing boat with wheelhouse. Nominal 10 × 3.5 m.</summary>
    FishingBoat = 5,

    /// <summary>Personal watercraft. Nominal 3.2 × 1.2 m.</summary>
    JetSki = 6,
}

/// <summary>Length overall and beam, in meters.</summary>
/// <param name="Length">Length overall.</param>
/// <param name="Beam">Maximum width.</param>
public readonly record struct BoatDimensions(float Length, float Beam);

/// <summary>Reference data for each <see cref="BoatType"/>.</summary>
public static class BoatTypeCatalog
{
    /// <summary>Every boat type, in enum order.</summary>
    public static IReadOnlyList<BoatType> All { get; } = Enum.GetValues<BoatType>();

    /// <summary>
    /// Dimensions the procedural (or imported) model is authored at. Instances are
    /// scaled from these to the boat's actual length and beam. Also the default size of a new <see cref="Boat"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Unknown boat type.</exception>
    public static BoatDimensions GetNominalDimensions(BoatType type) => type switch
    {
        BoatType.MonohullSailboat => new(12f, 4f),
        BoatType.CatamaranSailboat => new(12f, 7f),
        BoatType.DayMotorBoat => new(7f, 2.5f),
        BoatType.CatamaranMotorboat => new(13f, 6.5f),
        BoatType.MotorYacht => new(20f, 5.5f),
        BoatType.FishingBoat => new(10f, 3.5f),
        BoatType.JetSki => new(3.2f, 1.2f),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown boat type."),
    };

    /// <summary>Human-readable name, e.g. "Motor Yacht". Used by the default tooltip.</summary>
    public static string GetDisplayName(BoatType type) => type switch
    {
        BoatType.MonohullSailboat => "Monohull Sailboat",
        BoatType.CatamaranSailboat => "Catamaran Sailboat",
        BoatType.DayMotorBoat => "Day Motor Boat",
        BoatType.CatamaranMotorboat => "Catamaran Motorboat",
        BoatType.MotorYacht => "Motor Yacht",
        BoatType.FishingBoat => "Fishing Boat",
        BoatType.JetSki => "Jet Ski",
        _ => type.ToString(),
    };
}
