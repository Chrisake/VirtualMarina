using System.Numerics;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;
using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Design;

/// <summary>
/// <see cref="DesignTool.PlaceDividers"/>: click beside a row of berths to put a divider on the nearest boundary, or take
/// away the one there. Alt works on the whole row; Ctrl (or a right-click) only removes.
/// </summary>
internal sealed class DividerTool : DesignToolHandler
{
    public DividerTool(MarinaDesigner designer)
        : base(designer)
    {
    }

    public override string Hint => Strings.HintPlaceDividers;

    public override void OnClick(float x, float y, InputModifiers modifiers)
    {
        if (Target() is not { } target) return;
        var wholeRow = (modifiers & InputModifiers.Alt) != 0;
        if ((modifiers & InputModifiers.Control) != 0)
        {
            Designer.RemoveDividers(target.Pier.Id, target.Side, target.Along, wholeRow);
        }
        else if (!wholeRow && Removes(target))
        {
            // A plain click on a boundary that has a divider takes it away again.
            Designer.RemoveDividers(target.Pier.Id, target.Side, target.Along);
        }
        else
        {
            Designer.PlaceDividers(target.Pier.Id, target.Side, target.Along, wholeRow);
        }
    }

    /// <summary>There is no drawing to finish, so a right-click removes instead.</summary>
    public override void OnRightClick(float x, float y)
    {
        if (Target() is { } target) Designer.RemoveDividers(target.Pier.Id, target.Side, target.Along, (Designer.Modifiers & InputModifiers.Alt) != 0);
    }

    /// <summary>Alt widens the change to the whole row and Ctrl turns it into a removal, both of which the preview shows.</summary>
    public override bool ShowsModifiers(InputModifiers changed) => (changed & (InputModifiers.Alt | InputModifiers.Control)) != 0;

    public override void AppendOverlay(DesignOverlay overlay)
    {
        if (Pointer is not { } pointer) return;
        if (Target() is not { } target || Designer.DividerSlots(target.Pier, target.Side, target.Along, WholeRow) is not { Count: > 0 } slots)
        {
            overlay.Dot(pointer, 0.4f, OverlayColors.Pointer);
            return;
        }

        var y = target.Pier.DeckHeight + 0.15f;
        if ((Designer.Modifiers & InputModifiers.Control) != 0 || (!WholeRow && Removes(target)))
        {
            var rowOrSlots = WholeRow ? Designer.Dividers.Slots(target.Pier, target.Side) : slots;
            Show(overlay, Designer.Dividers.Existing(target.Pier, target.Side, rowOrSlots), "UndoRemoveDividers", OverlayColors.Erase, pointer, y);
        }
        else
        {
            Show(overlay, Designer.Dividers.Plan(target.Pier, target.Side, slots, Designer.DividerType), "UndoPlaceDividers", OverlayColors.BerthPreview, pointer, y);
        }
    }

    private bool WholeRow => (Designer.Modifiers & InputModifiers.Alt) != 0;

    /// <summary>The dividers about to come or go, with how many beside the pointer; a red dot when there are none.</summary>
    private static void Show(DesignOverlay overlay, List<Divider> dividers, string countKey, Vector4 color, Vector2 pointer, float y)
    {
        foreach (var divider in dividers) Footprint(overlay, divider, y, color);
        if (dividers.Count > 0) overlay.Text(Strings.Plural(countKey, dividers.Count, dividers.Count), pointer, y, OverlayColors.Text);
        else overlay.Dot(pointer, 0.4f, OverlayColors.Invalid);
    }

    /// <summary>The ground a divider covers, drawn a little wider than a pile so it can be seen from far above.</summary>
    private static void Footprint(DesignOverlay overlay, Divider divider, float y, Vector4 color) =>
        overlay.Box(divider.Center, divider.HeadingDegrees, new Vector3(MathF.Max(divider.Width, 0.6f), 0.4f, divider.Length), y, color);

    /// <summary>True when the boundary nearest the pointer already has a divider, so a plain click takes it away.</summary>
    private bool Removes((Pier Pier, PierSide Side, float Along) target)
    {
        var slots = Designer.Dividers.Slots(target.Pier, target.Side);
        return DividerPlanner.Nearest(slots, target.Along) is { } slot && Designer.Dividers.At(target.Pier, target.Side, slot) is not null;
    }

    /// <summary>The pier side under the pointer and how far along it, found as for the berth tool.</summary>
    private (Pier Pier, PierSide Side, float Along)? Target() =>
        Pointer is { } pointer ? Picker.BerthTarget(pointer, Designer.BerthWidth, MathF.Max(Designer.BerthLength, 30f)) : null;
}
