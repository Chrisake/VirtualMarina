using System.Reflection;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.WinForms;

namespace VirtualMarina.WinForms.Tests;

/// <summary>
/// The panel's combo boxes hold captioned choices rather than bare enum values; picking one must reach the designer.
/// </summary>
public class MarinaDesignerPanelTests
{
    [Fact]
    public void ChoosingALandType_SetsTheDesignersLandKind()
    {
        var marina = new MarinaVisualizer();
        using var panel = new MarinaDesignerPanel { Marina = marina };   // no handle is created
        var combo = Combo(panel, "_cmbLandKind");
        var kinds = Enum.GetValues<LandKind>();
        var target = kinds.First(k => k != marina.Designer.LandKind);

        combo.SelectedIndex = Array.IndexOf(kinds, target);

        Assert.Equal(target, marina.Designer.LandKind);
    }

    [Fact]
    public void EveryChoiceInEveryCombo_CanBePicked()
    {
        using var panel = new MarinaDesignerPanel { Marina = new MarinaVisualizer() };
        var combos = typeof(MarinaDesignerPanel)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(f => f.FieldType == typeof(ComboBox))
            .Select(f => (ComboBox)f.GetValue(panel)!)
            .ToList();
        Assert.NotEmpty(combos);

        foreach (var combo in combos)
        {
            for (var i = 0; i < combo.Items.Count; i++) combo.SelectedIndex = i;
        }
    }

    [Fact]
    public void TheNumberFieldsAndSliders_OfferTheDesignersOwnRanges()
    {
        using var panel = new MarinaDesignerPanel { Marina = new MarinaVisualizer() };

        AssertRange(DesignerLimits.LandHeight, Field<NumericUpDown>(panel, "_nudLandHeight"));
        AssertRange(DesignerLimits.PierWidth, Field<NumericUpDown>(panel, "_nudPierWidth"));
        AssertRange(DesignerLimits.BerthWidth, Field<NumericUpDown>(panel, "_nudBerthWidth"));
        AssertRange(DesignerLimits.BerthLength, Field<NumericUpDown>(panel, "_nudBerthLength"));
        AssertRange(DesignerLimits.BerthDepth, Field<NumericUpDown>(panel, "_nudBerthDepth"));
        AssertRange(DesignerLimits.BerthGap, Field<NumericUpDown>(panel, "_nudBerthGap"));
        AssertRange(DesignerLimits.LandBerthHeading, Field<NumericUpDown>(panel, "_nudLandBerthHeading"));
        AssertRange(DesignerLimits.ReferenceImageMetersPerPixel, Field<NumericUpDown>(panel, "_nudMetersPerPixel"));

        var trees = Field<TrackBar>(panel, "_trkTreeDensity");
        Assert.Equal(((int)DesignerLimits.TreeDensity.Minimum, (int)DesignerLimits.TreeDensity.Maximum), (trees.Minimum, trees.Maximum));
        var opacity = Field<TrackBar>(panel, "_trkOpacity"); // in percent
        Assert.Equal(((int)(DesignerLimits.ReferenceImageOpacity.Minimum * 100f), (int)(DesignerLimits.ReferenceImageOpacity.Maximum * 100f)), (opacity.Minimum, opacity.Maximum));
    }

    [Fact]
    public void EveryToolTheDesignerHas_HasAButton()
    {
        using var panel = new MarinaDesignerPanel { Marina = new MarinaVisualizer() };
        var buttons = Field<Dictionary<DesignTool, CheckBox>>(panel, "_toolButtons");

        Assert.Equal(Enum.GetValues<DesignTool>().Order(), buttons.Keys.Order());
        Assert.All(buttons, pair => Assert.Equal(pair.Key.GetDisplayName(), pair.Value.Text));
    }

    [Fact]
    public void Redo_IsOfferedWhenTheDesignerCanRedo_AndMakesTheChangeAgain()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new System.Numerics.Vector2(0, -30), 0f, 60f));
        using var panel = new MarinaDesignerPanel { Marina = marina };
        var undo = Field<Button>(panel, "_btnUndo");
        var redo = Field<Button>(panel, "_btnRedo");
        Assert.False(redo.Enabled);

        marina.Designer.CreateBerths("A", PierSide.Left, 0f, 10f);
        Assert.True(undo.Enabled);
        Click(undo);
        Assert.Empty(marina.GetBerths());
        Assert.True(redo.Enabled);
        Assert.Equal(marina.Designer.RedoDescription, Field<Label>(panel, "_lblRedo").Text);

        Click(redo);

        Assert.NotEmpty(marina.GetBerths());
        Assert.False(redo.Enabled);
    }

    private static void Click(Button button) =>
        typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(button, [EventArgs.Empty]);

    private static void AssertRange(DesignerSettingRange range, NumericUpDown field)
    {
        Assert.Equal((decimal)range.Minimum, field.Minimum);
        Assert.Equal((decimal)range.Maximum, field.Maximum);
    }

    private static T Field<T>(MarinaDesignerPanel panel, string field) =>
        (T)typeof(MarinaDesignerPanel).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(panel)!;

    private static ComboBox Combo(MarinaDesignerPanel panel, string field) =>
        (ComboBox)typeof(MarinaDesignerPanel).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(panel)!;
}
