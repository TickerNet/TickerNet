using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerNet.Provider;
using TickerNet.Utilities;
using TickerNet.Utilities.Interfaces.Managers;
using TickerNet.Utilities.Instrumentation;

namespace TickerNet.BackgroundServices;

/// <summary>
/// Performs TickerNet startup initialization (function discovery, cron ticker seeding,
/// external provider setup) as part of the host lifecycle.
///
/// By running inside <see cref="IHostedService.StartAsync"/> instead of inline in
/// <c>UseTickerNet</c>, this service is naturally skipped by design-time tools
/// (OpenAPI generators, EF migrations, etc.) that build the host but never start it.
/// Registered before the scheduler services to guarantee seeding completes first.
/// </summary>
internal sealed class TickerNetInitializerHostedService : IHostedService
{
    private readonly TickerExecutionContext _executionContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Set to true by <c>UseTickerNet</c> to signal that this hosted service
    /// should perform startup I/O when the host starts. When false (default),
    /// <see cref="StartAsync"/> is a no-op — this naturally prevents initialization
    /// in design-time tool contexts where UseTickerNet is never called.
    /// </summary>
    internal bool InitializationRequested { get; set; }

    public TickerNetInitializerHostedService(
        TickerExecutionContext executionContext,
        IServiceProvider serviceProvider,
        IConfiguration configuration)
    {
        _executionContext = executionContext;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!InitializationRequested)
            return;

        TickerFunctionProvider.UpdateCronExpressionsFromIConfiguration(_configuration);
        TickerFunctionProvider.Build();

        var options = _executionContext.OptionsSeeding;

        if (options == null || options.SeedDefinedCronTickers)
        {
            await WithSeedingInstrumentation(_serviceProvider, "CronTickers",
                () => SeedDefinedCronTickers(_serviceProvider));
        }

        if (options?.TimeSeederAction != null)
        {
            await WithSeedingInstrumentation(_serviceProvider, "TimeSeeder",
                () => options.TimeSeederAction(_serviceProvider));
        }

        if (options?.CronSeederAction != null)
        {
            await WithSeedingInstrumentation(_serviceProvider, "CronSeeder",
                () => options.CronSeederAction(_serviceProvider));
        }

        // Skip stale cron occurrences that were pending before this restart.
        // This prevents the fallback service from catching up past-due occurrences
        // from a previous application lifecycle, which would cause unexpected
        // duplicate executions at startup (see #776).
        await SkipStaleCronOccurrencesAsync(_serviceProvider, cancellationToken);

        if (_executionContext.ExternalProviderApplicationAction != null)
        {
            _executionContext.ExternalProviderApplicationAction(_serviceProvider);
            _executionContext.ExternalProviderApplicationAction = null;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Runs one seeding step between the instrumentation's started/completed
    /// calls. Completed is only reported when the step succeeds; a failure is
    /// left for the host's own error handling.
    /// </summary>
    private static async Task WithSeedingInstrumentation(
        IServiceProvider serviceProvider, string seedingDataType, Func<Task> seed)
    {
        var instrumentation = serviceProvider.GetService<ITickerNetInstrumentation>();
        instrumentation?.LogSeedingDataStarted(seedingDataType);
        await seed();
        instrumentation?.LogSeedingDataCompleted(seedingDataType);
    }

    private static async Task SeedDefinedCronTickers(IServiceProvider serviceProvider)
    {
        var internalTickerManager = serviceProvider.GetRequiredService<IInternalTickerManager>();

        var functionsToSeed = TickerFunctionProvider.TickerFunctions
            .Where(x => !string.IsNullOrEmpty(x.Value.cronExpression))
            .Select(x => (x.Key, x.Value.cronExpression)).ToArray();

        await internalTickerManager.MigrateDefinedCronTickers(functionsToSeed);
    }

    private static async Task SkipStaleCronOccurrencesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var schedulerOptions = serviceProvider.GetRequiredService<SchedulerOptionsBuilder>();
        if (schedulerOptions.StaleCronOccurrenceThreshold <= TimeSpan.Zero)
            return;

        var internalTickerManager = serviceProvider.GetRequiredService<IInternalTickerManager>();
        await internalTickerManager.SkipStaleCronOccurrencesAsync(schedulerOptions.StaleCronOccurrenceThreshold, cancellationToken);
    }
}
