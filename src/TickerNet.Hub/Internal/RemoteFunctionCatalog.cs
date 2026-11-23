using TickerNet.Hub.Protocol.V1;
using TickerNet.Utilities;
using TickerNet.Utilities.Enums;

namespace TickerNet.Hub.Internal;

/// <summary>
/// The functions worker nodes have declared since the hub started, published into
/// <see cref="TickerFunctionProvider"/> as <c>function@node</c> so the scheduler, the managers and
/// the dashboard see them like local functions.
/// </summary>
/// <remarks>
/// <para>
/// A node's list is the union of what its workers declared: a replica on an older or newer
/// version never removes functions another replica still offers. Functions stay registered after
/// their workers disconnect, so due runs wait for a worker (see
/// <see cref="TickerNetHubOptions.WorkerWaitTimeout"/>) instead of failing to find the function.
/// </para>
/// <para>
/// Only names this catalog published are replaced, so a local function that happens to contain
/// <c>@</c> is left alone.
/// </para>
/// </remarks>
internal sealed class RemoteFunctionCatalog
{
    private readonly object _lock = new();
    private readonly Dictionary<string, NodeFunctions> _byNode = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _published = new(StringComparer.Ordinal);

    /// <summary>Adds <paramref name="functions"/> to <paramref name="nodeName"/>'s list and republishes.</summary>
    public void Add(string nodeName, IReadOnlyList<FunctionDescriptor> functions)
    {
        if (functions.Count == 0) return;

        lock (_lock)
        {
            if (!_byNode.TryGetValue(nodeName, out var node))
                _byNode[nodeName] = node = new NodeFunctions(nodeName);

            foreach (var function in functions)
                node.Functions[function.Name] = function;

            Publish();
        }
    }

    /// <summary>True when <paramref name="functionName"/> is a remote name this catalog published.</summary>
    public bool IsPublished(string functionName)
    {
        lock (_lock) return _published.Contains(functionName);
    }

    /// <summary>Functions declared by each node since the hub started.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Snapshot()
    {
        lock (_lock)
        {
            return _byNode.Values.ToDictionary(
                node => node.Name,
                node => (IReadOnlyList<string>)node.Functions.Keys.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Publish()
    {
        var remoteFunctions = new Dictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate, int MaxConcurrency)>(StringComparer.Ordinal);
        var remoteRequestInfo = new Dictionary<string, (string RequestType, string RequestExampleJson)>(StringComparer.Ordinal);

        foreach (var node in _byNode.Values)
        {
            var dispatch = RemoteDispatch.For(node.Name);
            foreach (var function in node.Functions.Values)
            {
                var name = RemoteFunctionName.Qualify(function.Name, node.Name);
                var priority = System.Enum.IsDefined(typeof(TickerTaskPriority), function.Priority)
                    ? (TickerTaskPriority)function.Priority
                    : TickerTaskPriority.Normal;

                // The worker enforces its own per-function limit, so the scheduler doesn't add one.
                remoteFunctions[name] = (function.CronExpression ?? string.Empty, priority, dispatch, 0);
                remoteRequestInfo[name] = (function.RequestType ?? string.Empty, function.RequestExampleJson ?? string.Empty);
            }
        }

        var previous = _published;
        TickerFunctionProvider.MergeRemoteFunctions(remoteFunctions, previous.Contains);
        TickerFunctionProvider.MergeRemoteRequestInfo(remoteRequestInfo, previous.Contains);
        _published = remoteFunctions.Keys.ToHashSet(StringComparer.Ordinal);
    }

    private sealed class NodeFunctions(string name)
    {
        // The node's spelling from the first worker that connected; stored jobs use it.
        public string Name { get; } = name;
        public Dictionary<string, FunctionDescriptor> Functions { get; } = new(StringComparer.Ordinal);
    }
}
