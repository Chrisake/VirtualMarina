using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.SampleData;

/// <summary>Deterministic sample marina and simulated ERP activity for the test hosts.</summary>
public static class MockMarinaFactory
{
    private static readonly string[] BoatNames =
    {
        "Sea Breeze", "Blue Horizon", "Wind Dancer", "Salty Dog", "Aurora", "Serenity", "Odyssey",
        "Marlin Chaser", "Poseidon", "Lady Grace", "Wave Runner", "Kalimera", "Nautilus", "Sirocco",
        "Meltemi", "Halcyon", "Starlight", "Reel Deal", "Knot Working", "Seas the Day", "Freedom",
        "Liberty", "Pelagia", "Thalassa", "Zephyr", "Calypso", "Artemis", "Blue Jay", "Sunchaser", "Driftwood",
    };

    private static readonly string[] Owners =
    {
        "J. Papadopoulos", "M. Rossi", "A. Schmidt", "L. Martin", "K. Nielsen", "S. Johnson",
        "E. Garcia", "D. Weber", "N. Costa", "P. Dubois", "R. Evans", "T. Kowalski",
    };

    /// <summary>Id of the sample's multi-berth (a motor yacht moored alongside three berths on pier B).</summary>
    public const string SampleMultiBerthId = "BERTH-B-L10";

    /// <summary>The berths the sample multi-berth spans. Built once rather than per call (CA1861).</summary>
    private static readonly string[] SampleMultiBerthMembers = ["B-L10", "B-L11", "B-L12"];

    /// <summary>Id of the sample's boatyard, a land area with two rows of land berths.</summary>
    public const string BoatyardId = "boatyard";

    /// <summary>Id of the east mole, a land area with a maintenance row of land berths and a single-sided pier along its edge.</summary>
    public const string EastMoleId = "east-mole";

