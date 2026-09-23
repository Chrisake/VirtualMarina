using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// Which keys the view takes and which it leaves to the host. The host's own shortcuts come first: Ctrl+S has to
/// save, not pan the camera, and an Escape the view has no use for has to reach the dialog's Cancel button.
/// </summary>
public class KeyContractTests
{
    private const InputModifiers Ctrl = InputModifiers.Control;
    private const InputModifiers Shift = InputModifiers.Shift;
    private const InputModifiers Alt = InputModifiers.Alt;

    // Windows virtual-key codes, as WinForms' Keys holds them.
    private const int VkEscape = 0x1B;
    private const int VkLeft = 0x25;
    private const int VkS = 0x53;
    private const int VkY = 0x59;
    private const int VkZ = 0x5A;
    private const int VkF2 = 0x71;
    private const int VkOemPlus = 0xBB;

    private static MarinaVisualizer WithARow()
    {
        var marina = new MarinaVisualizer();
        marina.SetViewportSize(1000, 800);
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        var pier = marina.GetPier("A")!;
        for (var i = 0; i < 3; i++)
        {
            marina.AddBerth(BerthGenerator.AtPier(pier, $"A-L{i + 1:00}", PierSide.Left, i * 6f, 5f, 12f));
        }

        marina.Camera.SetPose(new CameraPose(Vector3.Zero, 0f, 45f, 120f), immediate: true);
        return marina;
    }

    // ---- The map ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("KeyW", "w", InputModifiers.None, MarinaKey.Up)]
    [InlineData("KeyA", "A", Shift, MarinaKey.Left)]
    [InlineData("KeyW", "z", InputModifiers.None, MarinaKey.Up)]         // AZERTY: the key where W is
    [InlineData("KeyW", "ц", InputModifiers.None, MarinaKey.Up)]         // Cyrillic
    [InlineData("KeyZ", "w", InputModifiers.None, null)]
    [InlineData("", "s", InputModifiers.None, MarinaKey.Down)]           // no physical key given: go by the letter
    [InlineData("Equal", "+", Shift, MarinaKey.ZoomIn)]
    [InlineData("BracketRight", "+", InputModifiers.None, MarinaKey.ZoomIn)] // German layout's own + key
    [InlineData("NumpadSubtract", "-", InputModifiers.None, MarinaKey.ZoomOut)]
    [InlineData("Escape", "Escape", Shift, MarinaKey.Escape)]
    [InlineData("KeyS", "s", Ctrl, null)]                                // Save
    [InlineData("KeyS", "S", Ctrl | Shift, null)]                        // Save As
    [InlineData("KeyN", "n", Alt, null)]
    [InlineData("KeyW", "w", Alt, null)]
    [InlineData("Equal", "=", Ctrl, null)]                               // the browser's zoom
    [InlineData("Minus", "-", Ctrl, null)]
    [InlineData("ArrowLeft", "ArrowLeft", Ctrl, null)]
    [InlineData("Escape", "Escape", Alt, null)]
    [InlineData("KeyZ", "z", Ctrl, MarinaKey.Undo)]
    [InlineData("KeyW", "z", Ctrl, MarinaKey.Undo)]                      // AZERTY Ctrl+Z follows the letter
    [InlineData("KeyZ", "w", Ctrl, null)]
    [InlineData("KeyZ", "я", Ctrl, MarinaKey.Undo)]                      // no Latin letters: the key's place
    [InlineData("KeyZ", "Z", Ctrl | Shift, MarinaKey.Redo)]
    [InlineData("KeyY", "y", Ctrl, MarinaKey.Redo)]
    [InlineData("KeyY", "z", Ctrl, MarinaKey.Undo)]                      // QWERTZ: the key that types z, wherever it is
    [InlineData("KeyZ", "y", Ctrl, MarinaKey.Redo)]
    [InlineData("KeyY", "y", Ctrl | Shift, null)]
    [InlineData("KeyY", "y", Ctrl | Alt, null)]
    [InlineData("KeyZ", "z", Ctrl | Alt, null)]                          // AltGr on many layouts
    [InlineData("KeyZ", "z", InputModifiers.None, null)]
    [InlineData("F2", "F2", InputModifiers.None, null)]
    public void DomKeys(string code, string key, InputModifiers modifiers, MarinaKey? expected)
    {
        Assert.Equal(expected, MarinaKeyMap.FromDomKey(code, key, modifiers));
    }

    [Theory]
    [InlineData(VkLeft, InputModifiers.None, MarinaKey.Left)]
    [InlineData(VkLeft, Shift, MarinaKey.Left)]
    [InlineData(VkLeft, Ctrl, null)]
    [InlineData(VkS, InputModifiers.None, MarinaKey.Down)]
    [InlineData(VkS, Ctrl, null)]
    [InlineData(VkS, Ctrl | Shift, null)]
    [InlineData(VkOemPlus, Shift, MarinaKey.ZoomIn)]
    [InlineData(VkOemPlus, Ctrl, null)]
    [InlineData(VkEscape, InputModifiers.None, MarinaKey.Escape)]
    [InlineData(VkZ, Ctrl, MarinaKey.Undo)]
    [InlineData(VkZ, Ctrl | Shift, MarinaKey.Redo)]
    [InlineData(VkY, Ctrl, MarinaKey.Redo)]
    [InlineData(VkY, Ctrl | Shift, null)]
    [InlineData(VkZ, Ctrl | Alt, null)]
    [InlineData(VkZ, InputModifiers.None, null)]
    [InlineData(VkF2, InputModifiers.None, null)]
    public void VirtualKeys(int virtualKey, InputModifiers modifiers, MarinaKey? expected)
    {
        Assert.Equal(expected, MarinaKeyMap.FromVirtualKey(virtualKey, modifiers));
    }

