using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Serialization;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// Input nobody would type on purpose: empty strings, degenerate shapes, values that are not numbers, names made
/// of characters outside the basic plane. The library takes these from files and from host applications, so what
/// it does with them is part of its behaviour whether it was designed or not.
/// </summary>
public class EdgeCaseProbeTests
{
    private static Pier APier(string id = "A") => new(id, "Pier " + id, Vector2.Zero, 0f, 40f);

    // ---- The berth-name pattern scanner ------------------------------------------------------

    [Theory]
    [InlineData("{pier}-{number}", "A-01")]
    [InlineData("no tokens at all", "no tokens at all")]
    [InlineData("{pier}{pier}", "AA")]
    [InlineData("{unknown}", "{unknown}")]          // Unknown tokens survive as written.
    [InlineData("{}", "{}")]                         // An empty token is not a token.
    [InlineData("{", "{")]                           // An unclosed brace is just a brace.
    [InlineData("a{b", "a{b")]
    [InlineData("}{", "}{")]
    [InlineData("{PIER}", "A")]                      // Token names are case-insensitive.
    [InlineData("{pier}{", "A{")]
    [InlineData("{{pier}}", "{{pier}}")]
    public void NamePattern_SurvivesBracesInEveryArrangement(string pattern, string expected)
    {
        var scheme = new BerthNamingScheme { Pattern = pattern, NumberDigits = 2 };

        Assert.Equal(expected, scheme.Format(APier(), PierSide.Left, 1));
    }

    [Fact]
    public void NamePattern_ThatFormatsToNothing_FallsBackToTheNumber()
    {
        var scheme = new BerthNamingScheme { Pattern = "   ", NumberDigits = 3 };

        Assert.Equal("001", scheme.Format(APier(), PierSide.Left, 1));
    }

    [Fact]
    public void NamePattern_WithAnAbsurdDigitCount_IsClamped()
    {
        var wide = new BerthNamingScheme { Pattern = "{number}", NumberDigits = 1000 };
        var narrow = new BerthNamingScheme { Pattern = "{number}", NumberDigits = -5 };

        Assert.Equal(9, wide.Format(APier(), PierSide.Left, 1).Length);
        Assert.Equal("1", narrow.Format(APier(), PierSide.Left, 1));
    }

    [Fact]
    public void NamePattern_WithANegativeNumber_StillProducesAName()
    {
        var scheme = new BerthNamingScheme { Pattern = "{pier}-{number}" };

        var name = scheme.Format(APier(), PierSide.Left, -7);

        Assert.False(string.IsNullOrWhiteSpace(name));
    }

    [Fact]
    public void NamePattern_WithIntMinValue_DoesNotOverflow()
    {
        var scheme = new BerthNamingScheme { Pattern = "{number}" };

        var name = scheme.Format(APier(), PierSide.Left, int.MinValue);

        Assert.Contains("2147483648", name, StringComparison.Ordinal);
    }

    // ---- Identifiers -------------------------------------------------------------------------

    [Fact]
    public void BerthIds_AreMatchedWithoutRegardToCase()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(APier());
        marina.AddBerth(BerthGenerator.AtPier(marina.GetPier("A")!, "A-L01", PierSide.Left, 0f, 5f, 12f));

