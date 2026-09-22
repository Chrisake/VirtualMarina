using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Rendering;
using VirtualMarina.Core.Serialization;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// The passing traffic out at sea. What matters is that it never sails over anything: the lanes have to keep their
/// distance from the marina, the land and the mainland, whatever seed they come from.
/// </summary>
public class MarineTrafficTests
{
    private static MarineTraffic Busy(int seed = 1) =>
        MarineTraffic.None with { IsEnabled = true, Intensity = 1f, Clearance = 200f, Reach = 1500f, Seed = seed };

    /// <summary>A quay along the south of the water, with a pier sticking out into it.</summary>
    private static LandArea Quay() =>
        new("quay", new[] { new Vector2(-120, -60), new Vector2(120, -60), new Vector2(120, -10), new Vector2(-120, -10) }, 1f, LandKind.Quay);

    private static (Vector2 Min, Vector2 Max) MarinaBounds() => (new Vector2(-120, -60), new Vector2(120, 40));

    [Fact]
    public void EveryLane_KeepsItsDistanceFromTheMarinaAndTheLand()
    {
        var traffic = Busy();
        var land = new[] { Quay() };
        var bounds = MarinaBounds();

        // Several seeds, since the lanes are drawn at random and one lucky seed would prove nothing.
        for (var seed = 1; seed <= 12; seed++)
        {
            var lanes = MarineTrafficPlanner.Plan(traffic with { Seed = seed }, bounds, land, null);
            Assert.NotEmpty(lanes);

            foreach (var lane in lanes)
            {
                for (var step = 0; step <= 200; step++)
                {
                    var point = Vector2.Lerp(lane.Start, lane.End, step / 200f);
                    Assert.False(PolygonMath.Contains(Quay().Points, point), $"seed {seed} runs a lane over the quay");
                    Assert.True(
                        PolygonMath.DistanceToBoundary(Quay().Points, point) >= traffic.Clearance - 1f,
                        $"seed {seed} passes {PolygonMath.DistanceToBoundary(Quay().Points, point):0} m from the quay");
                    Assert.True(DistanceToBox(point, bounds) >= traffic.Clearance - 1f, $"seed {seed} passes too close to the marina");
                }
            }
        }
    }

    [Fact]
    public void ALaneNeverCrossesTheMainland()
    {
        // Land to the south of the coast, so every lane has to stay out in the water to the north.
        var shoreline = new Shoreline(new[] { new Vector2(-900, -5), new Vector2(900, -5) }, landOnLeft: false);
        var lanes = MarineTrafficPlanner.Plan(Busy(seed: 4), MarinaBounds(), new[] { Quay() }, shoreline);

        Assert.NotEmpty(lanes);
        foreach (var lane in lanes)
        {
            for (var step = 0; step <= 200; step++)
            {
                var point = Vector2.Lerp(lane.Start, lane.End, step / 200f);
                Assert.False(shoreline.Contains(point), "a lane runs over the mainland");
                Assert.True(shoreline.DistanceToShore(point) >= 199f, "a lane hugs the shore");
            }
        }
    }

    [Fact]
    public void Intensity_DecidesHowManyVesselsAreOutThere()
    {
        var land = new[] { Quay() };
        var quiet = Busy() with { Intensity = 0.25f };
        var busy = Busy() with { Intensity = 1f };

        Assert.Equal(6, quiet.VesselCount);
        Assert.Equal(busy.MaximumVessels, busy.VesselCount);
        Assert.Equal(0, (busy with { IsEnabled = false }).VesselCount);

        Assert.Equal(quiet.VesselCount, Count(MarineTrafficPlanner.Plan(quiet, MarinaBounds(), land, null)));
        Assert.Equal(busy.VesselCount, Count(MarineTrafficPlanner.Plan(busy, MarinaBounds(), land, null)));

        // Switched off, there is nothing at all.
        Assert.Empty(MarineTrafficPlanner.Plan(MarineTraffic.None, MarinaBounds(), land, null));
    }

    [Fact]
    public void AClearanceBiggerThanTheSea_LeavesNoLanesRatherThanBadOnes()
    {
        // Asking to stay 5 km clear when the lanes only reach 1.5 km out cannot be satisfied.
        var impossible = Busy() with { Clearance = 5000f };

        Assert.NotEmpty(impossible.Validate());
        Assert.Empty(MarineTrafficPlanner.Plan(impossible, MarinaBounds(), new[] { Quay() }, null));
    }

