using System.Globalization;
using System.Reflection;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Tests;

/// <summary>Displayed text comes from Strings.resx, and an untranslated culture falls back to it instead of breaking.</summary>
/// <remarks>
/// Switching <see cref="MarinaLocalization.Culture"/> changes the UI culture of the whole process, so the class runs
/// in <see cref="ProcessWideState"/>, alone, instead of beside tests that expect English.
/// </remarks>
[Collection(ProcessWideState.Name)]
public class LocalizationTests
{
    [Fact]
    public void DisplayedText_ComesFromTheResourceFile()
    {
        Assert.Equal("Temporarily Free", BerthStatus.TemporarilyFree.GetDisplayName());
        Assert.Equal("Motor Yacht", BoatTypeCatalog.GetDisplayName(BoatType.MotorYacht));
        Assert.Equal("Floating (wooden)", Pier.GetDisplayName(PierType.FloatingWooden));
        Assert.Equal("Non-occupied", BerthLabelMode.NonOccupied.GetDisplayName());
        Assert.Equal("Land berths", DesignTool.AddLandBerths.GetDisplayName());
        Assert.Equal("Dividers", DesignTool.PlaceDividers.GetDisplayName());
        Assert.Equal("Power and water", PierServices.PowerAndWater.GetDisplayName());
        Assert.Equal("Boats on the left only", PierSides.Left.GetDisplayName());
        Assert.Equal("Lawn or park", LandKind.Grass.GetDisplayName());
    }

    [Fact]
    public void EveryKeyTheCodeAsksFor_IsInTheResourceFile()
    {
        // Strings.Get returns the name itself when a resource is missing, so a property equal to its own name is a hole.
        var properties = typeof(Strings)
            .GetProperties(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(p => p.PropertyType == typeof(string))
            .ToArray();

        Assert.True(properties.Length > 50, $"expected the whole string table, found {properties.Length} entries");
        Assert.All(properties, property =>
        {
            var value = (string)property.GetValue(null)!;
            Assert.False(string.IsNullOrWhiteSpace(value), $"{property.Name} is empty");
            Assert.True(value != property.Name, $"{property.Name} has no entry in Strings.resx");
        });
    }

    [Fact]
    public void AnUntranslatedCulture_FallsBackToTheNeutralText()
    {
        var previous = MarinaLocalization.Culture;
        var previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        var previousCurrent = CultureInfo.CurrentUICulture;
        try
        {
            MarinaLocalization.Culture = new CultureInfo("fr-FR");
            Assert.Equal("Occupied", BerthStatus.Occupied.GetDisplayName());
            Assert.Equal(new CultureInfo("fr-FR"), MarinaLocalization.Culture);
            Assert.Equal(new CultureInfo("fr-FR"), CultureInfo.DefaultThreadCurrentUICulture);
        }
        finally
        {
            // The setter changes three things; put all three back, not only the one it was handed.
            MarinaLocalization.Culture = previous;
            CultureInfo.DefaultThreadCurrentUICulture = previousDefault;
            CultureInfo.CurrentUICulture = previousCurrent;
        }
    }

    [Fact]
    public void AvailableCultures_StartsWithTheNeutralLanguage()
    {
        var cultures = MarinaLocalization.AvailableCultures();
        Assert.Equal(CultureInfo.InvariantCulture, cultures[0]);
    }

    [Fact]
    public void ToolHintsAndUndoSteps_AreRealSentences()
    {
        var marina = new MarinaVisualizer();
        var designer = marina.Designer;
        designer.IsActive = true;

        designer.Tool = DesignTool.AddBerths;
        Assert.Equal("Click beside a pier where the first berth goes.", designer.ToolHint);

        marina.AddPier(new Pier("A", "Pier A", System.Numerics.Vector2.Zero, 0f, 40f));
        designer.CreateBerths("A", PierSide.Left, 2f, 2f);
        Assert.Equal("Add berth A-L01", designer.UndoDescription);
    }
}
