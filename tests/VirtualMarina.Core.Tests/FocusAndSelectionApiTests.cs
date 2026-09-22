using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.SampleData;

namespace VirtualMarina.Core.Tests;

public class FocusAndSelectionApiTests
{
    private static MarinaVisualizer CreateSampleMarina(float width = 1280, float height = 720)
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina());
        marina.SetViewportSize(width, height);
        return marina;
    }

    /// <summary>Screen-space bounds (pixels) of the berths' corners with the current camera.</summary>
    private static (Vector2 Min, Vector2 Max) ScreenBounds(MarinaVisualizer marina, IEnumerable<string> berthIds)
    {
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var berth in berthIds.Select(id => marina.GetBerth(id)!))
        {
            foreach (var corner in berth.Bounds.GetCorners())
            {
                Assert.True(marina.TryProjectToScreen(MarinaMath.ToWorld(corner), out var p));
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
        }

        return (min, max);
    }

    private static void AssertFramed(MarinaVisualizer marina, IReadOnlyCollection<string> berthIds)
    {
        var (min, max) = ScreenBounds(marina, berthIds);
        var size = marina.ViewportSize;
        Assert.True(min.X >= 0 && min.Y >= 0 && max.X <= size.X && max.Y <= size.Y, $"berths {min}..{max} must be inside the {size} view");

        // Centered on the berths, and zoomed in (not just far away).
        var center = (min + max) * 0.5f;
        Assert.InRange(center.X, size.X * 0.4f, size.X * 0.6f);
        Assert.InRange(center.Y, size.Y * 0.35f, size.Y * 0.65f);
        // Unless the minimum focus distance kept the camera back (small targets keep some context around them).
        if (marina.Camera.Pose.Distance > marina.MinFocusDistance + 0.5f)
        {
            Assert.True((max.X - min.X) / size.X > 0.5f || (max.Y - min.Y) / size.Y > 0.5f, "the berths must fill a good part of the view");
        }
        else
        {
            Assert.True((max.X - min.X) / size.X > 0.2f || (max.Y - min.Y) / size.Y > 0.2f, "a single berth must still be clearly visible");
        }
    }

    // ---- SetSelection -------------------------------------------------------------------------------

    [Fact]
    public void SetSelection_SelectsManyBerths_DiscardingDisabledHiddenFilteredAndUnknown()
    {
        var marina = CreateSampleMarina();
        marina.SetBerthDisabled("A-L03", true);
        marina.SetBerthVisible("A-L04", false);
        marina.ReleaseBerth("A-L05");
        marina.SetStatusFilter(BerthStatusFilter.All & ~BerthStatusFilter.Free);
        marina.AssignBoat("A-L06", new Boat("X", "X", BoatType.FishingBoat));
        marina.AssignBoat("A-L07", new Boat("Y", "Y", BoatType.FishingBoat));
        MultiBerthSelectedEventArgs? multi = null;
        marina.MultiBerthSelected += (_, e) => multi = e;

        var result = marina.SetSelection("A-L06", "A-L03", "A-L04", "A-L05", "NOPE", "a-l07", "A-L06");

        Assert.Equal(new[] { "A-L06", "A-L07" }, result.SelectedBerthIds);
        Assert.True(result.Changed);
        Assert.Equal(
            new[] { ("A-L03", BerthSelectionRejection.Disabled), ("A-L04", BerthSelectionRejection.Hidden), ("A-L05", BerthSelectionRejection.FilteredOut), ("NOPE", BerthSelectionRejection.NotFound) },
            result.Rejected.Select(r => (r.BerthId, r.Reason)));
        Assert.Equal(new[] { "A-L06", "A-L07" }, marina.SelectedBerths.Select(s => s.Id));
        Assert.Equal("A-L07", marina.SelectedBerth?.Id);
        Assert.Equal(2, multi?.Berths.Count);

        // Same selection again: nothing changes.
        Assert.False(marina.SetSelection("A-L06", "A-L07").Changed);
    }

    [Fact]
    public void SetSelection_WithOnlyDisabledBerths_ClearsTheSelection()
    {
        var marina = CreateSampleMarina();
        marina.SetSelection("A-L06");
        marina.SetBerthFlags(new[] { "A-L08", "A-L09" }, disabled: true);

        var result = marina.SetSelection(new[] { "A-L08", "A-L09" });

        Assert.True(result.IsEmpty);
        Assert.Empty(marina.SelectedBerths);
        Assert.All(result.Rejected, r => Assert.Equal(BerthSelectionRejection.Disabled, r.Reason));
    }

    [Fact]
    public void SetSelection_WithFocus_FramesAllSelectedBerthsTopDown()
    {
        var marina = CreateSampleMarina();
        var pierB = marina.GetBerthsByPier("B").Select(s => s.Id).ToArray();

        var result = marina.SetSelection(pierB, focusCamera: true, CameraAngle.TopDown);
        marina.Camera.Update(10f); // let the smoothed camera arrive

        Assert.Equal(pierB.Length - 1, result.Count); // B-R12 is hidden in the sample
        Assert.True(marina.Camera.Pose.PitchDegrees > 88f);
        AssertFramed(marina, result.SelectedBerthIds);
    }

    // ---- FocusBerths ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("C-L04")]
    [InlineData("A-L01,A-L12")]
    [InlineData("A-R01,D-L01")] // opposite ends of the marina
    [InlineData("B-L01,B-L12,B-R01,B-R12,C-L08")]
    public void FocusBerths_TopDown_CentersAndFitsEveryBerth(string ids)
    {
        var berthIds = ids.Split(',');
        var marina = CreateSampleMarina();

        Assert.True(marina.FocusBerths(berthIds, CameraAngle.TopDown, immediate: true));

        Assert.Equal(180f, marina.Camera.Pose.YawDegrees % 360f, 1);
        Assert.True(marina.Camera.Pose.PitchDegrees > 88f);
        AssertFramed(marina, berthIds);
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(600, 900)] // portrait: width is the limiting dimension
    public void FocusBerths_FitsForAnyViewportAspect(float width, float height)
    {
        var marina = CreateSampleMarina(width, height);
        var berths = new[] { "A-R01", "D-L01" };

        marina.FocusBerths(berths, CameraAngle.TopDown, immediate: true);

        AssertFramed(marina, berths);
    }

    [Theory]
    [InlineData(200f, 45f)]
    [InlineData(90f, 30f)]
    [InlineData(20f, 60f)]
    public void FocusBerths_HonoursACustomAngle(float yaw, float pitch)
    {
        var marina = CreateSampleMarina();
        var berths = new[] { "C-L01", "C-L08", "C-R04" };

        marina.FocusBerths(berths, new CameraAngle(yaw, pitch), immediate: true);

        Assert.Equal(yaw, marina.Camera.Pose.YawDegrees, 1);
        Assert.Equal(pitch, marina.Camera.Pose.PitchDegrees, 1);
        var (min, max) = ScreenBounds(marina, berths);
        Assert.True(min.X >= 0 && min.Y >= 0 && max.X <= 1280 && max.Y <= 720, $"{min}..{max}");
    }

    [Theory]
    [InlineData(100, 20)]
    [InlineData(20, 100)]
    [InlineData(964, 1)]
    [InlineData(1, 839)]
    [InlineData(150, 150)]
    public void ComputeFocusPose_FitsEvenForDegenerateViewports(float width, float height)
    {
        var marina = CreateSampleMarina(width, height);
        var berths = new[] { "A-R02", "D-L03" };

        marina.FocusBerths(berths, CameraAngle.TopDown, immediate: true);

        var (min, max) = ScreenBounds(marina, berths);
        var center = (min + max) * 0.5f;
        Assert.InRange(center.X, width * 0.4f - 1f, width * 0.6f + 1f);
        Assert.InRange(center.Y, height * 0.4f - 1f, height * 0.6f + 1f);

        // A sliver-thin view may need more distance than the camera constraints allow; then the camera stops at the limit.
        if (marina.Camera.Pose.Distance < marina.Camera.Constraints.MaxDistance - 0.5f)
        {
            Assert.True(min.X >= -0.5f && min.Y >= -0.5f && max.X <= width + 0.5f && max.Y <= height + 0.5f, $"{min}..{max} in {width}x{height}");
        }
    }

    [Fact]
    public void FocusBeforeTheViewHasItsSize_IsRefittedOnResize_UnlessTheCameraMoved()
    {
        var marina = CreateSampleMarina(); // default 1280 × 720, as before a view reports its size
        var berths = new[] { "A-R02", "D-L03" };
        marina.FocusBerths(berths, CameraAngle.TopDown, immediate: true);

        marina.SetViewportSize(960, 840); // the real (narrower) view
        AssertFramed(marina, berths);

        // Once the user pans, resizing must not yank the camera back.
        marina.Camera.PanWorld(30f, 0f);
        marina.Camera.Update(10f);
        var moved = marina.Camera.DesiredPose;
        marina.SetViewportSize(1200, 700);
        Assert.Equal(moved, marina.Camera.DesiredPose);
    }

    [Fact]
    public void FocusBerths_MoreSpreadOutBerths_ZoomFurtherOut()
    {
        var marina = CreateSampleMarina();

        var single = marina.ComputeFocusPose(new[] { marina.GetBerth("B-L05")! }, CameraAngle.TopDown);
        var pier = marina.ComputeFocusPose(marina.GetBerthsByPier("B"), CameraAngle.TopDown);
        var marinaWide = marina.ComputeFocusPose(marina.GetBerths(), CameraAngle.TopDown);

        Assert.True(single.Distance < pier.Distance && pier.Distance < marinaWide.Distance);
        Assert.True(single.Distance >= marina.MinFocusDistance);
    }

    [Fact]
    public void Focus_UsesDefaultFocusAngle_ForOverloadsWithoutAngle_AndDoubleClick()
    {
        var marina = CreateSampleMarina();
        marina.ApplyCameraPreset(MarinaVisualizer.OverviewPresetName, immediate: true);
        var overviewYaw = marina.Camera.Pose.YawDegrees;

        // No default: keep the current yaw.
        marina.FocusBerth("B-L03", immediate: true);
        Assert.Equal(overviewYaw, marina.Camera.Pose.YawDegrees, 1);

        marina.DefaultFocusAngle = CameraAngle.TopDown;
        marina.ApplyCameraPreset(MarinaVisualizer.OverviewPresetName, immediate: true);
        Assert.True(marina.SelectBerth("B-L03", focusCamera: true));
        marina.Camera.Update(10f);
        Assert.True(marina.Camera.Pose.PitchDegrees > 88f);

        // Double-click on a berth focuses with the default angle too.
        marina.ApplyCameraPreset(MarinaVisualizer.OverviewPresetName, immediate: true);
        var berth = marina.GetBerth("C-L04")!;
        marina.Camera.SetPose(new CameraPose(MarinaMath.ToWorld(berth.Center), 0f, 60f, 60f), immediate: true);
        marina.Input.DoubleClick(640, 360, PointerButton.Left);
        marina.Camera.Update(10f);
        Assert.True(marina.Camera.Pose.PitchDegrees > 88f);
        AssertFramed(marina, new[] { "C-L04" });
    }

    [Fact]
    public void FocusSelection_AndUnknownIds()
    {
        var marina = CreateSampleMarina();
        Assert.False(marina.FocusSelection(CameraAngle.TopDown));
        Assert.False(marina.FocusBerths(new[] { "NOPE" }, CameraAngle.TopDown));

        marina.SetSelection("D-L01", "D-L09");
        Assert.True(marina.FocusSelection(CameraAngle.TopDown, immediate: true));
        AssertFramed(marina, new[] { "D-L01", "D-L09" });
    }
}
