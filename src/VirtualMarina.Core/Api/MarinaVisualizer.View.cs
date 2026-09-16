using System.Numerics;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Api;

public sealed partial class MarinaVisualizer
{
    /// <summary>Name of the built-in whole-marina preset used by <see cref="ResetCamera"/>.</summary>
    public const string OverviewPresetName = "Overview";

    // ---- Hover ----------------------------------------------------------------------------------

    /// <inheritdoc/>
    public Slip? HoveredSlip => _hoveredSlipId is null ? null : GetSlip(_hoveredSlipId);

    private void SetHoveredSlip(Slip? slip)
    {
        if (IdComparer.Equals(_hoveredSlipId, slip?.Id)) return;
        _hoveredSlipId = slip?.Id;
        MarkSceneDirty();
        SlipHoverChanged?.Invoke(this, new SlipHoverEventArgs(slip));
    }

    // ---- Called by MarinaInputController --------------------------------------------------------

    /// <summary>Disabled slips are never hovered.</summary>
    internal void HandlePointerHover(float x, float y)
    {
        var hit = HitTest(x, y);
        SetHoveredSlip(hit is { } h && GetSlip(h.SlipId) is { IsInteractive: true } slip ? slip : null);
    }

    internal void HandlePointerLeave() => SetHoveredSlip(null);

    // ---- Camera -----------------------------------------------------------------------------

    /// <inheritdoc/>
    public IReadOnlyList<CameraPreset> CameraPresets => _presets;

    /// <inheritdoc/>
    public void ResetCamera(bool immediate = false)
    {
        if (!ApplyCameraPreset(OverviewPresetName, immediate))
        {
            Camera.SetPose(new CameraPose(Vector3.Zero, 25f, 45f, 150f), immediate);
        }
    }

