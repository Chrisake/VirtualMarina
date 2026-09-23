using System.Globalization;
using VirtualMarina.Resources;

namespace VirtualMarina.Designer.Resources;

/// <summary>
/// Displayed text of the Designer application: its menus, toolbar, inspector, dialogs and log. The names of
/// the settings themselves come from the core library, through <see cref="Core.Api.DisplayNames"/>.
/// </summary>
internal static class Strings
{
    private static readonly ResourceText Text = new("VirtualMarina.Designer.Resources.Strings", typeof(Strings).Assembly);

    /// <summary>Culture used to look the text up; null (the default) follows <see cref="CultureInfo.CurrentUICulture"/>.</summary>
    internal static CultureInfo? Culture { get => Text.Culture; set => Text.Culture = value; }

    /// <summary>The text of a resource by name, or the name itself when the resource is missing.</summary>
    internal static string Get(string name) => Text.Get(name);

    /// <summary>Fills the placeholders of a localized format string using the current culture.</summary>
    internal static string Format(string format, params object?[] args) => ResourceText.Format(format, args);

    /// <summary>The neutral language plus every culture a Strings.&lt;culture&gt;.resx (or a host satellite assembly) supplies.</summary>
    internal static IReadOnlyList<CultureInfo> AvailableCultures() => Text.AvailableCultures();

    // ---- Application ---------------------------------------------------------------------------------

    /// <summary>"VirtualMarina Designer"</summary>
    internal static string AppName => Get("AppName");

    /// <summary>"{0}{1} — {2}"</summary>
    internal static string WindowTitle => Get("WindowTitle");

    /// <summary>"*"</summary>
    internal static string UnsavedMarker => Get("UnsavedMarker");

    /// <summary>"New marina"</summary>
    internal static string NewMarinaName => Get("NewMarinaName");

    /// <summary>"marina"</summary>
    internal static string DefaultFileName => Get("DefaultFileName");

    // ---- Menu: File ----------------------------------------------------------------------------------

    /// <summary>"&amp;File"</summary>
    internal static string MenuFile => Get("MenuFile");

    /// <summary>"&amp;New"</summary>
    internal static string MenuNew => Get("MenuNew");

    /// <summary>"&amp;Open…"</summary>
    internal static string MenuOpen => Get("MenuOpen");

    /// <summary>"&amp;Save"</summary>
    internal static string MenuSave => Get("MenuSave");

    /// <summary>"Save &amp;As…"</summary>
    internal static string MenuSaveAs => Get("MenuSaveAs");

    /// <summary>"Reference &amp;Image…"</summary>
    internal static string MenuLoadImage => Get("MenuLoadImage");

    /// <summary>"E&amp;xit"</summary>
    internal static string MenuExit => Get("MenuExit");

    // ---- Menu: Edit ----------------------------------------------------------------------------------

    /// <summary>"&amp;Edit"</summary>
    internal static string MenuEdit => Get("MenuEdit");

    /// <summary>"&amp;Undo"</summary>
    internal static string MenuUndo => Get("MenuUndo");

    /// <summary>"&amp;Cancel Drawing"</summary>
    internal static string MenuCancelDraft => Get("MenuCancelDraft");

    /// <summary>"&amp;Properties…"</summary>
    internal static string MenuMarinaProperties => Get("MenuMarinaProperties");

    // ---- Menu: View ----------------------------------------------------------------------------------

    /// <summary>"&amp;View"</summary>
    internal static string MenuView => Get("MenuView");

    /// <summary>"&amp;Top View"</summary>
    internal static string MenuTopView => Get("MenuTopView");

    /// <summary>"&amp;Fit Marina"</summary>
    internal static string MenuFitMarina => Get("MenuFitMarina");

    /// <summary>"Fit &amp;Image"</summary>
    internal static string MenuFitImage => Get("MenuFitImage");

    /// <summary>"Activity &amp;Log"</summary>
    internal static string MenuShowLog => Get("MenuShowLog");

    // ---- Menu: Marina and Help -----------------------------------------------------------------------

    /// <summary>"&amp;Marina"</summary>
    internal static string MenuMarina => Get("MenuMarina");

    /// <summary>"&amp;Appearance…"</summary>
    internal static string MenuAppearance => Get("MenuAppearance");

    /// <summary>"Berth &amp;Labels"</summary>
    internal static string MenuBerthLabels => Get("MenuBerthLabels");

    /// <summary>"&amp;Cameras…"</summary>
    internal static string MenuCameras => Get("MenuCameras");

    /// <summary>"&amp;Help"</summary>
    internal static string MenuHelp => Get("MenuHelp");

    /// <summary>"&amp;Shortcuts…"</summary>
    internal static string MenuShortcuts => Get("MenuShortcuts");

    /// <summary>"&amp;About"</summary>
    internal static string MenuAbout => Get("MenuAbout");

    // ---- Toolbar -------------------------------------------------------------------------------------

    /// <summary>"Navigate"</summary>
    internal static string ToolNavigate => Get("ToolNavigate");

    /// <summary>"Look around without changing anything (Esc)"</summary>
    internal static string ToolNavigateTip => Get("ToolNavigateTip");

    /// <summary>"Land"</summary>
    internal static string ToolLand => Get("ToolLand");

    /// <summary>"Draw a quay, a lawn or a breakwater"</summary>
    internal static string ToolLandTip => Get("ToolLandTip");

    /// <summary>"Piers"</summary>
    internal static string ToolPier => Get("ToolPier");

    /// <summary>"Draw a pier out from the shore"</summary>
    internal static string ToolPierTip => Get("ToolPierTip");

    /// <summary>"Berths"</summary>
    internal static string ToolBerths => Get("ToolBerths");

    /// <summary>"Fill a stretch of pier with berths"</summary>
    internal static string ToolBerthsTip => Get("ToolBerthsTip");

