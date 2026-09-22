using System.Collections.ObjectModel;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Serialization;

// The wire shape of a marina file. These types exist only for serialization; the public model is MarinaDocument and the domain
// records. Every field is optional with a sensible default, and every object keeps the properties it doesn't know in Extra, so a
// file written by a newer version survives a round trip through an older one (see Docs/13-marina-file-format.md).

/// <summary>Base of every serialized node: keeps properties this version doesn't know about, including the names older versions used.</summary>
internal abstract class ExtensibleDto
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>A property written under an older name, e.g. "dockId" before piers were called piers.</summary>
    protected bool TryOld(string name, out JsonElement value)
    {
        if (Extra is not null && Extra.TryGetValue(name, out value)) return true;
        value = default;
        return false;
    }

    protected string? OldText(string name) => TryOld(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    protected float? OldNumber(string name) => TryOld(name, out var value) && value.TryGetSingle(out var number) ? number : null;

    protected bool? OldFlag(string name) => TryOld(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    protected T? OldChoice<T>(string name)
        where T : struct, Enum =>
        OldText(name) is { } text && Enum.TryParse<T>(text, ignoreCase: true, out var parsed) ? parsed : null;

    /// <summary>Host metadata on its way out; an empty bag is left out of the file entirely.</summary>
    protected static Dictionary<string, string>? Copy(IReadOnlyDictionary<string, string> metadata) =>
        metadata.Count == 0 ? null : metadata.ToDictionary(entry => entry.Key, entry => entry.Value);

    /// <summary>Host metadata on its way in; a missing one reads as empty rather than null.</summary>
    protected static IReadOnlyDictionary<string, string> Read(Dictionary<string, string>? metadata) =>
        metadata is null or { Count: 0 } ? ReadOnlyDictionary<string, string>.Empty : metadata;
}

internal sealed class DocumentDto : ExtensibleDto
{
    public string? Format { get; set; }

    public string? FormatVersion { get; set; }

    public string? Generator { get; set; }

    public DateTimeOffset? SavedUtc { get; set; }

    public MarinaDto? Marina { get; set; }

    public LayoutDto? Layout { get; set; }

    public PresentationDto? Presentation { get; set; }

    public CameraDto? Camera { get; set; }

    public DesignerDto? Designer { get; set; }

    public ReferenceImageDto? ReferenceImage { get; set; }

    public List<CameraPresetDto>? CameraPresets { get; set; }
}

/// <summary>A viewpoint saved with the design.</summary>
internal sealed class CameraPresetDto : ExtensibleDto
{
    public string Name { get; set; } = "View";

    public string? Description { get; set; }

    public Vector3 Target { get; set; }

    public float YawDegrees { get; set; }

    public float PitchDegrees { get; set; } = 40f;

    public float Distance { get; set; } = 150f;

    public static CameraPresetDto From(CameraPreset preset) => new()
    {
        Name = preset.Name,
        Description = preset.Description,
        Target = preset.Pose.Target,
        YawDegrees = preset.Pose.YawDegrees,
        PitchDegrees = preset.Pose.PitchDegrees,
        Distance = preset.Pose.Distance,
    };

    public CameraPreset ToDomain() =>
        new(Name, new CameraPose(Target, YawDegrees, PitchDegrees, Distance), Description);
}

/// <summary>The traced-over picture: the original file in base64, and where it sits.</summary>
internal sealed class ReferenceImageDto : ExtensibleDto
{
    public string? Data { get; set; }

    public string? ContentType { get; set; }

    public int PixelWidth { get; set; }

    public int PixelHeight { get; set; }

    public Vector2 Center { get; set; }

    public float MetersPerPixel { get; set; } = 1f;

    public float Opacity { get; set; } = 0.6f;

    public bool Visible { get; set; } = true;

    public bool AboveScene { get; set; } = true;

    public static ReferenceImageDto From(ReferenceImageRecord record) => new()
    {
        Data = Convert.ToBase64String(record.Image.EncodedData ?? Array.Empty<byte>()),
        ContentType = record.Image.ContentType,
        PixelWidth = record.Image.PixelWidth,
        PixelHeight = record.Image.PixelHeight,
        Center = record.Center,
        MetersPerPixel = record.MetersPerPixel,
        Opacity = record.Opacity,
        Visible = record.Visible,
        AboveScene = record.AboveScene,
    };

    /// <summary>The stored picture, or null when the entry is unusable (no bytes, or a size that makes no sense).</summary>
    public ReferenceImageRecord? ToDomain()
    {
        if (string.IsNullOrWhiteSpace(Data) || PixelWidth <= 0 || PixelHeight <= 0) return null;

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(Data);
        }
        catch (FormatException)
        {
            return null; // A corrupted picture must not stop the rest of the design from loading.
        }

        if (bytes.Length == 0) return null;
        return new ReferenceImageRecord
        {
            Image = Design.ReferenceImage.FromEncoded(bytes, PixelWidth, PixelHeight, ContentType ?? "image/png"),
            Center = Center,
            MetersPerPixel = MetersPerPixel,
            Opacity = Opacity,
            Visible = Visible,
            AboveScene = AboveScene,
        };
    }
}

