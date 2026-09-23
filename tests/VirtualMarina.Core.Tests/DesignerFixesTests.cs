using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// Smaller things the designer got wrong: trees for an outline a handler replaced, a calibration past the allowed scale,
/// names past the end of the alphabet or of a pattern, patterns read back in the wrong case, and a picture that could be
/// changed behind its back.
/// </summary>
public class DesignerFixesTests
{
    private static readonly Vector2[] Field = [new(0, 0), new(80, 0), new(80, 60), new(0, 60)];

    // ---- Trees for the land actually made -------------------------------------------------------

    [Fact]
    public void ALawnAHandlerReshapes_GetsTreesInsideTheShapeItEndsUpWith()
    {
        var marina = new MarinaVisualizer();
        var designer = marina.Designer;
        designer.SetRandomSeed(11);
        designer.LandKind = LandKind.Grass;
        designer.TreeDensity = 40f;
        var smaller = new[] { new Vector2(0, 0), new Vector2(20, 0), new Vector2(20, 15), new Vector2(0, 15) };
        designer.ElementCreating += (_, e) => e.LandArea = e.LandArea! with { Points = smaller };

        var lawn = designer.CreateLandArea(Field)!;

        Assert.NotEmpty(lawn.Trees);
        Assert.All(lawn.Trees, tree => Assert.True(lawn.Contains(tree.Position), $"a tree at {tree.Position} stands outside the lawn"));
    }

    [Fact]
    public void LandAHandlerTurnsIntoAQuay_GetsNoTrees_AndTreesAHandlerChoseAreKept()
    {
        var marina = new MarinaVisualizer();
        var designer = marina.Designer;
        designer.LandKind = LandKind.Grass;
        designer.TreeDensity = 40f;
        EventHandler<DesignElementCreatingEventArgs> toQuay = (_, e) => e.LandArea = e.LandArea! with { Kind = LandKind.Quay };
        designer.ElementCreating += toQuay;
        Assert.Empty(designer.CreateLandArea(Field)!.Trees);
        designer.ElementCreating -= toQuay;

        var chosen = new[] { new LandTree(new Vector2(5, 5), 5f, 2f) };
        designer.ElementCreating += (_, e) => e.LandArea = e.LandArea! with { Points = [.. Field.Select(p => p + new Vector2(100, 0))], Trees = chosen };
        Assert.Equal(chosen, designer.CreateLandArea(Field)!.Trees);
    }

    [Fact]
    public void TheSameSeed_ScattersTheSameTrees()
    {
        IReadOnlyList<LandTree> Plant()
        {
            var marina = new MarinaVisualizer();
            marina.AddLandArea(new LandArea("lawn", Field, 1f, LandKind.Grass));
            marina.Designer.SetRandomSeed(42);
            return marina.Designer.PlantTrees("lawn")!.Trees;
        }

        Assert.Equal(Plant(), Plant());
    }

    // ---- Calibration past the allowed scale -----------------------------------------------------

    [Fact]
    public void ACalibrationBeyondTheLargestScale_MovesTheImageAndTheLineByTheScaleActuallyUsed()
    {
        var designer = new MarinaVisualizer().Designer;
        designer.SetReferenceImage(new ReferenceImage(10, 10, new byte[400]), 500f, new Vector2(10, 0));

        // Asks for 5,000,000 m a pixel; 1000 is the most there is, twice what it was.
        Assert.True(designer.CalibrateReferenceImage(Vector2.Zero, new Vector2(1, 0), 10_000f));

        Assert.Equal(1000f, designer.ReferenceImageMetersPerPixel);
        Assert.Equal(new Vector2(20, 0), designer.ReferenceImageCenter);
        Assert.Equal((Vector2.Zero, new Vector2(2, 0)), designer.ScaleLine);
    }

    // ---- Names past the end ---------------------------------------------------------------------

