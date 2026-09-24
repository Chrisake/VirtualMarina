using System.Numerics;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Input;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Picking;
using VirtualMarina.Core.Rendering;
using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Design;

/// <summary><see cref="DesignTool.Navigate"/>: clicks do nothing, the camera moves as usual.</summary>
internal sealed class NavigateTool : DesignToolHandler
{
    public NavigateTool(MarinaDesigner designer)
        : base(designer)
    {
    }

    public override string Hint => Strings.HintNavigate;
}

/// <summary>A tool that acts on the berth, pier or land area under the pointer, found as the pointer moves.</summary>
internal abstract class PickTool : DesignToolHandler
{
    protected PickTool(MarinaDesigner designer)
        : base(designer)
    {
    }

    /// <summary>What is under the pointer now: a <see cref="Berth"/>, <see cref="Pier"/> or <see cref="LandArea"/>, or null.</summary>
    protected object? Target { get; private set; }

    /// <summary>
    /// Finds the element under the pointer. A click comes straight after a move to the same pixel, so the click acts on
    /// what this found rather than looking again.
    /// </summary>
    public override void OnHover(float x, float y) => Target = Picker.ElementUnder(x, y);

    public override void ClearHover() => Target = null;

    /// <summary>Alt widens the tool from the one thing to its whole row, which the preview shows.</summary>
    public override bool ShowsModifiers(InputModifiers changed) => (changed & InputModifiers.Alt) != 0;

    /// <summary>The element under the pointer, shown in the eraser's red: a berth (its whole row with Alt), a pier with its berths, or a land area.</summary>
    protected void AppendTarget(DesignOverlay overlay)
    {
        switch (Target)
        {
            case Berth berth:
                // With Alt down the eraser takes the whole row, so show the whole row.
                var sweeping = (Designer.Modifiers & InputModifiers.Alt) != 0 && berth.PierId is not null;
                foreach (var doomed in sweeping ? Marina.GetBerthsByPier(berth.PierId!) : [berth])
                {
                    var height = SceneBuilder.GroundHeight(doomed, Marina.GetLandArea);
                    overlay.Pad(doomed, BerthPlacement.PadHeightFor(height) + 0.04f, OverlayColors.Erase);
                }

                break;
            case Pier pier:
                overlay.Box(pier.Center, pier.HeadingDegrees, new Vector3(pier.Width + 0.4f, 0.5f, pier.Length + 0.4f), pier.DeckHeight + 0.1f, OverlayColors.Erase);
                foreach (var berth in Marina.GetBerthsByPier(pier.Id)) overlay.Pad(berth, BerthPlacement.PadHeight + 0.04f, OverlayColors.Erase);
                break;
            case LandArea land:
                overlay.Outline(land.Points, land.Height + 0.1f, OverlayColors.Invalid);
                foreach (var berth in Marina.GetBerthsByLandArea(land.Id)) overlay.Pad(berth, land.Height + 0.1f, OverlayColors.Erase);
                break;
        }
    }
}

/// <summary><see cref="DesignTool.Erase"/>: click a berth, pier or land area to remove it; Alt over a berth clears its pier.</summary>
internal sealed class EraseTool : PickTool
{
    public EraseTool(MarinaDesigner designer)
        : base(designer)
    {
    }

    public override string Hint => Strings.HintErase;

    public override bool WantsDelete => true;

    public override void OnClick(float x, float y, InputModifiers modifiers)
    {
        if (Target is not { } target) return;

        // Alt over a berth clears the pier it belongs to, rather than that one berth.
        if ((modifiers & InputModifiers.Alt) != 0 && target is Berth { PierId: { } owner }) Designer.EraseBerthsOfPier(owner);
        else Designer.Erase(target);
    }

    public override bool Delete() => Target is { } target && Designer.Erase(target);

    public override void AppendOverlay(DesignOverlay overlay) => AppendTarget(overlay);
}

/// <summary><see cref="DesignTool.Rename"/>: click a berth or pier and the host is asked for its new name.</summary>
internal sealed class RenameTool : PickTool
{
    public RenameTool(MarinaDesigner designer)
        : base(designer)
    {
    }

    public override string Hint => Strings.HintRename;

    public override void OnClick(float x, float y, InputModifiers modifiers)
    {
        if (Target is { } target) Designer.AskToRename(target, (modifiers & InputModifiers.Alt) != 0);
    }

    public override void AppendOverlay(DesignOverlay overlay) => AppendTarget(overlay);
}

/// <summary><see cref="DesignTool.EditServices"/>: click a berth to give it the pedestals in hand; Alt or Ctrl for its whole side.</summary>
internal sealed class ServicesTool : PickTool
{
    public ServicesTool(MarinaDesigner designer)
        : base(designer)
    {
    }

    public override string Hint => Strings.HintEditServices;

    public override void OnClick(float x, float y, InputModifiers modifiers)
    {
        if (Target is Berth berth) Designer.SetBerthServices(berth, WholeSideWanted(modifiers));
    }

    /// <summary>Alt or Ctrl widens the change to the whole side, which the preview shows.</summary>
    public override bool ShowsModifiers(InputModifiers changed) => (changed & (InputModifiers.Alt | InputModifiers.Control)) != 0;

    public override void AppendOverlay(DesignOverlay overlay)
    {
        if (Target is not Berth serving) return;
        foreach (var berth in Designer.ServiceTargets(serving, WholeSideWanted(Designer.Modifiers)))
        {
            overlay.Pad(berth, BerthPlacement.PadHeightFor(SceneBuilder.GroundHeight(berth, Marina.GetLandArea)) + 0.04f, OverlayColors.ServicePreview);
        }
    }

