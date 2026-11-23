using System.Threading;
using System.Threading.Tasks;
using TickerNet.Utilities.Interfaces;
using TickerNet.Utilities.Models;

namespace TickerNet.Utilities.Temps;

/// <summary>
/// No-operation implementation of ITickerNetDispatcher.
/// Used when background services are disabled (queue-only mode).
/// </summary>
internal class NoOpTickerNetDispatcher : ITickerNetDispatcher
{
    public bool IsEnabled => false;
    
    public Task DispatchAsync(InternalFunctionContext[] contexts, CancellationToken cancellationToken = default)
    {
        // No-op: dispatcher not available in queue-only mode
        return Task.CompletedTask;
    }

    public Task DispatchAsync(InternalFunctionContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
