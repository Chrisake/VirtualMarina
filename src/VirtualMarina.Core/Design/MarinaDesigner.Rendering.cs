using System.Numerics;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Design;

/// <summary>What the designer adds to the scene: the reference image, and the previews drawn over everything.</summary>
public sealed partial class MarinaDesigner
{
    private float _overlayDistance = -1f;
    private float _overlayYaw;

    internal ReferenceImageLayer? BuildImageLayer()
    {
        if (Image.Image is not { } image || !Image.Visible || Image.Opacity <= 0f || Image.Bounds is not { } bounds) return null;
        return new ReferenceImageLayer(image, bounds.Min, bounds.Max, ImageDrawHeight(), Image.Opacity, Image.AboveScene);
    }

    /// <summary>True when the camera moved enough that line widths and text orientation of the overlay should be rebuilt.</summary>
    internal bool OverlayNeedsRefresh()
    {
        if (!_active && !Marina.ShowTrafficLanes) return false;
        var pose = Marina.Camera.Pose;
        return _overlayDistance < 0f
            || MathF.Abs(pose.Distance - _overlayDistance) > _overlayDistance * 0.08f
            || MathF.Abs(MarinaMath.DeltaAngle(pose.YawDegrees, _overlayYaw)) > 20f;
    }

    internal void AppendOverlay(List<RenderObject> output)
    {
        var pose = Marina.Camera.Pose;
        _overlayDistance = pose.Distance;
        _overlayYaw = pose.YawDegrees;
        if (!_active && Image.ScaleLine is null && !Marina.ShowTrafficLanes) return;

        var line = Math.Clamp(pose.Distance * 0.0035f, 0.08f, 4f);
        var textUp = MarinaMath.DirectionToHeading(new Vector2(-MathF.Sin(pose.YawDegrees * MarinaMath.DegToRad), -MathF.Cos(pose.YawDegrees * MarinaMath.DegToRad)));
        var overlay = new DesignOverlay(output, line, textUp, TextFloor());

        if (Image.ScaleLine is { } scale && Image.Image is not null && Image.Visible && Image.Opacity > 0.01f && (_active || _tool == DesignTool.MeasureScale))
        {
            var y = ImageDrawHeight() + 0.05f;

            // The line is drawn on the picture, so it fades with it.
            var color = OverlayColors.ScaleLine with { W = OverlayColors.ScaleLine.W * Image.Opacity };
            overlay.Line(scale.Start, scale.End, y, color);
            overlay.Dot(scale.Start, y, color);
            overlay.Dot(scale.End, y, color);
        }

        // Where the passing traffic will run. Shown on demand while the traffic settings are being adjusted, and
        // not tied to a tool, so the clearance and the spacing can be set from any view.
        if (Marina.ShowTrafficLanes)
        {
            // Just clear of the wave crests, so the lines are not swallowed by the water they lie on.
            var y = ShaderSources.MaxWaveHeightFactor * MathF.Abs(Marina.Water.WaveAmplitude) + 0.2f;
            foreach (var lane in Marina.TrafficLanes)
            {
                // The two directions are tinted apart, so which way a lane runs can be seen at a glance.
                var color = lane.Reversed ? OverlayColors.TrafficLaneBack : OverlayColors.TrafficLane;
                for (var i = 0; i < lane.Points.Count - 1; i++) overlay.Line(lane.Points[i], lane.Points[i + 1], y, color);
            }
        }

        if (_active) _handler.AppendOverlay(overlay);
    }

    /// <summary>Measurements float above the highest land area (and the land being drawn), so a surface never hides them.</summary>
    private float TextFloor()
    {
        var highest = MathF.Max(_landHeight, Pier.GetDefaultDeckHeight(PierType.Concrete));
        foreach (var land in Marina.GetLandAreas()) highest = MathF.Max(highest, land.Height);
        return highest + 0.5f;
    }
}