    /// <summary>"Ashore"</summary>
    internal static string ToolAshore => Get("ToolAshore");

    /// <summary>"Place berths ashore, for boats kept out of the water"</summary>
    internal static string ToolAshoreTip => Get("ToolAshoreTip");

    /// <summary>"Trees"</summary>
    internal static string ToolTrees => Get("ToolTrees");

    /// <summary>"Scatter trees over a lawn"</summary>
    internal static string ToolTreesTip => Get("ToolTreesTip");

    /// <summary>"Erase"</summary>
    internal static string ToolErase => Get("ToolErase");

    /// <summary>"Remove what you click"</summary>
    internal static string ToolEraseTip => Get("ToolEraseTip");

    /// <summary>"Top View"</summary>
    internal static string CommandTopView => Get("CommandTopView");

    /// <summary>"Look straight down with north up (Ctrl+T)"</summary>
    internal static string CommandTopViewTip => Get("CommandTopViewTip");

    /// <summary>"Fit Marina"</summary>
    internal static string CommandFitMarina => Get("CommandFitMarina");

    /// <summary>"Frame the whole marina (Ctrl+F)"</summary>
    internal static string CommandFitMarinaTip => Get("CommandFitMarinaTip");

    // ---- Status bar and log --------------------------------------------------------------------------

    /// <summary>"Eye {0:0} m · tilt {1:0}°"</summary>
    internal static string StatusCamera => Get("StatusCamera");

    /// <summary>"x {0:0.0} m · y {1:0.0} m"</summary>
    internal static string StatusPointer => Get("StatusPointer");

    /// <summary>"ACTIVITY LOG"</summary>
    internal static string LogHeader => Get("LogHeader");

    /// <summary>"{0:HH:mm:ss}  {1}"</summary>
    internal static string LogEntry => Get("LogEntry");

    /// <summary>"New marina. Load a reference image to trace the real shape, or start with the land tool."</summary>
    internal static string LogNewMarina => Get("LogNewMarina");

    /// <summary>"Added {0}: {1} corners, {2:0} m²"</summary>
    internal static string LogAddedLand => Get("LogAddedLand");

    /// <summary>"Added pier {0}: {1:0.0} × {2:0.0} m"</summary>
    internal static string LogAddedPier => Get("LogAddedPier");

    /// <summary>"Added berth {0}"</summary>
    internal static string LogAddedBerth => Get("LogAddedBerth");

    /// <summary>"Added {0} berths: {1}"</summary>
    internal static string LogAddedBerths => Get("LogAddedBerths");

    /// <summary>"Removed {0} ({1} berth(s), {2} separator(s))"</summary>
    internal static string LogRemoved => Get("LogRemoved");

    /// <summary>"{0}: {1} trees (was {2})"</summary>
    internal static string LogTrees => Get("LogTrees");

    /// <summary>"Undone: {0}"</summary>
    internal static string LogUndone => Get("LogUndone");

    /// <summary>"Scale line drawn: {0:0.0} m at the current image scale"</summary>
    internal static string LogScaleLine => Get("LogScaleLine");

    /// <summary>"Reference image {0} ({1:0.####} m per pixel)"</summary>
    internal static string LogReferenceImage => Get("LogReferenceImage");

    /// <summary>"Renderer: {0}"</summary>
    internal static string LogRendererError => Get("LogRendererError");

    /// <summary>"Opened {0}: {1} berths, {2} piers"</summary>
    internal static string LogOpened => Get("LogOpened");

    /// <summary>" (written by a newer version, {0})"</summary>
    internal static string LogOpenedNewerVersion => Get("LogOpenedNewerVersion");

    /// <summary>"Saved {0} ({1} berths)"</summary>
    internal static string LogSaved => Get("LogSaved");

    /// <summary>"Loaded {0}. Draw a line over something of a known length, then type that length."</summary>
    internal static string LogImageLoaded => Get("LogImageLoaded");

    /// <summary>"Berth labels shown"</summary>
    internal static string LogBerthLabelsShown => Get("LogBerthLabelsShown");

    /// <summary>"Berth labels hidden"</summary>
    internal static string LogBerthLabelsHidden => Get("LogBerthLabelsHidden");

    /// <summary>"{0}: {1}"</summary>
    internal static string LogFailed => Get("LogFailed");

    // ---- File dialogs and messages -------------------------------------------------------------------

    /// <summary>"Open a marina design"</summary>
    internal static string OpenDesignTitle => Get("OpenDesignTitle");

    /// <summary>"Save the marina design"</summary>
    internal static string SaveDesignTitle => Get("SaveDesignTitle");

    /// <summary>"Top-down image of the marina (north at the top)"</summary>
    internal static string OpenImageTitle => Get("OpenImageTitle");

    /// <summary>"The design could not be opened"</summary>
    internal static string OpenFailed => Get("OpenFailed");

    /// <summary>"The design could not be saved"</summary>
    internal static string SaveFailed => Get("SaveFailed");

    /// <summary>"The image could not be loaded"</summary>
    internal static string ImageLoadFailed => Get("ImageLoadFailed");

    /// <summary>"{0}:  {1}"</summary>
    internal static string WarnBody => Get("WarnBody");

    /// <summary>"Save the changes to {0} first?"</summary>
    internal static string ConfirmDiscard => Get("ConfirmDiscard");

    /// <summary>"Don't save"</summary>
    internal static string ConfirmDiscardButton => Get("ConfirmDiscardButton");

    /// <summary>"Marina name"</summary>
    internal static string MarinaNameTitle => Get("MarinaNameTitle");

    /// <summary>"What is this marina called?"</summary>
    internal static string MarinaNameQuestion => Get("MarinaNameQuestion");

