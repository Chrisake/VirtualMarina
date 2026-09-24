using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// The domain records are values: they keep their own copies of what they are given, compare by content, read null lists as
/// empty, and a boat's size follows its type until it is given one of its own.
/// </summary>
public class DomainRecordSemanticsTests
{
    private static readonly Vector2[] Square = { new(0, 0), new(10, 0), new(10, 10), new(0, 10) };

    [Fact]
    public void Records_CopyWhatTheyAreGiven_SoTheHostsListCanChangeFreely()
    {
        var points = Square.ToList();
        var land = new LandArea("L", new[] { new Vector2(5, 5), new Vector2(6, 5), new Vector2(6, 6) }, 1f) with { Points = points };
        points[0] = new Vector2(-99, -99);
        points.Add(Vector2.One);
        Assert.Equal(Square, land.Points);

        var metadata = new Dictionary<string, string> { ["erp"] = "1" };
        var pier = new Pier("A", "A", Vector2.Zero, 0f, 20f) { Metadata = metadata };
        metadata["erp"] = "2";
        Assert.Equal("1", pier.Metadata["erp"]);

        var ids = new List<string> { "a", "b" };
        var group = new MultiBerth("G", ids, new Boat("B", "Boat", BoatType.MotorYacht)) with { BerthIds = ids };
        ids.Clear();
        Assert.Equal(new[] { "a", "b" }, group.BerthIds);

        var vessels = new List<BoatType> { BoatType.Ferry };
        var traffic = MarineTraffic.None with { Vessels = vessels };
        vessels.Add(BoatType.JetSki);
        Assert.Single(traffic.Vessels);

        var berths = new List<Berth> { new("A-1", "A", Vector2.Zero, 0f, 10f, 4f) };
        var layout = new MarinaLayout { Berths = berths };
        berths.Clear();
        Assert.Single(layout.Berths);

        // What comes back cannot be cast to something writable either.
        Assert.False(land.Points is Vector2[] or List<Vector2>);
        Assert.False(pier.Metadata is IDictionary<string, string>);
    }

