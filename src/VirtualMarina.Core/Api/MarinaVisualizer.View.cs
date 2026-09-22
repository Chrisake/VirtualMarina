using System.Numerics;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Api;

public sealed partial class MarinaVisualizer
{
    /// <summary>Name of the built-in whole-marina preset used by <see cref="ResetCamera"/>.</summary>
    public const string OverviewPresetName = "Overview";

    /// <summary>Name of the built-in plan view.</summary>
    public const string TopDownPresetName = "Top Down";

    /// <summary>Built-in views the user switched off, by name. Kept across layout changes and saved with the design.</summary>
    private readonly HashSet<string> _disabledPresets = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public bool SetCameraPresetEnabled(string presetName, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(presetName);
        var index = _presets.FindIndex(p => string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;

        if (enabled) _disabledPresets.Remove(_presets[index].Name);
        else _disabledPresets.Add(_presets[index].Name);

        _presets[index] = _presets[index] with { IsEnabled = enabled };
        return true;
    }

    /// <summary>The names of the built-in views that are switched off, for saving with the design.</summary>
    internal IReadOnlyCollection<string> DisabledCameraPresets => _disabledPresets;

    /// <summary>Restores which built-in views are switched off, when a design is loaded.</summary>
    internal void RestoreDisabledCameraPresets(IEnumerable<string> names)
    {
        _disabledPresets.Clear();
        foreach (var name in names) _disabledPresets.Add(name);
        RebuildBuiltInPresets();
    }

    // ---- Hover ----------------------------------------------------------------------------------

    /// <inheritdoc/>
    public Berth? HoveredBerth => _hoveredBerthId is null ? null : GetBerth(_hoveredBerthId);

    private void SetHoveredBerth(Berth? berth)
    {
        if (IdComparer.Equals(_hoveredBerthId, berth?.Id)) return;
        _hoveredBerthId = berth?.Id;
        MarkSceneDirty();
        BerthHoverChanged?.Invoke(this, new BerthHoverEventArgs(berth));
    }

    // ---- Called by MarinaInputController --------------------------------------------------------

    /// <summary>Disabled berths are never hovered, and nothing is while the designer is active.</summary>
    internal void HandlePointerHover(float x, float y)
    {
        if (Designer.IsActive)
        {
            SetHoveredBerth(null);
            return;
        }

        var hit = HitTest(x, y);
        SetHoveredBerth(hit is { } h && GetBerth(h.BerthId) is { IsInteractive: true } berth ? berth : null);
    }

    internal void HandlePointerLeave() => SetHoveredBerth(null);

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

    // ---- Focus on berths ------------------------------------------------------------------------

    /// <summary>
    /// Angle used when a focus call doesn't pass one (including double-click and <c>SelectBerth(id, focusCamera: true)</c>).
    /// Null (default) keeps the current yaw and looks down at least 35°.
    /// </summary>
    public CameraAngle? DefaultFocusAngle { get; set; }

    /// <summary>Fraction of the view kept free around focused berths on each side (0–0.45, default 0.12).</summary>
    public float FocusMargin
    {
        get => _focusMargin;
        set => _focusMargin = Math.Clamp(float.IsFinite(value) ? value : 0.12f, 0f, 0.45f);
    }

    /// <summary>Closest the camera gets when focusing (so a single berth still shows some context). Default 25 m.</summary>
    public float MinFocusDistance { get; set; } = 25f;

    private float _focusMargin = 0.12f;

    /// <summary>Centers the camera on a berth at <see cref="DefaultFocusAngle"/>, zoomed to show it whole.</summary>
    public bool FocusBerth(string berthId, bool immediate = false) => FocusBerths(new[] { berthId }, null, immediate);

    /// <summary>Centers the camera on a berth, seen from <paramref name="angle"/>, zoomed to show it whole.</summary>
    public bool FocusBerth(string berthId, CameraAngle angle, bool immediate = false) => FocusBerths(new[] { berthId }, angle, immediate);

    /// <summary>
    /// Moves the camera so every listed berth (and its boat) is in view: the target is the middle of the berths and the
    /// distance is the closest that fits them, with <see cref="FocusMargin"/> around them.
    /// </summary>
    /// <param name="berthIds">Berths to show; unknown ids are ignored. Hidden, disabled and filtered-out berths are included.</param>
    /// <param name="angle">Viewing angle, e.g. <see cref="CameraAngle.TopDown"/>. Null uses <see cref="DefaultFocusAngle"/>.</param>
    /// <param name="immediate">Jump instead of animating.</param>
    /// <returns>False when none of the ids exist.</returns>
    public bool FocusBerths(IEnumerable<string> berthIds, CameraAngle? angle = null, bool immediate = false)
    {
        ArgumentNullException.ThrowIfNull(berthIds);
        var berths = berthIds.Where(id => id is not null).Select(GetBerth).OfType<Berth>().DistinctBy(s => s.Id, IdComparer).ToList();
        if (berths.Count == 0) return false;

        Camera.SetPose(ComputeFocusPose(berths, angle), immediate);
        _lastFocus = new FocusRequest(berths.Select(s => s.Id).ToArray(), angle, Camera.DesiredPose);
        return true;
    }

    /// <summary>The last focus, so it can be re-fitted when the view is resized (e.g. focus called before the view got its size).</summary>
    private sealed record FocusRequest(string[] BerthIds, CameraAngle? Angle, CameraPose Pose);

    private FocusRequest? _lastFocus;

    /// <summary>Re-fits the last focus for a new viewport size, unless the camera has been moved since.</summary>
    private void RefitFocusAfterResize()
    {
        if (_lastFocus is not { } focus || Camera.DesiredPose != focus.Pose)
        {
            _lastFocus = null;
            return;
        }

        var berths = focus.BerthIds.Select(GetBerth).OfType<Berth>().ToList();
        if (berths.Count == 0)
        {
            _lastFocus = null;
            return;
        }

        var arrived = Camera.Pose == Camera.DesiredPose;
        Camera.SetPose(ComputeFocusPose(berths, focus.Angle), immediate: arrived);
        _lastFocus = focus with { Pose = Camera.DesiredPose };
    }

    /// <summary>Focuses on the current selection. Returns false when nothing is selected.</summary>
    public bool FocusSelection(CameraAngle? angle = null, bool immediate = false) =>
        _selection.Count > 0 && FocusBerths(_selection, angle, immediate);

    /// <summary>The pose <see cref="FocusBerths"/> would move to, without moving the camera.</summary>
    public CameraPose ComputeFocusPose(IReadOnlyCollection<Berth> berths, CameraAngle? angle = null)
    {
        ArgumentNullException.ThrowIfNull(berths);
        if (berths.Count == 0) throw new ArgumentException("At least one berth is required.", nameof(berths));

        var resolved = angle ?? DefaultFocusAngle ?? new CameraAngle(Camera.DesiredPose.YawDegrees, MathF.Max(Camera.DesiredPose.PitchDegrees, 35f));
        var points = FocusPoints(berths);

        var (planMin, planMax) = MarinaLayout.ComputeBounds(berths.Select(s => s.Bounds));
        var target = MarinaMath.ToWorld((planMin + planMax) * 0.5f, berths.Average(s => GroundHeight(s) ?? 0f));
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

    /// <summary>Berth corners on the water (or land) and at the height of their boats (so masts stay in view from oblique angles).</summary>
    private List<Vector3> FocusPoints(IEnumerable<Berth> berths)
    {
        var points = new List<Vector3>();
        foreach (var berth in berths)
        {
            var ground = GroundHeight(berth);
            var top = berth.Boat is { } boat && berth.Status.CanHaveBoat() ? Picking.BerthPlacement.BoatTopHeight(boat, Meshes, ground) : (ground ?? 0f) + 1f;
            foreach (var corner in berth.Bounds.GetCorners())
            {
                points.Add(MarinaMath.ToWorld(corner, ground ?? 0f));
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
    public bool FocusPier(string pierId, bool immediate = false)
    {
        var pier = GetPier(pierId);
        if (pier is null) return false;
        Camera.SetPose(CreatePierPose(pier), immediate);
        return true;
    }

    /// <inheritdoc/>
    public bool FocusPier(string pierId, CameraAngle? angle, bool immediate = false)
    {
        if (GetPier(pierId) is not { } pier) return false;

        var (min, max) = PierBounds(pier);
        var extent = MathF.Max(max.X - min.X, max.Y - min.Y);
        var view = angle ?? DefaultFocusAngle;
        var pose = new CameraPose(
            MarinaMath.ToWorld((min + max) * 0.5f),
            view?.YawDegrees ?? Camera.DesiredPose.YawDegrees,
            view?.PitchDegrees ?? MathF.Max(Camera.DesiredPose.PitchDegrees, 35f),
            FitDistance(extent));

        Camera.SetPose(pose, immediate);
        return true;
    }

    /// <inheritdoc/>
    public CameraPreset SaveCameraPreset(string name, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var preset = new CameraPreset(name.Trim(), Camera.DesiredPose, description);
        AddCameraPreset(preset);
        return preset with { IsBuiltIn = false };
    }

    /// <summary>The pier and everything berthed along it.</summary>
    private (Vector2 Min, Vector2 Max) PierBounds(Pier pier)
    {
        var berths = OrderedBerths().Where(s => IdComparer.Equals(s.PierId, pier.Id)).Select(s => s.Bounds).Append(pier.Bounds);
        return MarinaLayout.ComputeBounds(berths);
    }

    /// <summary>
    /// The stored viewpoint for a pier: the camera stands off the pier's shore end and looks down it, so the whole
    /// pier and its berths recede into the view.
    /// </summary>
    private CameraPose CreatePierPose(Pier pier)
    {
        var (min, max) = PierBounds(pier);
        var center = (min + max) * 0.5f;
        var extent = MathF.Max(max.X - min.X, max.Y - min.Y);

        // Yaw is the compass direction the camera sits in from its target; the shore end is back down the pier.
        var fromTheShore = MarinaMath.DeltaAngle(0f, pier.HeadingDegrees + 180f);
        return new CameraPose(MarinaMath.ToWorld(center), fromTheShore, 38f, MathF.Max(45f, FitDistance(extent) * 0.85f));
    }

    private float FitDistance(float extent) =>
        MathF.Max(40f, extent * 0.5f / MathF.Tan(Camera.FieldOfViewDegrees * MarinaMath.DegToRad * 0.5f) * 1.15f);

    /// <summary>Widens the camera limits to the layout and the designer's reference image (called when the image moves or scales).</summary>
    internal void RefreshCameraBounds() => RebuildBuiltInPresets();

    /// <summary>Regenerates the automatic presets and camera bounds from the current layout (and the reference image, for the bounds).</summary>
    private void RebuildBuiltInPresets()
    {
        var rects = OrderedPiers().Select(d => d.Bounds)
            .Concat(OrderedBerths().Select(s => s.Bounds))
            .Concat(OrderedDividers().Select(d => d.Bounds));
        var (min, max) = MarinaLayout.ComputeBounds(rects, OrderedLandAreas());
        var center = MarinaMath.ToWorld((min + max) * 0.5f);
        var extent = MathF.Max(max.X - min.X, max.Y - min.Y);
        var fit = FitDistance(extent);

        const float margin = 120f;
        var (boundsMin, boundsMax) = (min, max);
        if (Designer?.ReferenceImageBounds is { } image)
        {
            boundsMin = Vector2.Min(boundsMin, image.Min);
            boundsMax = Vector2.Max(boundsMax, image.Max);
        }

        EnsureWaterCovers(boundsMin, boundsMax);
        Camera.Constraints.TargetBoundsMin = boundsMin - new Vector2(margin);
        Camera.Constraints.TargetBoundsMax = boundsMax + new Vector2(margin);
        Camera.Constraints.MaxDistance = MathF.Max(250f, MathF.Max(fit, FitDistance(MathF.Max(boundsMax.X - boundsMin.X, boundsMax.Y - boundsMin.Y))) * 2.5f);
        Camera.FarPlane = MathF.Max(1500f, Camera.Constraints.MaxDistance * 4f);

        // The whole marina, straight down on it, and one from each compass point — all centred on the marina and
        // pulled back far enough to hold it — then one per pier.
        var builtIn = new List<CameraPreset>
        {
            new(OverviewPresetName, new CameraPose(center, 200f, 42f, fit), "The whole marina") { IsBuiltIn = true },
            new(TopDownPresetName, new CameraPose(center, 180f, 89f, fit * 1.05f), "Straight down, north up") { IsBuiltIn = true },
            new("North", new CameraPose(center, 0f, 35f, fit), "From the north") { IsBuiltIn = true },
            new("East", new CameraPose(center, 90f, 35f, fit), "From the east") { IsBuiltIn = true },
            new("South", new CameraPose(center, 180f, 35f, fit), "From the south") { IsBuiltIn = true },
            new("West", new CameraPose(center, 270f, 35f, fit), "From the west") { IsBuiltIn = true },
        };

        foreach (var pier in OrderedPiers())
        {
            builtIn.Add(new CameraPreset($"Pier: {pier.Name}", CreatePierPose(pier), $"Close-up of {pier.Name}") { IsBuiltIn = true });
        }

        var custom = _presets.Where(p => !p.IsBuiltIn).ToList();
        _presets.Clear();
        _presets.AddRange(builtIn.Select(preset => preset with { IsEnabled = !_disabledPresets.Contains(preset.Name) }));
        _presets.AddRange(custom);
    }
}
