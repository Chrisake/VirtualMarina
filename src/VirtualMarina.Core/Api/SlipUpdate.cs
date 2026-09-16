using System.Numerics;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.Core.Api;

/// <summary>
/// Partial change to a slip, for <c>IMarinaVisualizer.UpdateSlip(SlipUpdate)</c> and <c>BatchUpdate</c>.
/// Only non-null members are applied, so a batch can mix status changes, boat assignments, geometry edits and interaction flags.
/// </summary>
/// <param name="SlipId">The slip to change.</param>
/// <example>
/// <code>
/// marina.BatchUpdate(new[]
/// {
///     SlipUpdate.Occupy("A-L01", boat),
///     SlipUpdate.TemporarilyFree("A-L02"),
///     SlipUpdate.Flags("A-L03", disabled: true),
///     new SlipUpdate("A-L04") { Label = "A-4 (long)", Length = 15 },
/// });
/// </code>
/// </example>
public sealed record SlipUpdate(string SlipId)
{
    /// <summary>New status. Free also removes the boat.</summary>
    public SlipStatus? Status { get; init; }

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

    /// <summary>Show or hide the slip (see <see cref="Slip.IsVisible"/>).</summary>
    public bool? IsVisible { get; init; }

    /// <summary>Disable or enable the slip (see <see cref="Slip.IsDisabled"/>).</summary>
    public bool? IsDisabled { get; init; }

    /// <summary>Make the slip read-only or editable (see <see cref="Slip.IsReadOnly"/>).</summary>
    public bool? IsReadOnly { get; init; }

    /// <summary>Replaces the slip's <see cref="Slip.Metadata"/>.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>Entries written into the slip's <see cref="Slip.ExternalData"/> (merged; a null value is stored as null).</summary>
    public IReadOnlyDictionary<string, object?>? ExternalData { get; init; }

    /// <summary>Marks the slip Occupied by <paramref name="boat"/>.</summary>
    public static SlipUpdate Occupy(string slipId, Boat boat) => new(slipId) { Status = SlipStatus.Occupied, Boat = boat };

    /// <summary>Marks the slip Reserved, optionally for a known incoming boat.</summary>
    public static SlipUpdate Reserve(string slipId, Boat? expectedBoat = null) =>
        new(slipId) { Status = SlipStatus.Reserved, Boat = expectedBoat, ClearBoat = expectedBoat is null };

    /// <summary>Marks the slip Free and removes any boat.</summary>
    public static SlipUpdate Free(string slipId) => new(slipId) { Status = SlipStatus.Free, ClearBoat = true };

    /// <summary>Marks the slip Temporarily Free. Keeps the assigned boat unless <paramref name="boat"/> replaces it.</summary>
    public static SlipUpdate TemporarilyFree(string slipId, Boat? boat = null) =>
        new(slipId) { Status = SlipStatus.TemporarilyFree, Boat = boat };

    /// <summary>Sets the interaction flags; null leaves a flag unchanged.</summary>
    public static SlipUpdate Flags(string slipId, bool? visible = null, bool? disabled = null, bool? readOnly = null) =>
        new(slipId) { IsVisible = visible, IsDisabled = disabled, IsReadOnly = readOnly };

    /// <summary>Moves or resizes the slip; null leaves a value unchanged.</summary>
    public static SlipUpdate Geometry(string slipId, Vector2? center = null, float? headingDegrees = null, float? length = null, float? width = null) =>
        new(slipId) { Center = center, HeadingDegrees = headingDegrees, Length = length, Width = width };

    internal bool ChangesOccupancy => Status.HasValue || Boat is not null || ClearBoat;

    internal Slip ApplyTo(Slip slip)
    {
        var result = slip;
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
/// Partial change to a dock's position, size, orientation, type or name, for <c>IMarinaVisualizer.UpdateDock(DockUpdate)</c>.
/// Only non-null members are applied. When the length or heading changes without a new <see cref="Start"/>, the dock keeps its center.
/// </summary>
/// <param name="DockId">The dock to change.</param>
/// <example><code>marina.UpdateDock(new DockUpdate("E") { Type = DockType.FloatingConcrete, HeadingDegrees = 10, Length = 80 });</code></example>
public sealed record DockUpdate(string DockId)
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
    public DockType? Type { get; init; }

    /// <summary>New deck height above the water, in meters.</summary>
    public float? DeckHeight { get; init; }

    /// <summary>New support spacing in meters.</summary>
    public float? PilingSpacing { get; init; }

    internal Dock ApplyTo(Dock dock)
    {
        // Keep the center fixed while resizing/rotating unless a new start point is given.
        var center = Center ?? (Start.HasValue ? (Vector2?)null : dock.Center);
        var result = dock;
        if (Name is not null) result = result with { Name = Name };
        if (Start.HasValue) result = result with { Start = Start.Value };
        if (HeadingDegrees.HasValue) result = result with { HeadingDegrees = HeadingDegrees.Value };
        if (Length.HasValue) result = result with { Length = Length.Value };
        if (Width.HasValue) result = result with { Width = Width.Value };
        if (Type.HasValue) result = result with { Type = Type.Value };
        if (DeckHeight.HasValue) result = result with { DeckHeight = DeckHeight.Value };
        if (PilingSpacing.HasValue) result = result with { PilingSpacing = PilingSpacing.Value };
        if (center.HasValue && (Center.HasValue || HeadingDegrees.HasValue || Length.HasValue)) result = result.WithCenter(center.Value);
        return result;
    }
}

/// <summary>An update in a batch that could not be applied.</summary>
/// <param name="SlipId">The update's slip id.</param>
/// <param name="Message">Why it failed (unknown slip, invalid value, ...).</param>
public sealed record BatchUpdateError(string SlipId, string Message);

/// <summary>Outcome of <c>IMarinaVisualizer.BatchUpdate</c> or <c>SetSlipFlags</c>.</summary>
/// <param name="AppliedCount">Number of updates applied.</param>
/// <param name="Errors">Updates that failed; the others were still applied.</param>
public sealed record BatchUpdateResult(int AppliedCount, IReadOnlyList<BatchUpdateError> Errors)
{
    /// <summary>True when every update was applied.</summary>
    public bool Succeeded => Errors.Count == 0;
}

/// <summary>Occupancy counts for dashboards (<c>IMarinaVisualizer.GetStatistics</c>). Hidden and disabled slips are counted too.</summary>
/// <param name="TotalSlips">All slips.</param>
/// <param name="Free">Free slips.</param>
/// <param name="Occupied">Occupied slips.</param>
/// <param name="Reserved">Reserved slips.</param>
/// <param name="TemporarilyFree">Temporarily free slips.</param>
public sealed record MarinaStatistics(int TotalSlips, int Free, int Occupied, int Reserved, int TemporarilyFree = 0)
{
    /// <summary>Occupied slips as a fraction of all slips (0–1).</summary>
    public double OccupancyRate => TotalSlips == 0 ? 0d : (double)Occupied / TotalSlips;
}
