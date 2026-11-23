using System.Threading;

namespace TickerNet.TickerNetThreadPool;

/// <summary>
/// Custom SynchronizationContext that keeps async continuations on TickerNet worker threads.
/// Ensures true concurrency control by preventing execution from switching to ThreadPool.
/// Uses flexible thread assignment for optimal performance and load balancing.
/// </summary>
internal sealed class TickerNetSynchronizationContext : SynchronizationContext
{
    private readonly TickerNetTaskScheduler _scheduler;

    public TickerNetSynchronizationContext(TickerNetTaskScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    /// <summary>
    /// Posts async continuations to TickerNet workers with circular dependency protection.
    /// Uses direct execution if called from within a TickerNet worker to prevent deadlocks.
    /// </summary>
    public override void Post(SendOrPostCallback d, object state)
    {
        // Check if we're already on a TickerNet worker thread using ThreadStatic flag
        if (TickerNetTaskScheduler.IsTickerNetWorkerThread)
        {
            // We're on a TickerNet worker - execute directly to avoid circular queueing
            try 
            { 
                d(state); 
            } 
            catch 
            { 
                /* swallow continuation exceptions */ 
            }
        }
        else
        {
            // We're not on a TickerNet worker - safe to queue the continuation
            _scheduler.PostContinuation(d, state);
        }
    }

    /// <summary>
    /// Sends synchronous operations (not typically used in async scenarios).
    /// </summary>
    public override void Send(SendOrPostCallback d, object state)
    {
        // For synchronous operations, execute directly
        d(state);
    }
}