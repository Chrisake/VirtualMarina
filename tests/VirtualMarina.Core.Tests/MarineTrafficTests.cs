using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Rendering;
using VirtualMarina.Core.Serialization;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// The passing traffic. A lane is one smooth curve from the edge of the map, in past the marina at the clearance
/// asked for, and out to the far edge; vessels cross it once at the speed their kind really does, and another
/// follows a while after each one leaves.
/// </summary>
public class MarineTrafficTests
{
    private static MarineTraffic Busy(int seed = 1) =>
        MarineTraffic.None with { IsEnabled = true, Clearance = 300f, Seed = seed };

    private static LandArea Quay() =>
        new("quay", new[] { new Vector2(-120, -60), new Vector2(120, -60), new Vector2(120, -10), new Vector2(-120, -10) }, 1f, LandKind.Quay);

    private static (Vector2 Min, Vector2 Max) MarinaBounds() => (new Vector2(-120, -60), new Vector2(120, 40));

    private static Vector2 Centre() => (MarinaBounds().Min + MarinaBounds().Max) * 0.5f;

    /// <summary>A coast running east-west well south of the marina, with the land behind it.</summary>
    private static Shoreline StraightCoast() =>
        new(new[] { new Vector2(-900, -90), new Vector2(900, -90) }, landOnLeft: false);

    /// <summary>The same coast, but turning away to the south-east half way along: a headland to get round.</summary>
    private static Shoreline BentCoast() =>
        new(new[] { new Vector2(-900, -90), new Vector2(0, -90), new Vector2(700, -700) }, landOnLeft: false);

    private static float NearestApproach(IReadOnlyList<TrafficLane> lanes) => lanes.Min(lane => lane.DistanceTo(Centre()));

    [Fact]
    public void EveryLaneIsOneUnbrokenCurve()
    {
        // The lanes used to be the coastline pushed out to sea, which folds over on itself where the coast turns in.
        // That put a right-angle kink in a lane and a six-kilometre jump between its first two points.
        foreach (var shoreline in new Shoreline?[] { null, StraightCoast(), BentCoast() })
        {
            var lanes = MarineTrafficPlanner.Plan(Busy() with { LaneCount = 4 }, MarinaBounds(), shoreline);
            Assert.Equal(4, lanes.Count);

            foreach (var lane in lanes)
            {
                var steps = new List<float>();
                for (var i = 1; i < lane.Points.Count; i++)
                {
                    var step = lane.Points[i] - lane.Points[i - 1];
                    Assert.True(step.Length() > 1e-3f, "a lane has two points on top of each other");
                    steps.Add(step.Length());
                }

                // No jump: the longest step along a lane is nothing like the whole of it.
                Assert.True(steps.Max() < lane.Length * 0.2f, $"a lane jumps {steps.Max():0} m in one step of a {lane.Length:0} m run");

                // And no corner: it turns gently the whole way.
                for (var i = 1; i < lane.Points.Count - 1; i++)
                {
                    var before = Vector2.Normalize(lane.Points[i] - lane.Points[i - 1]);
                    var after = Vector2.Normalize(lane.Points[i + 1] - lane.Points[i]);
                    var turn = MathF.Acos(Math.Clamp(Vector2.Dot(before, after), -1f, 1f)) * MarinaMath.RadToDeg;
                    Assert.True(turn < 8f, $"a lane kinks by {turn:0}° at point {i}");
                }
            }
        }
    }