    /// <summary>"OK"</summary>
    internal static string DialogOk => Get("DialogOk");

    /// <summary>"Cancel"</summary>
    internal static string DialogCancel => Get("DialogCancel");

    // ---- Help ----------------------------------------------------------------------------------------

    /// <summary>"Keyboard and mouse"</summary>
    internal static string ShortcutsTitle => Get("ShortcutsTitle");

    /// <summary>"Mouse   Left drag           pan the view   Right drag          orbit   Wheel               zo..."</summary>
    internal static string ShortcutsBody => Get("ShortcutsBody");

    /// <summary>"About"</summary>
    internal static string AboutTitle => Get("AboutTitle");

    /// <summary>"{0} Version {1}  Library      VirtualMarina {2} Renderer     {3} Runtime      {4}  © {5} {6}"</summary>
    internal static string AboutBody => Get("AboutBody");

    // ---- Look panel ----------------------------------------------------------------------------------

    /// <summary>"Look"</summary>
    internal static string ToolLook => Get("ToolLook");

    /// <summary>"Water, light, colours and labels, with the marina still live beside them"</summary>
    internal static string ToolLookTip => Get("ToolLookTip");

    /// <summary>"Look"</summary>
    internal static string TitleLook => Get("TitleLook");

    /// <summary>"Change how the marina is drawn. The view stays live, so you can turn and zoom while you work."</summary>
    internal static string LookHint => Get("LookHint");

    /// <summary>"Back to the default"</summary>
    internal static string ResetTip => Get("ResetTip");

    /// <summary>"↺"</summary>
    internal static string ResetGlyph => Get("ResetGlyph");

    /// <summary>"Land and trees"</summary>
    internal static string CardLand => Get("CardLand");

    /// <summary>"Quay top"</summary>
    internal static string LandQuay => Get("LandQuay");

    /// <summary>"Quay wall"</summary>
    internal static string LandQuayWall => Get("LandQuayWall");

    /// <summary>"Lawn"</summary>
    internal static string LandGrass => Get("LandGrass");

    /// <summary>"Lawn edge"</summary>
    internal static string LandGrassBank => Get("LandGrassBank");

    /// <summary>"Rocks"</summary>
    internal static string LandRock => Get("LandRock");

    /// <summary>"Rock variation"</summary>
    internal static string LandRockVariation => Get("LandRockVariation");

    /// <summary>"How much single rocks differ in brightness from one another."</summary>
    internal static string LandRockVariationTip => Get("LandRockVariationTip");

    /// <summary>"Broadleaf"</summary>
    internal static string LandFoliage => Get("LandFoliage");

    /// <summary>"Conifer"</summary>
    internal static string LandConifer => Get("LandConifer");

    /// <summary>"Palm fronds"</summary>
    internal static string LandPalm => Get("LandPalm");

    /// <summary>"Cherry blossom"</summary>
    internal static string LandBlossom => Get("LandBlossom");

    /// <summary>"Trunks"</summary>
    internal static string LandTrunk => Get("LandTrunk");

    /// <summary>"Draw the trees"</summary>
    internal static string LandShowTrees => Get("LandShowTrees");

    /// <summary>"Berth labels"</summary>
    internal static string CardLabels => Get("CardLabels");

    /// <summary>"Face"</summary>
    /// <summary>"Weight and width of the lettering painted on the water."</summary>
    /// <summary>"Normal"</summary>
    internal static string LabelColorNormal => Get("LabelColorNormal");

    /// <summary>"Selected"</summary>
    internal static string LabelColorHighlight => Get("LabelColorHighlight");

    /// <summary>"Ashore"</summary>
    internal static string LabelColorAshore => Get("LabelColorAshore");

    /// <summary>"Colour of the names of berths ashore. ..."</summary>
    internal static string LabelColorAshoreTip => Get("LabelColorAshoreTip");

    /// <summary>"Disabled"</summary>
    internal static string LabelColorDisabled => Get("LabelColorDisabled");

    /// <summary>"Regular"</summary>
    internal static string LabelFont => Get("LabelFont");

    internal static string LabelFontTip => Get("LabelFontTip");

    internal static string LabelBold => Get("LabelBold");

    internal static string LabelBoldTip => Get("LabelBoldTip");

    internal static string FontBuiltIn => Get("FontBuiltIn");

    /// <summary>"Bold"</summary>
    /// <summary>"Condensed"</summary>
    /// <summary>"Wide"</summary>
    /// <summary>"Preview boats"</summary>
    internal static string CardPreview => Get("CardPreview");

    /// <summary>"Marina full"</summary>
    internal static string PreviewFill => Get("PreviewFill");

    /// <summary>"How full the marina ends up when you press Add boats: at 100% every berth has a boat in..."</summary>
    internal static string PreviewFillTip => Get("PreviewFillTip");

    /// <summary>"Add boats"</summary>
    internal static string PreviewAdd => Get("PreviewAdd");

    /// <summary>"Clear boats"</summary>
    internal static string PreviewClear => Get("PreviewClear");

    /// <summary>"Boats to judge the settings against, each picked to suit the berth it goes in, with the wi..."</summary>
    internal static string PreviewHint => Get("PreviewHint");

    /// <summary>"Filled {0} of the {1} berths with preview boats"</summary>
    internal static string LogPreviewBoats => Get("LogPreviewBoats");

    /// <summary>"Cleared the preview boats"</summary>
    internal static string LogPreviewCleared => Get("LogPreviewCleared");

    // ---- Inspector: headings -------------------------------------------------------------------------

    /// <summary>"Land area"</summary>
    internal static string CardLandArea => Get("CardLandArea");

    /// <summary>"Pier"</summary>
    internal static string CardPier => Get("CardPier");

    /// <summary>"Berths"</summary>
    internal static string CardBerths => Get("CardBerths");

