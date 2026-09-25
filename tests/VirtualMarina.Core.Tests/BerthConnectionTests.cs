using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// Which berths a boat can share (<see cref="Berth.ConnectedBerthIds"/>), and the divider tool that parts them.
/// </summary>
public class BerthConnectionTests
{
    /// <summary>
    /// A pier running south from (0, −25), 50 m long and 2 m wide, looked at from straight above, with
    /// <paramref name="count"/> berths 5 m wide and 10 m long along its right-hand side (−X): A-R01 at 0–5 m along it,
    /// A-R02 at 5–10 m, and so on.
    /// </summary>
    private static MarinaVisualizer Row(int count, out MarinaDesigner designer, DesignTool tool = DesignTool.PlaceDividers)
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(1000, 800);
        marina.Camera.SetPose(new CameraPose(Vector3.Zero, 0f, 89f, 150f), immediate: true);
        marina.AddPier(new Pier("A", "A", new Vector2(0, -25), 0f, 50f, 2f));
        designer = marina.Designer;
        designer.BerthWidth = 5f;
        designer.BerthLength = 10f;
        designer.CreateBerths("A", PierSide.Right, 0f, 5f * count);
        designer.IsActive = true;
        designer.Tool = tool;
        return marina;
    }

    /// <summary>A point on the water beside the right-hand row, <paramref name="along"/> meters down the pier.</summary>
    private static Vector2 Beside(float along) => new(-6f, -25f + along);

    private static void Click(MarinaVisualizer marina, Vector2 plan, PointerButton button = PointerButton.Left, InputModifiers modifiers = InputModifiers.None)
    {
        Assert.True(marina.TryProjectToScreen(MarinaMath.ToWorld(plan, 0f), out var s));
        marina.Input.PointerMove(s.X, s.Y, modifiers);
        marina.Input.PointerDown(s.X, s.Y, button, modifiers);
        marina.Input.PointerUp(s.X, s.Y, button, modifiers);
    }

    private static string[] Connections(MarinaVisualizer marina, string berthId) => [.. marina.GetBerth(berthId)!.ConnectedBerthIds];

    // ---- Which berths are connected -------------------------------------------------------------

    [Fact]
    public void ARowOfBerths_IsConnectedNeighbourToNeighbour_ButNotAcrossThePier()
    {
        var marina = Row(3, out var designer);
        designer.CreateBerths("A", PierSide.Left, 0f, 5f);

        Assert.Equal(["A-R02"], Connections(marina, "A-R01"));
        Assert.Equal(["A-R01", "A-R03"], Connections(marina, "A-R02"));
        Assert.Equal(["A-R02"], Connections(marina, "A-R03"));

        // The berth backing onto A-R01 from the other side of the pier faces the other way.
        Assert.Empty(Connections(marina, "A-L01"));
    }

    [Theory]
    [InlineData(DividerType.FingerPier)]
    [InlineData(DividerType.Piles)]
    [InlineData(DividerType.Boom)]
    [InlineData(DividerType.SinglePile)]
    public void ADividerOfAnyType_PartsTheBerthsEitherSide_AndTakingItAwayJoinsThemAgain(DividerType type)
    {
        var marina = Row(3, out var designer);
        designer.DividerType = type;

        var placed = Assert.Single(designer.PlaceDividers("A", PierSide.Right, 5f));
        Assert.Equal(type, placed.Type);
        Assert.Empty(Connections(marina, "A-R01"));
        Assert.Equal(["A-R03"], Connections(marina, "A-R02"));

        Assert.Single(designer.RemoveDividers("A", PierSide.Right, 5f));
        Assert.Equal(["A-R02"], Connections(marina, "A-R01"));
        Assert.Equal(["A-R01", "A-R03"], Connections(marina, "A-R02"));
    }

    [Fact]
    public void ABerthWithFingerPiersOfItsOwn_IsConnectedToNoOne()
    {
        var marina = Row(3, out _);
        marina.UpdateBerth(marina.GetBerth("A-R02")! with { HasFingerPiers = true });

        Assert.Empty(Connections(marina, "A-R01"));
        Assert.Empty(Connections(marina, "A-R02"));
        Assert.Empty(Connections(marina, "A-R03"));
    }

    [Theory]
    [InlineData(1f, 0f, true)]    // a metre of open water between them
    [InlineData(2f, 0f, false)]   // too far apart for one boat to lie across both
    [InlineData(0f, 4f, true)]    // staggered, but mostly level
    [InlineData(0f, 8f, false)]   // barely beside each other at all
    public void BerthsAreConnected_OnlyWhenCloseAndLevel(float gap, float stagger, bool connected)
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "A", new Vector2(0, -25), 0f, 50f, 2f));
        var pier = marina.GetPier("A")!;
        var first = BerthGenerator.AtPier(pier, "one", PierSide.Right, 0f, 5f, 10f) with { HasFingerPiers = false };
        var second = BerthGenerator.AtPier(pier, "two", PierSide.Right, 5f + gap, 5f, 10f) with { HasFingerPiers = false };
        marina.AddBerths([first, second with { Center = second.Center + second.Forward * stagger }]);

        Assert.Equal(connected, Connections(marina, "one").SequenceEqual(["two"]));
        Assert.Equal(connected, Connections(marina, "two").SequenceEqual(["one"]));
    }

    [Fact]
    public void LandBerthsSideBySide_AreConnectedToo()
    {
        var marina = new MarinaVisualizer();
        marina.AddLandArea(new LandArea("yard", [new(0, 0), new(40, 0), new(40, 40), new(0, 40)], 1f, LandKind.Quay));
        marina.AddBerths(
        [
            Berth.OnLand("Y-1", "yard", new Vector2(10, 20), 0f, 12f, 5f),
            Berth.OnLand("Y-2", "yard", new Vector2(15, 20), 0f, 12f, 5f),
            Berth.OnLand("Y-3", "yard", new Vector2(30, 20), 0f, 12f, 5f),
        ]);

        Assert.Equal(["Y-2"], Connections(marina, "Y-1"));
        Assert.Equal(["Y-1"], Connections(marina, "Y-2"));
        Assert.Empty(Connections(marina, "Y-3"));
    }

    [Fact]
    public void Connections_FollowARenameAndAMove_AndAreNeverTakenFromTheCaller()
    {
        var marina = Row(3, out var designer);
        designer.RenameBerth("A-R02", "VIP");
        Assert.Equal(["VIP"], Connections(marina, "A-R01"));
        Assert.Equal(["A-R01", "A-R03"], Connections(marina, "VIP"));

        var third = marina.GetBerth("A-R03")!;
        marina.UpdateBerth(third with { Center = third.Center + new Vector2(0, 20) });
        Assert.Equal(["A-R01"], Connections(marina, "VIP"));
        Assert.Empty(Connections(marina, "A-R03"));

        // A status change keeps them; a caller's own list is ignored either way.
        marina.UpdateBerth(marina.GetBerth("A-R01")! with { Status = BerthStatus.Occupied, Boat = new Boat("B", "Aurora", BoatType.MotorYacht), ConnectedBerthIds = ["nonsense"] });
        Assert.Equal(["VIP"], Connections(marina, "A-R01"));
    }

    [Fact]
    public void ALayoutBuiltInCode_WorksItsConnectionsOut_WithoutAVisualizer()
    {
        var pier = new Pier("A", "A", new Vector2(0, -25), 0f, 50f, 2f);
        var berths = Enumerable.Range(0, 3)
            .Select(i => BerthGenerator.AtPier(pier, $"B{i + 1}", PierSide.Right, i * 5f, 5f, 10f) with { HasFingerPiers = false })
            .ToArray();
        var layout = new MarinaLayout
        {
            Piers = [pier],
            Berths = berths,
            Dividers = [BerthGenerator.DividerAtPier(pier, "D1", PierSide.Right, 10f, 10f, DividerType.Piles)],
        };
        Assert.All(layout.Berths, berth => Assert.Empty(berth.ConnectedBerthIds));

        var connected = layout.WithBerthConnections();
        Assert.Equal(["B2"], connected.Berths[0].ConnectedBerthIds);
        Assert.Equal(["B1"], connected.Berths[1].ConnectedBerthIds);
        Assert.Empty(connected.Berths[2].ConnectedBerthIds);

        // A visualizer showing the same layout comes to the same answer.
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(layout);
        Assert.Equal(connected.Berths.Select(berth => berth.ConnectedBerthIds), marina.GetBerths().Select(berth => berth.ConnectedBerthIds));
    }

    // ---- The divider tool -----------------------------------------------------------------------

    [Fact]
    public void AClick_PutsADividerOnTheNearestBoundary_AndASecondClickTakesItAway()
    {
        var marina = Row(3, out _);

        Click(marina, Beside(5.8f));
        var divider = Assert.Single(marina.GetDividers());
        Assert.Equal("A", divider.PierId);
        Assert.Equal(new Vector2(-1f, -20f), divider.Start); // at the pier's edge, 5 m along it
        Assert.Empty(Connections(marina, "A-R01"));

        Click(marina, Beside(4.5f));
        Assert.Empty(marina.GetDividers());
        Assert.Equal(["A-R02"], Connections(marina, "A-R01"));
    }

    [Fact]
    public void AltClick_FillsTheRow_EveryIntervalBoundariesCountingFromTheOneClicked()
    {
        var marina = Row(4, out var designer);
        designer.DividerInterval = 2;

        Click(marina, Beside(5f), modifiers: InputModifiers.Alt);

        // The boundaries are at 0, 5, 10, 15 and 20 m; counting from 5, every other one is 5 and 15.
        var along = marina.GetDividers().Select(d => MathF.Round(d.Start.Y + 25f, 2)).Order();
        Assert.Equal([5f, 15f], along);
        Assert.Empty(Connections(marina, "A-R01"));
        Assert.Equal(["A-R03"], Connections(marina, "A-R02"));
        Assert.Equal(["A-R02"], Connections(marina, "A-R03"));
        Assert.Empty(Connections(marina, "A-R04"));

        // Filling it again at every boundary adds only the ones missing.
        designer.DividerInterval = 1;
        Click(marina, Beside(0f), modifiers: InputModifiers.Alt);
        Assert.Equal(5, marina.GetDividers().Count);
        Assert.All(marina.GetBerths(), berth => Assert.Empty(berth.ConnectedBerthIds));
    }

    [Fact]
    public void CtrlClick_AndARightClick_OnlyRemove_AndWithAltClearTheWholeRow()
    {
        var marina = Row(4, out var designer);
        designer.PlaceDividers("A", PierSide.Right, 0f, wholeRow: true);
        Assert.Equal(5, marina.GetDividers().Count);

        Click(marina, Beside(10f), modifiers: InputModifiers.Control);
        Assert.Equal(4, marina.GetDividers().Count);
        Click(marina, Beside(10f), modifiers: InputModifiers.Control); // nothing left there, and Ctrl never adds
        Assert.Equal(4, marina.GetDividers().Count);

        Click(marina, Beside(5f), PointerButton.Right);
        Assert.Equal(3, marina.GetDividers().Count);

        Click(marina, Beside(12f), modifiers: InputModifiers.Control | InputModifiers.Alt);
        Assert.Empty(marina.GetDividers());
    }

    [Fact]
    public void ARowWithAGapInIt_HasABoundaryOnEachSideOfTheGap()
    {
        var marina = Row(2, out var designer);
        designer.AlignBerthsToExisting = false;
        designer.CreateBerths("A", PierSide.Right, 20f, 20f);

        Assert.Equal(5, designer.PlaceDividers("A", PierSide.Right, 0f, wholeRow: true).Count);
        Assert.Equal([0f, 5f, 10f, 20f, 25f], marina.GetDividers().Select(d => MathF.Round(d.Start.Y + 25f, 2)).Order());
    }

    [Fact]
    public void PlacingAndRemovingDividers_AreOneUndoStepEach_AndRaiseTheDesignerEvents()
    {
        var marina = Row(3, out var designer);
        var created = new List<DesignElementCreatedEventArgs>();
        var erased = new List<DesignElementErasedEventArgs>();
        designer.ElementCreated += (_, e) => created.Add(e);
        designer.ElementErased += (_, e) => erased.Add(e);

        var placed = designer.PlaceDividers("A", PierSide.Right, 0f, wholeRow: true);
        Assert.Equal(4, placed.Count);
        Assert.Equal(DesignTool.PlaceDividers, Assert.Single(created).Tool);
        Assert.Equal(placed, created[0].Dividers);
        Assert.Equal("Place 4 dividers", designer.UndoDescription);

        Assert.True(designer.Undo());
        Assert.Empty(marina.GetDividers());
        Assert.Equal(["A-R02"], Connections(marina, "A-R01"));
        Assert.True(designer.Redo());
        Assert.Equal(4, marina.GetDividers().Count);
        Assert.Empty(Connections(marina, "A-R01"));

        var removed = Assert.Single(designer.RemoveDividers("A", PierSide.Right, 5f));
        Assert.Equal(removed, Assert.Single(erased).Element);
        Assert.Equal("Remove 1 divider", designer.UndoDescription);
        designer.RemoveDividers("A", PierSide.Right, 5f, wholeRow: true);
        Assert.Equal(marina.GetPier("A"), erased[^1].Element); // a whole row is reported as the pier's

        Assert.True(designer.Undo());
        Assert.True(designer.Undo());
        Assert.Equal(4, marina.GetDividers().Count);
    }

    [Fact]
    public void AHandlerCanRenameOrCancelTheDividers()
    {
        var marina = Row(3, out var designer);
        designer.ElementCreating += (_, e) => e.Dividers = e.Dividers.Select(d => d with { Id = "ERP-" + d.Id }).ToArray();
        Assert.StartsWith("ERP-", Assert.Single(designer.PlaceDividers("A", PierSide.Right, 5f)).Id, StringComparison.Ordinal);

        designer.ElementCreating += (_, e) => e.Cancel = true;
        Assert.Empty(designer.PlaceDividers("A", PierSide.Right, 10f));
        Assert.Single(marina.GetDividers());
    }

    [Fact]
    public void TheBerthTool_PlacesNoDividers_AndGivesItsBerthsNoFingerPiers()
    {
        var marina = Row(3, out var designer, DesignTool.AddBerths);
        var created = new List<DesignElementCreatedEventArgs>();
        designer.ElementCreated += (_, e) => created.Add(e);
        designer.CreateBerths("A", PierSide.Left, 0f, 15f);

        Assert.Empty(marina.GetDividers());
        Assert.Empty(Assert.Single(created).Dividers);
        Assert.All(marina.GetBerths(), berth => Assert.False(berth.HasFingerPiers));
    }

    [Fact]
    public void ThePreviewFleet_OnlyPutsABoatAcrossBerthsThatAreConnected()
    {
        var marina = Row(10, out var designer);
        designer.PlaceDividers("A", PierSide.Right, 0f, wholeRow: true);

        for (var seed = 0; seed < 20; seed++)
        {
            var plan = PreviewFleet.Plan(marina.GetBerths(), 10, new Random(seed));
            Assert.All(plan, mooring => Assert.Single(mooring.BerthIds));
        }
    }
}