internal sealed class MarinaDto : ExtensibleDto
{
    public string? Name { get; set; }

    public string? Description { get; set; }
}

internal sealed class LayoutDto : ExtensibleDto
{
    public List<LandAreaDto>? LandAreas { get; set; }

    public List<PierDto>? Piers { get; set; }

    public List<DividerDto>? Dividers { get; set; }

    public List<BerthDto>? Berths { get; set; }

    public List<MultiBerthDto>? MultiBerths { get; set; }

    /// <summary>The piers. Up to format 1.x they were written as "docks".</summary>
    public List<PierDto> ReadPiers() => Piers ?? Read(MarinaJson.Indented.ListPierDto, "docks") ?? new List<PierDto>();

    /// <summary>
    /// The berths. Up to format 1.x they were "slips", and "berths" meant the multi-berth groups, so an older file's berth list
    /// lives under the old name.
    /// </summary>
    public List<BerthDto> ReadBerths(bool legacy) =>
        (legacy ? Read(MarinaJson.Indented.ListBerthDto, "slips") : Berths) ?? new List<BerthDto>();

    /// <summary>The multi-berth groups; in an older file they are the list called "berths".</summary>
    public List<MultiBerthDto> ReadMultiBerths(bool legacy) =>
        (legacy ? Berths?.Select(MultiBerthDto.FromLegacyBerthEntry).ToList() : MultiBerths) ?? new List<MultiBerthDto>();