    /// <summary>
    /// Seen from the quay looking out to sea, piers A to D run left to right.
    /// Four piers, one of each construction type (A fixed concrete with pile dividers, B floating wooden with a
    /// yacht moored alongside three berths, C floating concrete for multihulls, D floating wooden with booms between
    /// jet ski berths), plus two single-sided piers: E, a floating concrete pontoon along the east mole, and W, a fixed
    /// concrete quay wall. Land is made of polygons: the quay, a trapezoid boatyard with two rows of land berths, a tapered
    /// east mole with a maintenance row, an irregular lawn, and two curved rubble-mound breakwaters drawn as rocks.
    /// A few berths are disabled, read-only or hidden.
    /// </summary>
    public static MarinaLayout CreateSampleMarina(int seed = 42)
    {
        var rng = new Random(seed);
        var mixed = new[] { BoatType.MonohullSailboat, BoatType.MonohullSailboat, BoatType.FishingBoat, BoatType.DayMotorBoat };
        var small = new[] { BoatType.DayMotorBoat, BoatType.FishingBoat, BoatType.MonohullSailboat };
        var cats = new[] { BoatType.CatamaranSailboat, BoatType.CatamaranMotorboat };
        var yachts = new[] { BoatType.MotorYacht };
        var personal = new[] { BoatType.JetSki, BoatType.JetSki, BoatType.DayMotorBoat };

        Berth Populate(Berth berth, BoatType[] preferred) => WithRandomOccupancy(berth, rng, preferred);

        // The land berths and single-sided piers draw from their own generator, so piers A to D keep their occupancy.
        var extraRng = new Random(unchecked(seed * 31 + 7));
        Berth PopulateExtra(Berth berth, BoatType[] preferred) => WithRandomOccupancy(berth, extraRng, preferred);
        Berth Stored(Berth berth, BoatType[] preferred) => PopulateExtra(berth, preferred) with
        {
            Metadata = new Dictionary<string, string> { ["Kind"] = "Dry storage", ["Power"] = "16A", ["DailyRate"] = $"{Math.Round(berth.Length * 2.0, 0)} EUR" },
        };

        const float quayEdge = -6f;
        const float quayHeight = 1.0f;
        var layout = new MarinaLayoutBuilder("VirtualMarina Test Harbor")
            .AddLandArea(new LandArea("quay", new[] { new Vector2(-130f, -32f), new Vector2(150f, -32f), new Vector2(150f, quayEdge), new Vector2(-130f, quayEdge) }, quayHeight)
            {
                Name = "Main quay",
            })
            // Boatyard south-west of the quay: hard standing with two rows of land berths, bows facing the lane between them.
            .AddLandArea(new LandArea(BoatyardId, new[] { new Vector2(-130f, -32f), new Vector2(-44f, -32f), new Vector2(-60f, -74f), new Vector2(-130f, -74f) }, quayHeight)
            {
                Name = "Boatyard",
            }, yard => yard
                .AddBerths("Y-A", new Vector2(-122f, -40f), rowHeadingDegrees: 90f, count: 9, berthWidth: 5.5f, berthLength: 12f,
                    (_, s) => Stored(s, mixed), gap: 0.8f, boatHeadingDegrees: 180f)
                .AddBerths("Y-B", new Vector2(-122f, -64f), rowHeadingDegrees: 90f, count: 8, berthWidth: 5.5f, berthLength: 12f,
                    (_, s) => Stored(s, small), gap: 0.8f, boatHeadingDegrees: 0f))
            // East mole: a tapered spit with a maintenance row; pier E runs along its west edge.
            .AddLandArea(new LandArea(EastMoleId, new[] { new Vector2(112f, quayEdge), new Vector2(150f, quayEdge), new Vector2(150f, 38f), new Vector2(138f, 50f), new Vector2(112f, 50f) }, quayHeight)
            {
                Name = "East mole (maintenance)",
            }, mole => mole
                .AddBerths("M-", new Vector2(137f, 2f), rowHeadingDegrees: 0f, count: 5, berthWidth: 6.5f, berthLength: 16f,
                    (_, s) => Stored(s, cats), gap: 1f, boatHeadingDegrees: 270f))
            // A park with trees. The trees are generated from a fixed seed, so they stand in the same places every time;
            // a real application would store them with the land area (the designer generates them once, when the lawn is drawn).
            .AddLandArea(new LandArea("lawn", LawnOutline, 1.15f, LandKind.Grass)
            {
                Name = "Park",
                Trees = LandArea.GenerateTrees(LawnOutline, treesPer1000SquareMeters: 12f, new Random(seed + 101)),
            })
            .AddLandArea(new LandArea("breakwater-north", Band(new[]
            {
                new Vector2(-66f, 84f), new Vector2(-10f, 93f), new Vector2(50f, 96f), new Vector2(105f, 90f), new Vector2(140f, 72f),
            }, 9f), 2.2f, LandKind.Breakwater))
            .AddLandArea(new LandArea("breakwater-west", Band(new[]
            {
                new Vector2(-134f, -8f), new Vector2(-137f, 25f), new Vector2(-131f, 52f), new Vector2(-116f, 68f),
            }, 9f), 2.2f, LandKind.Breakwater))
            // Single-sided piers: berths, mooring points and fenders only on the water side.
            .AddPier(new Pier("E", "Pier E (along the mole)", new Vector2(110.75f, quayEdge), 0f, 50f, 2.5f, PierType.FloatingConcrete)
            {
                BerthingSides = PierSides.Right,
                Services = PierServices.PowerAndWater,   // pedestals beside the berths
            }, pier => pier
                .AddBerths(PierSide.Right, 9, 5f, 10f, (_, s) => PopulateExtra(s, small), gap: 0.4f, dividers: DividerType.SinglePile))
            .AddPier(new Pier("W", "Pier W (quay wall)", new Vector2(-119f, quayEdge + 1.5f), 90f, 22f, 3f, PierType.Concrete)
            {
                BerthingSides = PierSides.Right,
            }, pier => pier
                .AddBerths(PierSide.Right, 3, 6f, 12f, (_, s) => PopulateExtra(s, mixed), startOffset: 2f, dividers: DividerType.Piles))
            .AddPier("A", "Pier A (Concrete)", new Vector2(80f, quayEdge), 0f, 72f, pier => pier
                .AddBerths(PierSide.Left, 12, 5.5f, 13f, (_, s) => Populate(s, mixed))
                .AddBerths(PierSide.Right, 12, 5.5f, 13f, (_, s) => Populate(s, mixed), dividers: DividerType.Piles),
                width: 3.5f, type: PierType.Concrete)
            .AddPier("B", "Pier B", new Vector2(35f, quayEdge), 0f, 66f, pier => pier
                .AddBerths(PierSide.Left, 12, 5f, 11f, (_, s) => Populate(s, small))
                .AddBerths(PierSide.Right, 12, 5f, 11f, (_, s) => Populate(s, small)))
            .AddPier("C", "Pier C (Multihulls)", new Vector2(-12f, quayEdge), 0f, 74f, pier => pier
                .AddBerths(PierSide.Left, 8, 8.5f, 15f, (_, s) => Populate(s, cats))
                .AddBerths(PierSide.Right, 8, 8.5f, 15f, (_, s) => Populate(s, cats)), width: 3f, type: PierType.FloatingConcrete)
            .AddPier("D", "Pier D (Superyacht / PWC)", new Vector2(-72f, quayEdge), 0f, 72f, pier => pier
                .AddBerths(PierSide.Left, 9, 7.5f, 23f, (_, s) => Populate(s, yachts))
                .AddBerths(PierSide.Right, 16, 4f, 8f, (_, s) => Populate(s, personal), dividers: DividerType.Boom), width: 3f)
            .AddMultiBerth(new MultiBerth(
                SampleMultiBerthId,
                SampleMultiBerthMembers,
                new Boat("BT-70001", "Meltemi Star", BoatType.MotorYacht)
                {
                    LengthMeters = 14.5f,
                    BeamMeters = 4.2f,
                    OwnerName = "Aegean Charters Ltd.",
                    RegistrationNumber = "REG-70001",
                },
                BerthStatus.Occupied,
                MooringStyle.Alongside))
            // The mainland behind the quay, so the harbor is on a coast rather than adrift in open sea. It follows the
            // quay's water edge and wanders off beyond it; the land is south of the line, under the quay and boatyard.
            .WithShoreline(new Shoreline(
                new[]
                {
                    new Vector2(-900f, -86f),
                    new Vector2(-420f, -34f),
                    new Vector2(-130f, quayEdge),
                    new Vector2(150f, quayEdge),
                    new Vector2(360f, -30f),
                    new Vector2(880f, -104f),
                },
                landOnLeft: false)
            {
                Height = quayHeight,
                Scenery = HinterlandScenery.Countryside,
                ScenerySeed = 41,
            })
            // Passing traffic well out in the bay, so the sea beyond the breakwaters is not empty either.
            .WithMarineTraffic(MarineTraffic.None with
            {
                IsEnabled = true,
                Clearance = 260f,
                LaneCount = 3,
                MaximumVessels = 12,
                Seed = 12,
            })
            .Build();

        // A few interaction flags to demonstrate: under maintenance, contract locked, not rentable.
        return layout with
        {
            Berths = layout.Berths.Select(s => s.Id switch
            {
                "C-R08" or "C-R07" => s with { IsDisabled = true, Label = $"{s.Id} (maintenance)" },
                "D-L09" or "A-L01" => s with { IsReadOnly = true },
                "B-R12" => s with { IsVisible = false },
                "M-05" => s with { IsDisabled = true, Label = "M-05 (lift)" },
                "Y-A09" => s with { IsReadOnly = true },
                _ => s,
            }).ToArray(),
        };
    }

