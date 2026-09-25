using System.Globalization;
using VirtualMarina.Resources;

namespace VirtualMarina.Core.Resources;

/// <summary>
/// Displayed text of the core library: status and type names, the default tooltip, the designer tool hints
/// and the undo step descriptions. See <see cref="Api.MarinaLocalization"/> for choosing the language.
/// </summary>
internal static class Strings
{
    private static readonly ResourceText Text = new("VirtualMarina.Core.Resources.Strings", typeof(Strings).Assembly);

    /// <summary>Culture used to look the text up; null (the default) follows <see cref="CultureInfo.CurrentUICulture"/>.</summary>
    internal static CultureInfo? Culture { get => Text.Culture; set => Text.Culture = value; }

    /// <summary>The text of a resource by name, or the name itself when the resource is missing.</summary>
    internal static string Get(string name) => Text.Get(name);

    /// <summary>Fills the placeholders of a localized format string using the current culture.</summary>
    internal static string Format(string format, params object?[] args) => ResourceText.Format(format, args);

    /// <summary>
    /// A text about a number of things, in the singular or the plural as the display language wants for
    /// <paramref name="count"/>: the resource <c><paramref name="key"/>_One</c> or <c><paramref name="key"/>_Other</c>,
    /// with its placeholders filled from <paramref name="args"/> (which include the count wherever the text shows it).
    /// </summary>
    /// <remarks>
    /// Two forms cover English and most Western European languages; French and Portuguese take the singular for 0 as
    /// well. A language with more forms (Polish, Russian, Arabic) gets its nearest two until more keys are added.
    /// </remarks>
    internal static string Plural(string key, int count, params object?[] args) =>
        Format(Get(key + (TakesSingular(count) ? "_One" : "_Other")), args);