    [Fact]
    public void TheClearance_IsHowNearTheMiddleOfTheMarinaTheNearestLaneComes()
    {
        foreach (var shoreline in new Shoreline?[] { null, StraightCoast(), BentCoast() })
        {
            foreach (var clearance in new[] { 150f, 300f, 600f, 1000f })
            {
                foreach (var count in new[] { 1, 2, 5 })
                {
                    var traffic = Busy() with { Clearance = clearance, LaneCount = count };
                    var lanes = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), shoreline);

                    Assert.Equal(count, lanes.Count);
                    Assert.Equal(clearance, NearestApproach(lanes), tolerance: clearance * 0.05f);
                }
            }
        }
    }

    [Fact]
    public void TheClearance_DoesNotGrowWithTheSizeOfTheMarina()
    {
        var small = (new Vector2(-50, -50), new Vector2(50, 50));
        var large = (new Vector2(-2000, -2000), new Vector2(2000, 2000));
        var traffic = Busy() with { Clearance = 400f };

        foreach (var shoreline in new Shoreline?[] { null, StraightCoast() })
        {
            var near = MarineTrafficPlanner.Plan(traffic, small, shoreline);
            var far = MarineTrafficPlanner.Plan(traffic, large, shoreline);

            Assert.Equal(400f, near.Min(lane => lane.DistanceTo((small.Item1 + small.Item2) * 0.5f)), tolerance: 20f);
            Assert.Equal(400f, far.Min(lane => lane.DistanceTo((large.Item1 + large.Item2) * 0.5f)), tolerance: 20f);
        }
    }

    [Fact]
    public void TheEdgeClearance_SetsHowFarOffTheCoastALaneLeavesTheMap()
    {
        var shore = StraightCoast();

        foreach (var edge in new[] { 200f, 700f, 2000f })
        {
            var lanes = MarineTrafficPlanner.Plan(Busy() with { EdgeClearance = edge, LaneCount = 1 }, MarinaBounds(), shore);
            var lane = Assert.Single(lanes);

            // Both ends sit that far off where the coast reaches the edge of the map, whatever the middle is
            // doing. Measured from there rather than with DistanceToShore, which only knows the drawn segments and
            // not the endless ones the ends are out on.
            var ends = shore.EndsAtTheMapEdge();
            Assert.NotNull(ends);
            foreach (var corner in new[] { ends!.Value.Start, ends.Value.End })
            {
                var nearest = MathF.Min(Vector2.Distance(corner, lane.Points[0]), Vector2.Distance(corner, lane.Points[^1]));
                Assert.Equal(edge, nearest, tolerance: edge * 0.2f + 5f);
            }

            Assert.False(shore.Contains(lane.Points[0]), "a lane leaves the map on the land side of the coast");
            Assert.False(shore.Contains(lane.Points[^1]), "a lane leaves the map on the land side of the coast");
        }
    }

    [Fact]
    public void ALaneRunsFromOneEdgeOfTheMapToTheOther_PastTheMarina()
    {
        var shore = StraightCoast();
        var lanes = MarineTrafficPlanner.Plan(Busy() with { LaneCount = 2 }, MarinaBounds(), shore);
        var ends = shore.EndsAtTheMapEdge();
        Assert.NotNull(ends);

        foreach (var lane in lanes)
        {
            // Its ends are out where the mainland ends, and its middle comes in to the marina.
            var first = lane.Points[0];
            var last = lane.Points[^1];
            Assert.True(Vector2.Distance(first, Centre()) > 5000f, "a lane starts near the marina rather than at the edge of the map");
            Assert.True(Vector2.Distance(last, Centre()) > 5000f, "a lane ends near the marina rather than at the edge of the map");

            // One end near each end of the coast, so it crosses rather than doubling back.
            var toStart = MathF.Min(Vector2.Distance(first, ends!.Value.Start), Vector2.Distance(first, ends.Value.End));
            Assert.True(toStart < 3000f, "a lane does not reach the edge of the map");
            Assert.True(lane.DistanceTo(Centre()) < 1000f, "a lane never comes in to the marina");
        }
    }

    [Fact]
    public void TheSpacing_IsHowFarApartTheLanesAreOnAverage()
    {
        foreach (var spacing in new[] { 80f, 160f, 400f })
        {
            var traffic = Busy() with { LaneCount = 5, LaneSpacing = spacing };
            var lanes = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), StraightCoast());
            Assert.Equal(5, lanes.Count);

            var steps = new List<float>();
            for (var i = 1; i < lanes.Count; i++)
            {
                steps.Add(lanes[i].DistanceTo(Centre()) - lanes[i - 1].DistanceTo(Centre()));
            }

            // Each step is about the spacing, and the average of them is closer still.
            Assert.All(steps, step => Assert.InRange(step, spacing * 0.7f, spacing * 1.3f));
            Assert.Equal(spacing, steps.Average(), tolerance: spacing * 0.12f);

            // But not identical, or they would be ruled parallel.
            Assert.True(steps.Max() - steps.Min() > 1f, "the lanes are spaced exactly evenly");
        }
    }

    [Fact]
    public void OneOrTwoLanesRunOppositeWays_AndMoreThanThatAreMixed()
    {
        // Two lanes are a separation scheme: one each way.
        var pair = MarineTrafficPlanner.Plan(Busy() with { LaneCount = 2 }, MarinaBounds(), StraightCoast());
        Assert.Equal(new[] { false, true }, pair.Select(lane => lane.Reversed).ToArray());
        Assert.True(Vector2.Dot(pair[0].At(0.5f).Direction, pair[1].At(0.5f).Direction) < -0.9f, "the two lanes run the same way");

        // Beyond that the directions are drawn at random, so over a spread of seeds they are not all alternating.
        var patterns = new HashSet<string>();
        for (var seed = 1; seed <= 12; seed++)
        {
            var lanes = MarineTrafficPlanner.Plan(Busy(seed) with { LaneCount = 5 }, MarinaBounds(), StraightCoast());
            patterns.Add(string.Concat(lanes.Select(lane => lane.Reversed ? "<" : ">")));
        }

        Assert.True(patterns.Count > 2, $"the directions of five lanes only ever came out as {patterns.Count} pattern(s)");
    }

    [Fact]
    public void EachKindOfVesselTravelsAtItsOwnSpeed()
    {
        // The order asked for: a fishing boat plods and a jet ski tears past.
        var order = new[]
        {
            BoatType.FishingBoat,
            BoatType.MonohullSailboat,
            BoatType.CatamaranSailboat,
            BoatType.DayMotorBoat,
            BoatType.CatamaranMotorboat,
            BoatType.Ferry,
            BoatType.MotorYacht,
            BoatType.JetSki,
        };

        for (var i = 1; i < order.Length; i++)
        {
            Assert.True(
                MarineTraffic.CruisingKnots(order[i]) > MarineTraffic.CruisingKnots(order[i - 1]),
                $"{order[i]} is not faster than {order[i - 1]}");
        }

        // The speed setting is a percentage of those, not a replacement for them.
        var traffic = Busy();
        Assert.Equal(100f, traffic.SpeedPercent);
        var half = traffic with { SpeedPercent = 50f };
        foreach (var type in order)
        {
            Assert.Equal(traffic.SpeedMetersPerSecond(type) * 0.5f, half.SpeedMetersPerSecond(type), tolerance: 0.001f);
        }

        Assert.True(traffic.SpeedMetersPerSecond(BoatType.JetSki) > traffic.SpeedMetersPerSecond(BoatType.FishingBoat) * 3f);
    }

    [Fact]
    public void VesselsOfOneKind_VaryAroundTheSpeedTheirKindReallyDoes()
    {
        // One kind only, so what is left is the variation between them.
        var traffic = Busy() with
        {
            LaneCount = 1, MaximumVessels = 40, SpawnDelaySeconds = 1f, Vessels = new[] { BoatType.Ferry },
        };

        var lanes = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), StraightCoast());
        var field = new MarineTrafficField(traffic, lanes);

        // Measure each one over a step, which is exactly how fast it is going.
        var before = field.Vessels.Select(vessel => vessel.Position).ToArray();
        field.Advance(10d);
        var moved = field.Vessels.Take(before.Length)
            .Select((vessel, i) => Vector2.Distance(vessel.Position, before[i]) / 10f)
            .Where(speed => speed > 0.01f)
            .ToArray();

        Assert.True(moved.Length >= 4, "not enough vessels to compare");
        var expected = traffic.SpeedMetersPerSecond(BoatType.Ferry);
        Assert.Equal(expected, moved.Average(), tolerance: expected * 0.12f);
        Assert.True(moved.Max() - moved.Min() > expected * 0.05f, "every vessel of a kind travels at exactly the same speed");
    }

    [Fact]
    public void TheSeaStartsWithARandomNumberOfVessels_AndKeepsItself()
    {
        var traffic = Busy() with { MaximumVessels = 12, SpawnDelaySeconds = 5f, LaneCount = 3 };
        var lanes = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), StraightCoast());

        var counts = new HashSet<int>();
        for (var seed = 1; seed <= 10; seed++)
        {
            var field = new MarineTrafficField(traffic with { Seed = seed }, lanes);
            Assert.InRange(field.Vessels.Count, 1, traffic.MaximumVessels);
            counts.Add(field.Vessels.Count);
        }

        Assert.True(counts.Count > 1, "the sea always starts with exactly the same number of vessels");

        // Left running, it neither empties nor overflows.
        var running = new MarineTrafficField(traffic, lanes);
        for (var step = 0; step < 400; step++)
        {
            running.Advance(30d);
            Assert.InRange(running.Vessels.Count, 0, traffic.MaximumVessels);
        }

        Assert.NotEmpty(running.Vessels);
    }

    [Fact]
    public void AVesselCrossesItsLaneOnce_AndIsReplacedLater()
    {
        // One lane, one vessel allowed, moving quickly: it crosses, leaves, and another follows.
        var traffic = Busy() with
        {
            LaneCount = 1, MaximumVessels = 1, SpeedPercent = 4000f, SpawnDelaySeconds = 30f, Seed = 4,
        };

        var lanes = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), StraightCoast());
        var field = new MarineTrafficField(traffic, lanes);
        Assert.Single(field.Vessels);

        // Followed over a long run: it only ever travels forwards along its lane, it leaves at the end, and after a
        // wait another takes its place.
        var direction = lanes[0].At(0.5f).Direction;
        var previous = field.Vessels[0].Position;
        var travelled = 0f;
        var departures = 0;
        var arrivals = 0;
        var wasThere = true;

        for (var step = 0; step < 2000; step++)
        {
            field.Advance(1d);
            var here = field.Vessels.Count > 0;
            if (wasThere && !here) departures++;
            if (!wasThere && here) arrivals++;

            if (here && wasThere)
            {
                var moved = field.Vessels[0].Position - previous;
                if (moved.Length() > 1f)
                {
                    Assert.True(Vector2.Dot(Vector2.Normalize(moved), direction) > 0.9f, "a vessel turned round");
                    travelled += moved.Length();
                }
            }

            if (here) previous = field.Vessels[0].Position;
            wasThere = here;
        }

        Assert.True(travelled > 5000f, $"the vessel barely moved: {travelled:0} m");
        Assert.True(departures > 0, "no vessel ever reached the end of its lane");
        Assert.True(arrivals > 0, "no replacement ever appeared");
    }

    [Fact]
    public void VesselsFadeInAndOutAtTheEndsOfTheirLane()
    {
        var traffic = Busy() with { LaneCount = 1, MaximumVessels = 1, SpeedPercent = 3000f, SpawnDelaySeconds = 1f, Seed = 7 };
        var lanes = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), StraightCoast());
        var field = new MarineTrafficField(traffic, lanes);

        var seen = new List<(float Opacity, float FromEnd)>();
        for (var step = 0; step < 600; step++)
        {
            field.Advance(2d);
            foreach (var vessel in field.Vessels)
            {
                var fromEnd = MathF.Min(
                    Vector2.Distance(vessel.Position, lanes[0].Points[0]),
                    Vector2.Distance(vessel.Position, lanes[0].Points[^1])) / lanes[0].Length;
                seen.Add((vessel.Opacity, fromEnd));
            }
        }

        Assert.NotEmpty(seen);
        Assert.All(seen, s => Assert.InRange(s.Opacity, 0f, 1f));
        Assert.True(seen.Any(s => s.Opacity < 0.5f), "nothing ever faded");
        Assert.True(seen.Any(s => s.Opacity > 0.99f), "nothing was ever drawn at full strength");

        // Well away from the ends everything is solid, so nobody watches one materialise.
        Assert.All(seen.Where(s => s.FromEnd > 0.05f), s => Assert.True(s.Opacity > 0.99f, $"a vessel {s.FromEnd:0.000} along is only {s.Opacity:0.00} visible"));
    }

    [Fact]
    public void VesselsHoldTheirOwnOffsetWithinTheLane()
    {
        var traffic = Busy() with { LaneCount = 1, MaximumVessels = 20, LaneSpacing = 200f, SpawnDelaySeconds = 1f };
        var lanes = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), StraightCoast());
        var field = new MarineTrafficField(traffic, lanes);

        var offsets = field.Vessels.Select(vessel => lanes[0].DistanceTo(vessel.Position)).ToArray();
        Assert.True(offsets.Length >= 4, "not enough vessels to compare");
        Assert.True(offsets.Max() > 1f, "every vessel rides exactly the middle of the lane");
        Assert.All(offsets, offset => Assert.True(offset < traffic.LaneSpacing * 0.5f, "a vessel sits outside its own lane"));
    }

    [Fact]
    public void SettingsThatMakeNoSense_LeaveNoLanesRatherThanBadOnes()
    {
        Assert.NotEmpty((Busy() with { Clearance = 9000f }).Validate());
        Assert.Empty(MarineTrafficPlanner.Plan(Busy() with { Clearance = 9000f }, MarinaBounds(), null));

        Assert.NotEmpty((Busy() with { LaneCount = 0 }).Validate());
        Assert.NotEmpty((Busy() with { LaneCount = MarineTraffic.LaneLimit + 1 }).Validate());
        Assert.NotEmpty((Busy() with { LaneSpacing = 0f }).Validate());
        Assert.NotEmpty((Busy() with { SpeedPercent = 0f }).Validate());
        Assert.NotEmpty((Busy() with { SpawnDelaySeconds = -1f }).Validate());
        Assert.NotEmpty((Busy() with { MaximumVessels = 0 }).Validate());
        Assert.NotEmpty((Busy() with { MaximumVessels = MarineTraffic.VesselLimit + 1 }).Validate());

        // And switched off there is nothing at all.
        Assert.Empty(MarineTrafficPlanner.Plan(MarineTraffic.None, MarinaBounds(), StraightCoast()));
    }

    [Fact]
    public void TheSameSeed_PutsTheSameTrafficInTheSamePlace()
    {
        var traffic = Busy(seed: 5) with { LaneCount = 3 };
        var lanes = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), StraightCoast());

        static TrafficVessel[] Run(MarineTraffic settings, IReadOnlyList<TrafficLane> on)
        {
            var field = new MarineTrafficField(settings, on);
            for (var step = 0; step < 20; step++) field.Advance(7d);
            return field.Vessels.ToArray();
        }

        Assert.Equal(Run(traffic, lanes), Run(traffic, lanes));
        Assert.NotEqual(Run(traffic, lanes), Run(traffic with { Seed = 6 }, lanes));
    }

    [Fact]
    public void AMarinaWithTraffic_DrawsItAsMovingBoats_AndKeepsTheSettingsThroughAFile()
    {
        var marina = new MarinaVisualizer();
        marina.AddLandArea(Quay());
        marina.SetShoreline(StraightCoast());
        Assert.Same(MarineTraffic.None, marina.MarineTraffic);
        Assert.Empty(marina.GetTrafficVessels());
        Assert.Empty(marina.TrafficLanes);

        var quiet = marina.BuildRenderFrame().Objects.Count;
        marina.SetMarineTraffic(MarineTraffic.None with
        {
            IsEnabled = true, Clearance = 150f, Seed = 3, LaneCount = 3, LaneSpacing = 220f,
            EdgeClearance = 900f, SpeedPercent = 140f, SpawnDelaySeconds = 12f, MaximumVessels = 9,
        });

        Assert.Equal(3, marina.TrafficLanes.Count);
        Assert.NotEmpty(marina.GetTrafficVessels());
        Assert.True(marina.BuildRenderFrame().Objects.Count > quiet, "the traffic was not drawn");

        // The vessels move on their own, without anything being marked dirty.
        var before = marina.GetTrafficVessels()[0].Position;
        marina.Update(60d);
        Assert.NotEqual(before, marina.GetTrafficVessels()[0].Position);

        var reloaded = new MarinaVisualizer();
        MarinaDocument.Parse(MarinaDocument.FromVisualizer(marina, generator: "tests").ToJson()).ApplyTo(reloaded);

        Assert.True(reloaded.MarineTraffic.IsEnabled);
        Assert.Equal(150f, reloaded.MarineTraffic.Clearance);
        Assert.Equal(900f, reloaded.MarineTraffic.EdgeClearance);
        Assert.Equal(140f, reloaded.MarineTraffic.SpeedPercent);
        Assert.Equal(12f, reloaded.MarineTraffic.SpawnDelaySeconds);
        Assert.Equal(9, reloaded.MarineTraffic.MaximumVessels);
        Assert.Equal(3, reloaded.MarineTraffic.LaneCount);
        Assert.Equal(220f, reloaded.MarineTraffic.LaneSpacing);

        // Switching it off empties the sea again.
        marina.SetMarineTraffic(null);
        Assert.Empty(marina.GetTrafficVessels());
        Assert.Empty(marina.TrafficLanes);
        Assert.Equal(quiet, marina.BuildRenderFrame().Objects.Count);
    }

    [Fact]
    public void AFileFromBeforeTheseSettings_StillLoadsWithSensibleOnes()
    {
        var marina = new MarinaVisualizer();
        marina.AddLandArea(Quay());
        marina.SetShoreline(StraightCoast());
        marina.SetMarineTraffic(MarineTraffic.None with { IsEnabled = true, Seed = 2 });

        // A design written when the traffic had none of these has to come back with the defaults, not with zeros
        // that would fail validation and leave an empty sea.
        var json = MarinaDocument.FromVisualizer(marina, generator: "tests").ToJson()
            .Replace("\"edgeClearance\":", "\"edgeClearanceWas\":")
            .Replace("\"speedPercent\":", "\"speedPercentWas\":")
            .Replace("\"spawnDelaySeconds\":", "\"spawnDelaySecondsWas\":");

        var reloaded = new MarinaVisualizer();
        MarinaDocument.Parse(json).ApplyTo(reloaded);

        Assert.Equal(MarineTraffic.None.EdgeClearance, reloaded.MarineTraffic.EdgeClearance);
        Assert.Equal(MarineTraffic.None.SpeedPercent, reloaded.MarineTraffic.SpeedPercent);
        Assert.Equal(MarineTraffic.None.SpawnDelaySeconds, reloaded.MarineTraffic.SpawnDelaySeconds);
        Assert.NotEmpty(reloaded.GetTrafficVessels());
    }

    [Fact]
    public void ShowingTheLanes_DrawsThemAndIsNotSavedWithTheDesign()
    {
        var marina = new MarinaVisualizer();
        marina.AddLandArea(Quay());
        marina.SetShoreline(StraightCoast());
        marina.SetMarineTraffic(MarineTraffic.None with { IsEnabled = true, Clearance = 250f, Seed = 3 });

        Assert.False(marina.ShowTrafficLanes);
        var hidden = marina.BuildRenderFrame().Objects.Count;

        marina.ShowTrafficLanes = true;
        var shown = marina.BuildRenderFrame().Objects.Count;
        Assert.True(shown > hidden, "turning the lanes on drew nothing");

        var json = MarinaDocument.FromVisualizer(marina, generator: "tests").ToJson();
        var reloaded = new MarinaVisualizer();
        MarinaDocument.Parse(json).ApplyTo(reloaded);
        Assert.False(reloaded.ShowTrafficLanes);
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
        marina.SetShoreline(StraightCoast());
        marina.SetMarineTraffic(MarineTraffic.None with { IsEnabled = true, Clearance = 400f, Seed = 2 });
        marina.BuildRenderFrame();

        var before = marina.TrafficLanes[0].DistanceTo(Centre());
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -10), 0f, 320f));
        marina.BuildRenderFrame();

        // The marina grew, so its middle moved and the lanes were laid out again around the new one.
        Assert.NotEmpty(marina.TrafficLanes);
        var after = marina.TrafficLanes[0].DistanceTo((marina.GetLayout().ComputeBounds().Min + marina.GetLayout().ComputeBounds().Max) * 0.5f);
        Assert.Equal(400f, after, tolerance: 40f);
        Assert.NotEqual(before, marina.TrafficLanes[0].DistanceTo(Centre()));
    }
}
