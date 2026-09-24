using System.Numerics;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Api;

/// <summary>
/// Partial change to a berth, for <c>IMarinaVisualizer.UpdateBerth(BerthUpdate)</c> and <c>BatchUpdate</c>.
/// Only non-null members are applied, so a batch can mix status changes, boat assignments, geometry edits and interaction flags.
/// </summary>
/// <param name="BerthId">The berth to change.</param>
/// <example>
/// <code>
/// marina.BatchUpdate(new[]
/// {
///     BerthUpdate.Occupy("A-L01", boat),
///     BerthUpdate.TemporarilyFree("A-L02"),
///     BerthUpdate.Flags("A-L03", disabled: true),
///     new BerthUpdate("A-L04") { Label = "A-4 (long)", Length = 15 },
/// });
/// </code>
/// </example>
public sealed record BerthUpdate(string BerthId)
{
    /// <summary>New status. Free also removes the boat.</summary>
    public BerthStatus? Status { get; init; }

    /// <summary>Boat to assign. Ignored when the resulting status is Free.</summary>
    public Boat? Boat { get; init; }

    /// <summary>Removes the assigned boat (applied before <see cref="Boat"/>).</summary>
    public bool ClearBoat { get; init; }

    /// <summary>New display label.</summary>
    public string? Label { get; init; }

    /// <summary>New center of the water area, in plan coordinates.</summary>
    public Vector2? Center { get; init; }

    /// <summary>New heading (bow direction) in degrees.</summary>
    public float? HeadingDegrees { get; init; }

    /// <summary>New length in meters.</summary>
    public float? Length { get; init; }

    /// <summary>New width in meters.</summary>
    public float? Width { get; init; }

    /// <summary>New maximum draft in meters.</summary>
    public float? MaxDraft { get; init; }

    /// <summary>Show or hide the automatic finger piers.</summary>
    public bool? HasFingerPiers { get; init; }

    /// <summary>Show or hide the berth (see <see cref="Berth.IsVisible"/>).</summary>
    public bool? IsVisible { get; init; }

    /// <summary>Disable or enable the berth (see <see cref="Berth.IsDisabled"/>).</summary>
    public bool? IsDisabled { get; init; }

    /// <summary>Make the berth read-only or editable (see <see cref="Berth.IsReadOnly"/>).</summary>
    public bool? IsReadOnly { get; init; }

    /// <summary>Replaces the berth's <see cref="Berth.Metadata"/>.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Entries written into the berth's <see cref="Berth.ExternalData"/>. Merged: keys listed here are added or
    /// overwritten, keys not listed are left as they are, and a null value is stored as null (the key stays). To take a
    /// key out, list it in <see cref="ExternalDataRemovals"/>.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? ExternalData { get; init; }

    /// <summary>
    /// Keys taken out of the berth's <see cref="Berth.ExternalData"/>, before <see cref="ExternalData"/> is merged in (so
    /// a key both removed and written ends up with the written value). Keys that are not there are ignored.
    /// </summary>
    public IReadOnlyCollection<string>? ExternalDataRemovals { get; init; }

    /// <summary>Marks the berth Occupied by <paramref name="boat"/>.</summary>
    public static BerthUpdate Occupy(string berthId, Boat boat) => new(berthId) { Status = BerthStatus.Occupied, Boat = boat };

    /// <summary>Marks the berth Reserved, optionally for a known incoming boat.</summary>
    public static BerthUpdate Reserve(string berthId, Boat? expectedBoat = null) =>
        new(berthId) { Status = BerthStatus.Reserved, Boat = expectedBoat, ClearBoat = expectedBoat is null };

    /// <summary>Marks the berth Free and removes any boat.</summary>
    public static BerthUpdate Free(string berthId) => new(berthId) { Status = BerthStatus.Free, ClearBoat = true };

    /// <summary>Marks the berth Temporarily Free. Keeps the assigned boat unless <paramref name="boat"/> replaces it.</summary>
    public static BerthUpdate TemporarilyFree(string berthId, Boat? boat = null) =>
        new(berthId) { Status = BerthStatus.TemporarilyFree, Boat = boat };

    /// <summary>Sets the interaction flags; null leaves a flag unchanged.</summary>
    public static BerthUpdate Flags(string berthId, bool? visible = null, bool? disabled = null, bool? readOnly = null) =>
        new(berthId) { IsVisible = visible, IsDisabled = disabled, IsReadOnly = readOnly };

    /// <summary>Moves or resizes the berth; null leaves a value unchanged.</summary>
    public static BerthUpdate Geometry(string berthId, Vector2? center = null, float? headingDegrees = null, float? length = null, float? width = null) =>
        new(berthId) { Center = center, HeadingDegrees = headingDegrees, Length = length, Width = width };

