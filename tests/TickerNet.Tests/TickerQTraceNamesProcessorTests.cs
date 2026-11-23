using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;
using TickerNet.Instrumentation.OpenTelemetry;

namespace TickerNet.Tests;

/// <summary>
/// The opt-in processor that exports TickerNet's spans with TickerQ's names (tickernet.* → tickerq.*).
/// </summary>
public class TickerQTraceNamesProcessorTests
{
    [Fact]
    public void Processor_RenamesSpanNameAndTickerNetTags()
    {
        using var source = new ActivitySource("TickerNet");
        var exported = new List<SpanSnapshot>();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddTickerNetSource(useTickerQNames: true)
            .AddProcessor(new SimpleActivityExportProcessor(new SnapshotExporter(exported)))
            .Build();

        var activity = StartUnique(source, "tickernet.job.execute.timeticker");
        activity.SetTag("tickernet.job.id", "123");
        activity.SetTag("tickernet.job.retries", 3);
        activity.SetTag("other.tag", "kept");
        activity.Stop();

        var span = Assert.Single(exported, s => s.Id == activity.Id);
        Assert.Equal("tickerq.job.execute.timeticker", span.Name);
        Assert.Equal("123", span.Tags["tickerq.job.id"]);
        Assert.Equal(3, span.Tags["tickerq.job.retries"]);
        Assert.Equal("kept", span.Tags["other.tag"]);
        Assert.DoesNotContain(span.Tags.Keys, k => k.StartsWith("tickernet.", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutTickerQNames_KeepsTickerNetNames()
    {
        using var source = new ActivitySource("TickerNet");
        var exported = new List<SpanSnapshot>();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddTickerNetSource()
            .AddProcessor(new SimpleActivityExportProcessor(new SnapshotExporter(exported)))
            .Build();

        var activity = StartUnique(source, "tickernet.job.completed");
        activity.SetTag("tickernet.job.id", "123");
        activity.Stop();

        var span = Assert.Single(exported, s => s.Id == activity.Id);
        Assert.Equal("tickernet.job.completed", span.Name);
        Assert.Equal("123", span.Tags["tickernet.job.id"]);
    }

    [Fact]
    public void ExporterAddedBeforeProcessor_SeesTickerNetNames()
    {
        // Documents the ordering rule: the renaming has to run before the exporter.
        using var source = new ActivitySource("TickerNet");
        var exported = new List<SpanSnapshot>();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddProcessor(new SimpleActivityExportProcessor(new SnapshotExporter(exported)))
            .AddTickerNetSource(useTickerQNames: true)
            .Build();

        var activity = StartUnique(source, "tickernet.job.failed");
        activity.SetTag("tickernet.job.id", "123");
        activity.Stop();

        var span = Assert.Single(exported, s => s.Id == activity.Id);
        Assert.Equal("tickernet.job.failed", span.Name);
        Assert.True(span.Tags.ContainsKey("tickernet.job.id"));
    }

    [Fact]
    public void Processor_IgnoresOtherSources()
    {
        using var source = new ActivitySource("TickerNet.Tests.Other");
        var exported = new List<SpanSnapshot>();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource("TickerNet.Tests.Other")
            .AddTickerNetSource(useTickerQNames: true)
            .AddProcessor(new SimpleActivityExportProcessor(new SnapshotExporter(exported)))
            .Build();

        var activity = StartUnique(source, "tickernet.job.enqueued");
        activity.SetTag("tickernet.job.id", "123");
        activity.Stop();

        var span = Assert.Single(exported, s => s.Id == activity.Id);
        Assert.Equal("tickernet.job.enqueued", span.Name);
        Assert.True(span.Tags.ContainsKey("tickernet.job.id"));
    }

    [Fact]
    public void Processor_LeavesNamesWithoutTickerNetPrefixAlone()
    {
        using var source = new ActivitySource("TickerNet");
        var exported = new List<SpanSnapshot>();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddTickerNetSource(useTickerQNames: true)
            .AddProcessor(new SimpleActivityExportProcessor(new SnapshotExporter(exported)))
            .Build();

        var activity = StartUnique(source, "custom span");
        activity.SetTag("custom.key", "v");
        activity.Stop();

        var span = Assert.Single(exported, s => s.Id == activity.Id);
        Assert.Equal("custom span", span.Name);
        Assert.Equal("v", span.Tags["custom.key"]);
    }

    private static Activity StartUnique(ActivitySource source, string name)
    {
        // Start without a parent so spans from other tests running at the same time can't interfere.
        var activity = source.StartActivity(name, ActivityKind.Internal, parentContext: default(ActivityContext));
        Assert.NotNull(activity);
        return activity!;
    }

    private sealed record SpanSnapshot(string? Id, string Name, Dictionary<string, object?> Tags);

    private sealed class SnapshotExporter(List<SpanSnapshot> sink) : BaseExporter<Activity>
    {
        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                var tags = activity.TagObjects.ToDictionary(t => t.Key, t => t.Value);
                lock (sink)
                    sink.Add(new SpanSnapshot(activity.Id, activity.DisplayName, tags));
            }
            return ExportResult.Success;
        }
    }
}
