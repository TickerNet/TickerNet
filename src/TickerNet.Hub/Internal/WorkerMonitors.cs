using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerNet.Utilities;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Interfaces;

namespace TickerNet.Hub.Internal;

/// <summary>
/// Drops workers that have sent nothing within <see cref="TickerNetHubOptions.WorkerTimeout"/>, so
/// runs aren't sent to a worker whose connection died without closing.
/// </summary>
internal sealed class WorkerLivenessMonitor(
    WorkerRegistry registry,
    TickerNetHubOptions options,
    ILogger<WorkerLivenessMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromTicks(Math.Clamp(options.WorkerTimeout.Ticks / 3, TimeSpan.FromSeconds(1).Ticks, TimeSpan.FromSeconds(10).Ticks));
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                var cutoff = DateTime.UtcNow - options.WorkerTimeout;
                foreach (var connection in registry.All())
                {
                    if (connection.LastSeenAt >= cutoff) continue;
                    logger.LogWarning(
                        "Dropping worker {WorkerId} of node {Node}: nothing received for more than {Seconds} seconds",
                        connection.WorkerId, connection.NodeName, options.WorkerTimeout.TotalSeconds);
                    registry.Unregister(connection);
                    connection.Close();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Host is stopping.
        }
    }
}

/// <summary>
/// Sets <see cref="CronTickerEntity.IsSystemPaused"/> on cron jobs for remote functions
/// (<c>function@node</c>) while no worker that declares the function can take runs, and clears it
/// when one can. Runs after every connect, disconnect or drain (debounced) and every minute as a
/// safety net. Cron jobs for local functions, including local names that contain <c>@</c>, are never
/// paused. With <see cref="TickerNetHubOptions.PauseCronTickersWhileOffline"/> off, it clears any
/// pause once at startup and stops.
/// </summary>
internal sealed class WorkerPauseMonitor<TTimeTicker, TCronTicker>(
    WorkerRegistry registry,
    RemoteFunctionCatalog catalog,
    TickerNetHubOptions options,
    CronWriteGate writeGate,
    IServiceScopeFactory scopeFactory,
    ILogger<WorkerPauseMonitor<TTimeTicker, TCronTicker>> logger) : BackgroundService
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Sweep = TimeSpan.FromSeconds(60);

    private CancellationTokenSource? _pending;
    private CancellationToken _stopping;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stopping = stoppingToken;
        if (!options.PauseCronTickersWhileOffline)
        {
            try { await ReconcileAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            return;
        }

        registry.Changed += OnRegistryChanged;
        try
        {
            await ReconcileAsync(stoppingToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(Sweep);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                await ReconcileAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Host is stopping.
        }
        finally
        {
            registry.Changed -= OnRegistryChanged;
        }
    }

    private void OnRegistryChanged()
    {
        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _pending, next);
        previous?.Cancel();
        previous?.Dispose();

        _ = Task.Run(async () =>
        {
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(next.Token, _stopping);
                await Task.Delay(Debounce, linked.Token).ConfigureAwait(false);
                await ReconcileAsync(_stopping).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // A newer change came in, or the host is stopping.
            }
            catch (ObjectDisposedException)
            {
                // Superseded by a newer change.
            }
        });
    }

    private bool ShouldPause(string function)
    {
        if (!options.PauseCronTickersWhileOffline || !RemoteFunctionName.IsRemote(function)) return false;

        // A local function whose name happens to contain '@' runs here, not on a worker.
        if (TickerFunctionProvider.TickerFunctions.ContainsKey(function) && !catalog.IsPublished(function)) return false;

        return !registry.CanRun(RemoteFunctionName.GetNodeName(function), RemoteFunctionName.GetFunctionName(function));
    }

    internal async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        await writeGate.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var persistence = scope.ServiceProvider.GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>();

            // Only remote functions (function@node) are ever paused.
            var crons = await persistence.CronTickersQuery()
                .Where(c => c.Function != null && c.Function.Contains("@"))
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);
            var changed = new List<TCronTicker>();
            foreach (var cron in crons)
            {
                var shouldPause = ShouldPause(cron.Function);
                if (cron.IsSystemPaused == shouldPause) continue;
                cron.IsSystemPaused = shouldPause;
                changed.Add(cron);
            }

            if (changed.Count == 0) return;

            await persistence.UpdateCronTickers(changed.ToArray(), cancellationToken).ConfigureAwait(false);
            scope.ServiceProvider.GetService<ITickerNetHostScheduler>()?.Restart();
            logger.LogInformation(
                "Paused {Paused} and resumed {Resumed} cron jobs for worker nodes",
                changed.Count(c => c.IsSystemPaused), changed.Count(c => !c.IsSystemPaused));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Couldn't update paused cron jobs for worker nodes");
        }
        finally
        {
            writeGate.Gate.Release();
        }
    }
}
