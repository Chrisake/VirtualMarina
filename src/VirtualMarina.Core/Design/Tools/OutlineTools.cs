using System.Numerics;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Design;

/// <summary><see cref="DesignTool.DrawLandArea"/>: click the corners of a land area, close it on the first one.</summary>
internal sealed class LandAreaTool : DraftTool
{
    public LandAreaTool(MarinaDesigner designer)
        : base(designer)
    {
    }

    public override string Hint => Points.Count switch
    {
        0 => Strings.HintDrawLandAreaFirst,
        < 3 => Strings.HintDrawLandAreaCorners,
        _ => Strings.HintDrawLandAreaFinish,
    };

    /// <summary>The outline is drawn at the height the land will have, so that is where the pointer is picked.</summary>
    public override float PointerPlane => Designer.LandHeight;

    public override bool Snaps => true;

    public override void OnClick(float x, float y, InputModifiers modifiers)
    {
        if (Pointer is not { } p) return;

        // Clicking the first corner closes the outline. The corner is judged where it is drawn, at the land's height.
        if (Points.Count >= 3 && Picker.IsNearOnScreen(Points[0], PointerPlane, new Vector2(x, y), MathF.Max(Designer.SnapDistancePixels, 6f)))
        {
            Designer.CompleteDraft();
            return;
        }

        // The second click of a double-click lands on the same spot; the double-click then finishes the outline.
        if (Points.Count > 0 && Vector2.Distance(Points[^1], p) < 1e-3f) return;
        AddPoint(p);
    }

    public override void OnDoubleClick() => Designer.CompleteDraft();

    public override DraftOutcome Complete()
    {
        if (Points.Count < 3) return DraftOutcome.Nothing;
        var outline = PolygonMath.RemoveRepeatedPoints(Points, closed: true);
        if (outline.Count < 3 || !PolygonMath.IsSimple(outline)) return DraftOutcome.Invalid;
        return Designer.TryCreate(() => Designer.CreateLandArea(outline) is not null) ? DraftOutcome.Created : DraftOutcome.Nothing;
    }

    /// <summary>Alt turns snapping off, which moves the preview.</summary>
    public override bool ShowsModifiers(InputModifiers changed) => (changed & (InputModifiers.Alt | InputModifiers.Shift)) != 0;

    public override void AppendOverlay(DesignOverlay overlay)
    {
        var y = Designer.LandHeight + 0.08f;
        var preview = PointsWithPointer();
        var valid = preview.Count < 3 || PolygonMath.IsSimple(preview);
        var color = valid ? OverlayColors.Draft : OverlayColors.Invalid;

        for (var i = 0; i + 1 < preview.Count; i++) overlay.Line(preview[i], preview[i + 1], y, color);
        if (preview.Count >= 3) overlay.Line(preview[^1], preview[0], y, valid ? OverlayColors.DraftClosing : OverlayColors.Invalid);
        foreach (var point in Points) overlay.Dot(point, y, color);
        if (Points.Count >= 3) overlay.Dot(Points[0], y, OverlayColors.Snap, 1.6f);

        if (Pointer is { } pointer)
        {
            overlay.Pointer(pointer, y, Designer.PointerSnapped);
            if (Points.Count > 0) overlay.Text(DesignOverlay.Meters(Vector2.Distance(Points[^1], pointer)), pointer, y, OverlayColors.Text);
        }
    }
}

/// <summary><see cref="DesignTool.DrawPier"/>: click the shore end, then the far end.</summary>
internal sealed class PierTool : DraftTool
{
    public PierTool(MarinaDesigner designer)
        : base(designer)
    {
    }

    public override string Hint => Points.Count == 0 ? Strings.HintDrawPierStart : Strings.HintDrawPierEnd;

    /// <summary>The pier is drawn at its deck height, so that is where the pointer is picked.</summary>
    public override float PointerPlane => Pier.GetDefaultDeckHeight(Designer.PierType);

    public override bool Snaps => true;

