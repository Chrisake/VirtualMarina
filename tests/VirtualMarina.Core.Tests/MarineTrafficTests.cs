using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Rendering;
using VirtualMarina.Core.Serialization;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// The passing traffic out at sea. Two things matter: the clearance setting means what it says — the path's closest
/// approach to the middle of the marina, and nothing else — and the path follows the coast instead of cutting across
/// it, never sailing over the land on the way.
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

    [Fact]
    public void TheClearance_IsHowNearTheMiddleOfTheMarinaThePathComes()
    {
        // The whole point of the setting: 300 m means 300 m, whether or not there is a coast to follow.
        foreach (var shoreline in new Shoreline?[] { null, StraightCoast(), BentCoast() })
        {
            foreach (var clearance in new[] { 150f, 300f, 600f, 1000f })
            {
                var traffic = Busy() with { Clearance = clearance, Reach = 6000f };
                var path = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), new[] { Quay() }, shoreline);

                Assert.NotNull(path);
                Assert.Equal(clearance, path!.DistanceTo(Centre()), tolerance: clearance * 0.05f);
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

            Assert.NotNull(near);
            Assert.NotNull(far);
            Assert.Equal(400f, near!.DistanceTo((small.Item1 + small.Item2) * 0.5f), tolerance: 20f);
            Assert.Equal(400f, far!.DistanceTo((large.Item1 + large.Item2) * 0.5f), tolerance: 20f);
        }
    }

    [Fact]
    public void ThePathRunsAlongsideTheCoast_WithItsEndsParallelToTheEndlessSegments()
    {
        var shore = BentCoast();
        var path = MarineTrafficPlanner.Plan(Busy() with { Reach = 6000f }, MarinaBounds(), new[] { Quay() }, shore);
        Assert.NotNull(path);

        var points = path!.Points;

        // The first segment of the path runs the way the coast's first endless segment runs, and the last one the way
        // the last does — so the traffic arrives along the coast rather than out of the open sea at an angle.
        var first = Vector2.Normalize(points[1] - points[0]);
        var last = Vector2.Normalize(points[^1] - points[^2]);
        Assert.Equal(1f, Vector2.Dot(first, Vector2.Normalize(shore.Points[1] - shore.Points[0])), tolerance: 0.001f);
        Assert.Equal(1f, Vector2.Dot(last, Vector2.Normalize(shore.Points[^1] - shore.Points[^2])), tolerance: 0.001f);

        // In between it bends: the direction at the far end is nothing like the direction at the near one.
        Assert.True(Vector2.Dot(first, last) < 0.9f, "the path never turns with the coast");

        // And the turn is taken as a curve, not a corner: no single joint changes direction sharply.
        for (var i = 1; i < points.Count - 1; i++)
        {
            var before = Vector2.Normalize(points[i] - points[i - 1]);
            var after = Vector2.Normalize(points[i + 1] - points[i]);
            Assert.True(Vector2.Dot(before, after) > 0.86f, $"the path kinks by {MathF.Acos(Vector2.Dot(before, after)) * MarinaMath.RadToDeg:0}° at point {i}");
        }
    }

    [Fact]
    public void ThePathNeverCrossesTheMainlandOrTheLand()
    {
        var shore = BentCoast();
        var path = MarineTrafficPlanner.Plan(Busy() with { Reach = 4000f }, MarinaBounds(), new[] { Quay() }, shore);
        Assert.NotNull(path);

        foreach (var point in Walk(path!, 1200))
        {
            Assert.False(shore.Contains(point), $"the path runs over the mainland at {point}");
            Assert.False(PolygonMath.Contains(Quay().Points, point), $"the path runs over the quay at {point}");
        }
    }

    [Fact]
    public void APathThatWouldCrossAQuay_IsPushedOutUntilItDoesNot()
    {
        // A breakwater reaching a long way out to sea, right where a 150 m clearance would put the shipping.
        var breakwater = new LandArea(
            "breakwater",
            new[] { new Vector2(-15, 0), new Vector2(15, 0), new Vector2(15, 900), new Vector2(-15, 900) },
            1f,
            LandKind.Quay);

        var traffic = Busy() with { Clearance = 150f, Reach = 4000f };
        var path = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), new[] { breakwater }, StraightCoast());

        Assert.NotNull(path);
        foreach (var point in Walk(path!, 1500))
        {
            Assert.False(PolygonMath.Contains(breakwater.Points, point), "the traffic sails through the breakwater");
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

        Assert.Equal(quiet.VesselCount, MarineTrafficPlanner.Plan(quiet, MarinaBounds(), land, null)?.VesselCount);
        Assert.Equal(busy.VesselCount, MarineTrafficPlanner.Plan(busy, MarinaBounds(), land, null)?.VesselCount);

        // Switched off, there is nothing at all.
        Assert.Null(MarineTrafficPlanner.Plan(MarineTraffic.None, MarinaBounds(), land, null));
    }

    [Fact]
    public void SettingsThatMakeNoSense_LeaveNoPathRatherThanABadOne()
    {
        // Asking to stay 5 km clear when the path only reaches 1.5 km out cannot be satisfied.
        var impossible = Busy() with { Clearance = 5000f };

        Assert.NotEmpty(impossible.Validate());
        Assert.Null(MarineTrafficPlanner.Plan(impossible, MarinaBounds(), new[] { Quay() }, null));
    }

    [Fact]
    public void VesselsMoveAlongThePath_AndFadeOutAtBothEnds()
    {
        var traffic = Busy() with { Intensity = 0.2f, SpeedKnots = 20f };
        var path = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), new[] { Quay() }, StraightCoast());
        Assert.NotNull(path);

        var first = MarineTrafficPlanner.Place(path, traffic, 0d).ToArray();
        var later = MarineTrafficPlanner.Place(path, traffic, 30d).ToArray();

        Assert.NotEmpty(first);
        Assert.Equal(first.Length, later.Length);
        Assert.Contains(first.Zip(later), pair => Vector2.Distance(pair.First.Position, pair.Second.Position) > 50f);

        // Every vessel is on the path, and its strength is a sensible fade.
        foreach (var vessel in first.Concat(later))
        {
            Assert.InRange(vessel.Opacity, 0f, 1f);
            Assert.True(path!.DistanceTo(vessel.Position) < 1f, "a vessel is off the path");
        }

        // Nothing pops into view: followed right around the path, a vessel is invisible at the ends and solid in the
        // middle, and its strength tracks how far it is from the nearer end.
        var laps = 3d * path!.Length / (traffic.SpeedMetersPerSecond * 0.8f);
        var samples = Enumerable.Range(0, 600)
            .Select(step => MarineTrafficPlanner.Place(path, traffic, step * laps / 599d).First())
            .Select(vessel => (vessel.Opacity, ToEnd: MathF.Min(
                Vector2.Distance(vessel.Position, path.Points[0]),
                Vector2.Distance(vessel.Position, path.Points[^1])) / path.Length))
            .ToArray();

        Assert.True(samples.Min(s => s.Opacity) < 0.5f, "a vessel never fades at all at the end of the path");
        Assert.True(samples.Max(s => s.Opacity) > 0.99f, "a vessel is never drawn at full strength");

        // And the fade is over quickly: a twentieth of the way along — still far out in the flat sea — a vessel is
        // already solid, so nobody watches one materialise.
        Assert.All(
            samples.Where(s => s.ToEnd > 0.05f),
            s => Assert.True(s.Opacity > 0.99f, $"a vessel {s.ToEnd:0.000} along the path is only {s.Opacity:0.00} visible"));
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
        Assert.Null(marina.TrafficPath);

        var quiet = marina.BuildRenderFrame().Objects.Count;
        marina.SetMarineTraffic(MarineTraffic.None with { IsEnabled = true, Intensity = 0.5f, Clearance = 150f, Seed = 3 });

        Assert.NotNull(marina.TrafficPath);
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
        Assert.Null(marina.TrafficPath);
        Assert.Equal(quiet, marina.BuildRenderFrame().Objects.Count);
    }

    [Fact]
    public void ShowingThePath_DrawsItAndIsNotSavedWithTheDesign()
    {
        var marina = new MarinaVisualizer();
        marina.AddLandArea(Quay());
        marina.SetShoreline(StraightCoast());
        marina.SetMarineTraffic(MarineTraffic.None with { IsEnabled = true, Intensity = 0.4f, Clearance = 250f, Seed = 3 });

        Assert.False(marina.ShowTrafficPath);
        var hidden = marina.BuildRenderFrame().Objects.Count;

        marina.ShowTrafficPath = true;
        var shown = marina.BuildRenderFrame().Objects.Count;
        Assert.True(shown > hidden, "turning the path on drew nothing");

        // It is a working aid, so it is not written to the file and a reloaded design does not have it on.
        var json = MarinaDocument.FromVisualizer(marina, generator: "tests").ToJson();
        var reloaded = new MarinaVisualizer();
        MarinaDocument.Parse(json).ApplyTo(reloaded);
        Assert.False(reloaded.ShowTrafficPath);

        marina.ShowTrafficPath = false;
        Assert.Equal(hidden, marina.BuildRenderFrame().Objects.Count);
    }

    [Fact]
    public void BuildingTheMarinaOut_MovesThePathOutOfTheWay()
    {
        var marina = new MarinaVisualizer();
        marina.AddLandArea(Quay());
        marina.SetMarineTraffic(MarineTraffic.None with { IsEnabled = true, Intensity = 0.4f, Clearance = 400f, Seed = 2 });
        marina.BuildRenderFrame();

        var before = marina.TrafficPath;
        Assert.NotNull(before);

        // A long pier reaching out into the water: the marina grew, so the path is planned again around the new middle.
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -10), 0f, 320f));
        marina.BuildRenderFrame();

        var after = marina.TrafficPath;
        Assert.NotNull(after);
        Assert.NotEqual(before!.Points[0], after!.Points[0]);

        var reach = new OrientedRect(new Vector2(0, 150), new Vector2(320f, 6f), 0f).GetAxisAlignedBounds();
        foreach (var vessel in marina.GetTrafficVessels())
        {
            Assert.True(
                DistanceToBox(vessel.Position, reach) >= 100f,
                $"a vessel sails {DistanceToBox(vessel.Position, reach):0} m from the new pier");
        }
    }

    [Fact]
    public void ThePathStartsFarOut_CrossesTheWaterNearTheMarina_AndLeavesOnTheOtherSide()
    {
        const float waterRadius = 2100f;
        var traffic = MarineTraffic.None with
        {
            IsEnabled = true, Intensity = 1f, Clearance = 200f, Reach = 6000f, Seed = 3,
        };

        foreach (var shoreline in new Shoreline?[] { null, StraightCoast(), BentCoast() })
        {
            var path = MarineTrafficPlanner.Plan(traffic, MarinaBounds(), new[] { Quay() }, shoreline);
            Assert.NotNull(path);

            // Both ends are far outside the water, so vessels fade in and out where nobody is looking.
            Assert.True(Vector2.Distance(path!.Points[0], Centre()) > waterRadius, "the path starts inside the detailed water");
            Assert.True(Vector2.Distance(path.Points[^1], Centre()) > waterRadius, "the path ends inside the detailed water");

            // And it passes close enough to be seen crossing among the waves.
            Assert.True(path.DistanceTo(Centre()) < waterRadius, "the path never comes in among the waves");
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
        Assert.Equal(8, MarineTrafficPlanner.Plan(traffic, MarinaBounds(), land, null)?.VesselCount);

        // Half as busy puts out half as many, and raising the cap raises both.
        Assert.Equal(4, (traffic with { Intensity = 0.5f }).VesselCount);
        Assert.Equal(40, (traffic with { MaximumVessels = 40 }).VesselCount);

        // A cap outside the allowed range is refused rather than silently clamped.
        Assert.NotEmpty((traffic with { MaximumVessels = 0 }).Validate());
        Assert.NotEmpty((traffic with { MaximumVessels = MarineTraffic.VesselLimit + 1 }).Validate());
    }

    /// <summary>Points evenly spaced along the whole path.</summary>
    private static IEnumerable<Vector2> Walk(TrafficPath path, int steps) =>
        Enumerable.Range(0, steps + 1).Select(step => path.At(step / (float)steps).Position);

    private static float DistanceToBox(Vector2 point, (Vector2 Min, Vector2 Max) box) =>
        Vector2.Max(Vector2.Max(box.Min - point, point - box.Max), Vector2.Zero).Length();
}