    private static bool TakesSingular(int count) =>
        (Culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName is "fr" or "pt" ? count is 0 or 1 : count == 1;

    /// <summary>The neutral language plus every culture a Strings.&lt;culture&gt;.resx (or a host satellite assembly) supplies.</summary>
    internal static IReadOnlyList<CultureInfo> AvailableCultures() => Text.AvailableCultures();

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

    /// <summary>"Ferry"</summary>
    internal static string BoatTypeFerry => Get("BoatTypeFerry");

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

    /// <summary>"{0} berth selected" (see <see cref="Plural"/>)</summary>
    internal static string TooltipBerthsSelectedOne => Get("TooltipBerthsSelected_One");

    /// <summary>"{0} berths selected" (see <see cref="Plural"/>)</summary>
    internal static string TooltipBerthsSelectedOther => Get("TooltipBerthsSelected_Other");

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

    /// <summary>"Pedestals"</summary>
    internal static string ToolEditServices => Get("ToolEditServices");

    /// <summary>"Select"</summary>
    internal static string ToolSelectArea => Get("ToolSelectArea");

    /// <summary>"Move image"</summary>
    internal static string ToolMoveReferenceImage => Get("ToolMoveReferenceImage");

    /// <summary>"Draw scale line"</summary>
    internal static string ToolMeasureScale => Get("ToolMeasureScale");

    /// <summary>"Coast"</summary>
    internal static string ToolDrawShoreline => Get("ToolDrawShoreline");

    /// <summary>"Dividers"</summary>
    internal static string ToolPlaceDividers => Get("ToolPlaceDividers");

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

    // ---- Hinterland scenery --------------------------------------------------------------------------

    /// <summary>"Bare ground"</summary>
    internal static string SceneryNone => Get("SceneryNone");

    /// <summary>"Countryside"</summary>
    internal static string SceneryCountryside => Get("SceneryCountryside");

    /// <summary>"Fields"</summary>
    internal static string SceneryFields => Get("SceneryFields");

    /// <summary>"Town"</summary>
    internal static string SceneryTown => Get("SceneryTown");

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

    /// <summary>"Click the far end of the pier. It squares up with the quay and the piers already there; hold ..."</summary>
    internal static string HintDrawPierEnd => Get("HintDrawPierEnd");

    /// <summary>"Click beside a pier where the first berth goes."</summary>
    internal static string HintAddBerthsStart => Get("HintAddBerthsStart");

    /// <summary>"Click where the row of berths ends (same spot: one berth). Esc cancels."</summary>
    internal static string HintAddBerthsEnd => Get("HintAddBerthsEnd");

    /// <summary>"Click a land area where the boat should stand."</summary>
    internal static string HintAddLandBerthsStart => Get("HintAddLandBerthsStart");

    /// <summary>"Click where the bow should point (Shift: 15° steps; same spot: the set heading). Esc cancels."</summary>
    internal static string HintAddLandBerthsHeading => Get("HintAddLandBerthsHeading");

    /// <summary>"Click a berth, pier or land area to remove it. Alt+click a berth to clear that whole pier."</summary>
    internal static string HintErase => Get("HintErase");

    /// <summary>"Click a berth or a pier to give it another name."</summary>
    internal static string HintRename => Get("HintRename");

    /// <summary>"Drag a box over the water to select the berths inside it; hold Shift or Ctrl to add to the on..."</summary>
    internal static string HintSelectArea => Get("HintSelectArea");

    /// <summary>"Click a berth to give it the pedestals chosen above; Alt or Ctrl changes that whole side of t..."</summary>
    internal static string HintEditServices => Get("HintEditServices");

    /// <summary>"Click beside a row of berths to put a divider on the nearest boundary ..."</summary>
    internal static string HintPlaceDividers => Get("HintPlaceDividers");

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

    /// <summary>"Click to place the first point of the coast (two points are enough for a straight one)."</summary>
    internal static string HintDrawShorelineFirst => Get("HintDrawShorelineFirst");

    /// <summary>"Click to add points along the coast, then press Enter. Backspace removes the last one, Esc ca..."</summary>
    internal static string HintDrawShorelineMore => Get("HintDrawShorelineMore");

    /// <summary>"Click the side of the line that is land."</summary>
    internal static string HintDrawShorelineSide => Get("HintDrawShorelineSide");

    /// <summary>"The two ends of this line run into each other, so neither side of it is the land. Move a poin..."</summary>
    internal static string HintDrawShorelineCrossing => Get("HintDrawShorelineCrossing");

    /// <summary>"Drag to pan, right-drag to orbit, wheel to zoom."</summary>
    internal static string HintNavigate => Get("HintNavigate");

    // ---- Designer overlay labels ---------------------------------------------------------------------

    /// <summary>"LAND THIS SIDE"</summary>
    internal static string OverlayLandThisSide => Get("OverlayLandThisSide");

    /// <summary>"{0} TREES"</summary>
    internal static string OverlayTreeCount => Get("OverlayTreeCount");

    /// <summary>"{0} M"</summary>
    internal static string OverlayMeters => Get("OverlayMeters");

    /// <summary>"1 BERTH {0} X {1} M"</summary>
    internal static string OverlayOneBerth => Get("OverlayOneBerth");

    /// <summary>"{0} BERTHS"</summary>
    internal static string OverlayBerthCount => Get("OverlayBerthCount");

    /// <summary>"{0} X {1} M AT {2}"</summary>
    internal static string OverlayLandBerth => Get("OverlayLandBerth");

    /// <summary>"N"</summary>
    internal static string OverlayNorth => Get("OverlayNorth");

    // ---- Berth naming problems -----------------------------------------------------------------------

    /// <summary>"The berth naming pattern must not be empty."</summary>
    internal static string NamingPatternEmpty => Get("NamingPatternEmpty");

    /// <summary>"The land berth naming pattern must not be empty."</summary>
    internal static string NamingLandPatternEmpty => Get("NamingLandPatternEmpty");

    /// <summary>"The berth numbering increment must not be 0."</summary>
    internal static string NamingIncrementZero => Get("NamingIncrementZero");

    /// <summary>"The berth numbering must be padded to between 1 and 9 digits."</summary>
    internal static string NamingDigitsOutOfRange => Get("NamingDigitsOutOfRange");

    /// <summary>"The numbering increment of the slots ashore must not be 0."</summary>
    internal static string NamingLandIncrementZero => Get("NamingLandIncrementZero");

    /// <summary>"The slots ashore must be padded to between 1 and 9 digits."</summary>
    internal static string NamingLandDigitsOutOfRange => Get("NamingLandDigitsOutOfRange");

    // ---- Undo step descriptions ----------------------------------------------------------------------

    /// <summary>"Draw {0}"</summary>
    internal static string UndoDrawLandArea => Get("UndoDrawLandArea");

    /// <summary>"Draw pier {0}"</summary>
    internal static string UndoDrawPier => Get("UndoDrawPier");

    /// <summary>"Draw the mainland"</summary>
    internal static string UndoDrawShoreline => Get("UndoDrawShoreline");

    /// <summary>"Remove the mainland"</summary>
    internal static string UndoRemoveShoreline => Get("UndoRemoveShoreline");

    /// <summary>"Add berth {0}"</summary>
    internal static string UndoAddBerth => Get("UndoAddBerth");

    /// <summary>"Add {0} berth" (see <see cref="Plural"/>)</summary>
    internal static string UndoAddBerthsOne => Get("UndoAddBerths_One");

    /// <summary>"Add {0} berths" (see <see cref="Plural"/>)</summary>
    internal static string UndoAddBerthsOther => Get("UndoAddBerths_Other");

    /// <summary>"Add land berth {0}"</summary>
    internal static string UndoAddLandBerth => Get("UndoAddLandBerth");

    /// <summary>"Plant trees"</summary>
    internal static string UndoPlantTrees => Get("UndoPlantTrees");

    /// <summary>"Remove trees"</summary>
    internal static string UndoRemoveTrees => Get("UndoRemoveTrees");

    /// <summary>"{0} on {1}"</summary>
    internal static string UndoTreesOn => Get("UndoTreesOn");

    /// <summary>"Erase {0} berth from {1}" (see <see cref="Plural"/>)</summary>
    internal static string UndoEraseBerthsOfPierOne => Get("UndoEraseBerthsOfPier_One");

    /// <summary>"Erase {0} berths from {1}" (see <see cref="Plural"/>)</summary>
    internal static string UndoEraseBerthsOfPierOther => Get("UndoEraseBerthsOfPier_Other");

    /// <summary>"Erase {0}"</summary>
    internal static string UndoErase => Get("UndoErase");

    /// <summary>"Erase {0} berth" (see <see cref="Plural"/>)</summary>
    internal static string UndoEraseBerthsOne => Get("UndoEraseBerths_One");

    /// <summary>"Erase {0} berths" (see <see cref="Plural"/>)</summary>
    internal static string UndoEraseBerthsOther => Get("UndoEraseBerths_Other");

    /// <summary>"Undo {0}"</summary>
    internal static string UndoFailed => Get("UndoFailed");

    /// <summary>"Redo {0}"</summary>
    internal static string RedoFailed => Get("RedoFailed");

    /// <summary>"Rename {0} to {1}"</summary>
    internal static string UndoRenameBerth => Get("UndoRenameBerth");

    /// <summary>"Rename {0} berth on {1}" (see <see cref="Plural"/>)</summary>
    internal static string UndoRenumberBerthsOne => Get("UndoRenumberBerths_One");

    /// <summary>"Rename {0} berths on {1}" (see <see cref="Plural"/>)</summary>
    internal static string UndoRenumberBerthsOther => Get("UndoRenumberBerths_Other");

    /// <summary>"Set {0} on {1} berth" (see <see cref="Plural"/>)</summary>
    internal static string UndoSetServicesOne => Get("UndoSetServices_One");

    /// <summary>"Set {0} on {1} berths" (see <see cref="Plural"/>)</summary>
    internal static string UndoSetServicesOther => Get("UndoSetServices_Other");

    /// <summary>"Place {0} divider" (see <see cref="Plural"/>)</summary>
    internal static string UndoPlaceDividersOne => Get("UndoPlaceDividers_One");

    /// <summary>"Place {0} dividers" (see <see cref="Plural"/>)</summary>
    internal static string UndoPlaceDividersOther => Get("UndoPlaceDividers_Other");

    /// <summary>"Remove {0} divider" (see <see cref="Plural"/>)</summary>
    internal static string UndoRemoveDividersOne => Get("UndoRemoveDividers_One");

    /// <summary>"Remove {0} dividers" (see <see cref="Plural"/>)</summary>
    internal static string UndoRemoveDividersOther => Get("UndoRemoveDividers_Other");

    /// <summary>"Change pier id {0} to {1}"</summary>
    internal static string UndoChangePierId => Get("UndoChangePierId");

    /// <summary>"Rename pier {0} to {1}"</summary>
    internal static string UndoRenamePier => Get("UndoRenamePier");

    /// <summary>"berth {0}"</summary>
    internal static string ElementBerth => Get("ElementBerth");

    /// <summary>"pier {0}"</summary>
    internal static string ElementPier => Get("ElementPier");

    // ---- Camera presets ------------------------------------------------------------------------------

    /// <summary>"Overview"</summary>
    internal static string CameraPresetOverview => Get("CameraPresetOverview");

    /// <summary>"The whole marina"</summary>
    internal static string CameraPresetOverviewDescription => Get("CameraPresetOverviewDescription");

    /// <summary>"Top Down"</summary>
    internal static string CameraPresetTopDown => Get("CameraPresetTopDown");

    /// <summary>"Straight down, north up"</summary>
    internal static string CameraPresetTopDownDescription => Get("CameraPresetTopDownDescription");

    /// <summary>"North"</summary>
    internal static string CameraPresetNorth => Get("CameraPresetNorth");

    /// <summary>"From the north"</summary>
    internal static string CameraPresetNorthDescription => Get("CameraPresetNorthDescription");

    /// <summary>"East"</summary>
    internal static string CameraPresetEast => Get("CameraPresetEast");

    /// <summary>"From the east"</summary>
    internal static string CameraPresetEastDescription => Get("CameraPresetEastDescription");

    /// <summary>"South"</summary>
    internal static string CameraPresetSouth => Get("CameraPresetSouth");

    /// <summary>"From the south"</summary>
    internal static string CameraPresetSouthDescription => Get("CameraPresetSouthDescription");

    /// <summary>"West"</summary>
    internal static string CameraPresetWest => Get("CameraPresetWest");

    /// <summary>"From the west"</summary>
    internal static string CameraPresetWestDescription => Get("CameraPresetWestDescription");

    /// <summary>"Pier: {0}"</summary>
    internal static string CameraPresetPier => Get("CameraPresetPier");

    /// <summary>"Pier: {0} ({1})"</summary>
    internal static string CameraPresetPierWithId => Get("CameraPresetPierWithId");

    /// <summary>"Close-up of {0}"</summary>
    internal static string CameraPresetPierDescription => Get("CameraPresetPierDescription");

    // ---- Validation and file errors ------------------------------------------------------------------

    /// <summary>"Boat id must not be empty."</summary>
    internal static string ErrorBoatIdEmpty => Get("ErrorBoatIdEmpty");

    /// <summary>"Boat '{0}' has an unknown type '{1}'."</summary>
    internal static string ErrorBoatUnknownType => Get("ErrorBoatUnknownType");

    /// <summary>"Boat '{0}' must have a positive, finite length."</summary>
    internal static string ErrorBoatLength => Get("ErrorBoatLength");

    /// <summary>"Boat '{0}' must have a positive, finite beam."</summary>
    internal static string ErrorBoatBeam => Get("ErrorBoatBeam");

    /// <summary>"Divider id must not be empty."</summary>
    internal static string ErrorDividerIdEmpty => Get("ErrorDividerIdEmpty");

    /// <summary>"Divider '{0}' must have a positive, finite length."</summary>
    internal static string ErrorDividerLength => Get("ErrorDividerLength");

    /// <summary>"Divider '{0}' must have a positive, finite width."</summary>
    internal static string ErrorDividerWidth => Get("ErrorDividerWidth");

    /// <summary>"Divider '{0}' spacing must be at least 0.5 m and finite."</summary>
    internal static string ErrorDividerSpacing => Get("ErrorDividerSpacing");

    /// <summary>"Divider '{0}' has a non-finite position or heading."</summary>
    internal static string ErrorDividerPosition => Get("ErrorDividerPosition");

    /// <summary>"Divider '{0}' has an unknown type '{1}'."</summary>
    internal static string ErrorDividerUnknownType => Get("ErrorDividerUnknownType");

    /// <summary>"Berth id must not be empty."</summary>
    internal static string ErrorBerthIdEmpty => Get("ErrorBerthIdEmpty");

    /// <summary>"Berth '{0}' has an empty land area id."</summary>
    internal static string ErrorBerthEmptyLandAreaId => Get("ErrorBerthEmptyLandAreaId");

    /// <summary>"Berth '{0}' cannot belong to both a pier and a land area."</summary>
    internal static string ErrorBerthPierAndLand => Get("ErrorBerthPierAndLand");

    /// <summary>"Berth '{0}' must reference a pier or a land area."</summary>
    internal static string ErrorBerthNoPlace => Get("ErrorBerthNoPlace");

    /// <summary>"Berth '{0}' must have a positive, finite length."</summary>
    internal static string ErrorBerthLength => Get("ErrorBerthLength");

    /// <summary>"Berth '{0}' must have a positive, finite width."</summary>
    internal static string ErrorBerthWidth => Get("ErrorBerthWidth");

    /// <summary>"Berth '{0}' has a non-finite position."</summary>
    internal static string ErrorBerthPosition => Get("ErrorBerthPosition");

    /// <summary>"Berth '{0}' has a non-finite heading."</summary>
    internal static string ErrorBerthHeading => Get("ErrorBerthHeading");

    /// <summary>"Berth '{0}' has an unknown status '{1}'."</summary>
    internal static string ErrorBerthUnknownStatus => Get("ErrorBerthUnknownStatus");

    /// <summary>"Berth '{0}' must have an ExternalData dictionary."</summary>
    internal static string ErrorBerthNoExternalData => Get("ErrorBerthNoExternalData");

    /// <summary>"Berth '{0}': {1}"</summary>
    internal static string ErrorBerthBoat => Get("ErrorBerthBoat");

    /// <summary>"Land area id must not be empty."</summary>
    internal static string ErrorLandIdEmpty => Get("ErrorLandIdEmpty");

    /// <summary>"Land area '{0}' needs at least three points."</summary>
    internal static string ErrorLandTooFewPoints => Get("ErrorLandTooFewPoints");

    /// <summary>"Land area '{0}' has a non-finite point."</summary>
    internal static string ErrorLandNonFinitePoint => Get("ErrorLandNonFinitePoint");

    /// <summary>"Land area '{0}' outline must not cross itself or repeat points."</summary>
    internal static string ErrorLandCrossesItself => Get("ErrorLandCrossesItself");

    /// <summary>"Land area '{0}' outline has no area."</summary>
    internal static string ErrorLandNoArea => Get("ErrorLandNoArea");

    /// <summary>"Land area '{0}' height must be between 0 and 50 m."</summary>
    internal static string ErrorLandHeight => Get("ErrorLandHeight");

    /// <summary>"Land area '{0}' has a tree with a non-finite position, a height outside 1–40 m or a crown radius outside 0.3–15 m."</summary>
    internal static string ErrorLandTree => Get("ErrorLandTree");

    /// <summary>"Land area '{0}' has an unknown kind '{1}'."</summary>
    internal static string ErrorLandUnknownKind => Get("ErrorLandUnknownKind");

    /// <summary>"Marine traffic can show between 1 and {0} vessels at once."</summary>
    internal static string ErrorTrafficVessels => Get("ErrorTrafficVessels");

    /// <summary>"Marine traffic can run between 1 and {0} lanes."</summary>
    internal static string ErrorTrafficLanes => Get("ErrorTrafficLanes");

    /// <summary>"Marine traffic lane spacing must be a positive distance."</summary>
    internal static string ErrorTrafficLaneSpacing => Get("ErrorTrafficLaneSpacing");

    /// <summary>"Marine traffic clearance must not be negative."</summary>
    internal static string ErrorTrafficClearance => Get("ErrorTrafficClearance");

    /// <summary>"Marine traffic edge clearance must not be negative."</summary>
    internal static string ErrorTrafficEdgeClearance => Get("ErrorTrafficEdgeClearance");

    /// <summary>"Marine traffic speed must be a positive percentage."</summary>
    internal static string ErrorTrafficSpeed => Get("ErrorTrafficSpeed");

    /// <summary>"Marine traffic spawn delay must not be negative."</summary>
    internal static string ErrorTrafficSpawnDelay => Get("ErrorTrafficSpawnDelay");

    /// <summary>"Marine traffic reach must be a positive distance."</summary>
    internal static string ErrorTrafficReach => Get("ErrorTrafficReach");

    /// <summary>"Marine traffic lists an unknown vessel type '{0}'."</summary>
    internal static string ErrorTrafficUnknownVessel => Get("ErrorTrafficUnknownVessel");

    /// <summary>"Multi-berth id must not be empty."</summary>
    internal static string ErrorMultiBerthIdEmpty => Get("ErrorMultiBerthIdEmpty");

    /// <summary>"Multi-berth '{0}' must span at least two berths."</summary>
    internal static string ErrorMultiBerthTooFew => Get("ErrorMultiBerthTooFew");

    /// <summary>"Multi-berth '{0}' contains an empty berth id."</summary>
    internal static string ErrorMultiBerthEmptyMember => Get("ErrorMultiBerthEmptyMember");

    /// <summary>"Multi-berth '{0}' lists a berth more than once."</summary>
    internal static string ErrorMultiBerthDuplicateMember => Get("ErrorMultiBerthDuplicateMember");

    /// <summary>"Multi-berth '{0}' must have a boat."</summary>
    internal static string ErrorMultiBerthNoBoat => Get("ErrorMultiBerthNoBoat");

    /// <summary>"Multi-berth '{0}': {1}"</summary>
    internal static string ErrorMultiBerthBoat => Get("ErrorMultiBerthBoat");

    /// <summary>"Multi-berth '{0}' status must be Occupied, Reserved or TemporarilyFree."</summary>
    internal static string ErrorMultiBerthStatus => Get("ErrorMultiBerthStatus");

    /// <summary>"Multi-berth '{0}' has an unknown mooring style '{1}'."</summary>
    internal static string ErrorMultiBerthUnknownStyle => Get("ErrorMultiBerthUnknownStyle");

    /// <summary>"Multi-berth '{0}' mixes berths on the water and on land; one boat cannot lie across both."</summary>
    internal static string ErrorMultiBerthWaterAndLand => Get("ErrorMultiBerthWaterAndLand");

    /// <summary>"Multi-berth '{0}' spans berths on different land areas; its berths must all be on the same one."</summary>
    internal static string ErrorMultiBerthSeveralLandAreas => Get("ErrorMultiBerthSeveralLandAreas");

    /// <summary>"Multi-berth '{0}' spans berths along different piers; its berths must all be along the same pier."</summary>
    internal static string ErrorMultiBerthSeveralPiers => Get("ErrorMultiBerthSeveralPiers");

    /// <summary>"Pier id must not be empty."</summary>
    internal static string ErrorPierIdEmpty => Get("ErrorPierIdEmpty");

    /// <summary>"Pier '{0}' must have a positive, finite length."</summary>
    internal static string ErrorPierLength => Get("ErrorPierLength");

    /// <summary>"Pier '{0}' must have a positive, finite width."</summary>
    internal static string ErrorPierWidth => Get("ErrorPierWidth");

    /// <summary>"Pier '{0}' piling spacing must be greater than 0.5 m and finite."</summary>
    internal static string ErrorPierPilingSpacing => Get("ErrorPierPilingSpacing");

    /// <summary>"Pier '{0}' has a non-finite position or heading."</summary>
    internal static string ErrorPierPosition => Get("ErrorPierPosition");

    /// <summary>"Pier '{0}' has an unknown type '{1}'."</summary>
    internal static string ErrorPierUnknownType => Get("ErrorPierUnknownType");

    /// <summary>"Pier '{0}' berthing sides must be Left, Right or Both."</summary>
    internal static string ErrorPierSides => Get("ErrorPierSides");

    /// <summary>"Pier '{0}' has unknown services '{1}'."</summary>
    internal static string ErrorPierUnknownServices => Get("ErrorPierUnknownServices");

    /// <summary>"Pier '{0}' deck height must be between 0 and 5 m."</summary>
    internal static string ErrorPierDeckHeight => Get("ErrorPierDeckHeight");

    /// <summary>"Piers contains a null entry."</summary>
    internal static string ErrorNullPier => Get("ErrorNullPier");

    /// <summary>"Duplicate pier id '{0}'."</summary>
    internal static string ErrorDuplicatePier => Get("ErrorDuplicatePier");

    /// <summary>"LandAreas contains a null entry."</summary>
    internal static string ErrorNullLandArea => Get("ErrorNullLandArea");

    /// <summary>"Duplicate land area id '{0}'."</summary>
    internal static string ErrorDuplicateLandArea => Get("ErrorDuplicateLandArea");

    /// <summary>"Berths contains a null entry."</summary>
    internal static string ErrorNullBerth => Get("ErrorNullBerth");

    /// <summary>"Duplicate berth id '{0}'."</summary>
    internal static string ErrorDuplicateBerth => Get("ErrorDuplicateBerth");

    /// <summary>"Berth '{0}' references unknown pier '{1}'."</summary>
    internal static string ErrorBerthUnknownPier => Get("ErrorBerthUnknownPier");

    /// <summary>"Berth '{0}' references unknown land area '{1}'."</summary>
    internal static string ErrorBerthUnknownLandArea => Get("ErrorBerthUnknownLandArea");

    /// <summary>"Dividers contains a null entry."</summary>
    internal static string ErrorNullDivider => Get("ErrorNullDivider");

    /// <summary>"Duplicate divider id '{0}'."</summary>
    internal static string ErrorDuplicateDivider => Get("ErrorDuplicateDivider");

    /// <summary>"Divider '{0}' references unknown pier '{1}'."</summary>
    internal static string ErrorDividerUnknownPier => Get("ErrorDividerUnknownPier");

    /// <summary>"MultiBerths contains a null entry."</summary>
    internal static string ErrorNullMultiBerth => Get("ErrorNullMultiBerth");

    /// <summary>"Duplicate multi-berth id '{0}'."</summary>
    internal static string ErrorDuplicateMultiBerth => Get("ErrorDuplicateMultiBerth");

    /// <summary>"Multi-berth '{0}' has the same id as a berth; they must differ."</summary>
    internal static string ErrorMultiBerthIdIsBerthId => Get("ErrorMultiBerthIdIsBerthId");

    /// <summary>"Multi-berth '{0}' references unknown berth '{1}'."</summary>
    internal static string ErrorMultiBerthUnknownBerth => Get("ErrorMultiBerthUnknownBerth");

    /// <summary>"Berth '{0}' belongs to both multi-berth '{1}' and multi-berth '{2}'."</summary>
    internal static string ErrorBerthInTwoMultiBerths => Get("ErrorBerthInTwoMultiBerths");

    /// <summary>"Berth '{0}' already belongs to multi-berth '{1}'. Release or update that one first."</summary>
    internal static string ErrorBerthAlreadyInMultiBerth => Get("ErrorBerthAlreadyInMultiBerth");

    /// <summary>"Unsupported marina element: {0}."</summary>
    internal static string ErrorUnsupportedMarinaElement => Get("ErrorUnsupportedMarinaElement");

    /// <summary>"'{0}' is a name the marina file format uses itself and can't be used for an extension; ..."</summary>
    internal static string ErrorReservedExtensionKey => Get("ErrorReservedExtensionKey");

    /// <summary>"A shoreline needs at least two points."</summary>
    internal static string ErrorShorelineTooFewPoints => Get("ErrorShorelineTooFewPoints");

    /// <summary>"Shoreline point {0} is not a finite position."</summary>
    internal static string ErrorShorelineNonFinitePoint => Get("ErrorShorelineNonFinitePoint");

    /// <summary>"A shoreline's two points must not be in the same place."</summary>
    internal static string ErrorShorelineTwoPointsTogether => Get("ErrorShorelineTwoPointsTogether");

    /// <summary>"Shoreline points {0} and {1} are in the same place."</summary>
    internal static string ErrorShorelinePointsTogether => Get("ErrorShorelinePointsTogether");

    /// <summary>"The shoreline crosses itself (segments {0} and {1}), so neither side of it is the land."</summary>
    internal static string ErrorShorelineCrossesItself => Get("ErrorShorelineCrossesItself");

    /// <summary>"The shoreline's two endless segments cross each other, so neither side of it is the land."</summary>
    internal static string ErrorShorelineEndsCross => Get("ErrorShorelineEndsCross");

    /// <summary>"The shoreline's endless segment before its first point runs back across the drawn line, so neither side of it is the land."</summary>
    internal static string ErrorShorelineStartCrosses => Get("ErrorShorelineStartCrosses");

    /// <summary>"The shoreline's endless segment after its last point runs back across the drawn line, so neither side of it is the land."</summary>
    internal static string ErrorShorelineEndCrosses => Get("ErrorShorelineEndCrosses");

    /// <summary>"This is a '{0}' file, not a {1} file."</summary>
    internal static string ErrorFileWrongFormat => Get("ErrorFileWrongFormat");

    /// <summary>"The file was written in format version {0}, which is newer than this application understands ({1}). Update the application to open it."</summary>
    internal static string ErrorFileTooNew => Get("ErrorFileTooNew");

    /// <summary>"The file is empty."</summary>
    internal static string ErrorFileEmpty => Get("ErrorFileEmpty");

    /// <summary>"The file is not a marina file."</summary>
    internal static string ErrorFileNotMarina => Get("ErrorFileNotMarina");

    /// <summary>"The file holds a value that can't be used: {0}"</summary>
    internal static string ErrorFileBadValue => Get("ErrorFileBadValue");

    /// <summary>"The file is not valid JSON: {0}"</summary>
    internal static string ErrorFileNotJson => Get("ErrorFileNotJson");

    // ---- Validation and file errors (visualizer) -----------------------------------------------------

    /// <summary>"Berth '{0}' has the same id as a multi-berth; they must differ."</summary>
    internal static string ErrorBerthIdIsMultiBerthId => Get("ErrorBerthIdIsMultiBerthId");
}
