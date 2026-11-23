using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TickerNet.Utilities;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Interfaces;
using TickerNet.Worker.Internal;

namespace TickerNet.Worker;

/// <summary>
/// Sets up an app as a TickerNet worker node.
/// </summary>
public static class TickerNetWorkerServiceExtensions
{
    /// <summary>
    /// Makes this app a worker for a scheduler that uses TickerNet.Hub. The app connects to the hub,
    /// declares its <c>[TickerFunction]</c> functions, and runs them when the scheduler sends them.
    /// It doesn't poll or store jobs itself: jobs it adds through <c>ITimeTickerManager</c> or
    /// <c>ICronTickerManager</c> are saved on the scheduler as <c>function@node</c>. Don't add a job
    /// store (EF Core, Redis, MongoDB) to a worker. Also call <c>UseTickerNet()</c> as usual.
    /// </summary>
    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddTickerNetWorker<TTimeTicker, TCronTicker>(
        this TickerOptionsBuilder<TTimeTicker, TCronTicker> builder,
        Action<TickerNetWorkerOptions> configure)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TickerNetWorkerOptions();
        configure(options);
        options.Validate();

        builder.DisableBackgroundServices();
        builder.IgnoreSeedDefinedCronTickers();

        builder.ExternalProviderConfigServiceAction += services =>
        {
            services.AddSingleton(options);
            services.AddSingleton<WorkerClient>();
            services.AddHostedService(provider => provider.GetRequiredService<WorkerClient>());

            services.RemoveAll<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>();
            services.AddSingleton<ITickerPersistenceProvider<TTimeTicker, TCronTicker>, RemotePersistenceProvider<TTimeTicker, TCronTicker>>();
        };

        return builder;
    }
}
