using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Api;

/// <summary>
/// Readable, localized names for the settings a design UI shows in its pickers, so the WinForms panel, the Blazor
/// panel and the Designer application all offer the same words.
/// </summary>
/// <remarks>
/// The text comes from the core library's resources and follows <see cref="MarinaLocalization.Culture"/>.
/// <see cref="Domain.Pier.GetDisplayName(PierType)"/>, <see cref="BoatTypeCatalog.GetDisplayName(BoatType)"/> and
/// <c>GetDisplayName()</c> on <see cref="BerthStatus"/> and <see cref="BerthLabelMode"/> name the rest.
/// </remarks>
public static class DisplayNames
{
    /// <summary>Name of a design tool, e.g. "Land berths", for a toolbar button.</summary>
    public static string GetDisplayName(this DesignTool tool) => tool switch
    {
        DesignTool.Navigate => Strings.ToolNavigate,
        DesignTool.DrawLandArea => Strings.ToolDrawLandArea,
        DesignTool.DrawPier => Strings.ToolDrawPier,
        DesignTool.AddBerths => Strings.ToolAddBerths,
        DesignTool.AddLandBerths => Strings.ToolAddLandBerths,
        DesignTool.PlantTrees => Strings.ToolPlantTrees,
        DesignTool.Erase => Strings.ToolErase,
        DesignTool.Rename => Strings.ToolRename,
        DesignTool.EditServices => Strings.ToolEditServices,
        DesignTool.PlaceDividers => Strings.ToolPlaceDividers,
        DesignTool.SelectArea => Strings.ToolSelectArea,
        DesignTool.MoveReferenceImage => Strings.ToolMoveReferenceImage,
        DesignTool.MeasureScale => Strings.ToolMeasureScale,
        DesignTool.DrawShoreline => Strings.ToolDrawShoreline,
        _ => tool.ToString(),
    };

    /// <summary>Name of a pier's services, e.g. "Power and water".</summary>
    public static string GetDisplayName(this PierServices services) => services switch
    {
        PierServices.None => Strings.ServicesNone,
        PierServices.Power => Strings.ServicesPower,
        PierServices.Water => Strings.ServicesWater,
        PierServices.PowerAndWater => Strings.ServicesPowerAndWater,
        _ => services.ToString(),
    };

    /// <summary>Name of the berthing sides of a pier, e.g. "Boats on the left only".</summary>
    public static string GetDisplayName(this PierSides sides) => sides switch
    {
        PierSides.Both => Strings.PierSidesBoth,
        PierSides.Left => Strings.PierSidesLeft,
        PierSides.Right => Strings.PierSidesRight,
        _ => sides.ToString(),
    };

    /// <summary>Name of a land surface, e.g. "Lawn or park".</summary>
    public static string GetDisplayName(this LandKind kind) => kind switch
    {
        LandKind.Quay => Strings.LandKindQuay,
        LandKind.Breakwater => Strings.LandKindBreakwater,
        LandKind.Grass => Strings.LandKindGrass,
        _ => kind.ToString(),
    };

    /// <summary>Name of what covers the mainland behind the shore, e.g. "Countryside".</summary>
    public static string GetDisplayName(this HinterlandScenery scenery) => scenery switch
    {
        HinterlandScenery.None => Strings.SceneryNone,
        HinterlandScenery.Countryside => Strings.SceneryCountryside,
        HinterlandScenery.Fields => Strings.SceneryFields,
        HinterlandScenery.Town => Strings.SceneryTown,
        _ => scenery.ToString(),
    };

    /// <summary>Name of a divider, e.g. "Mooring piles".</summary>
    public static string GetDisplayName(this DividerType type) => type switch
    {
        DividerType.FingerPier => Strings.DividerFingerPier,
        DividerType.Piles => Strings.DividerPiles,
        DividerType.Boom => Strings.DividerBoom,
        DividerType.SinglePile => Strings.DividerSinglePile,
        _ => type.ToString(),
    };

    /// <summary>Name of a mooring style: "Alongside" or "Bow-in".</summary>
    public static string GetDisplayName(this MooringStyle style) => style switch
    {
        MooringStyle.Alongside => Strings.MooringAlongside,
        MooringStyle.BowIn => Strings.MooringBowIn,
        _ => style.ToString(),
    };
}
