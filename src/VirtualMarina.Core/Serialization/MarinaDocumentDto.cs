using System.Collections.ObjectModel;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Serialization;

// The wire shape of a marina file, current version only: files written by older versions are brought up to date as raw JSON by
// the steps in MarinaMigrations before they get here. These types exist only for serialization; the public model is
// MarinaDocument and the domain records. Every field is optional, and every object keeps the properties it doesn't know in
// Extra, so a file written by a newer version survives a round trip through an older one (see Docs/13-marina-file-format.md).
//
// Defaults: a setting the domain has a default for is nullable here, and a missing one takes the domain's default when read
// (`value ?? defaults.Value`), so no default is written down twice. Everything is written out in full on save.

/// <summary>Base of every serialized node: keeps the properties this version doesn't know about.</summary>
/// <remarks>
/// A file may hold <c>null</c> where this version expects something: a null entry in a list is dropped, and a null or blank id
/// reads as if the id were missing (the node's default id), so reading a file only ever fails with a
/// <see cref="MarinaFormatException"/>, never with an exception from deep inside. Each node tidies itself up in
/// <see cref="Normalize"/> as soon as it has been read.
/// </remarks>
internal abstract class ExtensibleDto : IJsonOnDeserialized
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    void IJsonOnDeserialized.OnDeserialized() => Normalize();

    /// <summary>Replaces what the file left null with what a missing value means.</summary>
    protected virtual void Normalize()
    {
    }

    /// <summary><paramref name="id"/>, or <paramref name="fallback"/> when it is null or blank.</summary>
    protected static string IdOr(string? id, string fallback) => string.IsNullOrWhiteSpace(id) ? fallback : id;

    /// <summary>Drops null entries from a list read from the file.</summary>
    protected static void DropNulls<T>(List<T>? list)
        where T : class =>
        list?.RemoveAll(item => item is null);

    /// <summary>Host metadata on its way out; an empty bag is left out of the file entirely.</summary>
    protected static Dictionary<string, string>? Copy(IReadOnlyDictionary<string, string> metadata) =>
        metadata.Count == 0 ? null : metadata.ToDictionary(entry => entry.Key, entry => entry.Value);

    /// <summary>Host metadata on its way in; a missing one reads as empty rather than null.</summary>
    protected static IReadOnlyDictionary<string, string> Read(Dictionary<string, string>? metadata) =>
        metadata is null or { Count: 0 } ? ReadOnlyDictionary<string, string>.Empty : metadata;

    /// <summary>
    /// A drawn line on its way in, without points repeated next to each other (within a centimetre, as the designer
    /// strips them while drawing). Validation now refuses such points; files written before it did can still hold
    /// them, and they would otherwise stop the whole design from opening. With <paramref name="closed"/>, a last point
    /// repeating the first goes too.
    /// </summary>
    protected static List<Vector2> WithoutRepeatedPoints(List<Vector2> points, bool closed) =>
        [.. PolygonMath.RemoveRepeatedPoints(points, closed)];
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

    /// <summary>Names of the built-in views that are switched off; written only when there are any.</summary>
    public List<string>? DisabledCameraPresets { get; set; }

    protected override void Normalize()
    {
        DropNulls(CameraPresets);
        DisabledCameraPresets?.RemoveAll(string.IsNullOrWhiteSpace);
    }
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

    protected override void Normalize() => Name = IdOr(Name, "View");
}

/// <summary>
/// The traced-over picture: the original file, and where it sits. <see cref="Data"/> is base64 in the file, decoded straight from
/// the UTF-8 bytes into the array (see <see cref="TolerantBase64Converter"/>), so a large photo is never held as a string.
/// </summary>
internal sealed class ReferenceImageDto : ExtensibleDto
{
    public byte[]? Data { get; set; }

    public string? ContentType { get; set; }

    public int PixelWidth { get; set; }

    public int PixelHeight { get; set; }

    public Vector2 Center { get; set; }

    public float? MetersPerPixel { get; set; }

    public float? Opacity { get; set; }

    public bool? Visible { get; set; }

    public bool? AboveScene { get; set; }