        Assert.NotNull(marina.GetBerth("a-l01"));
        Assert.NotNull(marina.GetBerth("A-L01"));
    }

    [Fact]
    public void AddingTwoBerthsWhoseIdsDifferOnlyInCase_IsRefused()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(APier());
        var pier = marina.GetPier("A")!;
        marina.AddBerth(BerthGenerator.AtPier(pier, "A-L01", PierSide.Left, 0f, 5f, 12f));

        Assert.ThrowsAny<Exception>(() =>
            marina.AddBerth(BerthGenerator.AtPier(pier, "a-l01", PierSide.Left, 12f, 5f, 12f)));
    }

    [Fact]
    public void AnIdMadeOfWhitespace_IsRefusedRatherThanStored()
    {
        var marina = new MarinaVisualizer();

        Assert.ThrowsAny<Exception>(() => marina.AddPier(new Pier("   ", "Blank", Vector2.Zero, 0f, 40f)));
    }

    // ---- Labels made of unusual characters ---------------------------------------------------

    /// <summary>
    /// A berth label is drawn glyph by glyph. Characters outside the basic plane arrive as two UTF-16 code units,
    /// so a naive per-char walk sees two halves of a surrogate pair rather than one character.
    /// </summary>
    [Fact]
    public void ABerthLabelOutsideTheBasicPlane_DoesNotBreakTheSceneBuild()
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(800, 600);
        marina.AddPier(APier());
        marina.AddBerth(BerthGenerator.AtPier(marina.GetPier("A")!, "A-L01", PierSide.Left, 0f, 5f, 12f)
            with
        { Label = "\U0001F6A2 \U0001F30A" });   // ship, wave
        marina.BerthLabelMode = BerthLabelMode.All;

        var frame = marina.BuildRenderFrame();

        Assert.NotEmpty(frame.Objects);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    [InlineData("عربي")]
    [InlineData("日本語")]
    public void AnyLabel_BuildsAFrameWithoutThrowing(string label)
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(800, 600);
        marina.AddPier(APier());
        marina.AddBerth(BerthGenerator.AtPier(marina.GetPier("A")!, "A-L01", PierSide.Left, 0f, 5f, 12f)
            with
        { Label = label });
        marina.BerthLabelMode = BerthLabelMode.All;

        Assert.NotEmpty(marina.BuildRenderFrame().Objects);
    }

    // ---- Numbers that are not numbers --------------------------------------------------------

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void APierWithANonFiniteHeading_IsRefusedWithAMessageThatNamesIt(float heading)
    {
        var marina = new MarinaVisualizer();

        var error = Assert.Throws<MarinaLayoutException>(() =>
            marina.AddPier(new Pier("A", "Pier A", Vector2.Zero, heading, 40f)));

        Assert.Contains("non-finite", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'A'", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A size of positive infinity passes a bare "greater than zero" test, so it used to reach the geometry and
    /// turn the object's transform into NaN. Sizes are checked for finiteness the way positions always were.
    /// </summary>
    [Fact]
    public void AnInfiniteSize_IsRefusedEverywhereASizeIsTaken()
    {
        var marina = new MarinaVisualizer();

        Assert.Throws<MarinaLayoutException>(() =>
            marina.AddPier(new Pier("A", "Pier A", Vector2.Zero, 0f, float.PositiveInfinity)));
        Assert.Throws<MarinaLayoutException>(() =>
            marina.AddPier(new Pier("B", "Pier B", Vector2.Zero, 0f, 40f, float.PositiveInfinity)));

        marina.AddPier(new Pier("C", "Pier C", Vector2.Zero, 0f, 40f));
        marina.AddBerth(BerthGenerator.AtPier(marina.GetPier("C")!, "C-L01", PierSide.Left, 0f, 5f, 12f));

        Assert.Throws<MarinaLayoutException>(() => marina.AssignBoat("C-L01",
            new Boat("B1", "Odd", BoatType.MotorYacht) { LengthMeters = float.PositiveInfinity, BeamMeters = 3f }));
        Assert.Throws<MarinaLayoutException>(() => marina.AssignBoat("C-L01",
            new Boat("B2", "Odd", BoatType.MotorYacht) { LengthMeters = 10f, BeamMeters = float.PositiveInfinity }));
    }

    [Fact]
    public void ACameraGivenAPoseOfNothingButNaN_RecoversToSomethingUsable()
    {
        var camera = new OrbitCamera();

        camera.SetPose(new CameraPose(new Vector3(float.NaN, float.NaN, float.NaN), float.NaN, float.NaN, float.NaN), immediate: true);

        Assert.True(float.IsFinite(camera.Pose.Distance), "distance should stay finite");
        Assert.True(float.IsFinite(camera.Pose.YawDegrees), "yaw should stay finite");
        Assert.True(float.IsFinite(camera.Pose.PitchDegrees), "pitch should stay finite");
        Assert.True(float.IsFinite(camera.Pose.Target.X), "the target should stay finite");
        Assert.True(float.IsFinite(camera.Position.X), "the eye should stay finite");
    }

    [Fact]
    public void AZeroSizedViewport_DoesNotDivideByZero()
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(0, 0);
        marina.AddPier(APier());

        var frame = marina.BuildRenderFrame();

        Assert.NotNull(frame);
        var ray = marina.Camera.ScreenPointToRay(0f, 0f, 0f, 0f);
        Assert.True(float.IsFinite(ray.Direction.X));
    }

    [Fact]
    public void StableHash_HandlesEmptyAndAstralStrings()
    {
        Assert.InRange(MarinaMath.StableHash01(string.Empty), 0f, 1f);
        Assert.InRange(MarinaMath.StableHash01("\U0001F6A2"), 0f, 1f);
        Assert.InRange(MarinaMath.StableHash01(new string('x', 10_000)), 0f, 1f);

        // The same input must give the same answer, or a marina looks different on every load.
        Assert.Equal(MarinaMath.StableHash01("A-L01"), MarinaMath.StableHash01("A-L01"));
    }

    // ---- Degenerate shapes -------------------------------------------------------------------

    [Fact]
    public void ALandAreaWithRepeatedPoints_IsReportedRatherThanDrawnWrong()
    {
        var repeated = new[] { new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0) };

        var land = new LandArea("yard", repeated, 1f);

        Assert.NotEmpty(land.Validate());
    }

    [Fact]
    public void ALandAreaWhoseOutlineEnclosesNothing_IsRefused()
    {
        var marina = new MarinaVisualizer();

        var error = Assert.Throws<MarinaLayoutException>(() => marina.AddLandArea(new LandArea("yard", new[]
        {
            new Vector2(0, 0), new Vector2(10, 0), new Vector2(20, 0), new Vector2(30, 0),
        }, 1f)));

        Assert.Contains("no area", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void APierOfZeroLength_IsRefused()
    {
        var marina = new MarinaVisualizer();

        var error = Assert.Throws<MarinaLayoutException>(() =>
            marina.AddPier(new Pier("A", "Pier A", Vector2.Zero, 0f, 0f)));

        Assert.Contains("positive", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PolygonArea_OfADegenerateOutline_IsZeroRatherThanNaN()
    {
        var degenerate = new[] { new Vector2(0, 0), new Vector2(1, 1), new Vector2(2, 2) };

        var area = PolygonMath.SignedArea(degenerate);

        Assert.True(float.IsFinite(area));
        Assert.Equal(0f, area, 4);
    }

    // ---- Round trips -------------------------------------------------------------------------

    [Theory]
    [InlineData("Καλαμάτα")]
    [InlineData("Pier \"A\"")]
    [InlineData("back\\slash")]
    [InlineData("line\nbreak")]
    [InlineData("\U0001F6A2 emoji")]
    public void AwkwardNames_SurviveASaveAndLoad(string name)
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", name, Vector2.Zero, 0f, 40f));

        var reloaded = MarinaDocument.Parse(MarinaDocument.FromVisualizer(marina).ToJson());

        Assert.Equal(name, reloaded.Layout.Piers.Single().Name);
    }

    [Fact]
    public void VeryLargeAndVerySmallCoordinates_SurviveASaveAndLoad()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(1e7f, -1e7f), 0f, 40f));
        marina.AddPier(new Pier("B", "Pier B", new Vector2(1e-7f, -1e-7f), 0f, 40f));

        var reloaded = MarinaDocument.Parse(MarinaDocument.FromVisualizer(marina).ToJson());

        var a = reloaded.Layout.Piers.Single(p => p.Id == "A");
        Assert.Equal(1e7f, a.Start.X, 0);
        // Coordinates are written rounded: a marina is measured in metres, and sub-millimetre precision
        // would only make the file bigger. A position far below that resolution comes back as zero.
        var b = reloaded.Layout.Piers.Single(p => p.Id == "B");
        Assert.Equal(0f, b.Start.X, 6);
    }

    [Fact]
    public void AnEmptyMarina_SavesAndLoadsAsAnEmptyMarina()
    {
        var json = MarinaDocument.FromVisualizer(new MarinaVisualizer()).ToJson();

        var reloaded = MarinaDocument.Parse(json);

        Assert.Empty(reloaded.Layout.Piers);
        Assert.Empty(reloaded.Layout.Berths);
    }

    // ---- Selection and multi-berths ----------------------------------------------------------

    [Fact]
    public void RemovingASelectedBerth_LeavesNoDanglingSelection()
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(800, 600);
        marina.AddPier(APier());
        marina.AddBerth(BerthGenerator.AtPier(marina.GetPier("A")!, "A-L01", PierSide.Left, 0f, 5f, 12f));
        Assert.True(marina.SelectBerth("A-L01"));

        marina.RemoveBerth("A-L01");

        Assert.Null(marina.SelectedBerth);
        Assert.Empty(marina.SelectedBerths);
        Assert.Null(marina.ActivePopup);
    }

    [Fact]
    public void RemovingAPierUnderASelectedBerth_LeavesNoDanglingSelection()
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(800, 600);
        marina.AddPier(APier());
        marina.AddBerth(BerthGenerator.AtPier(marina.GetPier("A")!, "A-L01", PierSide.Left, 0f, 5f, 12f));
        marina.SelectBerth("A-L01");

        marina.RemovePier("A");

        Assert.Null(marina.SelectedBerth);
        Assert.Empty(marina.SelectedBerths);
    }

    [Fact]
    public void SelectingTheSameBerthTwice_DoesNotSelectItTwice()
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(800, 600);
        marina.AddPier(APier());
        marina.AddBerth(BerthGenerator.AtPier(marina.GetPier("A")!, "A-L01", PierSide.Left, 0f, 5f, 12f));

        marina.SetSelection("A-L01", "A-L01", "a-l01");

        Assert.Single(marina.SelectedBerths);
    }

    [Fact]
    public void SelectingIdsThatDoNotExist_SaysSoRatherThanThrowing()
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(800, 600);

        var result = marina.SetSelection("nope", "also-nope");

        Assert.True(result.IsEmpty);
        Assert.Empty(marina.SelectedBerths);
    }
}
