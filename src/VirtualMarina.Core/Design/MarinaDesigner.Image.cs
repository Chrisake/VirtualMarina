using System.Numerics;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Design;

/// <summary>The reference image to trace, and the camera views that suit tracing it.</summary>
public sealed partial class MarinaDesigner
{
    /// <summary>The image shown to trace the marina, or null.</summary>
    public ReferenceImage? ReferenceImage => Image.Image;

    /// <summary>Center of the reference image in plan coordinates.</summary>
    /// <remarks>The scale line was measured on the picture, so it moves with it.</remarks>
    public Vector2 ReferenceImageCenter
    {
        get => Image.Center;
        set => Image.SetCenter(value);
    }

    /// <summary>Ground size of one image pixel, in meters. Set it directly or with <see cref="CalibrateReferenceImage(float)"/>.</summary>
    public float ReferenceImageMetersPerPixel
    {
        get => Image.MetersPerPixel;
        set => Image.SetMetersPerPixel(value);
    }

    /// <summary>Opacity of the reference image, 0–1 (default 0.6). Values outside the range are clamped to it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a number.</exception>
    public float ReferenceImageOpacity
    {
        get => Image.Opacity;
        set => Image.SetOpacity(value);
    }

    /// <summary>Show the reference image (default true). It is shown whether or not the designer is active.</summary>
    public bool ReferenceImageVisible
    {
        get => Image.Visible;
        set => Image.SetVisible(value);
    }

    /// <summary>
    /// True (default): the image is drawn over land, piers and boats, so it stays visible while tracing. False: it lies on the water
    /// and land and structures hide it. Drawing previews are always on top.
    /// </summary>
    public bool ReferenceImageAboveScene
    {
        get => Image.AboveScene;
        set => Image.SetAboveScene(value);
    }

    /// <summary>Ground size of the reference image in meters (X = east–west, Y = north–south), or zero without an image.</summary>
    public Vector2 ReferenceImageSize => Image.Size;

    /// <summary>The last line drawn with <see cref="DesignTool.MeasureScale"/>, or null.</summary>
    /// <remarks>It is measured on the picture, so it moves and scales with it, and is not saved to a marina file.</remarks>
    public (Vector2 Start, Vector2 End)? ScaleLine => Image.ScaleLine;

    /// <summary>Plan-view bounds of the reference image (north-west and south-east corners), or null without an image.</summary>
    public (Vector2 Min, Vector2 Max)? ReferenceImageBounds => Image.Bounds;

    /// <summary>
    /// Forgets the measuring line, once the image has been scaled by it and the line is only in the way.
    /// Returns false when there was none.
    /// </summary>
    public bool ClearScaleLine()
    {
        if (Image.ScaleLine is null) return false;
        Image.SetScaleLine(null);
        InvalidateOverlay();
        RaiseStateChanged();
        return true;
    }

    /// <summary>
    /// Shows an image to trace (north at the top). Without <paramref name="metersPerPixel"/> it is sized to cover the current layout
    /// (at least 300 m wide) until calibrated; without <paramref name="center"/> it is centered on the camera target.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="metersPerPixel">Known ground size of a pixel, e.g. from map metadata.</param>
    /// <param name="center">Plan position of the image center.</param>
    public void SetReferenceImage(ReferenceImage image, float? metersPerPixel = null, Vector2? center = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        float scale;
        if (metersPerPixel is { } mpp)
        {
            scale = DesignerDefaults.ImageMetersPerPixel.Require(mpp, nameof(metersPerPixel));
        }
        else
        {
            var layout = Marina.GetLayout();
            var hasContent = layout.Piers.Count + layout.LandAreas.Count + layout.Berths.Count > 0;
            var (min, max) = layout.ComputeBounds();
            var extent = hasContent ? MathF.Max(max.X - min.X, max.Y - min.Y) : 0f;
            scale = DesignerDefaults.ImageMetersPerPixel.Clamp(MathF.Max(extent * 1.2f, 300f) / MathF.Max(image.PixelWidth, image.PixelHeight));
        }

        var target = Marina.Camera.DesiredPose.Target;
        Image.Set(image, scale, center ?? new Vector2(target.X, target.Z));
    }