    [Theory]
    [InlineData(1, "A")]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    [InlineData(52, "AZ")]
    [InlineData(53, "BA")]
    [InlineData(702, "ZZ")]
    [InlineData(703, "AAA")]
    public void PierIds_RunOnInLetters(int index, string expected) => Assert.Equal(expected, DesignNaming.Letters(index));

    [Fact]
    public void ThePierAfterZ_IsAA()
    {
        var marina = new MarinaVisualizer();
        for (var i = 0; i < 26; i++) marina.Designer.CreatePier(new Vector2(i * 10, 0), new Vector2(i * 10, 20));
        Assert.Equal("Z", marina.GetPiers()[^1].Id);

        Assert.Equal("AA", marina.Designer.CreatePier(new Vector2(300, 0), new Vector2(300, 20))!.Id);
    }

    [Fact]
    public void APatternWithoutANumber_NumbersTheNameItGives_RatherThanFallingBackToAnotherForm()
    {
        var marina = new MarinaVisualizer();
        marina.AddLandArea(new LandArea("yard", Field, 1f));
        var designer = marina.Designer;
        designer.BerthNaming = new BerthNamingScheme { LandPattern = "SHED" };

        Assert.Equal("SHED", designer.CreateLandBerth("yard", new Vector2(10, 10))!.Id);
        Assert.Equal("SHED-02", designer.CreateLandBerth("yard", new Vector2(30, 10))!.Id);
        Assert.Equal("SHED-03", designer.CreateLandBerth("yard", new Vector2(50, 10))!.Id);
    }

    // ---- Patterns read back out of names --------------------------------------------------------

    [Theory]
    [InlineData("BE", PierSides.Both, "Berth 07", "Berth {number}")]
    [InlineData("Al", PierSides.Both, "Al07", "{pier}{number}")]
    [InlineData("A", PierSides.Both, "a-l01", "a-l{number}")]
    [InlineData("A", PierSides.Both, "A-L01", "{pier}-{side}{number}")]
    [InlineData("K", PierSides.Left, "K-07", "{pier}-{number}")]
    public void AnInferredPattern_OnlyTakesTokensSpelledExactlyAsThePierAndSchemeSpellThem(string pierId, PierSides sides, string name, string pattern)
    {
        var pier = new Pier(pierId, "Pier", Vector2.Zero, 0f, 40f) { BerthingSides = sides };
        var inferred = BerthNamingScheme.Default.Infer(pier, PierSide.Left, name);

        Assert.NotNull(inferred);
        Assert.Equal(pattern, inferred.Value.Pattern);
    }

