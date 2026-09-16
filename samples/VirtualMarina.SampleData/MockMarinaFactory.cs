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

    /// <summary>Id of the sample's multi-slip berth (a motor yacht moored alongside three slips on dock B).</summary>
    public const string SampleBerthId = "BERTH-B-L10";

    /// <summary>
    /// Seen from the quay looking out to sea, docks A to D run left to right.
    /// Four docks, one of each construction type (A fixed concrete with pile dividers, B floating wooden with a
    /// yacht moored alongside three slips, C floating concrete for multihulls, D floating wooden with booms between
    /// jet ski slips), a quay, a lawn and breakwaters. A few slips are disabled, read-only or hidden.
    /// </summary>
    public static MarinaLayout CreateSampleMarina(int seed = 42)
    {
        var rng = new Random(seed);
        var mixed = new[] { BoatType.MonohullSailboat, BoatType.MonohullSailboat, BoatType.FishingBoat, BoatType.DayMotorBoat };
        var small = new[] { BoatType.DayMotorBoat, BoatType.FishingBoat, BoatType.MonohullSailboat };
        var cats = new[] { BoatType.CatamaranSailboat, BoatType.CatamaranMotorboat };
        var yachts = new[] { BoatType.MotorYacht };
        var personal = new[] { BoatType.JetSki, BoatType.JetSki, BoatType.DayMotorBoat };

        Slip Populate(Slip slip, BoatType[] preferred) => WithRandomOccupancy(slip, rng, preferred);

        const float quayEdge = -6f;
        var layout = new MarinaLayoutBuilder("VirtualMarina Test Harbor")
            .AddLandArea(new LandArea("quay", new OrientedRect(new Vector2(0f, -19f), new Vector2(260f, 26f), 0f), 1.0f, LandKind.Quay))
            .AddLandArea(new LandArea("lawn", new OrientedRect(new Vector2(95f, -24f), new Vector2(50f, 12f), 0f), 1.15f, LandKind.Grass))
            .AddLandArea(new LandArea("breakwater-north", new OrientedRect(new Vector2(30f, 92f), new Vector2(200f, 7f), 0f), 2.2f, LandKind.Breakwater))
            .AddLandArea(new LandArea("breakwater-west", new OrientedRect(new Vector2(-128f, 25f), new Vector2(8f, 80f), 0f), 2.2f, LandKind.Breakwater))
            .AddDock("A", "Dock A (Concrete)", new Vector2(80f, quayEdge), 0f, 72f, dock => dock
                .AddSlips(DockSide.Left, 12, 5.5f, 13f, (_, s) => Populate(s, mixed))
                .AddSlips(DockSide.Right, 12, 5.5f, 13f, (_, s) => Populate(s, mixed), dividers: DividerType.Piles),
                width: 3.5f, type: DockType.Concrete)
            .AddDock("B", "Dock B", new Vector2(35f, quayEdge), 0f, 66f, dock => dock
                .AddSlips(DockSide.Left, 12, 5f, 11f, (_, s) => Populate(s, small))
                .AddSlips(DockSide.Right, 12, 5f, 11f, (_, s) => Populate(s, small)))
            .AddDock("C", "Dock C (Multihulls)", new Vector2(-12f, quayEdge), 0f, 74f, dock => dock
                .AddSlips(DockSide.Left, 8, 8.5f, 15f, (_, s) => Populate(s, cats))
                .AddSlips(DockSide.Right, 8, 8.5f, 15f, (_, s) => Populate(s, cats)), width: 3f, type: DockType.FloatingConcrete)
            .AddDock("D", "Dock D (Superyacht / PWC)", new Vector2(-72f, quayEdge), 0f, 72f, dock => dock
                .AddSlips(DockSide.Left, 9, 7.5f, 23f, (_, s) => Populate(s, yachts))
                .AddSlips(DockSide.Right, 16, 4f, 8f, (_, s) => Populate(s, personal), dividers: DividerType.Boom), width: 3f)
            .AddMultiSlipBerth(new MultiSlipBerth(
                SampleBerthId,
                new[] { "B-L10", "B-L11", "B-L12" },
                new Boat("BT-70001", "Meltemi Star", BoatType.MotorYacht)
                {
                    LengthMeters = 14.5f,
                    BeamMeters = 4.2f,
                    OwnerName = "Aegean Charters Ltd.",
                    RegistrationNumber = "REG-70001",
                },
                SlipStatus.Occupied,
                MooringStyle.Alongside))
            .Build();

        // A few interaction flags to demonstrate: under maintenance, contract locked, not rentable.
        return layout with
        {
            Slips = layout.Slips.Select(s => s.Id switch
            {
                "C-R08" or "C-R07" => s with { IsDisabled = true, Label = $"{s.Id} (maintenance)" },
                "D-L09" or "A-L01" => s with { IsReadOnly = true },
                "B-R12" => s with { IsVisible = false },
                _ => s,
            }).ToArray(),
        };
    }

    /// <summary>Creates a plausible boat that fits the slip, preferring the given types.</summary>
    public static Boat CreateBoatForSlip(Slip slip, Random rng, IReadOnlyList<BoatType>? preferredTypes = null)
    {
        var candidates = (preferredTypes ?? BoatTypeCatalog.All).Where(t => Fits(t, slip)).ToList();
        if (candidates.Count == 0) candidates = BoatTypeCatalog.All.Where(t => Fits(t, slip)).ToList();
        var type = candidates.Count > 0 ? candidates[rng.Next(candidates.Count)] : BoatType.JetSki;

        var nominal = BoatTypeCatalog.GetNominalDimensions(type);
        var length = MathF.Min(nominal.Length * (0.85f + (float)rng.NextDouble() * 0.25f), slip.Length - 1f);
        var beam = MathF.Min(nominal.Beam * length / nominal.Length, slip.Width - 0.6f);
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
    /// Disabled slips and multi-slip berths are left alone.
    /// </summary>
    public static IReadOnlyList<SlipUpdate> CreateRandomActivity(IReadOnlyList<Slip> slips, Random rng, int count)
    {
        var updates = new List<SlipUpdate>();
        foreach (var slip in slips.Where(s => !s.IsDisabled && s.BerthId is null).OrderBy(_ => rng.Next()).Take(count))
        {
            updates.Add(slip.Status switch
            {
                SlipStatus.Occupied => rng.NextDouble() < 0.3
                    ? SlipUpdate.TemporarilyFree(slip.Id, slip.Boat is { } b ? b with { ExpectedArrival = DateTimeOffset.Now.AddDays(rng.Next(2, 21)) } : null)
                    : SlipUpdate.Free(slip.Id),
                SlipStatus.TemporarilyFree => SlipUpdate.Occupy(slip.Id, (slip.Boat ?? CreateBoatForSlip(slip, rng)) with { ExpectedArrival = null }),
                SlipStatus.Reserved => SlipUpdate.Occupy(slip.Id, slip.Boat ?? CreateBoatForSlip(slip, rng)),
                _ => rng.NextDouble() < 0.5
                    ? SlipUpdate.Reserve(slip.Id, CreateBoatForSlip(slip, rng) with { ExpectedArrival = DateTimeOffset.Now.AddHours(rng.Next(1, 48)) })
                    : SlipUpdate.Occupy(slip.Id, CreateBoatForSlip(slip, rng)),
            });
        }

        return updates;
    }

    /// <summary>
    /// A guest berth off the end of a dock, alternating sides. Returns null when the dock end is full (5 berths).
    /// </summary>
    public static Slip? CreateGuestSlip(IMarinaVisualizer marina, string dockId)
    {
        var dock = marina.GetDock(dockId);
        if (dock is null) return null;

        var existing = marina.GetSlipsByDock(dockId).Count(s => s.Id.StartsWith($"{dock.Id}-G", StringComparison.OrdinalIgnoreCase));
        if (existing >= 5) return null;

        const float width = 6f;
        const float length = 14f;
        var index = existing + 1;
        var lateral = index == 1 ? 0f : ((index % 2 == 0 ? 1f : -1f) * width * (index / 2));
        var center = dock.End + dock.Direction * (length * 0.5f) + dock.Right * lateral;
        var id = $"{dock.Id}-G{index:00}";

        return new Slip(id, dock.Id, center, MarinaMath.DirectionToHeading(-dock.Direction), length, width)
        {
            Label = $"{dock.Id} Guest {index}",
            HasFingerPiers = false,
            Metadata = new Dictionary<string, string> { ["Kind"] = "Guest berth" },
        };
    }

    private static Slip WithRandomOccupancy(Slip slip, Random rng, IReadOnlyList<BoatType> preferred)
    {
        var roll = rng.NextDouble();
        var slipWithMeta = slip with
        {
            Metadata = new Dictionary<string, string>
            {
                ["Power"] = rng.NextDouble() < 0.7 ? "32A" : "16A",
                ["Water"] = "Yes",
                ["DailyRate"] = $"{Math.Round(slip.Length * 4.5, 0)} EUR",
            },
            MaxDraft = MathF.Round(2f + (float)rng.NextDouble() * 2f, 1),
        };

        if (roll < 0.55)
        {
            return slipWithMeta with { Status = SlipStatus.Occupied, Boat = CreateBoatForSlip(slip, rng, preferred) };
        }

        if (roll < 0.75)
        {
            var expected = rng.NextDouble() < 0.75
                ? CreateBoatForSlip(slip, rng, preferred) with { ExpectedArrival = DateTimeOffset.Now.AddHours(rng.Next(1, 72)) }
                : null;
            return slipWithMeta with { Status = SlipStatus.Reserved, Boat = expected };
        }

        if (roll < 0.83)
        {
            // Berth holder away cruising: the slip can be let out until they return.
            var away = CreateBoatForSlip(slip, rng, preferred) with { ExpectedArrival = DateTimeOffset.Now.AddDays(rng.Next(2, 21)) };
            return slipWithMeta with { Status = SlipStatus.TemporarilyFree, Boat = away };
        }

        return slipWithMeta;
    }

    private static bool Fits(BoatType type, Slip slip)
    {
        var nominal = BoatTypeCatalog.GetNominalDimensions(type);
        return nominal.Length * 0.8f <= slip.Length - 1f && nominal.Beam * 0.85f <= slip.Width - 0.6f;
    }
}
