using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Designer.Resources;

namespace VirtualMarina.Designer;

/// <summary>
/// The sentences the Designer shows about the marina and the tool in hand, worked out the same way for both apps: the
/// panel title, the hints under a field, the summary card, the status bar and the file name a design is offered under.
/// </summary>
public static class DesignerText
{
    /// <summary>
    /// Characters left out of a suggested file name. The union of what Windows, macOS and Linux refuse, so a name
    /// suggested on one is valid on all of them; <see cref="Path.GetInvalidFileNameChars"/> would differ per system.
    /// </summary>
    private static readonly SearchValues<char> Unsafe = SearchValues.Create("<>:\"/\\|?*");

    /// <summary>The heading of the settings panel for a tool.</summary>
    /// <param name="tool">The tool in hand.</param>
    public static string ToolTitle(DesignTool tool) => tool switch
    {
        DesignTool.DrawLandArea => Strings.TitleLandArea,
        DesignTool.DrawPier => Strings.TitlePier,
        DesignTool.AddBerths => Strings.TitleBerths,
        DesignTool.AddLandBerths => Strings.TitleStorageAshore,
        DesignTool.PlantTrees => Strings.TitleTrees,
        DesignTool.Erase => Strings.TitleErase,
        DesignTool.Rename => Strings.TitleRename,
        DesignTool.EditServices => Strings.TitleServices,
        DesignTool.SelectArea => Strings.TitleSelect,
        DesignTool.DrawShoreline => Strings.TitleCoast,
        DesignTool.MoveReferenceImage => Strings.TitleMoveImage,
        DesignTool.MeasureScale => Strings.TitleMeasureScale,
        _ => Strings.TitleNavigate,
    };

    /// <summary>What the chosen separator looks like, and how far apart the berths end up with the gap.</summary>
    /// <param name="separator">The separator.</param>
    /// <param name="width">Berth width, meters.</param>
    /// <param name="gap">Gap between berths, meters.</param>
    public static string SeparatorHint(BerthSeparator separator, float width, float gap) => separator switch
    {
        BerthSeparator.FingerPiers => Strings.SeparatorHintFingerPiers,
        BerthSeparator.None => Strings.SeparatorHintNone,
        BerthSeparator.FingerPier => Strings.SeparatorHintFingerPier,
        BerthSeparator.Piles => Strings.SeparatorHintPiles,
        BerthSeparator.Boom => Strings.SeparatorHintBoom,
        BerthSeparator.PairedFingerPiers => Strings.SeparatorHintPaired,
        BerthSeparator.SinglePile => Strings.SeparatorHintSinglePile,
        _ => string.Empty,
    } + (gap > 0f ? string.Format(CultureInfo.CurrentCulture, Strings.SeparatorHintSpacing, gap, width + gap) : string.Empty);

    /// <summary>The first three names the scheme would give berths on the water, so a pattern's effect shows while it is typed.</summary>
    /// <param name="marina">The marina; its first pier is used when it has one.</param>
    /// <param name="naming">The scheme.</param>
    public static string NamingExample(MarinaVisualizer marina, BerthNamingScheme naming)
    {
        ArgumentNullException.ThrowIfNull(marina);
        ArgumentNullException.ThrowIfNull(naming);
        var piers = marina.GetPiers();
        var pier = piers.Count > 0 ? piers[0] : new Pier("A", "Pier A", Vector2.Zero, 0f, 20f);
        var numbers = Enumerable.Range(0, 3).Select(i => naming.StartNumber + i * naming.Increment);
        return Strings.Format(Strings.BerthNamingExample, string.Join(", ", numbers.Select(n => naming.Format(pier, PierSide.Left, n))));
    }

