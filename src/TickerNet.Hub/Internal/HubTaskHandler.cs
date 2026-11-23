using TickerNet.Utilities;
using TickerNet.Utilities.Interfaces;
using TickerNet.Utilities.Models;

namespace TickerNet.Hub.Internal;

/// <summary>
/// Wraps the scheduler's job runner. A remote function (<c>function@node</c>) that no worker has
/// registered since the scheduler started (for example right after a restart) has no delegate yet;
/// this gives it the dispatch delegate for its node, so the run waits for a worker instead of being
/// skipped at once. Everything else goes to the scheduler's runner unchanged.
/// </summary>
internal sealed class HubTaskHandler(ITickerExecutionTaskHandler inner) : ITickerExecutionTaskHandler
{
    public Task ExecuteTaskAsync(InternalFunctionContext context, bool isDue, CancellationToken cancellationToken = default)
    {
        AttachRemoteDelegates(context);
        return inner.ExecuteTaskAsync(context, isDue, cancellationToken);
    }

    private static void AttachRemoteDelegates(InternalFunctionContext context)
    {
        if (context.CachedDelegate is null && RemoteFunctionName.IsRemote(context.FunctionName))
            context.CachedDelegate = RemoteDispatch.For(RemoteFunctionName.GetNodeName(context.FunctionName));

        if (context.TimeTickerChildren is { Count: > 0 } children)
        {
            foreach (var child in children)
                AttachRemoteDelegates(child);
        }
    }
}