    public static ReferenceImageDto From(ReferenceImageRecord record) => new()
    {
        Data = record.Image.EncodedData ?? Array.Empty<byte>(),
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
        // A missing or corrupted picture (the converter reads bad base64 as none) must not stop the rest of the design loading.
        if (Data is not { Length: > 0 } bytes || PixelWidth <= 0 || PixelHeight <= 0) return null;

        var image = Design.ReferenceImage.FromEncoded(bytes, PixelWidth, PixelHeight, ContentType ?? "image/png");
        var defaults = new ReferenceImageRecord { Image = image };
        return defaults with
        {
            Center = Center,
            MetersPerPixel = MetersPerPixel ?? defaults.MetersPerPixel,
            Opacity = Opacity ?? defaults.Opacity,
            Visible = Visible ?? defaults.Visible,
            AboveScene = AboveScene ?? defaults.AboveScene,
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
    public ShorelineDto? Shoreline { get; set; }

    public MarineTrafficDto? MarineTraffic { get; set; }

    public List<LandAreaDto>? LandAreas { get; set; }

    public List<PierDto>? Piers { get; set; }

    public List<DividerDto>? Dividers { get; set; }

    public List<BerthDto>? Berths { get; set; }

    public List<MultiBerthDto>? MultiBerths { get; set; }

    protected override void Normalize()
    {
        DropNulls(LandAreas);
        DropNulls(Piers);
        DropNulls(Dividers);
        DropNulls(Berths);
        DropNulls(MultiBerths);
    }
}

/// <summary>
/// Passing traffic out at sea. Only the settings are written: the lanes and the vessels on them are worked out from
/// the seed and the shoreline when the file is loaded, so a busy sea costs no more to store than an empty one.
/// </summary>
internal sealed class MarineTrafficDto : ExtensibleDto
{
    public bool? Enabled { get; set; }

    public float? Clearance { get; set; }

    public float? EdgeClearance { get; set; }

    public float? SpeedPercent { get; set; }

    public float? SpawnDelaySeconds { get; set; }

    public float? Reach { get; set; }

    public int? MaximumVessels { get; set; }

    public int? LaneCount { get; set; }

    public float? LaneSpacing { get; set; }

    public int? Seed { get; set; }

    /// <summary>The vessel mix; written only when it is not the default one.</summary>
    public List<BoatType>? Vessels { get; set; }

    /// <summary>Host-owned string attributes; written only when there are any.</summary>
    public Dictionary<string, string>? Metadata { get; set; }

    public static MarineTrafficDto From(MarineTraffic traffic) => new()
    {
        Enabled = traffic.IsEnabled,
        Clearance = traffic.Clearance,
        EdgeClearance = traffic.EdgeClearance,
        SpeedPercent = traffic.SpeedPercent,
        SpawnDelaySeconds = traffic.SpawnDelaySeconds,
        Reach = traffic.Reach,
        MaximumVessels = traffic.MaximumVessels,
        LaneCount = traffic.LaneCount,
        LaneSpacing = traffic.LaneSpacing,
        Seed = traffic.Seed,
        Vessels = traffic.Vessels.Count == 0 ? null : traffic.Vessels.ToList(),
        Metadata = Copy(traffic.Metadata),
    };

    public MarineTraffic ToDomain()
    {
        var defaults = MarineTraffic.None;
        return new MarineTraffic
        {
            IsEnabled = Enabled ?? defaults.IsEnabled,
            Clearance = Clearance ?? defaults.Clearance,
            EdgeClearance = EdgeClearance is > 0f and var edge ? edge : defaults.EdgeClearance,
            SpeedPercent = SpeedPercent is > 0f and var speed ? speed : defaults.SpeedPercent,
            SpawnDelaySeconds = SpawnDelaySeconds is >= 0f and var delay ? delay : defaults.SpawnDelaySeconds,
            Reach = Reach ?? defaults.Reach,
            MaximumVessels = MaximumVessels is > 0 and var most ? most : defaults.MaximumVessels,

            // A file written before the traffic had lanes has neither, and zero would fail validation and empty the sea.
            LaneCount = LaneCount is > 0 and var lanes ? lanes : defaults.LaneCount,
            LaneSpacing = LaneSpacing is > 0f and var spacing ? spacing : defaults.LaneSpacing,
            Seed = Seed ?? defaults.Seed,
            Vessels = (IReadOnlyList<BoatType>?)Vessels ?? Array.Empty<BoatType>(),
            Metadata = Read(Metadata),
        };
    }
}

/// <summary>
/// The mainland behind the marina. Only the drawn line and which side of it is land are written: the shape covering
/// that side is worked out on load, so the far edge never ends up in the file.
/// </summary>
internal sealed class ShorelineDto : ExtensibleDto
{
    public List<Vector2>? Line { get; set; }

    public bool LandOnLeft { get; set; }

    public float? Height { get; set; }

    public LandKind? Kind { get; set; }

    public HinterlandScenery? Scenery { get; set; }

    public int? ScenerySeed { get; set; }

    /// <summary>Host-owned string attributes; written only when there are any.</summary>
    public Dictionary<string, string>? Metadata { get; set; }

    public static ShorelineDto From(Shoreline shoreline) => new()
    {
        Line = shoreline.Points.ToList(),
        LandOnLeft = shoreline.LandOnLeft,
        Height = shoreline.Height,
        Kind = shoreline.Kind,
        Scenery = shoreline.Scenery,
        ScenerySeed = shoreline.ScenerySeed,
        Metadata = Copy(shoreline.Metadata),
    };

    /// <summary>
    /// The shoreline, or null when the file's line is too short to divide the plan. A line that is long enough but crosses itself
    /// is read as it is, so nothing is lost; <see cref="MarinaLayout.Validate"/> then reports it, as it does a land area whose
    /// outline crosses itself.
    /// </summary>
    public Shoreline? ToDomain()
    {
        var line = Line is null ? [] : WithoutRepeatedPoints(Line, closed: false);
        if (line.Count < 2) return null;
        var shoreline = new Shoreline(line, LandOnLeft);
        return shoreline with
        {
            Height = Height ?? shoreline.Height,
            Kind = Kind ?? shoreline.Kind,
            Scenery = Scenery ?? shoreline.Scenery,
            ScenerySeed = ScenerySeed ?? shoreline.ScenerySeed,
            Metadata = Read(Metadata),
        };
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

    protected override void Normalize()
    {
        Id = IdOr(Id, "land");
        DropNulls(Trees);
    }

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

    public LandArea ToDomain() => new(Id, Outline is null ? [] : WithoutRepeatedPoints(Outline, closed: true), Height, Kind)
    {
        Name = Name,
        Trees = Trees?.Select(tree => tree.ToDomain()).ToArray() ?? Array.Empty<LandTree>(),
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

    /// <summary>Written only when the pier has a height of its own; missing means the default for its type.</summary>
    public float? DeckHeight { get; set; }

    public float? PilingSpacing { get; set; }

    public PierSides? BerthingSides { get; set; }

    public PierServices? Services { get; set; }

    /// <summary>Host-owned string attributes; written only when there are any.</summary>
    public Dictionary<string, string>? Metadata { get; set; }

    protected override void Normalize() => Id = IdOr(Id, "pier");

    public static PierDto From(Pier pier) => new()
    {
        Id = pier.Id,
        Name = pier.Name,
        Start = pier.Start,
        HeadingDegrees = pier.HeadingDegrees,
        Length = pier.Length,
        Width = pier.Width,
        Type = pier.Type,
        DeckHeight = pier.HasCustomDeckHeight ? pier.DeckHeight : null,
        PilingSpacing = pier.PilingSpacing,
        BerthingSides = pier.BerthingSides,
        Services = pier.Services,
        Metadata = Copy(pier.Metadata),
    };

    public Pier ToDomain()
    {
        var pier = new Pier(Id, Name ?? Id, Start, HeadingDegrees, Length, Width, Type);
        pier = pier with
        {
            PilingSpacing = PilingSpacing ?? pier.PilingSpacing,
            BerthingSides = BerthingSides is { } sides && sides != 0 ? sides : pier.BerthingSides,
            Services = Services ?? pier.Services,
            Metadata = Read(Metadata),
        };

        // Files used to write the height whatever it was; one that is just the type's default is read as the default, so
        // changing the pier's type later still changes its height.
        return DeckHeight is { } height && height != Pier.GetDefaultDeckHeight(pier.Type) ? pier with { DeckHeight = height } : pier;
    }
}

internal sealed class DividerDto : ExtensibleDto
{
    public string Id { get; set; } = "divider";

    public string? PierId { get; set; }

    public Vector2 Start { get; set; }

    public float HeadingDegrees { get; set; }

    public float Length { get; set; } = 8f;

    public float? Width { get; set; }

    public DividerType Type { get; set; }

    public float? Spacing { get; set; }

    /// <summary>Host-owned string attributes; written only when there are any.</summary>
    public Dictionary<string, string>? Metadata { get; set; }

    protected override void Normalize() => Id = IdOr(Id, "divider");

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

    public Divider ToDomain()
    {
        var divider = new Divider(Id, Start, HeadingDegrees, Length, Type);
        return divider with
        {
            PierId = PierId,
            Width = Width is > 0f and var width ? width : divider.Width,
            Spacing = Spacing is >= 0.5f and var spacing ? spacing : divider.Spacing,
            Metadata = Read(Metadata),
        };
    }
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
    /// every session, so a design does not carry them. They are still read, for files written before that was so. (A
    /// <see cref="MultiBerthDto"/> does carry its boat and status: a multi-berth cannot exist without them.)
    /// </summary>
    public BerthStatus? Status { get; set; }

    /// <inheritdoc cref="Status"/>
    public BoatDto? Boat { get; set; }

    public bool? HasFingerPiers { get; set; }

    /// <summary>Pedestals at this berth alone; absent means it takes whatever its pier offers.</summary>
    public PierServices? Services { get; set; }

    /// <inheritdoc cref="Status"/>
    public bool? IsVisible { get; set; }

    /// <inheritdoc cref="Status"/>
    public bool? IsDisabled { get; set; }

    /// <inheritdoc cref="Status"/>
    public bool? IsReadOnly { get; set; }

    public Dictionary<string, string>? Metadata { get; set; }

    protected override void Normalize() => Id = IdOr(Id, "berth");

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
        Metadata = Copy(berth.Metadata),
    };

    public Berth ToDomain()
    {
        var onLand = !string.IsNullOrWhiteSpace(LandAreaId);
        var berth = onLand
            ? Berth.OnLand(Id, LandAreaId!, Center, HeadingDegrees, Length, Width)
            : new Berth(Id, IdOr(PierId, "pier"), Center, HeadingDegrees, Length, Width);

        return berth with
        {
            Label = Label,
            MaxDraft = MaxDraft,
            Status = Status ?? BerthStatus.Free,
            Boat = Boat?.ToDomain(),
            HasFingerPiers = !onLand && (HasFingerPiers ?? berth.HasFingerPiers),
            Services = Services,
            IsVisible = IsVisible ?? true,
            IsDisabled = IsDisabled ?? false,
            IsReadOnly = IsReadOnly ?? false,
            Metadata = Read(Metadata),
        };
    }
}

internal sealed class BoatDto : ExtensibleDto
{
    public string Id { get; set; } = "boat";

    public string? Name { get; set; }

    public BoatType Type { get; set; }

    /// <summary>Written only when the boat has a length of its own; missing means the nominal length of its type.</summary>
    public float? LengthMeters { get; set; }

    /// <summary>Written only when the boat has a beam of its own; missing means the nominal beam of its type.</summary>
    public float? BeamMeters { get; set; }

    public string? OwnerName { get; set; }

    public string? RegistrationNumber { get; set; }

    public DateTimeOffset? ExpectedArrival { get; set; }

    public Dictionary<string, string>? Metadata { get; set; }

    protected override void Normalize() => Id = IdOr(Id, "boat");

    public static BoatDto From(Boat boat) => new()
    {
        Id = boat.Id,
        Name = boat.Name,
        Type = boat.Type,
        LengthMeters = boat.HasCustomLength ? boat.LengthMeters : null,
        BeamMeters = boat.HasCustomBeam ? boat.BeamMeters : null,
        OwnerName = boat.OwnerName,
        RegistrationNumber = boat.RegistrationNumber,
        ExpectedArrival = boat.ExpectedArrival,
        Metadata = Copy(boat.Metadata),
    };

    public Boat ToDomain()
    {
        var boat = new Boat(Id, Name ?? Id, Type)
        {
            OwnerName = OwnerName,
            RegistrationNumber = RegistrationNumber,
            ExpectedArrival = ExpectedArrival,
        };

        // Zero was how a file said "the type's size" before it could leave the number out.
        if (LengthMeters is > 0f and var length) boat = boat with { LengthMeters = length };
        if (BeamMeters is > 0f and var beam) boat = boat with { BeamMeters = beam };
        return Metadata is null ? boat : boat with { Metadata = Read(Metadata) };
    }
}

internal sealed class MultiBerthDto : ExtensibleDto
{
    public string Id { get; set; } = "berth";

    public List<string>? BerthIds { get; set; }

    public BoatDto? Boat { get; set; }

    public BerthStatus? Status { get; set; }

    public MooringStyle? Style { get; set; }

    /// <summary>Host-owned string attributes; written only when there are any.</summary>
    public Dictionary<string, string>? Metadata { get; set; }

    protected override void Normalize() => Id = IdOr(Id, "berth");

    public static MultiBerthDto From(MultiBerth berth) => new()
    {
        Id = berth.Id,
        BerthIds = berth.BerthIds.ToList(),
        Boat = BoatDto.From(berth.Boat),
        Status = berth.Status,
        Style = berth.Style,
        Metadata = Copy(berth.Metadata),
    };

    /// <summary>
    /// The multi-berth, or null when the entry has no members or no boat, which no multi-berth can be without. A status of Free
    /// (or none) reads as Occupied.
    /// </summary>
    public MultiBerth? ToDomain()
    {
        if (BerthIds is not { Count: > 0 } members || Boat is null) return null;
        var status = Status is { } read && read != BerthStatus.Free ? read : BerthStatus.Occupied;
        return new MultiBerth(Id, members, Boat.ToDomain(), status, Style ?? MooringStyle.Alongside) { Metadata = Read(Metadata) };
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
    public LandKind? LandKind { get; set; }

    public float? LandHeight { get; set; }

    public float? TreeDensity { get; set; }

    public HinterlandScenery? Scenery { get; set; }

    public float? FogFactor { get; set; }

    public PierType? PierType { get; set; }

    public float? PierWidth { get; set; }

    public PierSides? PierBerthingSides { get; set; }

    public float? BerthWidth { get; set; }

    public float? BerthLength { get; set; }

    public float? BerthDepth { get; set; }

    public BerthSeparator? BerthSeparators { get; set; }

    public float? BerthGap { get; set; }

    public bool? AlignBerthsToExisting { get; set; }

    public PierServices? BerthServices { get; set; }

    public float? LandBerthHeading { get; set; }

    public float? SnapDistancePixels { get; set; }

    public string? PierNamePattern { get; set; }

    public BerthNamingDto? BerthNaming { get; set; }

    public static DesignerDto From(DesignerSettings settings) => new()
    {
        LandKind = settings.LandKind,
        LandHeight = settings.LandHeight,
        TreeDensity = settings.TreeDensity,
        Scenery = settings.Scenery,
        FogFactor = settings.FogFactor,
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

    public DesignerSettings ToDomain()
    {
        var defaults = new DesignerSettings();
        return new DesignerSettings
        {
            LandKind = LandKind ?? defaults.LandKind,
            LandHeight = LandHeight ?? defaults.LandHeight,
            TreeDensity = TreeDensity ?? defaults.TreeDensity,
            Scenery = Scenery ?? defaults.Scenery,
            FogFactor = FogFactor ?? defaults.FogFactor,
            PierType = PierType ?? defaults.PierType,
            PierWidth = PierWidth ?? defaults.PierWidth,
            PierBerthingSides = PierBerthingSides is { } sides && sides != 0 ? sides : defaults.PierBerthingSides,
            BerthWidth = BerthWidth ?? defaults.BerthWidth,
            BerthLength = BerthLength ?? defaults.BerthLength,
            BerthDepth = BerthDepth ?? defaults.BerthDepth,
            BerthSeparators = BerthSeparators ?? defaults.BerthSeparators,
            BerthGap = BerthGap ?? defaults.BerthGap,
            AlignBerthsToExisting = AlignBerthsToExisting ?? defaults.AlignBerthsToExisting,
            BerthServices = BerthServices ?? defaults.BerthServices,
            LandBerthHeading = LandBerthHeading ?? defaults.LandBerthHeading,
            SnapDistancePixels = SnapDistancePixels ?? defaults.SnapDistancePixels,
            PierNamePattern = string.IsNullOrWhiteSpace(PierNamePattern) ? defaults.PierNamePattern : PierNamePattern,
            BerthNaming = BerthNaming?.ToDomain() ?? defaults.BerthNaming,
        };
    }
}

/// <summary>How the designer names the berths it draws.</summary>
internal sealed class BerthNamingDto : ExtensibleDto
{
    public string? Pattern { get; set; }

    public string? LandPattern { get; set; }

    public int? LandStartNumber { get; set; }

    public int? LandIncrement { get; set; }

    public int? LandNumberDigits { get; set; }

    public int? StartNumber { get; set; }

    public int? Increment { get; set; }

    public int? NumberDigits { get; set; }

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
        var defaults = BerthNamingScheme.Default;
        var scheme = new BerthNamingScheme
        {
            Pattern = string.IsNullOrWhiteSpace(Pattern) ? BerthNamingScheme.Default.Pattern : Pattern,
            LandPattern = string.IsNullOrWhiteSpace(LandPattern) ? null : LandPattern,
            LandStartNumber = LandStartNumber,
            LandIncrement = LandIncrement == 0 ? null : LandIncrement,
            LandNumberDigits = LandNumberDigits is null ? null : Math.Clamp(LandNumberDigits.Value, 1, 9),
            StartNumber = StartNumber ?? defaults.StartNumber,
            Increment = Increment is { } step && step != 0 ? step : defaults.Increment,
            NumberDigits = Math.Clamp(NumberDigits ?? defaults.NumberDigits, 1, 9),
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

    public BerthLabelMode? BerthLabels { get; set; }

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
        };

        Lighting?.ApplyTo(style.Lighting);
        View?.ApplyTo(style.View);
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
    /// <summary>Null when the file leaves it out, so the current default applies rather than one this class would have to repeat.</summary>
    public float? Size { get; set; }

    /// <summary>
    /// Read as a number of any shape, so a file saying <c>1e9</c> is still read (and then held to the range) rather than refused
    /// outright; written as the whole number it always is.
    /// </summary>
    public double? GridResolution { get; set; }

    /// <summary>Null when the file leaves it out or it can't be read; black is a color like any other.</summary>
    public Vector3? DeepColor { get; set; }

    public Vector3? ShallowColor { get; set; }

    public float? WaveAmplitude { get; set; }

    public float? WaveFrequency { get; set; }

    public float? WaveSpeed { get; set; }

    public float? SkyReflection { get; set; }

    public float? Ripples { get; set; }

    public float? SunGlints { get; set; }

    public float? BoatMotion { get; set; }

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
        BoatMotion = water.BoatMotion,
    };

    /// <summary>A new section (the grid resolution can only be set as it is built), with every setting the file leaves out at its default.</summary>
    public WaterSettings ToDomain()
    {
        var defaults = new WaterSettings();
        return new WaterSettings
        {
            Size = Size is { } size && size > 0f ? size : defaults.Size,
            GridResolution = ReadGridResolution(GridResolution, defaults.GridResolution),
            DeepColor = DeepColor ?? defaults.DeepColor,
            ShallowColor = ShallowColor ?? defaults.ShallowColor,
            WaveAmplitude = WaveAmplitude ?? defaults.WaveAmplitude,
            WaveFrequency = WaveFrequency ?? defaults.WaveFrequency,
            WaveSpeed = WaveSpeed ?? defaults.WaveSpeed,
            SkyReflection = SkyReflection ?? defaults.SkyReflection,
            Ripples = Ripples ?? defaults.Ripples,
            SunGlints = SunGlints ?? defaults.SunGlints,
            BoatMotion = BoatMotion ?? defaults.BoatMotion,
        };
    }

    /// <summary>
    /// The grid size a file asks for, held to what the visualizer builds: nothing sensible (below two cells, or not a number)
    /// means the default, and anything past the maximum is the maximum, since a grid of a billion cells a side is no grid at all.
    /// </summary>
    private static int ReadGridResolution(double? value, int fallback) =>
        value is { } read && double.IsFinite(read) && read >= WaterSettings.MinGridResolution
            ? (int)Math.Min(Math.Round(read), WaterSettings.MaxGridResolution)
            : fallback;
}

internal sealed class LightingDto : ExtensibleDto
{
    /// <summary>Null when the file leaves it out; a zero vector points nowhere and is taken as missing too.</summary>
    public Vector3? SunDirection { get; set; }

    /// <summary>Null when the file leaves it out or it can't be read, as for every color here; black is a color like any other.</summary>
    public Vector3? SunColor { get; set; }

    public Vector3? AmbientColor { get; set; }

    public float? SpecularStrength { get; set; }

    public float? Shininess { get; set; }

    public Vector3? SkyColor { get; set; }

    public Vector3? FogColor { get; set; }

    public float? FogDensity { get; set; }

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

    public void ApplyTo(LightingSettings lighting)
    {
        if (SunDirection is { } sun && sun != Vector3.Zero) lighting.SunDirection = sun;
        lighting.SunColor = SunColor ?? lighting.SunColor;
        lighting.AmbientColor = AmbientColor ?? lighting.AmbientColor;
        lighting.SpecularStrength = SpecularStrength ?? lighting.SpecularStrength;
        lighting.Shininess = Shininess ?? lighting.Shininess;
        lighting.SkyColor = SkyColor ?? lighting.SkyColor;
        lighting.FogColor = FogColor ?? lighting.FogColor;
        lighting.FogDensity = FogDensity ?? lighting.FogDensity;
    }
}

/// <summary>
/// The status colors. Like every color of the style sections, a color is null when the file leaves it out or holds one that
/// can't be read, and the section then keeps its own default.
/// </summary>
internal sealed class StatusDto : ExtensibleDto
{
    public ColorRgba? Free { get; set; }

    public ColorRgba? Occupied { get; set; }

    public ColorRgba? Reserved { get; set; }

    public ColorRgba? TemporarilyFree { get; set; }

    public ColorRgba? Disabled { get; set; }

    public float? PadOpacity { get; set; }

    public float? OccupiedBoatOpacity { get; set; }

    public float? ReservedBoatOpacity { get; set; }

    public float? TemporarilyFreeBoatOpacity { get; set; }

    public float? GhostBoatTint { get; set; }

    public bool? ShowStatusMarkers { get; set; }

    public float? StatusMarkerScale { get; set; }

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
        status.FreeColor = Free ?? status.FreeColor;
        status.OccupiedColor = Occupied ?? status.OccupiedColor;
        status.ReservedColor = Reserved ?? status.ReservedColor;
        status.TemporarilyFreeColor = TemporarilyFree ?? status.TemporarilyFreeColor;
        status.DisabledColor = Disabled ?? status.DisabledColor;
        status.PadOpacity = PadOpacity ?? status.PadOpacity;
        status.OccupiedBoatOpacity = OccupiedBoatOpacity ?? status.OccupiedBoatOpacity;
        status.ReservedBoatOpacity = ReservedBoatOpacity ?? status.ReservedBoatOpacity;
        status.TemporarilyFreeBoatOpacity = TemporarilyFreeBoatOpacity ?? status.TemporarilyFreeBoatOpacity;
        status.GhostBoatTint = GhostBoatTint ?? status.GhostBoatTint;
        status.ShowStatusMarkers = ShowStatusMarkers ?? status.ShowStatusMarkers;
        status.StatusMarkerScale = StatusMarkerScale ?? status.StatusMarkerScale;
    }
}

internal sealed class LandStyleDto : ExtensibleDto
{
    public ColorRgba? Quay { get; set; }

    public ColorRgba? QuayWall { get; set; }

    public ColorRgba? Grass { get; set; }

    public ColorRgba? GrassBank { get; set; }

    public ColorRgba? Rock { get; set; }

    public float? RockColorVariation { get; set; }

    public ColorRgba? Foliage { get; set; }

    public ColorRgba? Conifer { get; set; }

    public ColorRgba? Trunk { get; set; }

    public ColorRgba? Palm { get; set; }

    public ColorRgba? Blossom { get; set; }

    public ColorRgba? Building { get; set; }

    public ColorRgba? Roof { get; set; }

    public bool? ShowTrees { get; set; }

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
        Building = land.BuildingColor,
        Roof = land.RoofColor,
        ShowTrees = land.ShowTrees,
    };

    public void ApplyTo(LandStyle land)
    {
        land.QuayColor = Quay ?? land.QuayColor;
        land.QuayWallColor = QuayWall ?? land.QuayWallColor;
        land.GrassColor = Grass ?? land.GrassColor;
        land.GrassBankColor = GrassBank ?? land.GrassBankColor;
        land.RockColor = Rock ?? land.RockColor;
        land.RockColorVariation = RockColorVariation ?? land.RockColorVariation;
        land.FoliageColor = Foliage ?? land.FoliageColor;
        land.ConiferColor = Conifer ?? land.ConiferColor;
        land.TrunkColor = Trunk ?? land.TrunkColor;
        land.PalmColor = Palm ?? land.PalmColor;
        land.BlossomColor = Blossom ?? land.BlossomColor;
        land.BuildingColor = Building ?? land.BuildingColor;
        land.RoofColor = Roof ?? land.RoofColor;
        land.ShowTrees = ShowTrees ?? land.ShowTrees;
    }
}

internal sealed class StructureDto : ExtensibleDto
{
    public ColorRgba? Wood { get; set; }

    public ColorRgba? Concrete { get; set; }

    public ColorRgba? Float { get; set; }

    public ColorRgba? Fender { get; set; }

    public ColorRgba? Bollard { get; set; }

    public ColorRgba? Steel { get; set; }

    public ColorRgba? BoomFloat { get; set; }

    public ColorRgba? BoomEnd { get; set; }

    public ColorRgba? Pedestal { get; set; }

    public ColorRgba? Power { get; set; }

    public ColorRgba? Water { get; set; }

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
        piers.WoodColor = Wood ?? piers.WoodColor;
        piers.ConcreteColor = Concrete ?? piers.ConcreteColor;
        piers.FloatColor = Float ?? piers.FloatColor;
        piers.FenderColor = Fender ?? piers.FenderColor;
        piers.BollardColor = Bollard ?? piers.BollardColor;
        piers.SteelColor = Steel ?? piers.SteelColor;
        piers.BoomFloatColor = BoomFloat ?? piers.BoomFloatColor;
        piers.BoomEndColor = BoomEnd ?? piers.BoomEndColor;
        piers.PedestalColor = Pedestal ?? piers.PedestalColor;
        piers.PowerColor = Power ?? piers.PowerColor;
        piers.WaterColor = Water ?? piers.WaterColor;
    }
}

internal sealed class LabelDto : ExtensibleDto
{
    public ColorRgba? Color { get; set; }

