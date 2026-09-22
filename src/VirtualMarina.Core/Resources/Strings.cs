using System.Globalization;
using System.Resources;

namespace VirtualMarina.Core.Resources;

/// <summary>
/// Displayed text of the core library: status and type names, the default tooltip, the designer tool hints
/// and the undo step descriptions. See <see cref="Api.MarinaLocalization"/> for choosing the language.
/// </summary>
internal static class Strings
{
    private static readonly ResourceManager Manager = new("VirtualMarina.Core.Resources.Strings", typeof(Strings).Assembly);

    /// <summary>Culture used to look the text up; null (the default) follows <see cref="CultureInfo.CurrentUICulture"/>.</summary>
    internal static CultureInfo? Culture { get; set; }

    /// <summary>The text of a resource by name, or the name itself when the resource is missing.</summary>
    internal static string Get(string name) => Manager.GetString(name, Culture) ?? name;

    /// <summary>Fills the placeholders of a localized format string using the current culture.</summary>
    internal static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    /// <summary>The neutral language plus every culture a Strings.&lt;culture&gt;.resx (or a host satellite assembly) supplies.</summary>
    internal static IReadOnlyList<CultureInfo> AvailableCultures()
    {
        var found = new List<CultureInfo> { CultureInfo.InvariantCulture };
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.AllCultures))
        {
            if (culture.Equals(CultureInfo.InvariantCulture)) continue;
            // Only the culture itself, never its parents: that is what tells a real translation from a fallback.
            if (Manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false) is not null) found.Add(culture);
        }

        return found;
    }

    // ---- Berth status --------------------------------------------------------------------------------

    /// <summary>"Free"</summary>
    internal static string StatusFree => Get("StatusFree");

    /// <summary>"Occupied"</summary>
    internal static string StatusOccupied => Get("StatusOccupied");

    /// <summary>"Reserved"</summary>
    internal static string StatusReserved => Get("StatusReserved");

    /// <summary>"Temporarily Free"</summary>
    internal static string StatusTemporarilyFree => Get("StatusTemporarilyFree");

    // ---- Berth label modes ---------------------------------------------------------------------------

    /// <summary>"None"</summary>
    internal static string BerthLabelsNone => Get("BerthLabelsNone");

    /// <summary>"Only free"</summary>
    internal static string BerthLabelsOnlyFree => Get("BerthLabelsOnlyFree");

    /// <summary>"Non-occupied"</summary>
    internal static string BerthLabelsNonOccupied => Get("BerthLabelsNonOccupied");

    /// <summary>"All"</summary>
    internal static string BerthLabelsAll => Get("BerthLabelsAll");

    // ---- Boat types ----------------------------------------------------------------------------------

    /// <summary>"Monohull Sailboat"</summary>
    internal static string BoatTypeMonohullSailboat => Get("BoatTypeMonohullSailboat");

    /// <summary>"Catamaran Sailboat"</summary>
    internal static string BoatTypeCatamaranSailboat => Get("BoatTypeCatamaranSailboat");

    /// <summary>"Day Motor Boat"</summary>
    internal static string BoatTypeDayMotorBoat => Get("BoatTypeDayMotorBoat");

    /// <summary>"Catamaran Motorboat"</summary>
    internal static string BoatTypeCatamaranMotorboat => Get("BoatTypeCatamaranMotorboat");

    /// <summary>"Motor Yacht"</summary>
    internal static string BoatTypeMotorYacht => Get("BoatTypeMotorYacht");

    /// <summary>"Fishing Boat"</summary>
    internal static string BoatTypeFishingBoat => Get("BoatTypeFishingBoat");

    /// <summary>"Jet Ski"</summary>
    internal static string BoatTypeJetSki => Get("BoatTypeJetSki");

    // ---- Pier types ----------------------------------------------------------------------------------

    /// <summary>"Floating (wooden)"</summary>
    internal static string PierTypeFloatingWooden => Get("PierTypeFloatingWooden");

    /// <summary>"Floating (concrete)"</summary>
    internal static string PierTypeFloatingConcrete => Get("PierTypeFloatingConcrete");

    /// <summary>"Fixed concrete"</summary>
    internal static string PierTypeConcrete => Get("PierTypeConcrete");

    // ---- Mooring styles ------------------------------------------------------------------------------

    /// <summary>"Alongside"</summary>
    internal static string MooringAlongside => Get("MooringAlongside");

    /// <summary>"Bow-in"</summary>
    internal static string MooringBowIn => Get("MooringBowIn");

    // ---- Default tooltip -----------------------------------------------------------------------------

    /// <summary>"On land · {0}"</summary>
    internal static string TooltipOnLand => Get("TooltipOnLand");

    /// <summary>"Status"</summary>
    internal static string TooltipStatus => Get("TooltipStatus");

    /// <summary>"Berth size"</summary>
    internal static string TooltipBerthSize => Get("TooltipBerthSize");

    /// <summary>"{0:0.0} × {1:0.0} m"</summary>
    internal static string TooltipSize => Get("TooltipSize");

    /// <summary>", draft {0:0.0} m"</summary>
    internal static string TooltipDraftSuffix => Get("TooltipDraftSuffix");

    /// <summary>"Boat"</summary>
    internal static string TooltipBoat => Get("TooltipBoat");

    /// <summary>"Boat type"</summary>
    internal static string TooltipBoatType => Get("TooltipBoatType");

    /// <summary>"Boat size"</summary>
    internal static string TooltipBoatSize => Get("TooltipBoatSize");

    /// <summary>"Owner"</summary>
    internal static string TooltipOwner => Get("TooltipOwner");

    /// <summary>"Registration"</summary>
    internal static string TooltipRegistration => Get("TooltipRegistration");

    /// <summary>"Returns"</summary>
    internal static string TooltipReturns => Get("TooltipReturns");

    /// <summary>"Expected"</summary>
    internal static string TooltipExpected => Get("TooltipExpected");

    /// <summary>"Boat lies"</summary>
    internal static string TooltipBoatLies => Get("TooltipBoatLies");

    /// <summary>"{0} across {1}"</summary>
    internal static string TooltipAcrossBerths => Get("TooltipAcrossBerths");

    /// <summary>"Access"</summary>
    internal static string TooltipAccess => Get("TooltipAccess");

    /// <summary>"Read-only"</summary>
    internal static string TooltipReadOnly => Get("TooltipReadOnly");

    /// <summary>"{0} berths selected"</summary>
    internal static string TooltipBerthsSelected => Get("TooltipBerthsSelected");

    /// <summary>"{0} · {1}"</summary>
    internal static string TooltipBerthAndBoat => Get("TooltipBerthAndBoat");

    /// <summary>"and {0} more"</summary>
    internal static string TooltipAndMore => Get("TooltipAndMore");

    // ---- Design tools --------------------------------------------------------------------------------

    /// <summary>"Navigate"</summary>
    internal static string ToolNavigate => Get("ToolNavigate");

    /// <summary>"Land area"</summary>
    internal static string ToolDrawLandArea => Get("ToolDrawLandArea");

    /// <summary>"Pier"</summary>
    internal static string ToolDrawPier => Get("ToolDrawPier");

    /// <summary>"Berths"</summary>
    internal static string ToolAddBerths => Get("ToolAddBerths");

    /// <summary>"Land berths"</summary>
    internal static string ToolAddLandBerths => Get("ToolAddLandBerths");

    /// <summary>"Trees"</summary>
    internal static string ToolPlantTrees => Get("ToolPlantTrees");

    /// <summary>"Erase"</summary>
    internal static string ToolErase => Get("ToolErase");

    /// <summary>"Rename"</summary>
    internal static string ToolRename => Get("ToolRename");

    /// <summary>"Move image"</summary>
    internal static string ToolMoveReferenceImage => Get("ToolMoveReferenceImage");

    /// <summary>"Draw scale line"</summary>
    internal static string ToolMeasureScale => Get("ToolMeasureScale");

    // ---- Berth separators ----------------------------------------------------------------------------

    /// <summary>"Own finger piers"</summary>
    internal static string SeparatorFingerPiers => Get("SeparatorFingerPiers");

    /// <summary>"Nothing (gap only)"</summary>
    internal static string SeparatorNone => Get("SeparatorNone");

    /// <summary>"Pier between all"</summary>
    internal static string SeparatorFingerPier => Get("SeparatorFingerPier");

    /// <summary>"Pier every other berth"</summary>
    internal static string SeparatorPairedFingerPiers => Get("SeparatorPairedFingerPiers");

    /// <summary>"Mooring piles"</summary>
    internal static string SeparatorPiles => Get("SeparatorPiles");

    /// <summary>"Floating boom"</summary>
    internal static string SeparatorBoom => Get("SeparatorBoom");

    /// <summary>"Single pile at the end"</summary>
    internal static string SeparatorSinglePile => Get("SeparatorSinglePile");

    // ---- Pier services -------------------------------------------------------------------------------

    /// <summary>"None"</summary>
    internal static string ServicesNone => Get("ServicesNone");

    /// <summary>"Power only"</summary>
    internal static string ServicesPower => Get("ServicesPower");

    /// <summary>"Water only"</summary>
    internal static string ServicesWater => Get("ServicesWater");

    /// <summary>"Power and water"</summary>
    internal static string ServicesPowerAndWater => Get("ServicesPowerAndWater");

    // ---- Berthing sides ------------------------------------------------------------------------------

    /// <summary>"Boats on both sides"</summary>
    internal static string PierSidesBoth => Get("PierSidesBoth");

    /// <summary>"Boats on the left only"</summary>
    internal static string PierSidesLeft => Get("PierSidesLeft");

    /// <summary>"Boats on the right only"</summary>
    internal static string PierSidesRight => Get("PierSidesRight");

    // ---- Land surfaces -------------------------------------------------------------------------------

    /// <summary>"Quay or pier head"</summary>
    internal static string LandKindQuay => Get("LandKindQuay");

    /// <summary>"Rock breakwater"</summary>
    internal static string LandKindBreakwater => Get("LandKindBreakwater");

    /// <summary>"Lawn or park"</summary>
    internal static string LandKindGrass => Get("LandKindGrass");

    // ---- Divider types -------------------------------------------------------------------------------

    /// <summary>"Finger pier"</summary>
    internal static string DividerFingerPier => Get("DividerFingerPier");

    /// <summary>"Mooring piles"</summary>
    internal static string DividerPiles => Get("DividerPiles");

    /// <summary>"Floating boom"</summary>
    internal static string DividerBoom => Get("DividerBoom");

    /// <summary>"Single pile"</summary>
    internal static string DividerSinglePile => Get("DividerSinglePile");

    // ---- Designer tool hints -------------------------------------------------------------------------

    /// <summary>"Click to place the first corner of the land area."</summary>
    internal static string HintDrawLandAreaFirst => Get("HintDrawLandAreaFirst");

    /// <summary>"Click to add corners (at least 3). Backspace removes the last one, Esc cancels."</summary>
    internal static string HintDrawLandAreaCorners => Get("HintDrawLandAreaCorners");

    /// <summary>"Click to add corners; double-click, Enter, right-click or click the first corner to finish."</summary>
    internal static string HintDrawLandAreaFinish => Get("HintDrawLandAreaFinish");

    /// <summary>"Click the shore end of the pier."</summary>
    internal static string HintDrawPierStart => Get("HintDrawPierStart");

    /// <summary>"Click the far end of the pier (Shift: 15° steps). Esc cancels."</summary>
    internal static string HintDrawPierEnd => Get("HintDrawPierEnd");

    /// <summary>"Click beside a pier where the first berth goes."</summary>
    internal static string HintAddBerthsStart => Get("HintAddBerthsStart");

    /// <summary>"Click where the row of berths ends (same spot: one berth). Esc cancels."</summary>
    internal static string HintAddBerthsEnd => Get("HintAddBerthsEnd");

    /// <summary>"Click a land area where the boat should stand."</summary>
    internal static string HintAddLandBerthsStart => Get("HintAddLandBerthsStart");

    /// <summary>"Click where the bow should point (Shift: 15° steps; same spot: the set heading). Esc cancels."</summary>
    internal static string HintAddLandBerthsHeading => Get("HintAddLandBerthsHeading");

    /// <summary>"Click a berth, pier or land area to remove it."</summary>
    internal static string HintErase => Get("HintErase");

    /// <summary>"Click a berth or a pier to give it another name."</summary>
    internal static string HintRename => Get("HintRename");

    /// <summary>"Click a lawn to scatter trees on it (replacing the ones it has); Ctrl+click or right-click re..."</summary>
    internal static string HintPlantTrees => Get("HintPlantTrees");

    /// <summary>"Tree coverage is 0: click a lawn to remove its trees."</summary>
    internal static string HintPlantTreesNone => Get("HintPlantTreesNone");

    /// <summary>"Load a reference image first."</summary>
    internal static string HintReferenceImageMissing => Get("HintReferenceImageMissing");

    /// <summary>"Drag with the left mouse button to move the image."</summary>
    internal static string HintMoveReferenceImage => Get("HintMoveReferenceImage");

    /// <summary>"Click one end of the image's scale bar."</summary>
    internal static string HintMeasureScaleFirst => Get("HintMeasureScaleFirst");

    /// <summary>"Click the other end of the scale bar."</summary>
    internal static string HintMeasureScaleSecond => Get("HintMeasureScaleSecond");

    /// <summary>"Drag to pan, right-drag to orbit, wheel to zoom."</summary>
    internal static string HintNavigate => Get("HintNavigate");

    // ---- Undo step descriptions ----------------------------------------------------------------------

    /// <summary>"Draw {0}"</summary>
    internal static string UndoDrawLandArea => Get("UndoDrawLandArea");

    /// <summary>"Draw pier {0}"</summary>
    internal static string UndoDrawPier => Get("UndoDrawPier");

    /// <summary>"Add berth {0}"</summary>
    internal static string UndoAddBerth => Get("UndoAddBerth");

    /// <summary>"Add {0} berths"</summary>
    internal static string UndoAddBerths => Get("UndoAddBerths");

    /// <summary>"Add land berth {0}"</summary>
    internal static string UndoAddLandBerth => Get("UndoAddLandBerth");

    /// <summary>"Plant trees"</summary>
    internal static string UndoPlantTrees => Get("UndoPlantTrees");

    /// <summary>"Remove trees"</summary>
    internal static string UndoRemoveTrees => Get("UndoRemoveTrees");

    /// <summary>"{0} on {1}"</summary>
    internal static string UndoTreesOn => Get("UndoTreesOn");

    /// <summary>"Erase {0}"</summary>
    internal static string UndoErase => Get("UndoErase");

    /// <summary>"Rename {0} to {1}"</summary>
    internal static string UndoRenameBerth => Get("UndoRenameBerth");

    /// <summary>"Rename pier {0} to {1}"</summary>
    internal static string UndoRenamePier => Get("UndoRenamePier");

    /// <summary>"berth {0}"</summary>
    internal static string ElementBerth => Get("ElementBerth");

    /// <summary>"pier {0}"</summary>
    internal static string ElementPier => Get("ElementPier");
}