    [Fact]
    public void AnInferredPattern_GivesBackTheNameItWasReadFrom_ForAnyName()
    {
        var random = new Random(5);
        const string alphabet = "ABLRalr-._ {}xX";
        var schemes = new[] { BerthNamingScheme.Default, BerthNamingScheme.Default with { LeftSide = "port", RightSide = "stb" } };
        for (var i = 0; i < 2000; i++)
        {
            var pierId = new string(Enumerable.Range(0, random.Next(1, 3)).Select(_ => alphabet[random.Next(alphabet.Length - 4)]).ToArray()).Trim();
            if (pierId.Length == 0) continue;
            var head = new string(Enumerable.Range(0, random.Next(0, 6)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            var number = random.Next(0, 1000).ToString(new string('0', random.Next(1, 4)), System.Globalization.CultureInfo.InvariantCulture);
            var name = (head + number).Trim();
            var pier = new Pier(pierId, "Name", Vector2.Zero, 0f, 40f) { BerthingSides = random.Next(2) == 0 ? PierSides.Both : PierSides.Left };
            var side = random.Next(2) == 0 ? PierSide.Left : PierSide.Right;
            var scheme = schemes[random.Next(schemes.Length)];

            if (scheme.Infer(pier, side, name) is not { } inferred) continue;
            var again = (scheme with { Pattern = inferred.Pattern, NumberDigits = inferred.Digits }).Format(pier, side, int.Parse(name[^Math.Min(inferred.Digits, name.Length)..], System.Globalization.CultureInfo.InvariantCulture));
            Assert.True(string.Equals(name, again, StringComparison.Ordinal), $"'{name}' on pier '{pierId}' read back as '{inferred.Pattern}', which gives '{again}'");
        }
    }

    // ---- The reference picture ------------------------------------------------------------------

    [Fact]
    public void AReferenceImage_KeepsItsOwnCopyOfThePixels()
    {
        var rgba = new byte[] { 1, 2, 3, 4 };
        var encoded = new byte[] { 9, 9, 9 };
        var image = new ReferenceImage(1, 1, rgba, encoded);
        var fromFile = ReferenceImage.FromEncoded(encoded, 1, 1);

        rgba[0] = 200;
        encoded[0] = 200;

        Assert.Equal(1, image.Rgba![0]);
        Assert.Equal(9, image.EncodedData![0]);
        Assert.Equal(9, fromFile.EncodedData![0]);
    }

    [Fact]
    public void ReferenceImageOpacity_RefusesANonNumber_AndClampsTheRest()
    {
        var designer = new MarinaVisualizer().Designer;
        Assert.Throws<ArgumentOutOfRangeException>(() => designer.ReferenceImageOpacity = float.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => designer.ReferenceImageOpacity = float.PositiveInfinity);
        Assert.Equal(0.6f, designer.ReferenceImageOpacity);

        designer.ReferenceImageOpacity = 1.5f;
        Assert.Equal(1f, designer.ReferenceImageOpacity);
    }

    // ---- Separators an erasure leaves behind ----------------------------------------------------

    [Fact]
    public void ErasingABerth_TakesOnlyTheSeparatorsItAloneUsed_OnItsOwnPierOrNone()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, 0), 0f, 60f));
        marina.AddPier(new Pier("B", "Pier B", new Vector2(40, 0), 0f, 60f));
        var designer = marina.Designer;
        designer.BerthWidth = 5f;
        designer.BerthSeparators = BerthSeparator.Piles;
        designer.CreateBerths("A", PierSide.Left, 0f, 15f);
        designer.CreateBerths("B", PierSide.Left, 0f, 15f);
        var onB = marina.GetDividersByPier("B").Select(d => d.Id).ToArray();

        // Separators of no pier: one only the last berth uses, one it shares with the berth before it.
        var last = marina.GetBerth("A-L03")!;
        var middle = marina.GetBerth("A-L02")!;
        var alone = marina.GetDividersByPier("A").First(d => BerthPlanner.Separates(d, last) && !BerthPlanner.Separates(d, middle));
        var shared = marina.GetDividersByPier("A").First(d => BerthPlanner.Separates(d, last) && BerthPlanner.Separates(d, middle));
        marina.AddDivider(alone with { Id = "FREE-ALONE", PierId = null });
        marina.AddDivider(shared with { Id = "FREE-SHARED", PierId = null });

        var erased = new List<DesignElementErasedEventArgs>();
        designer.ElementErased += (_, e) => erased.Add(e);
        Assert.True(designer.Erase(last));

        var gone = erased.Single().RemovedDividers.Select(d => d.Id).ToArray();
        Assert.Contains(alone.Id, gone);
        Assert.Contains("FREE-ALONE", gone);
        Assert.DoesNotContain(shared.Id, gone);
        Assert.DoesNotContain("FREE-SHARED", gone);
        Assert.Equal(onB, marina.GetDividersByPier("B").Select(d => d.Id));
        Assert.NotNull(marina.GetDivider("FREE-SHARED"));
        Assert.Null(marina.GetDivider("FREE-ALONE"));
    }

    // ---- Words in the overlay and in the naming checks ------------------------------------------

    [Fact]
    public void NamingProblems_AreWordedByTheResourceFile()
    {
        var problems = new BerthNamingScheme { Pattern = " ", Increment = 0, NumberDigits = 12 }.Validate().ToArray();
        Assert.Equal(
            ["The berth naming pattern must not be empty.", "The berth numbering increment must not be 0.", "The berth numbering must be padded to between 1 and 9 digits."],
            problems);
    }
}
