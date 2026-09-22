using System.Numerics;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Geometry;

/// <summary>
/// The traffic actually out on the water: which vessels are on which lanes, where along them they have got to, and
/// when the next one is due.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the lanes, this changes as time passes. A random number of vessels is out there to begin with, scattered
/// along the lanes; each runs its lane once, from the edge of the map to the far edge, and is gone. A little while
/// after one leaves another appears, on a lane of its own choosing, of its own kind, at its own speed and its own
/// offset within the lane — so the sea never repeats itself and never empties.
/// </para>
/// <para>
/// This replaced a fixed set of vessels going round and round their lane forever, where the only thing that changed
/// was where in the loop each one was.
/// </para>
/// </remarks>
public sealed class MarineTrafficField
{
    /// <summary>How much of each end of a lane a vessel spends fading in or out.</summary>
    /// <remarks>
    /// Short on purpose: the ends are far out in the flat sea, and a long fade there is a smear on the horizon
    /// rather than something arriving.
    /// </remarks>
    private const float FadeFraction = 0.02f;

    /// <summary>How far off the middle of its lane a vessel may hold, as a share of the spacing between lanes.</summary>
    private const float OffsetFraction = 0.3f;

    /// <summary>How much a vessel's speed may differ from what its kind normally does, as a share of it.</summary>
    /// <remarks>
    /// Both ways, so the average across the sea is still the real figure while no two of the same kind keep station.
    /// </remarks>
    private const float SpeedVariation = 0.18f;

    /// <summary>How much the wait for the next vessel may differ from the setting, as a share of it.</summary>
    private const float DelayVariation = 0.8f;

    private readonly List<Sailing> _sailings = new();
    private readonly List<TrafficVessel> _vessels = new();

    /// <summary>How long until each vessel that has left the map is replaced.</summary>
    private readonly List<double> _due = new();
    private readonly Random _random;

    /// <summary>Puts a random amount of traffic out on the lanes, ready to move.</summary>
    /// <param name="settings">The traffic settings.</param>
    /// <param name="lanes">The lanes from <see cref="MarineTrafficPlanner.Plan"/>.</param>
    public MarineTrafficField(MarineTraffic settings, IReadOnlyList<TrafficLane> lanes)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(lanes);

        Settings = settings;
        Lanes = lanes;
        _random = new Random(settings.Seed);

        if (Lanes.Count == 0 || settings.VesselCount == 0) return;

        // However many happen to be about, rather than always the most allowed: an empty-ish sea on one opening and
        // a busy one on the next is the point of it.
        var afloat = _random.Next(Math.Max(1, settings.VesselCount / 3), settings.VesselCount + 1);
        for (var i = 0; i < afloat; i++)
        {
            // Already under way, so the marina does not open with a row of vessels sitting on the horizon.
            _sailings.Add(NewSailing((float)_random.NextDouble()));
        }

        Refresh();
    }

    /// <summary>The settings this traffic was laid on.</summary>
    public MarineTraffic Settings { get; }

    /// <summary>The lanes it runs along, nearest the marina first.</summary>
    public IReadOnlyList<TrafficLane> Lanes { get; }

    /// <summary>Where every vessel is now. A snapshot: they have moved on by the next <see cref="Advance"/>.</summary>
    public IReadOnlyList<TrafficVessel> Vessels => _vessels;

    /// <summary>
    /// Moves everything on by a stretch of time: vessels travel, ones that have run out of lane leave, and new ones
    /// appear when their wait is up.
    /// </summary>
    /// <param name="seconds">How long has passed. Zero or less does nothing.</param>
    public void Advance(double seconds)
    {
        if (Lanes.Count == 0 || seconds <= 0d) return;

        // A long jump — a window left in the background, a design just opened — is not worth simulating step by
        // step, and running the spawn queue thousands of times would only churn the random.
        var elapsed = (float)Math.Min(seconds, 600d);

        for (var i = _sailings.Count - 1; i >= 0; i--)
        {
            var sailing = _sailings[i];
            var lane = Lanes[sailing.Lane];
            if (lane.Length < 1f)
            {
                _sailings.RemoveAt(i);
                continue;
            }

            sailing.Along += sailing.Speed * elapsed / lane.Length;
            if (sailing.Along < 1f)
            {
                _sailings[i] = sailing;
                continue;
            }

            // Off the far edge of the map and gone. Another will be along shortly to take its place, which is what
            // keeps the sea at roughly the number it started with rather than filling up to the most allowed.
            _sailings.RemoveAt(i);
            _due.Add(NextDelay());
        }

        for (var i = _due.Count - 1; i >= 0; i--)
        {
            _due[i] -= elapsed;
            if (_due[i] > 0d) continue;

            _due.RemoveAt(i);
            if (_sailings.Count < Settings.VesselCount) _sailings.Add(NewSailing(0f));
        }

        Refresh();
    }

    /// <summary>A new vessel: a lane, a kind, a speed and a place in the lane, all of its own.</summary>
    /// <param name="along">Where on the lane it starts, 0 at the near end.</param>
    private Sailing NewSailing(float along)
    {
        var mix = Settings.EffectiveVessels;
        var type = mix[_random.Next(mix.Count)];
        var variation = 1f + ((float)_random.NextDouble() * 2f - 1f) * SpeedVariation;

        return new Sailing
        {
            Lane = _random.Next(Lanes.Count),
            Type = type,
            Along = along,
            Speed = MathF.Max(0.05f, Settings.SpeedMetersPerSecond(type) * variation),
            Offset = ((float)_random.NextDouble() * 2f - 1f) * OffsetFraction * Settings.LaneSpacing,
        };
    }

    /// <summary>How long to wait before the next vessel turns up.</summary>
    private double NextDelay() =>
        Math.Max(0.5f, Settings.SpawnDelaySeconds * (1f + ((float)_random.NextDouble() * 2f - 1f) * DelayVariation));

    /// <summary>Works out where everything is, ready to be drawn.</summary>
    private void Refresh()
    {
        _vessels.Clear();
        foreach (var sailing in _sailings)
        {
            var lane = Lanes[sailing.Lane];
            var (position, direction) = lane.At(sailing.Along);

            // Held off the middle of the lane, so a lane does not read as a single wire with beads on it.
            var sideways = new Vector2(-direction.Y, direction.X) * sailing.Offset;
            _vessels.Add(new TrafficVessel(
                sailing.Type,
                position + sideways,
                MarinaMath.DirectionToHeading(direction),
                FadeAt(sailing.Along)));
        }
    }

    /// <summary>Full strength along the lane, fading to nothing at both ends so nothing pops in or out.</summary>
    private static float FadeAt(float along)
    {
        var edge = MathF.Min(along, 1f - along);
        return Math.Clamp(edge / FadeFraction, 0f, 1f);
    }

    /// <summary>One vessel's run down one lane.</summary>
    private struct Sailing
    {
        /// <summary>Which lane it is on.</summary>
        public int Lane;

        /// <summary>What kind of vessel it is.</summary>
        public BoatType Type;

        /// <summary>How far along the lane it has got, 0 to 1.</summary>
        public float Along;

        /// <summary>How fast it travels, in meters per second.</summary>
        public float Speed;

        /// <summary>How far off the middle of the lane it holds, in meters.</summary>
        public float Offset;
    }
}
