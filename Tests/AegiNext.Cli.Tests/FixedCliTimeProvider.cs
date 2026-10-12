namespace AegiNext.Cli.Tests;

internal sealed class FixedCliTimeProvider : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        return new(2026, 10, 12, 8, 9, 10, TimeSpan.Zero);
    }

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
