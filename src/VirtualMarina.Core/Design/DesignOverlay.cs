using System.Globalization;
using System.Numerics;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Rendering;
using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Design;

/// <summary>The colors of the designer's previews.</summary>
internal static class OverlayColors
{
    public static readonly Vector4 Draft = new(1f, 0.93f, 0.35f, 0.95f);
    public static readonly Vector4 DraftClosing = new(1f, 0.93f, 0.35f, 0.5f);
    public static readonly Vector4 Invalid = new(0.95f, 0.25f, 0.2f, 0.95f);
    public static readonly Vector4 Pointer = new(1f, 1f, 1f, 0.95f);
    public static readonly Vector4 Snap = new(0.3f, 0.95f, 1f, 0.95f);
    public static readonly Vector4 BerthPreview = new(0.25f, 0.85f, 0.4f, 0.55f);
    public static readonly Vector4 Erase = new(0.95f, 0.2f, 0.15f, 0.55f);
    public static readonly Vector4 ImageOutline = new(1f, 0.6f, 0.15f, 0.95f);
    public static readonly Vector4 ScaleLine = new(1f, 0.35f, 0.85f, 0.95f);
    public static readonly Vector4 ServicePreview = new(0.99f, 0.78f, 0.15f, 0.6f);
    public static readonly Vector4 SelectionBox = new(0.35f, 0.78f, 1f, 0.95f);
    public static readonly Vector4 TrafficLane = new(1f, 0.85f, 0.25f, 0.85f);
    public static readonly Vector4 TrafficLaneBack = new(0.45f, 0.85f, 1f, 0.85f);
    public static readonly Vector4 Text = new(1f, 1f, 1f, 0.97f);

    /// <summary>The deck color of a pier type, for the pier being drawn.</summary>
    public static Vector3 PierPreview(PierType type) => type switch
    {
        PierType.Concrete => new Vector3(0.74f, 0.73f, 0.70f),
        PierType.FloatingConcrete => new Vector3(0.68f, 0.67f, 0.64f),
        _ => new Vector3(0.66f, 0.50f, 0.33f),
    };
}

/// <summary>
/// Emits preview geometry (thin boxes, dots, pads and text) into the transparent pass, so it shows above the reference
/// image. Also writes the measurements shown beside the pointer, in the overlay font's capitals.
/// </summary>
internal readonly struct DesignOverlay
{
    private readonly List<RenderObject> _output;
    private readonly float _textUpHeading;
    private readonly float _textFloor;

    public DesignOverlay(List<RenderObject> output, float lineWidth, float textUpHeading, float textFloor)
    {
        _output = output;
        LineWidth = lineWidth;
        _textUpHeading = textUpHeading;
        _textFloor = textFloor;
    }

    public float LineWidth { get; }

    /// <summary>The lowest height text is laid at: above the highest land, so a surface never hides it.</summary>
    public float TextFloor => _textFloor;

    /// <summary>A distance for the overlay, e.g. "12.5 M".</summary>
    public static string Meters(float meters) => Strings.Format(Strings.OverlayMeters, Number(meters));

    /// <summary>A number the way the overlay writes it: one decimal below 100, none above.</summary>
    public static string Number(float value) => value.ToString(value >= 100f ? "0" : "0.0", CultureInfo.InvariantCulture);

    public void Line(Vector2 a, Vector2 b, float y, Vector4 color)
    {
        var length = Vector2.Distance(a, b);
        if (length < 1e-4f) return;
        Box((a + b) * 0.5f, MarinaMath.DirectionToHeading(b - a), new Vector3(LineWidth, LineWidth * 0.4f, length + LineWidth), y, color);
    }

    /// <summary>The closed outline through the points.</summary>
    public void Outline(IReadOnlyList<Vector2> points, float y, Vector4 color)
    {
        for (var i = 0; i < points.Count; i++) Line(points[i], points[(i + 1) % points.Count], y, color);
    }

    public void Box(Vector2 center, float heading, Vector3 size, float y, Vector4 color) =>
        _output.Add(new RenderObject(MeshIds.UnitBox, MarinaMath.CreatePlacement(size, heading, MarinaMath.ToWorld(center, y)), color, 0.6f));

    public void Dot(Vector2 point, float y, Vector4 color, float scale = 1f) =>
        _output.Add(new RenderObject(
            MeshIds.Buoy, Matrix4x4.CreateScale(LineWidth * 3f * scale) * Matrix4x4.CreateTranslation(MarinaMath.ToWorld(point, y)), color, 0.6f));

    /// <summary>The pointer: bright and larger where it snapped to something.</summary>
    public void Pointer(Vector2 point, float y, bool snapped) =>
        Dot(point, y, snapped ? OverlayColors.Snap : OverlayColors.Pointer, snapped ? 1.5f : 1f);

    public void Pad(Berth berth, float y, Vector4 color) =>
        _output.Add(new RenderObject(
            MeshIds.BerthPad,
            MarinaMath.CreatePlacement(new Vector3(MathF.Max(0.2f, berth.Width - 0.3f), 1f, MathF.Max(0.2f, berth.Length - 0.3f)), berth.HeadingDegrees, MarinaMath.ToWorld(berth.Center, y)),
            color, 0.4f));

    /// <summary>Text laid flat next to <paramref name="anchor"/>, upright for the current camera yaw, never below the highest land.</summary>
    public void Text(string text, Vector2 anchor, float y, Vector4 color)
    {
        if (text.Length == 0) return;
        y = MathF.Max(y, _textFloor);
        var height = LineWidth * 5f;
        var up = MarinaMath.HeadingToDirection(_textUpHeading);
        var reading = -MarinaMath.HeadingToRight(_textUpHeading);

        // Beside the pointer: to the right and a little above it on screen.
        var start = anchor + reading * (LineWidth * 5f) + up * (height * 0.9f);
        var scale = new Vector3(height, 1f, height);
        for (var i = 0; i < text.Length; i++)
        {
            if (!GlyphFont.TryGetMeshId(text[i], out var meshId)) continue;
            var position = start + reading * (i * GlyphFont.Advance * height + GlyphFont.GlyphWidth * height * 0.5f);
            _output.Add(new RenderObject(meshId, MarinaMath.CreatePlacement(scale, _textUpHeading, MarinaMath.ToWorld(position, y + 0.05f)), color, 0.8f));
        }
    }
}
