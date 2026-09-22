using System.Globalization;
using System.Resources;

namespace VirtualMarina.WinForms.Resources;

/// <summary>
/// Displayed text of the WinForms host controls. The names of the settings themselves (tools, separators,
/// pier services and the rest) come from the core library, through <see cref="Core.Api.DisplayNames"/>.
/// </summary>
internal static class Strings
{
    private static readonly ResourceManager Manager = new("VirtualMarina.WinForms.Resources.Strings", typeof(Strings).Assembly);

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

    // ---- Designer panel ------------------------------------------------------------------------------

    /// <summary>"Designer"</summary>
    internal static string SectionDesigner => Get("SectionDesigner");

    /// <summary>"Design mode"</summary>
    internal static string DesignMode => Get("DesignMode");

    /// <summary>"Undo (Ctrl+Z)"</summary>
    internal static string Undo => Get("Undo");

    /// <summary>"Nothing to undo"</summary>
    internal static string NothingToUndo => Get("NothingToUndo");

    /// <summary>"Turn on design mode to draw land areas, piers and berths."</summary>
    internal static string TurnOnDesignMode => Get("TurnOnDesignMode");

    // ---- Land area settings --------------------------------------------------------------------------

    /// <summary>"Land area"</summary>
    internal static string SectionLandArea => Get("SectionLandArea");

    /// <summary>"Type"</summary>
    internal static string LandType => Get("LandType");

    /// <summary>"Height (m)"</summary>
    internal static string LandHeight => Get("LandHeight");

    /// <summary>"Tree coverage"</summary>
    internal static string TreeCoverage => Get("TreeCoverage");

    /// <summary>"{0:0} trees / 1000 m²"</summary>
    internal static string TreeDensity => Get("TreeDensity");

    /// <summary>"no trees"</summary>
    internal static string NoTrees => Get("NoTrees");

    // ---- Pier settings -------------------------------------------------------------------------------

    /// <summary>"Pier"</summary>
    internal static string SectionPier => Get("SectionPier");

    /// <summary>"Type"</summary>
    internal static string PierType => Get("PierType");

    /// <summary>"Width (m)"</summary>
    internal static string PierWidth => Get("PierWidth");

    /// <summary>"Berths"</summary>
    internal static string PierBerths => Get("PierBerths");

    // ---- Berth settings ------------------------------------------------------------------------------

    /// <summary>"Berths"</summary>
    internal static string SectionBerths => Get("SectionBerths");

    /// <summary>"Width (m)"</summary>
    internal static string BerthWidth => Get("BerthWidth");

    /// <summary>"Length (m)"</summary>
    internal static string BerthLength => Get("BerthLength");

    /// <summary>"Depth (m)"</summary>
    internal static string BerthDepth => Get("BerthDepth");

    /// <summary>"Separators"</summary>
    internal static string BerthSeparators => Get("BerthSeparators");

    /// <summary>"Space between (m)"</summary>
    internal static string BerthGap => Get("BerthGap");

    /// <summary>"Line up with the berths already there"</summary>
    internal static string AlignBerths => Get("AlignBerths");

    /// <summary>"Pedestals"</summary>
    internal static string BerthServices => Get("BerthServices");

    /// <summary>"Land berth bow (°)"</summary>
    internal static string LandBerthHeading => Get("LandBerthHeading");

    // ---- Reference image -----------------------------------------------------------------------------

    /// <summary>"Reference image (north up)"</summary>
    internal static string SectionReferenceImage => Get("SectionReferenceImage");

    /// <summary>"Load image…"</summary>
    internal static string LoadImage => Get("LoadImage");

    /// <summary>"Remove"</summary>
    internal static string RemoveImage => Get("RemoveImage");

    /// <summary>"View image"</summary>
    internal static string ViewImage => Get("ViewImage");

    /// <summary>"Top view, north up"</summary>
    internal static string TopViewNorthUp => Get("TopViewNorthUp");

    /// <summary>"Show"</summary>
    internal static string ShowImage => Get("ShowImage");

    /// <summary>"Over land and piers"</summary>
    internal static string ImageAboveScene => Get("ImageAboveScene");

    /// <summary>"Opacity"</summary>
    internal static string ImageOpacity => Get("ImageOpacity");

    /// <summary>"Scale bar (m)"</summary>
    internal static string ScaleBarLength => Get("ScaleBarLength");

    /// <summary>"Apply scale"</summary>
    internal static string ApplyScale => Get("ApplyScale");

    /// <summary>"Meters / pixel"</summary>
    internal static string MetersPerPixel => Get("MetersPerPixel");

    /// <summary>"Top-down image of the marina (north up)"</summary>
    internal static string OpenImageTitle => Get("OpenImageTitle");

    /// <summary>"The image could not be loaded:  {0}"</summary>
    internal static string ImageLoadFailed => Get("ImageLoadFailed");

    /// <summary>"Reference image"</summary>
    internal static string ImageLoadFailedTitle => Get("ImageLoadFailedTitle");

    /// <summary>"Load a top-down image to trace the marina."</summary>
    internal static string NoImageYet => Get("NoImageYet");

    /// <summary>"Scale line: {0:0.0} m now. Enter its real length:"</summary>
    internal static string ScaleLineDrawn => Get("ScaleLineDrawn");

    /// <summary>"{0:0} × {1:0} m. Draw a line over the map’s scale bar."</summary>
    internal static string ImageSizeHint => Get("ImageSizeHint");

    // ---- Selection popup -----------------------------------------------------------------------------

    /// <summary>"Close (Esc)"</summary>
    internal static string PopupClose => Get("PopupClose");

    // ---- View control --------------------------------------------------------------------------------

    /// <summary>"Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)"</summary>
    internal static string FileDialogFilterImages => Get("FileDialogFilterImages");

    /// <summary>"All files (*.*)"</summary>
    internal static string FileDialogFilterAll => Get("FileDialogFilterAll");

    /// <summary>"VirtualMarina 3D view"</summary>
    internal static string DesignTimeTitle => Get("DesignTimeTitle");

    /// <summary>"The marina is rendered here at runtime (OpenGL 3.3)."</summary>
    internal static string DesignTimeSubtitle => Get("DesignTimeSubtitle");

    /// <summary>"OpenGL (not initialized)"</summary>
    internal static string RendererNotInitialized => Get("RendererNotInitialized");
}