    /// <summary>"Storage ashore"</summary>
    internal static string CardStorageAshore => Get("CardStorageAshore");

    /// <summary>"Trees"</summary>
    internal static string CardTrees => Get("CardTrees");

    /// <summary>"Erase"</summary>
    internal static string CardErase => Get("CardErase");

    /// <summary>"Reference image"</summary>
    internal static string CardReferenceImage => Get("CardReferenceImage");

    /// <summary>"This marina"</summary>
    internal static string CardSummary => Get("CardSummary");

    /// <summary>"Navigate"</summary>
    internal static string TitleNavigate => Get("TitleNavigate");

    /// <summary>"Land area"</summary>
    internal static string TitleLandArea => Get("TitleLandArea");

    /// <summary>"Pier"</summary>
    internal static string TitlePier => Get("TitlePier");

    /// <summary>"Berths"</summary>
    internal static string TitleBerths => Get("TitleBerths");

    /// <summary>"Storage ashore"</summary>
    internal static string TitleStorageAshore => Get("TitleStorageAshore");

    /// <summary>"Trees"</summary>
    internal static string TitleTrees => Get("TitleTrees");

    /// <summary>"Erase"</summary>
    internal static string TitleErase => Get("TitleErase");

    /// <summary>"Move the image"</summary>
    internal static string TitleMoveImage => Get("TitleMoveImage");

    /// <summary>"Set the scale"</summary>
    internal static string TitleMeasureScale => Get("TitleMeasureScale");

    // ---- Inspector: land area ------------------------------------------------------------------------

    /// <summary>"Surface"</summary>
    internal static string LandSurface => Get("LandSurface");

    /// <summary>"What the area is made of. Lawns can have trees on them."</summary>
    internal static string LandSurfaceTip => Get("LandSurfaceTip");

    /// <summary>"Height (m)"</summary>
    internal static string LandHeight => Get("LandHeight");

    /// <summary>"How far the surface stands above the water."</summary>
    internal static string LandHeightTip => Get("LandHeightTip");

    /// <summary>"Click each corner in the view, then press Enter (or click the first corner) to close the shape."</summary>
    internal static string LandHint => Get("LandHint");

    // ---- Inspector: pier -----------------------------------------------------------------------------

    /// <summary>"Construction"</summary>
    internal static string PierConstruction => Get("PierConstruction");

    /// <summary>"Floating pontoon or fixed concrete pier; it sets the deck height and the look."</summary>
    internal static string PierConstructionTip => Get("PierConstructionTip");

    /// <summary>"Deck width (m)"</summary>
    internal static string PierWidth => Get("PierWidth");

    /// <summary>"Berths"</summary>
    internal static string PierBerths => Get("PierBerths");

    /// <summary>"A pier along a quay wall or a mole only takes boats on its water side."</summary>
    internal static string PierBerthsTip => Get("PierBerthsTip");

    /// <summary>"Click the shore end, then the far end. The pier squares up with the quay and the piers alread..."</summary>
    internal static string PierHint => Get("PierHint");

    /// <summary>"Name"</summary>
    internal static string PierName => Get("PierName");

    /// <summary>"Name given to each new pier. {pier} stands for its letter, so "Pier {pier}" gives "Pier A"."</summary>
    internal static string PierNameTip => Get("PierNameTip");

    // ---- Inspector: berths ---------------------------------------------------------------------------

    /// <summary>"Width (m)"</summary>
    internal static string BerthWidth => Get("BerthWidth");

    /// <summary>"Across the berth: the widest boat that fits."</summary>
    internal static string BerthWidthTip => Get("BerthWidthTip");

    /// <summary>"Length (m)"</summary>
    internal static string BerthLength => Get("BerthLength");

    /// <summary>"Away from the pier: the longest boat that fits."</summary>
    internal static string BerthLengthTip => Get("BerthLengthTip");

    /// <summary>"Depth (m)"</summary>
    internal static string BerthDepth => Get("BerthDepth");

    /// <summary>"Water depth, stored as the berth's maximum draft."</summary>
    internal static string BerthDepthTip => Get("BerthDepthTip");

    /// <summary>"Separator"</summary>
    internal static string BerthSeparator => Get("BerthSeparator");

    /// <summary>"Gap (m)"</summary>
    internal static string BerthGap => Get("BerthGap");

    /// <summary>"Space left between neighbouring berths."</summary>
    internal static string BerthGapTip => Get("BerthGapTip");

    /// <summary>"Continue the row already on that side"</summary>
    internal static string AlignBerths => Get("AlignBerths");

    /// <summary>"Pedestals"</summary>
    internal static string BerthServices => Get("BerthServices");

    /// <summary>"Power and water points, placed between every two berths of the pier."</summary>
    internal static string BerthServicesTip => Get("BerthServicesTip");

    /// <summary>"Click beside a pier where the row starts, then where it ends. Clear the tick above to start t..."</summary>
    internal static string BerthHint => Get("BerthHint");

    /// <summary>"Each berth gets its own pair of short finger piers."</summary>
    internal static string SeparatorHintFingerPiers => Get("SeparatorHintFingerPiers");

    /// <summary>"Nothing between the berths but a gap, so boats lie side by side."</summary>
    internal static string SeparatorHintNone => Get("SeparatorHintNone");

    /// <summary>"One walkable pier between every two berths."</summary>
    internal static string SeparatorHintFingerPier => Get("SeparatorHintFingerPier");

    /// <summary>"A row of mooring piles between every two berths."</summary>
    internal static string SeparatorHintPiles => Get("SeparatorHintPiles");

    /// <summary>"A floating boom between every two berths."</summary>
    internal static string SeparatorHintBoom => Get("SeparatorHintBoom");

    /// <summary>"A pier every other berth: each boat has a pier on one side and a neighbour on the other."</summary>
    internal static string SeparatorHintPaired => Get("SeparatorHintPaired");

