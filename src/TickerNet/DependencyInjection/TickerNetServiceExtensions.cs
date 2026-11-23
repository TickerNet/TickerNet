using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TickerNet.BackgroundServices;
using TickerNet.Dispatcher;
using TickerNet.Provider;
using TickerNet.TickerNetThreadPool;
using TickerNet.Utilities;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Enums;
using TickerNet.Utilities.Infrastructure;
using TickerNet.Utilities.Instrumentation;
using TickerNet.Utilities.Interfaces;
using TickerNet.Utilities.Interfaces.Managers;
using TickerNet.Utilities.Managers;
using TickerNet.Utilities.Temps;

namespace TickerNet.DependencyInjection
{
    public static class TickerNetServiceExtensions
    {
        public static IServiceCollection AddTickerNet(this IServiceCollection services, Action<TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>> optionsBuilder = null)
            => AddTickerNet<TimeTickerEntity, CronTickerEntity>(services, optionsBuilder);

        public static IServiceCollection AddTickerNet<TTimeTicker, TCronTicker>(this IServiceCollection services, Action<TickerOptionsBuilder<TTimeTicker, TCronTicker>> optionsBuilder = null)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            where TCronTicker : CronTickerEntity, new()
        {
            var tickerExecutionContext = new TickerExecutionContext();
            var schedulerOptionsBuilder = new SchedulerOptionsBuilder();
            var optionInstance = new TickerOptionsBuilder<TTimeTicker,TCronTicker>(tickerExecutionContext, schedulerOptionsBuilder);
            optionsBuilder?.Invoke(optionInstance);
            CronScheduleCache.TimeZoneInfo = schedulerOptionsBuilder.SchedulerTimeZone;

            // Apply JSON serializer options for ticker requests if configured during service registration
            if (optionInstance.RequestJsonSerializerOptions != null)
            {
                TickerHelper.RequestJsonSerializerOptions = optionInstance.RequestJsonSerializerOptions;
            }

            // Configure whether ticker request payloads should use GZip compression
            TickerHelper.UseGZipCompression = optionInstance.RequestGZipCompressionEnabled;
            services.AddSingleton<ITimeTickerManager<TTimeTicker>, TickerManager<TTimeTicker, TCronTicker>>();
            services.AddSingleton<ICronTickerManager<TCronTicker>, TickerManager<TTimeTicker, TCronTicker>>();
            services.AddSingleton<IInternalTickerManager, InternalTickerManager<TTimeTicker, TCronTicker>>();
            services.AddSingleton<ITickerNetRedisContext, NoOpTickerNetRedisContext>();
            services.AddSingleton<ITickerNetNotificationHubSender, NoOpTickerNetNotificationHubSender>();
            services.AddSingleton<ITickerClock, TickerSystemClock>();

            // Register the initializer hosted service BEFORE scheduler services
            // to guarantee seeding completes before the scheduler starts polling.
            // Registered as a singleton so UseTickerNet can resolve it to set the initialization flag.
            services.AddSingleton<TickerNetInitializerHostedService>();
            services.AddHostedService(sp => sp.GetRequiredService<TickerNetInitializerHostedService>());

            // Only register background services if enabled (default is true)
            if (optionInstance.RegisterBackgroundServices)
            {
                services.AddSingleton<TickerNetSchedulerBackgroundService>();
                services.AddSingleton<ITickerNetHostScheduler>(provider =>
                    provider.GetRequiredService<TickerNetSchedulerBackgroundService>());
                services.AddHostedService(provider =>
                    provider.GetRequiredService<TickerNetSchedulerBackgroundService>());
                services.AddHostedService(provider => provider.GetRequiredService<TickerNetFallbackBackgroundService>());
                services.AddSingleton<TickerNetFallbackBackgroundService>();
                services.AddSingleton<ITickerNetDispatcher, TickerNetDispatcher>();
                services.AddSingleton<ITickerNetTaskScheduler>(sp =>
                {
                    var notification = sp.GetRequiredService<ITickerNetNotificationHubSender>();
                    var notifyDebounce = new SoftSchedulerNotifyDebounce((value) => notification.UpdateActiveThreads(value));
                    return new TickerNetTaskScheduler(schedulerOptionsBuilder.MaxConcurrency, schedulerOptionsBuilder.IdleWorkerTimeOut, notifyDebounce);
                });
            }
            else
            {
                services.AddSingleton<ITickerNetTaskScheduler>(_ => new TickerNetTaskScheduler(schedulerOptionsBuilder.MaxConcurrency, schedulerOptionsBuilder.IdleWorkerTimeOut));
                // Register NoOp implementations when background services are disabled
                services.AddSingleton<ITickerNetHostScheduler, NoOpTickerNetHostScheduler>();
                services.AddSingleton<ITickerNetDispatcher, NoOpTickerNetDispatcher>();
            }
            services.AddSingleton<ITickerFunctionConcurrencyGate, TickerFunctionConcurrencyGate>();
            services.AddSingleton<ITickerExecutionTaskHandler, TickerExecutionTaskHandler>();
            services.AddSingleton<ITickerNetInstrumentation, LoggerInstrumentation>();
            services.AddSingleton<ITickerDashboardDataService<TTimeTicker, TCronTicker>, TickerDashboardDataService<TTimeTicker, TCronTicker>>();

            optionInstance.ExternalProviderConfigServiceAction?.Invoke(services);

            // Register in-memory persistence as fallback — only used when no external provider
            // (EF Core, Redis, SDK) has been configured via ExternalProviderConfigServiceAction above.
            services.TryAddSingleton<ITickerPersistenceProvider<TTimeTicker, TCronTicker>, TickerInMemoryPersistenceProvider<TTimeTicker, TCronTicker>>();

            optionInstance.DashboardServiceAction?.Invoke(services);

            if (optionInstance.TickerExceptionHandlerType != null)
                services.AddSingleton(typeof(ITickerExceptionHandler), optionInstance.TickerExceptionHandlerType);

            services.AddSingleton(_ => optionInstance);
            services.AddSingleton(_ => tickerExecutionContext);
            services.AddSingleton(_ => schedulerOptionsBuilder);

            // Register AFTER initializer and scheduler to ensure it runs last
            services.AddHostedService<TickerNetStartupValidator>();

            return services;
        }

