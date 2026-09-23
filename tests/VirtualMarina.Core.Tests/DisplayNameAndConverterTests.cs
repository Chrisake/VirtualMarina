using System.Numerics;
using System.Text.Json;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Rendering;
using VirtualMarina.Core.Serialization;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// The edges a host meets rather than the marina itself: the words put in front of enum values, the shapes the
/// file format accepts for a point or a colour, and the exceptions thrown when either is wrong.
/// </summary>
public class DisplayNameAndConverterTests
{
    // ---- DisplayNames ------------------------------------------------------------------------

    public static TheoryData<Enum> EveryEnumMemberWithADisplayName()
    {
        var data = new TheoryData<Enum>();
        foreach (var value in Enum.GetValues<DesignTool>()) data.Add(value);
        foreach (var value in Enum.GetValues<BerthSeparator>()) data.Add(value);
        foreach (var value in Enum.GetValues<PierServices>()) data.Add(value);
        foreach (var value in Enum.GetValues<PierSides>()) data.Add(value);
        foreach (var value in Enum.GetValues<LandKind>()) data.Add(value);
        foreach (var value in Enum.GetValues<HinterlandScenery>()) data.Add(value);
        foreach (var value in Enum.GetValues<DividerType>()) data.Add(value);
        foreach (var value in Enum.GetValues<MooringStyle>()) data.Add(value);
        return data;
    }

    /// <summary>
    /// Every member of every enum the API puts in front of a user has to have something to show. A member added
    /// without a case in the switch would otherwise reach the UI as a bare identifier, or as nothing at all.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryEnumMemberWithADisplayName))]
    public void EveryDisplayName_IsNonEmpty(Enum value)
    {
        var name = value switch
        {
            DesignTool tool => tool.GetDisplayName(),
            BerthSeparator separator => separator.GetDisplayName(),
            PierServices services => services.GetDisplayName(),
            PierSides sides => sides.GetDisplayName(),
            LandKind kind => kind.GetDisplayName(),
            HinterlandScenery scenery => scenery.GetDisplayName(),
            DividerType type => type.GetDisplayName(),
            MooringStyle style => style.GetDisplayName(),
            _ => throw new InvalidOperationException($"No display name tested for {value.GetType().Name}."),
        };

        Assert.False(string.IsNullOrWhiteSpace(name), $"{value.GetType().Name}.{value} has no display name");
    }

    [Fact]
    public void DisplayName_OfAValueOutsideTheEnum_FallsBackInsteadOfThrowing()
    {
        // A file from a newer version can carry a number this build has no name for.
        Assert.False(string.IsNullOrEmpty(((DesignTool)999).GetDisplayName()));
        Assert.False(string.IsNullOrEmpty(((LandKind)999).GetDisplayName()));
        Assert.False(string.IsNullOrEmpty(((DividerType)999).GetDisplayName()));
    }

    [Fact]
    public void PierServices_DisplayName_ListsTheCombination()
    {
        var both = (PierServices.Power | PierServices.Water).GetDisplayName();
        var none = PierServices.None.GetDisplayName();

        Assert.False(string.IsNullOrWhiteSpace(both));
        Assert.False(string.IsNullOrWhiteSpace(none));
        Assert.NotEqual(none, both);
    }

    // ---- Point and colour shapes in the file format ------------------------------------------

