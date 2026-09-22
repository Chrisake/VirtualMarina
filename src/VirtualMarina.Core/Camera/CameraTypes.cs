using System.Numerics;

namespace VirtualMarina.Core.Camera;

/// <summary>An orbit-camera state: the point looked at, plus spherical offset angles and distance.</summary>
/// <param name="Target">Focus point (usually on the water plane).</param>
/// <param name="YawDegrees">Compass angle of the camera around the target (0° = camera on the +Z side).</param>
/// <param name="PitchDegrees">Elevation above the horizon (90° = straight down).</param>
/// <param name="Distance">Distance from target to eye in meters.</param>
public readonly record struct CameraPose(Vector3 Target, float YawDegrees, float PitchDegrees, float Distance);

/// <summary>Viewing direction for focusing the camera: where it sits around the target, and how steeply it looks down.</summary>
/// <param name="YawDegrees">Compass angle of the camera around the target (0° = camera on the +Z side looking toward −Z, 180° = on the −Z side looking toward +Z).</param>
/// <param name="PitchDegrees">Elevation above the horizon: 90° looks straight down. Clamped by <see cref="CameraConstraints"/> (at most 89°).</param>
public readonly record struct CameraAngle(float YawDegrees, float PitchDegrees)
{
    /// <summary>Straight down, with +Z (away from the shore in the usual layout) at the top of the screen.</summary>
    public static CameraAngle TopDown { get; } = new(180f, 89f);

    /// <summary>Straight down with the given compass orientation.</summary>
    public static CameraAngle TopDownFacing(float yawDegrees) => new(yawDegrees, 89f);

    /// <summary>The angle of an existing pose.</summary>
    public static CameraAngle Of(CameraPose pose) => new(pose.YawDegrees, pose.PitchDegrees);
}

/// <summary>A named viewpoint staff can jump to (<c>IMarinaVisualizer.ApplyCameraPreset</c>).</summary>
/// <param name="Name">Unique name (case-insensitive), e.g. "Fuel pier".</param>
/// <param name="Pose">Camera target, angles and distance.</param>
/// <param name="Description">Optional description, e.g. for a tooltip in a preset menu.</param>
/// <example><code>marina.AddCameraPreset(new CameraPreset("Fuel pier", new CameraPose(new Vector3(40, 0, 10), 150, 35, 60)));</code></example>
public sealed record CameraPreset(string Name, CameraPose Pose, string? Description = null)
{
    /// <summary>True for presets generated automatically from the layout (rebuilt when the layout changes).</summary>
    public bool IsBuiltIn { get; init; }

    /// <summary>
    /// False when this view has been switched off, so a host should leave it out of the list it offers. Switched-off
    /// views are still in <c>CameraPresets</c> and can still be applied by name; see
    /// <c>IMarinaVisualizer.SetCameraPresetEnabled</c>.
    /// </summary>
    public bool IsEnabled { get; init; } = true;
}

/// <summary>Limits that keep the camera usable: no flipping, no dipping under water, no flying away.</summary>
public sealed class CameraConstraints
{
    /// <summary>Lowest elevation angle. Keeps the view above the horizon.</summary>
    public float MinPitchDegrees { get; set; } = 8f;

    /// <summary>Highest elevation angle. Must stay below 90° so the view never flips over.</summary>
    public float MaxPitchDegrees { get; set; } = 89f;

    /// <summary>Closest the camera may get to its target, in meters.</summary>
    public float MinDistance { get; set; } = 6f;

    /// <summary>Farthest the camera may get from its target, in meters (the visualizer raises it to fit the layout).</summary>
    public float MaxDistance { get; set; } = 650f;

    /// <summary>Minimum eye height above the water plane, in meters.</summary>
    public float MinEyeHeight { get; set; } = 1.5f;

    /// <summary>Minimum corner of the plan-view region (X, Z) the target may move within (layout bounds plus a margin).</summary>
    public Vector2 TargetBoundsMin { get; set; } = new(-600f, -600f);

    /// <summary>Maximum corner of the plan-view region (X, Z) the target may move within.</summary>
    public Vector2 TargetBoundsMax { get; set; } = new(600f, 600f);
}