    /// <summary>Removes the reference image and the scale line.</summary>
    public void ClearReferenceImage() => Image.Clear();

    /// <summary>
    /// Rescales the reference image so the last <see cref="ScaleLine"/> becomes <paramref name="knownLengthMeters"/> long. The image scales
    /// about the line's first end, which stays put. Returns false without an image or scale line.
    /// </summary>
    /// <param name="knownLengthMeters">The real length of the scale bar the line was drawn over.</param>
    /// <exception cref="ArgumentOutOfRangeException">The length is not positive.</exception>
    public bool CalibrateReferenceImage(float knownLengthMeters) =>
        Image.ScaleLine is { } line && CalibrateReferenceImage(line.Start, line.End, knownLengthMeters);

    /// <summary>
    /// Rescales the reference image so the segment <paramref name="start"/>–<paramref name="end"/> (in plan coordinates, drawn over the
    /// image at its current scale) becomes <paramref name="knownLengthMeters"/> long. <paramref name="start"/> stays put.
    /// </summary>
    /// <remarks>
    /// A pixel can be 0.0001–1000 m. When the length asks for more or less than that, the nearest of the two is used, and the image
    /// and the line are scaled by that same amount, so they still line up with each other.
    /// </remarks>
    /// <returns>False without an image or when the segment has no length.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The length is not positive.</exception>
    public bool CalibrateReferenceImage(Vector2 start, Vector2 end, float knownLengthMeters) => Image.Calibrate(start, end, knownLengthMeters);

    /// <summary>
    /// Looks straight down with north (−Z) at the top of the view, as aerial images are shown: the best angle to trace an image.
    /// Keeps the camera target and distance.
    /// </summary>
    public void ViewTopDown(bool immediate = false)
    {
        var pose = Marina.Camera.DesiredPose;
        Marina.Camera.SetPose(pose with { YawDegrees = 0f, PitchDegrees = 89f }, immediate);
    }

    /// <summary>Looks straight down, north up, at the whole reference image. Returns false without an image.</summary>
    public bool FocusReferenceImage(bool immediate = false)
    {
        if (ReferenceImageBounds is not { } bounds) return false;
        var size = bounds.Max - bounds.Min;
        var aspect = Marina.ViewportSize.X / MathF.Max(1f, Marina.ViewportSize.Y);
        var tan = MathF.Tan(Marina.Camera.FieldOfViewDegrees * MarinaMath.DegToRad * 0.5f);
        var distance = MathF.Max(size.Y * 0.5f / tan, size.X * 0.5f / (tan * aspect)) * 1.08f;
        Marina.Camera.SetPose(new CameraPose(MarinaMath.ToWorld(Image.Center), 0f, 89f, MathF.Max(10f, distance)), immediate);
        return true;
    }

    /// <summary>Height the reference image is drawn at: just clear of the wave crests.</summary>
    internal float ImageDrawHeight() => ShaderSources.MaxWaveHeightFactor * MathF.Abs(Marina.Water.WaveAmplitude) + 0.03f;

    /// <summary>
    /// Ends a line drawn with <see cref="DesignTool.MeasureScale"/>: keeps it, asks the host for its real length through
    /// <see cref="ScaleLineDrawn"/>, and calibrates the image when the host knows it. One <see cref="StateChanged"/> for all of it.
    /// </summary>
    internal void FinishScaleLine(Vector2 start, Vector2 end)
    {
        using (BeginUpdate())
        {
            Image.SetScaleLine((start, end));
            FinishDraft(DesignDraftChange.Completed);
            var args = new ScaleLineDrawnEventArgs(start, end, Vector2.Distance(start, end));
            ScaleLineDrawn?.Invoke(this, args);
            if (args.KnownLengthMeters is > 0f and var known) CalibrateReferenceImage(start, end, known);
            RaiseStateChanged();
        }
    }

    /// <summary>A finished change to the image: the camera may now reach further, the host is told, the scene redrawn.</summary>
    private void OnImageChanged(ReferenceImageChange change)
    {
        Marina.RefreshCameraBounds();
        InvalidateScene();
        ReferenceImageChanged?.Invoke(this, new ReferenceImageChangedEventArgs(change, Image.Image, Image.Center, Image.MetersPerPixel));
        RaiseStateChanged();
    }
}
