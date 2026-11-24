namespace TickerNet.Tests;

/// <summary>
/// Tests that set CronScheduleCache.TimeZoneInfo, which is process-wide. They run on their own, so no other test
/// reads the time zone while one of them has changed it.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CronTimeZoneGlobalStateCollection
{
    public const string Name = "CronTimeZoneGlobalState";
}
