using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Designer.Resources;

namespace VirtualMarina.Designer.Common.Tests;

/// <summary>The display text both apps show, worked out once.</summary>
public class DesignerTextTests
{
    [Theory]
    [InlineData("Porto Vecchio", "Porto Vecchio")]
    [InlineData("A/B: C?", "A-B- C")]
    [InlineData("  ..weird..  ", "weird")]
    [InlineData("<>:\"/\\|?*", "marina")]
    [InlineData("", "marina")]
    [InlineData(null, "marina")]
    [InlineData("tab\there", "tab-here")]
    public void File_names_lose_what_no_file_system_accepts(string? name, string expected) =>
        Assert.Equal(expected, DesignerText.SanitizeFileName(name));

    [Theory]
    [InlineData("&Save", "Save")]
    [InlineData("Save &As…", "Save As…")]
    [InlineData("Salt && Pepper", "Salt & Pepper")]
    [InlineData("Plain", "Plain")]
    public void Mnemonics_are_stripped(string label, string expected) => Assert.Equal(expected, DesignerText.StripMnemonic(label));

    [Fact]
    public void Every_tool_has_a_title()
    {
        foreach (var tool in Enum.GetValues<DesignTool>())
        {
            Assert.False(string.IsNullOrWhiteSpace(DesignerText.ToolTitle(tool)));
        }

        Assert.Equal(Strings.TitleCoast, DesignerText.ToolTitle(DesignTool.DrawShoreline));
    }

    [Fact]
    public void Naming_examples_use_the_first_pier_or_a_stand_in()
    {
        var marina = new MarinaVisualizer();
        var naming = BerthNamingScheme.Default;

        var standIn = DesignerText.NamingExample(marina, naming);
        marina.AddPier(new Pier("Q", "Pier Q", new Vector2(0, 0), 0f, 40f));
        var real = DesignerText.NamingExample(marina, naming);

        Assert.StartsWith(Strings.Format(Strings.BerthNamingExample, string.Empty), standIn, StringComparison.Ordinal);
        Assert.Contains("Q", real, StringComparison.Ordinal);
        Assert.Contains(",", DesignerText.AshoreNameExample(marina, naming), StringComparison.Ordinal);
    }

    [Fact]
    public void A_pattern_the_scheme_refuses_says_why()
    {
        Assert.Null(DesignerText.NamingProblem(BerthNamingScheme.Default));
        Assert.NotNull(DesignerText.NamingProblem(BerthNamingScheme.Default with { NumberDigits = 0 }));
    }

    [Fact]
    public void The_summary_counts_what_is_there()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        marina.Designer.CreateBerths("A", PierSide.Left, 0f, 15f);

        var summary = DesignerText.Summary(marina);

