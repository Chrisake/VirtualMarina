using System.Numerics;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Input;
using VirtualMarina.Core.Picking;
using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Design;

/// <summary>
/// <see cref="DesignTool.AddBerths"/>: click beside a pier where the row starts, then where it ends. The first click
/// anchors the row to that pier and side.
/// </summary>
internal sealed class BerthsTool : DraftTool
{
    private (string PierId, PierSide Side, float Along)? _anchor;

    public BerthsTool(MarinaDesigner designer)
        : base(designer)
    {
    }

    public override string Hint => _anchor is null ? Strings.HintAddBerthsStart : Strings.HintAddBerthsEnd;

    public override bool HasDraft => base.HasDraft || _anchor is not null;

    public override void OnClick(float x, float y, InputModifiers modifiers)
    {
        if (Pointer is not { } pointer) return;

        if (_anchor is not { } anchor)
        {
            if (Target(pointer) is not { } target || !target.Pier.HasBerthsOn(target.Side)) return;
            _anchor = (target.Pier.Id, target.Side, target.Along);
            AddPoint(pointer);
            return;
        }

        if (Marina.GetPier(anchor.PierId) is not { } pier)
        {
            Designer.CancelDraft();
            return;
        }

        Designer.TryCreate(() => Designer.CreateBerths(anchor.PierId, anchor.Side, anchor.Along, PierGeometry.Along(pier, pointer)).Count > 0);
        if (HasDraft) Designer.FinishDraft(DesignDraftChange.Canceled);
    }

    public override DraftOutcome Complete() =>
        _anchor is { } anchor
            ? Designer.TryCreate(() => Designer.CreateBerths(anchor.PierId, anchor.Side, anchor.Along, anchor.Along).Count > 0) ? DraftOutcome.Created : DraftOutcome.Nothing
            : DraftOutcome.Nothing;

    public override bool ShowsModifiers(InputModifiers changed) => (changed & (InputModifiers.Alt | InputModifiers.Shift)) != 0;

    public override void AppendOverlay(DesignOverlay overlay)
    {
        if (Pointer is not { } pointer) return;

        var target = _anchor is { } anchor && Marina.GetPier(anchor.PierId) is { } anchored
            ? (Pier: anchored, anchor.Side, Along: PierGeometry.Along(anchored, pointer))
            : Target(pointer);
        if (target is not { } t)
        {
            overlay.Dot(pointer, 0.4f, OverlayColors.Pointer);
            return;
        }

        if (!t.Pier.HasBerthsOn(t.Side))
        {
            var edge = t.Pier.Right * (t.Side == PierSide.Right ? 1f : -1f) * (t.Pier.Width * 0.5f + overlay.LineWidth);
            overlay.Line(t.Pier.Start + edge, t.Pier.End + edge, t.Pier.DeckHeight + 0.1f, OverlayColors.Invalid);
            overlay.Dot(pointer, 0.4f, OverlayColors.Invalid);
            return;
        }

        var from = _anchor?.Along ?? t.Along;
        var (berths, _) = Designer.Planner.Plan(t.Pier, t.Side, from, t.Along, Designer.RowSettings, preview: true);
        foreach (var berth in berths) overlay.Pad(berth, BerthPlacement.PadHeight + 0.03f, OverlayColors.BerthPreview);

        if (berths.Count == 0)
        {
            overlay.Dot(pointer, 0.4f, OverlayColors.Invalid);
            return;
        }

        var label = berths.Count == 1
            ? Strings.Format(Strings.OverlayOneBerth, DesignOverlay.Number(Designer.BerthWidth), DesignOverlay.Number(Designer.BerthLength))
            : Strings.Format(Strings.OverlayBerthCount, berths.Count);
        overlay.Text(label, pointer, 0.5f, OverlayColors.Text);
    }

    protected override void ClearDraft()
    {
        _anchor = null;
        base.ClearDraft();
    }

    protected override void OnPointsChanged()
    {
        // Taking back the anchoring click lets go of the pier too.
        if (Points.Count == 0) _anchor = null;
    }