    private static readonly Vector2[] LawnOutline =
    {
        new(68f, -30f), new(100f, -31f), new(122f, -26f), new(118f, -15f), new(92f, -12f), new(72f, -18f),
    };

    /// <summary>A closed outline of the given width around a polyline (a breakwater's crest line).</summary>
    private static Vector2[] Band(Vector2[] centerline, float width)
    {
        var left = new List<Vector2>();
        var right = new List<Vector2>();
        for (var i = 0; i < centerline.Length; i++)
        {
            var prev = centerline[Math.Max(0, i - 1)];
            var next = centerline[Math.Min(centerline.Length - 1, i + 1)];
            var direction = Vector2.Normalize(next - prev);
            var normal = new Vector2(-direction.Y, direction.X) * (width * 0.5f);
            left.Add(centerline[i] + normal);
            right.Add(centerline[i] - normal);
        }

        right.Reverse();
        return left.Concat(right).ToArray();
    }

    /// <summary>The first free, enabled land berth whose spot fits <paramref name="boat"/>, or null.</summary>
    public static Berth? FindFreeLandBerth(IMarinaVisualizer marina, Boat boat)
    {
        ArgumentNullException.ThrowIfNull(marina);
        ArgumentNullException.ThrowIfNull(boat);
        return marina.GetBerths().FirstOrDefault(s => s.IsOnLand && s.Status == BerthStatus.Free && s.AllowsActions && FitsBoat(s, boat));
    }

