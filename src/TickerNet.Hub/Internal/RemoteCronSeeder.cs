using Microsoft.Extensions.DependencyInjection;
using TickerNet.Hub.Protocol.V1;
using TickerNet.Utilities;
using TickerNet.Utilities.Interfaces;
using TickerNet.Utilities.Interfaces.Managers;

namespace TickerNet.Hub.Internal;

/// <summary>
/// Serializes the hub's writes to cron jobs (seeding and pausing), so one can't overwrite the
/// other with a stale copy of the row.
/// </summary>
internal sealed class CronWriteGate
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
}

/// <summary>
/// Creates or updates cron jobs for worker functions that declare a cron expression, the same way
/// the scheduler seeds its own <c>[TickerFunction]</c> cron jobs.
/// </summary>
/// <remarks>
/// It never deletes. A replica on another version, or one started without its functions, mustn't
/// remove cron jobs (and their history) that other replicas still serve. A seeded cron job whose
/// function no worker declares any more stays paused (see <see cref="WorkerPauseMonitor{TTimeTicker,TCronTicker}"/>);
/// remove it from the dashboard when it is no longer wanted.
/// </remarks>
internal sealed class RemoteCronSeeder(IServiceScopeFactory scopeFactory, CronWriteGate writeGate)
{
    public async Task SeedAsync(string nodeName, IReadOnlyList<FunctionDescriptor> functions, CancellationToken cancellationToken)
    {
        var declared = functions
            .Where(f => !string.IsNullOrWhiteSpace(f.CronExpression))
            .Select(f => (Function: RemoteFunctionName.Qualify(f.Name, nodeName), Expression: f.CronExpression))
            .ToArray();

        if (declared.Length == 0) return;

        await writeGate.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var services = scope.ServiceProvider;
            await services.GetRequiredService<IInternalTickerManager>()
                .MigrateDefinedCronTickers(declared, cancellationToken).ConfigureAwait(false);
            services.GetService<ITickerNetHostScheduler>()?.Restart();
        }
        finally
        {
            writeGate.Gate.Release();
        }
    }
}
