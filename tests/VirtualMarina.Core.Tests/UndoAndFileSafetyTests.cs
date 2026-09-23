using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Rendering;
using VirtualMarina.Core.Serialization;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// Ways a design could be lost: an undo that crashed half way through, a right-click that threw away a drawing, and files
/// that saved but would not open again, or opened as something other than what was saved.
/// </summary>
public class UndoAndFileSafetyTests
{
    // ---- Undoing renames ------------------------------------------------------------------------

    /// <summary>A pier with a row of berths down its left side, numbered from <paramref name="start"/>.</summary>
    private static MarinaVisualizer WithARow(int start, out MarinaDesigner designer)
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(1000, 800);
        marina.Camera.SetPose(new CameraPose(Vector3.Zero, 0f, 89f, 160f), immediate: true);
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));

        designer = marina.Designer;
        designer.IsActive = true;
        designer.BerthWidth = 5f;
        designer.BerthLength = 12f;
        designer.CreateBerths("A", PierSide.Left, 0f, 60f);
        if (start != 1)
        {
            designer.BerthNaming = designer.BerthNaming with { StartNumber = start };
            designer.ApplyBerthNames(designer.PlanBerthNames("A", "{pier}-{side}{number}"));
            designer.BerthNaming = designer.BerthNaming with { StartNumber = 1 };
        }

        designer.ClearHistory();
        return marina;
    }

    private static string[] Ids(MarinaVisualizer marina) => marina.GetBerthsByPier("A").Select(berth => berth.Id).ToArray();

    [Fact]
    public void RenumberingDownThePier_ThenUndo_PutsEveryNameBack()
    {
        var marina = WithARow(start: 2, out var designer);
        var before = Ids(marina);
        Assert.Equal("A-L02", before[0]);

        // A-L02 becomes A-L01, A-L03 becomes A-L02, ...: undone one at a time in that order, the first step landed on a
        // name the second berth still held, and the undo threw half way through with the change already off the history.
        designer.ApplyBerthNames(designer.PlanBerthNames("A", "{pier}-{side}{number}"));
        Assert.Equal("A-L01", Ids(marina)[0]);

        Assert.True(designer.Undo());
        Assert.Equal(before, Ids(marina));
        Assert.False(designer.CanUndo);
    }

    [Fact]
    public void SwappingTwoNames_ThenUndo_SwapsThemBack()
    {
        var marina = WithARow(start: 1, out var designer);
        var before = Ids(marina);
        var first = marina.GetBerth("A-L01")!;
        var second = marina.GetBerth("A-L02")!;

        designer.ApplyBerthNames(new BerthNamePlan("A", "swap", [("A-L01", "A-L02"), ("A-L02", "A-L01")], []));
        Assert.Equal(first.Center, marina.GetBerth("A-L02")!.Center);
        Assert.Equal(second.Center, marina.GetBerth("A-L01")!.Center);

        Assert.True(marina.Input.KeyDown(MarinaKey.Undo));
        Assert.Equal(before, Ids(marina));
        Assert.Equal(first.Center, marina.GetBerth("A-L01")!.Center);
        Assert.Equal(second.Center, marina.GetBerth("A-L02")!.Center);
    }

    [Fact]
    public void ApplyingNames_RaisesOneRenameEachAndNeverShowsAPassingName()
    {
        var marina = WithARow(start: 2, out var designer);
        var count = Ids(marina).Length;
        var renamed = new List<string>();
        marina.LayoutChanged += (_, e) =>
        {
            if (e.Kind == LayoutChangeKind.BerthRenamed) renamed.Add(e.BerthId!);
        };

        designer.ApplyBerthNames(designer.PlanBerthNames("A", "{pier}-{side}{number}"));

        // It used to park every berth under a made-up "~1a2b3c4d-0" first, and told the host about both moves.
        Assert.Equal(count, renamed.Count);
        Assert.All(renamed, id => Assert.StartsWith("A-L", id, StringComparison.Ordinal));
    }

    [Fact]
    public void RenamingSeveralBerths_IsAllOrNothing()
    {
        var marina = WithARow(start: 1, out _);
        var before = Ids(marina);

        // The second rename lands on a berth that stays where it is, so neither happens.
        var refused = Assert.Throws<InvalidOperationException>(() => marina.RenameBerths([("A-L01", "X"), ("A-L02", "A-L03")]));
        Assert.Contains("A-L03", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, Ids(marina));

        // Two berths onto one name, and one berth named twice.
        Assert.Throws<InvalidOperationException>(() => marina.RenameBerths([("A-L01", "X"), ("A-L02", "x")]));
        Assert.Throws<ArgumentException>(() => marina.RenameBerths([("A-L01", "X"), ("A-L01", "Y")]));
        Assert.Throws<KeyNotFoundException>(() => marina.RenameBerths([("nope", "X")]));
        Assert.Equal(before, Ids(marina));
    }

    [Fact]
    public void AnUndoTheHostHasMadeImpossible_KeepsTheChangeAndDoesNotThrowIntoTheInputLoop()
    {
        var marina = WithARow(start: 1, out var designer);
        var berth = marina.GetBerth("A-L01")!;
        Assert.True(designer.Erase(berth));
        Assert.True(designer.CanUndo);

        // The pier the erased berth belonged to is gone, so the berth cannot come back.
        marina.RemovePier("A");

        Assert.Throws<MarinaLayoutException>(() => designer.Undo());
        Assert.True(designer.CanUndo);

        // From the keyboard it is reported as "nothing done" rather than thrown at the host's key handler.
        var ex = Record.Exception(() => marina.Input.KeyDown(MarinaKey.Undo));
        Assert.Null(ex);
        Assert.True(designer.CanUndo);
    }

    [Fact]
    public void AnOldNameTheHostHasTakenSince_IsLeftWithTheHost()
    {
        var marina = WithARow(start: 2, out var designer);
        var last = Ids(marina)[^1];
        designer.ApplyBerthNames(designer.PlanBerthNames("A", "{pier}-{side}{number}"));

        // The host puts a berth of its own on the name the last one had before the renumber.
        marina.AddBerth(last, "A", new Vector2(40, 40), 0f, 10f, 4f);

        Assert.True(designer.Undo());
        Assert.NotNull(marina.GetBerth(last));
        Assert.Equal(Ids(marina).Length, Ids(marina).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ---- Right-click and Enter while drawing -------------------------------------------------------

    private static MarinaVisualizer Drawing(DesignTool tool)
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(1000, 800);
        marina.Camera.SetPose(new CameraPose(Vector3.Zero, 0f, 89f, 150f), immediate: true);
        marina.Designer.IsActive = true;
        marina.Designer.Tool = tool;
        return marina;
    }

    private static void Click(MarinaVisualizer marina, Vector2 plan, PointerButton button = PointerButton.Left)
    {
        Assert.True(marina.TryProjectToScreen(MarinaMath.ToWorld(plan), out var s));
        marina.Input.PointerMove(s.X, s.Y);
        marina.Input.PointerDown(s.X, s.Y, button);
        marina.Input.PointerUp(s.X, s.Y, button);
    }

    [Fact]
    public void RightClickOnACoast_SettlesItRatherThanThrowingItAway()
    {
        var marina = Drawing(DesignTool.DrawShoreline);
        Click(marina, new Vector2(-60, -20));
        Click(marina, new Vector2(0, -30));
        Click(marina, new Vector2(60, -20));

        Click(marina, new Vector2(60, -20), PointerButton.Right);

        Assert.Equal(3, marina.Designer.ShorelineAwaitingSide?.Count);
        Click(marina, new Vector2(0, -120));
        Assert.NotNull(marina.Shoreline);
    }

    [Fact]
    public void EnterSettlingACoast_SaysTheKeyWasUsed()
    {
        var marina = Drawing(DesignTool.DrawShoreline);
        Click(marina, new Vector2(-60, -20));
        Click(marina, new Vector2(60, -20));

        Assert.True(marina.Input.KeyDown(MarinaKey.Enter));
        Assert.NotNull(marina.Designer.ShorelineAwaitingSide);
    }

    [Fact]
    public void RightClickOnAnOutlineThatCrossesItself_KeepsTheCorners()
    {
        var marina = Drawing(DesignTool.DrawLandArea);

        // A bow tie: the third corner takes the edge back across the first.
        foreach (var p in new[] { new Vector2(-30, -30), new Vector2(30, 30), new Vector2(30, -30), new Vector2(-30, 30) }) Click(marina, p);
        Click(marina, new Vector2(-30, 30), PointerButton.Right);

        Assert.Empty(marina.GetLandAreas());
        Assert.Equal(4, marina.Designer.DraftPoints.Count);

        // Taking back the corner at fault and finishing works.
        Assert.True(marina.Input.KeyDown(MarinaKey.Backspace));
        Click(marina, new Vector2(-30, 30), PointerButton.Right);
        Assert.Single(marina.GetLandAreas());
    }

    [Fact]
    public void RightClickWithNothingToFinish_StillDropsTheDrawing()
    {
        var marina = Drawing(DesignTool.DrawLandArea);
        Click(marina, new Vector2(-30, -30));
        Click(marina, new Vector2(30, -30));

        Click(marina, new Vector2(30, 30), PointerButton.Right);

        Assert.False(marina.Designer.HasDraft);
        Assert.Empty(marina.GetLandAreas());
    }

    // ---- The water grid -------------------------------------------------------------------------------

    private static string WithPresentation(Action<JsonObject> edit)
    {
        var node = JsonNode.Parse(MarinaDocument.FromVisualizer(new MarinaVisualizer()).ToJson())!.AsObject();
        edit(node["presentation"]!.AsObject());
        return node.ToJsonString();
    }

    [Theory]
    [InlineData("500", 400)]
    [InlineData("1000000000", 400)]
    [InlineData("1e9", 400)]
    [InlineData("-3", 160)]
    [InlineData("1", 160)]
    [InlineData("64", 64)]
    public void AGridResolutionFromAFile_IsHeldToWhatTheVisualizerBuilds(string written, int expected)
    {
        var json = WithPresentation(p => p["water"]!["gridResolution"] = JsonNode.Parse(written));

        var document = MarinaDocument.Parse(json);
        Assert.Equal(expected, document.Style.Water.GridResolution);

        // The grid is built from it when a visualizer is created, and grows from it when the layout is large.
        var marina = new MarinaVisualizer(new MarinaVisualizerOptions { Water = document.Style.Water });
        marina.AddPier(new Pier("FAR", "Far", new Vector2(20000, 0), 0f, 50f));
        Assert.True(marina.Meshes.TryGet(Geometry.MeshIds.Water, out _));
    }

    [Fact]
    public void WaterSettings_HoldTheGridResolutionInRange()
    {
        Assert.Equal(400, new WaterSettings { GridResolution = int.MaxValue }.GridResolution);
        Assert.Equal(2, new WaterSettings { GridResolution = -5 }.GridResolution);
    }

    // ---- Numbers that are not numbers -------------------------------------------------------------------

    [Fact]
    public void ANonFiniteCameraPose_SavesToAFileThatOpens()
    {
        var document = MarinaDocument.FromVisualizer(new MarinaVisualizer());
        document.Camera = new CameraPose(new Vector3(float.NaN, 2f, float.PositiveInfinity), float.NaN, 40f, float.NegativeInfinity);
        document.CameraPresets = [new CameraPreset("Odd", new CameraPose(new Vector3(float.NegativeInfinity, 0f, 1f), 0f, 40f, 100f))];

        var json = document.ToJson();
        Assert.DoesNotContain("[NaN", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Infinity,", json, StringComparison.Ordinal);

        var reloaded = MarinaDocument.Parse(json);
        Assert.Equal(new Vector3(0f, 2f, 0f), reloaded.Camera!.Value.Target);
        Assert.Equal(new Vector3(0f, 0f, 1f), reloaded.CameraPresets[0].Pose.Target);
    }

    [Fact]
    public void ImpossibleLightAndWaterSettings_ComeBackInRange()
    {
        var json = WithPresentation(p =>
        {
            var lighting = p["lighting"]!.AsObject();
            lighting["specularStrength"] = "NaN";
            lighting["shininess"] = -8;
            lighting["fogDensity"] = "-Infinity";
            lighting["sunColor"] = JsonNode.Parse("[\"NaN\", -1, 99]");
            lighting["sunDirection"] = JsonNode.Parse("[\"Infinity\", 1, 0]");

            var water = p["water"]!.AsObject();
            water["waveAmplitude"] = -2;
            water["waveSpeed"] = "Infinity";
            water["skyReflection"] = 7;
            water["size"] = -100;
        });

        var style = MarinaDocument.Parse(json).Style;
        var defaults = new MarinaStyle();

        Assert.Equal(defaults.Lighting.SpecularStrength, style.Lighting.SpecularStrength);
        Assert.Equal(1f, style.Lighting.Shininess);
        Assert.Equal(defaults.Lighting.FogDensity, style.Lighting.FogDensity);
        Assert.Equal(new Vector3(defaults.Lighting.SunColor.X, 0f, LightingSettings.MaxColorChannel), style.Lighting.SunColor);
        Assert.Equal(Vector3.UnitY, style.Lighting.SunDirection);
        Assert.Equal(0f, style.Water.WaveAmplitude);
        Assert.Equal(defaults.Water.WaveSpeed, style.Water.WaveSpeed);
        Assert.Equal(1f, style.Water.SkyReflection);
        Assert.Equal(defaults.Water.Size, style.Water.Size);

        // And the clone the visualizer takes keeps them as they are.
        var clone = style.Clone();
        Assert.Equal(style.Lighting.SunColor, clone.Lighting.SunColor);
        Assert.Equal(style.Water.WaveAmplitude, clone.Water.WaveAmplitude);
    }

    [Fact]
    public void SettingsSetInCode_AreHeldInRangeToo()
    {
        var lighting = new LightingSettings { FogDensity = float.NaN, Shininess = 1e9f, SpecularStrength = -1f, AmbientColor = new Vector3(float.NaN, 0.5f, -3f) };
        Assert.Equal(0.0022f, lighting.FogDensity);
        Assert.Equal(1024f, lighting.Shininess);
        Assert.Equal(0f, lighting.SpecularStrength);
        Assert.Equal(new Vector3(new LightingSettings().AmbientColor.X, 0.5f, 0f), lighting.AmbientColor);

        var water = new WaterSettings { Size = float.PositiveInfinity, WaveFrequency = -1f, Ripples = 9f, SunGlints = float.NaN, BoatMotion = 4f };
        Assert.Equal(4200f, water.Size);
        Assert.Equal(0f, water.WaveFrequency);
        Assert.Equal(2f, water.Ripples);
        Assert.Equal(1f, water.SunGlints);
        Assert.Equal(3f, water.BoatMotion);
    }

    // ---- Black ------------------------------------------------------------------------------------------

    [Fact]
    public void BlackLightAndWaterColors_SurviveTheRoundTrip()
    {
        var marina = new MarinaVisualizer();
        marina.Style.Lighting.AmbientColor = Vector3.Zero;
        marina.Style.Lighting.FogColor = Vector3.Zero;
        marina.Style.Lighting.SkyColor = Vector3.Zero;
        marina.Style.Lighting.SunColor = Vector3.Zero;
        marina.Style.Water.DeepColor = Vector3.Zero;
        marina.Style.Water.ShallowColor = Vector3.Zero;

        var style = MarinaDocument.Parse(MarinaDocument.FromVisualizer(marina).ToJson()).Style;

        Assert.Equal(Vector3.Zero, style.Lighting.AmbientColor);
        Assert.Equal(Vector3.Zero, style.Lighting.FogColor);
        Assert.Equal(Vector3.Zero, style.Lighting.SkyColor);
        Assert.Equal(Vector3.Zero, style.Lighting.SunColor);
        Assert.Equal(Vector3.Zero, style.Water.DeepColor);
        Assert.Equal(Vector3.Zero, style.Water.ShallowColor);
    }

    [Fact]
    public void ColorsTheFileLeavesOutOrGetsWrong_KeepTheirDefaults()
    {
        var json = WithPresentation(p =>
        {
            var lighting = p["lighting"]!.AsObject();
            lighting.Remove("ambientColor");
            lighting["fogColor"] = "not a color";
            p["water"]!.AsObject().Remove("deepColor");
        });

        var style = MarinaDocument.Parse(json).Style;
        var defaults = new MarinaStyle();
        Assert.Equal(defaults.Lighting.AmbientColor, style.Lighting.AmbientColor);
        Assert.Equal(defaults.Lighting.FogColor, style.Lighting.FogColor);
        Assert.Equal(defaults.Water.DeepColor, style.Water.DeepColor);
    }

    // ---- Extensions ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("layout")]
    [InlineData("presentation")]
    [InlineData("Camera")]
    [InlineData("FORMATVERSION")]
    [InlineData("disabledCameraPresets")]
    [InlineData("savedUtc")]
    public void AnExtensionUnderANameTheFormatUses_IsRefused(string key)
    {
        var document = MarinaDocument.FromVisualizer(new MarinaVisualizer());
        using var json = JsonDocument.Parse("{\"x\":1}");

        var viaHelper = Assert.Throws<ArgumentException>(() => document.SetExtension(key, 1));
        Assert.Contains(key, viaHelper.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => document.Extensions[key] = json.RootElement.Clone());
        Assert.Throws<ArgumentException>(() => document.Extensions.Add(key, json.RootElement.Clone()));
        Assert.Throws<ArgumentException>(() => document.Extensions.Add(new KeyValuePair<string, JsonElement>(key, json.RootElement.Clone())));
        Assert.Empty(document.Extensions);
    }

    [Fact]
    public void AnExtensionUnderAnyOtherName_RoundTrips()
    {
        var document = MarinaDocument.FromVisualizer(new MarinaVisualizer());
        document.SetExtension("acme.layout", 7);
        document.SetExtension("extra", "kept");

        var reloaded = MarinaDocument.Parse(document.ToJson());

        Assert.Equal(7, reloaded.GetExtension<int>("acme.layout"));
        Assert.Equal("kept", reloaded.GetExtension<string>("extra"));
        Assert.Equal(2, reloaded.Extensions.Count);
    }

    [Fact]
    public void AFileWithAReservedNameInAnotherCase_ReadsItAsTheRealThing_NotAsAnExtension()
    {
        var node = JsonNode.Parse(MarinaDocument.FromVisualizer(new MarinaVisualizer()).ToJson())!.AsObject();
        var camera = node["camera"]!.DeepClone();
        node.Remove("camera");
        node["CAMERA"] = camera;

        var document = MarinaDocument.Parse(node.ToJsonString());

        Assert.NotNull(document.Camera);
        Assert.Empty(document.Extensions);
    }

    // ---- Enums -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("\"42\"")]
    [InlineData("42")]
    [InlineData("\"-1\"")]
    public void ANumberForAnEnumThisVersionHasNoNameFor_FallsBackToTheDefault(string written)
    {
        var json = WithPresentation(p => p["berthLabels"] = JsonNode.Parse(written));

        Assert.Equal(BerthLabelMode.None, MarinaDocument.Parse(json).BerthLabels);
    }

    [Fact]
    public void ANumberForAKnownEnumValue_IsStillRead()
    {
        var json = WithPresentation(p => p["berthLabels"] = "3");

        Assert.Equal(BerthLabelMode.All, MarinaDocument.Parse(json).BerthLabels);
    }
}
