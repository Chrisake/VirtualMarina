using System.Collections.ObjectModel;
using System.Numerics;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Api;

public sealed partial class MarinaVisualizer
{
    /// <summary>
    /// Key (and English name) of the built-in whole-marina preset used by <see cref="ResetCamera"/>.
    /// </summary>
    public const string OverviewPresetName = "Overview";

    /// <summary>Key (and English name) of the built-in plan view.</summary>
    public const string TopDownPresetName = "Top Down";

    /// <summary>Keys of the four built-in compass views, which are also their English names.</summary>
    private const string NorthKey = "North", EastKey = "East", SouthKey = "South", WestKey = "West";

    /// <summary>What a built-in pier view's <see cref="CameraPreset.Key"/> starts with; the pier's id follows.</summary>
    private const string PierKeyPrefix = "Pier:";

    /// <summary>
    /// Built-in views the user switched off, by <see cref="CameraPreset.Key"/> (or, for files written before views had
    /// keys, by name until the name is matched to a view). Kept across layout changes and saved with the design.
    /// </summary>
    private readonly HashSet<string> _disabledPresets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The built-in presets are out of date and are worked out again the next time anything asks for them.</summary>
    private bool _presetsDirty = true;

    /// <summary>The camera limits and the water's reach are out of date (see <see cref="EnsureCameraBounds"/>).</summary>
    private bool _boundsDirty = true;

    /// <summary>Guards the rebuild against asking for itself through the camera.</summary>
    private bool _rebuildingView;

    /// <summary>Counts changes to anything the automatic views frame: the piers, berths, dividers and land.</summary>
    private int _geometryVersion;

    /// <summary>The outline of the marina, kept for as long as <see cref="_geometryVersion"/> stays the same.</summary>
    private LayoutFrame? _layoutFrame;

    /// <summary>The list handed out by <see cref="CameraPresets"/>; null when the presets have changed since.</summary>
    private ReadOnlyCollection<CameraPreset>? _presetsSnapshot;

    /// <summary>
    /// <see cref="CameraPresetsChanged"/> has been raised and <see cref="CameraPresets"/> not read since, so another
    /// change needs no second notice.
    /// </summary>
    private bool _presetsChangeUnread;

    /// <summary><see cref="CameraPresetsChanged"/> waits for the end of the current update scope.</summary>
    private bool _presetsChangePending;

    /// <summary>
    /// Raised when <see cref="CameraPresets"/> may have changed: a saved view was added or removed, a view was
    /// switched on or off, or the layout or the size of the view changed and the automatic views have to be worked
    /// out again.
    /// </summary>
    /// <remarks>
    /// Raised once when the list goes out of date, and not again until <see cref="CameraPresets"/> has been read, so a
    /// host that refills its list in the handler hears about every change while one that does not is not flooded
    /// during a window resize. Inside <see cref="BeginUpdate"/> it is raised when the scope ends. The automatic views
    /// are only worked out when they are read (or applied), so this event itself is cheap.
    /// </remarks>
    public event EventHandler? CameraPresetsChanged;

    /// <summary>The <see cref="CameraPreset.Key"/> of the built-in close-up view of a pier.</summary>
    /// <param name="pierId">The pier's id.</param>
    public static string PierPresetKey(string pierId)
    {
        ArgumentNullException.ThrowIfNull(pierId);
        return PierKeyPrefix + pierId;
    }

    /// <inheritdoc/>
    public bool SetCameraPresetEnabled(string presetName, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(presetName);
        EnsurePresets();
        var index = FindBuiltInIndex(presetName);
        if (index < 0) return false;

        var preset = _presets[index];
        var key = preset.Key ?? preset.Name;
        if (enabled)
        {
            _disabledPresets.Remove(key);
            _disabledPresets.Remove(preset.Name);
        }
        else
        {
            _disabledPresets.Add(key);
        }

        if (preset.IsEnabled == enabled) return true;
        _presets[index] = preset with { IsEnabled = enabled };
        OnPresetsListChanged();
        return true;
    }

    /// <summary>The keys of the built-in views that are switched off, for saving with the design.</summary>
    internal IReadOnlyCollection<string> DisabledCameraPresets
    {
        get
        {
            // Matches names from older files to their views' keys before they are written out again.
            EnsurePresets();
            return _disabledPresets;
        }
    }