    internal Berth ApplyTo(Berth berth)
    {
        var result = berth;
        if (Label is not null) result = result with { Label = Label };
        if (Center.HasValue) result = result with { Center = Center.Value };
        if (HeadingDegrees.HasValue) result = result with { HeadingDegrees = HeadingDegrees.Value };
        if (Length.HasValue) result = result with { Length = Length.Value };
        if (Width.HasValue) result = result with { Width = Width.Value };
        if (MaxDraft.HasValue) result = result with { MaxDraft = MaxDraft.Value };
        if (HasFingerPiers.HasValue) result = result with { HasFingerPiers = HasFingerPiers.Value };
        if (IsVisible.HasValue) result = result with { IsVisible = IsVisible.Value };
        if (IsDisabled.HasValue) result = result with { IsDisabled = IsDisabled.Value };
        if (IsReadOnly.HasValue) result = result with { IsReadOnly = IsReadOnly.Value };
        if (Metadata is not null) result = result with { Metadata = Metadata };
        if (ClearBoat) result = result with { Boat = null };
        if (Boat is not null) result = result with { Boat = Boat };
        if (Status.HasValue) result = result with { Status = Status.Value };
        return result;
    }
}

/// <summary>
/// Partial change to a pier's position, size, orientation, type or name, for <c>IMarinaVisualizer.UpdatePier(PierUpdate)</c>.
/// Only non-null members are applied. When the length or heading changes without a new <see cref="Start"/>, the pier keeps its center.
/// </summary>
/// <param name="PierId">The pier to change.</param>
/// <example><code>marina.UpdatePier(new PierUpdate("E") { Type = PierType.FloatingConcrete, HeadingDegrees = 10, Length = 80 });</code></example>
public sealed record PierUpdate(string PierId)
{
    /// <summary>New display name.</summary>
    public string? Name { get; init; }

    /// <summary>New shore-end point. Ignored when <see cref="Center"/> is set.</summary>
    public Vector2? Start { get; init; }

    /// <summary>New center point (applied after length and heading changes).</summary>
    public Vector2? Center { get; init; }

    /// <summary>New heading in degrees.</summary>
    public float? HeadingDegrees { get; init; }

    /// <summary>New length in meters.</summary>
    public float? Length { get; init; }

    /// <summary>New deck width in meters.</summary>
    public float? Width { get; init; }

    /// <summary>New construction type (changes the look; the default deck height follows unless <see cref="DeckHeight"/> was set explicitly).</summary>
    public PierType? Type { get; init; }

    /// <summary>New deck height above the water, in meters.</summary>
    public float? DeckHeight { get; init; }

    /// <summary>New support spacing in meters.</summary>
    public float? PilingSpacing { get; init; }

    /// <summary>New berthing sides (single- or double-sided pier). Existing berths are not moved or removed.</summary>
    public PierSides? BerthingSides { get; init; }

    /// <summary>New power/water pedestals (drawn beside the pier's berths).</summary>
    public PierServices? Services { get; init; }

    internal Pier ApplyTo(Pier pier)
    {
        // Keep the center fixed while resizing/rotating unless a new start point is given.
        var center = Center ?? (Start.HasValue ? (Vector2?)null : pier.Center);
        var result = pier;
        if (Name is not null) result = result with { Name = Name };
        if (Start.HasValue) result = result with { Start = Start.Value };
        if (HeadingDegrees.HasValue) result = result with { HeadingDegrees = HeadingDegrees.Value };
        if (Length.HasValue) result = result with { Length = Length.Value };
        if (Width.HasValue) result = result with { Width = Width.Value };
        if (Type.HasValue) result = result with { Type = Type.Value };
        if (DeckHeight.HasValue) result = result with { DeckHeight = DeckHeight.Value };
        if (PilingSpacing.HasValue) result = result with { PilingSpacing = PilingSpacing.Value };
        if (BerthingSides.HasValue) result = result with { BerthingSides = BerthingSides.Value };
        if (Services.HasValue) result = result with { Services = Services.Value };
        if (center.HasValue && (Center.HasValue || HeadingDegrees.HasValue || Length.HasValue)) result = result.WithCenter(center.Value);
        return result;
    }
}

/// <summary>An update in a batch that could not be applied.</summary>
/// <param name="BerthId">The update's berth id.</param>
/// <param name="Message">Why it failed (unknown berth, invalid value, ...).</param>
public sealed record BatchUpdateError(string BerthId, string Message);

/// <summary>Outcome of <c>IMarinaVisualizer.BatchUpdate</c> or <c>SetBerthFlags</c>.</summary>
/// <param name="AppliedCount">Number of updates applied.</param>
/// <param name="Errors">Updates that failed; the others were still applied.</param>
public sealed record BatchUpdateResult(int AppliedCount, IReadOnlyList<BatchUpdateError> Errors)
{
    /// <summary>True when every update was applied.</summary>
    public bool Succeeded => Errors.Count == 0;
}

/// <summary>Occupancy counts for dashboards (<c>IMarinaVisualizer.GetStatistics</c>). Hidden and disabled berths are counted too.</summary>
/// <param name="TotalBerths">All berths.</param>
/// <param name="Free">Free berths.</param>
/// <param name="Occupied">Occupied berths.</param>
/// <param name="Reserved">Reserved berths.</param>
/// <param name="TemporarilyFree">Temporarily free berths.</param>
public sealed record MarinaStatistics(int TotalBerths, int Free, int Occupied, int Reserved, int TemporarilyFree = 0)
{
    /// <summary>Occupied berths as a fraction of all berths (0–1).</summary>
    public double OccupancyRate => TotalBerths == 0 ? 0d : (double)Occupied / TotalBerths;
}