    /// <summary>"One mooring pile at the outer end of every berth boundary (Mediterranean mooring)."</summary>
    internal static string SeparatorHintSinglePile => Get("SeparatorHintSinglePile");

    /// <summary>" Berths are {0:0.##} m apart, {1:0.##} m from center to center."</summary>
    internal static string SeparatorHintSpacing => Get("SeparatorHintSpacing");

    /// <summary>"Names"</summary>
    internal static string BerthNamingHeading => Get("BerthNamingHeading");

    /// <summary>"Pattern"</summary>
    internal static string BerthNamePattern => Get("BerthNamePattern");

    /// <summary>"Name of each new berth. {pier} is the pier, {pierName} its name, {side} the side (L or R) and..."</summary>
    internal static string BerthNamePatternTip => Get("BerthNamePatternTip");

    /// <summary>"Start at"</summary>
    internal static string BerthStartNumber => Get("BerthStartNumber");

    /// <summary>"Number the first new berth gets. Names already taken are skipped, so a second row carries on ..."</summary>
    internal static string BerthStartNumberTip => Get("BerthStartNumberTip");

    /// <summary>"Count by"</summary>
    internal static string BerthIncrement => Get("BerthIncrement");

    /// <summary>"Step from one berth to the next: 1 gives 1, 2, 3; 2 gives 1, 3, 5."</summary>
    internal static string BerthIncrementTip => Get("BerthIncrementTip");

    /// <summary>"Digits"</summary>
    internal static string BerthNumberDigits => Get("BerthNumberDigits");

    /// <summary>"Leading zeros: 2 writes 1 as 01."</summary>
    internal static string BerthNumberDigitsTip => Get("BerthNumberDigitsTip");

    /// <summary>"Next: {0}"</summary>
    internal static string BerthNamingExample => Get("BerthNamingExample");

    // ---- Inspector: storage ashore -------------------------------------------------------------------

    /// <summary>"Width (m)"</summary>
    internal static string LandBerthWidth => Get("LandBerthWidth");

    /// <summary>"Length (m)"</summary>
    internal static string LandBerthLength => Get("LandBerthLength");

    /// <summary>"Bow points"</summary>
    internal static string LandBerthHeading => Get("LandBerthHeading");

    /// <summary>"Direction the stored boat faces: 0° = north, 90° = east."</summary>
    internal static string LandBerthHeadingTip => Get("LandBerthHeadingTip");

    /// <summary>"Name of each new slot ashore. {pier} is the land area, {pierName} its name and {number} the r..."</summary>
    internal static string LandNamePatternTip => Get("LandNamePatternTip");

    /// <summary>"Number the first new slot ashore gets. Counts separately from the berths on the water."</summary>
    internal static string LandStartNumberTip => Get("LandStartNumberTip");

    /// <summary>"Click the spot on a land area, then click where the bow should point. Click the same spot twi..."</summary>
    internal static string LandBerthHint => Get("LandBerthHint");

    // ---- Inspector: trees and erase ------------------------------------------------------------------

    /// <summary>"none"</summary>
    internal static string TreeNone => Get("TreeNone");

    /// <summary>"Trees per 1000 m². Click a lawn to scatter them; click again for a different arrangement, Ctr..."</summary>
    internal static string TreeHint => Get("TreeHint");

    /// <summary>"Click a berth, a pier or a land area to remove it. Piers and land areas take their berths wit..."</summary>
    internal static string EraseHint => Get("EraseHint");

    /// <summary>"Ctrl+Z brings back anything you remove by mistake."</summary>
    internal static string EraseUndoHint => Get("EraseUndoHint");

    // ---- Renaming ------------------------------------------------------------------------------------

    /// <summary>"Pedestals"</summary>
    internal static string CardServices => Get("CardServices");

    /// <summary>"Pedestals"</summary>
    internal static string TitleServices => Get("TitleServices");

    /// <summary>"Select"</summary>
    internal static string TitleSelect => Get("TitleSelect");

    /// <summary>"Select"</summary>
    internal static string CardSelect => Get("CardSelect");

    /// <summary>"Drag a box over the water. Every berth whose middle falls inside it is selected; hold Shift o..."</summary>
    internal static string SelectHint => Get("SelectHint");

    /// <summary>"{0} berth(s) selected"</summary>
    internal static string SelectCount => Get("SelectCount");

    /// <summary>"Pick what the pedestals offer, then click a berth. Hold Alt or Ctrl to change every berth dow..."</summary>
    internal static string ServicesHint => Get("ServicesHint");

    /// <summary>"A berth left alone takes whatever its pier offers, so only the ones you change carry their ow..."</summary>
    internal static string ServicesInheritHint => Get("ServicesInheritHint");

    /// <summary>"Rename"</summary>
    internal static string CardRename => Get("CardRename");

    /// <summary>"Click a berth to give it another name, or a pier to give it a title. Berth names are also the..."</summary>
    internal static string RenameHint => Get("RenameHint");

    /// <summary>"Ctrl+Z puts the old name back."</summary>
    internal static string RenameUndoHint => Get("RenameUndoHint");

    /// <summary>"Rename berth"</summary>
    internal static string RenameBerthTitle => Get("RenameBerthTitle");

    /// <summary>"What should this berth be called?"</summary>
    internal static string RenameBerthQuestion => Get("RenameBerthQuestion");

    /// <summary>"Name the pier"</summary>
    internal static string RenamePierTitle => Get("RenamePierTitle");

    /// <summary>"What should this pier be called?"</summary>
    internal static string RenamePierQuestion => Get("RenamePierQuestion");

    /// <summary>"Its id, which its berths and dividers point at:"</summary>
    internal static string RenamePierIdQuestion => Get("RenamePierIdQuestion");