    [Fact]
    public void VesselsMoveAlongTheirLane_AndFadeOutAtBothEnds()
    {
        var traffic = Busy() with { Intensity = 0.2f, SpeedKnots = 20f };
        var lanes = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), new[] { Quay() }, null);

        var first = MarineTrafficPlanner.Place(lanes, traffic, 0d).ToArray();
        var later = MarineTrafficPlanner.Place(lanes, traffic, 30d).ToArray();

        Assert.NotEmpty(first);
        Assert.Equal(first.Length, later.Length);
        Assert.Contains(first.Zip(later), pair => Vector2.Distance(pair.First.Position, pair.Second.Position) > 50f);

        // Every vessel is somewhere on a lane, and its strength is a sensible fade.
        foreach (var vessel in first.Concat(later))
        {
            Assert.InRange(vessel.Opacity, 0f, 1f);
            Assert.Contains(lanes, lane => DistanceToSegment(vessel.Position, lane.Start, lane.End) < 1f);
        }

        // Nothing pops into view: followed right around its lane, a vessel is invisible at the ends and solid in the
        // middle, and its strength tracks how far it is from the nearer end.
        var lane = lanes[0];
        // Vessels carry a speed factor of their own, so a couple of laps at the slowest one covers the whole lane.
        var laps = 3d * lane.Length / (traffic.SpeedMetersPerSecond * 0.75f);
        var samples = Enumerable.Range(0, 600)
            .Select(step => MarineTrafficPlanner.Place(lanes, traffic, step * laps / 599d).First())
            .Select(vessel => (vessel.Opacity, ToEnd: MathF.Min(
                Vector2.Distance(vessel.Position, lane.Start),
                Vector2.Distance(vessel.Position, lane.End)) / lane.Length))
            .ToArray();

        Assert.True(samples.Min(s => s.Opacity) < 0.5f, "a vessel never fades at all at the end of its lane");
        Assert.True(samples.Max(s => s.Opacity) > 0.99f, "a vessel is never drawn at full strength");

        // And the fade is over quickly: a twentieth of the way along the lane — still far out in the flat sea — a
        // vessel is already solid, so nobody watches one materialise.
        Assert.All(
            samples.Where(s => s.ToEnd > 0.05f),
            s => Assert.True(s.Opacity > 0.99f, $"a vessel {s.ToEnd:0.000} along its lane is only {s.Opacity:0.00} visible"));
    }

    [Fact]
    public void TheSameSeed_PutsTheSameTrafficInTheSamePlace()
    {
        var traffic = Busy(seed: 5);
        var land = new[] { Quay() };

        var once = MarineTrafficPlanner.Place(MarineTrafficPlanner.Plan(traffic, MarinaBounds(), land, null), traffic, 12d).ToArray();
        var again = MarineTrafficPlanner.Place(MarineTrafficPlanner.Plan(traffic, MarinaBounds(), land, null), traffic, 12d).ToArray();
        Assert.Equal(once, again);

        var elsewhere = MarineTrafficPlanner.Plan(traffic with { Seed = 6 }, MarinaBounds(), land, null);
        Assert.NotEqual(once, MarineTrafficPlanner.Place(elsewhere, traffic, 12d).ToArray());
    }

    [Fact]
    public void AMarinaWithTraffic_DrawsItAsMovingBoats_AndKeepsTheSettingsThroughAFile()
    {
        var marina = new MarinaVisualizer();
        marina.AddLandArea(Quay());
        Assert.Same(MarineTraffic.None, marina.MarineTraffic);
        Assert.Empty(marina.GetTrafficVessels());

        var quiet = marina.BuildRenderFrame().Objects.Count;
        marina.SetMarineTraffic(MarineTraffic.None with { IsEnabled = true, Intensity = 0.5f, Clearance = 150f, Seed = 3 });

        Assert.True(marina.TrafficLaneCount > 0, "no lane was found room for");
        Assert.NotEmpty(marina.GetTrafficVessels());
        Assert.True(marina.BuildRenderFrame().Objects.Count > quiet, "the traffic was not drawn");

        // The vessels move on their own, without anything being marked dirty.
        var before = marina.GetTrafficVessels()[0].Position;
        marina.Update(20d);
        Assert.NotEqual(before, marina.GetTrafficVessels()[0].Position);

        var reloaded = new MarinaVisualizer();
        MarinaDocument.Parse(MarinaDocument.FromVisualizer(marina, generator: "tests").ToJson()).ApplyTo(reloaded);

        Assert.True(reloaded.MarineTraffic.IsEnabled);
        Assert.Equal(0.5f, reloaded.MarineTraffic.Intensity);
        Assert.Equal(150f, reloaded.MarineTraffic.Clearance);
        Assert.Equal(3, reloaded.MarineTraffic.Seed);

        // Switching it off empties the sea again.
        marina.SetMarineTraffic(null);
        Assert.Empty(marina.GetTrafficVessels());
        Assert.Equal(quiet, marina.BuildRenderFrame().Objects.Count);
    }

    [Fact]
    public void AMarinaWithoutTraffic_WritesNothingAboutIt()
    {
        var marina = new MarinaVisualizer();
        marina.AddLandArea(Quay());

        var json = MarinaDocument.FromVisualizer(marina, generator: "tests").ToJson();
        Assert.DoesNotContain("\"marineTraffic\"", json);

        var reloaded = new MarinaVisualizer();
        MarinaDocument.Parse(json).ApplyTo(reloaded);
        Assert.False(reloaded.MarineTraffic.IsEnabled);
    }

    [Fact]
    public void BuildingTheMarinaOut_MovesTheLanesOutOfTheWay()
    {
        var marina = new MarinaVisualizer();
        marina.AddLandArea(Quay());
        marina.SetMarineTraffic(MarineTraffic.None with { IsEnabled = true, Intensity = 0.4f, Clearance = 120f, Seed = 2 });
        marina.BuildRenderFrame();

        // A long pier reaching out into the water: the lanes have to be replanned around it.
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -10), 0f, 320f));
        marina.BuildRenderFrame();

        var reach = new OrientedRect(new Vector2(0, 150), new Vector2(320f, 6f), 0f).GetAxisAlignedBounds();
        foreach (var vessel in marina.GetTrafficVessels())
        {
            Assert.True(
                DistanceToBox(vessel.Position, reach) >= 100f,
                $"a vessel sails {DistanceToBox(vessel.Position, reach):0} m from the new pier");
        }
    }

    [Fact]
    public void ALaneStartsFarOut_CrossesTheWaterNearTheMarina_AndLeavesOnTheOtherSide()
    {
        const float waterRadius = 2100f;
        var traffic = MarineTraffic.None with
        {
            IsEnabled = true, Intensity = 1f, Clearance = 200f, Reach = 6000f, Seed = 3,
        };

        var lanes = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), new[] { Quay() }, null, waterRadius);
        Assert.NotEmpty(lanes);

        var centre = (MarinaBounds().Min + MarinaBounds().Max) * 0.5f;
        foreach (var lane in lanes)
        {
            // Both ends are far outside the water, so vessels fade in and out where nobody is looking.
            Assert.True(Vector2.Distance(lane.Start, centre) > waterRadius, "a lane starts inside the detailed water");
            Assert.True(Vector2.Distance(lane.End, centre) > waterRadius, "a lane ends inside the detailed water");

            // And it passes close enough to be seen crossing among the waves.
            var nearest = Enumerable.Range(0, 401)
                .Select(step => Vector2.Distance(Vector2.Lerp(lane.Start, lane.End, step / 400f), centre))
                .Min();
            Assert.True(nearest < waterRadius, $"a lane never comes nearer than {nearest:0} m, outside the water");
            Assert.True(nearest >= traffic.Clearance - 1f, $"a lane passes {nearest:0} m from the marina");
        }
    }

    [Fact]
    public void WideningTheWater_PushesWhereVesselsAppear_FurtherOutWithIt()
    {
        // A short reach, but a lot of detailed water to cross: the lane has to grow to still start outside it.
        const float waterRadius = 5000f;
        var traffic = MarineTraffic.None with
        {
            IsEnabled = true, Intensity = 0.5f, Clearance = 200f, Reach = 600f, Seed = 7,
        };

        var lanes = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), new[] { Quay() }, null, waterRadius);
        Assert.NotEmpty(lanes);

        var centre = (MarinaBounds().Min + MarinaBounds().Max) * 0.5f;
        foreach (var lane in lanes)
        {
            Assert.True(Vector2.Distance(lane.Start, centre) > waterRadius, "a vessel appears inside the detailed water");
            Assert.True(Vector2.Distance(lane.End, centre) > waterRadius, "a vessel disappears inside the detailed water");
        }
    }

    [Fact]
    public void MaximumVessels_CapsWhatABusySeaPutsOut()
    {
        var land = new[] { Quay() };
        var traffic = MarineTraffic.None with
        {
            IsEnabled = true, Intensity = 1f, Clearance = 200f, Reach = 3000f, Seed = 1, MaximumVessels = 8,
        };

        Assert.Equal(8, traffic.VesselCount);
        Assert.Equal(8, Count(MarineTrafficPlanner.Plan(traffic, MarinaBounds(), land, null, 1500f)));

        // Half as busy puts out half as many, and raising the cap raises both.
        Assert.Equal(4, (traffic with { Intensity = 0.5f }).VesselCount);
        Assert.Equal(40, (traffic with { MaximumVessels = 40 }).VesselCount);

        // A cap outside the allowed range is refused rather than silently clamped.
        Assert.NotEmpty((traffic with { MaximumVessels = 0 }).Validate());
        Assert.NotEmpty((traffic with { MaximumVessels = MarineTraffic.VesselLimit + 1 }).Validate());
    }

    private static int Count(IReadOnlyList<TrafficLane> lanes) => lanes.Sum(lane => lane.VesselCount);

    private static float DistanceToBox(Vector2 point, (Vector2 Min, Vector2 Max) box) =>
        Vector2.Max(Vector2.Max(box.Min - point, point - box.Max), Vector2.Zero).Length();

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var t = Math.Clamp(Vector2.Dot(point - a, ab) / ab.LengthSquared(), 0f, 1f);
        return Vector2.Distance(point, a + ab * t);
    }
}
