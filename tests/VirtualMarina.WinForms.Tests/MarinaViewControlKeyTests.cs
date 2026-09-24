using System.Numerics;
using System.Reflection;
using System.Windows.Forms;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
using VirtualMarina.WinForms;

namespace VirtualMarina.WinForms.Tests;

/// <summary>
/// The view claims only the keys nobody else wants. Anything it claims in PreviewKeyDown skips the form's
/// ProcessCmdKey, so a claimed Ctrl+S would never reach File ▸ Save; anything it handles in KeyDown is gone.
/// </summary>
/// <remarks>
/// The GL surface's handlers are driven directly, since a real key press needs a window and an OpenGL context.
/// </remarks>
public class MarinaViewControlKeyTests
{
    private static bool Preview(MarinaViewControl view, Keys keyData)
    {
        var args = new PreviewKeyDownEventArgs(keyData);
        Invoke(view, "OnGlPreviewKeyDown", args);
        return args.IsInputKey;
    }

    private static bool KeyDown(MarinaViewControl view, Keys keyData)
    {
        var args = new KeyEventArgs(keyData);
        Invoke(view, "OnGlKeyDown", args);
        return args.Handled;
    }

    private static void Invoke(MarinaViewControl view, string name, EventArgs args)
    {
        var method = typeof(MarinaViewControl).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(method is not null, $"MarinaViewControl.{name} was renamed; update this test.");
        method.Invoke(view, [null, args]);
    }

    private static MarinaViewControl CreateView()
    {
        var view = new MarinaViewControl();
        view.Marina.SetViewportSize(800, 600);
        view.Marina.Camera.SetPose(new CameraPose(Vector3.Zero, 0f, 45f, 120f), immediate: true);
        return view;
    }

    [Theory]
    [InlineData(Keys.Control | Keys.S)]
    [InlineData(Keys.Control | Keys.Shift | Keys.S)]
    [InlineData(Keys.Control | Keys.Z)]
    [InlineData(Keys.Control | Keys.Left)]
    [InlineData(Keys.Alt | Keys.Left)]
    [InlineData(Keys.S)]
    [InlineData(Keys.F2)]
    [InlineData(Keys.Escape)]   // nothing to dismiss: the form's Cancel button gets it
    [InlineData(Keys.Enter)]    // nothing being drawn: the form's Accept button gets it
    public void KeysTheHostMayWant_AreNotClaimed(Keys keyData)
    {
        using var view = CreateView();
        Assert.False(Preview(view, keyData));
    }

    [Theory]
    [InlineData(Keys.Left)]
    [InlineData(Keys.Shift | Keys.Up)]
    public void TheArrows_AreClaimed_SoTheFormDoesNotMoveTheFocus(Keys keyData)
    {
        using var view = CreateView();
        Assert.True(Preview(view, keyData));
    }

    [Fact]
    public void Escape_IsClaimed_OnlyWhileTheViewHasSomethingToCancel()
    {
        using var view = CreateView();
        view.Marina.Designer.IsActive = true;
        view.Marina.Designer.Tool = DesignTool.DrawPier;

        Assert.True(Preview(view, Keys.Escape));
        Assert.True(KeyDown(view, Keys.Escape));
        Assert.False(Preview(view, Keys.Escape));
    }

    [Fact]
    public void CtrlS_ReachingTheView_DoesNotPanTheCamera()
    {
        using var view = CreateView();
        var before = view.Marina.Camera.DesiredPose;

        Assert.False(KeyDown(view, Keys.Control | Keys.S));
        Assert.Equal(before, view.Marina.Camera.DesiredPose);

        Assert.True(KeyDown(view, Keys.S));
        Assert.NotEqual(before, view.Marina.Camera.DesiredPose);
    }

    [Fact]
    public void AFrameWithAnUndecodedImage_KeepsTheFlatObjectListItWasBuiltWith()
    {
        using var view = CreateView();
        view.Marina.AddPier(new VirtualMarina.Core.Domain.Pier("A", "Pier A", Vector2.Zero, 0f, 40f));
        view.Marina.Designer.SetReferenceImage(ReferenceImage.FromEncoded([1, 2, 3, 4], 2, 2), metersPerPixel: 1f, center: Vector2.Zero);
        var frame = view.Marina.BuildRenderFrame();
        Assert.NotNull(frame.ReferenceImage);

        var method = typeof(MarinaViewControl).GetMethod("WithDrawableImage", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(method is not null, "MarinaViewControl.WithDrawableImage was renamed; update this test.");
        var drawn = Assert.IsType<VirtualMarina.Core.Rendering.RenderFrame>(method.Invoke(view, [frame]));

        Assert.NotSame(frame, drawn);
        Assert.Same(frame.Objects, drawn.Objects);
        Assert.Same(frame.Layers, drawn.Layers);
    }
}