    [Fact]
    public void ShiftAloneIsNotAChord_ButCtrlAndAltAre()
    {
        Assert.False(MarinaKeyMap.IsChord(InputModifiers.None));
        Assert.False(MarinaKeyMap.IsChord(Shift));
        Assert.True(MarinaKeyMap.IsChord(Ctrl));
        Assert.True(MarinaKeyMap.IsChord(Alt | Shift));
    }

    // ---- The controller ---------------------------------------------------------------------------

    [Fact]
    public void CtrlS_IsNotHandled_AndTheCameraStaysPut()
    {
        var marina = WithARow();
        var before = marina.Camera.DesiredPose;

        Assert.False(marina.Input.KeyDown(MarinaKey.Down, Ctrl));
        Assert.False(marina.Input.KeyDown(MarinaKey.ZoomIn, Ctrl));
        Assert.False(marina.Input.KeyDown(MarinaKey.Left, Alt));

        Assert.Equal(before, marina.Camera.DesiredPose);
    }

    [Fact]
    public void ShiftArrow_StillOrbits()
    {
        var marina = WithARow();
        var before = marina.Camera.DesiredPose;

        Assert.True(marina.Input.KeyDown(MarinaKey.Left, Shift));
        Assert.NotEqual(before.YawDegrees, marina.Camera.DesiredPose.YawDegrees);
    }

    [Fact]
    public void Escape_WithNothingToDismiss_IsNotHandled()
    {
        var marina = WithARow();

        Assert.False(marina.Input.WantsKey(MarinaKey.Escape));
        Assert.False(marina.Input.KeyDown(MarinaKey.Escape));
    }

    [Fact]
    public void Escape_ClosesThePopup_ThenClearsTheSelection_ThenLetsGo()
    {
        var marina = WithARow();
        marina.SelectBerth("A-L01");
        marina.ShowActions();
        Assert.NotNull(marina.ActivePopup);

        Assert.True(marina.Input.WantsKey(MarinaKey.Escape));
        Assert.True(marina.Input.KeyDown(MarinaKey.Escape));
        Assert.Null(marina.ActivePopup);
        Assert.True(marina.Input.KeyDown(MarinaKey.Escape));
        Assert.Null(marina.SelectedBerth);
        Assert.False(marina.Input.KeyDown(MarinaKey.Escape));
    }

    [Fact]
    public void Escape_InTheDesigner_OnlyCountsWhileThereIsADrawingOrAToolToPutDown()
    {
        var marina = WithARow();
        marina.Designer.IsActive = true;
        marina.Designer.Tool = DesignTool.DrawPier;

        Assert.True(marina.Input.WantsKey(MarinaKey.Escape));
        Assert.True(marina.Input.KeyDown(MarinaKey.Escape));
        Assert.Equal(DesignTool.Navigate, marina.Designer.Tool);

        Assert.False(marina.Input.WantsKey(MarinaKey.Escape));
        Assert.False(marina.Input.KeyDown(MarinaKey.Escape));
    }

    [Fact]
    public void Home_OnlyCountsWhenTheCameraHadSomewhereToGo()
    {
        var marina = WithARow();
        marina.ResetCamera(immediate: true);

        Assert.False(marina.Input.WantsKey(MarinaKey.Home));
        Assert.False(marina.Input.KeyDown(MarinaKey.Home));

        marina.Input.KeyDown(MarinaKey.Left);
        Assert.True(marina.Input.WantsKey(MarinaKey.Home));
        Assert.True(marina.Input.KeyDown(MarinaKey.Home));

        // Already heading home, though still on the way: nothing more for Home to do.
        Assert.False(marina.Input.WantsKey(MarinaKey.Home));
        Assert.False(marina.Input.KeyDown(MarinaKey.Home));
    }

    [Fact]
    public void Undo_IsTheOneChordTheViewTakes_AndOnlyWithSomethingToUndo()
    {
        var marina = WithARow();
        marina.Designer.IsActive = true;
        Assert.False(marina.Input.WantsKey(MarinaKey.Undo, Ctrl));
        Assert.False(marina.Input.KeyDown(MarinaKey.Undo, Ctrl));

        marina.Designer.CreateBerths("A", PierSide.Right, 2f, 2f);
        var berths = marina.GetBerths().Count;

        Assert.True(marina.Input.WantsKey(MarinaKey.Undo, Ctrl));
        Assert.True(marina.Input.KeyDown(MarinaKey.Undo, Ctrl));
        Assert.True(marina.GetBerths().Count < berths);
    }

    [Fact]
    public void EnterAndBackspace_AreOnlyWantedWhileSomethingIsBeingDrawn()
    {
        var marina = WithARow();
        Assert.False(marina.Input.WantsKey(MarinaKey.Enter));

        marina.Designer.IsActive = true;
        marina.Designer.Tool = DesignTool.DrawLandArea;
        Assert.False(marina.Input.WantsKey(MarinaKey.Enter));
        Assert.False(marina.Input.WantsKey(MarinaKey.Backspace));
        Assert.True(marina.Input.WantsKey(MarinaKey.Left));
        Assert.False(marina.Input.WantsKey(MarinaKey.Left, Ctrl));
    }
}
