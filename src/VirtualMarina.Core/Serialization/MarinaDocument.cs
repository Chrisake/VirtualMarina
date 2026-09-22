using System.Globalization;
using System.Text.Json;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Serialization;

/// <summary>
/// A whole marina in one file: its layout (land areas, piers, dividers, berths and berths), how it is drawn and animated (water,
/// waves, lighting, status colors, labels, camera) and the designer's tool settings. Saved as JSON by the designer and loaded by
/// the host application, so personalization travels with the design instead of living in host code.
/// </summary>
/// <remarks>
/// <para>
/// The format is <c>{ "format": "virtualmarina.marina", "formatVersion": "1.0", ... }</c>. Reading is deliberately forgiving so
/// files survive version changes in both directions:
/// </para>
/// <list type="bullet">
/// <item><description>Missing properties fall back to the current defaults, so files written by older versions keep working.</description></item>
/// <item><description>Properties this version doesn't know are kept in <see cref="Extensions"/> (document level) or alongside the element
/// they belong to, and written back out, so a newer file survives a round trip through an older application.</description></item>
/// <item><description>Unknown enum names fall back to the default instead of throwing.</description></item>
/// <item><description>A file whose major version is newer than <see cref="CurrentVersion"/> is refused unless you pass
/// <c>allowNewerVersion</c>: its meaning can't be guessed.</description></item>
/// </list>
/// <para>Use <see cref="Extensions"/> for your own data (ERP ids, tariffs, ...); those keys are never touched by the library.</para>
/// </remarks>
/// <example>
/// <code>
/// // Designer: save what the user drew, with the look and the camera they chose.
/// MarinaDocument.FromVisualizer(marina, generator: "My Designer 1.0").Save(@"C:\marinas\harbor.marina.json");
///
/// // Host application: load it into a view.
/// MarinaDocument.Load(@"C:\marinas\harbor.marina.json").ApplyTo(marinaView.Marina);
/// </code>
/// </example>
public sealed class MarinaDocument
{
    /// <summary>Value of the file's <c>format</c> property: <c>virtualmarina.marina</c>.</summary>
    public const string FormatName = "virtualmarina.marina";

    /// <summary>Recommended file extension, <c>.marina.json</c>.</summary>
    public const string FileExtension = ".marina.json";

    /// <summary>File filter for open/save dialogs.</summary>
    public const string FileDialogFilter = "Marina design (*.marina.json)|*.marina.json|JSON files (*.json)|*.json|All files (*.*)|*.*";

    private readonly Dictionary<string, Dictionary<string, JsonElement>> _elementExtras = new(StringComparer.Ordinal);

    /// <summary>The version of the format this build writes (2.0).</summary>
    public static Version CurrentVersion { get; } = new(2, 0);

    /// <summary>Name of the marina, shown in titles and stored as <c>MarinaLayout.Name</c>.</summary>
    public string Name { get; set; } = "Marina";

    /// <summary>Free-text description of the design (not used by the renderer).</summary>
    public string? Description { get; set; }

    /// <summary>The marina itself: land areas, piers, dividers, berths and multi-berths.</summary>
    public MarinaLayout Layout { get; set; } = MarinaLayout.Empty;

    /// <summary>How the marina is drawn and animated: water and waves, lighting, status colors, land, piers, labels, selection, camera optics.</summary>
    public MarinaStyle Style { get; set; } = new();

    /// <summary>Which berths show their name on the water.</summary>
    public BerthLabelMode BerthLabels { get; set; } = BerthLabelMode.None;

    /// <summary>Where the camera should start, or null to use the automatic overview.</summary>
    public CameraPose? Camera { get; set; }

    /// <summary>
    /// Named viewpoints saved with the design, offered by the host as "go to this view". The ones generated from
    /// the layout (Overview, Top Down, one per pier) are not stored: they are rebuilt from the piers on load, so
    /// they stay right when the marina changes.
    /// </summary>
    public IReadOnlyList<CameraPreset> CameraPresets { get; set; } = Array.Empty<CameraPreset>();

    /// <summary>The designer's tool settings when the file was saved, so a design reopens the way it was left. Null when not stored.</summary>
    public DesignerSettings? Designer { get; set; }

    /// <summary>
    /// The picture the marina was traced on, with its place and scale, or null. Stored so a design can be reopened
    /// and corrected against the same photo later; see <see cref="ReferenceImageRecord"/>.
    /// </summary>
    public ReferenceImageRecord? ReferenceImage { get; set; }

