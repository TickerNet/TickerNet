using TickerNet.Hub.Protocol.V1;
using TickerNet.Utilities;

namespace TickerNet.Worker.Internal;

/// <summary>Describes this worker's local <c>[TickerFunction]</c> functions for the hub.</summary>
internal static class FunctionManifest
{
    public static IEnumerable<FunctionDescriptor> Build(Func<string, bool>? filter = null)
    {
        foreach (var (name, function) in TickerFunctionProvider.TickerFunctions)
        {
            // Remote names can be present when a process is both hub and worker, as in tests.
            if (!Offers(name, filter)) continue;

            var requestType = string.Empty;
            var requestExample = string.Empty;
            if (TickerFunctionProvider.TickerFunctionRequestInfos.TryGetValue(name, out var info))
            {
                requestType = info.RequestType ?? string.Empty;
                requestExample = info.RequestExampleJson ?? string.Empty;
            }
            else if (TickerFunctionProvider.TickerFunctionRequestTypes.TryGetValue(name, out var type))
            {
                requestType = type.Item1 ?? string.Empty;
            }

            yield return new FunctionDescriptor
            {
                Name = name,
                CronExpression = function.cronExpression ?? string.Empty,
                Priority = (int)function.Priority,
                MaxConcurrency = function.MaxConcurrency,
                RequestType = requestType,
                RequestExampleJson = requestExample
            };
        }
    }

    /// <summary>True when this worker offers <paramref name="name"/>: a local function that passes the filter.</summary>
    public static bool Offers(string name, Func<string, bool>? filter = null)
        => !RemoteFunctionName.IsRemote(name)
           && TickerFunctionProvider.TickerFunctions.ContainsKey(name)
           && (filter?.Invoke(name) ?? true);
}
