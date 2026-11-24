using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Instrumentation;
using TickerNet.Utilities.Interfaces;
using TickerNet.Utilities.Interfaces.Managers;

namespace TickerNet.Utilities
{
    /// <summary>
    /// Configures TickerNet's scheduler, seeding, serialization and telemetry.
    /// Returned by <c>AddTickerNet(options =&gt; ...)</c>; every method returns the
    /// same builder so calls can be chained.
    /// </summary>
    public class TickerOptionsBuilder<TTimeTicker, TCronTicker> : ITickerOptionsSeeding
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        private readonly TickerExecutionContext _tickerExecutionContext;
        private readonly SchedulerOptionsBuilder _schedulerOptions;

        internal TickerOptionsBuilder(TickerExecutionContext tickerExecutionContext, SchedulerOptionsBuilder schedulerOptions)
        {
            _tickerExecutionContext = tickerExecutionContext;
            _schedulerOptions = schedulerOptions;
            // Store this instance in the execution context for later retrieval
            tickerExecutionContext.OptionsSeeding = this;
        }

        /// <summary>
        /// Internal flag for request GZip compression.
        /// Defaults to false (plain JSON bytes).
        /// </summary>
        internal bool RequestGZipCompressionEnabled { get; set; } = false;

        /// <summary>
        /// Controls whether code-defined cron tickers are seeded on startup.
        /// Defaults to true.
        /// </summary>
        internal bool SeedDefinedCronTickers { get; set; } = true;

        /// <summary>
        /// Controls whether background services (job processors) should be registered.
        /// Defaults to true. Set to false to only register managers for queuing jobs.
        /// </summary>
        internal bool RegisterBackgroundServices { get; set; } = true;

        /// <summary>
        /// Seeding delegate for time tickers, executed with the application's service provider.
        /// </summary>
        internal Func<IServiceProvider, System.Threading.Tasks.Task> TimeSeederAction { get; set; }

        /// <summary>
        /// Seeding delegate for cron tickers, executed with the application's service provider.
        /// </summary>
        internal Func<IServiceProvider, System.Threading.Tasks.Task> CronSeederAction { get; set; }

        // Explicit interface implementation for ITickerOptionsSeeding
        bool ITickerOptionsSeeding.SeedDefinedCronTickers => SeedDefinedCronTickers;
        Func<IServiceProvider, System.Threading.Tasks.Task> ITickerOptionsSeeding.TimeSeederAction => TimeSeederAction;
        Func<IServiceProvider, System.Threading.Tasks.Task> ITickerOptionsSeeding.CronSeederAction => CronSeederAction;

        internal Action<IServiceCollection> ExternalProviderConfigServiceAction { get; set; }
        internal Action<IServiceCollection> DashboardServiceAction { get; set; }
        internal Type TickerExceptionHandlerType { get; private set; }

        /// <summary>
        /// Changes the scheduler settings on <see cref="SchedulerOptionsBuilder"/>.
        /// </summary>
        /// <param name="schedulerOptionsBuilder">Receives the scheduler options to change.</param>
        /// <returns>The same builder, for chaining.</returns>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> ConfigureScheduler(Action<SchedulerOptionsBuilder> schedulerOptionsBuilder)
        {
            schedulerOptionsBuilder?.Invoke(_schedulerOptions);
            return this;
        }

        /// <summary>
        /// Gets or sets the minimum interval between database polls.
        /// Prevents tight loops when tasks are due or when the database is empty.
        /// </summary>
        public TimeSpan MinPollingInterval
        {
            get => _schedulerOptions.MinPollingInterval;
            set => _schedulerOptions.MinPollingInterval = value;
        }

        /// <summary>
        /// JsonSerializerOptions specifically for serializing/deserializing ticker requests.
        /// If not set, default JsonSerializerOptions will be used.
        /// </summary>
        internal JsonSerializerOptions RequestJsonSerializerOptions { get; set; }

