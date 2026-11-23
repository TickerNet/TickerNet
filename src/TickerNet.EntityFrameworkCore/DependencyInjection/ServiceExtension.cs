using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using TickerNet.Utilities;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Instrumentation;
using TickerNet.Utilities.Interfaces.Managers;
using TickerNet.Utilities.Managers;
using TickerNet.Utilities.Interfaces;

namespace TickerNet.EntityFrameworkCore.DependencyInjection;

public static class ServiceExtension
{
        
    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddOperationalStore<TTimeTicker, TCronTicker>(this TickerOptionsBuilder<TTimeTicker,TCronTicker> tickerConfiguration, Action<TickerNetEfCoreOptionBuilder<TTimeTicker, TCronTicker>> efConfiguration = null)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var efCoreOptionBuilder = new TickerNetEfCoreOptionBuilder<TTimeTicker, TCronTicker>();

        efConfiguration?.Invoke(efCoreOptionBuilder);
            
        if (efCoreOptionBuilder.PoolSize <= 0) 
            throw new ArgumentOutOfRangeException(nameof(efCoreOptionBuilder.PoolSize), "Pool size must be greater than 0");
            
        tickerConfiguration.ExternalProviderConfigServiceAction += (services) 
            => services.AddSingleton(_ => efCoreOptionBuilder);
            
        tickerConfiguration.ExternalProviderConfigServiceAction += efCoreOptionBuilder.ConfigureServices;
            
        UseApplicationService(tickerConfiguration, efCoreOptionBuilder);
            
        return tickerConfiguration;
    }
        
    private static void UseApplicationService<TTimeTicker, TCronTicker>(TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration, TickerNetEfCoreOptionBuilder<TTimeTicker, TCronTicker> options)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        tickerConfiguration.UseExternalProviderApplication((serviceProvider) =>
        {
            var internalTickerManager = serviceProvider.GetRequiredService<IInternalTickerManager>();
            var hostLifetime = serviceProvider.GetService<IHostApplicationLifetime>();
            var schedulerOptions = serviceProvider.GetService<SchedulerOptionsBuilder>();
            var hostScheduler = serviceProvider.GetService<ITickerNetHostScheduler>();

            hostLifetime.ApplicationStarted.Register(() =>
            {
                Task.Run(async () =>
                {
                    // Release resources held by dead nodes before the scheduler starts processing.
                    await internalTickerManager.ReleaseDeadNodeResources(schedulerOptions.NodeIdentifier);
                                        
                    // After cleanup, restart the host scheduler so it immediately
                    // picks up newly seeded cron tickers and jobs configured via the core pipeline.
                    if (hostScheduler != null && hostScheduler.IsRunning)
                    {
                        hostScheduler.Restart();
                    }
                });
            });
        });
    }
}