    /// <summary>The first free, enabled water berth (not in a multi-berth) whose size fits <paramref name="boat"/>, or null.</summary>
    public static Berth? FindFreeWaterBerth(IMarinaVisualizer marina, Boat boat)
    {
        ArgumentNullException.ThrowIfNull(marina);
        ArgumentNullException.ThrowIfNull(boat);
        return marina.GetBerths().FirstOrDefault(s => !s.IsOnLand && s.Status == BerthStatus.Free && s.AllowsActions && s.MultiBerthId is null && FitsBoat(s, boat));
    }

    private static bool FitsBoat(Berth berth, Boat boat) => boat.LengthMeters <= berth.Length - 0.5f && boat.BeamMeters <= berth.Width - 0.3f;

    /// <summary>Creates a plausible boat that fits the berth, preferring the given types.</summary>
    public static Boat CreateBoatForBerth(Berth berth, Random rng, IReadOnlyList<BoatType>? preferredTypes = null)
    {
        ArgumentNullException.ThrowIfNull(berth);
        ArgumentNullException.ThrowIfNull(rng);
        var candidates = (preferredTypes ?? BoatTypeCatalog.All).Where(t => Fits(t, berth)).ToList();
        if (candidates.Count == 0) candidates = BoatTypeCatalog.All.Where(t => Fits(t, berth)).ToList();
        var type = candidates.Count > 0 ? candidates[rng.Next(candidates.Count)] : BoatType.JetSki;

        var nominal = BoatTypeCatalog.GetNominalDimensions(type);
        var length = MathF.Min(nominal.Length * (0.85f + (float)rng.NextDouble() * 0.25f), berth.Length - 1f);
        var beam = MathF.Min(nominal.Beam * length / nominal.Length, berth.Width - 0.6f);
        var number = rng.Next(10000, 99999);

        return new Boat($"BT-{number}", BoatNames[rng.Next(BoatNames.Length)], type)
        {
            LengthMeters = MathF.Round(MathF.Max(1f, length), 1),
            BeamMeters = MathF.Round(MathF.Max(0.5f, beam), 1),
            OwnerName = Owners[rng.Next(Owners.Length)],
            RegistrationNumber = $"REG-{number}",
        };
    }