        /// <summary>
        /// Initializes TickerNet for generic host applications (Console, MAUI, WPF, Worker Services, etc.).
        ///
        /// This method configures middleware and scheduler settings. All I/O-bound
        /// initialization (function discovery, database seeding, external provider startup)
        /// is deferred to <see cref="TickerNetInitializerHostedService"/> which runs when
        /// the host starts. This means design-time tools that build the host without
        /// starting it (OpenAPI generators, EF migration tools, etc.) will not trigger
        /// database operations.
        /// </summary>
        public static IHost UseTickerNet(this IHost host, TickerNetStartMode qStartMode = TickerNetStartMode.Immediate)
        {
            var serviceProvider = host.Services;
            var tickerExecutionContext = serviceProvider.GetService<TickerExecutionContext>();
            var notificationHubSender = serviceProvider.GetService<ITickerNetNotificationHubSender>();
            var backgroundScheduler = serviceProvider.GetService<TickerNetSchedulerBackgroundService>();

            // Signal the initializer hosted service that UseTickerNet was called,
            // so it should perform startup I/O when the host starts.
            var initializer = serviceProvider.GetService<TickerNetInitializerHostedService>();
            if (initializer != null)
                initializer.InitializationRequested = true;

            // If background services are registered, configure them
            if (backgroundScheduler != null)
            {
                backgroundScheduler.SkipFirstRun = qStartMode == TickerNetStartMode.Manual;

                tickerExecutionContext.NotifyCoreAction += (value, type) =>
                {
                    if (type == CoreNotifyActionType.NotifyHostExceptionMessage)
                    {
                        notificationHubSender.UpdateHostException((string)value);
                        tickerExecutionContext.LastHostExceptionMessage = (string)value;
                    }
                    else if (type == CoreNotifyActionType.NotifyNextOccurence)
                        notificationHubSender.UpdateNextOccurrence(value is DateTime dt ? (DateTime?)dt : null);
                    else if (type == CoreNotifyActionType.NotifyHostStatus)
                        notificationHubSender.UpdateHostStatus(value is bool b && b);
                    else if (type == CoreNotifyActionType.NotifyThreadCount)
                        notificationHubSender.UpdateActiveThreads((string)value);
                };
            }
            // If background services are not registered (due to DisableBackgroundServices()),
            // silently skip background service configuration. This is expected behavior.

            if (tickerExecutionContext?.DashboardApplicationAction != null)
            {
                // Cast object back to IApplicationBuilder for Dashboard middleware
                tickerExecutionContext.DashboardApplicationAction(host);
                tickerExecutionContext.DashboardApplicationAction = null;
            }

            return host;
        }
    }
}
