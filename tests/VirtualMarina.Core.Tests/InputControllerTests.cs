using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// The layer a host forwards its mouse and keyboard to. Everything here is behaviour a user would notice: whether
/// a drag turned the camera or slid it, whether a small movement still counted as a click, and whether letting go
/// outside the view leaves anything half-done.
/// </summary>
public class InputControllerTests
{
    private static MarinaVisualizer WithARow(int count = 4)
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(1000, 800);
        marina.Camera.SetPose(new CameraPose(Vector3.Zero, 0f, 45f, 120f), immediate: true);
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        var pier = marina.GetPier("A")!;
        for (var i = 0; i < count; i++)
        {
            marina.AddBerth(BerthGenerator.AtPier(pier, $"A-L{i + 1:00}", PierSide.Left, i * 6f, 5f, 12f));
        }

        return marina;
    }

    // ---- What a drag does --------------------------------------------------------------------

    [Fact]
    public void ALeftDragPansByDefault_AndARightDragOrbits()
    {
        var marina = WithARow();
        var before = marina.Camera.DesiredPose;

        marina.Input.PointerDown(500, 400, PointerButton.Left);
        marina.Input.PointerMove(560, 460);
        marina.Input.PointerUp(560, 460, PointerButton.Left);

        var afterPan = marina.Camera.DesiredPose;
        Assert.NotEqual(before.Target, afterPan.Target);
        Assert.Equal(before.YawDegrees, afterPan.YawDegrees, 3);

        marina.Input.PointerDown(500, 400, PointerButton.Right);
        marina.Input.PointerMove(560, 400);
        marina.Input.PointerUp(560, 400, PointerButton.Right);

        Assert.NotEqual(afterPan.YawDegrees, marina.Camera.DesiredPose.YawDegrees);
    }

    [Fact]
    public void TheDragActionsCanBeSwapped()
    {
        var marina = WithARow();
        marina.Input.LeftDragAction = CameraDragAction.Orbit;
        var before = marina.Camera.DesiredPose;

        marina.Input.PointerDown(500, 400, PointerButton.Left);
        marina.Input.PointerMove(560, 400);
        marina.Input.PointerUp(560, 400, PointerButton.Left);

        Assert.NotEqual(before.YawDegrees, marina.Camera.DesiredPose.YawDegrees);
    }

    [Fact]
    public void ADragActionOfNone_LeavesTheCameraAlone()
    {
        var marina = WithARow();
        marina.Input.LeftDragAction = CameraDragAction.None;
        var before = marina.Camera.DesiredPose;

        marina.Input.PointerDown(500, 400, PointerButton.Left);
        marina.Input.PointerMove(600, 500);
        marina.Input.PointerUp(600, 500, PointerButton.Left);

        Assert.Equal(before.Target, marina.Camera.DesiredPose.Target);
        Assert.Equal(before.YawDegrees, marina.Camera.DesiredPose.YawDegrees, 4);
    }

    [Fact]
    public void OrbitDegreesPerPixel_ScalesHowFarADragTurns()
    {
        var slow = WithARow();
        slow.Input.RightDragAction = CameraDragAction.Orbit;
        slow.Input.OrbitDegreesPerPixel = 0.1f;
        slow.Input.PointerDown(500, 400, PointerButton.Right);
        slow.Input.PointerMove(600, 400);
        var slowTurn = MathF.Abs(slow.Camera.DesiredPose.YawDegrees);

        var fast = WithARow();
        fast.Input.RightDragAction = CameraDragAction.Orbit;
        fast.Input.OrbitDegreesPerPixel = 0.5f;
        fast.Input.PointerDown(500, 400, PointerButton.Right);
        fast.Input.PointerMove(600, 400);
        var fastTurn = MathF.Abs(fast.Camera.DesiredPose.YawDegrees);

        Assert.True(fastTurn > slowTurn, $"0.5 deg/px should turn further than 0.1 ({fastTurn} vs {slowTurn})");
    }

    /// <summary>
    /// IsDragging means "held and moved past the click tolerance", not "held". A host drawing a drag affordance
    /// needs that distinction, or it flickers on every click.
    /// </summary>
    [Fact]
    public void IsDragging_TurnsOnOnlyOnceTheMovementPassesTheClickTolerance()
    {
        var marina = WithARow();
        Assert.False(marina.Input.IsDragging);

        marina.Input.PointerDown(500, 400, PointerButton.Left);
        Assert.False(marina.Input.IsDragging);          // held, but not yet a drag

        marina.Input.PointerMove(502, 402);
        Assert.False(marina.Input.IsDragging);          // still inside the tolerance

        marina.Input.PointerMove(560, 460);
        Assert.True(marina.Input.IsDragging);           // past it

        marina.Input.PointerUp(560, 460, PointerButton.Left);
        Assert.False(marina.Input.IsDragging);
    }

    // ---- Click versus drag -------------------------------------------------------------------

    [Fact]
    public void AMovementInsideTheTolerance_StillCountsAsAClick()
    {
        var marina = WithARow();
        marina.FocusBerth("A-L02", CameraAngle.TopDown, immediate: true);
        var (x, y) = ScreenPointOf(marina, "A-L02");

        marina.Input.PointerDown(x, y, PointerButton.Left);
        marina.Input.PointerMove(x + 2, y + 2);           // inside the 5px tolerance
        marina.Input.PointerUp(x + 2, y + 2, PointerButton.Left);

        Assert.Equal("A-L02", marina.SelectedBerth?.Id);
    }

    [Fact]
    public void AMovementBeyondTheTolerance_IsADragAndSelectsNothing()
    {
        var marina = WithARow();
        marina.FocusBerth("A-L02", CameraAngle.TopDown, immediate: true);
        var (x, y) = ScreenPointOf(marina, "A-L02");

        marina.Input.PointerDown(x, y, PointerButton.Left);
        marina.Input.PointerMove(x + 80, y + 80);
        marina.Input.PointerUp(x + 80, y + 80, PointerButton.Left);

        Assert.Null(marina.SelectedBerth);
    }

    [Fact]
    public void ClickTolerance_CanBeWidened()
    {
        var marina = WithARow();
        marina.FocusBerth("A-L02", CameraAngle.TopDown, immediate: true);
        marina.Input.ClickTolerancePixels = 60f;
        var (x, y) = ScreenPointOf(marina, "A-L02");

        marina.Input.PointerDown(x, y, PointerButton.Left);
        marina.Input.PointerMove(x + 20, y + 20);
        marina.Input.PointerUp(x + 20, y + 20, PointerButton.Left);

        Assert.Equal("A-L02", marina.SelectedBerth?.Id);
    }

    /// <summary>Where a berth currently sits on screen, so a test can click it rather than guess.</summary>
    private static (float X, float Y) ScreenPointOf(MarinaVisualizer marina, string berthId)
    {
        var berth = marina.GetBerth(berthId)!;
        var screen = marina.Camera.WorldToScreen(MarinaMath.ToWorld(berth.Center), 1000, 800);
        Assert.NotNull(screen);
        return (screen.Value.X, screen.Value.Y);
    }

    // ---- The wheel ---------------------------------------------------------------------------

    [Fact]
    public void TheWheelZoomsInOneDirectionAndOutInTheOther()
    {
        var marina = WithARow();
        var start = marina.Camera.DesiredPose.Distance;

        marina.Input.Wheel(1f, 500, 400);
        var closer = marina.Camera.DesiredPose.Distance;
        Assert.True(closer < start, "a positive notch should move in");

        marina.Input.Wheel(-1f, 500, 400);
        Assert.True(marina.Camera.DesiredPose.Distance > closer, "a negative notch should move out");
    }

    [Fact]
    public void ZoomStepFactor_ScalesHowFarOneNotchGoes()
    {
        var gentle = WithARow();
        gentle.Input.ZoomStepFactor = 1.05f;
        gentle.Input.Wheel(1f, 500, 400);

        var steep = WithARow();
        steep.Input.ZoomStepFactor = 1.5f;
        steep.Input.Wheel(1f, 500, 400);

        Assert.True(steep.Camera.DesiredPose.Distance < gentle.Camera.DesiredPose.Distance);
    }

    [Fact]
    public void AnAbsurdWheelNotchIsClamped_RatherThanFlyingToTheLimit()
    {
        var marina = WithARow();

        marina.Input.Wheel(10_000f, 500, 400);

        Assert.True(float.IsFinite(marina.Camera.DesiredPose.Distance));
        Assert.True(marina.Camera.DesiredPose.Distance >= marina.Camera.Constraints.MinDistance);
    }

    // ---- Leaving the view --------------------------------------------------------------------

    [Fact]
    public void PointerLeave_EndsTheDragAndClearsTheHover()
    {
        var marina = WithARow();
        marina.Input.PointerDown(500, 400, PointerButton.Left);
        marina.Input.PointerMove(580, 480);             // past the tolerance, so a real drag
        Assert.True(marina.Input.IsDragging);

        marina.Input.PointerLeave();

        Assert.False(marina.Input.IsDragging);
        Assert.Null(marina.HoveredBerth);
    }

    [Fact]
    public void AMoveAfterLeaving_DoesNotKeepPanning()
    {
        var marina = WithARow();
        marina.Input.PointerDown(500, 400, PointerButton.Left);
        marina.Input.PointerMove(580, 480);
        marina.Input.PointerLeave();
        var afterLeave = marina.Camera.DesiredPose.Target;

        marina.Input.PointerMove(900, 700);

        Assert.Equal(afterLeave, marina.Camera.DesiredPose.Target);
    }

    [Fact]
    public void HoverCanBeTurnedOff()
    {
        var marina = WithARow();
        marina.FocusBerth("A-L02", CameraAngle.TopDown, immediate: true);
        var (x, y) = ScreenPointOf(marina, "A-L02");

        marina.Input.HoverEnabled = false;
        marina.Input.PointerMove(x, y);

        Assert.Null(marina.HoveredBerth);
    }

    // ---- Keyboard ----------------------------------------------------------------------------

    [Theory]
    [InlineData(MarinaKey.Left)]
    [InlineData(MarinaKey.Right)]
    [InlineData(MarinaKey.Up)]
    [InlineData(MarinaKey.Down)]
    public void TheArrowKeysMoveTheCamera(MarinaKey key)
    {
        var marina = WithARow();
        var before = marina.Camera.DesiredPose;

        var handled = marina.Input.KeyDown(key);

        Assert.True(handled, $"{key} should be handled");
        Assert.NotEqual(before.Target, marina.Camera.DesiredPose.Target);
    }

    [Fact]
    public void KeyboardPanMeters_ScalesHowFarAnArrowMoves()
    {
        var small = WithARow();
        small.Input.KeyboardPanMeters = 1f;
        small.Input.KeyDown(MarinaKey.Left);
        var smallMove = Vector3.Distance(small.Camera.DesiredPose.Target, Vector3.Zero);

        var big = WithARow();
        big.Input.KeyboardPanMeters = 50f;
        big.Input.KeyDown(MarinaKey.Left);
        var bigMove = Vector3.Distance(big.Camera.DesiredPose.Target, Vector3.Zero);

        Assert.True(bigMove > smallMove, $"50 m should move further than 1 m ({bigMove} vs {smallMove})");
    }

    [Fact]
    public void AKeyNobodyBoundIsNotHandled()
    {
        var marina = WithARow();

        Assert.False(marina.Input.KeyDown((MarinaKey)999));
    }

    [Fact]
    public void ModifiersChanged_IsSafeWhenTheDesignerIsOff()
    {
        var marina = WithARow();

        var exception = Record.Exception(() => marina.Input.ModifiersChanged(InputModifiers.Alt));

        Assert.Null(exception);
    }
}
