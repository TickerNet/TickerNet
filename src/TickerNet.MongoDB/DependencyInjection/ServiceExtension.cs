using System;
using TickerNet.Utilities;
using TickerNet.Utilities.Entities;

namespace TickerNet.MongoDB.DependencyInjection
{
    public static class ServiceExtension
    {
        /// <summary>
        /// Registers the MongoDB persistence provider for TickerNet. Call this from
        /// inside <c>AddTickerNet(options =&gt; ...)</c>.
        /// </summary>
        public static TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity> AddOperationalStore(
            this TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity> tickerConfiguration,
            Action<TickerNetMongoOptionBuilder<TimeTickerEntity, CronTickerEntity>> mongoConfiguration = null)
            => AddOperationalStore<TimeTickerEntity, CronTickerEntity>(tickerConfiguration, mongoConfiguration);

        public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddOperationalStore<TTimeTicker, TCronTicker>(
            this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration,
            Action<TickerNetMongoOptionBuilder<TTimeTicker, TCronTicker>> mongoConfiguration = null)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            where TCronTicker : CronTickerEntity, new()
        {
            var optionBuilder = new TickerNetMongoOptionBuilder<TTimeTicker, TCronTicker>();
            mongoConfiguration?.Invoke(optionBuilder);

            if (optionBuilder.ConfigureServices is null)
                throw new InvalidOperationException(
                    "TickerNet.MongoDB: you must call UseTickerNetMongoClient(...) or UseExistingMongoClient(...) on the option builder.");

            tickerConfiguration.ExternalProviderConfigServiceAction += optionBuilder.ConfigureServices;
            return tickerConfiguration;
        }
    }
}
