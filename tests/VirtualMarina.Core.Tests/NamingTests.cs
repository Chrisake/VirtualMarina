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
        WithPier(out var designer);
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
    public void ASingleSidedPier_LeavesTheSideLetterOutOfItsBerthNames()
    {
        var both = new Pier("A", "Pier A", Vector2.Zero, 0f, 40f);
        var oneSided = both with { BerthingSides = PierSides.Right };
        var scheme = BerthNamingScheme.Default;

        Assert.Equal("A-R01", scheme.Format(both, PierSide.Right, 1));

        // Nothing to tell apart, so no letter.
        Assert.Equal("A-01", scheme.Format(oneSided, PierSide.Right, 1));
    }

    [Fact]
    public void Designer_NamesBerthsOnASingleSidedPier_WithoutTheSideLetter()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("Q", "Quay pontoon", Vector2.Zero, 0f, 40f) { BerthingSides = PierSides.Left });
        var designer = marina.Designer;
        designer.IsActive = true;
        designer.BerthWidth = 5f;

        Assert.Equal(new[] { "Q-01", "Q-02" }, designer.CreateBerths("Q", PierSide.Left, 0f, 10f).Select(b => b.Id));
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

    [Fact]
    public void ChangePierId_TakesTheBerthsAndDividersWithIt_AndCanBeUndone()
    {
        var marina = WithPier(out var designer);
        marina.AddDivider(new Divider("A-D1", new Vector2(-2, 4), 90f, 9f, DividerType.Piles) { PierId = "A" });
        var berths = designer.CreateBerths("A", PierSide.Left, 0f, 10f);

        var changes = new List<LayoutChangedEventArgs>();
        marina.LayoutChanged += (_, e) => changes.Add(e);

        var moved = designer.ChangePierId("A", "WEST");

        Assert.Equal("WEST", moved.Id);
        Assert.Equal("Pier WEST", moved.Name); // the generated name follows the id
        Assert.Null(marina.GetPier("A"));
        Assert.Equal(berths.Count, marina.GetBerthsByPier("WEST").Count);
        Assert.Equal("WEST", marina.GetDivider("A-D1")!.PierId);

        // The berths are named after the pier, so they come with it: A-L01 becomes WEST-L01.
        Assert.StartsWith("A-", berths[0].Id);
        var moved0 = "WEST" + berths[0].Id["A".Length..];
        Assert.Null(marina.GetBerth(berths[0].Id));
        Assert.Equal("WEST", marina.GetBerth(moved0)!.PierId);
        Assert.All(marina.GetBerthsByPier("WEST"), berth => Assert.StartsWith("WEST-", berth.Id));

        // The pier and every berth that moved are reported, so a host can follow the ids.
        Assert.Contains(changes, change => change.Kind == LayoutChangeKind.PierRenamed && change.PierId == "WEST");
        Assert.Equal(berths.Count, changes.Count(change => change.Kind == LayoutChangeKind.BerthRenamed));

        // One step of undo puts the pier and all its berths back.
        Assert.True(designer.Undo());
        Assert.NotNull(marina.GetPier("A"));
        Assert.Null(marina.GetBerth(moved0));
        Assert.Equal("A", marina.GetBerth(berths[0].Id)!.PierId);
        Assert.All(marina.GetBerthsByPier("A"), berth => Assert.StartsWith("A-", berth.Id));
    }

    [Fact]
    public void ChangePierId_LeavesAloneABerthThatWasNamedByHand()
    {
        var marina = WithPier(out var designer);
        var berths = designer.CreateBerths("A", PierSide.Left, 0f, 10f);
        Assert.True(berths.Count >= 2);

        // One berth is given a name of its own, which has nothing to do with the pier.
        designer.RenameBerth(berths[0].Id, "Harbourmaster");

        designer.ChangePierId("A", "WEST");

        Assert.NotNull(marina.GetBerth("Harbourmaster"));
        Assert.Equal("WEST", marina.GetBerth("Harbourmaster")!.PierId);
        Assert.NotNull(marina.GetBerth("WEST" + berths[1].Id["A".Length..]));
    }

    [Fact]
    public void ChangePierId_RefusesAnIdAlreadyTaken()
    {
        var marina = WithPier(out _);
        marina.AddPier(new Pier("B", "Pier B", new Vector2(50, 0), 0f, 40f));

        Assert.Throws<InvalidOperationException>(() => marina.ChangePierId("A", "B"));
        Assert.Throws<KeyNotFoundException>(() => marina.ChangePierId("nope", "C"));
        Assert.Equal("a", marina.ChangePierId("A", "a").Id); // a different spelling is not a clash
    }

    [Fact]
    public void EraseBerthsOfPier_ClearsTheRowButLeavesThePier_AndCanBeUndone()
    {
        var marina = WithPier(out var designer);
        designer.DividerType = DividerType.Piles;
        var left = designer.CreateBerths("A", PierSide.Left, 0f, 15f);
        designer.PlaceDividers("A", PierSide.Left, 0f, wholeRow: true);
        Assert.NotEmpty(marina.GetDividers());

        Assert.True(designer.EraseBerthsOfPier("A"));

        Assert.NotNull(marina.GetPier("A"));
        Assert.Empty(marina.GetBerthsByPier("A"));
        Assert.Empty(marina.GetDividers()); // the separators only served those berths
        Assert.False(designer.EraseBerthsOfPier("A")); // nothing left to clear
        Assert.False(designer.EraseBerthsOfPier("nope"));

        Assert.True(designer.Undo());
        Assert.Equal(left.Count, marina.GetBerthsByPier("A").Count);
    }

    [Fact]
    public void SetBerthServices_ChangesOneBerth_OrTheWholeSide_AndCanBeUndone()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        var designer = marina.Designer;
        designer.IsActive = true;
        designer.BerthWidth = 5f;
        var left = designer.CreateBerths("A", PierSide.Left, 0f, 20f);
        var right = designer.CreateBerths("A", PierSide.Right, 0f, 20f);
        Assert.True(left.Count > 1 && right.Count > 1);

        // A berth takes its pier's pedestals until it is given its own.
        Assert.Null(marina.GetBerth(left[0].Id)!.Services);

        designer.BerthServices = PierServices.PowerAndWater;
        var one = designer.SetBerthServices(left[0].Id);
        Assert.Single(one);
        Assert.Equal(PierServices.PowerAndWater, marina.GetBerth(left[0].Id)!.Services);
        Assert.Null(marina.GetBerth(left[1].Id)!.Services); // its neighbour is untouched

        // Alt or Ctrl takes the whole side, and leaves the other side alone.
        designer.BerthServices = PierServices.Power;
        var side = designer.SetBerthServices(left[1].Id, wholeSide: true);
        Assert.Equal(left.Count, side.Count);
        Assert.All(left, berth => Assert.Equal(PierServices.Power, marina.GetBerth(berth.Id)!.Services));
        Assert.All(right, berth => Assert.Null(marina.GetBerth(berth.Id)!.Services));

        // Asking for what they already have changes nothing.
        Assert.Empty(designer.SetBerthServices(left[1].Id, wholeSide: true));

        Assert.True(designer.Undo());
        Assert.Equal(PierServices.PowerAndWater, marina.GetBerth(left[0].Id)!.Services);
    }

    [Fact]
    public void BerthServices_SurviveASaveAndLoad()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f) { Services = PierServices.Power });
        var designer = marina.Designer;
        designer.IsActive = true;
        var berths = designer.CreateBerths("A", PierSide.Left, 0f, 15f);
        designer.BerthServices = PierServices.PowerAndWater;
        designer.SetBerthServices(berths[0].Id);

        var copy = new MarinaVisualizer();
        MarinaDocument.Parse(MarinaDocument.FromVisualizer(marina).ToJson()).ApplyTo(copy);

        Assert.Equal(PierServices.PowerAndWater, copy.GetBerth(berths[0].Id)!.Services);
        Assert.Null(copy.GetBerth(berths[1].Id)!.Services);
        Assert.Equal(PierServices.Power, copy.GetPier("A")!.Services);
    }

    [Fact]
    public void ChangePierId_KeepsAPierNameSomeoneChose()
    {
        WithPier(out var designer);
        designer.RenamePier("A", "West pontoon");

        var moved = designer.ChangePierId("A", "W");

        Assert.Equal("West pontoon", moved.Name);
        Assert.Equal("W", moved.Id);
    }

    [Fact]
    public void ChangePierId_OnASingleSidedPier_DropsTheSideLetterFromItsBerths()
    {
        var marina = new MarinaVisualizer();
        var designer = marina.Designer;

        // Built as a two-sided pier, so its berths carry the side letter.
        designer.PierBerthingSides = PierSides.Both;
        var pier = designer.CreatePier(new Vector2(0, 0), new Vector2(0, 60))!;
        var berths = designer.CreateBerths(pier.Id, PierSide.Right, 0f, 40f);
        Assert.True(berths.Count >= 3);
        Assert.All(berths, berth => Assert.Contains("-R", berth.Id, StringComparison.Ordinal));

        // It turns out to run along the quay, so it only berths on one side.
        marina.UpdatePier(marina.GetPier(pier.Id)! with { BerthingSides = PierSides.Right });

        // Renaming it rebuilds the names from the scheme, which no longer has a side to tell apart.
        designer.ChangePierId(pier.Id, "Q");

        var renamed = marina.GetBerthsByPier("Q");
        Assert.Equal(berths.Count, renamed.Count);
        Assert.All(renamed, berth => Assert.DoesNotContain("-R", berth.Id, StringComparison.Ordinal));
        Assert.All(renamed, berth => Assert.StartsWith("Q-", berth.Id, StringComparison.Ordinal));

        // The running numbers are the ones the berths already had.
        Assert.Equal(
            berths.Select(berth => berth.Id[^2..]).ToList(),
            renamed.Select(berth => berth.Id[^2..]).ToList());
    }

    [Fact]
    public void AnIdAlreadyInUse_IsNotFree()
    {
        var marina = WithPier(out var designer);
        marina.AddPier(new Pier("B", "Pier B", new Vector2(40, 0), 0f, 30f));
        var berths = designer.CreateBerths("A", PierSide.Left, 0f, 10f);

        Assert.False(designer.IsPierIdAvailable("B"));
        Assert.False(designer.IsPierIdAvailable("b"));           // ids ignore case
        Assert.False(designer.IsPierIdAvailable(" "));
        Assert.True(designer.IsPierIdAvailable("B", forPierId: "B"));   // keeping its own is fine
        Assert.True(designer.IsPierIdAvailable("C"));

        Assert.False(designer.IsBerthNameAvailable(berths[0].Id));
        Assert.True(designer.IsBerthNameAvailable(berths[0].Id, forBerthId: berths[0].Id));
        Assert.True(designer.IsBerthNameAvailable("Harbourmaster"));

        // And the rename itself still refuses a taken id.
        Assert.Throws<InvalidOperationException>(() => designer.ChangePierId("A", "B"));
    }

    [Fact]
    public void RenumberBerths_PutsRightNamesLeftOverFromAnOlderId()
    {
        var marina = new MarinaVisualizer();
        var designer = marina.Designer;
        designer.PierBerthingSides = PierSides.Right;
        var pier = designer.CreatePier(new Vector2(0, 0), new Vector2(0, 60))!;
        var berths = designer.CreateBerths(pier.Id, PierSide.Right, 0f, 30f);
        Assert.True(berths.Count >= 3);

        // A berth given a name of its own has no running number, so it is left out of a renumber.
        designer.RenameBerth(berths[0].Id, "Harbourmaster");

        // Names left behind by an id this pier no longer has.
        foreach (var berth in berths.Skip(1)) marina.RenameBerth(berth.Id, "OLD" + berth.Id[pier.Id.Length..]);
        Assert.StartsWith("OLD", marina.GetBerthsByPier(pier.Id)[1].Id, StringComparison.Ordinal);

        var renamed = designer.RenumberBerths(pier.Id);

        Assert.Equal(berths.Count - 1, renamed.Count);
        Assert.NotNull(marina.GetBerth("Harbourmaster"));
        Assert.All(
            marina.GetBerthsByPier(pier.Id).Where(b => b.Id != "Harbourmaster"),
            berth => Assert.StartsWith(pier.Id + "-", berth.Id, StringComparison.Ordinal));

        // One step of undo puts every one of them back.
        Assert.True(designer.Undo());
        Assert.StartsWith("OLD", marina.GetBerthsByPier(pier.Id)[1].Id, StringComparison.Ordinal);
    }
}
