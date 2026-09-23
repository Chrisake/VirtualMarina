using VirtualMarina.Core.Api;
using VirtualMarina.Designer.Resources;

namespace VirtualMarina.Designer;

/// <summary>The names the Cameras panel gives saved views, shared by both apps.</summary>
public static class CameraNames
{
    /// <summary>True when a saved (not automatic) view already has this name, which saving again would replace.</summary>
    /// <param name="marina">The marina.</param>
    /// <param name="name">The name typed.</param>
    public static bool IsSaved(IMarinaVisualizer marina, string name)
    {
        ArgumentNullException.ThrowIfNull(marina);
        ArgumentNullException.ThrowIfNull(name);
        return marina.CameraPresets.Any(preset => !preset.IsBuiltIn && string.Equals(preset.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The first "View N" no view has yet, counting from 1. Counting the saved views instead would offer "View 3"
    /// again once "View 2" had been removed from three, and saving it would silently replace the "View 3" there is.
    /// </summary>
    /// <param name="marina">The marina.</param>
    public static string FirstFree(IMarinaVisualizer marina)
    {
        ArgumentNullException.ThrowIfNull(marina);
        var taken = marina.CameraPresets.Select(preset => preset.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var number = 1;
        string name;
        do
        {
            name = Strings.Format(Strings.CameraDefaultName, number++);
        }
        while (taken.Contains(name));

        return name;
    }
}
