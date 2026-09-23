namespace VirtualMarina.TestSupport;

/// <summary>A clock that always reads the same instant, in UTC, so anything dated from it is repeatable.</summary>
/// <param name="now">The instant it reads.</param>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    /// <summary>Noon UTC on 1 June 2025: a fixed, unremarkable date for fixtures.</summary>
    public static FixedClock Default { get; } = new(new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero));

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => now;

    /// <inheritdoc/>
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
