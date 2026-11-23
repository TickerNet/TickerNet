using System;
using System.Threading;
using System.Threading.Tasks;
using TickerNet.Utilities.Interfaces;

namespace TickerNet.Utilities.Temps;

/// <summary>
/// No-operation implementation of ITickerNetHostScheduler.
/// Used when background services are disabled (queue-only mode).
/// </summary>
internal class NoOpTickerNetHostScheduler : ITickerNetHostScheduler
{
    public bool IsRunning => false;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public void RestartIfNeeded(DateTime? dateTime)
    {
        // No-op: scheduler not running
    }

    public void Restart()
    {
        // No-op: scheduler not running
    }
}
