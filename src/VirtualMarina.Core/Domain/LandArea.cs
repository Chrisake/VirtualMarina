namespace VirtualMarina.Core.Domain;

/// <summary>Surface of a <see cref="LandArea"/>; controls its color.</summary>
public enum LandKind
{
    /// <summary>Paved quay or pier head (light concrete).</summary>
    Quay,

    /// <summary>Rock or concrete breakwater (gray).</summary>
    Breakwater,

    /// <summary>Lawn or park (green).</summary>
    Grass,
}

/// <summary>A solid block such as a quay, breakwater or lawn, drawn for context around the water. Not interactive.</summary>
/// <param name="Id">Identifier (for host bookkeeping; not validated for uniqueness).</param>
/// <param name="Area">Footprint in plan coordinates.</param>
/// <param name="Height">Top surface height above the water, in meters.</param>
/// <param name="Kind">Surface type.</param>
/// <example><code>new LandArea("quay", new OrientedRect(new Vector2(0, -19), new Vector2(260, 26), 0), 1.0f, LandKind.Quay)</code></example>
public sealed record LandArea(string Id, OrientedRect Area, float Height, LandKind Kind = LandKind.Quay);