    internal static string RenameBerthsTitle => Get("RenameBerthsTitle");

    internal static string RenameBerthsQuestion => Get("RenameBerthsQuestion");

    internal static string RenamePatternQuestion => Get("RenamePatternQuestion");

    internal static string RenamePatternHint => Get("RenamePatternHint");

    internal static string RenameClashTitle => Get("RenameClashTitle");

    internal static string RenameClashBody => Get("RenameClashBody");

    internal static string RenameClashMore => Get("RenameClashMore");

    internal static string LogRenameClash => Get("LogRenameClash");

    /// <summary>"Pier id {0} is now {1}"</summary>
    internal static string LogPierIdChanged => Get("LogPierIdChanged");

    /// <summary>"Rename"</summary>
    internal static string TitleRename => Get("TitleRename");

    /// <summary>"Rename"</summary>
    internal static string ToolRename => Get("ToolRename");

    /// <summary>"Pedestals"</summary>
    internal static string ToolServices => Get("ToolServices");

    /// <summary>"Give berths power and water; Alt or Ctrl changes a whole side of a pier"</summary>
    internal static string ToolServicesTip => Get("ToolServicesTip");

    /// <summary>"Select"</summary>
    internal static string ToolSelect => Get("ToolSelect");

    /// <summary>"Drag a box over the water to select the berths inside it"</summary>
    internal static string ToolSelectTip => Get("ToolSelectTip");

    /// <summary>"Rename a berth or a pier"</summary>
    internal static string ToolRenameTip => Get("ToolRenameTip");

    /// <summary>"&amp;Rename"</summary>
    internal static string MenuRename => Get("MenuRename");

    /// <summary>"Name already used"</summary>
    internal static string RenameTakenTitle => Get("RenameTakenTitle");

    /// <summary>"{0} is already used by something else in this marina. Pick another."</summary>
    internal static string RenameTakenBody => Get("RenameTakenBody");

    /// <summary>"Renamed {0} to {1}"</summary>
    internal static string LogRenamed => Get("LogRenamed");

    internal static string LogBerthPattern => Get("LogBerthPattern");

    /// <summary>"{0} is already taken; {1} keeps its name."</summary>
    internal static string LogRenameRefused => Get("LogRenameRefused");

    // ---- Inspector: the mainland ---------------------------------------------------------------------

    /// <summary>"Coast"</summary>
    internal static string ToolCoast => Get("ToolCoast");

    /// <summary>"Draw the coast of the mainland behind the marina"</summary>
    internal static string ToolCoastTip => Get("ToolCoastTip");

    /// <summary>"Coast"</summary>
    internal static string TitleCoast => Get("TitleCoast");

    /// <summary>"The mainland"</summary>
    internal static string CardCoast => Get("CardCoast");

    /// <summary>"Cover"</summary>
    internal static string CoastScenery => Get("CoastScenery");

    /// <summary>"What is scattered across the land behind the shore. Bare ground is the cheapest to draw."</summary>
    internal static string CoastSceneryTip => Get("CoastSceneryTip");

    /// <summary>"Click along the coast (two points make a straight one), press Enter, then click the side that..."</summary>
    internal static string CoastHint => Get("CoastHint");

    /// <summary>"The first and last stretches run on without end, so the land never stops however far you pull..."</summary>
    internal static string CoastEndlessHint => Get("CoastEndlessHint");

    /// <summary>"Remove the mainland"</summary>
    internal static string CoastRemove => Get("CoastRemove");

    /// <summary>"No mainland: the marina stands in open water."</summary>
    internal static string CoastNone => Get("CoastNone");

    /// <summary>"Mainland from {0} points, {1}."</summary>
    internal static string CoastPresent => Get("CoastPresent");

    /// <summary>"Draw the &amp;Coast"</summary>
    internal static string MenuCoast => Get("MenuCoast");

    /// <summary>"Drew the mainland along {0} points"</summary>
    internal static string LogCoastDrawn => Get("LogCoastDrawn");

    /// <summary>"Removed the mainland"</summary>
    internal static string LogCoastRemoved => Get("LogCoastRemoved");

    // ---- Look: passing traffic -----------------------------------------------------------------------

    /// <summary>"Passing traffic"</summary>
    internal static string CardTraffic => Get("CardTraffic");

    /// <summary>"Vessels out at sea"</summary>
    internal static string TrafficShow => Get("TrafficShow");

    /// <summary>"Boats, yachts and a ferry crossing the bay beyond the marina. They are decoration: they canno..."</summary>
    internal static string TrafficShowTip => Get("TrafficShowTip");

    /// <summary>"How busy"</summary>
    internal static string TrafficEdgeClearance => Get("TrafficEdgeClearance");

    /// <summary>"How many vessels are out there at once."</summary>
    internal static string TrafficEdgeClearanceTip => Get("TrafficEdgeClearanceTip");

    internal static string TrafficSpawnDelay => Get("TrafficSpawnDelay");

    internal static string TrafficSpawnDelayTip => Get("TrafficSpawnDelayTip");

    /// <summary>"Keep clear by"</summary>
    internal static string TrafficClearance => Get("TrafficClearance");

    /// <summary>"How far the lanes must stay from the marina and from any land. Raise it to push the traffic o..."</summary>
    internal static string TrafficClearanceTip => Get("TrafficClearanceTip");

    /// <summary>"Speed"</summary>
    internal static string TrafficSpeed => Get("TrafficSpeed");

    /// <summary>"How fast the vessels cross, in knots."</summary>
    internal static string TrafficSpeedTip => Get("TrafficSpeedTip");

    /// <summary>"{0} lane(s) found room out there."</summary>
    internal static string TrafficPasses => Get("TrafficPasses");

    internal static string TrafficLanes => Get("TrafficLanes");