    /// <summary>The first three names the scheme would give berths ashore.</summary>
    /// <param name="marina">The marina; its first land area is used when it has one.</param>
    /// <param name="naming">The scheme.</param>
    public static string AshoreNameExample(MarinaVisualizer marina, BerthNamingScheme naming)
    {
        ArgumentNullException.ThrowIfNull(marina);
        ArgumentNullException.ThrowIfNull(naming);
        var areas = marina.GetLandAreas();
        var land = areas.Count > 0 ? areas[0] : new LandArea("yard", [new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10)], 1f);
        var start = naming.LandStartNumber ?? naming.StartNumber;
        var step = naming.LandIncrement ?? naming.Increment;
        var numbers = Enumerable.Range(0, 3).Select(i => start + i * step);
        return Strings.Format(Strings.BerthNamingExample, string.Join(", ", numbers.Select(n => naming.Format(land, n))));
    }

    /// <summary>
    /// Why a naming pattern cannot be used, or null when it can. Empty text counts as "keep the pattern there is", so
    /// it is not a problem.
    /// </summary>
    /// <param name="naming">The scheme the pattern would give.</param>
    public static string? NamingProblem(BerthNamingScheme naming)
    {
        ArgumentNullException.ThrowIfNull(naming);
        var problems = naming.Validate().ToList();
        return problems.Count == 0 ? null : string.Join(Environment.NewLine, problems);
    }

    /// <summary>The counts on the "This marina" card: berths, piers, land, trees and the overall size.</summary>
    /// <param name="marina">The marina.</param>
    public static string Summary(MarinaVisualizer marina)
    {
        ArgumentNullException.ThrowIfNull(marina);
        var berths = marina.GetBerths();
        var land = marina.GetLandAreas();
        var water = berths.Count(berth => !berth.IsOnLand);
        var trees = land.Sum(area => area.Trees.Count);
        var (min, max) = marina.GetLayout().ComputeBounds();
        var size = max - min;
        return string.Format(
            CultureInfo.CurrentCulture,
            Strings.Summary,
            berths.Count, water, berths.Count - water, marina.GetPiers().Count, land.Count, marina.GetDividers().Count, trees, size.X, size.Y);
    }

    /// <summary>Whether there is a mainland, and what covers it.</summary>
    /// <param name="marina">The marina.</param>
    public static string CoastState(MarinaVisualizer marina)
    {
        ArgumentNullException.ThrowIfNull(marina);
        return marina.Shoreline is { } shore
            ? Strings.Format(Strings.CoastPresent, shore.Points.Count, shore.Scenery.GetDisplayName())
            : Strings.CoastNone;
    }

    /// <summary>What the reference image card says: load one, draw the scale line, or type its real length.</summary>
    /// <param name="designer">The designer.</param>
    public static string ImageState(MarinaDesigner designer)
    {
        ArgumentNullException.ThrowIfNull(designer);
        if (designer.ReferenceImage is null) return Strings.ImageStateEmpty;
        if (designer.ScaleLine is { } line)
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.ImageStateLine, Vector2.Distance(line.Start, line.End));
        }

        return string.Format(
            CultureInfo.CurrentCulture, Strings.ImageStateReady, designer.ReferenceImageSize.X, designer.ReferenceImageSize.Y, designer.ReferenceImageMetersPerPixel);
    }

    /// <summary>The status bar's pointer position, empty when the pointer is not over the water.</summary>
    /// <param name="point">Plan position in meters, or null.</param>
    public static string PointerText(Vector2? point) =>
        point is { } p ? string.Format(CultureInfo.CurrentCulture, Strings.StatusPointer, p.X, p.Y) : string.Empty;

    /// <summary>The status bar's camera readout: how far away the eye is and how steeply it looks down.</summary>
    /// <param name="pose">The camera pose.</param>
    public static string CameraText(CameraPose pose) =>
        string.Format(CultureInfo.CurrentCulture, Strings.StatusCamera, pose.Distance, pose.PitchDegrees);

    /// <summary>The tooltip of Undo: what it would take back (the last point, while a drawing is in progress).</summary>
    /// <param name="designer">The designer.</param>
    public static string UndoTip(MarinaDesigner designer)
    {
        ArgumentNullException.ThrowIfNull(designer);
        if (designer.HasDraft) return Strings.UndoPointTip;
        return designer.UndoDescription is { } step ? Strings.Format(Strings.UndoTip, step) : Strings.NothingToUndo;
    }

    /// <summary>The tooltip of Redo: what it would put back.</summary>
    /// <param name="designer">The designer.</param>
    public static string RedoTip(MarinaDesigner designer)
    {
        ArgumentNullException.ThrowIfNull(designer);
        return designer.RedoDescription is { } step ? Strings.Format(Strings.RedoTip, step) : Strings.NothingToRedo;
    }

    /// <summary>The window title: an unsaved marker, the file (or marina) name and the app name.</summary>
    /// <param name="dirty">True when there are unsaved changes.</param>
    /// <param name="name">The file name, or the marina's name for a design never saved.</param>
    public static string WindowTitle(bool dirty, string name) =>
        Strings.Format(Strings.WindowTitle, dirty ? Strings.UnsavedMarker : string.Empty, name, Strings.AppName);

    /// <summary>
    /// The file name a design is offered under: the marina's name with the characters no file system accepts replaced,
    /// or "marina" when nothing is left. The extension is not included.
    /// </summary>
    /// <param name="name">The marina's name.</param>
    public static string SanitizeFileName(string? name)
    {
        var cleaned = new string((name ?? string.Empty).Select(c => char.IsControl(c) || Unsafe.Contains(c) ? '-' : c).ToArray())
            .Trim()
            .Trim('.', '-', ' ');
        return cleaned.Length == 0 ? Strings.DefaultFileName : cleaned;
    }

    /// <summary>A menu label without its <c>&amp;</c> accelerator; <c>&amp;&amp;</c> stays a literal ampersand.</summary>
    /// <param name="label">The label.</param>
    public static string StripMnemonic(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        return label.Replace("&&", "\u0001", StringComparison.Ordinal)
            .Replace("&", string.Empty, StringComparison.Ordinal)
            .Replace('\u0001', '&');
    }

    /// <summary>The three-part version of the assembly a type lives in, e.g. "1.2.0".</summary>
    /// <param name="type">Any type from the assembly.</param>
    public static string Version(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.Assembly.GetName().Version is { } version ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
    }

    /// <summary>The About box.</summary>
    /// <param name="appType">A type from the app, for its version.</param>
    /// <param name="renderer">What draws the marina, e.g. the OpenGL or WebGL renderer's description.</param>
    /// <param name="author">Who to credit.</param>
    /// <param name="year">The year shown in the copyright line.</param>
    public static string About(Type appType, string renderer, string author, int year) =>
        Strings.Format(
            Strings.AboutBody,
            Strings.AppName,
            Version(appType),
            Version(typeof(MarinaVisualizer)),
            renderer,
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            year,
            author);

    /// <summary>How a design file names the program that wrote it: the app's name and version.</summary>
    /// <param name="appType">A type from the app.</param>
    public static string Generator(Type appType)
    {
        ArgumentNullException.ThrowIfNull(appType);
        return $"{Strings.AppName} {InformationalVersion(appType)}";
    }

    private static string InformationalVersion(Type type) =>
        type.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion is { Length: > 0 } version
            ? version.Split('+')[0]
            : Version(type);
}
