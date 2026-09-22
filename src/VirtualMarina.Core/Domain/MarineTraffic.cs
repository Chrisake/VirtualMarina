using System.Collections.ObjectModel;
using System.Numerics;

namespace VirtualMarina.Core.Domain;

/// <summary>One vessel of the passing traffic, where it is at a moment in time.</summary>
/// <param name="Type">What kind of vessel it is.</param>
/// <param name="Position">Where it is, in plan coordinates.</param>
/// <param name="HeadingDegrees">Which way its bow points, in the usual compass sense.</param>
/// <param name="Opacity">0–1. Vessels fade in at the start of the path and out at the end.</param>
public readonly record struct TrafficVessel(BoatType Type, Vector2 Position, float HeadingDegrees, float Opacity);

/// <summary>
/// Passing traffic out at sea: vessels running along one line that follows the coast past the marina, fading in far
/// out at one end of it and away again at the other.
/// </summary>
/// <remarks>
/// <para>
/// It is decoration, not layout: the vessels are not berths, cannot be clicked, and are worked out from
/// <see cref="Seed"/> rather than stored, so turning it up costs nothing in the file.
/// </para>
/// <para>
/// <b>Where the path goes.</b> It is the shoreline pushed out to sea: each end runs alongside one of the shoreline's
/// endless segments and the middle curves between them, so the shipping reads as following the coast rather than
/// cutting across it. With no shoreline behind the marina the path is a straight line instead.
/// </para>
/// <para>
/// <b>How near it comes.</b> <see cref="Clearance"/> is the closest the path gets to the middle of the marina, in
/// meters, so the setting means the same thing whatever size the marina is. A path that would cross a land area is
/// pushed further out until it does not, which is the only case where it ends up further away than asked.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// marina.SetMarineTraffic(MarineTraffic.None with { IsEnabled = true, Intensity = 0.4f, Clearance = 400f });
/// </code>
/// </example>
public sealed record MarineTraffic
{
    /// <summary>The most vessels <see cref="MaximumVessels"/> may be set to.</summary>
    public const int VesselLimit = 60;

    /// <summary>One knot in meters per second.</summary>
    private const float KnotsToMetersPerSecond = 0.514444f;

    /// <summary>Empty sea. This is what a marina has until traffic is switched on.</summary>
    public static MarineTraffic None { get; } = new();

    /// <summary>The mix used when <see cref="Vessels"/> is left empty: what is plausibly passing a marina offshore.</summary>
    public static IReadOnlyList<BoatType> DefaultVessels { get; } = new[]
    {
        BoatType.MonohullSailboat,
        BoatType.MonohullSailboat,
        BoatType.CatamaranSailboat,
        BoatType.FishingBoat,
        BoatType.FishingBoat,
        BoatType.MotorYacht,
        BoatType.Ferry,
    };

    /// <summary>Draw the traffic. Default false, so a marina is in empty sea until it is asked for.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>How busy the sea is, 0–1 (default 0.5). Scales the number of vessels up to <see cref="MaximumVessels"/>.</summary>
    public float Intensity { get; init; } = 0.5f;

    /// <summary>
    /// The most vessels on the water at once, 1–<see cref="VesselLimit"/> (default 24). <see cref="Intensity"/> is a
    /// fraction of this, so raising it makes a busy sea busier without touching the setting that says how busy.
    /// </summary>
    public int MaximumVessels { get; init; } = 24;

    /// <summary>
    /// How near the middle of the marina the traffic passes, in meters (default 300): the closest approach of the
    /// path, not a margin added to the size of the marina. Lower it to bring the shipping into view, raise it to put
    /// it out towards the horizon.
    /// </summary>
    public float Clearance { get; init; } = 300f;

    /// <summary>How fast the vessels go, in knots (default 8). They are meant to drift slowly across the view.</summary>
    public float SpeedKnots { get; init; } = 8f;

    /// <summary>
    /// How far the two ends of the path run on past the coast, in meters (default 6000): where a vessel starts and
    /// where it finally fades away. It is deliberately far beyond the detailed water, so vessels appear and disappear
    /// out of sight rather than popping into view at the edge of the waves.
    /// </summary>
    public float Reach { get; init; } = 6000f;

    /// <summary>Keeps the vessels on the path the same between sessions. Any number will do.</summary>
    public int Seed { get; init; } = 1;

    /// <summary>
    /// The kinds of vessel out there, drawn from at random. Repeat a type to make it more common. Empty means
    /// <see cref="DefaultVessels"/>.
    /// </summary>
    public IReadOnlyList<BoatType> Vessels { get; init; } = Array.Empty<BoatType>();

    /// <summary>Read-only string attributes the host application attaches to the traffic. Saved with the design.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>How many vessels this asks for; 0 when it is switched off.</summary>
    public int VesselCount =>
        IsEnabled ? (int)MathF.Round(Math.Clamp(Intensity, 0f, 1f) * Math.Clamp(MaximumVessels, 1, VesselLimit)) : 0;

    /// <summary><see cref="SpeedKnots"/> in meters per second.</summary>
    public float SpeedMetersPerSecond => SpeedKnots * KnotsToMetersPerSecond;

    /// <summary>The mix actually used: <see cref="Vessels"/>, or <see cref="DefaultVessels"/> when that is empty.</summary>
    public IReadOnlyList<BoatType> EffectiveVessels => Vessels.Count > 0 ? Vessels : DefaultVessels;

    /// <summary>Problems that would stop the traffic being drawn, empty when it is sound.</summary>
    public IEnumerable<string> Validate()
    {
        if (!float.IsFinite(Intensity) || Intensity < 0f || Intensity > 1f)
        {
            yield return "Marine traffic intensity must be between 0 and 1.";
        }

        if (MaximumVessels < 1 || MaximumVessels > VesselLimit)
        {
            yield return $"Marine traffic can show between 1 and {VesselLimit} vessels at once.";
        }

        if (!float.IsFinite(Clearance) || Clearance < 0f) yield return "Marine traffic clearance must not be negative.";
        if (!float.IsFinite(SpeedKnots) || SpeedKnots < 0f) yield return "Marine traffic speed must not be negative.";
        if (!float.IsFinite(Reach) || Reach <= 0f) yield return "Marine traffic reach must be a positive distance.";
        if (float.IsFinite(Reach) && float.IsFinite(Clearance) && Reach <= Clearance)
        {
            yield return "Marine traffic reach must be further out than its clearance, or no path fits.";
        }

        foreach (var vessel in Vessels.Where(vessel => !Enum.IsDefined(vessel)).Distinct())
        {
            yield return $"Marine traffic lists an unknown vessel type '{vessel}'.";
        }
    }
}
