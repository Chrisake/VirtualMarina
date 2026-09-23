using System.Numerics;

namespace VirtualMarina.Core.Mathematics;

/// <summary>
/// Math helpers shared by the domain, camera, picking and scene code.
/// </summary>
/// <remarks>
/// World space is right-handed and Y-up. The water surface is the plane Y = 0.
/// Plan-view (2D) positions use <see cref="Vector2"/> where X = world X and Y = world Z.
/// Headings are in degrees, rotating about +Y: 0° points along +Z, 90° points along +X.
/// </remarks>
public static class MarinaMath
{
    /// <summary>Multiply degrees by this to get radians.</summary>
    public const float DegToRad = MathF.PI / 180f;

    /// <summary>Multiply radians by this to get degrees.</summary>
    public const float RadToDeg = 180f / MathF.PI;

    /// <summary>Unit plan-view direction for a heading.</summary>
    public static Vector2 HeadingToDirection(float headingDegrees)
    {
        var r = headingDegrees * DegToRad;
        return new Vector2(MathF.Sin(r), MathF.Cos(r));
    }

    /// <summary>
    /// Unit plan-view vector for heading + 90°: <c>(cos h, −sin h)</c>, the local +X axis of placements made with
    /// <see cref="CreatePlacement"/>. For heading 0° (+Z) it is +X.
    /// </summary>
    public static Vector2 HeadingToRight(float headingDegrees)
    {
        var r = headingDegrees * DegToRad;
        return new Vector2(MathF.Cos(r), -MathF.Sin(r));
    }

    /// <summary>Heading in degrees (−180..180) of a plan-view direction; the inverse of <see cref="HeadingToDirection"/>.</summary>
    public static float DirectionToHeading(Vector2 direction) =>
        MathF.Atan2(direction.X, direction.Y) * RadToDeg;

    /// <summary>Shortest signed angle (degrees) that rotates <paramref name="from"/> onto <paramref name="to"/>.</summary>
    public static float DeltaAngle(float from, float to)
    {
        var d = (to - from) % 360f;
        if (d > 180f) d -= 360f;
        if (d < -180f) d += 360f;
        return d;
    }

    /// <summary>Plan position (X, Z) to a world point at height <paramref name="y"/>.</summary>
    public static Vector3 ToWorld(Vector2 planPosition, float y = 0f) => new(planPosition.X, y, planPosition.Y);

    /// <summary>World point to plan position (X, Z), dropping the height.</summary>
    public static Vector2 ToPlan(Vector3 world) => new(world.X, world.Z);

    /// <summary>
    /// OpenGL-style perspective projection (clip Z in [-1, 1]) in System.Numerics row-vector convention.
    /// Its 16 fields, uploaded in M11..M44 order, form the column-major matrix GLSL expects.
    /// </summary>
    public static Matrix4x4 CreatePerspectiveGL(float fieldOfViewRadians, float aspect, float near, float far)
    {
        var f = 1f / MathF.Tan(fieldOfViewRadians * 0.5f);
        return new Matrix4x4(
            f / aspect, 0f, 0f, 0f,
            0f, f, 0f, 0f,
            0f, 0f, (far + near) / (near - far), -1f,
            0f, 0f, 2f * far * near / (near - far), 0f);
    }

    /// <summary>Scale, then rotate about Y by the heading, then translate.</summary>
    public static Matrix4x4 CreatePlacement(Vector3 scale, float headingDegrees, Vector3 translation) =>
        Matrix4x4.CreateScale(scale) *
        Matrix4x4.CreateRotationY(headingDegrees * DegToRad) *
        Matrix4x4.CreateTranslation(translation);

    /// <summary>Deterministic hash of a string mapped to [0, 1). Stable across processes (unlike string.GetHashCode).</summary>
    public static float StableHash01(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        unchecked
        {
            uint hash = 2166136261;
            foreach (var c in value)
            {
                hash ^= c;
                hash *= 16777619;
            }

            return (hash % 10007) / 10007f;
        }
    }

    /// <summary>Clamps to 0..1.</summary>
    public static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
}
