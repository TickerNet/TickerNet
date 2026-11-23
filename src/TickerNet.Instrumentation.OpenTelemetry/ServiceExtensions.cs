using Microsoft.Extensions.DependencyInjection;
using TickerNet.Utilities;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Instrumentation;

namespace TickerNet.Instrumentation.OpenTelemetry
{
    public static class ServiceExtensions
    {
        /// <summary>
        /// Adds OpenTelemetry instrumentation with activity tracing for TickerNet jobs.
        /// Also includes standard logging through ILogger.
        /// </summary>
        public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddOpenTelemetryInstrumentation<TTimeTicker, TCronTicker>(
            this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            where TCronTicker : CronTickerEntity, new()
        {
            tickerConfiguration.ExternalProviderConfigServiceAction += services =>
            {
                // Replace any existing instrumentation with OpenTelemetry version
                services.AddSingleton<ITickerNetInstrumentation, OpenTelemetryInstrumentation>();
            };

            return tickerConfiguration;
        }
    }
}
