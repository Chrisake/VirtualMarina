using VirtualMarina.Core.Api;
using VirtualMarina.Designer.Resources;

namespace VirtualMarina.Designer.Common.Tests;

/// <summary>The names the Cameras panel gives saved views.</summary>
public class CameraNamesTests
{
    private static string View(int number) => Strings.Format(Strings.CameraDefaultName, number);

    [Fact]
    public void The_first_free_name_skips_the_ones_taken_and_fills_gaps()
    {
        var marina = new MarinaVisualizer();
        Assert.Equal(View(1), CameraNames.FirstFree(marina));

        marina.SaveCameraPreset(View(1));
        marina.SaveCameraPreset(View(2));
        marina.SaveCameraPreset(View(3));
        Assert.Equal(View(4), CameraNames.FirstFree(marina));

        // Three saved views with the second gone: counting them would offer View 3 again and replace it.
        Assert.True(marina.RemoveCameraPreset(View(2)));
        Assert.Equal(View(2), CameraNames.FirstFree(marina));
    }

    [Fact]
    public void Only_saved_views_count_as_taken_when_asking_before_a_replace()
    {
        var marina = new MarinaVisualizer();
        marina.SaveCameraPreset("Harbour mouth");
        var automatic = marina.CameraPresets.First(preset => preset.IsBuiltIn).Name;

        Assert.True(CameraNames.IsSaved(marina, "harbour MOUTH "));
        Assert.False(CameraNames.IsSaved(marina, automatic));
        Assert.False(CameraNames.IsSaved(marina, "Elsewhere"));
    }
}