    private List<T>? Read<T>(System.Text.Json.Serialization.Metadata.JsonTypeInfo<List<T>> typeInfo, string name)
    {
        if (!TryOld(name, out var value) || value.ValueKind != JsonValueKind.Array) return null;
        try
        {
            return value.Deserialize(typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal sealed class LandAreaDto : ExtensibleDto
{
    public string Id { get; set; } = "land";

    public string? Name { get; set; }

    public LandKind Kind { get; set; }

    public float Height { get; set; } = 1f;

    public List<Vector2>? Outline { get; set; }

    public List<TreeDto>? Trees { get; set; }

    /// <summary>Host-owned string attributes; written only when there are any.</summary>
    public Dictionary<string, string>? Metadata { get; set; }

    public static LandAreaDto From(LandArea land) => new()
    {
        Id = land.Id,
        Name = land.Name,
        Kind = land.Kind,
        Height = land.Height,
        Outline = land.Points.ToList(),
        Trees = land.Trees.Count == 0 ? null : land.Trees.Select(TreeDto.From).ToList(),
        Metadata = Copy(land.Metadata),
    };

    public LandArea ToDomain() => new(Id, Outline ?? new List<Vector2>(), Height, Kind)
    {
        Name = Name,
        Trees = Trees?.Select(t => t.ToDomain()).ToArray() ?? Array.Empty<LandTree>(),
        Metadata = Read(Metadata),
    };
}

internal sealed class TreeDto : ExtensibleDto
{
    public Vector2 Position { get; set; }

    public float Height { get; set; } = 6f;

    public float CrownRadius { get; set; } = 2f;

    public TreeShape Shape { get; set; }

    public static TreeDto From(LandTree tree) => new()
    {
        Position = tree.Position,
        Height = tree.Height,
        CrownRadius = tree.CrownRadius,
        Shape = tree.Shape,
    };

    public LandTree ToDomain() => new(Position, Height, CrownRadius, Shape);
}

internal sealed class PierDto : ExtensibleDto
{
    public string Id { get; set; } = "pier";

    public string? Name { get; set; }

    public Vector2 Start { get; set; }

    public float HeadingDegrees { get; set; }

    public float Length { get; set; } = 10f;

    public float Width { get; set; } = 2.5f;

    public PierType Type { get; set; }

    public float? DeckHeight { get; set; }

    public float PilingSpacing { get; set; } = 6f;

    public PierSides BerthingSides { get; set; } = PierSides.Both;

    public PierServices Services { get; set; }

    /// <summary>Host-owned string attributes; written only when there are any.</summary>
    public Dictionary<string, string>? Metadata { get; set; }

    public static PierDto From(Pier pier) => new()
    {
        Id = pier.Id,
        Name = pier.Name,
        Start = pier.Start,
        HeadingDegrees = pier.HeadingDegrees,
        Length = pier.Length,
        Width = pier.Width,
        Type = pier.Type,
        DeckHeight = pier.DeckHeight,
        PilingSpacing = pier.PilingSpacing,
        BerthingSides = pier.BerthingSides,
        Services = pier.Services,
        Metadata = Copy(pier.Metadata),
    };

    public Pier ToDomain()
    {
        var pier = new Pier(Id, Name ?? Id, Start, HeadingDegrees, Length, Width, Type)
        {
            PilingSpacing = PilingSpacing,
            BerthingSides = BerthingSides == 0 ? PierSides.Both : BerthingSides,
            Services = Services,
            Metadata = Read(Metadata),
        };

        return DeckHeight is { } height ? pier with { DeckHeight = height } : pier;
    }
}

internal sealed class DividerDto : ExtensibleDto
{
    public string Id { get; set; } = "divider";

    public string? PierId { get; set; }

    public Vector2 Start { get; set; }

    public float HeadingDegrees { get; set; }

    public float Length { get; set; } = 8f;

    public float Width { get; set; } = 0.8f;

    public DividerType Type { get; set; }

    public float Spacing { get; set; } = 4f;

    /// <summary>Host-owned string attributes; written only when there are any.</summary>
    public Dictionary<string, string>? Metadata { get; set; }

    public static DividerDto From(Divider divider) => new()
    {
        Id = divider.Id,
        PierId = divider.PierId,
        Start = divider.Start,
        HeadingDegrees = divider.HeadingDegrees,
        Length = divider.Length,
        Width = divider.Width,
        Type = divider.Type,
        Spacing = divider.Spacing,
        Metadata = Copy(divider.Metadata),
    };

    public Divider ToDomain() => new(Id, Start, HeadingDegrees, Length, Type)
    {
        PierId = PierId ?? OldText("dockId"),
        Width = Width <= 0f ? 0.8f : Width,
        Spacing = Spacing < 0.5f ? 4f : Spacing,
        Metadata = Read(Metadata),
    };
}

internal sealed class BerthDto : ExtensibleDto
{
    public string Id { get; set; } = "berth";

    public string? PierId { get; set; }

    public string? LandAreaId { get; set; }

    public string? Label { get; set; }

    public Vector2 Center { get; set; }

    public float HeadingDegrees { get; set; }

    public float Length { get; set; } = 12f;

    public float Width { get; set; } = 5f;

    public float? MaxDraft { get; set; }

    /// <summary>
    /// Occupancy and the interaction flags are runtime state: the host application sets them from its own records
    /// every session, so a design does not carry them. They are still read, for files written before that was so.
    /// </summary>
    public BerthStatus? Status { get; set; }

    public BoatDto? Boat { get; set; }

    public bool HasFingerPiers { get; set; } = true;

    /// <summary>Pedestals at this berth alone; absent means it takes whatever its pier offers.</summary>
    public PierServices? Services { get; set; }

    /// <inheritdoc cref="Status"/>
    public bool? IsVisible { get; set; }

    /// <inheritdoc cref="Status"/>
    public bool? IsDisabled { get; set; }

    /// <inheritdoc cref="Status"/>
    public bool? IsReadOnly { get; set; }

    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>Member ids when this entry is really a multi-berth group from a file up to format 1.x.</summary>
    public List<string>? LegacyMemberIds =>
        TryOld("slipIds", out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(id => id.ValueKind == JsonValueKind.String).Select(id => id.GetString()!).ToList()
            : TryOld("berthIds", out var ids) && ids.ValueKind == JsonValueKind.Array
                ? ids.EnumerateArray().Where(id => id.ValueKind == JsonValueKind.String).Select(id => id.GetString()!).ToList()
                : null;

    /// <summary>Mooring style when this entry is really a multi-berth group from a file up to format 1.x.</summary>
    public MooringStyle? LegacyMooringStyle => OldChoice<MooringStyle>("style");

    public static BerthDto From(Berth berth) => new()
    {
        Id = berth.Id,
        PierId = berth.PierId,
        LandAreaId = berth.LandAreaId,
        Label = berth.Label,
        Center = berth.Center,
        HeadingDegrees = berth.HeadingDegrees,
        Length = berth.Length,
        Width = berth.Width,
        MaxDraft = berth.MaxDraft,
        // Status, the flags and any boat are left out on purpose: see the Status property.
        HasFingerPiers = berth.HasFingerPiers,
        Services = berth.Services,
        Metadata = berth.Metadata.Count == 0 ? null : berth.Metadata.ToDictionary(e => e.Key, e => e.Value),
    };

    public Berth ToDomain()
    {
        PierId ??= OldText("dockId"); // Piers were called docks up to format 1.x.
        var berth = LandAreaId is { Length: > 0 }
            ? Berth.OnLand(Id, LandAreaId, Center, HeadingDegrees, Length, Width)
            : new Berth(Id, PierId is { Length: > 0 } pierId ? pierId : "pier", Center, HeadingDegrees, Length, Width);

        return berth with
        {
            Label = Label,
            MaxDraft = MaxDraft,
            Status = Status ?? BerthStatus.Free,
            Boat = Boat?.ToDomain(),
            HasFingerPiers = LandAreaId is { Length: > 0 } ? false : HasFingerPiers,
            Services = Services,
            IsVisible = IsVisible ?? true,
            IsDisabled = IsDisabled ?? false,
            IsReadOnly = IsReadOnly ?? false,
            Metadata = Metadata is null ? berth.Metadata : Metadata,
        };
    }
}

internal sealed class BoatDto : ExtensibleDto
{
    public string Id { get; set; } = "boat";

    public string? Name { get; set; }

    public BoatType Type { get; set; }

    public float LengthMeters { get; set; }

    public float BeamMeters { get; set; }

    public string? OwnerName { get; set; }

    public string? RegistrationNumber { get; set; }

    public DateTimeOffset? ExpectedArrival { get; set; }

    public Dictionary<string, string>? Metadata { get; set; }

    public static BoatDto From(Boat boat) => new()
    {
        Id = boat.Id,
        Name = boat.Name,
        Type = boat.Type,
        LengthMeters = boat.LengthMeters,
        BeamMeters = boat.BeamMeters,
        OwnerName = boat.OwnerName,
        RegistrationNumber = boat.RegistrationNumber,
        ExpectedArrival = boat.ExpectedArrival,
        Metadata = boat.Metadata.Count == 0 ? null : boat.Metadata.ToDictionary(e => e.Key, e => e.Value),
    };

    public Boat ToDomain()
    {
        var boat = new Boat(Id, Name ?? Id, Type)
        {
            OwnerName = OwnerName,
            RegistrationNumber = RegistrationNumber,
            ExpectedArrival = ExpectedArrival,
        };

        if (LengthMeters > 0f) boat = boat with { LengthMeters = LengthMeters };
        if (BeamMeters > 0f) boat = boat with { BeamMeters = BeamMeters };
        return Metadata is null ? boat : boat with { Metadata = Metadata };
    }
}

internal sealed class MultiBerthDto : ExtensibleDto
{
    public string Id { get; set; } = "berth";

    public List<string>? BerthIds { get; set; }

    public BoatDto? Boat { get; set; }

    public BerthStatus Status { get; set; } = BerthStatus.Occupied;

    public MooringStyle Style { get; set; }

    /// <summary>Host-owned string attributes; written only when there are any.</summary>
    public Dictionary<string, string>? Metadata { get; set; }

    public static MultiBerthDto From(MultiBerth berth) => new()
    {
        Id = berth.Id,
        BerthIds = berth.BerthIds.ToList(),
        Boat = BoatDto.From(berth.Boat),
        Status = berth.Status,
        Style = berth.Style,
        Metadata = Copy(berth.Metadata),
    };

    public MultiBerth? ToDomain()
    {
        var members = BerthIds ?? ReadOldBerthIds();
        if (members is not { Count: > 0 } || Boat is null) return null;
        return new MultiBerth(Id, members, Boat.ToDomain(), Status, Style) { Metadata = Read(Metadata) };
    }

    /// <summary>A group from a file up to format 1.x, where the groups were the list called "berths" and their members "slipIds".</summary>
    public static MultiBerthDto FromLegacyBerthEntry(BerthDto entry) => new()
    {
        Id = entry.Id,
        Boat = entry.Boat,
        Status = entry.Status ?? BerthStatus.Occupied,
        Style = entry.LegacyMooringStyle ?? MooringStyle.Alongside,
        BerthIds = entry.LegacyMemberIds,
        Extra = entry.Extra,
    };

    private List<string>? ReadOldBerthIds()
    {
        if (!TryOld("slipIds", out var value) || value.ValueKind != JsonValueKind.Array) return null;
        return value.EnumerateArray().Where(id => id.ValueKind == JsonValueKind.String).Select(id => id.GetString()!).ToList();
    }
}

internal sealed class CameraDto : ExtensibleDto
{
    public Vector3 Target { get; set; }

    public float YawDegrees { get; set; }

    public float PitchDegrees { get; set; } = 35f;

    public float Distance { get; set; } = 120f;
}

internal sealed class DesignerDto : ExtensibleDto
{
    public LandKind LandKind { get; set; }

    public float LandHeight { get; set; } = 1f;

    public float TreeDensity { get; set; } = 8f;

    public PierType PierType { get; set; }

    public float PierWidth { get; set; } = 2.5f;

    public PierSides PierBerthingSides { get; set; } = PierSides.Both;

    public float BerthWidth { get; set; } = 5f;

    public float BerthLength { get; set; } = 12f;

    public float BerthDepth { get; set; } = 3f;

    public BerthSeparator BerthSeparators { get; set; }

    public float BerthGap { get; set; }

    public bool AlignBerthsToExisting { get; set; } = true;

    public PierServices BerthServices { get; set; }

    public float LandBerthHeading { get; set; }

    public float SnapDistancePixels { get; set; } = 12f;

    public string? PierNamePattern { get; set; }

    public BerthNamingDto? BerthNaming { get; set; }

    public static DesignerDto From(DesignerSettings settings) => new()
    {
        LandKind = settings.LandKind,
        LandHeight = settings.LandHeight,
        TreeDensity = settings.TreeDensity,
        PierType = settings.PierType,
        PierWidth = settings.PierWidth,
        PierBerthingSides = settings.PierBerthingSides,
        BerthWidth = settings.BerthWidth,
        BerthLength = settings.BerthLength,
        BerthDepth = settings.BerthDepth,
        BerthSeparators = settings.BerthSeparators,
        BerthGap = settings.BerthGap,
        AlignBerthsToExisting = settings.AlignBerthsToExisting,
        BerthServices = settings.BerthServices,
        LandBerthHeading = settings.LandBerthHeading,
        SnapDistancePixels = settings.SnapDistancePixels,
        PierNamePattern = settings.PierNamePattern,
        BerthNaming = BerthNamingDto.From(settings.BerthNaming),
    };

    public DesignerSettings ToDomain() => new()
    {
        LandKind = LandKind,
        LandHeight = LandHeight,
        TreeDensity = TreeDensity,
        // The settings written before piers and berths had their names.
        PierType = OldChoice<PierType>("dockType") ?? PierType,
        PierWidth = OldNumber("dockWidth") ?? PierWidth,
        PierBerthingSides = OldChoice<PierSides>("dockBerthingSides") ?? (PierBerthingSides == 0 ? PierSides.Both : PierBerthingSides),
        BerthWidth = OldNumber("slipWidth") ?? BerthWidth,
        BerthLength = OldNumber("slipLength") ?? BerthLength,
        BerthDepth = OldNumber("slipDepth") ?? BerthDepth,
        BerthSeparators = OldChoice<BerthSeparator>("slipSeparators") ?? BerthSeparators,
        BerthGap = OldNumber("slipGap") ?? BerthGap,
        AlignBerthsToExisting = OldFlag("alignSlipsToExisting") ?? AlignBerthsToExisting,
        BerthServices = OldChoice<PierServices>("slipServices") ?? BerthServices,
        LandBerthHeading = OldNumber("landSlipHeading") ?? LandBerthHeading,
        SnapDistancePixels = SnapDistancePixels,
        PierNamePattern = string.IsNullOrWhiteSpace(PierNamePattern) ? "Pier {pier}" : PierNamePattern,
        BerthNaming = BerthNaming?.ToDomain() ?? BerthNamingScheme.Default,
    };
}

/// <summary>How the designer names the berths it draws.</summary>
internal sealed class BerthNamingDto : ExtensibleDto
{
    public string? Pattern { get; set; }

    public string? LandPattern { get; set; }

    public int? LandStartNumber { get; set; }

    public int? LandIncrement { get; set; }

    public int? LandNumberDigits { get; set; }

    public int StartNumber { get; set; } = 1;

    public int Increment { get; set; } = 1;

    public int NumberDigits { get; set; } = 2;

    public string? LeftSide { get; set; }

    public string? RightSide { get; set; }

    public static BerthNamingDto From(BerthNamingScheme scheme) => new()
    {
        Pattern = scheme.Pattern,
        LandPattern = scheme.LandPattern,
        LandStartNumber = scheme.LandStartNumber,
        LandIncrement = scheme.LandIncrement,
        LandNumberDigits = scheme.LandNumberDigits,
        StartNumber = scheme.StartNumber,
        Increment = scheme.Increment,
        NumberDigits = scheme.NumberDigits,
        LeftSide = scheme.LeftSide,
        RightSide = scheme.RightSide,
    };

    public BerthNamingScheme ToDomain()
    {
        var scheme = new BerthNamingScheme
        {
            Pattern = string.IsNullOrWhiteSpace(Pattern) ? BerthNamingScheme.Default.Pattern : Pattern,
            LandPattern = string.IsNullOrWhiteSpace(LandPattern) ? null : LandPattern,
            LandStartNumber = LandStartNumber,
            LandIncrement = LandIncrement == 0 ? null : LandIncrement,
            LandNumberDigits = LandNumberDigits is null ? null : Math.Clamp(LandNumberDigits.Value, 1, 9),
            StartNumber = StartNumber,
            Increment = Increment == 0 ? 1 : Increment,
            NumberDigits = Math.Clamp(NumberDigits, 1, 9),
            LeftSide = LeftSide ?? BerthNamingScheme.Default.LeftSide,
            RightSide = RightSide ?? BerthNamingScheme.Default.RightSide,
        };

        return scheme.Validate().Any() ? BerthNamingScheme.Default : scheme;
    }
}

/// <summary>Everything about how the marina looks and moves: the <see cref="MarinaStyle"/> sections plus the label mode.</summary>
internal sealed class PresentationDto : ExtensibleDto
{
    public WaterDto? Water { get; set; }

    public LightingDto? Lighting { get; set; }

    public StatusDto? Status { get; set; }

    public LandStyleDto? Land { get; set; }

    public StructureDto? Structures { get; set; }

    public LabelDto? Labels { get; set; }

    public SelectionDto? Selection { get; set; }

    public ViewDto? View { get; set; }

    public BerthLabelMode BerthLabels { get; set; }

    /// <summary>Berth labels were called slip labels up to format 1.x.</summary>
    public BerthLabelMode ReadBerthLabels() => BerthLabels != BerthLabelMode.None ? BerthLabels : OldChoice<BerthLabelMode>("slipLabels") ?? BerthLabelMode.None;

    public static PresentationDto From(MarinaStyle style, BerthLabelMode labels) => new()
    {
        Water = WaterDto.From(style.Water),
        Lighting = LightingDto.From(style.Lighting),
        Status = StatusDto.From(style.Status),
        Land = LandStyleDto.From(style.Land),
        Structures = StructureDto.From(style.Piers),
        Labels = LabelDto.From(style.Labels),
        Selection = SelectionDto.From(style.Selection),
        View = ViewDto.From(style.View),
        BerthLabels = labels,
    };

    public MarinaStyle ToStyle()
    {
        var style = new MarinaStyle
        {
            Water = Water?.ToDomain() ?? new WaterSettings(),
            Lighting = Lighting?.ToDomain() ?? new LightingSettings(),
            View = View?.ToDomain() ?? new ViewStyle(),
        };

        Status?.ApplyTo(style.Status);
        Land?.ApplyTo(style.Land);
        Structures?.ApplyTo(style.Piers);
        Labels?.ApplyTo(style.Labels);
        Selection?.ApplyTo(style.Selection);
        return style;
    }
}

internal sealed class WaterDto : ExtensibleDto
{
    public float Size { get; set; } = 1400f;

    public int GridResolution { get; set; } = 160;

    public Vector3 DeepColor { get; set; }

    public Vector3 ShallowColor { get; set; }

    public float WaveAmplitude { get; set; } = 0.08f;

    public float WaveFrequency { get; set; } = 1f;

    public float WaveSpeed { get; set; } = 1f;

    public float SkyReflection { get; set; } = 1f;

    public float Ripples { get; set; } = 1f;

    public float SunGlints { get; set; } = 1f;

    public float Whitecaps { get; set; } = 0.55f;

    public float WhitecapDistance { get; set; } = 220f;

    public float BoatMotion { get; set; } = 1f;

    public static WaterDto From(WaterSettings water) => new()
    {
        Size = water.Size,
        GridResolution = water.GridResolution,
        DeepColor = water.DeepColor,
        ShallowColor = water.ShallowColor,
        WaveAmplitude = water.WaveAmplitude,
        WaveFrequency = water.WaveFrequency,
        WaveSpeed = water.WaveSpeed,
        SkyReflection = water.SkyReflection,
        Ripples = water.Ripples,
        SunGlints = water.SunGlints,
        Whitecaps = water.Whitecaps,
        WhitecapDistance = water.WhitecapDistance,
        BoatMotion = water.BoatMotion,
    };

    public WaterSettings ToDomain()
    {
        var defaults = new WaterSettings();
        return new WaterSettings
        {
            Size = Size > 0f ? Size : defaults.Size,
            GridResolution = GridResolution > 1 ? GridResolution : defaults.GridResolution,
            DeepColor = DeepColor == Vector3.Zero ? defaults.DeepColor : DeepColor,
            ShallowColor = ShallowColor == Vector3.Zero ? defaults.ShallowColor : ShallowColor,
            WaveAmplitude = WaveAmplitude,
            WaveFrequency = WaveFrequency,
            WaveSpeed = WaveSpeed,
            SkyReflection = SkyReflection,
            Ripples = Ripples,
            SunGlints = SunGlints,
            Whitecaps = Whitecaps,
            WhitecapDistance = WhitecapDistance > 0f ? WhitecapDistance : defaults.WhitecapDistance,
            BoatMotion = BoatMotion,
        };
    }
}

internal sealed class LightingDto : ExtensibleDto
{
    public Vector3 SunDirection { get; set; }

    public Vector3 SunColor { get; set; }

    public Vector3 AmbientColor { get; set; }

    public float SpecularStrength { get; set; } = 0.35f;

    public float Shininess { get; set; } = 32f;

    public Vector3 SkyColor { get; set; }

    public Vector3 FogColor { get; set; }

    public float FogDensity { get; set; } = 0.0022f;

    public static LightingDto From(LightingSettings lighting) => new()
    {
        SunDirection = lighting.SunDirection,
        SunColor = lighting.SunColor,
        AmbientColor = lighting.AmbientColor,
        SpecularStrength = lighting.SpecularStrength,
        Shininess = lighting.Shininess,
        SkyColor = lighting.SkyColor,
        FogColor = lighting.FogColor,
        FogDensity = lighting.FogDensity,
    };

    public LightingSettings ToDomain()
    {
        var lighting = new LightingSettings
        {
            SpecularStrength = SpecularStrength,
            Shininess = Shininess,
            FogDensity = FogDensity,
        };

        if (SunDirection != Vector3.Zero) lighting.SunDirection = SunDirection;
        if (SunColor != Vector3.Zero) lighting.SunColor = SunColor;
        if (AmbientColor != Vector3.Zero) lighting.AmbientColor = AmbientColor;
        if (SkyColor != Vector3.Zero) lighting.SkyColor = SkyColor;
        if (FogColor != Vector3.Zero) lighting.FogColor = FogColor;
        return lighting;
    }
}

internal sealed class StatusDto : ExtensibleDto
{
    public ColorRgba Free { get; set; } = StatusColorScheme.DefaultFree;

    public ColorRgba Occupied { get; set; } = StatusColorScheme.DefaultOccupied;

    public ColorRgba Reserved { get; set; } = StatusColorScheme.DefaultReserved;

    public ColorRgba TemporarilyFree { get; set; } = StatusColorScheme.DefaultTemporarilyFree;

    public ColorRgba Disabled { get; set; } = StatusColorScheme.DefaultDisabled;

    public float PadOpacity { get; set; } = 0.45f;

    public float OccupiedBoatOpacity { get; set; } = 1f;

    public float ReservedBoatOpacity { get; set; } = 0.4f;

    public float TemporarilyFreeBoatOpacity { get; set; } = 0.4f;

    public float GhostBoatTint { get; set; } = 0.55f;

    public bool ShowStatusMarkers { get; set; } = true;

    public float StatusMarkerScale { get; set; } = 1f;

    public static StatusDto From(StatusColorScheme status) => new()
    {
        Free = status.FreeColor,
        Occupied = status.OccupiedColor,
        Reserved = status.ReservedColor,
        TemporarilyFree = status.TemporarilyFreeColor,
        Disabled = status.DisabledColor,
        PadOpacity = status.PadOpacity,
        OccupiedBoatOpacity = status.OccupiedBoatOpacity,
        ReservedBoatOpacity = status.ReservedBoatOpacity,
        TemporarilyFreeBoatOpacity = status.TemporarilyFreeBoatOpacity,
        GhostBoatTint = status.GhostBoatTint,
        ShowStatusMarkers = status.ShowStatusMarkers,
        StatusMarkerScale = status.StatusMarkerScale,
    };

    public void ApplyTo(StatusColorScheme status)
    {
        status.FreeColor = Free;
        status.OccupiedColor = Occupied;
        status.ReservedColor = Reserved;
        status.TemporarilyFreeColor = TemporarilyFree;
        status.DisabledColor = Disabled;
        status.PadOpacity = PadOpacity;
        status.OccupiedBoatOpacity = OccupiedBoatOpacity;
        status.ReservedBoatOpacity = ReservedBoatOpacity;
        status.TemporarilyFreeBoatOpacity = TemporarilyFreeBoatOpacity;
        status.GhostBoatTint = GhostBoatTint;
        status.ShowStatusMarkers = ShowStatusMarkers;
        status.StatusMarkerScale = StatusMarkerScale;
    }
}

internal sealed class LandStyleDto : ExtensibleDto
{
    public ColorRgba Quay { get; set; } = new(0.74f, 0.72f, 0.67f);

    public ColorRgba QuayWall { get; set; } = new(0.62f, 0.60f, 0.56f);

    public ColorRgba Grass { get; set; } = new(0.40f, 0.58f, 0.30f);

    public ColorRgba GrassBank { get; set; } = new(0.47f, 0.40f, 0.30f);

    public ColorRgba Rock { get; set; } = new(0.53f, 0.51f, 0.48f);

    public float RockColorVariation { get; set; } = 0.2f;

    public ColorRgba Foliage { get; set; } = new(0.24f, 0.46f, 0.20f);

    public ColorRgba Conifer { get; set; } = new(0.16f, 0.36f, 0.22f);

    public ColorRgba Trunk { get; set; } = new(0.38f, 0.27f, 0.17f);

    public ColorRgba Palm { get; set; } = new(0.33f, 0.52f, 0.26f);

    public ColorRgba Blossom { get; set; } = new(0.95f, 0.72f, 0.80f);

    public bool ShowTrees { get; set; } = true;

    public static LandStyleDto From(LandStyle land) => new()
    {
        Quay = land.QuayColor,
        QuayWall = land.QuayWallColor,
        Grass = land.GrassColor,
        GrassBank = land.GrassBankColor,
        Rock = land.RockColor,
        RockColorVariation = land.RockColorVariation,
        Foliage = land.FoliageColor,
        Conifer = land.ConiferColor,
        Trunk = land.TrunkColor,
        Palm = land.PalmColor,
        Blossom = land.BlossomColor,
        ShowTrees = land.ShowTrees,
    };

    public void ApplyTo(LandStyle land)
    {
        land.QuayColor = Quay;
        land.QuayWallColor = QuayWall;
        land.GrassColor = Grass;
        land.GrassBankColor = GrassBank;
        land.RockColor = Rock;
        land.RockColorVariation = RockColorVariation;
        land.FoliageColor = Foliage;
        land.ConiferColor = Conifer;
        land.TrunkColor = Trunk;
        land.PalmColor = Palm;
        land.BlossomColor = Blossom;
        land.ShowTrees = ShowTrees;
    }
}

internal sealed class StructureDto : ExtensibleDto
{
    public ColorRgba Wood { get; set; } = new(0.66f, 0.50f, 0.33f);

    public ColorRgba Concrete { get; set; } = new(0.74f, 0.73f, 0.70f);

    public ColorRgba Float { get; set; } = new(0.20f, 0.21f, 0.23f);

    public ColorRgba Fender { get; set; } = new(0.12f, 0.12f, 0.13f);

    public ColorRgba Bollard { get; set; } = new(0.17f, 0.18f, 0.20f);

    public ColorRgba Steel { get; set; } = new(0.56f, 0.58f, 0.61f);

    public ColorRgba BoomFloat { get; set; } = new(0.96f, 0.56f, 0.12f);

    public ColorRgba BoomEnd { get; set; } = new(0.98f, 0.84f, 0.15f);

    public ColorRgba Pedestal { get; set; } = new(0.82f, 0.83f, 0.85f);

    public ColorRgba Power { get; set; } = new(0.95f, 0.76f, 0.11f);

    public ColorRgba Water { get; set; } = new(0.16f, 0.52f, 0.85f);

    public static StructureDto From(StructureStyle piers) => new()
    {
        Wood = piers.WoodColor,
        Concrete = piers.ConcreteColor,
        Float = piers.FloatColor,
        Fender = piers.FenderColor,
        Bollard = piers.BollardColor,
        Steel = piers.SteelColor,
        BoomFloat = piers.BoomFloatColor,
        BoomEnd = piers.BoomEndColor,
        Pedestal = piers.PedestalColor,
        Power = piers.PowerColor,
        Water = piers.WaterColor,
    };

    public void ApplyTo(StructureStyle piers)
    {
        piers.WoodColor = Wood;
        piers.ConcreteColor = Concrete;
        piers.FloatColor = Float;
        piers.FenderColor = Fender;
        piers.BollardColor = Bollard;
        piers.SteelColor = Steel;
        piers.BoomFloatColor = BoomFloat;
        piers.BoomEndColor = BoomEnd;
        piers.PedestalColor = Pedestal;
        piers.PowerColor = Power;
        piers.WaterColor = Water;
    }
}

internal sealed class LabelDto : ExtensibleDto
{
    public ColorRgba Color { get; set; } = new(0.97f, 0.98f, 1f);

    public ColorRgba Highlight { get; set; } = new(1f, 0.90f, 0.35f);

    public ColorRgba Disabled { get; set; } = new(0.62f, 0.64f, 0.66f);

    public LabelFont FontFamily { get; set; }

    public static LabelDto From(LabelStyle labels) => new()
    {
        Color = labels.Color,
        Highlight = labels.HighlightColor,
        Disabled = labels.DisabledColor,
        FontFamily = labels.FontFamily,
    };

    public void ApplyTo(LabelStyle labels)
    {
        labels.Color = Color;
        labels.HighlightColor = Highlight;
        labels.DisabledColor = Disabled;
        labels.FontFamily = FontFamily;
    }
}

internal sealed class SelectionDto : ExtensibleDto
{
    public bool ShowMarker { get; set; } = true;

    public ColorRgba MarkerTint { get; set; } = new(1f, 1f, 1f);

    public float MarkerScale { get; set; } = 1f;

    public float SelectedGlow { get; set; } = 0.45f;

    public float HoverGlow { get; set; } = 0.25f;

    public bool Pulse { get; set; } = true;

    public static SelectionDto From(SelectionStyle selection) => new()
    {
        ShowMarker = selection.ShowMarker,
        MarkerTint = selection.MarkerTint,
        MarkerScale = selection.MarkerScale,
        SelectedGlow = selection.SelectedGlow,
        HoverGlow = selection.HoverGlow,
        Pulse = selection.Pulse,
    };

    public void ApplyTo(SelectionStyle selection)
    {
        selection.ShowMarker = ShowMarker;
        selection.MarkerTint = MarkerTint;
        selection.MarkerScale = MarkerScale;
        selection.SelectedGlow = SelectedGlow;
        selection.HoverGlow = HoverGlow;
        selection.Pulse = Pulse;
    }
}

internal sealed class ViewDto : ExtensibleDto
{
    public float FieldOfViewDegrees { get; set; } = 45f;

    public float CameraSmoothing { get; set; } = 10f;

    public static ViewDto From(ViewStyle view) => new()
    {
        FieldOfViewDegrees = view.FieldOfViewDegrees,
        CameraSmoothing = view.CameraSmoothing,
    };

    public ViewStyle ToDomain() => new()
    {
        FieldOfViewDegrees = FieldOfViewDegrees,
        CameraSmoothing = CameraSmoothing,
    };
}
