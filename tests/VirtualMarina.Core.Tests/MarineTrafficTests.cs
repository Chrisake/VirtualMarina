using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Rendering;
using VirtualMarina.Core.Serialization;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// The passing traffic out at sea. What matters is that the settings mean what they say — the clearance is the
/// nearest lane's closest approach to the middle of the marina and nothing else, the spacing is the gap between
/// lanes — that the lanes follow the coast without sailing over the land, and that nothing on them can meet
/// head-on.
/// </summary>
public class MarineTrafficTests
{
    private static MarineTraffic Busy(int seed = 1) =>
        MarineTraffic.None with { IsEnabled = true, Intensity = 1f, Clearance = 200f, Reach = 1500f, Seed = seed };

    /// <summary>A quay along the south of the water, with a pier sticking out into it.</summary>
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

    /// <summary>How near the nearest lane comes to the middle of the marina.</summary>
    private static float NearestApproach(IReadOnlyList<TrafficLane> lanes) => lanes.Min(lane => lane.DistanceTo(Centre()));

    [Fact]
    public void TheClearance_IsHowNearTheMiddleOfTheMarinaTheNearestLaneComes()
    {
        // The whole point of the setting: 300 m means 300 m, whether or not there is a coast to follow, and whether
        // there is one lane out there or six.
        foreach (var shoreline in new Shoreline?[] { null, StraightCoast(), BentCoast() })
        {
            foreach (var clearance in new[] { 150f, 300f, 600f, 1000f })
            {
                foreach (var count in new[] { 1, 2, 5 })
                {
                    var traffic = Busy() with { Clearance = clearance, Reach = 6000f, LaneCount = count };
                    var lanes = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), new[] { Quay() }, shoreline);

                    Assert.NotEmpty(lanes);
                    Assert.Equal(count, lanes.Count);
                    Assert.Equal(clearance, NearestApproach(lanes), tolerance: clearance * 0.05f);
                }
            }
        }
    }

    [Fact]
    public void TheClearance_DoesNotGrowWithTheSizeOfTheMarina()
    {
        // The old planner added half the marina's diagonal to the clearance, so a large marina pushed the shipping
        // out of sight however low the setting was.
        var small = (new Vector2(-50, -50), new Vector2(50, 50));
        var large = (new Vector2(-2000, -2000), new Vector2(2000, 2000));
        var traffic = Busy() with { Clearance = 400f, Reach = 9000f };

        foreach (var shoreline in new Shoreline?[] { null, StraightCoast() })
        {
            var near = MarineTrafficPlanner.Plan(traffic, small, Array.Empty<LandArea>(), shoreline);
            var far = MarineTrafficPlanner.Plan(traffic, large, Array.Empty<LandArea>(), shoreline);

            Assert.NotEmpty(near);
            Assert.NotEmpty(far);
            Assert.Equal(400f, near.Min(lane => lane.DistanceTo((small.Item1 + small.Item2) * 0.5f)), tolerance: 20f);
            Assert.Equal(400f, far.Min(lane => lane.DistanceTo((large.Item1 + large.Item2) * 0.5f)), tolerance: 20f);
        }
    }

    [Fact]
    public void TheSpacing_IsHowFarApartTheLanesAre()
    {
        foreach (var shoreline in new Shoreline?[] { null, StraightCoast(), BentCoast() })
        {
            foreach (var spacing in new[] { 80f, 160f, 400f })
            {
                var traffic = Busy() with { Clearance = 300f, Reach = 6000f, LaneCount = 4, LaneSpacing = spacing };
                var lanes = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), new[] { Quay() }, shoreline);
                Assert.Equal(4, lanes.Count);

                // Each lane in turn is one spacing further out than the one before it.
                for (var i = 1; i < lanes.Count; i++)
                {
                    var step = lanes[i].DistanceTo(Centre()) - lanes[i - 1].DistanceTo(Centre());
                    Assert.Equal(spacing, step, tolerance: spacing * 0.25f);
                }

                // And no two lanes ever touch, however they bulge.
                for (var i = 1; i < lanes.Count; i++)
                {
                    var gap = lanes[i - 1].Points.Min(point => lanes[i].DistanceTo(point));
                    Assert.True(gap > spacing * 0.5f, $"lanes {i - 1} and {i} come within {gap:0} m at a spacing of {spacing:0} m");
                }
            }
        }
    }

    [Fact]
    public void NeighbouringLanes_RunOppositeWays_SoNothingMeetsHeadOn()
    {
        var traffic = Busy() with { Clearance = 300f, Reach = 6000f, LaneCount = 4, Intensity = 1f };
        var lanes = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), new[] { Quay() }, StraightCoast());
        Assert.Equal(4, lanes.Count);

        // Every other lane is turned round.
        Assert.Equal(new[] { false, true, false, true }, lanes.Select(lane => lane.Reversed).ToArray());
        for (var i = 1; i < lanes.Count; i++)
        {
            var before = lanes[i - 1].At(0.5f).Direction;
            var after = lanes[i].At(0.5f).Direction;
            Assert.True(Vector2.Dot(before, after) < -0.9f, $"lanes {i - 1} and {i} run the same way");
        }

        // And within a lane everything goes the same way, at every moment, so nothing on it can ever meet head-on.
        foreach (var seconds in new[] { 0d, 45d, 400d })
        {
            foreach (var lane in lanes)
            {
                var headings = MarineTrafficPlanner.Place(new[] { lane }, traffic, seconds)
                    .Select(vessel => MarinaMath.HeadingToDirection(vessel.HeadingDegrees))
                    .ToArray();

                Assert.NotEmpty(headings);
                Assert.All(headings, heading => Assert.True(
                    Vector2.Dot(heading, headings[0]) > 0.8f,
                    "two vessels in one lane are heading at each other"));
            }
        }
    }

    [Fact]
    public void VesselsHoldTheirOwnOffset_AndWanderAcrossTheirLaneAsTheyGo()
    {
        var traffic = Busy() with { Clearance = 300f, Reach = 6000f, LaneCount = 2, LaneSpacing = 200f, SpeedKnots = 0.001f };
        var lanes = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), new[] { Quay() }, StraightCoast());
        Assert.NotEmpty(lanes);

        // Barely moving along the lane, so what is left is the wander across it.
        var offsets = Enumerable.Range(0, 120)
            .Select(step => MarineTrafficPlanner.Place(lanes, traffic, step * 2d).First())
            .Select(vessel => lanes.Min(lane => lane.DistanceTo(vessel.Position)))
            .ToArray();

        Assert.True(offsets.Max() - offsets.Min() > 4f, "a vessel holds exactly to its line and never wanders");
        Assert.True(offsets.Max() < traffic.LaneSpacing * 0.5f, "a vessel wanders out of its own lane");

        // Two vessels do not wander in step, or the whole lane would just breathe in and out together.
        var pair = Enumerable.Range(0, 120)
            .Select(step => MarineTrafficPlanner.Place(lanes, traffic, step * 2d).Take(2).ToArray())
            .Where(both => both.Length == 2)
            .Select(both => lanes.Min(l => l.DistanceTo(both[0].Position)) - lanes.Min(l => l.DistanceTo(both[1].Position)))
            .ToArray();
        Assert.True(pair.Max() - pair.Min() > 1f, "every vessel wanders in lockstep with every other");
    }

    [Fact]
    public void TheLanesRunAlongsideTheCoast_WithTheirEndsParallelToTheEndlessSegments()
    {
        var shore = BentCoast();
        var lanes = MarineTrafficPlanner.Plan(Busy() with { Reach = 6000f, LaneCount = 3 }, MarinaBounds(), new[] { Quay() }, shore);
        Assert.Equal(3, lanes.Count);

        var coastStart = Vector2.Normalize(shore.Points[1] - shore.Points[0]);
        var coastEnd = Vector2.Normalize(shore.Points[^1] - shore.Points[^2]);

        foreach (var lane in lanes)
        {
            // A reversed lane is stored back to front, so compare it the way it was built.
            var points = lane.Reversed ? lane.Points.Reverse().ToArray() : lane.Points.ToArray();

            // The first segment runs the way the coast's first endless segment runs, and the last one the way the
            // last does — so the traffic arrives along the coast rather than out of the open sea at an angle.
            var first = Vector2.Normalize(points[1] - points[0]);
            var last = Vector2.Normalize(points[^1] - points[^2]);
            Assert.Equal(1f, Vector2.Dot(first, coastStart), tolerance: 0.001f);
            Assert.Equal(1f, Vector2.Dot(last, coastEnd), tolerance: 0.001f);

            // In between it bends: the direction at the far end is nothing like the direction at the near one.
            Assert.True(Vector2.Dot(first, last) < 0.9f, "a lane never turns with the coast");

            // And the turn is taken as a curve, not a corner: no single joint changes direction sharply.
            for (var i = 1; i < points.Length - 1; i++)
            {
                var before = Vector2.Normalize(points[i] - points[i - 1]);
                var after = Vector2.Normalize(points[i + 1] - points[i]);
                Assert.True(Vector2.Dot(before, after) > 0.86f, $"a lane kinks by {MathF.Acos(Vector2.Dot(before, after)) * MarinaMath.RadToDeg:0}° at point {i}");
            }
        }
    }

    [Fact]
    public void TheLanesAreNotQuiteParallel()
    {
        var traffic = Busy() with { Clearance = 300f, Reach = 6000f, LaneCount = 3, LaneSpacing = 200f };
        var lanes = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), new[] { Quay() }, StraightCoast());
        Assert.Equal(3, lanes.Count);

        // The gap between two lanes opens and closes along their length rather than holding one figure.
        var gaps = lanes[0].Points
            .Skip(1).SkipLast(1)
            .Select(point => lanes[1].DistanceTo(point))
            .ToArray();

        Assert.True(gaps.Max() - gaps.Min() > 5f, "the lanes are exactly parallel");
        Assert.True(gaps.Max() - gaps.Min() < traffic.LaneSpacing * 0.5f, "the lanes wander so far apart they no longer read as lanes");
    }

    [Fact]
    public void TheLanesNeverCrossTheMainlandOrTheLand()
    {
        var shore = BentCoast();
        var lanes = MarineTrafficPlanner.Plan(Busy() with { Reach = 4000f, LaneCount = 4 }, MarinaBounds(), new[] { Quay() }, shore);
        Assert.NotEmpty(lanes);

        foreach (var lane in lanes)
        {
            foreach (var point in Walk(lane, 800))
            {
                Assert.False(shore.Contains(point), $"a lane runs over the mainland at {point}");
                Assert.False(PolygonMath.Contains(Quay().Points, point), $"a lane runs over the quay at {point}");
            }
        }
    }

    [Fact]
    public void LanesThatWouldCrossAQuay_ArePushedOutUntilTheyDoNot()
    {
        // A breakwater reaching a long way out to sea, right where a 150 m clearance would put the shipping.
        var breakwater = new LandArea(
            "breakwater",
            new[] { new Vector2(-15, 0), new Vector2(15, 0), new Vector2(15, 900), new Vector2(-15, 900) },
            1f,
            LandKind.Quay);

        var traffic = Busy() with { Clearance = 150f, Reach = 4000f, LaneCount = 3 };
        var lanes = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), new[] { breakwater }, StraightCoast());

        Assert.NotEmpty(lanes);
        foreach (var lane in lanes)
        {
            foreach (var point in Walk(lane, 800))
            {
                Assert.False(PolygonMath.Contains(breakwater.Points, point), "the traffic sails through the breakwater");
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

        // More lanes share out the same vessels rather than multiplying them.
        var spread = busy with { LaneCount = 5 };
        var lanes = MarineTrafficPlanner.Plan(spread, MarinaBounds(), land, null);
        Assert.Equal(busy.VesselCount, Count(lanes));
        Assert.All(lanes, lane => Assert.True(lane.VesselCount > 0, "a lane was laid out with nothing on it"));

        // Switched off, there is nothing at all.
        Assert.Empty(MarineTrafficPlanner.Plan(MarineTraffic.None, MarinaBounds(), land, null));
    }

    [Fact]
    public void SettingsThatMakeNoSense_LeaveNoLanesRatherThanBadOnes()
    {
        // Asking to stay 5 km clear when the lanes only reach 1.5 km out cannot be satisfied.
        Assert.NotEmpty((Busy() with { Clearance = 5000f }).Validate());
        Assert.Empty(MarineTrafficPlanner.Plan(Busy() with { Clearance = 5000f }, MarinaBounds(), new[] { Quay() }, null));

        // Nor can a lane count outside what is allowed, or a spacing of nothing.
        Assert.NotEmpty((Busy() with { LaneCount = 0 }).Validate());
        Assert.NotEmpty((Busy() with { LaneCount = MarineTraffic.LaneLimit + 1 }).Validate());
        Assert.NotEmpty((Busy() with { LaneSpacing = 0f }).Validate());
        Assert.Empty(MarineTrafficPlanner.Plan(Busy() with { LaneSpacing = 0f }, MarinaBounds(), new[] { Quay() }, null));
    }

    [Fact]
    public void VesselsMoveAlongTheirLane_AndFadeOutAtBothEnds()
    {
        var traffic = Busy() with { Intensity = 0.2f, SpeedKnots = 20f, LaneCount = 1 };
        var lanes = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), new[] { Quay() }, StraightCoast());
        Assert.Single(lanes);

        var first = MarineTrafficPlanner.Place(lanes, traffic, 0d).ToArray();
        var later = MarineTrafficPlanner.Place(lanes, traffic, 30d).ToArray();

        Assert.NotEmpty(first);
        Assert.Equal(first.Length, later.Length);
        Assert.Contains(first.Zip(later), pair => Vector2.Distance(pair.First.Position, pair.Second.Position) > 50f);

        // Every vessel is within its lane, and its strength is a sensible fade.
        foreach (var vessel in first.Concat(later))
        {
            Assert.InRange(vessel.Opacity, 0f, 1f);
            Assert.True(lanes[0].DistanceTo(vessel.Position) < traffic.LaneSpacing * 0.5f, "a vessel is outside its lane");
        }

        // Nothing pops into view: followed right around the lane, a vessel is invisible at the ends and solid in the
        // middle, and its strength tracks how far it is from the nearer end.
        var lane = lanes[0];
        var laps = 3d * lane.Length / (traffic.SpeedMetersPerSecond * 0.8f);
        var samples = Enumerable.Range(0, 600)
            .Select(step => MarineTrafficPlanner.Place(lanes, traffic, step * laps / 599d).First())
            .Select(vessel => (vessel.Opacity, ToEnd: MathF.Min(
                Vector2.Distance(vessel.Position, lane.Points[0]),
                Vector2.Distance(vessel.Position, lane.Points[^1])) / lane.Length))
            .ToArray();

        Assert.True(samples.Min(s => s.Opacity) < 0.5f, "a vessel never fades at all at the end of its lane");
        Assert.True(samples.Max(s => s.Opacity) > 0.99f, "a vessel is never drawn at full strength");

        // And the fade is over quickly: a twentieth of the way along — still far out in the flat sea — a vessel is
        // already solid, so nobody watches one materialise.
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
        Assert.Empty(marina.TrafficLanes);

        var quiet = marina.BuildRenderFrame().Objects.Count;
        marina.SetMarineTraffic(MarineTraffic.None with
        {
            IsEnabled = true, Intensity = 0.5f, Clearance = 150f, Seed = 3, LaneCount = 3, LaneSpacing = 220f,
        });

        Assert.Equal(3, marina.TrafficLanes.Count);
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
        Assert.Equal(3, reloaded.MarineTraffic.LaneCount);
        Assert.Equal(220f, reloaded.MarineTraffic.LaneSpacing);
        Assert.Equal(3, reloaded.MarineTraffic.Seed);

        // Switching it off empties the sea again.
        marina.SetMarineTraffic(null);
        Assert.Empty(marina.GetTrafficVessels());
        Assert.Empty(marina.TrafficLanes);
        Assert.Equal(quiet, marina.BuildRenderFrame().Objects.Count);
    }

    [Fact]
    public void AFileFromBeforeTheLanes_StillLoadsWithSensibleOnes()
    {
        // A design written when the traffic was a single path has no lane settings at all; zero lanes would fail
        // validation and empty the sea, so the defaults stand in.
        var marina = new MarinaVisualizer();
        marina.AddLandArea(Quay());
        marina.SetMarineTraffic(MarineTraffic.None with { IsEnabled = true, Intensity = 0.4f, Seed = 2 });

        var json = MarinaDocument.FromVisualizer(marina, generator: "tests").ToJson()
            .Replace("\"laneCount\":", "\"laneCountWas\":")
            .Replace("\"laneSpacing\":", "\"laneSpacingWas\":");

        var reloaded = new MarinaVisualizer();
        MarinaDocument.Parse(json).ApplyTo(reloaded);

        Assert.Equal(MarineTraffic.None.LaneCount, reloaded.MarineTraffic.LaneCount);
        Assert.Equal(MarineTraffic.None.LaneSpacing, reloaded.MarineTraffic.LaneSpacing);
        Assert.NotEmpty(reloaded.GetTrafficVessels());
    }

    [Fact]
    public void ShowingTheLanes_DrawsThemAndIsNotSavedWithTheDesign()
    {
        var marina = new MarinaVisualizer();
        marina.AddLandArea(Quay());
        marina.SetShoreline(StraightCoast());
        marina.SetMarineTraffic(MarineTraffic.None with { IsEnabled = true, Intensity = 0.4f, Clearance = 250f, Seed = 3 });

        Assert.False(marina.ShowTrafficLanes);
        var hidden = marina.BuildRenderFrame().Objects.Count;

        marina.ShowTrafficLanes = true;
        var shown = marina.BuildRenderFrame().Objects.Count;
        Assert.True(shown > hidden, "turning the lanes on drew nothing");

        // More lanes draw more of them.
        marina.SetMarineTraffic(marina.MarineTraffic with { LaneCount = 4 });
        Assert.True(marina.BuildRenderFrame().Objects.Count > shown, "the extra lanes were not drawn");

        // It is a working aid, so it is not written to the file and a reloaded design does not have it on.
        var json = MarinaDocument.FromVisualizer(marina, generator: "tests").ToJson();
        var reloaded = new MarinaVisualizer();
        MarinaDocument.Parse(json).ApplyTo(reloaded);
        Assert.False(reloaded.ShowTrafficLanes);

        marina.ShowTrafficLanes = false;
        marina.SetMarineTraffic(marina.MarineTraffic with { LaneCount = MarineTraffic.None.LaneCount });
        Assert.Equal(hidden, marina.BuildRenderFrame().Objects.Count);
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
        marina.SetMarineTraffic(MarineTraffic.None with { IsEnabled = true, Intensity = 0.4f, Clearance = 400f, Seed = 2 });
        marina.BuildRenderFrame();

        var before = marina.TrafficLanes[0].Points[0];

        // A long pier reaching out into the water: the marina grew, so the lanes are planned again around the new
        // middle of it.
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -10), 0f, 320f));
        marina.BuildRenderFrame();

        Assert.NotEmpty(marina.TrafficLanes);
        Assert.NotEqual(before, marina.TrafficLanes[0].Points[0]);

        var reach = new OrientedRect(new Vector2(0, 150), new Vector2(320f, 6f), 0f).GetAxisAlignedBounds();
        foreach (var vessel in marina.GetTrafficVessels())
        {
            Assert.True(
                DistanceToBox(vessel.Position, reach) >= 100f,
                $"a vessel sails {DistanceToBox(vessel.Position, reach):0} m from the new pier");
        }
    }

    [Fact]
    public void TheLanesStartFarOut_CrossTheWaterNearTheMarina_AndLeaveOnTheOtherSide()
    {
        const float waterRadius = 2100f;
        var traffic = MarineTraffic.None with
        {
            IsEnabled = true, Intensity = 1f, Clearance = 200f, Reach = 6000f, Seed = 3, LaneCount = 2,
        };

        foreach (var shoreline in new Shoreline?[] { null, StraightCoast(), BentCoast() })
        {
            var lanes = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), new[] { Quay() }, shoreline);
            Assert.NotEmpty(lanes);

            foreach (var lane in lanes)
            {
                // Both ends are far outside the water, so vessels fade in and out where nobody is looking.
                Assert.True(Vector2.Distance(lane.Points[0], Centre()) > waterRadius, "a lane starts inside the detailed water");
                Assert.True(Vector2.Distance(lane.Points[^1], Centre()) > waterRadius, "a lane ends inside the detailed water");
            }

            // And the nearest comes close enough to be seen crossing among the waves.
            Assert.True(NearestApproach(lanes) < waterRadius, "no lane comes in among the waves");
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
        Assert.Equal(8, Count(MarineTrafficPlanner.Plan(traffic, MarinaBounds(), land, null)));

        // Half as busy puts out half as many, and raising the cap raises both.
        Assert.Equal(4, (traffic with { Intensity = 0.5f }).VesselCount);
        Assert.Equal(40, (traffic with { MaximumVessels = 40 }).VesselCount);

        // A cap outside the allowed range is refused rather than silently clamped.
        Assert.NotEmpty((traffic with { MaximumVessels = 0 }).Validate());
        Assert.NotEmpty((traffic with { MaximumVessels = MarineTraffic.VesselLimit + 1 }).Validate());
    }

    private static int Count(IReadOnlyList<TrafficLane> lanes) => lanes.Sum(lane => lane.VesselCount);

    /// <summary>Points evenly spaced along the whole lane.</summary>
    private static IEnumerable<Vector2> Walk(TrafficLane lane, int steps) =>
        Enumerable.Range(0, steps + 1).Select(step => lane.At(step / (float)steps).Position);

    private static float DistanceToBox(Vector2 point, (Vector2 Min, Vector2 Max) box) =>
        Vector2.Max(Vector2.Max(box.Min - point, point - box.Max), Vector2.Zero).Length();
}
