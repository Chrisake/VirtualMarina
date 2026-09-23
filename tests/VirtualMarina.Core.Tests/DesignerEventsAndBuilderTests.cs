using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Serialization;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// What the designer tells a host while someone is drawing, and the builder a host uses to describe a marina in
/// code. Both are surfaces an integrator writes against directly, so what they report matters as much as what
/// they do.
/// </summary>
public class DesignerEventsAndBuilderTests
{
    private static MarinaVisualizer WithAPier()
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(1000, 800);
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        marina.Designer.IsActive = true;
        return marina;
    }

    // ---- Designer events ---------------------------------------------------------------------

    [Fact]
    public void ChangingTheTool_ReportsBothTheOldAndTheNewOne()
    {
        var marina = WithAPier();
        DesignToolChangedEventArgs? change = null;
        marina.Designer.ToolChanged += (_, e) => change = e;

        marina.Designer.Tool = DesignTool.AddBerths;

        Assert.NotNull(change);
        Assert.Equal(DesignTool.AddBerths, change.Current);
        Assert.NotEqual(DesignTool.AddBerths, change.Previous);
    }

    [Fact]
    public void SettingTheSameToolAgain_RaisesNothing()
    {
        var marina = WithAPier();
        marina.Designer.Tool = DesignTool.AddBerths;
        var changes = 0;
        marina.Designer.ToolChanged += (_, _) => changes++;

        marina.Designer.Tool = DesignTool.AddBerths;

        Assert.Equal(0, changes);
    }

    [Fact]
    public void TurningTheDesignerOnAndOff_RaisesActiveChanged()
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(1000, 800);
        var changes = 0;
        marina.Designer.ActiveChanged += (_, _) => changes++;

        marina.Designer.IsActive = true;
        marina.Designer.IsActive = true;    // already on: nothing to report
        marina.Designer.IsActive = false;

        Assert.Equal(2, changes);
    }

    [Fact]
    public void DrawingBerths_ReportsEachOneAsItIsCreated()
    {
        var marina = WithAPier();
        DesignElementCreatedEventArgs? created = null;
        marina.Designer.ElementCreated += (_, e) => created = e;

        var berths = marina.Designer.CreateBerths("A", PierSide.Left, 0f, 30f);

        Assert.NotEmpty(berths);
        Assert.NotNull(created);
        Assert.Equal(berths.Count, created.Berths.Count);
        Assert.Equal(berths.Select(b => b.Id), created.Berths.Select(b => b.Id));
    }

    [Fact]
    public void ErasingABerth_ReportsWhatWentWithIt()
    {
        var marina = WithAPier();
        var berths = marina.Designer.CreateBerths("A", PierSide.Left, 0f, 30f);
        DesignElementErasedEventArgs? erased = null;
        marina.Designer.ElementErased += (_, e) => erased = e;

        Assert.True(marina.Designer.Erase(berths[0]));

        Assert.NotNull(erased);
        Assert.Contains(erased.RemovedBerths, b => string.Equals(b.Id, berths[0].Id, StringComparison.OrdinalIgnoreCase));
        Assert.Null(marina.GetBerth(berths[0].Id));
    }

    [Fact]
    public void Undo_PutsTheBerthsBack_AndSaysWhatItUndid()
    {
        var marina = WithAPier();
        var berths = marina.Designer.CreateBerths("A", PierSide.Left, 0f, 30f);
        var countAfterDraw = marina.GetBerths().Count;
        DesignActionUndoneEventArgs? undone = null;
        marina.Designer.ActionUndone += (_, e) => undone = e;

        Assert.True(marina.Designer.Undo());

        Assert.NotNull(undone);
        Assert.False(string.IsNullOrWhiteSpace(undone.Description));
        Assert.True(marina.GetBerths().Count < countAfterDraw);
        Assert.NotEmpty(berths);
    }

    [Fact]
    public void UndoWithNothingToUndo_ReturnsFalseAndRaisesNothing()
    {
        var marina = WithAPier();
        var undos = 0;
        marina.Designer.ActionUndone += (_, _) => undos++;

        while (marina.Designer.Undo())
        {
            // Drain whatever the fixture did.
        }

        var before = undos;
        Assert.False(marina.Designer.Undo());
        Assert.Equal(before, undos);
    }

    [Fact]
    public void ChangingTheReferenceImageScale_ReportsTheChangeAndTheNewScale()
    {
        var marina = WithAPier();
        ReferenceImageChangedEventArgs? change = null;
        marina.Designer.ReferenceImageChanged += (_, e) => change = e;

        marina.Designer.ReferenceImageMetersPerPixel = 0.5f;   // the default is 0.25

        Assert.NotNull(change);
        Assert.Equal(0.5f, change.MetersPerPixel, 4);
        Assert.Equal(ReferenceImageChange.Scaled, change.Change);
    }

    [Fact]
    public void ChangingTheReferenceImageOpacity_ReportsItAsAnAppearanceChange()
    {
        var marina = WithAPier();
        ReferenceImageChangedEventArgs? change = null;
        marina.Designer.ReferenceImageChanged += (_, e) => change = e;

        marina.Designer.ReferenceImageOpacity = 0.3f;

        Assert.NotNull(change);
        Assert.Equal(ReferenceImageChange.AppearanceChanged, change.Change);
    }

    [Fact]
    public void SettingTheSameImageScaleTwice_ReportsItOnce()
    {
        var marina = WithAPier();
        marina.Designer.ReferenceImageMetersPerPixel = 0.25f;
        var changes = 0;
        marina.Designer.ReferenceImageChanged += (_, _) => changes++;

        marina.Designer.ReferenceImageMetersPerPixel = 0.25f;

        Assert.Equal(0, changes);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(1e9f)]
    public void AnImpossibleImageScale_IsRefusedOrClamped_NotStored(float scale)
    {
        var marina = WithAPier();

        var exception = Record.Exception(() => marina.Designer.ReferenceImageMetersPerPixel = scale);

        if (exception is null)
        {
            var stored = marina.Designer.ReferenceImageMetersPerPixel;
            Assert.True(float.IsFinite(stored) && stored > 0f, $"stored scale {stored} should be positive and finite");
        }
    }

    // ---- The layout builder ------------------------------------------------------------------

    [Fact]
    public void TheBuilderProducesTheMarinaItWasDescribed()
    {
        var layout = new MarinaLayoutBuilder("Test Harbour")
            .AddLandArea("yard", [new Vector2(0, 0), new Vector2(20, 0), new Vector2(20, 20), new Vector2(0, 20)], 1f)
            .AddPier(new Pier("A", "Pier A", new Vector2(40, 0), 0f, 40f), pier =>
            {
                pier.AddBerth(BerthGenerator.AtPier(pier.Pier, "A-L01", PierSide.Left, 0f, 5f, 12f));
                pier.AddDivider(new Divider("D1", new Vector2(40, 3f), 0f, 10f));
            })
            .Build();

        Assert.Equal("Test Harbour", layout.Name);
        Assert.Single(layout.Piers);
        Assert.Single(layout.LandAreas);
        Assert.Single(layout.Berths);
        Assert.Single(layout.Dividers);
        Assert.Empty(layout.Validate());
    }

    [Fact]
    public void TheBuilderStampsThePierIdOntoWhatIsAddedUnderIt()
    {
        var layout = new MarinaLayoutBuilder()
            .AddPier(new Pier("A", "Pier A", Vector2.Zero, 0f, 40f), pier =>
                // Deliberately built against a different pier, to prove the builder re-homes it.
                pier.AddBerth(new Berth("X-01", "SOMEWHERE-ELSE", new Vector2(2, 2), 0f, 12f, 5f)))
            .Build();

        Assert.Equal("A", layout.Berths.Single().PierId);
    }

    [Fact]
    public void AnEmptyBuilderProducesAValidEmptyMarina()
    {
        var layout = new MarinaLayoutBuilder().Build();

        Assert.Empty(layout.Piers);
        Assert.Empty(layout.Berths);
        Assert.Empty(layout.Validate());
    }

    [Fact]
    public void ABuiltMarinaSurvivesASaveAndLoad()
    {
        var layout = new MarinaLayoutBuilder("Round Trip")
            .AddPier(new Pier("A", "Pier A", new Vector2(5, 5), 30f, 40f), pier =>
                pier.AddBerth(BerthGenerator.AtPier(pier.Pier, "A-L01", PierSide.Left, 0f, 5f, 12f)))
            .Build();
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(layout);

        var reloaded = MarinaDocument.Parse(MarinaDocument.FromVisualizer(marina).ToJson());

        // The marina's name lives on the document, not on the layout inside it: InitializeLayout copies
        // MarinaLayout.Name into MarinaName, and it is MarinaName that is written to the file.
        Assert.Equal("Round Trip", reloaded.Name);
        Assert.Equal("A-L01", reloaded.Layout.Berths.Single().Id);
    }

    // ---- The boat catalogue ------------------------------------------------------------------

    [Fact]
    public void EveryBoatTypeHasPlausibleNominalDimensions()
    {
        Assert.NotEmpty(BoatTypeCatalog.All);

        foreach (var type in BoatTypeCatalog.All)
        {
            var size = BoatTypeCatalog.GetNominalDimensions(type);

            Assert.True(size.Length > 0f && float.IsFinite(size.Length), $"{type} length {size.Length}");
            Assert.True(size.Beam > 0f && float.IsFinite(size.Beam), $"{type} beam {size.Beam}");
            Assert.True(size.Beam < size.Length, $"{type} should be longer than it is wide");
            Assert.False(string.IsNullOrWhiteSpace(BoatTypeCatalog.GetDisplayName(type)), $"{type} has no display name");
        }
    }

    /// <summary>
    /// The two lookups treat an unknown type differently on purpose: a name can fall back to the enum's own
    /// spelling and still read sensibly, whereas inventing a size would put a boat of the wrong length on the
    /// water. Worth pinning down, because the asymmetry looks like an oversight until you think about it.
    /// </summary>
    [Fact]
    public void AnUnknownBoatType_NamesItselfButRefusesToInventASize()
    {
        Assert.Equal("999", BoatTypeCatalog.GetDisplayName((BoatType)999));

        Assert.Throws<ArgumentOutOfRangeException>(() => BoatTypeCatalog.GetNominalDimensions((BoatType)999));
    }
}