    /// <summary>A pier lines up square with what is already there; Shift asks for 15° steps instead, Alt for neither.</summary>
    public override Vector2 AdjustPointer(Vector2 point, bool snapped)
    {
        if (Points.Count != 1 || snapped) return point;

        var offset = point - Points[0];
        if (offset.LengthSquared() <= 1e-6f) return point;

        var heading = MarinaMath.DirectionToHeading(offset);
        var modifiers = Designer.Modifiers;
        float? aligned;
        if ((modifiers & InputModifiers.Shift) != 0) aligned = MathF.Round(heading / 15f) * 15f;
        else if ((modifiers & InputModifiers.Alt) != 0) aligned = null;
        else aligned = Picker.SquareWithSurroundings(heading, Points[0]);

        Designer.HeadingSnapped = aligned is not null;
        return aligned is { } snappedHeading ? Points[0] + MarinaMath.HeadingToDirection(snappedHeading) * offset.Length() : point;
    }

    public override void OnClick(float x, float y, InputModifiers modifiers)
    {
        if (Pointer is not { } p) return;
        if (Points.Count == 0) AddPoint(p);
        else if (Vector2.Distance(Points[0], p) >= MarinaDesigner.MinimumPierLength) Designer.TryCreate(() => Designer.CreatePier(Points[0], p) is not null);
    }

    public override DraftOutcome Complete() =>
        Points.Count == 1 && Pointer is { } end
            ? Designer.TryCreate(() => Designer.CreatePier(Points[0], end) is not null) ? DraftOutcome.Created : DraftOutcome.Nothing
            : DraftOutcome.Nothing;

    /// <summary>Alt turns snapping off and Shift squares the angle up, both of which move the preview.</summary>
    public override bool ShowsModifiers(InputModifiers changed) => (changed & (InputModifiers.Alt | InputModifiers.Shift)) != 0;

    public override void AppendOverlay(DesignOverlay overlay)
    {
        var deck = PointerPlane;
        if (Pointer is not { } pointer) return;
        overlay.Pointer(pointer, deck + 0.2f, Designer.PointerSnapped);
        if (Points.Count == 0) return;

        var start = Points[0];
        var length = Vector2.Distance(start, pointer);
        overlay.Dot(start, deck + 0.2f, OverlayColors.Draft);
        if (length < 1e-3f) return;

        var width = Designer.PierWidth;
        var heading = MarinaMath.DirectionToHeading(pointer - start);
        var center = (start + pointer) * 0.5f;
        var color = length >= MarinaDesigner.MinimumPierLength ? new Vector4(OverlayColors.PierPreview(Designer.PierType), 0.7f) : OverlayColors.Invalid;

        // A line back along the pier marks the direction as squared up rather than freehand.
        if (Designer.HeadingSnapped) overlay.Line(start - MarinaMath.HeadingToDirection(heading) * 4f, start, deck + 0.1f, OverlayColors.Snap);
        overlay.Box(center, heading, new Vector3(width, 0.3f, length), deck - 0.15f, color);
        overlay.Line(start, pointer, deck + 0.1f, OverlayColors.Draft);

        var sides = Designer.PierBerthingSides;
        if (sides != PierSides.Both)
        {
            // Mark the open side with a strip along its edge.
            var right = -MarinaMath.HeadingToRight(heading) * (sides == PierSides.Right ? 1f : -1f); // Pier.Right
            var edge = right * (width * 0.5f + overlay.LineWidth);
            overlay.Line(start + edge, pointer + edge, deck + 0.1f, OverlayColors.BerthPreview with { W = 0.95f });
        }

        overlay.Text(DesignOverlay.Meters(length), pointer, deck + 0.2f, OverlayColors.Text);
    }
}

/// <summary>
/// <see cref="DesignTool.DrawShoreline"/>: click points along the coast, press Enter to settle the line, then click the
/// side that is land.
/// </summary>
internal sealed class ShorelineTool : DraftTool
{
    /// <summary>How much of the endless ends is drawn, enough to read which way they go.</summary>
    private const float EndlessPreview = 300f;

    private List<Vector2>? _awaitingSide;
    private Vector2[]? _awaitingSnapshot;
    private bool? _crosses;

    public ShorelineTool(MarinaDesigner designer)
        : base(designer)
    {
    }

