using System;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using TickerNet.Caching.StackExchangeRedis.Infrastructure;
using TickerNet.Utilities;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Interfaces;

namespace TickerNet.Caching.StackExchangeRedis.DependencyInjection;

public static class ServiceExtension
{
    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddStackExchangeRedis<TTimeTicker, TCronTicker>(
        this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration, Action<TickerNetRedisOptionBuilder> setupAction)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        tickerConfiguration.ExternalProviderConfigServiceAction += services =>
        {
            var options = new TickerNetRedisOptionBuilder
            {
                InstanceName = "tickernet:"
            };
            
            setupAction?.Invoke(options);
            if (string.IsNullOrWhiteSpace(options.Configuration)
                && options.ConfigurationOptions == null
                && options.ConnectionMultiplexer == null
                && options.ConnectionMultiplexerFactory == null)
                throw new InvalidOperationException("Redis configuration or connection must be provided when enabling StackExchange.Redis persistence.");

            services.AddKeyedSingleton<IConnectionMultiplexer>("tickernet", (_, _) =>
                options.ConnectionMultiplexer
                ?? (options.ConnectionMultiplexerFactory != null
                    ? options.ConnectionMultiplexerFactory().GetAwaiter().GetResult()
                    : (options.ConfigurationOptions != null
                        ? ConnectionMultiplexer.Connect(options.ConfigurationOptions)
                        : ConnectionMultiplexer.Connect(options.Configuration))));
            services.AddKeyedSingleton<IDatabase>("tickernet", (sp, _) =>
                sp.GetRequiredKeyedService<IConnectionMultiplexer>("tickernet").GetDatabase());
            services.AddHostedService<NodeHeartBeatBackgroundService>();
            services.AddSingleton<ITickerNetRedisContext, TickerNetRedisContext>();
            services.AddKeyedSingleton<IDistributedCache>("tickernet", (sp, _) =>
                new RedisCache(new RedisCacheOptions
                {
                    InstanceName = options.InstanceName,
                    ConnectionMultiplexerFactory = () => Task.FromResult(
                        sp.GetRequiredKeyedService<IConnectionMultiplexer>("tickernet")),
                }));
            services.AddSingleton(_ => options);
            services.TryAddSingleton<ITickerPersistenceProvider<TTimeTicker, TCronTicker>, TickerRedisPersistenceProvider<TTimeTicker, TCronTicker>>();
        };

        return tickerConfiguration;
    }

    public class TickerNetRedisOptionBuilder : RedisCacheOptions
    {
        public TimeSpan NodeHeartbeatInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// An existing <see cref="IConnectionMultiplexer"/> to use for all Redis operations.
        /// When set, <see cref="RedisCacheOptions.Configuration"/> and
        /// <see cref="RedisCacheOptions.ConfigurationOptions"/> are ignored.
        /// The caller is responsible for the lifetime and disposal of the multiplexer.
        /// </summary>
        public IConnectionMultiplexer ConnectionMultiplexer { get; set; }

        /// <summary>
        /// Optional source-generated JsonSerializerContext for AOT compatibility.
        /// When provided, enables trimming-safe JSON serialization for ticker entities.
        /// The context should include [JsonSerializable] attributes for your TTimeTicker,
        /// TCronTicker, and CronTickerOccurrenceEntity&lt;TCronTicker&gt; types.
        /// </summary>
        public JsonSerializerContext JsonSerializerContext { get; set; }
    }
}