    /// <summary>Restores which built-in views are switched off, when a design is loaded. Accepts keys or names.</summary>
    internal void RestoreDisabledCameraPresets(IEnumerable<string> names)
    {
        _disabledPresets.Clear();
        foreach (var name in names) _disabledPresets.Add(name);
        InvalidatePresets(force: true);
    }

    /// <summary>
    /// Puts back the saved views and the switched-off built-in views of a design being loaded, in place of the ones
    /// the previous design had, so opening a file never mixes two designs' views.
    /// </summary>
    internal void RestoreCameraPresets(IEnumerable<CameraPreset> saved, IEnumerable<string> disabled)
    {
        _presets.RemoveAll(p => !p.IsBuiltIn);
        foreach (var preset in saved) AddCameraPreset(preset);
        RestoreDisabledCameraPresets(disabled);
    }

    // ---- Hover ----------------------------------------------------------------------------------

    /// <inheritdoc/>
    public Berth? HoveredBerth => _hoveredBerthId is null ? null : GetBerth(_hoveredBerthId);

    private void SetHoveredBerth(Berth? berth)
    {
        if (IdComparer.Equals(_hoveredBerthId, berth?.Id)) return;
        _hoveredBerthId = berth?.Id;
        MarkHighlightDirty();
        BerthHoverChanged?.Invoke(this, new BerthHoverEventArgs(berth));
    }

    // ---- Called by MarinaInputController --------------------------------------------------------

    /// <summary>Disabled berths are never hovered. (The input controller sends pointer movement to the designer instead while it is active.)</summary>
    internal void HandlePointerHover(float x, float y)
    {
        var hit = HitTest(x, y);
        SetHoveredBerth(hit is { } h && GetBerth(h.BerthId) is { IsInteractive: true } berth ? berth : null);
    }

    internal void HandlePointerLeave() => SetHoveredBerth(null);

    // ---- Camera -----------------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// A read-only snapshot: it does not change after it is handed out, and a new one is made when the views change
    /// (see <see cref="CameraPresetsChanged"/>). The automatic views are worked out here, when they are asked for,
    /// rather than on every change to the layout or the size of the view.
    /// </remarks>
    public IReadOnlyList<CameraPreset> CameraPresets
    {
        get
        {
            EnsurePresets();
            _presetsChangeUnread = false;
            return _presetsSnapshot ??= new ReadOnlyCollection<CameraPreset>(_presets.ToArray());
        }
    }

    /// <inheritdoc/>
    public void ResetCamera(bool immediate = false) => Camera.SetPose(ResetPose(), immediate);

    /// <summary>Where <see cref="ResetCamera"/> goes.</summary>
    internal CameraPose ResetPose()
    {
        // The automatic Overview, even when a saved view has been given the same name.
        EnsurePresets();
        var index = FindBuiltInIndex(OverviewPresetName);
        if (index >= 0) return _presets[index].Pose;

        var saved = _presets.Find(p => !p.IsBuiltIn && string.Equals(p.Name, OverviewPresetName, StringComparison.OrdinalIgnoreCase));
        return saved?.Pose ?? new CameraPose(Vector3.Zero, 25f, 45f, 150f);
    }

    /// <summary>
    /// Moves the camera to one of the views worked out from the layout, by key or name, ignoring any saved view that
    /// happens to share the name. Returns false when there is no automatic view called this.
    /// </summary>
    /// <param name="presetName">Key (see <see cref="CameraPreset.Key"/>) or name of an automatic view (case-insensitive).</param>
    /// <param name="immediate">Jump instead of animating.</param>
    public bool ApplyBuiltInCameraPreset(string presetName, bool immediate = false)
    {
        ArgumentNullException.ThrowIfNull(presetName);
        EnsurePresets();
        var index = FindBuiltInIndex(presetName);
        if (index < 0) return false;
        Camera.SetPose(_presets[index].Pose, immediate);
        return true;
    }

    /// <inheritdoc/>
    public bool ApplyCameraPreset(string presetName, bool immediate = false)
    {
        if (presetName is null) return false;
        EnsurePresets();

        // A saved view of the same name wins: someone made it deliberately, over a name the layout generated.
        var preset = _presets.Find(p => !p.IsBuiltIn && string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));
        if (preset is null && FindBuiltInIndex(presetName) is var index and >= 0) preset = _presets[index];