    public override string Hint
    {
        get
        {
            if (_awaitingSide is not null) return Strings.HintDrawShorelineSide;
            if (Points.Count == 0) return Strings.HintDrawShorelineFirst;
            return Crosses ? Strings.HintDrawShorelineCrossing : Strings.HintDrawShorelineMore;
        }
    }

    public override bool HasDraft => base.HasDraft || _awaitingSide is not null;

    /// <summary>The coast drawn at the mainland's height, or at the land height when there is no mainland yet.</summary>
    public override float PointerPlane => Marina.Shoreline?.Height ?? Designer.LandHeight;

    /// <summary>The settled coast waiting for its side, or null. The same instance is returned until it changes.</summary>
    public IReadOnlyList<Vector2>? AwaitingSide => _awaitingSide is null ? null : _awaitingSnapshot ??= [.. _awaitingSide];

    /// <summary>True while the coast drawn so far has endless ends that run into each other, so neither side is the land.</summary>
    private bool Crosses => _crosses ??= LineCrosses(PolygonMath.RemoveRepeatedPoints(Points, closed: false));

    public override void OnClick(float x, float y, InputModifiers modifiers)
    {
        if (Pointer is not { } p) return;

        // Points first; once Enter has settled the line, the click says which side is the land.
        if (_awaitingSide is null) AddPoint(p);
        else Designer.TryCreate(() => PickSide(p) is not null);
    }

    public override DraftOutcome Complete()
    {
        if (_awaitingSide is not null || Points.Count < 2) return DraftOutcome.Nothing;
        if (Crosses) return DraftOutcome.Invalid;

        // Enter does not finish a coast; it settles the line, and the next click says which side is land.
        var line = PolygonMath.RemoveRepeatedPoints(Points, closed: false);
        if (line.Count < 2) return DraftOutcome.Nothing;

        _awaitingSide = [.. line];
        _awaitingSnapshot = null;
        base.ClearDraft();
        Designer.InvalidateOverlay();
        Designer.RaiseDraftChanged(DesignDraftChange.PointAdded, AwaitingSide!);
        return DraftOutcome.Advanced;
    }

    public override bool RemoveLastPoint()
    {
        if (Points.Count > 0 || _awaitingSide is not { Count: > 0 } waiting) return base.RemoveLastPoint();

        // Back from choosing a side to drawing the line.
        _awaitingSide = null;
        _awaitingSnapshot = null;
        RestorePoints(waiting);
        Designer.RaiseDraftChanged(DesignDraftChange.PointRemoved, DraftPoints);
        return true;
    }

    /// <summary>Makes the mainland from the settled coast, with the land on the side <paramref name="landSide"/> falls on.</summary>
    public Shoreline? PickSide(Vector2 landSide)
    {
        if (_awaitingSide is not { Count: >= 2 } line) return null;

        // Both halves of the plan are covered by the two candidates, so testing one of them decides it.
        var onLeft = new Shoreline(line, landOnLeft: true).Contains(landSide);
        return Designer.CreateShoreline(line, onLeft);
    }

    public override bool ShowsModifiers(InputModifiers changed) => (changed & (InputModifiers.Alt | InputModifiers.Shift)) != 0;

    public override void AppendOverlay(DesignOverlay overlay)
    {
        var y = PointerPlane + 0.08f;
        var settled = _awaitingSide is not null;
        var line = _awaitingSide ?? PointsWithPointer();

        var crosses = LineCrosses(line);
        var color = crosses ? OverlayColors.Invalid : settled ? OverlayColors.Snap : OverlayColors.Draft;

        for (var i = 0; i + 1 < line.Count; i++) overlay.Line(line[i], line[i + 1], y, color);
        foreach (var point in Points) overlay.Dot(point, y, color);

        if (line.Count >= 2)
        {
            // The ends run on without end; showing a few hundred meters of them says which way the land is cut.
            overlay.Line(line[0], line[0] + Vector2.Normalize(line[0] - line[1]) * EndlessPreview, y, color with { W = color.W * 0.45f });
            overlay.Line(line[^1], line[^1] + Vector2.Normalize(line[^1] - line[^2]) * EndlessPreview, y, color with { W = color.W * 0.45f });
        }

        if (Pointer is not { } pointer) return;
        overlay.Pointer(pointer, y, Designer.PointerSnapped);

        if (settled && line.Count >= 2)
        {
            // A line from the coast out to the pointer, showing the half of the plan the click would make land.
            var nearest = line[0];
            foreach (var point in line)
            {
                if (Vector2.DistanceSquared(point, pointer) < Vector2.DistanceSquared(nearest, pointer)) nearest = point;
            }

            overlay.Line(nearest, pointer, y, OverlayColors.BerthPreview with { W = 0.9f });
            overlay.Text(Strings.OverlayLandThisSide, pointer, y, OverlayColors.Text);
        }
        else if (Points.Count > 0)
        {
            overlay.Text(DesignOverlay.Meters(Vector2.Distance(Points[^1], pointer)), pointer, y, OverlayColors.Text);
        }
    }