    /// <summary>Version of the format the document was read from; <see cref="CurrentVersion"/> for a new one.</summary>
    public Version Version { get; private set; } = CurrentVersion;

    /// <summary>What wrote the file, e.g. "VirtualMarina Designer 1.0".</summary>
    public string? Generator { get; set; }

    /// <summary>When the file was written.</summary>
    public DateTimeOffset? SavedUtc { get; set; }

    /// <summary>
    /// Anything else stored in the file: your own sections, and sections written by a newer version of the format. Keys here are
    /// written back exactly as they came in.
    /// </summary>
    public IDictionary<string, JsonElement> Extensions { get; } = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    /// <summary>True when the file came from a newer minor version, so it may contain settings this build ignores.</summary>
    public bool IsFromNewerVersion => Version > CurrentVersion;

    // ---- Building ------------------------------------------------------------------------------

    /// <summary>Captures a visualizer: its layout, style, label mode, camera and (optionally) the designer's settings.</summary>
    /// <param name="marina">The visualizer to capture.</param>
    /// <param name="generator">Name and version of the application writing the file.</param>
    /// <param name="includeCamera">Store the camera pose so the view opens where the designer left it (default true).</param>
    /// <param name="includeDesignerSettings">Store the designer's tool settings (default true).</param>
    /// <param name="includeReferenceImage">
    /// Store the tracing image, when there is one and its original file bytes are known (default true). It is by far
    /// the largest thing in a marina file, so pass false for a layout-only export.
    /// </param>
    public static MarinaDocument FromVisualizer(
        MarinaVisualizer marina,
        string? generator = null,
        bool includeCamera = true,
        bool includeDesignerSettings = true,
        bool includeReferenceImage = true)
    {
        ArgumentNullException.ThrowIfNull(marina);
        return new MarinaDocument
        {
            Name = marina.MarinaName,
            Layout = marina.GetLayout(),
            Style = marina.Style,
            BerthLabels = marina.BerthLabelMode,
            Camera = includeCamera ? marina.Camera.DesiredPose : null,
            CameraPresets = includeCamera ? marina.CameraPresets.Where(preset => !preset.IsBuiltIn).ToArray() : Array.Empty<CameraPreset>(),
            Designer = includeDesignerSettings ? DesignerSettings.FromDesigner(marina.Designer) : null,
            ReferenceImage = includeReferenceImage ? ReferenceImageRecord.FromDesigner(marina.Designer) : null,
            Generator = generator,
        };
    }

    /// <summary>
    /// Loads the document into a visualizer: the style and label mode first (so the water grid is built at the stored size), then
    /// the layout, then the camera and the designer's settings.
    /// </summary>
    /// <param name="marina">The visualizer to load into.</param>
    /// <param name="applyStyle">Apply the stored look and animation (default true).</param>
    /// <param name="applyCamera">Move the camera to the stored pose, if the file has one (default true).</param>
    /// <param name="applyDesignerSettings">Apply the stored designer tool settings, if the file has them (default true).</param>
    /// <param name="applyReferenceImage">Put the stored tracing image back under the design, if the file has one (default true).</param>
    /// <exception cref="MarinaLayoutException">The layout in the document is not valid (e.g. a berth references a missing pier).</exception>
    public void ApplyTo(
        MarinaVisualizer marina,
        bool applyStyle = true,
        bool applyCamera = true,
        bool applyDesignerSettings = true,
        bool applyReferenceImage = true)
    {
        ArgumentNullException.ThrowIfNull(marina);
        if (applyStyle) marina.Style = Style;

        marina.InitializeLayout(Layout with { Name = Name });
        marina.BerthLabelMode = BerthLabels;
        if (applyCamera)
        {
            foreach (var preset in CameraPresets) marina.AddCameraPreset(preset with { IsBuiltIn = false });
            if (Camera is { } pose) marina.Camera.SetPose(pose, immediate: true);
        }

        if (applyDesignerSettings) Designer?.ApplyTo(marina.Designer);
        if (applyReferenceImage) ReferenceImage?.ApplyTo(marina.Designer);
    }

    /// <summary>Errors in the stored layout, empty when it is valid (the same checks <c>InitializeLayout</c> makes).</summary>
    public IReadOnlyList<string> Validate() => Layout.Validate();

    // ---- Reading and writing --------------------------------------------------------------------