    /// <summary>Alt or Ctrl widens a pedestal change from one berth to the whole side.</summary>
    private static bool WholeSideWanted(InputModifiers modifiers) => (modifiers & (InputModifiers.Alt | InputModifiers.Control)) != 0;
}

/// <summary><see cref="DesignTool.PlantTrees"/>: click a lawn to scatter trees on it; Ctrl+click or a right-click clears them.</summary>
internal sealed class PlantTreesTool : DesignToolHandler
{
    private LandArea? _lawn;

    public PlantTreesTool(MarinaDesigner designer)
        : base(designer)
    {
    }

    public override string Hint => Designer.TreeDensity > 0f ? Strings.HintPlantTrees : Strings.HintPlantTreesNone;

    public override void OnHover(float x, float y) => _lawn = Picker.LandUnder(x, y, LandKind.Grass);

    public override void ClearHover() => _lawn = null;

    public override void OnClick(float x, float y, InputModifiers modifiers)
    {
        if ((modifiers & InputModifiers.Control) != 0) RemoveTreesUnder(x, y);
        else if (_lawn is { } lawn) Designer.PlantTrees(lawn.Id);
    }

    /// <summary>There is no drawing to finish, so a right-click clears the trees instead.</summary>
    public override void OnRightClick(float x, float y) => RemoveTreesUnder(x, y);

    public override void AppendOverlay(DesignOverlay overlay)
    {
        if (_lawn is not { } hovered || Marina.GetLandArea(hovered.Id) is not { } lawn) return;
        var y = lawn.Height + 0.1f;
        overlay.Outline(lawn.Points, y, OverlayColors.BerthPreview with { W = 0.95f });
        overlay.Text(Strings.Format(Strings.OverlayTreeCount, lawn.Trees.Count), Pointer ?? lawn.Points[0], y, OverlayColors.Text);
    }

    /// <summary>Trees come off any land area, not only lawns: a quay can have been given some from code.</summary>
    private void RemoveTreesUnder(float x, float y)
    {
        if (Picker.LandUnder(x, y) is { } land) Designer.RemoveTrees(land.Id);
    }
}

/// <summary>
/// <see cref="DesignTool.SelectArea"/>: drag a box to select the berths inside it; Delete then erases them.
/// </summary>
internal sealed class SelectAreaTool : DesignToolHandler
{
    private Vector2? _from;
    private Vector2? _to;
    private InputModifiers _modifiers;
    private float _heading;

    public SelectAreaTool(MarinaDesigner designer)
        : base(designer)
    {
    }

    public override string Hint => Strings.HintSelectArea;

    public override bool CapturesDrag => true;

    public override bool IsDragging => _from is not null;

    public override bool WantsDelete => Marina.SelectedBerths.Count > 0;

    /// <summary>The corners of the box being dragged, or null.</summary>
    public IReadOnlyList<Vector2>? Quad => _from is { } from && _to is { } to ? MarinaDesigner.BoxCorners(from, to, _heading) : null;

    public override void BeginDrag(float x, float y)
    {
        _from = Picker.PlanPointAt(x, y, 0f);
        _to = _from;
        _modifiers = Designer.Modifiers;
        _heading = Marina.Camera.Pose.YawDegrees;
    }

    public override void Drag(float x, float y)
    {
        if (_from is null) return;
        if (Picker.PlanPointAt(x, y, 0f) is { } corner) _to = corner;
        Designer.InvalidateOverlay();
    }

    public override void EndDrag()
    {
        if (_from is { } from && _to is { } to && Vector2.Distance(from, to) > 0.5f)
        {
            Designer.SelectBerthsInArea(from, to, _heading, (_modifiers & (InputModifiers.Shift | InputModifiers.Control)) != 0);
        }

        CancelDrag();
    }

    public override bool CancelDrag()
    {
        if (_from is null) return false;
        _from = null;
        _to = null;
        Designer.InvalidateOverlay();
        return true;
    }

    /// <summary>Delete erases the berths the box selected, as one step.</summary>
    public override bool Delete() => Designer.EraseSelectedBerths() > 0;

    public override void AppendOverlay(DesignOverlay overlay)
    {
        if (Quad is not { } corners) return;
        overlay.Outline(corners, overlay.TextFloor - 0.4f, OverlayColors.SelectionBox);
    }
}

/// <summary><see cref="DesignTool.MoveReferenceImage"/>: drag the picture into place.</summary>
internal sealed class MoveImageTool : DesignToolHandler
{
    public MoveImageTool(MarinaDesigner designer)
        : base(designer)
    {
    }

    public override string Hint => Designer.ReferenceImage is null ? Strings.HintReferenceImageMissing : Strings.HintMoveReferenceImage;

    /// <summary>The picture is dragged by the point of it under the pointer, where it lies just above the water.</summary>
    public override float PointerPlane => Designer.ImageDrawHeight();

    public override bool CapturesDrag => Designer.ReferenceImage is not null;

    public override bool IsDragging => Designer.Image.IsDragging;

    public override void BeginDrag(float x, float y) => Designer.Image.BeginDrag(Picker.PlanPointAt(x, y, PointerPlane));

    public override void Drag(float x, float y) => Designer.Image.Drag(Picker.PlanPointAt(x, y, PointerPlane));

    public override void EndDrag() => Designer.Image.EndDrag();

    public override bool CancelDrag() => Designer.Image.CancelDrag();

    public override bool ShowsModifiers(InputModifiers changed) => (changed & (InputModifiers.Alt | InputModifiers.Shift)) != 0;

    public override void AppendOverlay(DesignOverlay overlay) => ImageToolOverlay.Append(Designer, overlay, out _);
}
