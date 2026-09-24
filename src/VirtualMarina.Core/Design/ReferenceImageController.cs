using System.Numerics;

namespace VirtualMarina.Core.Design;

/// <summary>
/// The picture being traced and everything about how it lies on the plan: where, how large, how visible, and the scale
/// line measured on it. <see cref="MarinaDesigner"/> exposes it; this keeps the arithmetic and the dragging in one place.
/// </summary>
internal sealed class ReferenceImageController
{
    private readonly Action<ReferenceImageChange> _changed;
    private readonly Action _moved;

    /// <summary>Where the drag started, so Escape can put the picture back; null when nothing is being dragged.</summary>
    private (Vector2 Center, (Vector2 Start, Vector2 End)? Line)? _dragStart;
    private Vector2? _dragLast;

    /// <param name="changed">Announces a finished change (events, camera bounds, redraw).</param>
    /// <param name="moved">Asks for a redraw only, while the picture is being dragged.</param>
    public ReferenceImageController(Action<ReferenceImageChange> changed, Action moved)
    {
        _changed = changed;
        _moved = moved;
    }

    public ReferenceImage? Image { get; private set; }

    public Vector2 Center { get; private set; }

    public float MetersPerPixel { get; private set; } = DesignerDefaults.ImageMetersPerPixel.Default;

    public float Opacity { get; private set; } = DesignerDefaults.ImageOpacity.Default;

    public bool Visible { get; private set; } = true;

    public bool AboveScene { get; private set; } = true;

    public (Vector2 Start, Vector2 End)? ScaleLine { get; private set; }

    /// <summary>True while the picture is being dragged.</summary>
    public bool IsDragging => _dragStart is not null;

    public Vector2 Size => Image is null ? Vector2.Zero : new Vector2(Image.PixelWidth, Image.PixelHeight) * MetersPerPixel;

    public (Vector2 Min, Vector2 Max)? Bounds
    {
        get
        {
            if (Image is null) return null;
            var half = Size * 0.5f;
            return (Center - half, Center + half);
        }
    }

    public void SetCenter(Vector2 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y)) throw new ArgumentOutOfRangeException(nameof(value), value, "The center must be finite.");
        if (value == Center) return;
        MoveTo(value);
        _changed(ReferenceImageChange.Moved);
    }

    public void SetMetersPerPixel(float value)
    {
        var checkedValue = DesignerDefaults.ImageMetersPerPixel.Require(value);
        if (checkedValue == MetersPerPixel) return;
        MetersPerPixel = checkedValue;
        _changed(ReferenceImageChange.Scaled);
    }

    /// <exception cref="ArgumentOutOfRangeException">The value is not a number. A finite value outside 0–1 is clamped.</exception>
    public void SetOpacity(float value)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "The opacity must be a number between 0 and 1.");
        var clamped = DesignerDefaults.ImageOpacity.Clamp(value);
        if (clamped == Opacity) return;
        Opacity = clamped;
        _changed(ReferenceImageChange.AppearanceChanged);
    }

    public void SetVisible(bool value)
    {
        if (value == Visible) return;
        Visible = value;
        _changed(ReferenceImageChange.AppearanceChanged);
    }

    public void SetAboveScene(bool value)
    {
        if (value == AboveScene) return;
        AboveScene = value;
        _changed(ReferenceImageChange.AppearanceChanged);
    }

    public void Set(ReferenceImage image, float metersPerPixel, Vector2 center)
    {
        CancelDrag();
        Image = image;
        MetersPerPixel = DesignerDefaults.ImageMetersPerPixel.Require(metersPerPixel);
        Center = center;
        ScaleLine = null;
        _changed(ReferenceImageChange.Set);
    }

    public void Clear()
    {
        if (Image is null) return;
        _dragStart = null;
        _dragLast = null;
        Image = null;
        ScaleLine = null;
        _changed(ReferenceImageChange.Cleared);
    }

    /// <summary>Records a measured line without announcing it; the caller announces the change it is part of.</summary>
    public void SetScaleLine((Vector2 Start, Vector2 End)? line) => ScaleLine = line;

    /// <summary>
    /// Rescales the picture so the segment becomes <paramref name="knownLengthMeters"/> long, about <paramref name="start"/>.
    /// When the scale this asks for is outside what a picture may have, the nearest allowed scale is used and the center and
    /// the line move by that same factor, so they stay where they are on the picture.
    /// </summary>
    public bool Calibrate(Vector2 start, Vector2 end, float knownLengthMeters)
    {
        if (!(knownLengthMeters > 0f) || !float.IsFinite(knownLengthMeters))
        {
            throw new ArgumentOutOfRangeException(nameof(knownLengthMeters), knownLengthMeters, "The length must be positive.");
        }

        var measured = Vector2.Distance(start, end);
        if (Image is null || measured < 1e-4f) return false;

        var wanted = MetersPerPixel * (knownLengthMeters / measured);
        var scale = DesignerDefaults.ImageMetersPerPixel.Clamp(wanted);
        var factor = scale / MetersPerPixel;

        MetersPerPixel = scale;
        Center = start + (Center - start) * factor;
        ScaleLine = (start, start + (end - start) * factor);
        _changed(ReferenceImageChange.Scaled);
        return true;
    }

    public void BeginDrag(Vector2? at)
    {
        if (Image is null || at is null) return;
        _dragStart = (Center, ScaleLine);
        _dragLast = at;
    }

    /// <summary>Moves the picture with the pointer. Only a redraw is asked for; the change is announced when the drag ends.</summary>
    public void Drag(Vector2? at)
    {
        if (Image is null || _dragLast is not { } last || at is not { } current) return;
        _dragLast = current;
        if (current == last) return;
        MoveTo(Center + (current - last));
        _moved();
    }

    /// <summary>Ends a drag, announcing the move once. Returns true when the picture moved.</summary>
    public bool EndDrag()
    {
        if (_dragStart is not { } start) return false;
        _dragStart = null;
        _dragLast = null;
        if (start.Center == Center) return false;
        _changed(ReferenceImageChange.Moved);
        return true;
    }

    /// <summary>Abandons a drag, putting the picture back where it was. Returns false when nothing was being dragged.</summary>
    public bool CancelDrag()
    {
        if (_dragStart is not { } start) return false;
        _dragStart = null;
        _dragLast = null;
        Center = start.Center;
        ScaleLine = start.Line;
        _moved();
        return true;
    }

    /// <summary>Moves the picture, and the line measured on it with it.</summary>
    private void MoveTo(Vector2 value)
    {
        var moved = value - Center;
        if (ScaleLine is { } line) ScaleLine = (line.Start + moved, line.End + moved);
        Center = value;
    }
}