        if (preset is null) return false;
        Camera.SetPose(preset.Pose, immediate);
        return true;
    }

    /// <inheritdoc/>
    public void ApplyCameraPreset(CameraPreset preset, bool immediate = false)
    {
        ArgumentNullException.ThrowIfNull(preset);
        Camera.SetPose(preset.Pose, immediate);
    }

    /// <summary>
    /// Adds a saved view, replacing a saved view of the same name. An automatic view of that name is left alone: the
    /// two live side by side, and a list shows both. Saved views have no <see cref="CameraPreset.Key"/>.
    /// </summary>
    public void AddCameraPreset(CameraPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        _presets.RemoveAll(p => !p.IsBuiltIn && string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        _presets.Add(preset with { IsBuiltIn = false, Key = null });
        OnPresetsListChanged();
    }

    /// <summary>Removes a saved view by name. Automatic views can't be removed. Returns false when nothing was removed.</summary>
    /// <param name="presetName">Preset name (case-insensitive).</param>
    public bool RemoveCameraPreset(string presetName)
    {
        if (_presets.RemoveAll(p => !p.IsBuiltIn && string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase)) == 0) return false;
        OnPresetsListChanged();
        return true;
    }

    /// <summary>
    /// The built-in view with this key, or failing that with this name. Keys never change with the language or a
    /// pier's name, so a host that stores one finds the same view again.
    /// </summary>
    private int FindBuiltInIndex(string keyOrName)
    {
        var index = _presets.FindIndex(p => p.IsBuiltIn && string.Equals(p.Key, keyOrName, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : _presets.FindIndex(p => p.IsBuiltIn && string.Equals(p.Name, keyOrName, StringComparison.OrdinalIgnoreCase));
    }

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
        var pose = Camera.Constrain(new CameraPose(target, resolved.YawDegrees, resolved.PitchDegrees, MathF.Max(MinFocusDistance, FitDistance(extent))));
        return BackOffUntilInside(RefineFocus(pose, points), points);
    }

    /// <summary>Re-centers the projected bounds and scales the distance until they fill the usable area.</summary>
    private CameraPose RefineFocus(CameraPose pose, Vector3[] points)
    {
        var limit = 1f - 2f * _focusMargin; // usable NDC half-extent
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

            var distance = pose.Distance * (halfExtent > 1e-4f ? Math.Clamp(scale, 0.25f, 4f) : 1f);
            pose = Camera.Constrain(pose with { Target = pose.Target + ShiftToCentre(pose, center), Distance = MathF.Max(MinFocusDistance, distance) });
        }

        return pose;
    }

    /// <summary>
    /// Backs off until everything is inside the view (perspective makes the estimate slightly optimistic, and degenerate
    /// viewports can stop the refinement early). Re-centers on every step.
    /// </summary>
    private CameraPose BackOffUntilInside(CameraPose pose, Vector3[] points)
    {
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

            var overflow = MathF.Max(MathF.Max(max.X - min.X, max.Y - min.Y) * 0.5f, 1f);
            pose = Camera.Constrain(pose with { Target = pose.Target + ShiftToCentre(pose, (min + max) * 0.5f), Distance = pose.Distance * MathF.Max(1.05f, overflow) });
        }

        return pose;
    }

    /// <summary>
    /// What the focus has to hold: the convex outline of the berths' corners, each point once on the water (or land)
    /// and once at the height of the tallest boat, so masts stay in view from oblique angles.
    /// </summary>
    /// <remarks>
    /// Projecting the outline instead of every corner of every berth keeps the fit's few hundred projections cheap for
    /// a whole pier's worth of berths; anything inside the outline projects inside it too.
    /// </remarks>
    private Vector3[] FocusPoints(IEnumerable<Berth> berths)
    {
        var low = float.MaxValue;
        var high = float.MinValue;
        var corners = new List<Vector2>();
        foreach (var berth in berths)
        {
            var ground = GroundHeight(berth);
            var top = berth.Boat is { } boat && berth.Status.CanHaveBoat() ? Picking.BerthPlacement.BoatTopHeight(boat, Meshes, ground) : (ground ?? 0f) + 1f;
            low = MathF.Min(low, ground ?? 0f);
            high = MathF.Max(high, top);
            corners.AddRange(berth.Bounds.GetCorners());
        }

        var hull = ConvexHull(corners);
        var points = new Vector3[hull.Count * 2];
        for (var i = 0; i < hull.Count; i++)
        {
            points[2 * i] = MarinaMath.ToWorld(hull[i], low);
            points[(2 * i) + 1] = MarinaMath.ToWorld(hull[i], high);
        }

        return points;
    }

    /// <summary>
    /// How far to move a view's target along the ground for a point now at <paramref name="ndc"/> (normalized device
    /// coordinates) to come to the middle of the picture: sideways by the view's width at that distance, and forward by
    /// its height, stretched by how obliquely the view meets the ground.
    /// </summary>
    private Vector3 ShiftToCentre(CameraPose pose, Vector2 ndc)
    {
        var (tanH, tanV) = TanHalfFov();
        var yaw = pose.YawDegrees * MarinaMath.DegToRad;
        var right = new Vector3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));
        var groundForward = new Vector3(-MathF.Sin(yaw), 0f, -MathF.Cos(yaw));
        var sinPitch = MathF.Max(MathF.Sin(pose.PitchDegrees * MarinaMath.DegToRad), 0.25f);
        return (right * (ndc.X * pose.Distance * tanH)) + (groundForward * (ndc.Y * pose.Distance * tanV / sinPitch));
    }

    private (float TanH, float TanV) TanHalfFov()
    {
        var tanV = MathF.Tan(Camera.FieldOfViewDegrees * MarinaMath.DegToRad * 0.5f);
        return (tanV * (_viewportSize.X / _viewportSize.Y), tanV);
    }

    /// <summary>NDC bounds of <paramref name="points"/> seen from <paramref name="pose"/>. False if any point is behind the camera.</summary>
    private bool TryProjectedBounds(CameraPose pose, IReadOnlyList<Vector3> points, out Vector2 min, out Vector2 max)
    {
        var viewProjection = Camera.GetViewProjectionMatrix(pose, _viewportSize.X / _viewportSize.Y);

        min = new Vector2(float.MaxValue);
        max = new Vector2(float.MinValue);
        for (var i = 0; i < points.Count; i++)
        {
            if (!OrbitCamera.TryProjectToNdc(viewProjection, points[i], out var ndc)) return false;
            min = Vector2.Min(min, ndc);
            max = Vector2.Max(max, ndc);
        }

        return true;
    }

    /// <summary>Moves the camera to the pier's close-up view. False when the pier doesn't exist.</summary>
