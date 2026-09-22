using System.Numerics;
using VirtualMarina.Core.Design;

namespace VirtualMarina.Core.Serialization;

/// <summary>
/// The tracing image stored inside a marina file: the picture itself plus where it sits and how it is shown, so a
/// design reopens on the same photo, at the same place and the same scale, months later.
/// </summary>
/// <remarks>
/// <para>
/// The picture is stored as the original PNG/JPEG file, not as pixels, so it costs roughly what the file costs on
/// disk. A <see cref="Design.ReferenceImage"/> can only be written when it still knows those bytes
/// (<see cref="Design.ReferenceImage.CanBeSaved"/>); one built from raw pixels alone is skipped rather than
/// bloating the file with a bitmap.
/// </para>
/// <para>
/// The measuring line is deliberately not part of this: it is scaffolding for calibrating the picture, and once the
/// scale is right it has no meaning.
/// </para>
/// </remarks>
public sealed record ReferenceImageRecord
{
    /// <summary>The picture, with its original file bytes.</summary>
    public required ReferenceImage Image { get; init; }

    /// <summary>Center of the picture in plan coordinates.</summary>
    public Vector2 Center { get; init; }

    /// <summary>Ground size of one pixel, in meters: the scale the picture was calibrated to.</summary>
    public float MetersPerPixel { get; init; } = 1f;

    /// <summary>How solid the picture is drawn, 0–1.</summary>
    public float Opacity { get; init; } = 0.6f;

    /// <summary>Whether the picture is shown at all.</summary>
    public bool Visible { get; init; } = true;

    /// <summary>Whether it is drawn over land and piers rather than under them.</summary>
    public bool AboveScene { get; init; } = true;

    /// <summary>Reads the tracing image out of a designer, or null when there is none worth storing.</summary>
    /// <param name="designer">The designer to read.</param>
    public static ReferenceImageRecord? FromDesigner(MarinaDesigner designer)
    {
        ArgumentNullException.ThrowIfNull(designer);
        return designer.ReferenceImage is { CanBeSaved: true } image
            ? new ReferenceImageRecord
            {
                Image = image,
                Center = designer.ReferenceImageCenter,
                MetersPerPixel = designer.ReferenceImageMetersPerPixel,
                Opacity = designer.ReferenceImageOpacity,
                Visible = designer.ReferenceImageVisible,
                AboveScene = designer.ReferenceImageAboveScene,
            }
            : null;
    }

    /// <summary>Puts the picture back under a designer, where it was and at the scale it was.</summary>
    /// <param name="designer">The designer to load into.</param>
    public void ApplyTo(MarinaDesigner designer)
    {
        ArgumentNullException.ThrowIfNull(designer);
        designer.SetReferenceImage(Image, MetersPerPixel, Center);
        designer.ReferenceImageOpacity = Opacity;
        designer.ReferenceImageVisible = Visible;
        designer.ReferenceImageAboveScene = AboveScene;
    }
}
