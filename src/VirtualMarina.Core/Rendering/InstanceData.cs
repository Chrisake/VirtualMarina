namespace VirtualMarina.Core.Rendering;

/// <summary>
/// How a <see cref="RenderObject"/> is laid out as instance data for <see cref="ShaderSources.InstancedModelVertex"/>:
/// <see cref="Stride"/> floats per instance, read as five vec4 attributes at locations 3 to 7.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term>0–11 (locations 3–5)</term><description>The model matrix's three columns (M11 M12 M13, M21 M22 M23,
/// M31 M32 M33), each followed by one component of the translation (M41, M42, M43). Transforms are affine, so the fourth
/// column is always (0, 0, 0, 1) and is not stored.</description></item>
/// <item><term>12–15 (location 6)</term><description>Tint.</description></item>
/// <item><term>16–19 (location 7)</term><description>Emissive, animation flags (as a float), phase, desaturation.</description></item>
/// </list>
/// </remarks>
public static class InstanceData
{
    /// <summary>Floats per instance.</summary>
    public const int Stride = 20;

    /// <summary>Bytes per instance.</summary>
    public const int StrideBytes = Stride * sizeof(float);

    /// <summary>Attribute location of the first of the five instance attributes.</summary>
    public const int FirstAttributeLocation = 3;

    /// <summary>How many vec4 instance attributes there are.</summary>
    public const int AttributeCount = 5;

    /// <summary>Writes <paramref name="objects"/> into <paramref name="destination"/>, <see cref="Stride"/> floats each.</summary>
    /// <param name="objects">The instances to pack.</param>
    /// <param name="destination">At least <see cref="Stride"/> × the number of objects floats.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too short.</exception>
    public static void Pack(ReadOnlySpan<RenderObject> objects, Span<float> destination)
    {
        if (destination.Length < objects.Length * Stride)
        {
            throw new ArgumentException($"Needs {objects.Length * Stride} floats.", nameof(destination));
        }

        var o = 0;
        foreach (ref readonly var obj in objects)
        {
            var m = obj.World;
            destination[o] = m.M11; destination[o + 1] = m.M12; destination[o + 2] = m.M13; destination[o + 3] = m.M41;
            destination[o + 4] = m.M21; destination[o + 5] = m.M22; destination[o + 6] = m.M23; destination[o + 7] = m.M42;
            destination[o + 8] = m.M31; destination[o + 9] = m.M32; destination[o + 10] = m.M33; destination[o + 11] = m.M43;
            destination[o + 12] = obj.Tint.X; destination[o + 13] = obj.Tint.Y; destination[o + 14] = obj.Tint.Z; destination[o + 15] = obj.Tint.W;
            destination[o + 16] = obj.Emissive;
            destination[o + 17] = (int)obj.Animation;
            destination[o + 18] = obj.Phase;
            destination[o + 19] = obj.Desaturation;
            o += Stride;
        }
    }
}