    /// <inheritdoc/>
    public bool ApplyCameraPreset(string presetName, bool immediate = false)
    {
        var preset = _presets.FirstOrDefault(p => string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));
        if (preset is null) return false;
        Camera.SetPose(preset.Pose, immediate);
        return true;
    }

    /// <summary>Adds a custom preset (replacing one with the same name). Custom presets survive layout changes.</summary>
    public void AddCameraPreset(CameraPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        _presets.RemoveAll(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        _presets.Add(preset with { IsBuiltIn = false });
    }

    /// <summary>Removes a custom preset by name. Built-in presets can't be removed. Returns false when nothing was removed.</summary>
    /// <param name="presetName">Preset name (case-insensitive).</param>
    public bool RemoveCameraPreset(string presetName) =>
        _presets.RemoveAll(p => !p.IsBuiltIn && string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase)) > 0;

    // ---- Focus on slips ------------------------------------------------------------------------

    /// <summary>
    /// Angle used when a focus call doesn't pass one (including double-click and <c>SelectSlip(id, focusCamera: true)</c>).
    /// Null (default) keeps the current yaw and looks down at least 35°.
    /// </summary>
    public CameraAngle? DefaultFocusAngle { get; set; }

    /// <summary>Fraction of the view kept free around focused slips on each side (0–0.45, default 0.12).</summary>
    public float FocusMargin
    {
        get => _focusMargin;
        set => _focusMargin = Math.Clamp(float.IsFinite(value) ? value : 0.12f, 0f, 0.45f);
    }

    /// <summary>Closest the camera gets when focusing (so a single slip still shows some context). Default 25 m.</summary>
    public float MinFocusDistance { get; set; } = 25f;

    private float _focusMargin = 0.12f;

    /// <summary>Centers the camera on a slip at <see cref="DefaultFocusAngle"/>, zoomed to show it whole.</summary>
    public bool FocusSlip(string slipId, bool immediate = false) => FocusSlips(new[] { slipId }, null, immediate);

    /// <summary>Centers the camera on a slip, seen from <paramref name="angle"/>, zoomed to show it whole.</summary>
    public bool FocusSlip(string slipId, CameraAngle angle, bool immediate = false) => FocusSlips(new[] { slipId }, angle, immediate);

    /// <summary>
    /// Moves the camera so every listed slip (and its boat) is in view: the target is the middle of the slips and the
    /// distance is the closest that fits them, with <see cref="FocusMargin"/> around them.
    /// </summary>
    /// <param name="slipIds">Slips to show; unknown ids are ignored. Hidden, disabled and filtered-out slips are included.</param>
    /// <param name="angle">Viewing angle, e.g. <see cref="CameraAngle.TopDown"/>. Null uses <see cref="DefaultFocusAngle"/>.</param>
    /// <param name="immediate">Jump instead of animating.</param>
    /// <returns>False when none of the ids exist.</returns>
    public bool FocusSlips(IEnumerable<string> slipIds, CameraAngle? angle = null, bool immediate = false)
    {
        ArgumentNullException.ThrowIfNull(slipIds);
        var slips = slipIds.Where(id => id is not null).Select(GetSlip).OfType<Slip>().DistinctBy(s => s.Id, IdComparer).ToList();
        if (slips.Count == 0) return false;

        Camera.SetPose(ComputeFocusPose(slips, angle), immediate);
        _lastFocus = new FocusRequest(slips.Select(s => s.Id).ToArray(), angle, Camera.DesiredPose);
        return true;
    }

    /// <summary>The last focus, so it can be re-fitted when the view is resized (e.g. focus called before the view got its size).</summary>
    private sealed record FocusRequest(string[] SlipIds, CameraAngle? Angle, CameraPose Pose);

    private FocusRequest? _lastFocus;

    /// <summary>Re-fits the last focus for a new viewport size, unless the camera has been moved since.</summary>
    private void RefitFocusAfterResize()
    {
        if (_lastFocus is not { } focus || Camera.DesiredPose != focus.Pose)
        {
            _lastFocus = null;
            return;
        }

        var slips = focus.SlipIds.Select(GetSlip).OfType<Slip>().ToList();
        if (slips.Count == 0)
        {
            _lastFocus = null;
            return;
        }

        var arrived = Camera.Pose == Camera.DesiredPose;
        Camera.SetPose(ComputeFocusPose(slips, focus.Angle), immediate: arrived);
        _lastFocus = focus with { Pose = Camera.DesiredPose };
    }

    /// <summary>Focuses on the current selection. Returns false when nothing is selected.</summary>
    public bool FocusSelection(CameraAngle? angle = null, bool immediate = false) =>
        _selection.Count > 0 && FocusSlips(_selection, angle, immediate);

    /// <summary>The pose <see cref="FocusSlips"/> would move to, without moving the camera.</summary>
    public CameraPose ComputeFocusPose(IReadOnlyCollection<Slip> slips, CameraAngle? angle = null)
    {
        ArgumentNullException.ThrowIfNull(slips);
        if (slips.Count == 0) throw new ArgumentException("At least one slip is required.", nameof(slips));

        var resolved = angle ?? DefaultFocusAngle ?? new CameraAngle(Camera.DesiredPose.YawDegrees, MathF.Max(Camera.DesiredPose.PitchDegrees, 35f));
        var points = FocusPoints(slips);

        var (planMin, planMax) = MarinaLayout.ComputeBounds(slips.Select(s => s.Bounds));
        var target = MarinaMath.ToWorld((planMin + planMax) * 0.5f);
        var extent = MathF.Max(planMax.X - planMin.X, planMax.Y - planMin.Y);
        var limit = 1f - 2f * _focusMargin; // usable NDC half-extent
        var pose = Camera.Constrain(new CameraPose(target, resolved.YawDegrees, resolved.PitchDegrees, MathF.Max(MinFocusDistance, FitDistance(extent))));

        // Refine: re-center the projected bounds and scale the distance until they fill the usable area.
        for (var iteration = 0; iteration < 12; iteration++)
        {
            if (!TryProjectedBounds(pose, points, out var min, out var max))
            {
                pose = Camera.Constrain(pose with { Distance = pose.Distance * 2f });
                continue;
            }

            var center = (min + max) * 0.5f;
            var halfExtent = MathF.Max(max.X - min.X, max.Y - min.Y) * 0.5f;
            var scale = halfExtent / limit;
            if (MathF.Abs(center.X) < 0.01f && MathF.Abs(center.Y) < 0.01f && MathF.Abs(scale - 1f) < 0.02f) break;

            var (tanH, tanV) = TanHalfFov();
            var yaw = pose.YawDegrees * MarinaMath.DegToRad;
            var right = new Vector3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));
            var groundForward = new Vector3(-MathF.Sin(yaw), 0f, -MathF.Cos(yaw));
            var sinPitch = MathF.Max(MathF.Sin(pose.PitchDegrees * MarinaMath.DegToRad), 0.25f);
            var shift = right * (center.X * pose.Distance * tanH) + groundForward * (center.Y * pose.Distance * tanV / sinPitch);

            var distance = pose.Distance * (halfExtent > 1e-4f ? Math.Clamp(scale, 0.25f, 4f) : 1f);
            pose = Camera.Constrain(pose with { Target = pose.Target + shift, Distance = MathF.Max(MinFocusDistance, distance) });
        }

        // Guarantee: back off until everything is inside the view (perspective makes the estimate slightly optimistic,
        // and degenerate viewports can stop the refinement early). Re-center on every step.
        for (var i = 0; i < 200; i++)
        {
            if (!TryProjectedBounds(pose, points, out var min, out var max))
            {
                if (pose.Distance >= Camera.Constraints.MaxDistance) break;
                pose = Camera.Constrain(pose with { Distance = pose.Distance * 1.5f });
                continue;
            }

            if (min.X >= -1f && max.X <= 1f && min.Y >= -1f && max.Y <= 1f) break;
            if (pose.Distance >= Camera.Constraints.MaxDistance) break;

            var center = (min + max) * 0.5f;
            var (tanH, tanV) = TanHalfFov();
            var yaw = pose.YawDegrees * MarinaMath.DegToRad;
            var right = new Vector3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));
            var groundForward = new Vector3(-MathF.Sin(yaw), 0f, -MathF.Cos(yaw));
            var sinPitch = MathF.Max(MathF.Sin(pose.PitchDegrees * MarinaMath.DegToRad), 0.25f);
            var shift = right * (center.X * pose.Distance * tanH) + groundForward * (center.Y * pose.Distance * tanV / sinPitch);
            var overflow = MathF.Max(MathF.Max(max.X - min.X, max.Y - min.Y) * 0.5f, 1f);
            pose = Camera.Constrain(pose with { Target = pose.Target + shift, Distance = pose.Distance * MathF.Max(1.05f, overflow) });
        }

        return pose;
    }

    /// <summary>Slip corners on the water and at the height of their boats (so masts stay in view from oblique angles).</summary>
    private List<Vector3> FocusPoints(IEnumerable<Slip> slips)
    {
        var points = new List<Vector3>();
        foreach (var slip in slips)
        {
            var top = slip.Boat is { } boat && slip.Status.CanHaveBoat() ? Picking.SlipPlacement.BoatTopHeight(boat, Meshes) : 1f;
            foreach (var corner in slip.Bounds.GetCorners())
            {
                points.Add(MarinaMath.ToWorld(corner));
                points.Add(MarinaMath.ToWorld(corner, top));
            }
        }

        return points;
    }

    private (float TanH, float TanV) TanHalfFov()
    {
        var tanV = MathF.Tan(Camera.FieldOfViewDegrees * MarinaMath.DegToRad * 0.5f);
        return (tanV * (_viewportSize.X / _viewportSize.Y), tanV);
    }

    /// <summary>NDC bounds of <paramref name="points"/> seen from <paramref name="pose"/>. False if any point is behind the camera.</summary>
    private bool TryProjectedBounds(CameraPose pose, IReadOnlyList<Vector3> points, out Vector2 min, out Vector2 max)
    {
        var eye = OrbitCamera.ComputeEye(pose);
        // Same view/projection as OrbitCamera (pitch is capped below 90°, so +Y is always a valid up vector).
        var viewProjection = Matrix4x4.CreateLookAt(eye, pose.Target, Vector3.UnitY) *
            MarinaMath.CreatePerspectiveGL(Camera.FieldOfViewDegrees * MarinaMath.DegToRad, _viewportSize.X / _viewportSize.Y, Camera.NearPlane, Camera.FarPlane);

        min = new Vector2(float.MaxValue);
        max = new Vector2(float.MinValue);
        foreach (var point in points)
        {
            var clip = Vector4.Transform(new Vector4(point, 1f), viewProjection);
            if (clip.W <= 1e-3f) return false;
            var ndc = new Vector2(clip.X, clip.Y) / clip.W;
            min = Vector2.Min(min, ndc);
            max = Vector2.Max(max, ndc);
        }

        return true;
    }

    /// <inheritdoc/>
    public bool FocusDock(string dockId, bool immediate = false)
    {
        var dock = GetDock(dockId);
        if (dock is null) return false;
        Camera.SetPose(CreateDockPose(dock), immediate);
        return true;
    }

    private CameraPose CreateDockPose(Dock dock)
    {
        var slips = OrderedSlips().Where(s => IdComparer.Equals(s.DockId, dock.Id)).Select(s => s.Bounds).Append(dock.Bounds);
        var (min, max) = MarinaLayout.ComputeBounds(slips);
        var center = (min + max) * 0.5f;
        var extent = MathF.Max(max.X - min.X, max.Y - min.Y);
        return new CameraPose(MarinaMath.ToWorld(center), dock.HeadingDegrees + 215f, 40f, MathF.Max(45f, FitDistance(extent) * 0.62f));
    }

    private float FitDistance(float extent) =>
        MathF.Max(40f, extent * 0.5f / MathF.Tan(Camera.FieldOfViewDegrees * MarinaMath.DegToRad * 0.5f) * 1.15f);

    /// <summary>Regenerates the automatic presets and camera bounds from the current layout.</summary>
    private void RebuildBuiltInPresets()
    {
        var rects = OrderedDocks().Select(d => d.Bounds)
            .Concat(OrderedSlips().Select(s => s.Bounds))
            .Concat(OrderedDividers().Select(d => d.Bounds))
            .Concat(_landAreas.Select(l => l.Area));
        var (min, max) = MarinaLayout.ComputeBounds(rects);
        var center = MarinaMath.ToWorld((min + max) * 0.5f);
        var extent = MathF.Max(max.X - min.X, max.Y - min.Y);
        var fit = FitDistance(extent);

        const float margin = 120f;
        Camera.Constraints.TargetBoundsMin = min - new Vector2(margin);
        Camera.Constraints.TargetBoundsMax = max + new Vector2(margin);
        Camera.Constraints.MaxDistance = MathF.Max(250f, fit * 2.5f);
        Camera.FarPlane = MathF.Max(1500f, Camera.Constraints.MaxDistance * 4f);

        var builtIn = new List<CameraPreset>
        {
            new(OverviewPresetName, new CameraPose(center, 200f, 42f, fit), "Whole marina from the shore side") { IsBuiltIn = true },
            new("Top Down", new CameraPose(center, 180f, 89f, fit * 1.05f), "Plan view, shore at the bottom") { IsBuiltIn = true },
            new("Sea Side", new CameraPose(center, 20f, 30f, fit * 0.95f), "Looking back toward the shore") { IsBuiltIn = true },
            new("East", new CameraPose(center, 90f, 35f, fit * 0.9f), "From the east") { IsBuiltIn = true },
            new("West", new CameraPose(center, 270f, 35f, fit * 0.9f), "From the west") { IsBuiltIn = true },
            new("Low Angle", new CameraPose(center, 225f, 14f, fit * 0.7f), "Close to the water line") { IsBuiltIn = true },
        };

        foreach (var dock in OrderedDocks())
        {
            builtIn.Add(new CameraPreset($"Dock: {dock.Name}", CreateDockPose(dock), $"Close-up of {dock.Name}") { IsBuiltIn = true });
        }

        var custom = _presets.Where(p => !p.IsBuiltIn).ToList();
        _presets.Clear();
        _presets.AddRange(builtIn);
        _presets.AddRange(custom);
    }
}
