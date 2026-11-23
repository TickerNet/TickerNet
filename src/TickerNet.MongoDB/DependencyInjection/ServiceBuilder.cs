using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;
using TickerNet.MongoDB.Indexes;
using TickerNet.MongoDB.Infrastructure;
using TickerNet.MongoDB.Serialization;
using TickerNet.Utilities;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Interfaces;

namespace TickerNet.MongoDB.DependencyInjection
{
    internal static class ServiceBuilder
    {
        internal static void UseOwnedClient<TTimeTicker, TCronTicker>(
            TickerNetMongoOptionBuilder<TTimeTicker, TCronTicker> builder,
            string connectionString,
            string databaseName)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            where TCronTicker : CronTickerEntity, new()
        {
            builder.ConfigureServices = services =>
            {
                TickerClassMaps.RegisterOnce<TTimeTicker, TCronTicker>();

                services.TryAddSingleton<IMongoClient>(_ => new MongoClient(connectionString));
                services.TryAddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));
                RegisterShared<TTimeTicker, TCronTicker>(services, builder.CollectionPrefix);
            };
        }

        internal static void UseExistingClient<TTimeTicker, TCronTicker>(
            TickerNetMongoOptionBuilder<TTimeTicker, TCronTicker> builder,
            string databaseName)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            where TCronTicker : CronTickerEntity, new()
        {
            builder.ConfigureServices = services =>
            {
                TickerClassMaps.RegisterOnce<TTimeTicker, TCronTicker>();

                services.TryAddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));
                RegisterShared<TTimeTicker, TCronTicker>(services, builder.CollectionPrefix);
            };
        }

        private static void RegisterShared<TTimeTicker, TCronTicker>(IServiceCollection services, string collectionPrefix)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            where TCronTicker : CronTickerEntity, new()
        {
            services.AddSingleton<ITickerMongoContext<TTimeTicker, TCronTicker>>(sp =>
                new TickerMongoContext<TTimeTicker, TCronTicker>(
                    sp.GetRequiredService<IMongoDatabase>(),
                    collectionPrefix));

            services.AddSingleton<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>(sp =>
                new TickerMongoPersistenceProvider<TTimeTicker, TCronTicker>(
                    sp.GetRequiredService<ITickerMongoContext<TTimeTicker, TCronTicker>>(),
                    sp.GetRequiredService<ITickerClock>(),
                    sp.GetRequiredService<SchedulerOptionsBuilder>()));

            services.AddHostedService(sp => new TickerIndexProvisioner<TTimeTicker, TCronTicker>(
                sp.GetRequiredService<ITickerMongoContext<TTimeTicker, TCronTicker>>()));
        }
    }
}