    private (Pier Pier, PierSide Side, float Along)? Target(Vector2 pointer) =>
        Picker.BerthTarget(pointer, Designer.BerthWidth, Designer.BerthLength);
}

/// <summary>
/// <see cref="DesignTool.AddLandBerths"/>: click a land area where the boat stands, then click again to aim its bow.
/// </summary>
internal sealed class LandBerthTool : DraftTool
{
    private string? _landId;
    private LandArea? _hovered;

    public LandBerthTool(MarinaDesigner designer)
        : base(designer)
    {
    }

    public override string Hint => _landId is null ? Strings.HintAddLandBerthsStart : Strings.HintAddLandBerthsHeading;

    public override bool HasDraft => base.HasDraft || _landId is not null;

    /// <summary>The boat stands on the land, so the pointer is picked at the height of the land it is over.</summary>
    public override float PointerPlane => Land?.Height ?? 0f;

    /// <summary>The land the berth goes on: the one clicked, or the one under the pointer before that.</summary>
    private LandArea? Land => _landId is { } id ? Marina.GetLandArea(id) : _hovered;

    public override void OnHover(float x, float y) => _hovered = _landId is null ? Picker.LandUnder(x, y) : null;

    public override void ClearHover() => _hovered = null;

    public override void OnClick(float x, float y, InputModifiers modifiers)
    {
        if (Pointer is not { } pointer) return;

        if (_landId is not { } landAreaId)
        {
            // The land under the pointer was found when it moved here; a click is the pointer coming to rest.
            if (_hovered is not { } land) return;
            _landId = land.Id;
            AddPoint(pointer);
            return;
        }

        if (Marina.GetLandArea(landAreaId) is null || Points.Count == 0)
        {
            Designer.CancelDraft();
            return;
        }

        var center = Points[0];
        Designer.TryCreate(() => Designer.CreateLandBerth(landAreaId, center, Designer.HeadingFor(center, pointer)) is not null);
        if (HasDraft) Designer.FinishDraft(DesignDraftChange.Canceled);
    }

    public override DraftOutcome Complete()
    {
        if (_landId is not { } landAreaId || Points.Count != 1) return DraftOutcome.Nothing;
        var center = Points[0];
        return Designer.TryCreate(() => Designer.CreateLandBerth(landAreaId, center, Designer.HeadingFor(center, Pointer)) is not null)
            ? DraftOutcome.Created
            : DraftOutcome.Nothing;
    }

    public override bool ShowsModifiers(InputModifiers changed) => (changed & (InputModifiers.Alt | InputModifiers.Shift)) != 0;

    public override void AppendOverlay(DesignOverlay overlay)
    {
        if (Pointer is not { } pointer) return;

        if (Land is not { } land)
        {
            overlay.Dot(pointer, 0.4f, OverlayColors.Invalid);
            return;
        }

        var y = land.Height + 0.12f;
        var center = Points.Count > 0 ? Points[0] : pointer;
        var heading = Points.Count > 0 ? Designer.HeadingFor(center, pointer) : Designer.LandBerthHeading;
        var length = Designer.BerthLength;
        var width = Designer.BerthWidth;
        var preview = Berth.OnLand("preview", land.Id, center, heading, length, width);
        overlay.Pad(preview, y, land.Contains(center) ? OverlayColors.BerthPreview : OverlayColors.Invalid with { W = 0.5f });

        // An arrow out of the spot, the way the bow points.
        var nose = center + preview.Forward * (length * 0.5f + 1.5f);
        overlay.Line(center, nose, y, OverlayColors.Draft);
        overlay.Dot(nose, y, OverlayColors.Draft, 1.4f);
        var label = Strings.Format(Strings.OverlayLandBerth, DesignOverlay.Number(width), DesignOverlay.Number(length), DesignOverlay.Number(heading));
        overlay.Text(label, pointer, y, OverlayColors.Text);
    }

    protected override void ClearDraft()
    {
        _landId = null;
        base.ClearDraft();
    }

    protected override void OnPointsChanged()
    {
        if (Points.Count == 0) _landId = null;
    }
}
