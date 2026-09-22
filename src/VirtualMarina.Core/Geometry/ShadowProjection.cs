using System.Numerics;

namespace VirtualMarina.Core.Geometry;

/// <summary>
/// Flattens geometry onto a horizontal plane along the sun's rays, which is how the marina casts its shadows.
/// </summary>
/// <remarks>
/// <para>
/// A marina is almost all flat ground: water at nought, quays and yards a meter or two above it. Squashing the boats
/// and the piers onto that ground and drawing them dark is enough to read as sunlight, and it costs one extra
/// instance per object rather than a depth pass and a shadow map in every backend.
/// </para>
/// <para>
/// What it does not do: a shadow lands on one plane, so a boat's shadow falls on the water rather than up the side of
/// the pier beside it, and nothing shadows itself. The ground casts none — it is what the shadows land on — which is
/// why the trees and the hinterland are meshes of their own rather than part of it.
/// </para>
/// </remarks>
public static class ShadowProjection
{
    /// <summary>
    /// How low the sun may be before shadows are dropped, as the Y of its unit direction (about 4° above the horizon).
    /// </summary>
    /// <remarks>
    /// At the horizon a shadow stretches to infinity, which is both useless and a good way to send vertices to the
    /// other side of the world. Below this the marina is simply drawn without shadows.
    /// </remarks>
    public const float MinimumSunHeight = 0.07f;

    /// <summary>True when the sun is high enough for a shadow to be worth drawing.</summary>
    /// <param name="sunDirection">Direction toward the sun (<c>LightingSettings.SunDirection</c>).</param>
    public static bool CanCast(Vector3 sunDirection) =>
        float.IsFinite(sunDirection.Y) && sunDirection.Y >= MinimumSunHeight;

    /// <summary>
    /// The transform that drops a point straight down the sun's rays onto a horizontal plane.
    /// </summary>
    /// <param name="sunDirection">Direction toward the sun; its length does not matter.</param>
    /// <param name="planeHeight">World Y of the ground the shadow lands on.</param>
    /// <returns>
    /// A matrix to apply after an object's own world transform. Everything it touches ends up on the plane, so the
    /// result is flat and must be drawn blended rather than depth-written.
    /// </returns>
    /// <example>
    /// <code>
    /// var flatten = ShadowProjection.OntoPlane(marina.Lighting.SunDirection, 0f);
    /// var shadow = boat with { World = boat.World * flatten, Tint = new Vector4(0, 0, 0, 0.25f) };
    /// </code>
    /// </example>
    public static Matrix4x4 OntoPlane(Vector3 sunDirection, float planeHeight)
    {
        var sun = sunDirection.LengthSquared() > 1e-8f ? Vector3.Normalize(sunDirection) : Vector3.UnitY;
        var height = MathF.Max(sun.Y, MinimumSunHeight);

        // A point P lands at P - sun * (P.y - planeHeight) / sun.y: walk back down the ray until it meets the plane.
        var slideX = sun.X / height;
        var slideZ = sun.Z / height;

        // Row-vector convention (v * M), so the second row carries what P.y contributes to x and z.
        var matrix = Matrix4x4.Identity;
        matrix.M21 = -slideX;
        matrix.M22 = 0f;
        matrix.M23 = -slideZ;
        matrix.M41 = slideX * planeHeight;
        matrix.M42 = planeHeight;
        matrix.M43 = slideZ * planeHeight;
        return matrix;
    }
}