    public ColorRgba? Ashore { get; set; }

    public ColorRgba? Highlight { get; set; }

    public ColorRgba? Disabled { get; set; }

    public LabelFont? FontFamily { get; set; }

    public LabelTypeface? Typeface { get; set; }

    /// <summary>Name of the captured font, when the design carries one.</summary>
    public string? FontName { get; set; }

    /// <summary>True when the captured face was the bold one.</summary>
    public bool? FontBold { get; set; }

    /// <summary>
    /// The captured font's glyphs, one line per character. The outlines travel with the design so the lettering
    /// survives on a machine that does not have the font installed.
    /// </summary>
    public List<string>? FontGlyphs { get; set; }

    protected override void Normalize() => FontGlyphs?.RemoveAll(line => line is null);

    public static LabelDto From(LabelStyle labels) => new()
    {
        Color = labels.Color,
        Ashore = labels.AshoreColor,
        Highlight = labels.HighlightColor,
        Disabled = labels.DisabledColor,
        FontFamily = labels.FontFamily,
        Typeface = labels.Typeface,
        FontName = labels.Font?.Name,
        FontBold = labels.Font?.IsBold,
        FontGlyphs = labels.Font?.Encode().ToList(),
    };

    public void ApplyTo(LabelStyle labels)
    {
        labels.Color = Color ?? labels.Color;
        labels.AshoreColor = Ashore ?? labels.AshoreColor;
        labels.HighlightColor = Highlight ?? labels.HighlightColor;
        labels.DisabledColor = Disabled ?? labels.DisabledColor;
        labels.FontFamily = FontFamily ?? labels.FontFamily;
        labels.Typeface = Typeface ?? labels.Typeface;
        labels.Font = LabelFontDefinition.Decode(FontName, FontGlyphs, FontBold ?? false);
    }
}

internal sealed class SelectionDto : ExtensibleDto
{
    public bool? ShowMarker { get; set; }

