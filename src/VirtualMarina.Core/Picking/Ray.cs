using System.Numerics;

namespace VirtualMarina.Core.Picking;

/// <summary>A half-line in world space, e.g. from the camera through a pixel (<c>OrbitCamera.ScreenPointToRay</c>).</summary>
/// <param name="Origin">Start point.</param>
/// <param name="Direction">Direction (normalized when produced by the camera).</param>
public readonly record struct Ray(Vector3 Origin, Vector3 Direction)
{
    /// <summary>The point at <paramref name="distance"/> along the ray.</summary>
    public Vector3 GetPoint(float distance) => Origin + Direction * distance;

    /// <summary>Intersects the horizontal plane at <paramref name="height"/>, in front of the origin only.</summary>
    /// <param name="height">World Y of the plane.</param>
    /// <param name="distance">Distance along the ray to the intersection.</param>
    /// <returns>False when the ray is parallel to the plane or points away from it.</returns>
    public bool IntersectHorizontalPlane(float height, out float distance)
    {
        distance = 0f;
        if (MathF.Abs(Direction.Y) < 1e-6f) return false;
        distance = (height - Origin.Y) / Direction.Y;
        return distance >= 0f;
    }
}

/// <summary>Result of a slip hit test (<see cref="Api.IMarinaVisualizer.HitTest"/>).</summary>
/// <param name="SlipId">Slip that was hit. For a boat spanning several slips, the member slip nearest the hit point.</param>
/// <param name="Distance">Distance from the camera along the ray.</param>
/// <param name="WorldPoint">World-space point that was hit.</param>
/// <param name="HitBoat">True when the boat was hit rather than the slip's water area.</param>
public readonly record struct SlipHit(string SlipId, float Distance, Vector3 WorldPoint, bool HitBoat);
