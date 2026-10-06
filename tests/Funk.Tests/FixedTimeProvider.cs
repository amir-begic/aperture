namespace Funk.Tests;

/// <summary>A clock stuck at one local time, in UTC so tests don't depend on the machine's time zone.</summary>
public sealed class FixedTimeProvider(DateTime localNow) : TimeProvider
{
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(localNow, DateTimeKind.Utc));
}
