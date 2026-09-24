using System.Numerics;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Picking;

namespace VirtualMarina.Core.Camera;

/// <summary>
/// Smoothed orbit/pan/zoom camera. Input changes the <see cref="DesiredPose"/>; <see cref="Update"/>
/// eases the current <see cref="Pose"/> toward it frame by frame.
/// </summary>
public sealed class OrbitCamera
{
    private CameraPose _current = new(Vector3.Zero, 25f, 45f, 150f);
    private CameraPose _desired = new(Vector3.Zero, 25f, 45f, 150f);

    /// <summary>Limits on pitch, distance, eye height and target area. The visualizer updates the distance and target limits when the layout changes.</summary>
    public CameraConstraints Constraints { get; } = new();

    /// <summary>Vertical field of view in degrees (default 45).</summary>
    public float FieldOfViewDegrees { get; set; } = 45f;

    /// <summary>Near clipping plane in meters.</summary>
    public float NearPlane { get; set; } = 0.5f;

    /// <summary>Far clipping plane in meters (raised automatically for large marinas).</summary>
    public float FarPlane { get; set; } = 3000f;

    /// <summary>Exponential smoothing rate (1/s). Higher is snappier; 0 or less disables smoothing.</summary>
    public float Smoothing { get; set; } = 10f;

    /// <summary>The pose currently on screen.</summary>
    public CameraPose Pose => _current;

    /// <summary>The pose the camera is easing toward.</summary>
    public CameraPose DesiredPose => _desired;

    /// <summary>World-space eye position of the current pose.</summary>
    public Vector3 Position => ComputeEye(_current);

    /// <summary>True while the camera is still easing toward <see cref="DesiredPose"/>.</summary>
    public bool IsMoving =>
        Vector3.DistanceSquared(_current.Target, _desired.Target) > 1e-4f ||
        MathF.Abs(_current.YawDegrees - _desired.YawDegrees) > 0.01f ||
        MathF.Abs(_current.PitchDegrees - _desired.PitchDegrees) > 0.01f ||
        MathF.Abs(_current.Distance - _desired.Distance) > 0.01f;

    /// <summary>World-space eye position for a pose.</summary>
    public static Vector3 ComputeEye(CameraPose pose)
    {
        var yaw = pose.YawDegrees * MarinaMath.DegToRad;
        var pitch = pose.PitchDegrees * MarinaMath.DegToRad;
        var offset = new Vector3(
            MathF.Cos(pitch) * MathF.Sin(yaw),
            MathF.Sin(pitch),
            MathF.Cos(pitch) * MathF.Cos(yaw));
        return pose.Target + offset * pose.Distance;
    }

    /// <summary>Moves to a pose, animating unless <paramref name="immediate"/> is set. Yaw takes the shortest way round.</summary>
    public void SetPose(CameraPose pose, bool immediate = false)
    {
        var constrained = Settle(pose);
        _desired = constrained;
        if (immediate) _current = constrained;
    }

    /// <summary>True when <see cref="SetPose"/> with this pose would change where the camera is headed.</summary>
    internal bool WouldMoveTo(CameraPose pose) => Settle(pose) != _desired;

    /// <summary>The pose <see cref="SetPose"/> heads for: constrained, with the yaw taking the shortest way round.</summary>
    private CameraPose Settle(CameraPose pose)
    {
        var constrained = Constrain(pose);
        return constrained with
        {
            YawDegrees = _current.YawDegrees + MarinaMath.DeltaAngle(_current.YawDegrees, constrained.YawDegrees),
        };
    }

    /// <summary>Rotates the desired pose around its target (pitch is clamped by <see cref="Constraints"/>).</summary>
    /// <param name="deltaYawDegrees">Change of compass angle.</param>
    /// <param name="deltaPitchDegrees">Change of elevation (positive looks more steeply down).</param>
    public void Orbit(float deltaYawDegrees, float deltaPitchDegrees)
    {
        _desired = Constrain(_desired with
        {
            YawDegrees = _desired.YawDegrees + deltaYawDegrees,
            PitchDegrees = _desired.PitchDegrees + deltaPitchDegrees,
        });
    }

