using System.Numerics;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Domain;

/// <summary>
/// Plan-view rectangle rotated about its center. Used as the spatial boundary of berths, piers and land.
/// </summary>
/// <param name="Center">Center in plan coordinates (X = world X, Y = world Z).</param>
/// <param name="Size">X = width across the heading, Y = length along the heading.</param>
/// <param name="HeadingDegrees">Direction of the rectangle's length axis.</param>
public readonly record struct OrientedRect(Vector2 Center, Vector2 Size, float HeadingDegrees)
{
    /// <summary>Unit vector along the length axis.</summary>
    public Vector2 Forward => MarinaMath.HeadingToDirection(HeadingDegrees);

    /// <summary>Unit vector along the width axis (the heading's local +X).</summary>
    public Vector2 Right => MarinaMath.HeadingToRight(HeadingDegrees);

    /// <summary>Extent across the heading (<c>Size.X</c>).</summary>
    public float Width => Size.X;

    /// <summary>Extent along the heading (<c>Size.Y</c>).</summary>
    public float Length => Size.Y;

    /// <summary>True when the plan-view point lies inside or on the rectangle.</summary>
    public bool Contains(Vector2 point)
    {
        var rel = point - Center;
        return MathF.Abs(Vector2.Dot(rel, Right)) <= Size.X * 0.5f
            && MathF.Abs(Vector2.Dot(rel, Forward)) <= Size.Y * 0.5f;
    }

    /// <summary>Corners in counter-clockwise order (viewed from above).</summary>
    public Vector2[] GetCorners()
    {
        var r = Right * (Size.X * 0.5f);
        var f = Forward * (Size.Y * 0.5f);
        return new[] { Center - r - f, Center + r - f, Center + r + f, Center - r + f };
    }

    /// <summary>The smallest axis-aligned rectangle containing this one.</summary>
    public (Vector2 Min, Vector2 Max) GetAxisAlignedBounds()
    {
        var corners = GetCorners();
        var min = corners[0];
        var max = corners[0];
        for (var i = 1; i < corners.Length; i++)
        {
            min = Vector2.Min(min, corners[i]);
            max = Vector2.Max(max, corners[i]);
        }

        return (min, max);
    }
}
