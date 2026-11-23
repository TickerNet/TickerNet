using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenTelemetry;

namespace TickerNet.Instrumentation.OpenTelemetry;

/// <summary>
/// Renames TickerNet's spans to the names TickerQ used, so trace queries, dashboards and alerts written for TickerQ
/// keep working: span names <c>tickernet.*</c> become <c>tickerq.*</c>, and so do tag keys. Only spans from the
/// <c>TickerNet</c> ActivitySource are changed.
/// </summary>
/// <remarks>
/// <para>Renaming happens when a span ends, so this processor must run before any exporter. The easiest way is
/// <c>AddTickerNetSource(useTickerQNames: true)</c>, called before you add exporters. If you add it yourself with
/// <c>AddProcessor</c>, add it before the exporter too.</para>
/// <para>The instrumentation scope (the ActivitySource name) stays <c>TickerNet</c>; filters on the scope name need
/// to change.</para>
/// </remarks>
public sealed class TickerQTraceNamesProcessor : BaseProcessor<Activity>
{
    /// <summary>The ActivitySource TickerNet creates its spans with.</summary>
    public const string TickerNetSourceName = "TickerNet";

    private const string NewPrefix = "tickernet.";
    private const string OldPrefix = "tickerq.";

    /// <inheritdoc />
    public override void OnEnd(Activity activity)
    {
        if (activity is null || !string.Equals(activity.Source.Name, TickerNetSourceName, StringComparison.Ordinal))
            return;

        if (activity.DisplayName.StartsWith(NewPrefix, StringComparison.Ordinal))
            activity.DisplayName = OldPrefix + activity.DisplayName.Substring(NewPrefix.Length);

        List<KeyValuePair<string, object?>>? renamed = null;
        foreach (var tag in activity.TagObjects)
        {
            if (tag.Key.StartsWith(NewPrefix, StringComparison.Ordinal))
                (renamed ??= new List<KeyValuePair<string, object?>>()).Add(tag);
        }

        if (renamed is null)
            return;

        foreach (var tag in renamed)
        {
            activity.SetTag(tag.Key, null);
            activity.SetTag(OldPrefix + tag.Key.Substring(NewPrefix.Length), tag.Value);
        }
    }
}
