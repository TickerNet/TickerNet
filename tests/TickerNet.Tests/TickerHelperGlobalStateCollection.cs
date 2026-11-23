namespace TickerNet.Tests;

/// <summary>
/// Tests that set TickerHelper's process-wide request settings (UseGZipCompression,
/// RequestJsonSerializerOptions). Every AddTickerNet call also writes those settings, so these tests
/// run on their own, never alongside tests that build a host.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TickerHelperGlobalStateCollection
{
    public const string Name = "TickerHelperGlobalState";
}
