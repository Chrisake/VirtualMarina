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
        var constrained = Constrain(pose);
        constrained = constrained with
        {
            YawDegrees = _current.YawDegrees + MarinaMath.DeltaAngle(_current.YawDegrees, constrained.YawDegrees),
        };
        _desired = constrained;
        if (immediate) _current = constrained;
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

    /// <summary>Drags the scene by a screen-space delta so the ground under the pointer follows it.</summary>
    public void Pan(float deltaXPixels, float deltaYPixels, float viewportHeightPixels)
    {
        if (viewportHeightPixels <= 0f) return;

        var metersPerPixel = 2f * _desired.Distance * MathF.Tan(FieldOfViewDegrees * MarinaMath.DegToRad * 0.5f) / viewportHeightPixels;
        var yaw = _desired.YawDegrees * MarinaMath.DegToRad;
        var right = new Vector3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));
        var groundForward = new Vector3(-MathF.Sin(yaw), 0f, -MathF.Cos(yaw));
        var move = (-right * deltaXPixels + groundForward * deltaYPixels) * metersPerPixel;
        _desired = Constrain(_desired with { Target = _desired.Target + move });
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
        var newDistance = Math.Clamp(oldDistance / factor, Constraints.MinDistance, Constraints.MaxDistance);
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
        if (deltaSeconds <= 0f) return;

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
    public Ray ScreenPointToRay(float x, float y, float viewportWidth, float viewportHeight)
    {
        var aspect = viewportHeight > 0f ? viewportWidth / viewportHeight : 1f;
        var viewProjection = GetViewMatrix() * GetProjectionMatrix(aspect);
        if (!Matrix4x4.Invert(viewProjection, out var inverse))
        {
            return new Ray(Position, Vector3.Normalize(_current.Target - Position));
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
        if (viewportWidth <= 0f || viewportHeight <= 0f) return null;
        var viewProjection = GetViewMatrix() * GetProjectionMatrix(viewportWidth / viewportHeight);
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
        if (clip.W <= 1e-6f) return null;
        return new Vector2(
            (clip.X / clip.W + 1f) * 0.5f * viewportWidth,
            (1f - clip.Y / clip.W) * 0.5f * viewportHeight);
    }

    /// <summary>Applies <see cref="Constraints"/> to a pose.</summary>
    public CameraPose Constrain(CameraPose pose)
    {
        var c = Constraints;
        var distance = Math.Clamp(float.IsFinite(pose.Distance) ? pose.Distance : c.MaxDistance, c.MinDistance, MathF.Max(c.MinDistance, c.MaxDistance));
        var maxPitch = MathF.Min(c.MaxPitchDegrees, 89.5f);
        var pitch = Math.Clamp(float.IsFinite(pose.PitchDegrees) ? pose.PitchDegrees : 45f, MathF.Min(c.MinPitchDegrees, maxPitch), maxPitch);

        // Keep the eye above the water: sin(pitch) * distance >= MinEyeHeight.
        var minPitchForHeight = MathF.Asin(Math.Clamp(c.MinEyeHeight / distance, 0f, 1f)) * MarinaMath.RadToDeg;
        pitch = MathF.Min(MathF.Max(pitch, minPitchForHeight), maxPitch);

        var target = pose.Target;
        target = new Vector3(
            Math.Clamp(target.X, c.TargetBoundsMin.X, c.TargetBoundsMax.X),
            Math.Clamp(target.Y, 0f, 50f),
            Math.Clamp(target.Z, c.TargetBoundsMin.Y, c.TargetBoundsMax.Y));

        var yaw = float.IsFinite(pose.YawDegrees) ? pose.YawDegrees : 0f;
        return new CameraPose(target, yaw, pitch, distance);
    }
}
