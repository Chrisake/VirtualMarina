using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Serialization;

namespace VirtualMarina.Core.Tests;

/// <summary>Naming piers, the scheme new berths are named by, and renaming one berth on its own.</summary>
public class NamingTests
{
    private static MarinaVisualizer WithPier(out MarinaDesigner designer)
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", Vector2.Zero, 0f, 60f));
        designer = marina.Designer;
        designer.IsActive = true;
        designer.BerthWidth = 5f;
        designer.BerthLength = 12f;
        return marina;
    }

    [Fact]
    public void BerthNamingScheme_FillsItsTokens()
    {
        var pier = new Pier("A", "West pontoon", Vector2.Zero, 0f, 40f);
        var scheme = BerthNamingScheme.Default;

        Assert.Equal("A-L01", scheme.Format(pier, PierSide.Left, 1));
        Assert.Equal("A-R12", scheme.Format(pier, PierSide.Right, 12));

        var custom = new BerthNamingScheme { Pattern = "{pierName} {side}-{number}", LeftSide = "port", NumberDigits = 3 };
        Assert.Equal("West pontoon port-007", custom.Format(pier, PierSide.Left, 7));

        // Unknown tokens are left alone rather than eating part of the name.
        Assert.Equal("{quay}-9", new BerthNamingScheme { Pattern = "{quay}-{number}", NumberDigits = 1 }.Format(pier, PierSide.Left, 9));
    }

    [Fact]
    public void BerthNamingScheme_Validate_CatchesWhatCannotName()
    {
        Assert.Empty(BerthNamingScheme.Default.Validate());
        Assert.NotEmpty(new BerthNamingScheme { Pattern = "  " }.Validate());
        Assert.NotEmpty(new BerthNamingScheme { Increment = 0 }.Validate());
        Assert.NotEmpty(new BerthNamingScheme { NumberDigits = 0 }.Validate());
    }

    [Fact]
    public void Designer_NamesDrawnBerths_ByTheScheme()
    {
        var marina = WithPier(out var designer);
        designer.BerthNaming = new BerthNamingScheme { Pattern = "{number}", StartNumber = 101, Increment = 2, NumberDigits = 3 };

        var berths = designer.CreateBerths("A", PierSide.Left, 0f, 15f);

        Assert.Equal(new[] { "101", "103", "105" }, berths.Select(b => b.Id));

        // A second row carries on past the names already taken instead of clashing with them.
        var more = designer.CreateBerths("A", PierSide.Right, 0f, 10f);
        Assert.Equal(new[] { "107", "109" }, more.Select(b => b.Id));
    }

    [Fact]
    public void Designer_NamesLandBerths_ByTheLandPattern()
    {
        var marina = new MarinaVisualizer();
        marina.AddLandArea(new LandArea("yard", new[] { new Vector2(0, 0), new Vector2(40, 0), new Vector2(40, 30), new Vector2(0, 30) }, 1.5f, LandKind.Quay));
        var designer = marina.Designer;
        designer.IsActive = true;
        designer.BerthNaming = new BerthNamingScheme { LandPattern = "YARD-{number}", NumberDigits = 2 };

        Assert.Equal("YARD-01", designer.CreateLandBerth("yard", new Vector2(10, 10))!.Id);
        Assert.Equal("YARD-02", designer.CreateLandBerth("yard", new Vector2(20, 10))!.Id);
    }

    [Fact]
    public void Designer_NumbersSlotsAshore_SeparatelyFromTheBerths()
    {
        var marina = new MarinaVisualizer();
        marina.AddLandArea(new LandArea("yard", new[] { new Vector2(0, 0), new Vector2(60, 0), new Vector2(60, 40), new Vector2(0, 40) }, 1.5f, LandKind.Quay));
        marina.AddPier(new Pier("A", "Pier A", new Vector2(-20, 0), 0f, 40f));
        var designer = marina.Designer;
        designer.IsActive = true;
        designer.BerthWidth = 5f;
        designer.BerthLength = 12f;

        // The berths on the water count one way, the slots ashore another.
        designer.BerthNaming = new BerthNamingScheme
        {
            Pattern = "{number}",
            StartNumber = 101,
            Increment = 2,
            NumberDigits = 3,
            LandPattern = "YARD-{number}",
            LandStartNumber = 1,
            LandIncrement = 10,
            LandNumberDigits = 2,
        };

        Assert.Equal(new[] { "101", "103" }, designer.CreateBerths("A", PierSide.Left, 0f, 10f).Select(b => b.Id));
        Assert.Equal("YARD-01", designer.CreateLandBerth("yard", new Vector2(10, 10))!.Id);
        Assert.Equal("YARD-11", designer.CreateLandBerth("yard", new Vector2(25, 10))!.Id);
        Assert.Equal("YARD-21", designer.CreateLandBerth("yard", new Vector2(40, 10))!.Id);
    }

    [Fact]
    public void AshoreNaming_FallsBackToTheBerthNumbering_WhenItIsNotSetSeparately()
    {
        var scheme = new BerthNamingScheme { StartNumber = 7, Increment = 3, NumberDigits = 1, LandPattern = "Y{number}" };
        var land = new LandArea("yard", new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10) }, 1f);

        Assert.Equal("Y7", scheme.Format(land, 7));

        // Padding is the berths' until the slots ashore ask for their own.
        Assert.Equal("Y007", (scheme with { LandNumberDigits = 3 }).Format(land, 7));
    }

    [Fact]
    public void Designer_NamesDrawnPiers_ByThePattern()
    {
        var marina = new MarinaVisualizer();
        var designer = marina.Designer;
        designer.IsActive = true;
        designer.PierNamePattern = "Pontoon {pier}";

        var pier = designer.CreatePier(new Vector2(0, 0), new Vector2(0, 50))!;
        Assert.Equal("A", pier.Id);
        Assert.Equal("Pontoon A", pier.Name);
    }

    [Fact]
    public void RenameBerth_KeepsEverythingElse_AndCanBeUndone()
    {
        var marina = WithPier(out var designer);
        var berths = designer.CreateBerths("A", PierSide.Left, 0f, 15f);
        var first = berths[0];
        marina.UpdateBerth(new BerthUpdate(first.Id) { Status = BerthStatus.Reserved, Boat = new Boat("B-1", "Meltemi", BoatType.MotorYacht) });
        marina.SelectBerth(first.Id);

        var changes = new List<LayoutChangedEventArgs>();
        marina.LayoutChanged += (_, e) => changes.Add(e);

        var renamed = designer.RenameBerth(first.Id, "A-1");

        var change = Assert.Single(changes);
        Assert.Equal(LayoutChangeKind.BerthRenamed, change.Kind);
        Assert.Equal("A-1", change.BerthId);
        Assert.Equal("A", change.PierId);

        Assert.Equal("A-1", renamed.Id);
        Assert.Null(marina.GetBerth(first.Id));
        Assert.Equal(BerthStatus.Reserved, marina.GetBerth("A-1")!.Status);
        Assert.Equal("Meltemi", marina.GetBerth("A-1")!.Boat?.Name);
        Assert.Equal("A", marina.GetBerth("A-1")!.PierId);
        Assert.True(marina.IsBerthSelected("A-1"));
        Assert.Equal(renamed.Center, first.Center);

        // The berth keeps its place in the row.
        Assert.Equal(new[] { "A-1", berths[1].Id, berths[2].Id }, marina.GetBerths().Select(b => b.Id));

        Assert.Equal("Rename A-L01 to A-1", designer.UndoDescription);
        Assert.True(designer.Undo());
        Assert.NotNull(marina.GetBerth("A-L01"));
        Assert.Null(marina.GetBerth("A-1"));
    }

    [Fact]
    public void RenameBerth_RefusesANameAlreadyTaken()
    {
        var marina = WithPier(out var designer);
        var berths = designer.CreateBerths("A", PierSide.Left, 0f, 15f);

        Assert.Throws<InvalidOperationException>(() => marina.RenameBerth(berths[0].Id, berths[1].Id));
        Assert.Throws<KeyNotFoundException>(() => marina.RenameBerth("nope", "A-9"));

        // A different spelling of the same name is not a clash.
        Assert.Equal("a-l01", marina.RenameBerth("A-L01", "a-l01").Id);
    }

    [Fact]
    public void RenameBerth_TakesItsMultiBerthWithIt()
    {
        var marina = WithPier(out var designer);
        var berths = designer.CreateBerths("A", PierSide.Left, 0f, 10f);
        var group = marina.MoorAlongside(berths.Take(2).Select(b => b.Id), new Boat("B-1", "Meltemi", BoatType.MotorYacht));

        marina.RenameBerth(berths[0].Id, "VIP");

        Assert.Equal(new[] { "VIP", berths[1].Id }, marina.GetMultiBerth(group.Id)!.BerthIds);
        Assert.Same(marina.GetMultiBerth(group.Id), marina.GetMultiBerthFor("VIP"));
    }

    [Fact]
    public void RenamePier_ChangesTheNameOnly_AndCanBeUndone()
    {
        var marina = WithPier(out var designer);
        var berths = designer.CreateBerths("A", PierSide.Left, 0f, 5f);

        var renamed = designer.RenamePier("A", "  West pontoon  ");

        Assert.Equal("West pontoon", renamed.Name);
        Assert.Equal("A", renamed.Id);
        Assert.Equal("A", marina.GetBerth(berths[0].Id)!.PierId); // the berths are untouched

        Assert.True(designer.Undo());
        Assert.Equal("Pier A", marina.GetPier("A")!.Name);
    }

    [Fact]
    public void RenameTool_AsksTheHostForTheName_AndAppliesIt()
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(1000, 800);
        marina.Camera.SetPose(new CameraPose(Vector3.Zero, 0f, 89f, 120f), immediate: true);
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -20), 0f, 40f));
        var designer = marina.Designer;
        designer.IsActive = true;
        var berth = designer.CreateBerths("A", PierSide.Left, 10f, 10f)[0];

        var asked = new List<string>();
        designer.ElementRenaming += (_, e) =>
        {
            asked.Add(e.CurrentName);
            e.NewName = "VIP";
        };

        designer.Tool = DesignTool.Rename;
        Click(marina, berth.Center);

        Assert.Equal(new[] { berth.Id }, asked);
        Assert.NotNull(marina.GetBerth("VIP"));
        Assert.Null(marina.GetBerth(berth.Id));
    }

    [Fact]
    public void RenameTool_LeavesTheElementAlone_WhenTheHostCancels()
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(1000, 800);
        marina.Camera.SetPose(new CameraPose(Vector3.Zero, 0f, 89f, 120f), immediate: true);
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -20), 0f, 40f));
        var designer = marina.Designer;
        designer.IsActive = true;
        var berth = designer.CreateBerths("A", PierSide.Left, 10f, 10f)[0];
        var steps = designer.UndoCount;

        designer.ElementRenaming += (_, e) => e.Cancel = true;
        designer.Tool = DesignTool.Rename;
        Click(marina, berth.Center);

        Assert.NotNull(marina.GetBerth(berth.Id));
        Assert.Equal(steps, designer.UndoCount);
    }

    private static void Click(MarinaVisualizer marina, Vector2 plan)
    {
        Assert.True(marina.TryProjectToScreen(MarinaMath.ToWorld(plan), out var screen));
        marina.Input.PointerMove(screen.X, screen.Y);
        marina.Input.PointerDown(screen.X, screen.Y, PointerButton.Left);
        marina.Input.PointerUp(screen.X, screen.Y, PointerButton.Left);
    }

    [Fact]
    public void NamingSettings_SurviveASaveAndLoad()
    {
        var marina = WithPier(out var designer);
        designer.PierNamePattern = "Pontoon {pier}";
        designer.BerthNaming = new BerthNamingScheme
        {
            Pattern = "{pierName}/{side}{number}",
            LandPattern = "YARD-{number}",
            StartNumber = 5,
            Increment = 5,
            NumberDigits = 3,
            LeftSide = "W",
            RightSide = "E",
        };

        var document = MarinaDocument.Parse(MarinaDocument.FromVisualizer(marina).ToJson());
        var reloaded = new MarinaVisualizer();
        document.ApplyTo(reloaded);

        Assert.Equal("Pontoon {pier}", reloaded.Designer.PierNamePattern);
        var scheme = reloaded.Designer.BerthNaming;
        Assert.Equal("{pierName}/{side}{number}", scheme.Pattern);
        Assert.Equal("YARD-{number}", scheme.LandPattern);
        Assert.Equal(5, scheme.StartNumber);
        Assert.Equal(5, scheme.Increment);
        Assert.Equal(3, scheme.NumberDigits);
        Assert.Equal("W", scheme.LeftSide);
        Assert.Equal("E", scheme.RightSide);
    }

    [Fact]
    public void ADesignFromBeforeNaming_GetsTheDefaultScheme()
    {
        const string json = """
            {
              "format": "virtualmarina.marina",
              "formatVersion": "2.0",
              "marina": { "name": "Harbor" },
              "designer": { "berthWidth": 6 }
            }
            """;

        var designer = new MarinaVisualizer().Designer;
        MarinaDocument.Parse(json).Designer!.ApplyTo(designer);

        Assert.Equal(BerthNamingScheme.Default, designer.BerthNaming);
        Assert.Equal("Pier {pier}", designer.PierNamePattern);
    }
}