    /// <summary>A point written as an array reads back as the same point.</summary>
    [Fact]
    public void Vector2_RoundTripsThroughTheDocument()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(12.5f, -7.25f), 33f, 40f));

        var reloaded = MarinaDocument.Parse(MarinaDocument.FromVisualizer(marina).ToJson());

        var pier = reloaded.Layout.Piers.Single();
        Assert.Equal(12.5f, pier.Start.X, 4);
        Assert.Equal(-7.25f, pier.Start.Y, 4);
        Assert.Equal(33f, pier.HeadingDegrees, 4);
    }

    /// <summary>
    /// The reader also accepts the object form a hand-written or older file may use. The writer only ever emits
    /// the array form, so this is the leg of the contract nothing else exercises.
    /// </summary>
    [Theory]
    [InlineData("{ \"x\": 3, \"y\": 4 }")]
    [InlineData("{ \"X\": 3, \"Y\": 4 }")]
    [InlineData("{ \"y\": 4, \"x\": 3 }")]
    [InlineData("{ \"x\": 3, \"y\": 4, \"z\": 99 }")]
    [InlineData("[3, 4]")]
    public void Vector2_IsReadFromEitherAnArrayOrAnObject(string point)
    {
        var json = MarinaDocument.FromVisualizer(WithAPierAt(Vector2.Zero)).ToJson();
        var patched = ReplaceFirstPierStart(json, point);

        var pier = MarinaDocument.Parse(patched).Layout.Piers.Single();

        Assert.Equal(3f, pier.Start.X, 4);
        Assert.Equal(4f, pier.Start.Y, 4);
    }

    [Fact]
    public void Vector2_MissingComponentsReadAsZero()
    {
        var json = MarinaDocument.FromVisualizer(WithAPierAt(new Vector2(9f, 9f))).ToJson();
        var patched = ReplaceFirstPierStart(json, "{ \"x\": 5 }");

        var pier = MarinaDocument.Parse(patched).Layout.Piers.Single();

        Assert.Equal(5f, pier.Start.X, 4);
        Assert.Equal(0f, pier.Start.Y, 4);
    }

    private static MarinaVisualizer WithAPierAt(Vector2 start)
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", start, 0f, 40f));
        return marina;
    }

    /// <summary>Swaps the value of the first pier's <c>start</c> for another JSON shape.</summary>
    private static string ReplaceFirstPierStart(string json, string replacement)
    {
        const string key = "\"start\":";
        var at = json.IndexOf(key, StringComparison.Ordinal);
        Assert.True(at >= 0, "the document should carry a pier start");

        var start = at + key.Length;
        while (char.IsWhiteSpace(json[start])) start++;

        var end = start;
        var depth = 0;
        do
        {
            if (json[end] is '[' or '{') depth++;
            else if (json[end] is ']' or '}') depth--;
            end++;
        }
        while (depth > 0);

        return json[..start] + replacement + json[end..];
    }

    // ---- Exceptions --------------------------------------------------------------------------

    [Fact]
    public void MarinaLayoutException_CarriesTheProblemsItWasGiven()
    {
        var exception = new MarinaLayoutException(new[] { "Duplicate berth id 'A-L01'.", "Pier 'B' is missing." });

        Assert.Equal(2, exception.Errors.Count);
        Assert.Contains("A-L01", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>The three constructors every exception is expected to have, so it behaves for callers wrapping it.</summary>
    [Fact]
    public void MarinaLayoutException_HasTheStandardConstructors()
    {
        Assert.Empty(new MarinaLayoutException().Errors);

        var withMessage = new MarinaLayoutException("Broken.");
        Assert.Equal("Broken.", withMessage.Message);
        Assert.Empty(withMessage.Errors);

        var inner = new InvalidOperationException("underneath");
        var wrapped = new MarinaLayoutException("Broken.", inner);
        Assert.Same(inner, wrapped.InnerException);
        Assert.Empty(wrapped.Errors);
    }

    [Fact]
    public void MarinaFormatException_HasTheStandardConstructors()
    {
        Assert.Equal("Not a marina.", new MarinaFormatException("Not a marina.").Message);

        var inner = new JsonException("bad token");
        var wrapped = new MarinaFormatException("Not a marina.", inner);
        Assert.Same(inner, wrapped.InnerException);

        Assert.False(string.IsNullOrEmpty(new MarinaFormatException().Message));
    }

    [Fact]
    public void Parsing_RefusesWhatItCannotMeaningfullyRead()
    {
        Assert.Throws<MarinaFormatException>(() => MarinaDocument.Parse("not json at all"));
        Assert.Throws<MarinaFormatException>(() => MarinaDocument.Parse("null"));

        // A file that says it is something else is refused even though it parses.
        var foreign = "{ \"format\": \"someone.elses.format\", \"formatVersion\": \"1.0\" }";
        var wrongFormat = Assert.Throws<MarinaFormatException>(() => MarinaDocument.Parse(foreign));
        Assert.Contains("someone.elses.format", wrongFormat.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reading is deliberately forgiving (Docs/13-marina-file-format.md): JSON with nothing this version
    /// recognises in it loads as an empty marina rather than throwing, so a file keeps working as the format moves.
    /// </summary>
    [Fact]
    public void Parsing_JsonWithNoFormatMarker_LoadsAsAnEmptyMarina()
    {
        var document = MarinaDocument.Parse("{ \"hello\": 1 }");

        Assert.Empty(document.Layout.Piers);
        Assert.Empty(document.Layout.Berths);
        Assert.Equal("Marina", document.Name);
    }

    [Fact]
    public void Parsing_AFileFromANewerMajorVersion_IsRefusedUnlessAllowed()
    {
        var newer = "{ \"format\": \"" + MarinaDocument.FormatName + "\", \"formatVersion\": \"99.0\" }";

        Assert.Throws<MarinaFormatException>(() => MarinaDocument.Parse(newer));

        var forced = MarinaDocument.Parse(newer, allowNewerVersion: true);
        Assert.True(forced.IsFromNewerVersion);
        Assert.Equal(99, forced.Version.Major);
    }
}