#pragma warning disable S1133 // Kept, deprecated, until the next major version: removing it would break callers.
    [Obsolete("Use ShowPierCloseUp for the close-up from the pier's shore end, or FocusPier(pierId, angle) to fit the whole pier.")]
    public bool FocusPier(string pierId, bool immediate = false) => ShowPierCloseUp(pierId, immediate);
#pragma warning restore S1133

    /// <inheritdoc/>
    public bool ShowPierCloseUp(string pierId, bool immediate = false)
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

    /// <summary>The pier and everything berthed along it (from the index of berths by pier, not a walk over all of them).</summary>
    private (Vector2 Min, Vector2 Max) PierBounds(Pier pier) =>
        MarinaLayout.ComputeBounds(BerthsOfPier(pier.Id).Select(s => s.Bounds).Append(pier.Bounds));

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

    /// <summary>
    /// How far back a view has to stand, and where it has to aim, for all of <paramref name="corners"/> to be in
    /// frame from a given angle.
    /// </summary>
    /// <remarks>
    /// Worked out from the projection rather than from the extent on the plan: which way round the marina sits
    /// depends on the yaw, how much of its depth survives on the pitch, and how much room there is sideways on the
    /// shape of the window. Foreshortening cannot be approximated either — at a low angle the near edge is much
    /// closer than the middle and projects far larger — so the answer is found by halving the range until the
    /// corners fit.
    /// </remarks>
    /// <param name="corners">World points the view has to hold, usually the outline of the marina.</param>
    /// <param name="center">The middle of the marina, where the search starts from.</param>
    /// <param name="yawDegrees">Which way it faces.</param>
    /// <param name="pitchDegrees">How far down it looks.</param>
    private (Vector3 Target, float Distance) FitView(IReadOnlyList<Vector3> corners, Vector3 center, float yawDegrees, float pitchDegrees)
    {
        // Room left around the marina, as the share of the view it may fill.
        const float limit = 1f / FitMargin;

        bool Fits(float distance, out Vector3 target)
        {
            target = CentredTarget(new CameraPose(center, yawDegrees, pitchDegrees, distance), corners);
            var pose = Camera.Constrain(new CameraPose(target, yawDegrees, pitchDegrees, distance));
            return TryProjectedBounds(pose, corners, out var min, out var max)
                && min.X >= -limit && max.X <= limit && min.Y >= -limit && max.Y <= limit;
        }

        // Further away is always smaller on screen, so the smallest distance that fits can be halved in to.
        var far = MathF.Max(80f, Camera.Constraints.MaxDistance);
        if (!Fits(far, out var best)) return (best, far);

        var near = 20f;
        for (var step = 0; step < 24 && far - near > 0.5f; step++)
        {
            var middle = (near + far) * 0.5f;
            if (Fits(middle, out var aim))
            {
                far = middle;
                best = aim;
            }
            else
            {
                near = middle;
            }
        }

        return (best, far);
    }

    /// <summary>
    /// Where the view has to point for the corners to sit in the middle of the screen from the pose's distance, starting
    /// from the pose's target. Aiming straight at the middle of the marina does not do it: seen from an angle, the middle
    /// of a patch of ground does not land in the middle of the picture, so the marina sits high or low and a fit then has
    /// to back off until the overhanging side comes in — which is how a view that "fits" ended up filling 60% of itself
    /// with water.
    /// </summary>
    private Vector3 CentredTarget(CameraPose start, IReadOnlyList<Vector3> corners)
    {
        var target = start.Target;
        for (var pass = 0; pass < 6; pass++)
        {
            var pose = Camera.Constrain(start with { Target = target });
            if (!TryProjectedBounds(pose, corners, out var min, out var max)) break;

            var middle = (min + max) * 0.5f;
            if (MathF.Abs(middle.X) < 0.002f && MathF.Abs(middle.Y) < 0.002f) break;
            target += ShiftToCentre(pose, middle);
        }

        return target;
    }

    /// <summary>
    /// The convex outline of a cloud of plan points. Everything inside it is inside the hull too, so framing the hull
    /// frames the lot, and a few dozen points is far cheaper to project than a few thousand while a fit halves its
    /// way in.
    /// </summary>
    /// <param name="points">The points to wrap, in plan coordinates.</param>
    /// <returns>The hull, or the distinct points themselves when there are fewer than three.</returns>
    private static List<Vector2> ConvexHull(IEnumerable<Vector2> points)
    {
        var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        if (sorted.Length < 3) return sorted.ToList();

        // Andrew's monotone chain: the lower hull left to right, then the upper hull back again.
        var hull = new List<Vector2>(sorted.Length + 1);
        foreach (var pass in new[] { sorted, sorted.Reverse().ToArray() })
        {
            var start = hull.Count;
            foreach (var point in pass)
            {
                while (hull.Count >= start + 2 && !TurnsLeft(hull[^2], hull[^1], point)) hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }

            hull.RemoveAt(hull.Count - 1);   // the last point of each pass starts the next one
        }

        return hull;
    }

    private static bool TurnsLeft(Vector2 a, Vector2 b, Vector2 c) =>
        ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X)) > 0f;

    /// <summary>Room left around the marina in an automatic view, so it does not sit against the edges.</summary>
    private const float FitMargin = 1.15f;

    /// <summary>
    /// The extent and outline of the marina as the automatic views frame it, for one <see cref="_geometryVersion"/>.
    /// </summary>
    /// <param name="Version">The geometry version it was worked out for.</param>
    /// <param name="Outline">The convex outline of every structure and land area, as world points on the water.</param>
    /// <param name="Min">Plan minimum of everything.</param>
    /// <param name="Max">Plan maximum of everything.</param>
    private sealed record LayoutFrame(int Version, Vector3[] Outline, Vector2 Min, Vector2 Max);

    /// <summary>The marina's outline and extent, worked out once per change to the layout's geometry.</summary>
    private LayoutFrame GetLayoutFrame()
    {
        if (_layoutFrame is { } cached && cached.Version == _geometryVersion) return cached;

        var structures = OrderedPiers().Select(d => d.Bounds)
            .Concat(OrderedBerths().Select(s => s.Bounds))
            .Concat(OrderedDividers().Select(d => d.Bounds))
            .ToArray();

        var (min, max) = MarinaLayout.ComputeBounds(structures, OrderedLandAreas());

        // What the automatic views frame: the whole marina, quays and breakwaters included, as its actual outline
        // rather than the box around it. A box drawn round a marina that bends has corners standing in open water,
        // and centring those leaves everything pushed off to one side of the picture.
        var hull = ConvexHull(structures.SelectMany(rect => rect.GetCorners()).Concat(OrderedLandAreas().SelectMany(land => land.Points)));
        var outline = hull.Count >= 3
            ? hull.Select(point => MarinaMath.ToWorld(point)).ToArray()
            : new[]
            {
                MarinaMath.ToWorld(min),
                MarinaMath.ToWorld(new Vector2(max.X, min.Y)),
                MarinaMath.ToWorld(max),
                MarinaMath.ToWorld(new Vector2(min.X, max.Y)),
            };

        return _layoutFrame = new LayoutFrame(_geometryVersion, outline, min, max);
    }

    /// <summary>Widens the camera limits to the layout and the designer's reference image (called when the image moves or scales).</summary>
    /// <remarks>Only the limits and the water: the automatic views frame the marina, not the image.</remarks>
    internal void RefreshCameraBounds()
    {
        _boundsDirty = true;
        EnsureCameraBounds();
        RequestRedraw();
    }

    /// <summary>
    /// Something the automatic views frame changed (a pier, berth, divider or land area was added, moved or removed):
    /// the views, the camera limits and the water's reach are worked out again when next needed.
    /// </summary>
    private void InvalidateLayoutGeometry()
    {
        _geometryVersion++;
        _boundsDirty = true;
        InvalidatePresets();
    }

    /// <summary>Marks the built-in presets out of date (the layout or the view's size changed) and says so.</summary>
    /// <param name="force">Raise <see cref="CameraPresetsChanged"/> even when they were already out of date.</param>
    private void InvalidatePresets(bool force = false)
    {
        if (_presetsDirty && !force) return;
        _presetsDirty = true;
        OnPresetsListChanged();
    }

    /// <summary>The list <see cref="CameraPresets"/> hands out is out of date: drops it and raises <see cref="CameraPresetsChanged"/>.</summary>
    private void OnPresetsListChanged()
    {
        _presetsSnapshot = null;
        if (_presetsChangeUnread) return;
        if (_updateDepth > 0)
        {
            _presetsChangePending = true;
            return;
        }

        _presetsChangeUnread = true;
        CameraPresetsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raises the <see cref="CameraPresetsChanged"/> held back by an update scope that just ended.</summary>
    private void FlushPresetsChanged()
    {
        if (!_presetsChangePending) return;
        _presetsChangePending = false;
        if (_presetsChangeUnread) return;
        _presetsChangeUnread = true;
        CameraPresetsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Brings the camera limits and the water grid up to date with the layout and the reference image, if either
    /// changed since. Cheap when nothing did; called whenever the camera is used and before every frame.
    /// </summary>
    private void EnsureCameraBounds()
    {
        if (!_boundsDirty || _rebuildingView) return;
        _rebuildingView = true;
        try
        {
            _boundsDirty = false;
            var frame = GetLayoutFrame();
            var fit = FitDistance(MathF.Max(frame.Max.X - frame.Min.X, frame.Max.Y - frame.Min.Y));

            const float margin = 120f;
            var (boundsMin, boundsMax) = (frame.Min, frame.Max);
            if (Designer?.ReferenceImageBounds is { } image)
            {
                boundsMin = Vector2.Min(boundsMin, image.Min);
                boundsMax = Vector2.Max(boundsMax, image.Max);
            }

            EnsureWaterCovers(boundsMin, boundsMax);
            var constraints = _camera.Constraints;
            constraints.TargetBoundsMin = boundsMin - new Vector2(margin);
            constraints.TargetBoundsMax = boundsMax + new Vector2(margin);
            constraints.MaxDistance = MathF.Max(250f, MathF.Max(fit, FitDistance(MathF.Max(boundsMax.X - boundsMin.X, boundsMax.Y - boundsMin.Y))) * 2.5f);
            _camera.FarPlane = MathF.Max(1500f, constraints.MaxDistance * 4f);
        }
        finally
        {
            _rebuildingView = false;
        }
    }

    /// <summary>Works the built-in presets out again if the layout or the view's size changed since they last were.</summary>
    private void EnsurePresets()
    {
        if (!_presetsDirty || _rebuildingView) return;
        EnsureCameraBounds();
        _rebuildingView = true;
        try
        {
            _presetsDirty = false;
            RebuildBuiltInPresets();
        }
        finally
        {
            _rebuildingView = false;
        }
    }

    /// <summary>How many times the automatic presets have been worked out, for tests that check it happens only when needed.</summary>
    internal int PresetRebuildCount { get; private set; }

    /// <summary>Regenerates the automatic presets from the current layout and view size.</summary>
    private void RebuildBuiltInPresets()
    {
        PresetRebuildCount++;
        var frame = GetLayoutFrame();
        var center = MarinaMath.ToWorld((frame.Min + frame.Max) * 0.5f);

        // The whole marina, straight down on it, and one from each compass point — all centred on the marina and
        // pulled back far enough to hold it — then one per pier.
        // Each one is pulled back far enough for the marina to fit from its own angle, rather than all sharing a
        // distance worked out without reference to where they stand.
        CameraPreset Fitted(string key, string name, float yaw, float pitch, string description)
        {
            var (target, distance) = FitView(frame.Outline, center, yaw, pitch);
            return new CameraPreset(name, new CameraPose(target, yaw, pitch, distance), description) { IsBuiltIn = true, Key = key };
        }

        // Yaw is where the camera stands, not where it looks: 0 puts it on the +Z side, which is south, and 180
        // puts it north. So a view "from the north" is 180, and a top-down view with north at the top of the
        // screen is 0 — the camera standing south of the marina, looking north up the page.
        var builtIn = new List<CameraPreset>(6 + _piers.Count)
        {
            Fitted(OverviewPresetName, Strings.CameraPresetOverview, 200f, 42f, Strings.CameraPresetOverviewDescription),
            Fitted(TopDownPresetName, Strings.CameraPresetTopDown, 0f, 89f, Strings.CameraPresetTopDownDescription),
            Fitted(NorthKey, Strings.CameraPresetNorth, 180f, 35f, Strings.CameraPresetNorthDescription),
            Fitted(EastKey, Strings.CameraPresetEast, 90f, 35f, Strings.CameraPresetEastDescription),
            Fitted(SouthKey, Strings.CameraPresetSouth, 0f, 35f, Strings.CameraPresetSouthDescription),
            Fitted(WestKey, Strings.CameraPresetWest, 270f, 35f, Strings.CameraPresetWestDescription),
        };

        // Two piers may share a name; their views may not, or a list offering them by name could only ever reach one.
        var names = new HashSet<string>(builtIn.Select(preset => preset.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var pier in OrderedPiers())
        {
            var name = Strings.Format(Strings.CameraPresetPier, pier.Name);
            if (!names.Add(name))
            {
                name = Strings.Format(Strings.CameraPresetPierWithId, pier.Name, pier.Id);
                names.Add(name);
            }

            var description = Strings.Format(Strings.CameraPresetPierDescription, pier.Name);
            builtIn.Add(new CameraPreset(name, CreatePierPose(pier), description) { IsBuiltIn = true, Key = PierPresetKey(pier.Id) });
        }

        var custom = _presets.Where(p => !p.IsBuiltIn).ToList();
        _presets.Clear();
        foreach (var preset in builtIn)
        {
            // A view switched off in a file from before views had keys is remembered by its name; from now on by its key.
            var key = preset.Key!;
            if (!_disabledPresets.Contains(key) && _disabledPresets.Remove(preset.Name)) _disabledPresets.Add(key);
            _presets.Add(preset with { IsEnabled = !_disabledPresets.Contains(key) });
        }

        _presets.AddRange(custom);
        _presetsSnapshot = null;
    }
}