        Assert.StartsWith("3 ", summary, StringComparison.Ordinal);
        Assert.Contains(Strings.CoastNone, DesignerText.CoastState(marina), StringComparison.Ordinal);
    }

    [Fact]
    public void The_history_tips_say_what_would_be_undone_or_redone()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        var designer = marina.Designer;
        Assert.Equal(Strings.NothingToUndo, DesignerText.UndoTip(designer));
        Assert.Equal(Strings.NothingToRedo, DesignerText.RedoTip(designer));

        designer.CreateBerths("A", PierSide.Left, 0f, 15f);
        Assert.Equal(Strings.Format(Strings.UndoTip, designer.UndoDescription), DesignerText.UndoTip(designer));
        designer.Undo();
        Assert.Equal(Strings.Format(Strings.RedoTip, designer.RedoDescription), DesignerText.RedoTip(designer));
    }

    [Fact]
    public void The_image_state_follows_the_picture_and_the_scale_line()
    {
        var designer = new MarinaVisualizer().Designer;
        Assert.Equal(Strings.ImageStateEmpty, DesignerText.ImageState(designer));

        designer.SetReferenceImage(new ReferenceImage(4, 4, new byte[64]), 0.5f);
        Assert.NotEqual(Strings.ImageStateEmpty, DesignerText.ImageState(designer));
    }

    [Fact]
    public void Status_texts_and_versions_are_filled_in()
    {
        Assert.Equal(string.Empty, DesignerText.PointerText(null));
        Assert.NotEmpty(DesignerText.PointerText(new Vector2(1, 2)));
        Assert.NotEmpty(DesignerText.CameraText(new MarinaVisualizer().Camera.Pose));
        Assert.Matches(@"^\d+\.\d+\.\d+$", DesignerText.Version(typeof(DesignerText)));
        Assert.StartsWith(Strings.AppName, DesignerText.Generator(typeof(DesignerText)), StringComparison.Ordinal);
        Assert.Contains("Someone", DesignerText.About(typeof(DesignerText), "WebGL", "Someone", 2026), StringComparison.Ordinal);
        Assert.Equal(Strings.Format(Strings.WindowTitle, Strings.UnsavedMarker, "x", Strings.AppName), DesignerText.WindowTitle(true, "x"));
    }

    [Fact]
    public void Number_ranges_fit_values_into_what_a_field_can_show()
    {
        var range = DesignerRanges.BerthWidth;

        Assert.Equal(range.Min, range.Fit(-3f));
        Assert.Equal(range.Max, range.Fit(1e30f));
        Assert.Equal(range.Min, range.Fit(float.NaN));
        Assert.Equal(5.25m, range.Fit(5.2501f));
        Assert.Equal("0.25", range.StepText);
        Assert.Equal("100", DesignerRanges.TreeDensity.MaxText);
    }

    [Fact]
    public void Number_ranges_of_designer_settings_are_the_designers_own()
    {
        static void Same(DesignerSettingRange core, NumberRange field, decimal scale = 1m)
        {
            Assert.Equal((decimal)core.Minimum * scale, field.Min);
            Assert.Equal((decimal)core.Maximum * scale, field.Max);
        }

        Same(DesignerLimits.LandHeight, DesignerRanges.LandHeight);
        Same(DesignerLimits.PierWidth, DesignerRanges.PierWidth);
        Same(DesignerLimits.BerthWidth, DesignerRanges.BerthWidth);
        Same(DesignerLimits.BerthLength, DesignerRanges.BerthLength);
        Same(DesignerLimits.BerthDepth, DesignerRanges.BerthDepth);
        Same(DesignerLimits.BerthGap, DesignerRanges.BerthGap);
        Same(DesignerLimits.LandBerthHeading, DesignerRanges.LandBerthHeading);
        Same(DesignerLimits.TreeDensity, DesignerRanges.TreeDensity);
        Same(DesignerLimits.ReferenceImageOpacity, DesignerRanges.ImageOpacityPercent, 100m);
        Same(DesignerLimits.ReferenceImageMetersPerPixel, DesignerRanges.MetersPerPixel);
        Assert.Equal(0.1m, DesignerRanges.BerthDepth.Min); // a float limit comes over as the decimal it was written as
        Assert.Equal(DesignerLimits.FogFactor.Default, DesignerSession.DesigningFogFactor);
    }

    [Fact]
    public void Choices_are_described_by_the_core_library()
    {
        Assert.Equal(DividerType.Piles.GetDisplayName(), DesignerChoices.Describe(DividerType.Piles));
        Assert.Equal(Pier.GetDisplayName(PierType.FloatingWooden), DesignerChoices.Describe(PierType.FloatingWooden));
        Assert.Equal(DesignerChoices.DividerTypes.Count, DesignerChoices.DividerTypes.Distinct().Count());
        Assert.Equal(Enum.GetValues<DividerType>().Length, DesignerChoices.DividerTypes.Count);
    }

    [Fact]
    public void Erased_elements_are_named_the_way_a_person_would()
    {
        var pier = new Pier("A", "Pier A", Vector2.Zero, 0f, 20f);

        Assert.Equal(Strings.Format(Strings.ElementPier, "A"), DesignerLogText.Describe(pier));
        var text = DesignerLogText.Erased(new DesignElementErasedEventArgs(pier, [], []));
        Assert.Equal(Strings.Format(Strings.LogRemoved, Strings.Format(Strings.ElementPier, "A"), 0, 0), text);
        Assert.Null(DesignerLogText.Created(new DesignElementCreatedEventArgs(DesignTool.AddBerths, null, null, [], [])));
    }
}
