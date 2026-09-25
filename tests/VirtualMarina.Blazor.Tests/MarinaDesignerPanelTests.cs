using System.Numerics;
using VirtualMarina.Blazor;
using VirtualMarina.Blazor.Resources;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.Blazor.Tests;

/// <summary>The Blazor designer panel: its controls drive <see cref="MarinaDesigner"/>, and it follows the designer back.</summary>
public class MarinaDesignerPanelTests
{
    private static MarinaVisualizer CreateMarina()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        return marina;
    }

    // The panel's only use of JS is the InputFile it contains, which the fake answers with nothing.
    private static Task<Rendered<MarinaDesignerPanel>> RenderAsync(MarinaVisualizer marina, FakeJsModule module) =>
        TestRenderer.RenderAsync<MarinaDesignerPanel>(new FakeJsRuntime(module), new Dictionary<string, object?> { [nameof(MarinaDesignerPanel.Marina)] = marina });

    private static TestElement ToolButton(Rendered<MarinaDesignerPanel> panel, DesignTool tool) =>
        Assert.Single(panel.Elements(), e => e.Name == "button" && e.Text == tool.GetDisplayName());

    /// <summary>A number field, told apart from the others by its range, which is the one thing that makes it what it is.</summary>
    private static TestElement NumberField(Rendered<MarinaDesignerPanel> panel, string min, string max) =>
        Assert.Single(panel.Elements(), e => e.Name == "input" && e.Attribute("type") == "number" && e.Attribute("min") == min && e.Attribute("max") == max);

    [Fact]
    public async Task ChoosingATool_TurnsOnDesignMode_AndMarksTheToolActive()
    {
        var marina = CreateMarina();
        using var module = new FakeJsModule();
        using var panel = await RenderAsync(marina, module);
        Assert.False(marina.Designer.IsActive);
        Assert.DoesNotContain(panel.Elements(), e => e.HasClass("vm-designer__tool--active"));

        await panel.Renderer.ClickAsync(ToolButton(panel, DesignTool.AddBerths));

        Assert.True(marina.Designer.IsActive);
        Assert.Equal(DesignTool.AddBerths, marina.Designer.Tool);
        Assert.True(ToolButton(panel, DesignTool.AddBerths).HasClass("vm-designer__tool--active"));
        Assert.False(ToolButton(panel, DesignTool.DrawPier).HasClass("vm-designer__tool--active"));
    }

    [Fact]
    public async Task EveryToolTheDesignerHas_HasAButton_ThatPicksIt()
    {
        var marina = CreateMarina();
        using var module = new FakeJsModule();
        using var panel = await RenderAsync(marina, module);

        foreach (var tool in Enum.GetValues<DesignTool>())
        {
            var button = ToolButton(panel, tool);
            if (tool is DesignTool.MoveReferenceImage or DesignTool.MeasureScale)
            {
                Assert.NotNull(button.Attribute("disabled")); // nothing to move or measure without an image
                continue;
            }

            await panel.Renderer.ClickAsync(button);
            Assert.Equal(tool, marina.Designer.Tool);
            Assert.True(ToolButton(panel, tool).HasClass("vm-designer__tool--active"));
        }
    }

    [Fact]
    public async Task Redo_IsOfferedWhenTheDesignerCanRedo_AndMakesTheChangeAgain()
    {
        var marina = CreateMarina();
        using var module = new FakeJsModule();
        using var panel = await RenderAsync(marina, module);
        TestElement Button(string text) => Assert.Single(panel.Elements(), e => e.Name == "button" && e.Text == text);
        Assert.NotNull(Button(Strings.Redo).Attribute("disabled"));

        await panel.Renderer.InvokeAsync(() => marina.Designer.CreateBerths("A", PierSide.Left, 0f, 10f));
        await panel.Renderer.ClickAsync(Button(Strings.Undo));
        Assert.Empty(marina.GetBerths());
        Assert.Null(Button(Strings.Redo).Attribute("disabled"));
        Assert.Contains(panel.Elements(), e => e.Text == marina.Designer.RedoDescription);

        await panel.Renderer.ClickAsync(Button(Strings.Redo));

        Assert.NotEmpty(marina.GetBerths());
        Assert.NotNull(Button(Strings.Redo).Attribute("disabled"));
    }

    [Fact]
    public async Task Undo_WhileDrawing_TakesBackTheLastPoint()
    {
        var marina = CreateMarina();
        marina.SetViewportSize(1000, 800);
        marina.Camera.SetPose(new VirtualMarina.Core.Camera.CameraPose(Vector3.Zero, 0f, 89f, 150f), immediate: true);
        using var module = new FakeJsModule();
        using var panel = await RenderAsync(marina, module);
        await panel.Renderer.ClickAsync(ToolButton(panel, DesignTool.DrawLandArea));
        await panel.Renderer.InvokeAsync(() =>
        {
            foreach (var x in new[] { -40f, -20f })
            {
                Assert.True(marina.TryProjectToScreen(new Vector3(x, marina.Designer.LandHeight, 0f), out var at));
                marina.Input.PointerMove(at.X, at.Y);
                marina.Input.PointerDown(at.X, at.Y, VirtualMarina.Core.Input.PointerButton.Left);
                marina.Input.PointerUp(at.X, at.Y, VirtualMarina.Core.Input.PointerButton.Left);
            }
        });
        Assert.Equal(2, marina.Designer.DraftPoints.Count);
        Assert.Contains(panel.Elements(), e => e.Text == Strings.UndoLastPoint);

        await panel.Renderer.ClickAsync(Assert.Single(panel.Elements(), e => e.Name == "button" && e.Text == Strings.Undo));

        Assert.Single(marina.Designer.DraftPoints);
        Assert.Single(marina.GetPiers());
    }

    [Fact]
    public async Task AToolChangedElsewhere_IsShownInThePanel()
    {
        var marina = CreateMarina();
        using var module = new FakeJsModule();
        using var panel = await RenderAsync(marina, module);

        await panel.Renderer.InvokeAsync(() =>
        {
            marina.Designer.IsActive = true;
            marina.Designer.Tool = DesignTool.DrawPier;
        });

        Assert.True(ToolButton(panel, DesignTool.DrawPier).HasClass("vm-designer__tool--active"));
    }

    [Fact]
    public async Task ANumber_IsReadInvariantly_WhateverTheBrowserLocale()
    {
        var marina = CreateMarina();
        using var module = new FakeJsModule();
        using var panel = await RenderAsync(marina, module);

        await panel.Renderer.ChangeAsync(NumberField(panel, min: "1", max: "50"), "7.5"); // berth width

        Assert.Equal(7.5f, marina.Designer.BerthWidth);
        Assert.Equal("7.5", NumberField(panel, min: "1", max: "50").Attribute("value"));
    }

    [Fact]
    public async Task EveryBoundedField_OffersTheDesignersOwnRange()
    {
        var marina = CreateMarina();
        using var module = new FakeJsModule();
        using var panel = await RenderAsync(marina, module);
        static string Text(float value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var expected = new[]
            {
                DesignerLimits.LandHeight, DesignerLimits.TreeDensity, DesignerLimits.PierWidth, DesignerLimits.BerthWidth,
                DesignerLimits.BerthLength, DesignerLimits.BerthDepth, DesignerLimits.BerthGap, DesignerLimits.LandBerthHeading,
                DesignerLimits.DividerInterval, DesignerLimits.ReferenceImageMetersPerPixel,
                DesignerLimits.ReferenceImageOpacity with
                {
                    Minimum = DesignerLimits.ReferenceImageOpacity.Minimum * 100f,
                    Maximum = DesignerLimits.ReferenceImageOpacity.Maximum * 100f,
                }, // a slider in percent
            }
            .Select(range => (Text(range.Minimum), Text(range.Maximum)))
            .Order();

        // Every field with both ends set is a designer setting; the scale length, which is not one, has no upper end.
        var offered = panel.Elements()
            .Where(e => e.Name == "input" && e.Attribute("min") is not null && e.Attribute("max") is not null)
            .Select(e => (e.Attribute("min")!, e.Attribute("max")!))
            .Order();

        Assert.Equal(expected, offered);
        Assert.Equal("0.0001", Text(DesignerLimits.ReferenceImageMetersPerPixel.Minimum)); // no exponent in an attribute
    }

    [Theory]
    [InlineData("500")] // outside 1-50: the designer refuses it
    [InlineData("seven")] // not a number at all
    [InlineData("")]
    public async Task AValueTheDesignerRefuses_LeavesTheSettingAsItWas(string input)
    {
        var marina = CreateMarina();
        var before = marina.Designer.BerthWidth;
        using var module = new FakeJsModule();
        using var panel = await RenderAsync(marina, module);

        await panel.Renderer.ChangeAsync(NumberField(panel, min: "1", max: "50"), input);

        Assert.Equal(before, marina.Designer.BerthWidth);
    }

    [Fact]
    public async Task TheSelectLists_SetTheDesignerEnums()
    {
        var marina = CreateMarina();
        using var module = new FakeJsModule();
        using var panel = await RenderAsync(marina, module);
        var pierType = Assert.Single(panel.Elements(), e => e.Name == "select" && e.Attribute("value") == marina.Designer.PierType.ToString());

        await panel.Renderer.ChangeAsync(pierType, nameof(PierType.Concrete));

        Assert.Equal(PierType.Concrete, marina.Designer.PierType);
    }

    [Fact]
    public async Task WithoutAnImage_TheImageControlsAreDisabled()
    {
        var marina = CreateMarina();
        using var module = new FakeJsModule();
        using var panel = await RenderAsync(marina, module);

        Assert.True(ToolButton(panel, DesignTool.MoveReferenceImage).Attributes["disabled"] is true);
        Assert.True(ToolButton(panel, DesignTool.MeasureScale).Attributes["disabled"] is true);

        await panel.Renderer.InvokeAsync(() => marina.Designer.SetReferenceImage(ReferenceImage.FromEncoded([1, 2, 3], 100, 50), 0.5f));

        Assert.False(ToolButton(panel, DesignTool.MoveReferenceImage).Attributes.ContainsKey("disabled"));
    }

    [Fact]
    public async Task Disposing_StopsFollowingTheDesigner()
    {
        var marina = CreateMarina();
        using var module = new FakeJsModule();
        using var panel = await RenderAsync(marina, module);

        panel.Component.Dispose();
        var renders = panel.Renderer.Renders;
        await panel.Renderer.InvokeAsync(() => marina.Designer.IsActive = true);

        Assert.Equal(renders, panel.Renderer.Renders);
    }

    [Fact]
    public async Task WithoutAMarina_ThePanelRefusesToRender()
    {
        using var module = new FakeJsModule();

        await Assert.ThrowsAsync<InvalidOperationException>(() => TestRenderer.RenderAsync<MarinaDesignerPanel>(new FakeJsRuntime(module), new Dictionary<string, object?>()));
    }
}