    /// <summary>Reads a document from JSON.</summary>
    /// <param name="json">The file contents.</param>
    /// <param name="allowNewerVersion">Accept a file whose major version is newer than this build understands (values it doesn't know are ignored).</param>
    /// <exception cref="MarinaFormatException">The text is not valid JSON, is not a marina file, or comes from a newer major version.</exception>
    public static MarinaDocument Parse(string json, bool allowNewerVersion = false)
    {
        ArgumentNullException.ThrowIfNull(json);
        DocumentDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize(json, MarinaJson.Indented.DocumentDto);
        }
        catch (JsonException ex)
        {
            throw new MarinaFormatException("The file is not valid JSON: " + ex.Message, ex);
        }

        if (dto is null) throw new MarinaFormatException("The file is empty.");
        if (dto.Format is { Length: > 0 } format && !string.Equals(format, FormatName, StringComparison.OrdinalIgnoreCase))
        {
            throw new MarinaFormatException($"This is a '{format}' file, not a {FormatName} file.");
        }

        var version = ParseVersion(dto.FormatVersion);
        if (!allowNewerVersion && version.Major > CurrentVersion.Major)
        {
            throw new MarinaFormatException(
                $"The file was written in format version {version}, which is newer than this application understands ({CurrentVersion}). Update the application to open it.");
        }

        var document = new MarinaDocument
        {
            Version = version,
            Generator = dto.Generator,
            SavedUtc = dto.SavedUtc,
            Name = dto.Marina?.Name is { Length: > 0 } name ? name : "Marina",
            Description = dto.Marina?.Description,
            Layout = ReadLayout(dto.Layout, version, out var extras),
            Style = dto.Presentation?.ToStyle() ?? new MarinaStyle(),
            BerthLabels = dto.Presentation?.ReadBerthLabels() ?? BerthLabelMode.None,
            Camera = dto.Camera is { } camera ? new CameraPose(camera.Target, camera.YawDegrees, camera.PitchDegrees, camera.Distance) : null,
            Designer = dto.Designer?.ToDomain(),
            ReferenceImage = dto.ReferenceImage?.ToDomain(),
            CameraPresets = dto.CameraPresets?.Select(preset => preset.ToDomain()).ToArray() ?? Array.Empty<CameraPreset>(),
        };