    /// <summary>
    /// Drags the scene by a screen-space delta so the ground at the middle of the view follows the pointer. Away from
    /// the middle of an oblique view the ground moves a little faster or slower than the pointer; use
    /// <see cref="Pan(Vector2, Vector2, float, float)"/> when the pointer position is known.
    /// </summary>
    public void Pan(float deltaXPixels, float deltaYPixels, float viewportHeightPixels)
    {
        if (!(viewportHeightPixels > 0f) || !float.IsFinite(deltaXPixels) || !float.IsFinite(deltaYPixels)) return;

        var metersPerPixel = 2f * _desired.Distance * MathF.Tan(FieldOfViewDegrees * MarinaMath.DegToRad * 0.5f) / viewportHeightPixels;
        var yaw = _desired.YawDegrees * MarinaMath.DegToRad;
        var right = new Vector3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));
        var groundForward = new Vector3(-MathF.Sin(yaw), 0f, -MathF.Cos(yaw));

        // A pixel up the screen covers more ground the flatter the view looks across it: 1 / sin(pitch) more.
        var sinPitch = MathF.Max(MathF.Sin(_desired.PitchDegrees * MarinaMath.DegToRad), MinPanSinPitch);
        var move = ((-right * deltaXPixels) + (groundForward * (deltaYPixels / sinPitch))) * metersPerPixel;
        _desired = Constrain(_desired with { Target = _desired.Target + move });
    }

    /// <summary>
    /// Grab-pans: moves the view so the point of the ground (the horizontal plane through the target) that was under
    /// <paramref name="fromPixel"/> ends up under <paramref name="toPixel"/>, wherever in an oblique view it is.
    /// </summary>
    /// <remarks>
    /// Where either pointer ray runs above the horizon and never meets the ground, falls back to
    /// <see cref="Pan(float, float, float)"/>. The move is capped at a few view distances so a pointer skimming the
    /// horizon cannot fling the camera away.
    /// </remarks>
    /// <param name="fromPixel">Pointer position before the move, in view pixels (origin top-left).</param>
    /// <param name="toPixel">Pointer position after the move.</param>
    /// <param name="viewportWidth">View width in pixels.</param>
    /// <param name="viewportHeight">View height in pixels.</param>
    public void Pan(Vector2 fromPixel, Vector2 toPixel, float viewportWidth, float viewportHeight)
    {
        if (!(viewportWidth > 0f && viewportHeight > 0f) || fromPixel == toPixel) return;

        var height = _desired.Target.Y;
        var from = RayThrough(_desired, fromPixel.X, fromPixel.Y, viewportWidth, viewportHeight);
        var to = RayThrough(_desired, toPixel.X, toPixel.Y, viewportWidth, viewportHeight);
        if (from.IntersectHorizontalPlane(height, out var fromDistance) && to.IntersectHorizontalPlane(height, out var toDistance))
        {
            var move = from.GetPoint(fromDistance) - to.GetPoint(toDistance);
            move.Y = 0f;
            var cap = _desired.Distance * 4f;
            if (move.Length() > cap) move = Vector3.Normalize(move) * cap;
            if (float.IsFinite(move.X) && float.IsFinite(move.Z))
            {
                _desired = Constrain(_desired with { Target = _desired.Target + move });
                return;
            }
        }

        var delta = toPixel - fromPixel;
        Pan(delta.X, delta.Y, viewportHeight);
    }

    /// <summary>Moves the target along the ground in the camera's own orientation (meters).</summary>
    public void PanWorld(float rightMeters, float forwardMeters)
    {
        var yaw = _desired.YawDegrees * MarinaMath.DegToRad;
        var right = new Vector3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));
        var groundForward = new Vector3(-MathF.Sin(yaw), 0f, -MathF.Cos(yaw));
        _desired = Constrain(_desired with { Target = _desired.Target + right * rightMeters + groundForward * forwardMeters });
    }

    /// <summary>
    /// Zooms by a factor (&gt;1 moves closer). With a focus point, the view zooms toward it
    /// (e.g. the spot under the mouse cursor).
    /// </summary>
    public void Zoom(float factor, Vector3? focusPoint = null)
    {
        if (factor <= 0f || !float.IsFinite(factor)) return;

        var oldDistance = _desired.Distance;
        var (minDistance, maxDistance) = DistanceRange(Constraints);
        var newDistance = Math.Clamp(oldDistance / factor, minDistance, maxDistance);
        var target = _desired.Target;
        if (focusPoint.HasValue && oldDistance > 0f)
        {
            var moveFraction = 1f - newDistance / oldDistance;
            target += (focusPoint.Value - target) * moveFraction;
        }

        _desired = Constrain(_desired with { Distance = newDistance, Target = target });
    }

    /// <summary>Eases the current pose toward the desired pose. Called once per frame by the visualizer.</summary>
    /// <param name="deltaSeconds">Time since the previous frame.</param>
    public void Update(float deltaSeconds)
    {
        if (!(deltaSeconds > 0f)) return;

        if (Smoothing <= 0f)
        {
            _current = _desired;
            return;
        }

        var t = 1f - MathF.Exp(-Smoothing * deltaSeconds);
        _current = new CameraPose(
            Vector3.Lerp(_current.Target, _desired.Target, t),
            _current.YawDegrees + (_desired.YawDegrees - _current.YawDegrees) * t,
            _current.PitchDegrees + (_desired.PitchDegrees - _current.PitchDegrees) * t,
            MathF.Exp(float.Lerp(MathF.Log(_current.Distance), MathF.Log(_desired.Distance), t)));

        if (!IsMoving) _current = _desired;
    }

    /// <summary>View matrix of the current pose (System.Numerics row-vector convention).</summary>
    public Matrix4x4 GetViewMatrix() => Matrix4x4.CreateLookAt(Position, _current.Target, Vector3.UnitY);

    /// <summary>OpenGL-style perspective projection (clip Z in −1..1) for the given width/height ratio.</summary>
    public Matrix4x4 GetProjectionMatrix(float aspectRatio) =>
        MarinaMath.CreatePerspectiveGL(
            FieldOfViewDegrees * MarinaMath.DegToRad,
            aspectRatio > 0f && float.IsFinite(aspectRatio) ? aspectRatio : 1f,
            NearPlane,
            FarPlane);

    /// <summary>World-space ray through a pixel (origin top-left) of the current view.</summary>
    public Ray ScreenPointToRay(float x, float y, float viewportWidth, float viewportHeight) =>
        RayThrough(_current, x, y, viewportWidth, viewportHeight);

    /// <summary>View and projection of <paramref name="pose"/> combined, with this camera's lens and clipping planes.</summary>
    internal Matrix4x4 GetViewProjectionMatrix(CameraPose pose, float aspectRatio) =>
        Matrix4x4.CreateLookAt(ComputeEye(pose), pose.Target, Vector3.UnitY) * GetProjectionMatrix(aspectRatio);

    /// <summary>
    /// Projects a world point with a combined view-projection matrix to normalized device coordinates (−1..1 across
    /// the view). False when the point is behind the camera, or so close to its plane that the projection means nothing.
    /// </summary>
    internal static bool TryProjectToNdc(in Matrix4x4 viewProjection, Vector3 world, out Vector2 ndc)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
        if (clip.W <= MinClipW)
        {
            ndc = default;
            return false;
        }

        ndc = new Vector2(clip.X, clip.Y) / clip.W;
        return true;
    }

    /// <summary>Smallest clip-space W a point may have and still count as in front of the camera.</summary>
    private const float MinClipW = 1e-4f;

    /// <summary>Smallest sine of the pitch a pan divides by, so a view skimming the water does not pan to the horizon.</summary>
    private const float MinPanSinPitch = 0.25f;

    /// <summary>Closest the camera may ever be to its target, whatever the constraints say: the smoothing works on the logarithm of the distance.</summary>
    private const float MinimumDistance = 0.01f;

    private Ray RayThrough(CameraPose pose, float x, float y, float viewportWidth, float viewportHeight)
    {
        var aspect = viewportHeight > 0f ? viewportWidth / viewportHeight : 1f;
        var viewProjection = GetViewProjectionMatrix(pose, aspect);
        var eye = ComputeEye(pose);
        if (!Matrix4x4.Invert(viewProjection, out var inverse))
        {
            return new Ray(eye, Vector3.Normalize(pose.Target - eye));
        }

        var ndcX = viewportWidth > 0f ? 2f * x / viewportWidth - 1f : 0f;
        var ndcY = viewportHeight > 0f ? 1f - 2f * y / viewportHeight : 0f;
        var near = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), inverse);
        var far = Vector4.Transform(new Vector4(ndcX, ndcY, 1f, 1f), inverse);
        var nearPoint = new Vector3(near.X, near.Y, near.Z) / near.W;
        var farPoint = new Vector3(far.X, far.Y, far.Z) / far.W;
        return new Ray(nearPoint, Vector3.Normalize(farPoint - nearPoint));
    }

    /// <summary>Projects a world position to view pixels (origin top-left); null when behind the camera.</summary>
    public Vector2? WorldToScreen(Vector3 world, float viewportWidth, float viewportHeight)
    {
        if (!(viewportWidth > 0f && viewportHeight > 0f)) return null;
        if (!TryProjectToNdc(GetViewProjectionMatrix(_current, viewportWidth / viewportHeight), world, out var ndc)) return null;
        return new Vector2((ndc.X + 1f) * 0.5f * viewportWidth, (1f - ndc.Y) * 0.5f * viewportHeight);
    }

    /// <summary>Applies <see cref="Constraints"/> to a pose.</summary>
    /// <remarks>
    /// Never throws, whatever the constraints hold: limits given the wrong way round are taken the right way round,
    /// limits that are not numbers are ignored, and the distance never drops below a hair above zero.
    /// </remarks>
    public CameraPose Constrain(CameraPose pose)
    {
        var c = Constraints;
        var (minDistance, maxDistance) = DistanceRange(c);
        var distance = Math.Clamp(float.IsFinite(pose.Distance) ? pose.Distance : maxDistance, minDistance, maxDistance);
        var maxPitch = MathF.Min(Finite(c.MaxPitchDegrees, 89f), 89.5f);
        var pitch = Math.Clamp(float.IsFinite(pose.PitchDegrees) ? pose.PitchDegrees : 45f, MathF.Min(Finite(c.MinPitchDegrees, 8f), maxPitch), maxPitch);

        // Keep the eye above the water: sin(pitch) * distance >= MinEyeHeight.
        var minPitchForHeight = MathF.Asin(Math.Clamp(Finite(c.MinEyeHeight, 0f) / distance, 0f, 1f)) * MarinaMath.RadToDeg;
        pitch = MathF.Min(MathF.Max(pitch, minPitchForHeight), maxPitch);

        // Math.Clamp returns NaN for a NaN input, so the finiteness check has to come first; without it
        // a NaN target reaches the view matrix and the whole frame renders as nothing, silently.
        var target = pose.Target;
        if (!float.IsFinite(target.X) || !float.IsFinite(target.Y) || !float.IsFinite(target.Z)) target = Vector3.Zero;
        target = new Vector3(
            SafeClamp(target.X, c.TargetBoundsMin.X, c.TargetBoundsMax.X),
            SafeClamp(target.Y, c.MinTargetHeight, c.MaxTargetHeight),
            SafeClamp(target.Z, c.TargetBoundsMin.Y, c.TargetBoundsMax.Y));

        var yaw = float.IsFinite(pose.YawDegrees) ? pose.YawDegrees : 0f;
        return new CameraPose(target, yaw, pitch, distance);
    }

    /// <summary>The distance limits, the right way round, finite and above zero.</summary>
    private static (float Min, float Max) DistanceRange(CameraConstraints c)
    {
        var min = MathF.Max(Finite(c.MinDistance, MinimumDistance), MinimumDistance);
        // A maximum set below the minimum yields to it rather than making Math.Clamp throw.
        var max = MathF.Max(min, Finite(c.MaxDistance, float.MaxValue));
        return (min, max);
    }

    /// <summary>Clamps to the range between two limits, whichever order they come in; a limit that is not a number is no limit.</summary>
    private static float SafeClamp(float value, float a, float b)
    {
        var low = float.IsNaN(a) ? float.NegativeInfinity : a;
        var high = float.IsNaN(b) ? float.PositiveInfinity : b;
        if (low > high) (low, high) = (high, low);
        return Math.Clamp(value, low, high);
    }

    private static float Finite(float value, float fallback) => float.IsFinite(value) ? value : fallback;
}