    internal static string TrafficLanesTip => Get("TrafficLanesTip");

    internal static string TrafficLaneSpacing => Get("TrafficLaneSpacing");

    internal static string TrafficLaneSpacingTip => Get("TrafficLaneSpacingTip");

    internal static string TrafficShowLanes => Get("TrafficShowLanes");

    internal static string TrafficShowLanesTip => Get("TrafficShowLanesTip");

    /// <summary>"No room for a lane: lower the clearance, or widen the water."</summary>
    internal static string TrafficNoRoom => Get("TrafficNoRoom");

    /// <summary>"Lanes never cross the marina or the land, and vessels fade away at the edge of the map."</summary>
    internal static string TrafficHint => Get("TrafficHint");

    /// <summary>"{0} kn"</summary>
    internal static string ValueSeconds => Get("ValueSeconds");

    // ---- Look: the mainland --------------------------------------------------------------------------

    /// <summary>"Town walls"</summary>
    internal static string LandBuilding => Get("LandBuilding");

    /// <summary>"Town roofs"</summary>
    internal static string LandRoof => Get("LandRoof");

    // ---- Cameras panel -------------------------------------------------------------------------------

    /// <summary>"Cameras"</summary>
    internal static string ToolCameras => Get("ToolCameras");

    /// <summary>"Saved views of the marina, and which of the automatic ones to offer"</summary>
    internal static string ToolCamerasTip => Get("ToolCamerasTip");

    /// <summary>"Cameras"</summary>
    internal static string TitleCameras => Get("TitleCameras");

    /// <summary>"Pick a view to go there. Untick one to leave it out of the list the host application offers."</summary>
    internal static string CamerasHint => Get("CamerasHint");

    /// <summary>"Save this view"</summary>
    internal static string CardCameraSave => Get("CardCameraSave");

    /// <summary>"Name"</summary>
    internal static string CameraName => Get("CameraName");

    /// <summary>"What to call the view you are looking at now."</summary>
    internal static string CameraNameTip => Get("CameraNameTip");

    /// <summary>"Save view"</summary>
    internal static string CameraSave => Get("CameraSave");

    /// <summary>"Saves where the camera is now, so you and the host application can come back to it."</summary>
    internal static string CameraSaveHint => Get("CameraSaveHint");

    /// <summary>"Automatic views"</summary>
    internal static string CardCameraAutomatic => Get("CardCameraAutomatic");

    /// <summary>"Saved views"</summary>
    internal static string CardCameraSaved => Get("CardCameraSaved");

    /// <summary>"No saved views yet."</summary>
    internal static string CameraNoneSaved => Get("CameraNoneSaved");

    /// <summary>"▶"</summary>
    internal static string CameraGoToGlyph => Get("CameraGoToGlyph");

    /// <summary>"Move the camera to this view"</summary>
    internal static string CameraGoToTip => Get("CameraGoToTip");

    /// <summary>"✕"</summary>
    internal static string CameraDeleteGlyph => Get("CameraDeleteGlyph");

    /// <summary>"Remove this saved view"</summary>
    internal static string CameraDeleteTip => Get("CameraDeleteTip");

    /// <summary>"View {0}"</summary>
    internal static string CameraDefaultName => Get("CameraDefaultName");

    /// <summary>"Saved the view {0}"</summary>
    internal static string LogCameraSaved => Get("LogCameraSaved");

    /// <summary>"Deleted the view {0}"</summary>
    internal static string LogCameraDeleted => Get("LogCameraDeleted");

    /// <summary>"{0} is no longer offered"</summary>
    internal static string LogCameraDisabled => Get("LogCameraDisabled");

    /// <summary>"{0} is offered again"</summary>
    internal static string LogCameraEnabled => Get("LogCameraEnabled");

    // ---- Look: shadows -------------------------------------------------------------------------------

    /// <summary>"Shadows"</summary>
    internal static string CardShadows => Get("CardShadows");

    /// <summary>"Cast shadows"</summary>
    internal static string ShadowsShow => Get("ShadowsShow");

    /// <summary>"The boats and the piers throw their shape onto the water and the quays. Turning it off makes ..."</summary>
    internal static string ShadowsShowTip => Get("ShadowsShowTip");

    /// <summary>"Darkness"</summary>
    internal static string ShadowStrength => Get("ShadowStrength");

    /// <summary>"How dark a shadow is. Past about 40% the places where a boat overlaps its own shadow start to..."</summary>
    internal static string ShadowStrengthTip => Get("ShadowStrengthTip");

    /// <summary>"Shadows follow the sun above. They land on the ground an object stands over, so a boat ashore..."</summary>
    internal static string ShadowHint => Get("ShadowHint");

    // ---- Look: the water area ------------------------------------------------------------------------

    /// <summary>"Detailed area"</summary>
    internal static string WaterArea => Get("WaterArea");

    /// <summary>"How far out the water has waves, reflections and glints. Past it the sea carries on flat to t..."</summary>
    internal static string WaterAreaTip => Get("WaterAreaTip");

    /// <summary>"At most"</summary>
    internal static string TrafficMaximum => Get("TrafficMaximum");

    /// <summary>"The most vessels on the water at once. How busy is a share of this."</summary>
    internal static string TrafficMaximumTip => Get("TrafficMaximumTip");

    /// <summary>"{0} vessels"</summary>
    internal static string ValueVessels => Get("ValueVessels");

    internal static string ValueLanes => Get("ValueLanes");

    // ---- Inspector: reference image ------------------------------------------------------------------

    /// <summary>"Load image…"</summary>
    internal static string ImageLoad => Get("ImageLoad");

    /// <summary>"Fit to view"</summary>
    internal static string ImageFit => Get("ImageFit");

    /// <summary>"Remove"</summary>
    internal static string ImageRemove => Get("ImageRemove");

