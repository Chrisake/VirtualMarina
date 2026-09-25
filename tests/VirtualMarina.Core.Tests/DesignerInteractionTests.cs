using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// The keys and drags of the designer: Escape, Ctrl+Z while drawing, Delete with the area tool, drags that can be abandoned,
/// the pointer picked on the plane a drawing lies on, and one notification per operation.
/// </summary>
public class DesignerInteractionTests
{
    private static MarinaVisualizer CreateDesigner(DesignTool tool, float pitch = 89f)
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(1000, 800);
        marina.Camera.SetPose(new CameraPose(Vector3.Zero, 0f, pitch, 150f), immediate: true);
        marina.Designer.IsActive = true;
        marina.Designer.Tool = tool;
        return marina;
    }

    private static Vector2 Screen(MarinaVisualizer marina, Vector2 plan, float height = 0f)
    {
        Assert.True(marina.TryProjectToScreen(MarinaMath.ToWorld(plan, height), out var screen));
        return screen;
    }

    private static void Click(MarinaVisualizer marina, Vector2 plan, float height = 0f)
    {
        var s = Screen(marina, plan, height);
        marina.Input.PointerMove(s.X, s.Y);
        marina.Input.PointerDown(s.X, s.Y, PointerButton.Left);
        marina.Input.PointerUp(s.X, s.Y, PointerButton.Left);
    }

    private static MarinaVisualizer WithRow(out MarinaDesigner designer)
    {
        var marina = CreateDesigner(DesignTool.SelectArea);
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        designer = marina.Designer;
        designer.DividerType = DividerType.Piles;
        designer.CreateBerths("A", PierSide.Left, 0f, 30f);
        designer.PlaceDividers("A", PierSide.Left, 0f, wholeRow: true);
        return marina;
    }

    private static void AssertNear(Vector2 expected, Vector2? actual, float tolerance = 0.05f)
    {
        Assert.NotNull(actual);
        Assert.True(Vector2.Distance(expected, actual.Value) <= tolerance, $"expected {expected}, got {actual}");
    }

    // ---- Escape ---------------------------------------------------------------------------------

    [Fact]
    public void Escape_WithNothingToAbandon_PutsTheToolDown_UnlessTheHostKeepsEscapeForItself()
    {
        var marina = CreateDesigner(DesignTool.DrawPier);
        var designer = marina.Designer;
        Assert.True(designer.EscapeReturnsToNavigate);
        Assert.True(marina.Input.WantsKey(MarinaKey.Escape));
        Assert.True(marina.Input.KeyDown(MarinaKey.Escape));
        Assert.Equal(DesignTool.Navigate, designer.Tool);

        designer.Tool = DesignTool.DrawPier;
        designer.EscapeReturnsToNavigate = false;
        Assert.False(marina.Input.WantsKey(MarinaKey.Escape));
        Assert.False(marina.Input.KeyDown(MarinaKey.Escape));
        Assert.Equal(DesignTool.DrawPier, designer.Tool);

        // A drawing is still abandoned first, whatever the setting.
        Click(marina, new Vector2(0, 0), Pier.GetDefaultDeckHeight(designer.PierType));
        Assert.True(designer.HasDraft);
        Assert.True(marina.Input.WantsKey(MarinaKey.Escape));
        Assert.True(marina.Input.KeyDown(MarinaKey.Escape));
        Assert.False(designer.HasDraft);
        Assert.Equal(DesignTool.DrawPier, designer.Tool);
    }

    [Fact]
    public void Escape_AbandonsASelectionDrag_SoTheReleaseSelectsNothing()
    {
        var marina = WithRow(out var designer);
        var from = Screen(marina, new Vector2(-40, -40));
        var to = Screen(marina, new Vector2(40, 40));
        marina.Input.PointerDown(from.X, from.Y, PointerButton.Left);
        marina.Input.PointerMove(to.X, to.Y);
        Assert.NotNull(designer.SelectionQuad);
        Assert.True(marina.Input.WantsKey(MarinaKey.Escape));

        Assert.True(marina.Input.KeyDown(MarinaKey.Escape));
        Assert.Null(designer.SelectionQuad);
        Assert.Equal(DesignTool.SelectArea, designer.Tool);

        marina.Input.PointerUp(to.X, to.Y, PointerButton.Left);
        Assert.Empty(marina.SelectedBerths);
    }

    [Fact]
    public void ChangingTheTool_OrSwitchingOff_MidDrag_ForgetsTheBox()
    {
        var marina = WithRow(out var designer);
        var from = Screen(marina, new Vector2(-40, -40));
        var to = Screen(marina, new Vector2(40, 40));
        marina.Input.PointerDown(from.X, from.Y, PointerButton.Left);
        marina.Input.PointerMove(to.X, to.Y);

        designer.Tool = DesignTool.Erase;
        designer.Tool = DesignTool.SelectArea;
        Assert.Null(designer.SelectionQuad);
        marina.Input.PointerUp(to.X, to.Y, PointerButton.Left);
        Assert.Empty(marina.SelectedBerths);

        marina.Input.PointerDown(from.X, from.Y, PointerButton.Left);
        marina.Input.PointerMove(to.X, to.Y);
        designer.IsActive = false;
        designer.IsActive = true;
        Assert.Null(designer.SelectionQuad);
    }

    [Fact]
    public void Escape_AbandonsAnImageDrag_PuttingThePictureBack_WithoutAnnouncingAMove()
    {
        var marina = CreateDesigner(DesignTool.MoveReferenceImage);
        var designer = marina.Designer;
        designer.SetReferenceImage(new ReferenceImage(4, 4, new byte[64]), 10f, Vector2.Zero);
        var changes = new List<ReferenceImageChange>();
        designer.ReferenceImageChanged += (_, e) => changes.Add(e.Change);

        var from = Screen(marina, Vector2.Zero);
        var to = Screen(marina, new Vector2(15, 5));
        marina.Input.PointerDown(from.X, from.Y, PointerButton.Left);
        marina.Input.PointerMove(to.X, to.Y);
        Assert.NotEqual(Vector2.Zero, designer.ReferenceImageCenter);

        Assert.True(marina.Input.KeyDown(MarinaKey.Escape));
        Assert.Equal(Vector2.Zero, designer.ReferenceImageCenter);
        marina.Input.PointerUp(to.X, to.Y, PointerButton.Left);
        Assert.Equal(Vector2.Zero, designer.ReferenceImageCenter);
        Assert.Empty(changes);
    }

    [Fact]
    public void DraggingTheImage_MovesItAsItGoes_AndAnnouncesTheMoveOnce()
    {
        var marina = CreateDesigner(DesignTool.MoveReferenceImage);
        var designer = marina.Designer;
        designer.SetReferenceImage(new ReferenceImage(4, 4, new byte[64]), 10f, Vector2.Zero);
        var changes = new List<ReferenceImageChange>();
        var states = 0;
        designer.ReferenceImageChanged += (_, e) => changes.Add(e.Change);
        designer.StateChanged += (_, _) => states++;

        var from = Screen(marina, Vector2.Zero);
        marina.Input.PointerDown(from.X, from.Y, PointerButton.Left);
        Vector2? last = null;
        for (var step = 1; step <= 5; step++)
        {
            var at = Screen(marina, new Vector2(step * 3f, 0f));
            marina.Input.PointerMove(at.X, at.Y);
            if (last is { } previous) Assert.True(designer.ReferenceImageCenter.X > previous.X, "the picture did not follow the pointer");
            last = designer.ReferenceImageCenter;
        }

        Assert.Empty(changes);
        var end = Screen(marina, new Vector2(15f, 0f));
        marina.Input.PointerUp(end.X, end.Y, PointerButton.Left);

        Assert.Equal(ReferenceImageChange.Moved, Assert.Single(changes));
        Assert.Equal(1, states);
        AssertNear(new Vector2(15f, 0f), designer.ReferenceImageCenter, 0.1f);
    }

    // ---- Ctrl+Z and Delete ----------------------------------------------------------------------

    [Fact]
    public void CtrlZ_WhileDrawing_TakesBackTheLastPoint_NotTheLastElement()
    {
        var marina = CreateDesigner(DesignTool.DrawLandArea);
        var designer = marina.Designer;
        designer.CreatePier(new Vector2(80, 0), new Vector2(80, 30));
        var height = designer.LandHeight;
        Click(marina, new Vector2(0, 0), height);
        Click(marina, new Vector2(20, 0), height);

        Assert.True(marina.Input.WantsKey(MarinaKey.Undo, InputModifiers.Control));
        Assert.True(marina.Input.KeyDown(MarinaKey.Undo, InputModifiers.Control));
        Assert.Single(designer.DraftPoints);
        Assert.Single(marina.GetPiers());

        Assert.True(marina.Input.KeyDown(MarinaKey.Undo, InputModifiers.Control));
        Assert.False(designer.HasDraft);
        Assert.True(marina.Input.KeyDown(MarinaKey.Undo, InputModifiers.Control));
        Assert.Empty(marina.GetPiers());
    }

    [Fact]
    public void TryUndo_WhileDrawing_TakesBackTheLastPoint_LikeCtrlZ()
    {
        var marina = CreateDesigner(DesignTool.DrawLandArea);
        var designer = marina.Designer;
        designer.CreatePier(new Vector2(80, 0), new Vector2(80, 30));
        var height = designer.LandHeight;
        Click(marina, new Vector2(0, 0), height);
        Click(marina, new Vector2(20, 0), height);

        Assert.True(designer.TryUndo());
        Assert.Single(designer.DraftPoints);
        Assert.Single(marina.GetPiers());

        Assert.True(designer.TryUndo());
        Assert.False(designer.HasDraft);
        Assert.True(designer.TryUndo());
        Assert.Empty(marina.GetPiers());
        Assert.False(designer.TryUndo());

        Assert.True(designer.TryRedo());
        Assert.Single(marina.GetPiers());
        Assert.False(designer.TryRedo());
    }

    [Fact]
    public void Delete_WithTheAreaTool_ErasesTheSelectedBerths_AsOneStep()
    {
        var marina = WithRow(out var designer);
        var erased = new List<DesignElementErasedEventArgs>();
        designer.ElementErased += (_, e) => erased.Add(e);
        Assert.False(marina.Input.WantsKey(MarinaKey.Delete));

        var first = marina.GetBerth("A-L01")!.Center;
        var second = marina.GetBerth("A-L02")!.Center;
        designer.SelectBerthsInArea(Vector2.Min(first, second) - Vector2.One, Vector2.Max(first, second) + Vector2.One);
        var selected = marina.SelectedBerths.Select(berth => berth.Id).ToArray();
        Assert.Equal(new[] { "A-L01", "A-L02" }, selected);
        var steps = designer.UndoCount;
        var dividers = marina.GetDividers().Count;

        Assert.True(marina.Input.WantsKey(MarinaKey.Delete));
        Assert.True(marina.Input.KeyDown(MarinaKey.Delete));

        Assert.All(selected, id => Assert.Null(marina.GetBerth(id)));
        Assert.Equal(steps + 1, designer.UndoCount);
        Assert.Equal("Erase 2 berths", designer.UndoDescription);
        Assert.Equal(selected, erased.Select(e => ((Berth)e.Element).Id));
        Assert.Equal(dividers - marina.GetDividers().Count, erased.Sum(e => e.RemovedDividers.Count));

        Assert.True(designer.Undo());
        Assert.All(selected, id => Assert.NotNull(marina.GetBerth(id)));
        Assert.Equal(dividers, marina.GetDividers().Count);
    }

    // ---- The plane the pointer is picked on -----------------------------------------------------

    [Fact]
    public void ALandOutline_IsPickedAtTheLandsHeight_SoItsCornersStayUnderThePointer()
    {
        // Seen at an angle a high quay is far from where the same click meets the water.
        var marina = CreateDesigner(DesignTool.DrawLandArea, pitch: 45f);
        var designer = marina.Designer;
        designer.LandHeight = 10f;
        designer.SnapDistancePixels = 0f;
        var corners = new[] { new Vector2(-20, -10), new Vector2(20, -10), new Vector2(20, 15), new Vector2(-20, 15) };
        foreach (var corner in corners) Click(marina, corner, 10f);
        for (var i = 0; i < corners.Length; i++) AssertNear(corners[i], designer.DraftPoints[i]);

        // Clicking the first corner where it is drawn closes the outline.
        Click(marina, corners[0], 10f);
        var land = Assert.Single(marina.GetLandAreas());
        Assert.Equal(4, land.Points.Count);
    }

    [Fact]
    public void APier_IsPickedAtItsDeck_AndSnapsToACornerWhereTheCornerIsDrawn()
    {
        var marina = CreateDesigner(DesignTool.DrawPier, pitch: 45f);
        var designer = marina.Designer;
        marina.AddLandArea(new LandArea("quay", new[] { new Vector2(-40, -40), new Vector2(30, -40), new Vector2(30, -20), new Vector2(-40, -20) }, 8f));

        // Near the corner as it shows on screen, at the top of an 8 m quay: it snaps there exactly.
        var near = Screen(marina, new Vector2(30.4f, -20.3f), 8f);
        marina.Input.PointerMove(near.X, near.Y);
        Assert.Equal(new Vector2(30, -20), designer.PointerPosition);

        // Where the same corner would be on the water is well away from it on screen, so nothing snaps there.
        var water = Screen(marina, new Vector2(30f, -20f), 0f);
        marina.Input.PointerMove(water.X, water.Y);
        Assert.NotEqual(new Vector2(30, -20), designer.PointerPosition);
    }

    [Fact]
    public void Points_SnapToTheCoastAndToBerthCorners()
    {
        var marina = CreateDesigner(DesignTool.DrawLandArea);
        var designer = marina.Designer;
        marina.SetShoreline(new Shoreline(new[] { new Vector2(-100, -60), new Vector2(0, -50), new Vector2(100, -60) }, landOnLeft: true) { Height = 1f });
        marina.AddPier(new Pier("A", "Pier A", new Vector2(40, 0), 0f, 40f));
        designer.CreateBerths("A", PierSide.Left, 0f, 5f);
        var berth = Assert.Single(marina.GetBerths());

        var coast = Screen(marina, new Vector2(0.3f, -50.2f), designer.LandHeight);
        marina.Input.PointerMove(coast.X, coast.Y);
        Assert.Equal(new Vector2(0, -50), designer.PointerPosition);

        var corner = berth.Center + berth.Right * (berth.Width * 0.5f) + berth.Forward * (berth.Length * 0.5f);
        var nearCorner = Screen(marina, corner + new Vector2(0.2f, 0.2f), designer.LandHeight);
        marina.Input.PointerMove(nearCorner.X, nearCorner.Y);
        AssertNear(corner, designer.PointerPosition, 1e-3f);
    }

    // ---- Drawing state is not copied on every read ----------------------------------------------

    [Fact]
    public void DraftPoints_AndTheCoastWaitingForItsSide_AreTheSameListUntilTheyChange()
    {
        var marina = CreateDesigner(DesignTool.DrawShoreline);
        var designer = marina.Designer;
        Click(marina, new Vector2(-50, -20), designer.LandHeight);
        Click(marina, new Vector2(50, -20), designer.LandHeight);
        Assert.Same(designer.DraftPoints, designer.DraftPoints);
        var two = designer.DraftPoints;
        Click(marina, new Vector2(60, -30), designer.LandHeight);
        Assert.NotSame(two, designer.DraftPoints);

        Assert.True(marina.Input.KeyDown(MarinaKey.Enter));
        Assert.Same(designer.ShorelineAwaitingSide, designer.ShorelineAwaitingSide);
        Assert.True(designer.HasDraft);
        Assert.True(marina.Input.WantsKey(MarinaKey.Backspace));
    }

    // ---- One notification per operation ---------------------------------------------------------

    [Fact]
    public void ApplyingSettings_AnnouncesTheChangeOnce()
    {
        var designer = new MarinaVisualizer().Designer;
        var states = 0;
        designer.StateChanged += (_, _) => states++;

        new DesignerSettings { BerthWidth = 9f, BerthLength = 28f, LandHeight = 3f, PierWidth = 4f, Scenery = HinterlandScenery.Town, FogFactor = 0.5f }.ApplyTo(designer);

        Assert.Equal(1, states);
        Assert.Equal(HinterlandScenery.Town, designer.Scenery);
        Assert.Equal(0.5f, designer.FogFactor);
    }

    [Fact]
    public void DesignerSettings_CarryTheSceneryAndTheFog()
    {
        var designer = new MarinaVisualizer().Designer;
        designer.Scenery = HinterlandScenery.Town;
        designer.FogFactor = 0.4f;

        var settings = DesignerSettings.FromDesigner(designer);
        Assert.Equal(HinterlandScenery.Town, settings.Scenery);
        Assert.Equal(0.4f, settings.FogFactor);

        var other = new MarinaVisualizer().Designer;
        (settings with { FogFactor = float.NaN }).ApplyTo(other);
        Assert.Equal(HinterlandScenery.Town, other.Scenery);
        Assert.Equal(new DesignerSettings().FogFactor, other.FogFactor);
    }

    // ---- The published limits are the ones the designer keeps ----------------------------------------

    public static TheoryData<string> RefusingSettings =>
    [
        nameof(MarinaDesigner.LandHeight), nameof(MarinaDesigner.TreeDensity), nameof(MarinaDesigner.PierWidth),
        nameof(MarinaDesigner.BerthWidth), nameof(MarinaDesigner.BerthLength), nameof(MarinaDesigner.BerthDepth),
        nameof(MarinaDesigner.BerthGap), nameof(MarinaDesigner.SnapDistancePixels), nameof(MarinaDesigner.FogFactor),
        nameof(MarinaDesigner.ReferenceImageMetersPerPixel),
    ];

    private static DesignerSettingRange LimitOf(string setting) =>
        (DesignerSettingRange)typeof(DesignerLimits).GetProperty(setting)!.GetValue(null)!;

    [Theory]
    [MemberData(nameof(RefusingSettings))]
    public void DesignerLimits_AreWhatTheDesignerStartsAtTakesAndRefuses(string setting)
    {
        var designer = new MarinaVisualizer().Designer;
        var property = typeof(MarinaDesigner).GetProperty(setting)!;
        var range = LimitOf(setting);

        Assert.Equal(range.Default, (float)property.GetValue(designer)!);
        Assert.True(range.Contains(range.Default));
        foreach (var inside in new[] { range.Minimum, range.Maximum })
        {
            property.SetValue(designer, inside);
            Assert.Equal(inside, (float)property.GetValue(designer)!);
        }

        foreach (var outside in new[] { range.Minimum - 1f, range.Maximum + 1f, float.NaN })
        {
            Assert.False(range.Contains(outside));
            var thrown = Assert.Throws<System.Reflection.TargetInvocationException>(() => property.SetValue(designer, outside));
            Assert.IsType<ArgumentOutOfRangeException>(thrown.InnerException);
        }
    }

    [Fact]
    public void DesignerLimits_OfTheSettingsThatAreNotRefused_AreWhereTheyEndUp()
    {
        var designer = new MarinaVisualizer().Designer;
        Assert.Equal(DesignerLimits.ReferenceImageOpacity.Default, designer.ReferenceImageOpacity);
        Assert.Equal(DesignerLimits.LandBerthHeading.Default, designer.LandBerthHeading);

        designer.ReferenceImageOpacity = 5f;
        Assert.Equal(DesignerLimits.ReferenceImageOpacity.Maximum, designer.ReferenceImageOpacity);
        designer.LandBerthHeading = 350f;
        Assert.InRange(designer.LandBerthHeading, DesignerLimits.LandBerthHeading.Minimum, DesignerLimits.LandBerthHeading.Maximum);

        // What a file brings is clamped into the same ranges.
        new DesignerSettings { BerthLength = 1e6f, LandHeight = -4f }.ApplyTo(designer);
        Assert.Equal(DesignerLimits.BerthLength.Maximum, designer.BerthLength);
        Assert.Equal(DesignerLimits.LandHeight.Minimum, designer.LandHeight);
        Assert.Equal(DesignerLimits.BerthWidth.Default, DesignerLimits.BerthWidth.Clamp(float.NaN));
    }

    [Fact]
    public void DrawingAScaleLine_ThatCalibratesTheImage_AnnouncesTheChangeOnce()
    {
        var marina = CreateDesigner(DesignTool.MeasureScale);
        var designer = marina.Designer;
        designer.SetReferenceImage(new ReferenceImage(100, 100, new byte[40000]), 1f, Vector2.Zero);
        designer.ScaleLineDrawn += (_, e) => e.KnownLengthMeters = e.MeasuredLength * 2f;
        Click(marina, new Vector2(-10, 0));
        var states = 0;
        designer.StateChanged += (_, _) => states++;

        Click(marina, new Vector2(10, 0));

        Assert.Equal(1, states);
        Assert.Equal(2f, designer.ReferenceImageMetersPerPixel, 2);
    }
}