        /// <summary>
        /// Configures a JsonSerializerContext for AOT-compatible ticker request serialization/deserialization.
        /// Use this when publishing with Native AOT or when trimming is enabled.
        /// </summary>
        /// <param name="context">The JsonSerializerContext that includes all TRequest types used in ticker functions.</param>
        /// <returns>The TickerOptionsBuilder for method chaining</returns>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> WithJsonContext(IJsonTypeInfoResolver context)
        {
            RequestJsonSerializerOptions = new JsonSerializerOptions
            {
                TypeInfoResolver = context
            };
            return this;
        }

        /// <summary>
        /// Configures the JSON serialization options specifically for ticker request serialization/deserialization.
        /// </summary>
        /// <param name="configure">Action to configure JsonSerializerOptions for ticker requests</param>
        /// <returns>The TickerOptionsBuilder for method chaining</returns>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> ConfigureRequestJsonOptions(Action<JsonSerializerOptions> configure)
        {
            RequestJsonSerializerOptions ??= new JsonSerializerOptions();
            configure?.Invoke(RequestJsonSerializerOptions);
            return this;
        }

        /// <summary>
        /// Enables GZip compression for ticker request payloads.
        /// When not called, requests are stored as plain UTF-8 JSON bytes.
        /// </summary>
        /// <returns>The TickerOptionsBuilder for method chaining</returns>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> UseGZipCompression()
        {
            RequestGZipCompressionEnabled = true;
            return this;
        }

        /// <summary>
        /// Enables skipping of stale cron occurrences on application startup.
        /// Idle/Queued occurrences whose execution time is further behind the current
        /// time than <paramref name="threshold"/> are marked <c>Skipped</c>, preventing
        /// duplicate executions after a restart (see #776).
        /// </summary>
        /// <param name="threshold">
        /// How far behind the current time an occurrence must be to be considered stale.
        /// Defaults to 5 seconds when not specified.
        /// </param>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> SkipStaleCronOccurrencesOnStartup(TimeSpan? threshold = null)
        {
            _schedulerOptions.StaleCronOccurrenceThreshold = threshold ?? TimeSpan.FromSeconds(5);
            return this;
        }

        /// <summary>
        /// Disable automatic seeding of code-defined cron tickers on startup.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> IgnoreSeedDefinedCronTickers()
        {
            SeedDefinedCronTickers = false;
            return this;
        }

        /// <summary>
        /// Disables background services registration.
        /// Use this when you only want to queue jobs without processing them in this application.
        /// Only the managers (ITimeTickerManager, ICronTickerManager) will be available for queuing jobs.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> DisableBackgroundServices()
        {
            RegisterBackgroundServices = false;
            return this;
        }

        /// <summary>
        /// Creates an ActivitySource named "TickerNet" with activity tracing for TickerNet jobs.
        /// Also includes standard logging through ILogger.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> EnableActivitySource()
        {
            ExternalProviderConfigServiceAction += services =>
            {
              // Replace any existing instrumentation with OpenTelemetry version
              services.AddSingleton<ITickerNetInstrumentation, ActivitySourceInstrumentation>();
            };
            return this;
        }