    /// <summary>"Move image"</summary>
    internal static string ImageMove => Get("ImageMove");

    /// <summary>"Draw scale line"</summary>
    internal static string ImageMeasure => Get("ImageMeasure");

    /// <summary>"Apply"</summary>
    internal static string ImageApply => Get("ImageApply");

    /// <summary>"Real length (m)"</summary>
    internal static string ImageRealLength => Get("ImageRealLength");

    /// <summary>"Opacity"</summary>
    internal static string ImageOpacity => Get("ImageOpacity");

    /// <summary>"Draw over land and piers"</summary>
    internal static string ImageAbove => Get("ImageAbove");

    /// <summary>"Show the picture"</summary>
    internal static string ImageShown => Get("ImageShown");

    /// <summary>"Clear the scale line"</summary>
    internal static string ImageClearScaleLine => Get("ImageClearScaleLine");

    /// <summary>"Removes the measuring line once the picture is scaled. The line is never saved with the design."</summary>
    internal static string ImageClearScaleLineTip => Get("ImageClearScaleLineTip");

    /// <summary>"Load a top-down photo or map of the marina (north up) and set its scale, then trace over it."</summary>
    internal static string ImageStateEmpty => Get("ImageStateEmpty");

    /// <summary>"The line you drew is {0:0.0} m long at the current scale. Type what it should be and apply."</summary>
    internal static string ImageStateLine => Get("ImageStateLine");

    /// <summary>"Image is {0:0} × {1:0} m ({2:0.###} m per pixel). Draw a line over a known length to set the ..."</summary>
    internal static string ImageStateReady => Get("ImageStateReady");

    /// <summary>"{0}%"</summary>
    internal static string Percent => Get("Percent");

    // ---- Inspector: summary --------------------------------------------------------------------------

    /// <summary>"{0} berth(s): {1} on the water, {2} ashore {3} pier(s), {4} land area(s), {5} separator(s), {..."</summary>
    internal static string Summary => Get("Summary");

    // ---- Appearance dialog ---------------------------------------------------------------------------

    /// <summary>"Water, light and motion"</summary>
    internal static string AppearanceTitle => Get("AppearanceTitle");

    /// <summary>"Done"</summary>
    internal static string AppearanceDone => Get("AppearanceDone");

    /// <summary>"Cancel"</summary>
    internal static string AppearanceCancel => Get("AppearanceCancel");

    /// <summary>"Reset to defaults"</summary>
    internal static string AppearanceReset => Get("AppearanceReset");

    /// <summary>"Water"</summary>
    internal static string CardWater => Get("CardWater");

    /// <summary>"Wave height"</summary>
    internal static string WaveHeight => Get("WaveHeight");

    /// <summary>"Wave length"</summary>
    internal static string WaveLength => Get("WaveLength");

    /// <summary>"Wave speed"</summary>
    internal static string WaveSpeed => Get("WaveSpeed");

    /// <summary>"Reflections"</summary>
    internal static string Reflections => Get("Reflections");

    /// <summary>"Ripples"</summary>
    internal static string Ripples => Get("Ripples");

    /// <summary>"Sun glints"</summary>
    internal static string SunGlints => Get("SunGlints");

    /// <summary>"{0} m"</summary>
    internal static string ValueMetersWhole => Get("ValueMetersWhole");

    /// <summary>"Deep water"</summary>
    internal static string DeepWater => Get("DeepWater");

    /// <summary>"Shallow water"</summary>
    internal static string ShallowWater => Get("ShallowWater");

    /// <summary>"Boats"</summary>
    internal static string CardBoats => Get("CardBoats");

    /// <summary>"Movement"</summary>
    internal static string BoatMovement => Get("BoatMovement");

    /// <summary>"How much moored boats, buoys and booms rise, fall and roll with the waves."</summary>
    internal static string BoatMovementHint => Get("BoatMovementHint");

    /// <summary>"Light and air"</summary>
    internal static string CardLight => Get("CardLight");

    /// <summary>"Sun direction"</summary>
    internal static string SunDirection => Get("SunDirection");

    /// <summary>"Sun height"</summary>
    internal static string SunHeight => Get("SunHeight");

    /// <summary>"Haze"</summary>
    internal static string Haze => Get("Haze");

    /// <summary>"Sky"</summary>
    internal static string Sky => Get("Sky");

    /// <summary>"Horizon"</summary>
    internal static string Horizon => Get("Horizon");

    /// <summary>"Berth colors"</summary>
    internal static string CardBerthColors => Get("CardBerthColors");

    /// <summary>"Free"</summary>
    internal static string ColorFree => Get("ColorFree");

    /// <summary>"Occupied"</summary>
    internal static string ColorOccupied => Get("ColorOccupied");

    /// <summary>"Reserved"</summary>
    internal static string ColorReserved => Get("ColorReserved");

    /// <summary>"Owner away"</summary>
    internal static string ColorOwnerAway => Get("ColorOwnerAway");

    /// <summary>"Pad strength"</summary>
    internal static string PadStrength => Get("PadStrength");

    /// <summary>"Show the status buoys"</summary>
    internal static string ShowStatusBuoys => Get("ShowStatusBuoys");

    /// <summary>"{0:0.00} m"</summary>
    internal static string ValueMeters => Get("ValueMeters");

    /// <summary>"×{0:0.0}"</summary>
    internal static string ValueTimes => Get("ValueTimes");

    /// <summary>"still"</summary>
    internal static string ValueStill => Get("ValueStill");

    /// <summary>"{0}° from north"</summary>
    internal static string ValueDegreesFromNorth => Get("ValueDegreesFromNorth");

    /// <summary>"{0}° above the horizon"</summary>
    internal static string ValueDegreesAboveHorizon => Get("ValueDegreesAboveHorizon");
}