    /// <summary>
    /// Simulated ERP traffic: departures (Occupied → Free, or → Temporarily Free when the owner goes cruising), returns and
    /// arrivals (Temporarily Free / Reserved → Occupied) and new bookings (Free → Reserved/Occupied).
    /// Disabled berths and multi-berths are left alone.
    /// </summary>
    public static IReadOnlyList<BerthUpdate> CreateRandomActivity(IReadOnlyList<Berth> berths, Random rng, int count)
    {
        ArgumentNullException.ThrowIfNull(berths);
        ArgumentNullException.ThrowIfNull(rng);

        var updates = new List<BerthUpdate>();
        foreach (var berth in berths.Where(s => !s.IsDisabled && s.MultiBerthId is null).OrderBy(_ => rng.Next()).Take(count))
        {
            updates.Add(berth.Status switch
            {
                BerthStatus.Occupied => rng.NextDouble() < 0.3
                    ? BerthUpdate.TemporarilyFree(berth.Id, berth.Boat is { } b ? b with { ExpectedArrival = DateTimeOffset.Now.AddDays(rng.Next(2, 21)) } : null)
                    : BerthUpdate.Free(berth.Id),
                BerthStatus.TemporarilyFree => BerthUpdate.Occupy(berth.Id, (berth.Boat ?? CreateBoatForBerth(berth, rng)) with { ExpectedArrival = null }),
                BerthStatus.Reserved => BerthUpdate.Occupy(berth.Id, berth.Boat ?? CreateBoatForBerth(berth, rng)),
                _ => rng.NextDouble() < 0.5
                    ? BerthUpdate.Reserve(berth.Id, CreateBoatForBerth(berth, rng) with { ExpectedArrival = DateTimeOffset.Now.AddHours(rng.Next(1, 48)) })
                    : BerthUpdate.Occupy(berth.Id, CreateBoatForBerth(berth, rng)),
            });
        }

        return updates;
    }

    /// <summary>
    /// A guest berth off the end of a pier, alternating sides. Returns null when the pier end is full (5 berths).
    /// </summary>
    public static Berth? CreateGuestBerth(IMarinaVisualizer marina, string pierId)
    {
        ArgumentNullException.ThrowIfNull(marina);
        var pier = marina.GetPier(pierId);
        if (pier is null) return null;

        var existing = marina.GetBerthsByPier(pierId).Count(s => s.Id.StartsWith($"{pier.Id}-G", StringComparison.OrdinalIgnoreCase));
        if (existing >= 5) return null;

        const float width = 6f;
        const float length = 14f;
        var index = existing + 1;
        var lateral = index == 1 ? 0f : ((index % 2 == 0 ? 1f : -1f) * width * (index / 2));
        var center = pier.End + pier.Direction * (length * 0.5f) + pier.Right * lateral;
        var id = $"{pier.Id}-G{index:00}";

        return new Berth(id, pier.Id, center, MarinaMath.DirectionToHeading(-pier.Direction), length, width)
        {
            Label = $"{pier.Id} Guest {index}",
            HasFingerPiers = false,
            Metadata = new Dictionary<string, string> { ["Kind"] = "Guest berth" },
        };
    }

    private static Berth WithRandomOccupancy(Berth berth, Random rng, IReadOnlyList<BoatType> preferred)
    {
        var roll = rng.NextDouble();
        var berthWithMeta = berth with
        {
            Metadata = new Dictionary<string, string>
            {
                ["Power"] = rng.NextDouble() < 0.7 ? "32A" : "16A",
                ["Water"] = "Yes",
                ["DailyRate"] = $"{Math.Round(berth.Length * 4.5, 0)} EUR",
            },
            MaxDraft = MathF.Round(2f + (float)rng.NextDouble() * 2f, 1),
        };

        if (roll < 0.55)
        {
            return berthWithMeta with { Status = BerthStatus.Occupied, Boat = CreateBoatForBerth(berth, rng, preferred) };
        }

        if (roll < 0.75)
        {
            var expected = rng.NextDouble() < 0.75
                ? CreateBoatForBerth(berth, rng, preferred) with { ExpectedArrival = DateTimeOffset.Now.AddHours(rng.Next(1, 72)) }
                : null;
            return berthWithMeta with { Status = BerthStatus.Reserved, Boat = expected };
        }

        if (roll < 0.83)
        {
            // Berth holder away cruising: the berth can be let out until they return.
            var away = CreateBoatForBerth(berth, rng, preferred) with { ExpectedArrival = DateTimeOffset.Now.AddDays(rng.Next(2, 21)) };
            return berthWithMeta with { Status = BerthStatus.TemporarilyFree, Boat = away };
        }

        return berthWithMeta;
    }

    private static bool Fits(BoatType type, Berth berth)
    {
        var nominal = BoatTypeCatalog.GetNominalDimensions(type);
        return nominal.Length * 0.8f <= berth.Length - 1f && nominal.Beam * 0.85f <= berth.Width - 0.6f;
    }
}
