using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Input;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Resources;
using VirtualMarina.SampleData;

namespace VirtualMarina.Core.Tests;

/// <summary>The layout designer, driven through the same input path as the host views.</summary>
public class DesignerTests
{
    /// <summary>An empty marina looking straight down at the origin, north up, with the designer on.</summary>
    private static MarinaVisualizer CreateDesigner(DesignTool tool)
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(1000, 800);
        marina.Camera.SetPose(new CameraPose(Vector3.Zero, 0f, 89f, 150f), immediate: true);
        marina.Designer.IsActive = true;
        marina.Designer.Tool = tool;
        return marina;
    }

    /// <summary>Where a plan point shows on screen, at <paramref name="height"/> above the water.</summary>
    private static Vector2 Screen(MarinaVisualizer marina, Vector2 plan, float height = 0f)
    {
        Assert.True(marina.TryProjectToScreen(MarinaMath.ToWorld(plan, height), out var screen));
        return screen;
    }

    /// <summary>
    /// Clicks where a plan point shows. A drawing is picked on the plane it is drawn on — a land outline at the land's height, a
    /// pier at its deck — so a click meant for a point of one gives its height.
    /// </summary>
    private static void Click(MarinaVisualizer marina, Vector2 plan, PointerButton button = PointerButton.Left, InputModifiers modifiers = InputModifiers.None, float height = 0f)
    {
        var s = Screen(marina, plan, height);
        marina.Input.PointerMove(s.X, s.Y, modifiers);
        marina.Input.PointerDown(s.X, s.Y, button, modifiers);
        marina.Input.PointerUp(s.X, s.Y, button, modifiers);
    }

    private static void AssertNear(Vector2 expected, Vector2 actual, float tolerance = 0.05f) =>
        Assert.True(Vector2.Distance(expected, actual) <= tolerance, $"expected {expected}, got {actual}");

    [Fact]
    public void DrawLandArea_ClicksThenEnter_AddsAPolygonWithTheChosenKindAndHeight()
    {
        var marina = CreateDesigner(DesignTool.DrawLandArea);
        var designer = marina.Designer;
        designer.LandKind = LandKind.Breakwater;
        designer.LandHeight = 2.5f;
        var drafts = new List<DesignDraftChangedEventArgs>();
        var created = new List<DesignElementCreatedEventArgs>();
        var layoutChanges = new List<LayoutChangeKind>();
        designer.DraftChanged += (_, e) => drafts.Add(e);
        designer.ElementCreated += (_, e) => created.Add(e);
        marina.LayoutChanged += (_, e) => layoutChanges.Add(e.Kind);

        var outline = new[] { new Vector2(-30, -20), new Vector2(30, -20), new Vector2(30, 0), new Vector2(0, 20), new Vector2(-30, 10) };
        foreach (var point in outline) Click(marina, point, height: designer.LandHeight);
        Assert.Equal(outline.Length, designer.DraftPoints.Count);
        Assert.True(marina.Input.KeyDown(MarinaKey.Enter));

        var land = Assert.Single(marina.GetLandAreas());
        Assert.Equal(LandKind.Breakwater, land.Kind);
        Assert.Equal(2.5f, land.Height);
        Assert.Equal(outline.Length, land.Points.Count);
        for (var i = 0; i < outline.Length; i++) AssertNear(outline[i], land.Points[i]);

        Assert.Equal(outline.Length, drafts.Count(d => d.Change == DesignDraftChange.PointAdded));
        Assert.Equal(DesignDraftChange.Completed, drafts[^1].Change);
        Assert.Same(land, Assert.Single(created).LandArea);
        Assert.Contains(LayoutChangeKind.LandAreaAdded, layoutChanges);
        Assert.False(designer.HasDraft);
        Assert.True(marina.Meshes.TryGet(MeshIds.ForLand(0), out _));
    }

    [Fact]
    public void DrawLandArea_ClickingTheFirstCornerCloses_AndBackspaceAndEscapeEditTheDraft()
    {
        var marina = CreateDesigner(DesignTool.DrawLandArea);
        var designer = marina.Designer;

        Click(marina, new Vector2(-10, -10));
        Click(marina, new Vector2(10, -10));
        Click(marina, new Vector2(99, 99));
        Assert.True(marina.Input.KeyDown(MarinaKey.Backspace));
        Assert.Equal(2, designer.DraftPoints.Count);

        Click(marina, new Vector2(10, 10));
        Click(marina, new Vector2(-10, 10));
        Click(marina, new Vector2(-10, -10)); // the first corner
        Assert.Equal(4, Assert.Single(marina.GetLandAreas()).Points.Count);

        Click(marina, new Vector2(40, 40));
        Assert.True(marina.Input.KeyDown(MarinaKey.Escape));
        Assert.False(designer.HasDraft);
        Assert.True(marina.Input.KeyDown(MarinaKey.Escape)); // second Escape returns to navigation
        Assert.Equal(DesignTool.Navigate, designer.Tool);
    }

    [Fact]
    public void SelectArea_TheBoxFollowsTheCamera_NotTheCompass()
    {
        var marina = CreateDesigner(DesignTool.SelectArea);
        var designer = marina.Designer;

        // A row running north-east, the way a real marina rarely lines up with the compass.
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, 0), 45f, 70f));
        var rowA = designer.CreateBerths("A", PierSide.Left, 0f, 60f);
        Assert.True(rowA.Count >= 3);

        var first = marina.GetBerth(rowA[0].Id)!.Center;
        var last = marina.GetBerth(rowA[^1].Id)!.Center;
        var direction = Vector2.Normalize(last - first);
        var across = new Vector2(-direction.Y, direction.X);
        var along = MarinaMath.DirectionToHeading(direction);

        // A second row alongside the first: clear of it, but well inside the north-up square around it.
        marina.AddPier(new Pier("B", "Pier B", first + across * 24f, 45f, 70f));
        var rowB = designer.CreateBerths("B", PierSide.Left, 0f, 60f);
        Assert.True(rowB.Count >= 3);

        // A box drawn snugly around row A, the way it looks on screen when the view runs along the row.
        var from = first - direction * 4f - across * 4f;
        var to = last + direction * 4f + across * 4f;

        var turned = designer.SelectBerthsInArea(from, to, along);
        Assert.Equal(rowA.Count, turned.Count);
        Assert.All(turned, id => Assert.StartsWith("A-", id));

        // Read as a north-up box, those same corners sweep a square that reaches across to the other pier.
        var northUp = designer.SelectBerthsInArea(from, to, 0f);
        Assert.Contains(northUp, id => id.StartsWith("B-", StringComparison.Ordinal));
    }

    [Fact]
    public void SelectArea_TheDraggedBoxIsDrawnTurnedToTheView()
    {
        var marina = CreateDesigner(DesignTool.SelectArea);
        marina.Camera.SetPose(new CameraPose(Vector3.Zero, 30f, 89f, 150f), immediate: true);
        var designer = marina.Designer;

        Assert.Null(designer.SelectionQuad);

        var from = Screen(marina, new Vector2(-30, -20));
        var to = Screen(marina, new Vector2(30, 20));
        marina.Input.PointerMove(from.X, from.Y, InputModifiers.None);
        marina.Input.PointerDown(from.X, from.Y, PointerButton.Left, InputModifiers.None);
        marina.Input.PointerMove(to.X, to.Y, InputModifiers.None);

        var quad = designer.SelectionQuad;
        Assert.NotNull(quad);
        Assert.Equal(4, quad.Count);

        // Its sides run with the camera, so none of them is aligned to north while the view is turned.
        var side = Vector2.Normalize(quad[1] - quad[0]);
        Assert.True(MathF.Abs(side.X) > 0.05f && MathF.Abs(side.Y) > 0.05f, $"the box is still axis-aligned: {side}");

        // A turned box is still a rectangle: its two pairs of opposite sides match.
        Assert.Equal(Vector2.Distance(quad[0], quad[1]), Vector2.Distance(quad[3], quad[2]), 2);
        Assert.Equal(Vector2.Distance(quad[1], quad[2]), Vector2.Distance(quad[0], quad[3]), 2);

        marina.Input.PointerUp(to.X, to.Y, PointerButton.Left, InputModifiers.None);
        Assert.Null(designer.SelectionQuad);
    }

    [Fact]
    public void DrawShoreline_DrawThenEnterThenClickTheLandSide_MakesTheMainland()
    {
        var marina = CreateDesigner(DesignTool.DrawShoreline);
        var designer = marina.Designer;
        designer.Scenery = HinterlandScenery.Town;
        var created = new List<DesignElementCreatedEventArgs>();
        var layoutChanges = new List<LayoutChangeKind>();
        designer.ElementCreated += (_, e) => created.Add(e);
        marina.LayoutChanged += (_, e) => layoutChanges.Add(e.Kind);

        Click(marina, new Vector2(-60, -20));
        Click(marina, new Vector2(0, -30));
        Click(marina, new Vector2(60, -20));

        // Enter does not finish a coast; it settles the line and waits for the side. The key was still used.
        Assert.True(marina.Input.KeyDown(MarinaKey.Enter));
        Assert.Null(marina.Shoreline);
        Assert.Equal(3, designer.ShorelineAwaitingSide?.Count);

        // North of the line, which is to the right of it walking east.
        Click(marina, new Vector2(0, -120));

        var shoreline = marina.Shoreline;
        Assert.NotNull(shoreline);
        Assert.False(shoreline.LandOnLeft);
        Assert.True(shoreline.Contains(new Vector2(0, -400)));
        Assert.False(shoreline.Contains(new Vector2(0, 400)));
        Assert.Equal(HinterlandScenery.Town, shoreline.Scenery);
        Assert.Null(designer.ShorelineAwaitingSide);
        Assert.Same(shoreline, Assert.Single(created).Shoreline);
        Assert.Contains(LayoutChangeKind.ShorelineChanged, layoutChanges);
        Assert.True(marina.Meshes.TryGet(MeshIds.Shoreline, out _));

        // Undo takes it away again, back to open water.
        Assert.True(designer.Undo());
        Assert.Null(marina.Shoreline);
    }

    [Fact]
    public void DrawShoreline_ClickingTheOtherSide_PutsTheLandThere()
    {
        var marina = CreateDesigner(DesignTool.DrawShoreline);
        Click(marina, new Vector2(-60, -20));
        Click(marina, new Vector2(60, -20));
        marina.Input.KeyDown(MarinaKey.Enter);
        Click(marina, new Vector2(0, 120));

        Assert.True(marina.Shoreline!.LandOnLeft);
        Assert.True(marina.Shoreline.Contains(new Vector2(0, 400)));
    }

    [Fact]
    public void DrawShoreline_ALineThatCannotDivideThePlan_IsRefused()
    {
        var marina = CreateDesigner(DesignTool.DrawShoreline);

        // A C-shape whose endless ends lean into each other, so neither side of it is "the land".
        foreach (var p in new[] { new Vector2(0, 0), new Vector2(-40, 80), new Vector2(120, 80), new Vector2(80, 0) }) Click(marina, p);

        Assert.False(marina.Input.KeyDown(MarinaKey.Enter));
        Assert.Null(marina.Designer.ShorelineAwaitingSide);
        Assert.Null(marina.Shoreline);
        Assert.Equal(4, marina.Designer.DraftPoints.Count);
    }

    [Fact]
    public void DrawShoreline_BackspaceWhileChoosingTheSide_PutsTheLineBackOnTheBoard()
    {
        var marina = CreateDesigner(DesignTool.DrawShoreline);
        var designer = marina.Designer;
        Click(marina, new Vector2(-60, -20));
        Click(marina, new Vector2(60, -20));
        marina.Input.KeyDown(MarinaKey.Enter);

        Assert.True(marina.Input.KeyDown(MarinaKey.Backspace));
        Assert.Null(designer.ShorelineAwaitingSide);
        Assert.Equal(2, designer.DraftPoints.Count);

        // And Escape drops the whole thing.
        Assert.True(marina.Input.KeyDown(MarinaKey.Escape));
        Assert.False(designer.HasDraft);
        Assert.Null(marina.Shoreline);
    }

    [Fact]
    public void DrawShoreline_DrawingASecondOne_ReplacesTheFirst_AndDeleteRemovesIt()
    {
        var marina = CreateDesigner(DesignTool.DrawShoreline);
        var designer = marina.Designer;

        Click(marina, new Vector2(-60, -20), height: designer.LandHeight);
        Click(marina, new Vector2(60, -20), height: designer.LandHeight);
        marina.Input.KeyDown(MarinaKey.Enter);
        Click(marina, new Vector2(0, 120));
        var first = marina.Shoreline;

        // The coast is drawn at the height of the mainland it replaces.
        Click(marina, new Vector2(-60, 40), height: first!.Height);
        Click(marina, new Vector2(60, 40), height: first.Height);
        marina.Input.KeyDown(MarinaKey.Enter);
        Click(marina, new Vector2(0, 200));

        Assert.NotSame(first, marina.Shoreline);
        Assert.Equal(40f, marina.Shoreline!.Points[0].Y, 1);

        Assert.True(designer.DeleteShoreline());
        Assert.False(designer.DeleteShoreline());
        Assert.Null(marina.Shoreline);
        Assert.False(marina.Meshes.TryGet(MeshIds.Shoreline, out _));

        // Undoing the deletion brings the same coast back.
        Assert.True(designer.Undo());
        Assert.Equal(40f, marina.Shoreline!.Points[0].Y, 1);
    }

    [Fact]
    public void DrawLandArea_SelfCrossingOutlineIsNotCreated()
    {
        var marina = CreateDesigner(DesignTool.DrawLandArea);
        foreach (var p in new[] { new Vector2(0, 0), new Vector2(20, 20), new Vector2(20, 0), new Vector2(0, 20) }) Click(marina, p);

        Assert.False(marina.Input.KeyDown(MarinaKey.Enter));
        Assert.Empty(marina.GetLandAreas());
        Assert.Equal(4, marina.Designer.DraftPoints.Count);
    }

    [Fact]
    public void DrawPier_TwoClicks_AddAPierWithTheChosenTypeWidthAndSides_AndSnapsToLandCorners()
    {
        var marina = CreateDesigner(DesignTool.DrawPier);
        marina.AddLandArea(new LandArea("quay", new[] { new Vector2(-50, -40), new Vector2(50, -40), new Vector2(50, -20), new Vector2(-50, -20) }, 1f));
        var designer = marina.Designer;
        designer.PierType = PierType.Concrete;
        designer.PierWidth = 4f;
        designer.PierBerthingSides = PierSides.Left;

        var deck = Pier.GetDefaultDeckHeight(PierType.Concrete);
        Click(marina, new Vector2(50.4f, -20.3f), height: deck); // within snapping distance of the quay corner
        Click(marina, new Vector2(50, 20), height: deck);

        var pier = Assert.Single(marina.GetPiers());
        Assert.Equal("A", pier.Id);
        Assert.Equal(PierType.Concrete, pier.Type);
        Assert.Equal(4f, pier.Width);
        Assert.Equal(PierSides.Left, pier.BerthingSides);
        Assert.Equal(new Vector2(50, -20), pier.Start);
        Assert.Equal(40f, pier.Length, 1);
        Assert.Equal(0f, pier.HeadingDegrees, 1);
    }

    [Fact]
    public void DrawPier_ShiftSnapsTheDirectionTo15Degrees_AndCreatingCanRenameOrCancel()
    {
        var marina = CreateDesigner(DesignTool.DrawPier);
        var designer = marina.Designer;
        designer.ElementCreating += (_, e) =>
        {
            if (e.Pier is { } d) e.Pier = d with { Id = "PIER-" + d.Id, Name = "Pier" };
        };

        Click(marina, new Vector2(0, 0));
        Click(marina, new Vector2(30, 28), modifiers: InputModifiers.Shift);
        var pier = Assert.Single(marina.GetPiers());
        Assert.Equal("PIER-A", pier.Id);
        Assert.Equal(45f, pier.HeadingDegrees, 1);

        designer.ElementCreating += (_, e) => e.Cancel = true;
        Click(marina, new Vector2(-40, 0));
        Click(marina, new Vector2(-40, 30));
        Assert.Single(marina.GetPiers());
        Assert.False(designer.HasDraft);
    }

    [Fact]
    public void DrawPier_SquaresUpWithTheQuayAndTheOtherPiers_UnlessAltIsHeld()
    {
        // A quay running east-west, so the square directions are 0, 90, 180 and 270.
        var marina = CreateDesigner(DesignTool.DrawPier);
        marina.AddLandArea(new LandArea("quay", new[] { new Vector2(-50, -40), new Vector2(50, -40), new Vector2(50, -20), new Vector2(-50, -20) }, 1f));

        // Drawn a few degrees off square, it comes out square.
        Click(marina, new Vector2(0, -19));
        Click(marina, new Vector2(3, 40));
        var pier = Assert.Single(marina.GetPiers());
        Assert.Equal(0f, pier.HeadingDegrees, 1);

        // A second pier lines up with the first one the same way.
        Click(marina, new Vector2(-30, -19));
        Click(marina, new Vector2(-27, 20), modifiers: InputModifiers.None);
        Assert.Equal(0f, marina.GetPiers()[1].HeadingDegrees, 1);

        // Alt draws exactly what the pointer says.
        Click(marina, new Vector2(30, -19), modifiers: InputModifiers.Alt);
        Click(marina, new Vector2(33, 21), modifiers: InputModifiers.Alt);
        var free = marina.GetPiers()[2];
        Assert.Equal(MarinaMath.DirectionToHeading(new Vector2(3, 40)), free.HeadingDegrees, 1);
        Assert.NotEqual(0f, free.HeadingDegrees, 1);
    }

    [Fact]
    public void DrawPier_SquaringUp_LeavesAClearlyDifferentDirectionAlone()
    {
        var marina = CreateDesigner(DesignTool.DrawPier);
        marina.AddLandArea(new LandArea("quay", new[] { new Vector2(-50, -40), new Vector2(50, -40), new Vector2(50, -20), new Vector2(-50, -20) }, 1f));

        // 30° off square is a direction the user means; only small slips are corrected.
        // The first click snaps to the quay edge at y = -20, so this second one is exactly 30° from there.
        Click(marina, new Vector2(0, -19));
        Click(marina, new Vector2(30f, 31.96f));

        var pier = Assert.Single(marina.GetPiers());
        Assert.Equal(30f, pier.HeadingDegrees, 1);
    }

    [Fact]
    public void AddBerths_RowBetweenTwoClicks_UsesSizeAndDepth_AlignsWithExistingBerths_AndSkipsTakenPlaces()
    {
        var marina = CreateDesigner(DesignTool.AddBerths);
        marina.AddPier(new Pier("A", "A", new Vector2(0, -25), 0f, 50f, 2f));
        var designer = marina.Designer;
        designer.BerthWidth = 5f;
        designer.BerthLength = 10f;
        designer.BerthDepth = 3.5f;

        // The pier runs along +Z (south on a north-up view); its right-hand side is −X. From 10 m to 24 m along it: berths at 10, 15 and 20 m.
        Click(marina, new Vector2(-8, -25 + 10));
        Click(marina, new Vector2(-8, -25 + 24));
        var row = marina.GetBerthsByPier("A");
        Assert.Equal(new[] { "A-R01", "A-R02", "A-R03" }, row.Select(s => s.Id));
        Assert.All(row, s =>
        {
            Assert.Equal(3.5f, s.MaxDraft);
            Assert.Equal(10f, s.Length);
            Assert.Equal(5f, s.Width);
            Assert.True(s.Center.X < -1f);
        });
        AssertNear(new Vector2(-1 - 5, -25 + 12.5f), row[0].Center);

        // A click inside that row adds nothing; one past its end adds a berth lined up with it.
        Click(marina, new Vector2(-8, -25 + 12));
        Click(marina, new Vector2(-8, -25 + 12));
        Assert.Equal(3, marina.GetBerthsByPier("A").Count);
        Click(marina, new Vector2(-8, -25 + 26.5f));
        Click(marina, new Vector2(-8, -25 + 26.5f));
        var added = marina.GetBerth("A-R04")!;
        AssertNear(new Vector2(-6, -25 + 27.5f), added.Center);
    }

    [Fact]
    public void AddBerths_OnTheClosedSideOfASingleSidedPier_DoesNothing_AndDividersCanBeGenerated()
    {
        var marina = CreateDesigner(DesignTool.AddBerths);
        marina.AddPier(new Pier("Q", "Q", new Vector2(0, -25), 0f, 50f, 2f) { BerthingSides = PierSides.Left });
        var designer = marina.Designer;
        designer.BerthSeparators = BerthSeparator.Piles;

        // Right-hand side (−X) is closed.
        Click(marina, new Vector2(-6, -15));
        Click(marina, new Vector2(-6, -5));
        Assert.Empty(marina.GetBerths());
        Assert.False(designer.HasDraft);

        Click(marina, new Vector2(6, -15));
        Click(marina, new Vector2(6, -6));
        var berths = marina.GetBerthsByPier("Q");
        Assert.Equal(2, berths.Count);
        Assert.All(berths, s => Assert.False(s.HasFingerPiers));
        Assert.Equal(3, marina.GetDividersByPier("Q").Count); // shared boundary between the two berths
    }

    [Fact]
    public void AddBerths_SeparatorNone_LeavesAGapWithNoDividersOrFingerPiers()
    {
        var marina = CreateDesigner(DesignTool.AddBerths);
        marina.AddPier(new Pier("A", "A", new Vector2(0, -25), 0f, 50f, 2f));
        var designer = marina.Designer;
        designer.BerthWidth = 5f;
        designer.BerthLength = 10f;
        designer.BerthSeparators = BerthSeparator.None;

        Click(marina, new Vector2(-8, -25));
        Click(marina, new Vector2(-8, -25 + 10));

        var berths = marina.GetBerthsByPier("A");
        Assert.Equal(2, berths.Count);
        Assert.All(berths, berth => Assert.False(berth.HasFingerPiers));
        Assert.Empty(marina.GetDividers());
        // The berths are a hand's width apart instead of touching.
        Assert.Equal(5f + MarinaDesigner.MinimumSeparatorGap, Vector2.Distance(berths[0].Center, berths[1].Center), 2);
    }

    [Fact]
    public void AddBerths_SinglePileSeparator_PutsOnePileAtEveryBoundary()
    {
        var marina = CreateDesigner(DesignTool.AddBerths);
        marina.AddPier(new Pier("A", "A", new Vector2(0, -25), 0f, 50f, 2f));
        var designer = marina.Designer;
        designer.BerthWidth = 5f;
        designer.BerthLength = 10f;
        designer.BerthSeparators = BerthSeparator.SinglePile;

        Click(marina, new Vector2(-8, -25));
        Click(marina, new Vector2(-8, -25 + 10));

        var dividers = marina.GetDividersByPier("A");
        Assert.Equal(3, dividers.Count); // two berths share the middle boundary
        Assert.All(dividers, divider =>
        {
            Assert.Equal(DividerType.SinglePile, divider.Type);
            Assert.Equal(10f, divider.Length); // the pile stands at End, the outer end of the berth
            Assert.Equal(-1f, divider.Start.X, 2); // at the pier's right-hand edge
        });
    }

    [Fact]
    public void AddBerths_WithAGapAndFreeStart_PlacesTheFirstBerthExactlyWhereClicked()
    {
        var marina = CreateDesigner(DesignTool.AddBerths);
        marina.AddPier(new Pier("A", "A", new Vector2(0, -25), 0f, 50f, 2f));
        var designer = marina.Designer;
        designer.BerthWidth = 5f;
        designer.BerthLength = 10f;
        designer.BerthGap = 1f;
        designer.AlignBerthsToExisting = false;

        // 3.5 m along the pier is not a multiple of the berth width; the row starts there anyway.
        Click(marina, new Vector2(-8, -25 + 3.5f));
        Click(marina, new Vector2(-8, -25 + 15f));

        var berths = marina.GetBerthsByPier("A");
        Assert.Equal(2, berths.Count);
        AssertNear(new Vector2(-6, -25 + 6f), berths[0].Center);   // 3.5 + half a berth
        AssertNear(new Vector2(-6, -25 + 12f), berths[1].Center);  // one gap further on
    }

    [Fact]
    public void AddBerths_SwitchesOnThePierPedestals_OneBetweenEveryTwoBerths()
    {
        var marina = CreateDesigner(DesignTool.AddBerths);
        marina.AddPier(new Pier("A", "A", new Vector2(0, -25), 0f, 50f, 2f));
        var designer = marina.Designer;
        designer.BerthWidth = 5f;
        designer.BerthLength = 10f;
        designer.BerthServices = PierServices.PowerAndWater;

        Click(marina, new Vector2(-8, -25));
        Click(marina, new Vector2(-8, -25 + 15));

        var pier = marina.GetPier("A")!;
        Assert.Equal(3, marina.GetBerthsByPier("A").Count);
        Assert.Equal(PierServices.PowerAndWater, pier.Services);
        var objects = marina.BuildRenderFrame().Objects;

        // Each pedestal is a post, a top and a band, on the deck and on the berths' side (−X): one between the first two berths
        // (5 m along), one halfway along the third, which has no partner (12.5 m along).
        var posts = objects.Where(o => MathF.Abs(o.World.Translation.X + 0.7f) < 0.01f && o.World.Translation.Y > pier.DeckHeight).ToList();
        Assert.Equal(6, posts.Count);
        Assert.Equal(new[] { -20f, -12.5f }, posts.Select(o => MathF.Round(o.World.Translation.Z, 2)).Distinct().OrderBy(z => z));

        marina.UpdatePier(pier with { Services = PierServices.None });
        Assert.Equal(6, objects.Count - marina.BuildRenderFrame().Objects.Count);
    }

    [Fact]
    public void AddBerths_PairedFingerPiers_GiveEveryBerthExactlyOnePier()
    {
        var marina = CreateDesigner(DesignTool.AddBerths);
        marina.AddPier(new Pier("A", "A", new Vector2(0, -25), 0f, 50f, 2f));
        var designer = marina.Designer;
        designer.BerthWidth = 5f;
        designer.BerthLength = 10f;
        designer.BerthSeparators = BerthSeparator.PairedFingerPiers;

        Click(marina, new Vector2(-8, -25));
        Click(marina, new Vector2(-8, -25 + 20));

        var berths = marina.GetBerthsByPier("A");
        Assert.Equal(4, berths.Count);
        Assert.All(berths, berth => Assert.False(berth.HasFingerPiers));

        // Piers at 0, 10 and 20 m along the pier: one pair of berths between each, and one at both ends of the row.
        var dividers = marina.GetDividersByPier("A");
        Assert.Equal(3, dividers.Count);
        Assert.All(dividers, divider => Assert.Equal(DividerType.FingerPier, divider.Type));
        Assert.Equal(new[] { -25f, -15f, -5f }, dividers.Select(d => MathF.Round(d.Start.Y, 2)).OrderBy(y => y));

        // Every berth has a pier along exactly one of its two long sides.
        foreach (var berth in berths)
        {
            var touching = dividers.Count(d => MathF.Abs(MathF.Abs(Vector2.Dot(d.Center - berth.Center, berth.Right)) - berth.Width * 0.5f) < 0.05f);
            Assert.Equal(1, touching);
        }
    }

    [Fact]
    public void Erase_Berth_TakesItsDividersWithIt_ButKeepsTheOnesANeighbourStillUses()
    {
        var marina = CreateDesigner(DesignTool.AddBerths);
        marina.AddPier(new Pier("A", "A", new Vector2(0, -35), 0f, 60f, 2f));
        var designer = marina.Designer;
        designer.BerthWidth = 5f;
        designer.BerthLength = 10f;
        designer.BerthSeparators = BerthSeparator.Piles;

        Click(marina, new Vector2(-8, -35 + 10));
        Click(marina, new Vector2(-8, -35 + 25));
        Assert.Equal(3, marina.GetBerthsByPier("A").Count);
        Assert.Equal(4, marina.GetDividersByPier("A").Count);

        var erased = new List<DesignElementErasedEventArgs>();
        designer.ElementErased += (_, e) => erased.Add(e);
        designer.Tool = DesignTool.Erase;
        marina.Camera.SetPose(new CameraPose(new Vector3(-6, 0, -20), 0f, 89f, 120f), immediate: true);

        // The outer divider of the last berth has no other berth to separate; the one it shares with A-R02 stays.
        Click(marina, marina.GetBerth("A-R03")!.Center);
        Assert.Null(marina.GetBerth("A-R03"));
        Assert.Equal(3, marina.GetDividersByPier("A").Count);
        Assert.Single(erased[^1].RemovedDividers);

        Click(marina, marina.GetBerth("A-R02")!.Center);
        Assert.Equal(2, marina.GetDividersByPier("A").Count);

        Click(marina, marina.GetBerth("A-R01")!.Center);
        Assert.Empty(marina.GetBerths());
        Assert.Empty(marina.GetDividers()); // nothing left behind
    }

    [Fact]
    public void Undo_StepsBackThroughDrawing_Erasing_AndPlanting()
    {
        var marina = CreateDesigner(DesignTool.DrawPier);
        var designer = marina.Designer;
        var undone = new List<string>();
        designer.ActionUndone += (_, e) => undone.Add(e.Description);
        Assert.False(designer.CanUndo);
        Assert.Null(designer.UndoDescription);

        var deck = Pier.GetDefaultDeckHeight(designer.PierType);
        Click(marina, new Vector2(0, -25), height: deck);
        Click(marina, new Vector2(0, 25), height: deck);
        designer.Tool = DesignTool.AddBerths;
        designer.BerthWidth = 5f;
        designer.BerthLength = 10f;
        designer.BerthSeparators = BerthSeparator.Piles;
        Click(marina, new Vector2(-8, -25 + 10));
        Click(marina, new Vector2(-8, -25 + 20));
        Assert.Equal(2, marina.GetBerths().Count);
        Assert.Equal("Add 2 berths", designer.UndoDescription);

        // Erasing, then undoing, brings the berth and its dividers back.
        designer.Tool = DesignTool.Erase;
        marina.Camera.SetPose(new CameraPose(new Vector3(-6, 0, -10), 0f, 89f, 120f), immediate: true);
        var dividersBefore = marina.GetDividers().Count;
        Click(marina, marina.GetBerth("A-R02")!.Center);
        Assert.Null(marina.GetBerth("A-R02"));

        Assert.True(designer.Undo());
        Assert.NotNull(marina.GetBerth("A-R02"));
        Assert.Equal(dividersBefore, marina.GetDividers().Count);

        // Then back past the berths and the pier itself.
        Assert.True(designer.Undo());
        Assert.Empty(marina.GetBerths());
        Assert.Empty(marina.GetDividers());
        Assert.Single(marina.GetPiers());

        Assert.True(designer.Undo());
        Assert.Empty(marina.GetPiers());
        Assert.False(designer.CanUndo);
        Assert.False(designer.Undo());
        Assert.Equal(new[] { "Erase berth A-R02", "Add 2 berths", "Draw pier A" }, undone);
    }

    [Fact]
    public void Undo_RestoresTheTreesAndIsClearedWhenALayoutIsLoaded()
    {
        var marina = CreateDesigner(DesignTool.PlantTrees);
        var designer = marina.Designer;
        marina.AddLandArea(new LandArea("lawn", Square(30f), 1f, LandKind.Grass));
        designer.TreeDensity = 30f;

        Assert.NotNull(designer.PlantTrees("lawn"));
        var planted = marina.GetLandArea("lawn")!.Trees;
        Assert.NotEmpty(planted);

        Assert.NotNull(designer.RemoveTrees("lawn"));
        Assert.Empty(marina.GetLandArea("lawn")!.Trees);
        Assert.True(marina.Input.KeyDown(MarinaKey.Undo)); // Ctrl+Z through the normal input path
        Assert.Equal(planted, marina.GetLandArea("lawn")!.Trees);

        marina.ClearLayout();
        Assert.False(designer.CanUndo);
    }

    [Fact]
    public void PlantTrees_WorksOnLawnsOnly_ReplacesThem_AndCtrlClickRemovesThem()
    {
        var marina = CreateDesigner(DesignTool.PlantTrees);
        var designer = marina.Designer;
        marina.AddLandArea(new LandArea("lawn", Square(25f, new Vector2(-40, 0)), 1f, LandKind.Grass));
        marina.AddLandArea(new LandArea("quay", Square(25f, new Vector2(40, 0)), 1f));
        designer.TreeDensity = 30f;
        var planted = new List<DesignTreesPlantedEventArgs>();
        designer.TreesPlanted += (_, e) => planted.Add(e);

        Click(marina, new Vector2(-40, 0));
        var first = marina.GetLandArea("lawn")!.Trees;
        Assert.NotEmpty(first);

        // Clicking again replaces them with a new random scattering.
        Click(marina, new Vector2(-40, 0));
        var second = marina.GetLandArea("lawn")!.Trees;
        Assert.NotEmpty(second);
        Assert.NotEqual(first[0].Position, second[0].Position);
        Assert.Equal(first.Count, planted[^1].PreviousCount);

        // A quay is not planted at all, from the tool or from code.
        Click(marina, new Vector2(40, 0));
        Assert.Empty(marina.GetLandArea("quay")!.Trees);
        Assert.Null(designer.PlantTrees("quay"));

        Click(marina, new Vector2(-40, 0), modifiers: InputModifiers.Control);
        Assert.Empty(marina.GetLandArea("lawn")!.Trees);
        Assert.Null(designer.RemoveTrees("lawn")); // nothing left to remove
    }

    [Fact]
    public void AddLandBerths_TwoClicksPlaceABerthOnLandAndAimItsBow()
    {
        var marina = CreateDesigner(DesignTool.AddLandBerths);
        var designer = marina.Designer;
        marina.AddLandArea(new LandArea("yard", Square(40f), 1.5f));
        designer.BerthWidth = 4f;
        designer.BerthLength = 11f;
        var created = new List<DesignElementCreatedEventArgs>();
        designer.ElementCreated += (_, e) => created.Add(e);

        Click(marina, new Vector2(-10, 5));
        Assert.True(designer.HasDraft);
        Click(marina, new Vector2(-10, -20)); // the bow points north (−Z)

        var berth = Assert.Single(marina.GetBerthsByLandArea("yard"));
        Assert.Equal("yard-01", berth.Id);
        Assert.True(berth.IsOnLand);
        AssertNear(new Vector2(-10, 5), berth.Center, 0.2f);
        Assert.Equal(180f, MathF.Abs(berth.HeadingDegrees), 1);
        Assert.Equal(4f, berth.Width);
        Assert.Equal(11f, berth.Length);
        Assert.Same(berth, Assert.Single(created[^1].Berths));

        // Clicking the same spot twice uses the heading from the settings.
        designer.LandBerthHeading = 90f;
        Click(marina, new Vector2(10, 5));
        Click(marina, new Vector2(10, 5));
        var second = marina.GetBerth("yard-02")!;
        Assert.Equal(90f, second.HeadingDegrees, 1);

        Assert.True(designer.Undo());
        Assert.Null(marina.GetBerth("yard-02"));
    }

    /// <summary>An axis-aligned square of the given half-size, in plan coordinates.</summary>
    private static Vector2[] Square(float halfSize, Vector2 center = default) => new[]
    {
        center + new Vector2(-halfSize, -halfSize),
        center + new Vector2(halfSize, -halfSize),
        center + new Vector2(halfSize, halfSize),
        center + new Vector2(-halfSize, halfSize),
    };

    [Fact]
    public void Erase_RemovesTheBerthPierOrLandAreaUnderThePointer()
    {
        var marina = CreateDesigner(DesignTool.Erase);
        marina.InitializeLayout(new MarinaLayoutBuilder()
            .AddLandArea(new LandArea("quay", new[] { new Vector2(-60, -60), new Vector2(60, -60), new Vector2(60, -40), new Vector2(-60, -40) }, 1f))
            .AddPier("A", "A", new Vector2(0, -40), 0f, 40f, d => d.AddBerths(PierSide.Right, 4, 5f, 10f), width: 3f)
            .Build());
        marina.Camera.SetPose(new CameraPose(new Vector3(0, 0, -20), 0f, 89f, 150f), immediate: true);
        var erased = new List<DesignElementErasedEventArgs>();
        marina.Designer.ElementErased += (_, e) => erased.Add(e);

        Click(marina, marina.GetBerth("A-R02")!.Center);
        Assert.Null(marina.GetBerth("A-R02"));
        Assert.IsType<Berth>(erased[^1].Element);

        Click(marina, new Vector2(0, -20)); // on the pier deck
        Assert.Empty(marina.GetPiers());
        Assert.Equal(3, erased[^1].RemovedBerths.Count);

        Click(marina, new Vector2(20, -50));
        Assert.Empty(marina.GetLandAreas());
        Assert.IsType<LandArea>(erased[^1].Element);
    }

    [Fact]
    public void DesignMode_ClicksDoNotSelectBerths()
    {
        var marina = CreateDesigner(DesignTool.Navigate);
        marina.InitializeLayout(new MarinaLayoutBuilder().AddPier("A", "A", Vector2.Zero, 0f, 30f, d => d.AddBerths(PierSide.Left, 2, 5f, 10f)).Build());
        marina.Camera.SetPose(new CameraPose(new Vector3(0, 0, 15), 0f, 89f, 100f), immediate: true);
        var berth = marina.GetBerth("A-L01")!;

        Click(marina, berth.Center);
        Assert.Empty(marina.SelectedBerths);

        marina.Designer.IsActive = false;
        Click(marina, berth.Center);
        Assert.Equal("A-L01", marina.SelectedBerth?.Id);

        marina.Designer.IsActive = true;
        Assert.Empty(marina.SelectedBerths);
    }

    [Fact]
    public void ReferenceImage_IsPlacedNorthUp_AndCalibratedFromAScaleLine()
    {
        var marina = CreateDesigner(DesignTool.MeasureScale);
        var designer = marina.Designer;
        var image = new ReferenceImage(200, 100, new byte[200 * 100 * 4]);
        designer.SetReferenceImage(image, metersPerPixel: 0.5f, center: new Vector2(10, 0));
        Assert.Equal(new Vector2(100, 50), designer.ReferenceImageSize);

        var layer = marina.BuildRenderFrame().ReferenceImage!;
        Assert.Same(image, layer.Image);
        Assert.Equal(new Vector2(-40, -25), layer.Min); // top-left pixel: west and north (−Z)
        Assert.Equal(new Vector2(60, 25), layer.Max);
        Assert.True(layer.AboveScene);

        // The user draws over a scale bar that is 20 m at the current scale but represents 50 m.
        ScaleLineDrawnEventArgs? drawn = null;
        designer.ScaleLineDrawn += (_, e) =>
        {
            drawn = e;
            e.KnownLengthMeters = 50f;
        };
        Click(marina, new Vector2(0, 10));
        Click(marina, new Vector2(20, 10));

        Assert.Equal(20f, drawn!.MeasuredLength, 1);
        Assert.Equal(1.25f, designer.ReferenceImageMetersPerPixel, 2);
        AssertNear(new Vector2(25, -15), designer.ReferenceImageCenter, 0.2f); // (10, 0) scaled 2.5× about the line's start (0, 10)
        AssertNear(new Vector2(50, 10), designer.ScaleLine!.Value.End, 0.2f);

        // Calibrating again from code, and moving by dragging with the move tool.
        Assert.True(designer.CalibrateReferenceImage(100f));
        Assert.Equal(2.5f, designer.ReferenceImageMetersPerPixel, 2);

        designer.Tool = DesignTool.MoveReferenceImage;
        var before = designer.ReferenceImageCenter;
        var from = Screen(marina, new Vector2(0, 0));
        var to = Screen(marina, new Vector2(15, -5));
        marina.Input.PointerDown(from.X, from.Y, PointerButton.Left);
        marina.Input.PointerMove((from.X + to.X) / 2, (from.Y + to.Y) / 2);
        marina.Input.PointerMove(to.X, to.Y);
        marina.Input.PointerUp(to.X, to.Y, PointerButton.Left);
        AssertNear(before + new Vector2(15, -5), designer.ReferenceImageCenter, 0.2f);
        Assert.Equal(new Vector3(0, 0, 0), marina.Camera.DesiredPose.Target); // the camera didn't pan

        designer.ClearReferenceImage();
        Assert.Null(marina.BuildRenderFrame().ReferenceImage);
    }

    [Fact]
    public void WaterGrid_GrowsAndMovesToCoverAFarAwayReferenceImage()
    {
        var marina = CreateDesigner(DesignTool.Navigate);
        var before = marina.Meshes.Get(MeshIds.Water).Bounds;

        marina.Designer.SetReferenceImage(new ReferenceImage(1000, 1000, new byte[1000 * 1000 * 4]), metersPerPixel: 2f, center: new Vector2(1500, -800));

        var water = marina.Meshes.Get(MeshIds.Water).Bounds;
        Assert.NotEqual(before, water);
        Assert.True(water.Min.X <= 500 && water.Max.X >= 2500 && water.Min.Z <= -1800 && water.Max.Z >= 200, $"water {water.Min}..{water.Max}");
        Assert.True(marina.Camera.Constraints.TargetBoundsMax.X >= 2500);
    }

    [Fact]
    public void Overlay_ShowsDraftGeometryWhileDrawing()
    {
        var marina = CreateDesigner(DesignTool.DrawLandArea);
        var empty = marina.BuildRenderFrame().Objects.Count;

        Click(marina, new Vector2(0, 0));
        Click(marina, new Vector2(20, 0));
        var s = Screen(marina, new Vector2(20, 20));
        marina.Input.PointerMove(s.X, s.Y);

        var objects = marina.BuildRenderFrame().Objects;
        Assert.True(objects.Count > empty + 4);
        Assert.All(objects.Skip(empty), o => Assert.True(o.IsTransparent)); // drawn over the reference image
    }

    [Fact]
    public void LandAreas_CanBeAddedUpdatedAndRemovedAtRuntime_KeepingOtherMeshes()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());
        var breakwater = marina.Meshes.All.Single(m => m.Name == "Breakwater:breakwater-north");
        var changes = new List<LayoutChangedEventArgs>();
        marina.LayoutChanged += (_, e) => changes.Add(e);

        var square = new[] { new Vector2(200, 200), new Vector2(220, 200), new Vector2(220, 220), new Vector2(200, 220) };
        marina.AddLandArea(new LandArea("pad", square, 1f));
        marina.UpdateLandArea(marina.GetLandArea("pad")! with { Height = 2f, Kind = LandKind.Grass });
        Assert.Equal(2f, marina.GetLandArea("PAD")!.Height);
        Assert.Throws<InvalidOperationException>(() => marina.RemoveLandArea(MockMarinaFactory.BoatyardId, removeBerths: false));
        Assert.True(marina.RemoveLandArea(MockMarinaFactory.BoatyardId));
        Assert.Empty(marina.GetBerthsByLandArea(MockMarinaFactory.BoatyardId));
        Assert.True(marina.RemoveLandArea("pad"));

        Assert.Equal(
            new[] { LayoutChangeKind.LandAreaAdded, LayoutChangeKind.LandAreaUpdated, LayoutChangeKind.BatchUpdated, LayoutChangeKind.LandAreaRemoved },
            changes.Select(c => c.Kind));
        Assert.Equal("pad", changes[0].LandAreaId);
        Assert.Same(breakwater, marina.Meshes.All.Single(m => m.Name == "Breakwater:breakwater-north"));
        Assert.DoesNotContain(marina.Meshes.All, m => m.Name is "Land:pad" or "Land:boatyard");
        Assert.All(marina.BuildRenderFrame().Objects.Where(o => o.MeshId >= MeshIds.LandBase), o => Assert.True(marina.Meshes.TryGet(o.MeshId, out _)));
    }

    [Fact]
    public void ExportObjects_ReturnsEveryElement_AndRoundTripsThroughFromObjects()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());
        marina.SetMarineTraffic(MarineTraffic.None with { IsEnabled = true, Clearance = 400f, LaneCount = 3 });

        var objects = marina.ExportObjects();
        var layout = marina.GetLayout();
        Assert.NotNull(layout.Shoreline);
        Assert.NotNull(layout.MarineTraffic);
        Assert.Equal(2 + layout.LandAreas.Count + layout.Piers.Count + layout.Dividers.Count + layout.Berths.Count + layout.MultiBerths.Count, objects.Length);

        // The mainland goes first: everything else is drawn on top of it. The passing traffic comes with it.
        Assert.IsType<Shoreline>(objects[0]);
        Assert.IsType<MarineTraffic>(objects[1]);
        Assert.IsType<LandArea>(objects[2]);
        Assert.IsType<MultiBerth>(objects[^1]);

        // Everything but the name is in the array, so the same array and the name give the same layout back.
        Assert.Equal(layout, MarinaLayout.FromObjects(objects, layout.Name));

        var rebuilt = MarinaLayout.FromObjects(objects.Reverse(), layout.Name);
        Assert.Empty(rebuilt.Validate());
        Assert.Equal(layout.Shoreline, rebuilt.Shoreline);
        Assert.Equal(layout.MarineTraffic, rebuilt.MarineTraffic);
        Assert.Equal(layout.Berths.Select(s => s.Id).OrderBy(x => x), rebuilt.Berths.Select(s => s.Id).OrderBy(x => x));

        var copy = new MarinaVisualizer();
        copy.InitializeLayout(rebuilt);
        Assert.Equal(layout.MarineTraffic, copy.GetLayout().MarineTraffic);

        var refused = Assert.Throws<ArgumentException>(() => MarinaLayout.FromObjects(new object[] { "not an element" }));
        Assert.StartsWith(Strings.Format(Strings.ErrorUnsupportedMarinaElement, nameof(String)), refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportObjects_WithoutACoast_StillCarriesTheTrafficSettings_EvenSwitchedOff()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));

        var objects = marina.ExportObjects();

        Assert.Equal(2, objects.Length);
        Assert.Same(MarineTraffic.None, objects[0]);
        Assert.IsType<Pier>(objects[1]);
        Assert.Empty(MarinaLayout.Empty.ToObjects()); // a layout without traffic settings has none to give
        Assert.Equal(marina.GetLayout(), MarinaLayout.FromObjects(objects, marina.MarinaName));
    }

    [Fact]
    public void SelectArea_DraggingABox_SelectsTheBerthsInside()
    {
        var marina = CreateDesigner(DesignTool.SelectArea);
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        var designer = marina.Designer;
        var berths = designer.CreateBerths("A", PierSide.Left, 0f, 40f);
        Assert.True(berths.Count >= 4);

        // A box around the first two berths takes those two and nothing else.
        var first = berths[0].Center;
        var second = berths[1].Center;
        var selected = designer.SelectBerthsInArea(first - new Vector2(3, 3), second + new Vector2(3, 3));

        Assert.Equal(new[] { berths[0].Id, berths[1].Id }.OrderBy(id => id), selected.OrderBy(id => id));
        Assert.True(marina.IsBerthSelected(berths[0].Id));
        Assert.False(marina.IsBerthSelected(berths[3].Id));

        // Adding keeps what was already selected.
        var added = designer.SelectBerthsInArea(berths[3].Center - new Vector2(2, 2), berths[3].Center + new Vector2(2, 2), add: true);
        Assert.Contains(berths[0].Id, added);
        Assert.Contains(berths[3].Id, added);

        // Without adding, the earlier ones drop away.
        var replaced = designer.SelectBerthsInArea(berths[3].Center - new Vector2(2, 2), berths[3].Center + new Vector2(2, 2));
        Assert.Equal(new[] { berths[3].Id }, replaced);
    }

    [Fact]
    public void SelectArea_DraggingInTheView_ShowsTheBoxAndSelects()
    {
        var marina = CreateDesigner(DesignTool.SelectArea);
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        var designer = marina.Designer;
        var berths = designer.CreateBerths("A", PierSide.Left, 0f, 20f);

        var from = Screen(marina, berths[0].Center - new Vector2(4, 4));
        var to = Screen(marina, berths[0].Center + new Vector2(4, 4));

        marina.Input.PointerDown(from.X, from.Y, PointerButton.Left);
        marina.Input.PointerMove(to.X, to.Y);
        Assert.NotNull(designer.SelectionBox); // drawn while the drag is live

        marina.Input.PointerUp(to.X, to.Y, PointerButton.Left);
        Assert.Null(designer.SelectionBox);
        Assert.True(marina.IsBerthSelected(berths[0].Id));
    }
}