    [Fact]
    public void Records_CompareByContent()
    {
        Assert.Equal(new LandArea("L", Square, 1f), new LandArea("L", Square.ToList(), 1f));
        Assert.Equal(new LandArea("L", Square, 1f).GetHashCode(), new LandArea("L", Square.ToList(), 1f).GetHashCode());
        Assert.NotEqual(new LandArea("L", Square, 1f), new LandArea("L", Square.Reverse(), 1f));

        var boat = new Boat("B", "Aurora", BoatType.MotorYacht) { Metadata = new Dictionary<string, string> { ["k"] = "v", ["x"] = "y" } };
        var same = new Boat("B", "Aurora", BoatType.MotorYacht) { Metadata = new Dictionary<string, string> { ["x"] = "y", ["k"] = "v" } };
        Assert.Equal(boat, same);
        Assert.Equal(boat.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(boat, same with { Metadata = new Dictionary<string, string> { ["k"] = "w", ["x"] = "y" } });

        Assert.Equal(new Shoreline(Square, true), new Shoreline(Square.ToArray(), true));
        Assert.Equal(new MultiBerth("G", new[] { "a", "b" }, boat), new MultiBerth("G", Enumerable.Repeat("a", 1).Append("b").ToList(), same));
        Assert.Equal(MarineTraffic.None with { Vessels = new[] { BoatType.Ferry } }, MarineTraffic.None with { Vessels = Enumerable.Repeat(BoatType.Ferry, 1).ToList() });

        var layout = new MarinaLayoutBuilder("M").AddPier("A", "A", Vector2.Zero, 0f, 40f).AddLandArea("L", Square, 1f).Build();
        var again = new MarinaLayoutBuilder("M").AddPier("A", "A", Vector2.Zero, 0f, 40f).AddLandArea("L", Square.ToList(), 1f).Build();
        Assert.Equal(layout, again);

        // A berth's ExternalData is the one deliberately shared, mutable part: snapshots of the same berth share it and are
        // equal, two berths built apart each have their own and are not.
        var berth = new Berth("A-1", "A", Vector2.Zero, 0f, 10f, 4f);
        Assert.Equal(berth, berth with { });
        Assert.NotEqual(berth, new Berth("A-1", "A", Vector2.Zero, 0f, 10f, 4f));
    }

    [Fact]
    public void NullLists_ReadAsEmpty_AndAreReported_RatherThanThrowing()
    {
        var land = new LandArea("L", Square, 1f) with { Points = null!, Trees = null!, Metadata = null! };
        Assert.Empty(land.Points);
        Assert.Empty(land.Trees);
        Assert.Contains(new MarinaLayout { LandAreas = new[] { land } }.Validate(), e => e.Contains("at least three points", StringComparison.Ordinal));

        var traffic = MarineTraffic.None with { Vessels = null!, IsEnabled = true };
        Assert.Equal(MarineTraffic.DefaultVessels, traffic.EffectiveVessels);
        Assert.Empty(traffic.Validate());

        var group = new MultiBerth("G", new[] { "a", "b" }, new Boat("B", "B", BoatType.MotorYacht)) with { BerthIds = null! };
        Assert.Equal(string.Empty, group.PrimaryBerthId);
        Assert.Contains(new MarinaLayout { MultiBerths = new[] { group } }.Validate(), e => e.Contains("at least two berths", StringComparison.Ordinal));

        var layout = new MarinaLayout { Piers = null!, Berths = null! };
        Assert.Empty(layout.Piers);
        Assert.Empty(layout.Validate());
    }

    [Fact]
    public void ABoatsSize_FollowsItsType_UntilItHasOneOfItsOwn()
    {
        var yacht = new Boat("B", "Aurora", BoatType.MotorYacht);
        Assert.False(yacht.HasCustomLength);
        var ski = yacht with { Type = BoatType.JetSki };
        Assert.Equal(BoatTypeCatalog.GetNominalDimensions(BoatType.JetSki).Length, ski.LengthMeters);
        Assert.Equal(BoatTypeCatalog.GetNominalDimensions(BoatType.JetSki).Beam, ski.BeamMeters);

        var measured = yacht with { LengthMeters = 21f };
        Assert.True(measured.HasCustomLength);
        Assert.False(measured.HasCustomBeam);
        Assert.Equal(21f, (measured with { Type = BoatType.JetSki }).LengthMeters);
        Assert.Equal(BoatTypeCatalog.GetNominalDimensions(BoatType.JetSki).Beam, (measured with { Type = BoatType.JetSki }).BeamMeters);
    }

    [Fact]
    public void GeneratedBerths_HaveNoLabel_SoARenamedBerthShowsItsNewId()
    {
        var pier = new Pier("A", "A", Vector2.Zero, 0f, 40f);
        var berth = BerthGenerator.AtPier(pier, "A-L01", PierSide.Left, 2f, 5f, 12f);
        Assert.Null(berth.Label);
        Assert.Equal("A-7", (berth with { Id = "A-7" }).DisplayName);

        var ashore = Berth.OnLand("Y-01", "yard", Vector2.Zero);
        Assert.Null(ashore.Label);
        Assert.Equal("Y-02", (ashore with { Id = "Y-02" }).DisplayName);
    }

    [Fact]
    public void ABuilderRow_NumbersOn_FromTheSameSide_AndSkipsTakenIds()
    {
        var layout = new MarinaLayoutBuilder()
            .AddPier("A", "A", Vector2.Zero, 0f, 80f, pier => pier
                .AddBerth("A-L02", PierSide.Left, 60f, 5f, 12f)
                .AddBerths(PierSide.Left, 3, 5f, 12f, customize: (i, b) => i == 0 ? b with { Id = "VIP" } : b)
                .AddBerths(PierSide.Left, 2, 5f, 12f)
                .AddBerths(PierSide.Right, 2, 5f, 12f))
            .Build();

        var ids = layout.Berths.Select(b => b.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("VIP", ids);
        Assert.Equal(new[] { "A-L02", "VIP", "A-L04", "A-L05", "A-L06", "A-L07" }, ids.Where(id => !id.StartsWith("A-R", StringComparison.Ordinal)));
        Assert.Contains("A-R01", ids);
        Assert.Empty(layout.Validate());
    }

    [Fact]
    public void GeneratedDividers_HaveSaneIds_AndOnePerBoundaryPerPier()
    {
        var layout = new MarinaLayoutBuilder()
            .AddPier(new Pier("Q", "Quay", Vector2.Zero, 90f, 60f) { BerthingSides = PierSides.Left }, pier => pier
                .AddBerths(PierSide.Left, 2, 5f, 12f, dividers: DividerType.Piles)
                .AddBerths(PierSide.Left, 2, 5f, 12f, dividers: DividerType.Piles))
            .AddPier("A", "A", new Vector2(0, 100), 0f, 60f, pier => pier
                .AddBerths(PierSide.Left, 2, 5f, 12f, dividers: DividerType.FingerPier))

            // A second pier whose boundaries fall exactly where the first one's do: it still gets its own.
            .AddPier("B", "B", new Vector2(0, 100), 0f, 60f, pier => pier
                .AddBerths(PierSide.Left, 2, 5f, 12f, dividers: DividerType.FingerPier))
            .Build();

        var quay = layout.Dividers.Where(d => d.PierId == "Q").Select(d => d.Id).ToList();
        Assert.Equal(new[] { "Q-D01", "Q-D02", "Q-D03", "Q-D04", "Q-D05" }, quay);
        Assert.Equal(new[] { "A-L-D01", "A-L-D02", "A-L-D03" }, layout.Dividers.Where(d => d.PierId == "A").Select(d => d.Id));
        Assert.Equal(3, layout.Dividers.Count(d => d.PierId == "B"));
    }

    [Fact]
    public void MultiBerthProblems_SayMultiBerth_AndCatchMembersThatCannotShareABoat()
    {
        var boat = new Boat("X", "X", BoatType.MotorYacht);
        var builder = new MarinaLayoutBuilder()
            .AddLandArea("yard", Square.Select(p => p * 5f + new Vector2(200, 0)), 1f, configure: land => land
                .AddBerth("Y-1", new Vector2(210, 10))
                .AddBerth("Y-2", new Vector2(220, 10)))
            .AddPier("A", "A", Vector2.Zero, 0f, 40f, pier => pier.AddBerths(PierSide.Left, 2, 5f, 12f))
            .AddPier("B", "B", new Vector2(60, 0), 0f, 40f, pier => pier.AddBerths(PierSide.Left, 2, 5f, 12f));

        var layout = builder.Build() with
        {
            MultiBerths = new[]
            {
                new MultiBerth("A-L01", new[] { "A-L01", "A-L02" }, boat),
                new MultiBerth("mixed", new[] { "Y-1", "B-L01" }, boat),
                new MultiBerth("apart", new[] { "A-L02", "B-L02" }, boat),
                new MultiBerth("ashore", new[] { "Y-1", "Y-2" }, boat),
                new MultiBerth("free", new[] { "B-L01", "missing" }, boat, BerthStatus.Free),
            },
        };

        var errors = layout.Validate();
        Assert.Contains(errors, e => e.Contains("Multi-berth 'A-L01' has the same id as a berth", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Multi-berth 'mixed' mixes berths on the water and on land", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Multi-berth 'apart' spans berths along different piers", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Multi-berth 'free' status must be", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Multi-berth 'free' references unknown berth 'missing'", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, e => e.StartsWith("Multi-berth 'ashore'", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, e => e.StartsWith("Berth '", StringComparison.Ordinal) && e.Contains("status", StringComparison.Ordinal));
    }

    [Fact]
    public void ADividerWithAnEndlessWidthOrSpacing_IsReported()
    {
        var divider = new Divider("D", Vector2.Zero, 0f, 5f) { Width = float.PositiveInfinity, Spacing = float.PositiveInfinity };
        var errors = new MarinaLayout { Dividers = new[] { divider } }.Validate();
        Assert.Contains(errors, e => e.Contains("finite width", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("spacing", StringComparison.Ordinal));
    }

    [Fact]
    public void GenerateTrees_KeepsTreesApart_OutOfKeepClear_AndCopesWithABigLawn()
    {
        var lawn = new[] { new Vector2(0, 0), new Vector2(400, 0), new Vector2(400, 400), new Vector2(0, 400) };
        var keepClear = new OrientedRect(new Vector2(200, 200), new Vector2(60, 60), 30f);
        var started = System.Diagnostics.Stopwatch.StartNew();
        var trees = LandArea.GenerateTrees(lawn, 40f, new Random(3), new[] { keepClear });
        started.Stop();

        Assert.True(trees.Count > 1500, $"only {trees.Count} trees");
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(10), $"took {started.Elapsed}");
        foreach (var tree in trees)
        {
            Assert.False(new OrientedRect(keepClear.Center, keepClear.Size + new Vector2((tree.CrownRadius - 0.01f) * 2f + 1f), keepClear.HeadingDegrees).Contains(tree.Position));
        }

        // Every pair, the slow way, to prove the grid missed none.
        for (var i = 0; i < trees.Count; i++)
        {
            for (var j = i + 1; j < trees.Count; j++)
            {
                if (Vector2.Distance(trees[i].Position, trees[j].Position) < (trees[i].CrownRadius + trees[j].CrownRadius) * 0.9f - 0.02f)
                {
                    Assert.Fail($"trees {i} and {j} overlap");
                }
            }
        }

        // The same seed gives the same trees.
        Assert.Equal(trees, LandArea.GenerateTrees(lawn, 40f, new Random(3), new[] { keepClear }));
    }

    [Fact]
    public void TreeShapeProfiles_StayWithinWhatALandAreaAccepts()
    {
        foreach (var shape in Enum.GetValues<TreeShape>())
        {
            var profile = TreeShapeProfile.For(shape);
            Assert.InRange(profile.MinHeight, 1f, 40f);
            Assert.InRange(profile.MinHeight + profile.HeightRange, 1f, 40f);
            Assert.True((profile.MinHeight + profile.HeightRange) * (profile.MinCrownRatio + profile.CrownRatioRange) <= TreeShapeProfile.MaxCrownRadius + 1e-4f);
        }
    }
}