    protected override void ClearDraft()
    {
        _awaitingSide = null;
        _awaitingSnapshot = null;
        base.ClearDraft();
    }

    protected override void OnPointsChanged() => _crosses = null;

    private static bool LineCrosses(IReadOnlyList<Vector2> line) => line.Count >= 2 && new Shoreline(line, landOnLeft: true).Validate().Any();
}

/// <summary><see cref="DesignTool.MeasureScale"/>: click both ends of the picture's scale bar.</summary>
internal sealed class MeasureScaleTool : DraftTool
{
    public MeasureScaleTool(MarinaDesigner designer)
        : base(designer)
    {
    }

    public override string Hint
    {
        get
        {
            if (Designer.ReferenceImage is null) return Strings.HintReferenceImageMissing;
            return Points.Count == 0 ? Strings.HintMeasureScaleFirst : Strings.HintMeasureScaleSecond;
        }
    }

    /// <summary>The line is drawn on the picture, so the pointer is picked there.</summary>
    public override float PointerPlane => Designer.ImageDrawHeight();

    public override void OnClick(float x, float y, InputModifiers modifiers)
    {
        if (Pointer is not { } p || Designer.ReferenceImage is null) return;
        if (Points.Count == 0) AddPoint(p);
        else if (Vector2.Distance(Points[0], p) > 1e-3f) Designer.FinishScaleLine(Points[0], p);
    }

    public override bool ShowsModifiers(InputModifiers changed) => (changed & (InputModifiers.Alt | InputModifiers.Shift)) != 0;

    public override void AppendOverlay(DesignOverlay overlay)
    {
        if (!ImageToolOverlay.Append(Designer, overlay, out var y)) return;

        if (Points.Count == 1 && Pointer is { } pointer)
        {
            overlay.Line(Points[0], pointer, y, OverlayColors.ScaleLine);
            overlay.Dot(Points[0], y, OverlayColors.ScaleLine);
            overlay.Text(DesignOverlay.Meters(Vector2.Distance(Points[0], pointer)), pointer, y, OverlayColors.Text);
        }
        else if (Pointer is { } p)
        {
            overlay.Dot(p, y, OverlayColors.Pointer);
        }

        if (Designer.ScaleLine is { } scale)
        {
            overlay.Text(DesignOverlay.Meters(Vector2.Distance(scale.Start, scale.End)), (scale.Start + scale.End) * 0.5f, y, OverlayColors.ScaleLine);
        }
    }
}

/// <summary>The outline of the reference picture and its north mark, which both picture tools show.</summary>
internal static class ImageToolOverlay
{
    /// <summary>Draws the outline. Returns false without a picture; <paramref name="y"/> is the height the picture lies at.</summary>
    public static bool Append(MarinaDesigner designer, DesignOverlay overlay, out float y)
    {
        y = designer.ImageDrawHeight() + 0.05f;
        if (designer.ReferenceImageBounds is not { } b) return false;

        var corners = new[] { b.Min, new Vector2(b.Max.X, b.Min.Y), b.Max, new Vector2(b.Min.X, b.Max.Y) };
        overlay.Outline(corners, y, OverlayColors.ImageOutline);
        overlay.Text(Strings.OverlayNorth, new Vector2((b.Min.X + b.Max.X) * 0.5f, b.Min.Y - overlay.LineWidth * 6f), y, OverlayColors.ImageOutline);
        return true;
    }
}
