using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Rendering;
using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Serialization;

/// <summary>
/// A whole marina in one file: its layout (land areas, piers, dividers, berths and multi-berths), how it is drawn and animated (water,
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
/// <item><description>A file written by an older version is brought up to date as it is read, one version step at a time, so the
/// document always holds the current format; saving it writes the current format.</description></item>
/// </list>
/// <para>
/// Files are read and written as UTF-8. The <see cref="Parse(ReadOnlySpan{byte}, bool)"/>, <see cref="LoadAsync"/>,
/// <see cref="SaveAsync"/> and <see cref="ToUtf8Bytes"/> members work on bytes and streams directly, which matters for a design
/// carrying a large tracing photo; the string members are wrappers around them.
/// </para>
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

    /// <summary>
    /// Names of the built-in views the designer switched off. The built-in views themselves are rebuilt from the
    /// layout, so only the choice of which to offer is stored.
    /// </summary>
    public IReadOnlyList<string> DisabledCameraPresets { get; set; } = Array.Empty<string>();

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
    /// <remarks>
    /// A key may not be one of the names the format itself uses at the top of the file (<c>layout</c>, <c>presentation</c>,
    /// <c>camera</c> and the rest, in any case): it would be written next to the real one, and the file would not read back as
    /// it was saved. Such a key is refused with an <see cref="ArgumentException"/>.
    /// </remarks>
    public IDictionary<string, JsonElement> Extensions { get; } = new ExtensionDictionary();

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
    /// <remarks>
    /// This starts a new document. To save a design that was opened from a file, call <see cref="UpdateFrom"/> on the
    /// document it was loaded from instead, so what that file carried beyond this version's settings is written back.
    /// </remarks>
    public static MarinaDocument FromVisualizer(
        MarinaVisualizer marina,
        string? generator = null,
        bool includeCamera = true,
        bool includeDesignerSettings = true,
        bool includeReferenceImage = true)
    {
        var document = new MarinaDocument();
        document.UpdateFrom(marina, generator, includeCamera, includeDesignerSettings, includeReferenceImage);
        return document;
    }

    /// <summary>
    /// Refreshes this document from a visualizer — its layout, style, label mode, camera and (optionally) the designer's
    /// settings — while keeping what the file carried that the visualizer knows nothing about: the
    /// <see cref="Description"/>, the <see cref="Extensions"/>, and the properties a newer version stored alongside
    /// individual elements.
    /// </summary>
    /// <param name="marina">The visualizer to capture.</param>
    /// <param name="generator">Name and version of the application writing the file; null keeps the current <see cref="Generator"/>.</param>
    /// <param name="includeCamera">Store the camera pose and the saved views (default true); false drops them from the document.</param>
    /// <param name="includeDesignerSettings">Store the designer's tool settings (default true); false drops them from the document.</param>
    /// <param name="includeReferenceImage">
    /// Store the tracing image, when there is one and its original file bytes are known (default true); false drops it
    /// from the document.
    /// </param>
    /// <remarks>
    /// This is how a designer saves a design it opened: load the document, <see cref="ApplyTo"/> it, let the user edit,
    /// then call this on the same document and save it. Unknown properties stay with the element they came with, matched
    /// by id; those of elements that have since been deleted are simply not written.
    /// </remarks>
    /// <example>
    /// <code>
    /// var document = MarinaDocument.Load(path);
    /// document.ApplyTo(marina);
    /// // ... the user edits the marina ...
    /// document.UpdateFrom(marina, generator: "My Designer 1.0");
    /// document.Save(path);
    /// </code>
    /// </example>
    public void UpdateFrom(
        MarinaVisualizer marina,
        string? generator = null,
        bool includeCamera = true,
        bool includeDesignerSettings = true,
        bool includeReferenceImage = true)
    {
        ArgumentNullException.ThrowIfNull(marina);
        Name = marina.MarinaName;
        Layout = marina.GetLayout();
        Style = marina.Style.Clone();
        BerthLabels = marina.BerthLabelMode;
        Camera = includeCamera ? marina.Camera.DesiredPose : null;
        CameraPresets = includeCamera ? marina.CameraPresets.Where(preset => !preset.IsBuiltIn).ToArray() : Array.Empty<CameraPreset>();
        DisabledCameraPresets = includeCamera ? marina.DisabledCameraPresets.ToArray() : Array.Empty<string>();
        Designer = includeDesignerSettings ? DesignerSettings.FromDesigner(marina.Designer) : null;
        ReferenceImage = includeReferenceImage ? ReferenceImageRecord.FromDesigner(marina.Designer) : null;
        Generator = generator ?? Generator;
    }

    /// <summary>
    /// Loads the document into a visualizer: the style and label mode first (so the water grid is built at the stored size), then
    /// the layout, then the camera and the designer's settings. What the visualizer showed before is replaced, not merged: its
    /// saved views and tracing image go too, when the document's camera and tracing image are applied.
    /// </summary>
    /// <param name="marina">The visualizer to load into.</param>
    /// <param name="applyStyle">Apply the stored look and animation (default true). The visualizer gets its own copy of it.</param>
    /// <param name="applyCamera">
    /// Move the camera to the stored pose, if the file has one, and replace the saved views and the switched-off built-in
    /// views with the file's (default true).
    /// </param>
    /// <param name="applyDesignerSettings">Apply the stored designer tool settings, if the file has them (default true).</param>
    /// <param name="applyReferenceImage">
    /// Put the stored tracing image back under the design, or remove the current one when the file has none (default true).
    /// </param>
    /// <exception cref="MarinaLayoutException">
    /// The layout in the document is not valid (e.g. a berth references a missing pier). Nothing is changed in the visualizer.
    /// </exception>
    public void ApplyTo(
        MarinaVisualizer marina,
        bool applyStyle = true,
        bool applyCamera = true,
        bool applyDesignerSettings = true,
        bool applyReferenceImage = true)
    {
        ArgumentNullException.ThrowIfNull(marina);

        // Checked before anything is touched, so a file that can't be opened leaves the current design as it was.
        var layout = Layout with { Name = Name };
        var errors = layout.Validate();
        if (errors.Count > 0) throw new MarinaLayoutException(errors);

        if (applyStyle) marina.Style = Style.Clone();

        marina.InitializeLayout(layout);
        marina.BerthLabelMode = BerthLabels;
        if (applyCamera)
        {
            marina.RestoreCameraPresets(CameraPresets, DisabledCameraPresets);
            if (Camera is { } pose) marina.Camera.SetPose(pose, immediate: true);
        }

        if (applyDesignerSettings) Designer?.ApplyTo(marina.Designer);
        if (applyReferenceImage)
        {
            if (ReferenceImage is { } image) image.ApplyTo(marina.Designer);
            else marina.Designer.ClearReferenceImage();
        }
    }

    /// <summary>Errors in the stored layout, empty when it is valid (the same checks <c>InitializeLayout</c> makes).</summary>
    public IReadOnlyList<string> Validate() => Layout.Validate();

    // ---- Reading and writing --------------------------------------------------------------------

    /// <summary>Reads a document from JSON text.</summary>
    /// <param name="json">The file contents.</param>
    /// <param name="allowNewerVersion">Accept a file whose major version is newer than this build understands (values it doesn't know are ignored).</param>
    /// <exception cref="MarinaFormatException">The text is not valid JSON, is not a marina file, or comes from a newer major version.</exception>
    /// <remarks>A wrapper around <see cref="Parse(ReadOnlySpan{byte}, bool)"/>; prefer that one when you have the bytes.</remarks>
    public static MarinaDocument Parse(string json, bool allowNewerVersion = false)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Parse(Encoding.UTF8.GetBytes(json), allowNewerVersion);
    }

    /// <summary>Reads a document from UTF-8 JSON, such as the bytes of a file, a database column or a download.</summary>
    /// <param name="utf8Json">The file contents as UTF-8. A leading byte-order mark is skipped.</param>
    /// <param name="allowNewerVersion">Accept a file whose major version is newer than this build understands (values it doesn't know are ignored).</param>
    /// <exception cref="MarinaFormatException">The data is not valid JSON, is not a marina file, or comes from a newer major version.</exception>
    /// <remarks>
    /// The tracing image is decoded from its base64 straight into bytes, so a design with a large photo is read without the
    /// photo ever being held as text.
    /// </remarks>
    public static MarinaDocument Parse(ReadOnlySpan<byte> utf8Json, bool allowNewerVersion = false)
    {
        if (utf8Json.StartsWith(Utf8Bom)) utf8Json = utf8Json[Utf8Bom.Length..];
        var dto = Deserialize(utf8Json);
        if (dto.Format is { Length: > 0 } format && !string.Equals(format, FormatName, StringComparison.OrdinalIgnoreCase))
        {
            throw new MarinaFormatException(Strings.Format(Strings.ErrorFileWrongFormat, format, FormatName));
        }

        var declared = ParseVersion(dto.FormatVersion);
        var version = declared ?? CurrentVersion;
        if (!allowNewerVersion && version.Major > CurrentVersion.Major)
        {
            throw new MarinaFormatException(
                Strings.Format(Strings.ErrorFileTooNew, version, CurrentVersion));
        }

        // An older file, or one that still uses an older version's names whatever it says it is, is brought up to date as raw
        // JSON and read again. A current file (nearly all of them) is read once.
        var looksLegacy = PiersAndBerthsMigration.LooksLegacy(dto);
        if (looksLegacy || (declared is { } stated && MarinaMigrations.Compare(stated, CurrentVersion) < 0))
        {
            var start = declared is null || (looksLegacy && MarinaMigrations.Compare(declared, MarinaMigrations.Legacy) > 0) ? MarinaMigrations.Legacy : declared;
            dto = Migrate(utf8Json, start, new MigrationContext(declared));
        }

        try
        {
            return FromDto(dto, version);
        }
        catch (ArgumentException ex)
        {
            // Nothing the file holds should get this far (see ExtensibleDto), but if it does, it is still the file that is wrong.
            throw new MarinaFormatException(Strings.Format(Strings.ErrorFileBadValue, ex.Message), ex);
        }
    }

    /// <summary>Reads a document from a stream of UTF-8 JSON, such as an open file or an HTTP response body.</summary>
    /// <param name="utf8Json">The stream, read to its end. It is not closed.</param>
    /// <param name="allowNewerVersion">Accept a file from a newer major version of the format.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <exception cref="MarinaFormatException">The data is not a valid marina file.</exception>
    public static async Task<MarinaDocument> LoadAsync(Stream utf8Json, bool allowNewerVersion = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);
        using var buffer = utf8Json.CanSeek ? new MemoryStream(checked((int)Math.Max(0, utf8Json.Length - utf8Json.Position))) : new MemoryStream();
        await utf8Json.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return Parse(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), allowNewerVersion);
    }

    private static MarinaDocument FromDto(DocumentDto dto, Version version)
    {
        var document = new MarinaDocument
        {
            Version = version,
            Generator = dto.Generator,
            SavedUtc = dto.SavedUtc,
            Name = dto.Marina?.Name is { Length: > 0 } name ? name : "Marina",
            Description = dto.Marina?.Description,
            Layout = ReadLayout(dto.Layout, out var extras),
            Style = dto.Presentation?.ToStyle() ?? new MarinaStyle(),
            BerthLabels = dto.Presentation?.BerthLabels ?? BerthLabelMode.None,
            Camera = dto.Camera is { } camera ? new CameraPose(camera.Target, camera.YawDegrees, camera.PitchDegrees, camera.Distance) : null,
            Designer = dto.Designer?.ToDomain(),
            ReferenceImage = dto.ReferenceImage?.ToDomain(),
            CameraPresets = dto.CameraPresets?.Select(preset => preset.ToDomain()).ToArray() ?? Array.Empty<CameraPreset>(),
            DisabledCameraPresets = dto.DisabledCameraPresets?.ToArray() ?? Array.Empty<string>(),
        };

        foreach (var (key, value) in extras) document._elementExtras[key] = value;
        document.Keep(dto.Extra);
        document.Keep(dto.Marina?.Extra, "marina");
        document.Keep(dto.Layout?.Extra, "layout");
        document.Keep(dto.Camera?.Extra, "camera");
        document.Keep(dto.Designer?.Extra, "designer");
        document.Keep(dto.Designer?.BerthNaming?.Extra, "designer:berthNaming");
        document.Keep(dto.ReferenceImage?.Extra, "referenceImage");
        // Keyed by place in the list rather than by name: two views may share a name, and one may be renamed.
        var presets = dto.CameraPresets ?? [];
        for (var index = 0; index < presets.Count; index++) document.Keep(presets[index].Extra, PresetKey(index));
        document.KeepPresentation(dto.Presentation);
        return document;
    }

    /// <summary>Writes the document as JSON text.</summary>
    /// <param name="indented">Lay the JSON out over several lines (default true); false gives the most compact file.</param>
    public string ToJson(bool indented = true) => JsonSerializer.Serialize(ToDto(SavedUtc ?? DateTimeOffset.UtcNow), TypeInfo(indented));

    /// <summary>Writes the document as UTF-8 JSON, ready to store or send.</summary>
    /// <param name="indented">Lay the JSON out over several lines (default true); false gives the most compact file.</param>
    public byte[] ToUtf8Bytes(bool indented = true) => JsonSerializer.SerializeToUtf8Bytes(ToDto(SavedUtc ?? DateTimeOffset.UtcNow), TypeInfo(indented));

    /// <summary>Reads a document from a file.</summary>
    /// <param name="path">Path of the <c>.marina.json</c> file.</param>
    /// <param name="allowNewerVersion">Accept a file from a newer major version of the format.</param>
    /// <exception cref="MarinaFormatException">The file is not a valid marina file.</exception>
    /// <remarks>The file is read as UTF-8; a file saved as UTF-16 by a text editor (with its byte-order mark) is read too.</remarks>
    public static MarinaDocument Load(string path, bool allowNewerVersion = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Load(File.ReadAllBytes(path), allowNewerVersion);
    }

    /// <summary>Reads a document from the bytes of a marina file, such as a database column or a download.</summary>
    /// <param name="data">The file contents.</param>
    /// <param name="allowNewerVersion">Accept a file from a newer major version of the format.</param>
    /// <exception cref="MarinaFormatException">The data is not a valid marina file.</exception>
    /// <remarks>
    /// The bytes are read as <see cref="Load(string, bool)"/> reads a file: as UTF-8, or as UTF-16 when they start with its
    /// byte-order mark.
    /// </remarks>
    public static MarinaDocument Load(byte[] data, bool allowNewerVersion = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length >= 2 && ((data[0] == 0xFF && data[1] == 0xFE) || (data[0] == 0xFE && data[1] == 0xFF)))
        {
            // UTF-16 with its byte-order mark, as some editors save: decoded the slow way, since it is rare.
            using var reader = new StreamReader(new MemoryStream(data), detectEncodingFromByteOrderMarks: true);
            return Parse(reader.ReadToEnd(), allowNewerVersion);
        }

        return Parse(data, allowNewerVersion);
    }

    /// <summary>Writes the document to a file (UTF-8), and stamps <see cref="SavedUtc"/> once it is safely written.</summary>
    /// <param name="path">Where to write it.</param>
    /// <param name="indented">Lay the JSON out over several lines (default true).</param>
    /// <remarks>
    /// The file is first written in full to a temporary file in the same folder, which then takes its place in one step. A crash
    /// or a full disk part-way through leaves the old file as it was, never half of the new one. If anything fails,
    /// <see cref="SavedUtc"/> is left unchanged.
    /// </remarks>
    public void Save(string path, bool indented = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var target = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(target) ?? ".";
        var temporary = Path.Combine(directory, "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        var savedUtc = DateTimeOffset.UtcNow;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, ToDto(savedUtc), TypeInfo(indented));
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, target, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        SavedUtc = savedUtc;
    }

    /// <summary>Writes the document as the bytes of a marina file (UTF-8), and stamps <see cref="SavedUtc"/>.</summary>
    /// <param name="indented">Lay the JSON out over several lines (default true).</param>
    /// <returns>The file contents, ready to store in a database or send; <see cref="Load(byte[], bool)"/> reads them back.</returns>
    /// <remarks>
    /// Unlike <see cref="ToUtf8Bytes"/>, which leaves the document as it is, this counts as saving it: the bytes carry the
    /// time of this save, and <see cref="SavedUtc"/> is set to it.
    /// </remarks>
    public byte[] Save(bool indented = true)
    {
        var savedUtc = DateTimeOffset.UtcNow;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(ToDto(savedUtc), TypeInfo(indented));
        SavedUtc = savedUtc;
        return bytes;
    }

    /// <summary>Writes the document to a stream as UTF-8 JSON, and stamps <see cref="SavedUtc"/> once it is written.</summary>
    /// <param name="utf8Json">Where to write it. It is flushed but not closed.</param>
    /// <param name="indented">Lay the JSON out over several lines (default true).</param>
    /// <param name="cancellationToken">Stops the write.</param>
    /// <returns>A task that completes when the document is written.</returns>
    public async Task SaveAsync(Stream utf8Json, bool indented = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);
        var savedUtc = DateTimeOffset.UtcNow;
        await JsonSerializer.SerializeAsync(utf8Json, ToDto(savedUtc), TypeInfo(indented), cancellationToken).ConfigureAwait(false);
        await utf8Json.FlushAsync(cancellationToken).ConfigureAwait(false);
        SavedUtc = savedUtc;
    }

    private static JsonTypeInfo<DocumentDto> TypeInfo(bool indented) => indented ? MarinaJson.Indented.DocumentDto : MarinaJson.Compact.DocumentDto;

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A temporary file that will not go is left behind; the original is intact either way.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }

    private DocumentDto ToDto(DateTimeOffset savedUtc)
    {
        var dto = new DocumentDto
        {
            Format = FormatName,
            FormatVersion = CurrentVersion.ToString(2),
            Generator = Generator,
            SavedUtc = savedUtc,
            Marina = new MarinaDto { Name = Name, Description = Description, Extra = Restore("marina") },
            Layout = WriteLayout(),
            Presentation = PresentationDto.From(Style, BerthLabels),
            Camera = Camera is { } pose
                ? new CameraDto { Target = pose.Target, YawDegrees = pose.YawDegrees, PitchDegrees = pose.PitchDegrees, Distance = pose.Distance, Extra = Restore("camera") }
                : null,
            Designer = Designer is { } settings ? DesignerDto.From(settings) : null,
            ReferenceImage = ReferenceImage is { } image ? Attach(ReferenceImageDto.From(image), "referenceImage") : null,
            CameraPresets = CameraPresets.Count == 0
                ? null
                : CameraPresets.Select((preset, index) => Attach(CameraPresetDto.From(preset), PresetKey(index))).ToList(),
            DisabledCameraPresets = DisabledCameraPresets.Count == 0 ? null : DisabledCameraPresets.ToList(),
            Extra = Extensions.Count == 0 ? null : new Dictionary<string, JsonElement>(Extensions, StringComparer.Ordinal),
        };

        dto.Layout.Extra = Restore("layout");
        if (dto.Designer is not null)
        {
            dto.Designer.Extra = Restore("designer");
            if (dto.Designer.BerthNaming is not null) dto.Designer.BerthNaming.Extra = Restore("designer:berthNaming");
        }

        RestorePresentation(dto.Presentation);
        return dto;
    }

    // ---- Extensions ----------------------------------------------------------------------------

    /// <summary>Stores your own data in the file under <paramref name="key"/> (serialized with reflection, in the marina file's JSON style).</summary>
    /// <typeparam name="T">Type of the value.</typeparam>
    /// <param name="key">Property name in the document; use something unlikely to clash, e.g. your company or product name.</param>
    /// <param name="value">The value, or null to remove the key.</param>
    /// <exception cref="ArgumentException">The key is blank, or is a name the format itself uses (see <see cref="Extensions"/>).</exception>
    /// <remarks>
    /// This uses reflection, which trimmed and ahead-of-time compiled applications do not have for every type. There, use the
    /// overload taking a <see cref="JsonTypeInfo{T}"/> from your own source-generated context.
    /// </remarks>
    [RequiresUnreferencedCode("Host data is serialized with reflection; use the overload taking a JsonTypeInfo<T> in trimmed applications.")]
    [RequiresDynamicCode("Host data is serialized with reflection; use the overload taking a JsonTypeInfo<T> in ahead-of-time compiled applications.")]
    public void SetExtension<T>(string key, T? value) => SetExtension(key, value, (JsonTypeInfo<T>)ExtensionOptions().GetTypeInfo(typeof(T)));

    /// <summary>Stores your own data in the file under <paramref name="key"/>, serialized with metadata you supply.</summary>
    /// <typeparam name="T">Type of the value.</typeparam>
    /// <param name="key">Property name in the document; use something unlikely to clash, e.g. your company or product name.</param>
    /// <param name="value">The value, or null to remove the key.</param>
    /// <param name="typeInfo">
    /// How to write <typeparamref name="T"/>, typically <c>MyJsonContext.Default.MyType</c> from a source-generated context. A
    /// context built on <see cref="MarinaJson.CreateOptions"/> writes it in the marina file's own style.
    /// </param>
    /// <exception cref="ArgumentException">The key is blank, or is a name the format itself uses (see <see cref="Extensions"/>).</exception>
    /// <example><code>document.SetExtension("acmeErp", settings, AcmeJsonContext.Default.ErpSettings);</code></example>
    public void SetExtension<T>(string key, T? value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(typeInfo);
        ExtensionDictionary.ThrowIfReserved(key);
        if (value is null)
        {
            Extensions.Remove(key);
            return;
        }

        Extensions[key] = JsonSerializer.SerializeToElement(value, typeInfo);
    }

    /// <summary>Reads back data stored with <see cref="SetExtension{T}(string, T)"/> (or written by another application), or the default when missing or unreadable.</summary>
    /// <typeparam name="T">Type to read it as.</typeparam>
    /// <param name="key">Property name in the document.</param>
    /// <remarks>Uses reflection; in trimmed or ahead-of-time compiled applications use the overload taking a <see cref="JsonTypeInfo{T}"/>.</remarks>
    [RequiresUnreferencedCode("Host data is deserialized with reflection; use the overload taking a JsonTypeInfo<T> in trimmed applications.")]
    [RequiresDynamicCode("Host data is deserialized with reflection; use the overload taking a JsonTypeInfo<T> in ahead-of-time compiled applications.")]
    public T? GetExtension<T>(string key) => GetExtension(key, (JsonTypeInfo<T>)ExtensionOptions().GetTypeInfo(typeof(T)));

    /// <summary>Reads back data stored under <paramref name="key"/> with metadata you supply, or the default when missing or unreadable.</summary>
    /// <typeparam name="T">Type to read it as.</typeparam>
    /// <param name="key">Property name in the document.</param>
    /// <param name="typeInfo">How to read <typeparamref name="T"/>, typically from your own source-generated context.</param>
    public T? GetExtension<T>(string key, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        if (key is null || !Extensions.TryGetValue(key, out var element)) return default;
        try
        {
            return element.Deserialize(typeInfo);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    // ---- Internals -----------------------------------------------------------------------------

    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    private static JsonSerializerOptions? _extensionOptions;

    /// <summary>Reflection-based options for host data of any type (the marina format itself is source-generated), made on first use.</summary>
    [RequiresUnreferencedCode("Reflection-based serialization.")]
    [RequiresDynamicCode("Reflection-based serialization.")]
    private static JsonSerializerOptions ExtensionOptions()
    {
        if (_extensionOptions is { } options) return options;
        options = MarinaJson.CreateOptions(new DefaultJsonTypeInfoResolver());
        options.MakeReadOnly();
        return _extensionOptions = options;
    }

    private static DocumentDto Deserialize(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            return JsonSerializer.Deserialize(utf8Json, MarinaJson.Indented.DocumentDto) ?? throw new MarinaFormatException(Strings.ErrorFileEmpty);
        }
        catch (JsonException ex)
        {
            throw new MarinaFormatException(Strings.Format(Strings.ErrorFileNotJson, ex.Message), ex);
        }
    }

    /// <summary>Brings an older file up to the current format as raw JSON (see <see cref="MarinaMigrations"/>), then reads it.</summary>
    private static DocumentDto Migrate(ReadOnlySpan<byte> utf8Json, Version start, MigrationContext context)
    {
        try
        {
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            if (JsonNode.Parse(utf8Json, documentOptions: options) is not JsonObject root) throw new MarinaFormatException(Strings.ErrorFileNotMarina);
            MarinaMigrations.Run(root, start, context);
            return root.Deserialize(MarinaJson.Indented.DocumentDto) ?? throw new MarinaFormatException(Strings.ErrorFileEmpty);
        }
        catch (JsonException ex)
        {
            throw new MarinaFormatException(Strings.Format(Strings.ErrorFileNotJson, ex.Message), ex);
        }
    }

    private static string PresetKey(int index) => "preset#" + index.ToString(CultureInfo.InvariantCulture);

    /// <summary>The version the file states, or null when it states none or nothing that reads as one.</summary>
    private static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (Version.TryParse(text.Trim(), out var version)) return version;
        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var major) && major >= 0 ? new Version(major, 0) : null;
    }

    /// <summary>Reads the layout (always in the current format by now), remembering what each element carried that this version doesn't know.</summary>
    private static MarinaLayout ReadLayout(LayoutDto? dto, out Dictionary<string, Dictionary<string, JsonElement>> extras)
    {
        extras = new Dictionary<string, Dictionary<string, JsonElement>>(StringComparer.Ordinal);
        if (dto is null) return MarinaLayout.Empty;

        var shoreline = dto.Shoreline?.ToDomain();
        if (dto.Shoreline is not null) Remember(extras, "shoreline", string.Empty, dto.Shoreline.Extra);

        var traffic = dto.MarineTraffic?.ToDomain();
        if (dto.MarineTraffic is not null) Remember(extras, "traffic", string.Empty, dto.MarineTraffic.Extra);

        var landAreas = new List<LandArea>();
        foreach (var land in dto.LandAreas ?? [])
        {
            landAreas.Add(land.ToDomain());
            Remember(extras, "land", land.Id, land.Extra);

            // Trees have no id of their own, so what a newer version stored on one stays with its place in the list.
            var trees = land.Trees ?? [];
            for (var index = 0; index < trees.Count; index++) Remember(extras, "tree", TreeKey(land.Id, index), trees[index].Extra);
        }

        var piers = new List<Pier>();
        foreach (var pier in dto.Piers ?? [])
        {
            piers.Add(pier.ToDomain());
            Remember(extras, "pier", pier.Id, pier.Extra);
        }

        var dividers = new List<Divider>();
        foreach (var divider in dto.Dividers ?? [])
        {
            dividers.Add(divider.ToDomain());
            Remember(extras, "divider", divider.Id, divider.Extra);
        }

        var berths = new List<Berth>();
        foreach (var berth in dto.Berths ?? [])
        {
            // A berth's boat is read (older files carried occupancy) but not written back, so neither is what it carried.
            berths.Add(berth.ToDomain());
            Remember(extras, "berth", berth.Id, berth.Extra);
        }

        var multiBerths = new List<MultiBerth>();
        foreach (var group in dto.MultiBerths ?? [])
        {
            if (group.ToDomain() is not { } domain) continue;
            multiBerths.Add(domain);
            Remember(extras, "group", group.Id, group.Extra);
            Remember(extras, "groupBoat", group.Id, group.Boat?.Extra);
        }

        return new MarinaLayout
        {
            Shoreline = shoreline,
            MarineTraffic = traffic,
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
            // Traffic is only written once it has been asked for, so an untouched marina's file gains nothing.
            MarineTraffic = Layout.MarineTraffic is { IsEnabled: true } passing
                ? Attach(MarineTrafficDto.From(passing), "traffic", string.Empty)
                : null,
            LandAreas = Layout.LandAreas.Select(land =>
            {
                var dto = Attach(LandAreaDto.From(land), "land", land.Id);
                var trees = dto.Trees ?? [];
                for (var index = 0; index < trees.Count; index++) trees[index].Extra = Restore("tree:" + TreeKey(land.Id, index));
                return dto;
            }).ToList(),
            Piers = Layout.Piers.Select(pier => Attach(PierDto.From(pier), "pier", pier.Id)).ToList(),
            Dividers = Layout.Dividers.Select(divider => Attach(DividerDto.From(divider), "divider", divider.Id)).ToList(),
            Berths = Layout.Berths.Select(berth => Attach(BerthDto.From(berth), "berth", berth.Id)).ToList(),
            MultiBerths = Layout.MultiBerths.Count == 0
                ? null
                : Layout.MultiBerths.Select(group =>
                {
                    var dto = Attach(MultiBerthDto.From(group), "group", group.Id);
                    if (dto.Boat is not null) dto.Boat.Extra = Restore("groupBoat:" + group.Id);
                    return dto;
                }).ToList(),
        };

        return layout;
    }

    private static void Remember(Dictionary<string, Dictionary<string, JsonElement>> extras, string kind, string id, Dictionary<string, JsonElement>? extra)
    {
        if (extra is { Count: > 0 }) extras[kind + ":" + id] = extra;
    }

    private static string TreeKey(string landId, int index) => landId + "#" + index.ToString(CultureInfo.InvariantCulture);

    private T Attach<T>(T dto, string kind, string id)
        where T : ExtensibleDto =>
        Attach(dto, kind + ":" + id);

    private T Attach<T>(T dto, string key)
        where T : ExtensibleDto
    {
        dto.Extra = Restore(key);
        return dto;
    }

    private Dictionary<string, JsonElement>? Restore(string key) =>
        _elementExtras.TryGetValue(key, out var extra) && extra.Count > 0 ? extra : null;

    /// <summary>Remembers the properties of the look-and-feel sections that this version doesn't know.</summary>
    private void KeepPresentation(PresentationDto? presentation)
    {
        if (presentation is null) return;

        // Shadows were taken out of the library. A file written before still has their settings, which are dropped here
        // rather than kept as an unknown section and written back with every save.
        if (presentation.Extra is { } extra)
        {
            foreach (var key in extra.Keys.Where(key => string.Equals(key, "shadows", StringComparison.OrdinalIgnoreCase)).ToList()) extra.Remove(key);
        }

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
            // The reader only sets aside names it has no property for, so a reserved one cannot turn up here; skipped all the
            // same, since refusing it would refuse the file.
            foreach (var (key, value) in extra.Where(entry => !ExtensionDictionary.IsReserved(entry.Key))) Extensions[key] = value;
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

    /// <summary>Creates the exception with no message of its own.</summary>
    public MarinaFormatException()
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
