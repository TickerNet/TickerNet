using System;
using TickerNet.Instrumentation.OpenTelemetry;

// In OpenTelemetry.Trace, like other instrumentation libraries, so `using OpenTelemetry.Trace;` is enough.
namespace OpenTelemetry.Trace;

/// <summary>Extension methods for adding TickerNet's spans to an OpenTelemetry tracer provider.</summary>
public static class TickerNetTracerProviderBuilderExtensions
{
    /// <summary>
    /// Listens to TickerNet's spans (the <c>TickerNet</c> ActivitySource). The same as <c>AddSource("TickerNet")</c>.
    /// </summary>
    /// <param name="builder">The tracer provider builder.</param>
    /// <param name="useTickerQNames">
    /// Export spans with the names TickerQ used: span names and tag keys starting with <c>tickernet.</c> start with
    /// <c>tickerq.</c> instead, so trace queries, dashboards and alerts written for TickerQ keep working. Off by
    /// default. When on, call this method before adding exporters, because the renaming must run before them.
    /// </param>
    public static TracerProviderBuilder AddTickerNetSource(this TracerProviderBuilder builder, bool useTickerQNames = false)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddSource(TickerQTraceNamesProcessor.TickerNetSourceName);
        if (useTickerQNames)
            builder.AddProcessor(new TickerQTraceNamesProcessor());

        return builder;
    }
}