        document.Name = document.Layout.Name is { Length: > 0 } && dto.Marina?.Name is null ? document.Layout.Name : document.Name;
        foreach (var (key, value) in extras) document._elementExtras[key] = value;
        document.Keep(dto.Extra);
        document.Keep(dto.Marina?.Extra, "marina");
        document.Keep(dto.Layout?.Extra, "layout");
        document.Keep(dto.Camera?.Extra, "camera");
        document.Keep(dto.Designer?.Extra, "designer");
        document.KeepPresentation(dto.Presentation);
        return document;
    }

    /// <summary>Writes the document as JSON.</summary>
    /// <param name="indented">Lay the JSON out over several lines (default true); false gives the most compact file.</param>
    public string ToJson(bool indented = true)
    {
        var dto = new DocumentDto
        {
            Format = FormatName,
            FormatVersion = CurrentVersion.ToString(2),
            Generator = Generator,
            SavedUtc = SavedUtc ?? DateTimeOffset.UtcNow,
            Marina = new MarinaDto { Name = Name, Description = Description, Extra = Restore("marina") },
            Layout = WriteLayout(),
            Presentation = PresentationDto.From(Style, BerthLabels),
            Camera = Camera is { } pose
                ? new CameraDto { Target = pose.Target, YawDegrees = pose.YawDegrees, PitchDegrees = pose.PitchDegrees, Distance = pose.Distance, Extra = Restore("camera") }
                : null,
            Designer = Designer is { } settings ? DesignerDto.From(settings) : null,
            ReferenceImage = ReferenceImage is { } image ? ReferenceImageDto.From(image) : null,
            CameraPresets = CameraPresets.Count == 0 ? null : CameraPresets.Select(CameraPresetDto.From).ToList(),
            Extra = Extensions.Count == 0 ? null : new Dictionary<string, JsonElement>(Extensions, StringComparer.Ordinal),
        };

        dto.Layout!.Extra = Restore("layout");
        if (dto.Designer is not null) dto.Designer.Extra = Restore("designer");
        RestorePresentation(dto.Presentation!);
        return JsonSerializer.Serialize(dto, indented ? MarinaJson.Indented.DocumentDto : MarinaJson.Compact.DocumentDto);
    }

    /// <summary>Reads a document from a file.</summary>
    /// <param name="path">Path of the <c>.marina.json</c> file.</param>
    /// <param name="allowNewerVersion">Accept a file from a newer major version of the format.</param>
    /// <exception cref="MarinaFormatException">The file is not a valid marina file.</exception>
    public static MarinaDocument Load(string path, bool allowNewerVersion = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllText(path), allowNewerVersion);
    }

    /// <summary>Writes the document to a file (UTF-8).</summary>
    /// <param name="path">Where to write it.</param>
    /// <param name="indented">Lay the JSON out over several lines (default true).</param>
    public void Save(string path, bool indented = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        SavedUtc = DateTimeOffset.UtcNow;
        File.WriteAllText(path, ToJson(indented));
    }

    // ---- Extensions ----------------------------------------------------------------------------

    /// <summary>Stores your own data in the file under <paramref name="key"/> (serialized with the marina JSON settings).</summary>
    /// <typeparam name="T">Type of the value.</typeparam>
    /// <param name="key">Property name in the document; use something unlikely to clash, e.g. your company or product name.</param>
    /// <param name="value">The value, or null to remove the key.</param>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Host data is serialized with reflection; keep the type or write the JsonElement yourself.")]
    public void SetExtension<T>(string key, T? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (value is null)
        {
            Extensions.Remove(key);
            return;
        }

        Extensions[key] = JsonSerializer.SerializeToElement(value, ExtensionOptions);
    }

    /// <summary>Reads back data stored with <see cref="SetExtension"/> (or written by another application), or the default when missing or unreadable.</summary>
    /// <typeparam name="T">Type to read it as.</typeparam>
    /// <param name="key">Property name in the document.</param>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Host data is deserialized with reflection; keep the type or read the JsonElement yourself.")]
    public T? GetExtension<T>(string key)
    {
        if (key is null || !Extensions.TryGetValue(key, out var element)) return default;
        try
        {
            return element.Deserialize<T>(ExtensionOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    // ---- Internals -----------------------------------------------------------------------------

    /// <summary>Reflection-based options for host data of any type (the marina format itself is source-generated).</summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Only reached from the [RequiresUnreferencedCode] extension helpers.")]
    private static JsonSerializerOptions ExtensionOptions { get; } = new(MarinaJson.Options)
    {
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    private static Version ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return CurrentVersion;
        if (Version.TryParse(text.Trim(), out var version)) return version;
        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var major) ? new Version(major, 0) : CurrentVersion;
    }

    /// <summary>
    /// Reads the layout, taking the element names of <paramref name="version"/> into account: up to format 1.x piers were called
    /// docks, berths were called slips, and "berths" was the list of multi-berth groups.
    /// </summary>
    private static MarinaLayout ReadLayout(LayoutDto? dto, Version version, out Dictionary<string, Dictionary<string, JsonElement>> extras)
    {
        extras = new Dictionary<string, Dictionary<string, JsonElement>>(StringComparer.Ordinal);
        if (dto is null) return MarinaLayout.Empty;

        var legacy = version.Major < 2;

        var shoreline = dto.Shoreline?.ToDomain();
        if (dto.Shoreline is not null) Remember(extras, "shoreline", string.Empty, dto.Shoreline.Extra);

        var landAreas = new List<LandArea>();
        foreach (var land in dto.LandAreas ?? new List<LandAreaDto>())
        {
            landAreas.Add(land.ToDomain());
            Remember(extras, "land", land.Id, land.Extra);
        }

        var piers = new List<Pier>();
        foreach (var pier in dto.ReadPiers())
        {
            piers.Add(pier.ToDomain());
            Remember(extras, "pier", pier.Id, pier.Extra);
        }

        var dividers = new List<Divider>();
        foreach (var divider in dto.Dividers ?? new List<DividerDto>())
        {
            dividers.Add(divider.ToDomain());
            Remember(extras, "divider", divider.Id, divider.Extra);
        }

        var berths = new List<Berth>();
        foreach (var berth in dto.ReadBerths(legacy))
        {
            berths.Add(berth.ToDomain());
            Remember(extras, "berth", berth.Id, berth.Extra);
            Remember(extras, "boat", berth.Id, berth.Boat?.Extra);
        }

        var multiBerths = new List<MultiBerth>();
        foreach (var group in dto.ReadMultiBerths(legacy))
        {
            if (group.ToDomain() is not { } domain) continue;
            multiBerths.Add(domain);
            Remember(extras, "group", group.Id, group.Extra);
        }

        return new MarinaLayout
        {
            Shoreline = shoreline,
            LandAreas = landAreas,
            Piers = piers,
            Dividers = dividers,
            Berths = berths,
            MultiBerths = multiBerths,
        };
    }

    private LayoutDto WriteLayout()
    {
        var layout = new LayoutDto
        {
            Shoreline = Layout.Shoreline is { } shore ? Attach(ShorelineDto.From(shore), "shoreline", string.Empty) : null,
            LandAreas = Layout.LandAreas.Select(land => Attach(LandAreaDto.From(land), "land", land.Id)).ToList(),
            Piers = Layout.Piers.Select(pier => Attach(PierDto.From(pier), "pier", pier.Id)).ToList(),
            Dividers = Layout.Dividers.Select(divider => Attach(DividerDto.From(divider), "divider", divider.Id)).ToList(),
            Berths = Layout.Berths.Select(berth =>
            {
                var dto = Attach(BerthDto.From(berth), "berth", berth.Id);
                if (dto.Boat is not null) dto.Boat.Extra = Restore("boat:" + berth.Id);
                return dto;
            }).ToList(),
            MultiBerths = Layout.MultiBerths.Count == 0
                ? null
                : Layout.MultiBerths.Select(group => Attach(MultiBerthDto.From(group), "group", group.Id)).ToList(),
        };

        return layout;
    }

    private static void Remember(Dictionary<string, Dictionary<string, JsonElement>> extras, string kind, string id, Dictionary<string, JsonElement>? extra)
    {
        if (extra is { Count: > 0 }) extras[kind + ":" + id] = extra;
    }

    private T Attach<T>(T dto, string kind, string id)
        where T : ExtensibleDto
    {
        dto.Extra = Restore(kind + ":" + id);
        return dto;
    }

    private Dictionary<string, JsonElement>? Restore(string key) =>
        _elementExtras.TryGetValue(key, out var extra) && extra.Count > 0 ? extra : null;

    /// <summary>Remembers the properties of the look-and-feel sections that this version doesn't know.</summary>
    private void KeepPresentation(PresentationDto? presentation)
    {
        if (presentation is null) return;
        Keep(presentation.Extra, "presentation");
        Keep(presentation.Water?.Extra, "presentation:water");
        Keep(presentation.Lighting?.Extra, "presentation:lighting");
        Keep(presentation.Status?.Extra, "presentation:status");
        Keep(presentation.Land?.Extra, "presentation:land");
        Keep(presentation.Structures?.Extra, "presentation:structures");
        Keep(presentation.Labels?.Extra, "presentation:labels");
        Keep(presentation.Selection?.Extra, "presentation:selection");
        Keep(presentation.View?.Extra, "presentation:view");
    }

    /// <summary>Writes them back out next to the settings this version does know.</summary>
    private void RestorePresentation(PresentationDto presentation)
    {
        presentation.Extra = Restore("presentation");
        if (presentation.Water is not null) presentation.Water.Extra = Restore("presentation:water");
        if (presentation.Lighting is not null) presentation.Lighting.Extra = Restore("presentation:lighting");
        if (presentation.Status is not null) presentation.Status.Extra = Restore("presentation:status");
        if (presentation.Land is not null) presentation.Land.Extra = Restore("presentation:land");
        if (presentation.Structures is not null) presentation.Structures.Extra = Restore("presentation:structures");
        if (presentation.Labels is not null) presentation.Labels.Extra = Restore("presentation:labels");
        if (presentation.Selection is not null) presentation.Selection.Extra = Restore("presentation:selection");
        if (presentation.View is not null) presentation.View.Extra = Restore("presentation:view");
    }

    private void Keep(Dictionary<string, JsonElement>? extra, string? section = null)
    {
        if (extra is not { Count: > 0 }) return;
        if (section is null)
        {
            foreach (var (key, value) in extra) Extensions[key] = value;
            return;
        }

        _elementExtras[section] = extra;
    }
}

/// <summary>A marina file could not be read: it is not JSON, not a marina file, or comes from a newer major version of the format.</summary>
public sealed class MarinaFormatException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong with the file.</param>
    public MarinaFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception from a lower-level failure.</summary>
    /// <param name="message">What is wrong with the file.</param>
    /// <param name="innerException">The underlying error.</param>
    public MarinaFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