        /// <summary>
        /// Configure a custom seeder for time tickers, executed on application startup.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> UseTickerSeeder(
            Func<ITimeTickerManager<TTimeTicker>, System.Threading.Tasks.Task> timeSeeder)
        {
            if (timeSeeder == null) return this;

            TimeSeederAction = async sp =>
            {
                var manager = sp.GetRequiredService<ITimeTickerManager<TTimeTicker>>();
                await timeSeeder(manager).ConfigureAwait(false);
            };

            return this;
        }

        /// <summary>
        /// Configure a custom seeder for cron tickers, executed on application startup.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> UseTickerSeeder(
            Func<ICronTickerManager<TCronTicker>, System.Threading.Tasks.Task> cronSeeder)
        {
            if (cronSeeder == null) return this;

            CronSeederAction = async sp =>
            {
                var manager = sp.GetRequiredService<ICronTickerManager<TCronTicker>>();
                await cronSeeder(manager).ConfigureAwait(false);
            };

            return this;
        }

        /// <summary>
        /// Configure custom seeders for both time and cron tickers, executed on application startup.
        /// </summary>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> UseTickerSeeder(
            Func<ITimeTickerManager<TTimeTicker>, System.Threading.Tasks.Task> timeSeeder,
            Func<ICronTickerManager<TCronTicker>, System.Threading.Tasks.Task> cronSeeder)
        {
            UseTickerSeeder(timeSeeder);
            UseTickerSeeder(cronSeeder);
            return this;
        }

        /// <summary>
        /// Registers an <see cref="ITickerExceptionHandler"/> that is called when a job
        /// fails or is cancelled.
        /// </summary>
        /// <typeparam name="THandler">The handler type to resolve from the service provider.</typeparam>
        /// <returns>The same builder, for chaining.</returns>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> SetExceptionHandler<THandler>() where THandler : ITickerExceptionHandler
        {
            TickerExceptionHandlerType = typeof(THandler);
            return this;
        }

        internal void UseExternalProviderApplication(Action<IServiceProvider> action)
            => _tickerExecutionContext.ExternalProviderApplicationAction = action;

        internal void UseDashboardApplication(Action<object> action)
            => _tickerExecutionContext.DashboardApplicationAction = action;
    }

    /// <summary>
    /// Scheduler settings, changed through
    /// <see cref="TickerOptionsBuilder{TTimeTicker,TCronTicker}.ConfigureScheduler"/>.
    /// </summary>
    public class SchedulerOptionsBuilder
    {
        /// <summary>
        /// This node's name. It is written as the lock holder on jobs the node takes,
        /// and used for heartbeats. Defaults to <see cref="Environment.MachineName"/>.
        /// </summary>
        public string NodeIdentifier { get; set; } = Environment.MachineName;

        /// <summary>
        /// Number of worker threads that run jobs. Defaults to
        /// <see cref="Environment.ProcessorCount"/>.
        /// </summary>
        public int MaxConcurrency { get; set; } = Environment.ProcessorCount;

        /// <summary>
        /// How long an idle worker thread waits before it stops. Defaults to one minute.
        /// </summary>
        public TimeSpan IdleWorkerTimeOut { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// How often a second service looks for jobs that are past due and still
        /// waiting. Defaults to 30 seconds.
        /// </summary>
        public TimeSpan FallbackIntervalChecker { get; set; } = TimeSpan.FromSeconds(30);
        /// <summary>
        /// Gets or sets the minimum interval between database polls.
        /// Prevents tight loops when tasks are due or when the database is empty.
        /// </summary>
        public TimeSpan MinPollingInterval { get; set; } = TimeSpan.FromSeconds(1);
        /// <summary>
        /// How far behind the current time an Idle/Queued cron occurrence must be
        /// before it is marked <c>Skipped</c> on startup. Occurrences whose
        /// <c>ExecutionTime</c> is closer to the current time than this threshold
        /// are left for normal scheduling to pick up.
        /// <para>
        /// Disabled by default (<see cref="TimeSpan.Zero"/>). Enable via
        /// <c>SkipStaleCronOccurrencesOnStartup()</c> on the options builder.
        /// </para>
        /// </summary>
        public TimeSpan StaleCronOccurrenceThreshold { get; set; } = TimeSpan.Zero;
        /// <summary>
        /// Time zone for cron expressions, and for execution times given with
        /// <see cref="DateTimeKind.Unspecified"/>. Defaults to
        /// <see cref="TimeZoneInfo.Local"/>.
        /// </summary>
        public TimeZoneInfo SchedulerTimeZone = TimeZoneInfo.Local;
    }
}