    public ColorRgba? MarkerTint { get; set; }

    public float? MarkerScale { get; set; }

    public float? SelectedGlow { get; set; }

    public float? HoverGlow { get; set; }

    public bool? Pulse { get; set; }

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
        selection.ShowMarker = ShowMarker ?? selection.ShowMarker;
        selection.MarkerTint = MarkerTint ?? selection.MarkerTint;
        selection.MarkerScale = MarkerScale ?? selection.MarkerScale;
        selection.SelectedGlow = SelectedGlow ?? selection.SelectedGlow;
        selection.HoverGlow = HoverGlow ?? selection.HoverGlow;
        selection.Pulse = Pulse ?? selection.Pulse;
    }
}

internal sealed class ViewDto : ExtensibleDto
{
    public float? FieldOfViewDegrees { get; set; }

    public float? CameraSmoothing { get; set; }

    public static ViewDto From(ViewStyle view) => new()
    {
        FieldOfViewDegrees = view.FieldOfViewDegrees,
        CameraSmoothing = view.CameraSmoothing,
    };

    public void ApplyTo(ViewStyle view)
    {
        view.FieldOfViewDegrees = FieldOfViewDegrees ?? view.FieldOfViewDegrees;
        view.CameraSmoothing = CameraSmoothing ?? view.CameraSmoothing;
    }
}
