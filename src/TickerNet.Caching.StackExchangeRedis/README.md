# TickerNet.Caching.StackExchangeRedis

Redis support for [TickerNet](https://github.com/TickerNet/TickerNet), an open-source background job scheduler for
.NET, using StackExchange.Redis.

- **Used alone**, Redis is TickerNet's job store.
- **Used with the EF Core or MongoDB store**, that store keeps the jobs, and Redis adds node heartbeats, dead-node
  cleanup and, for EF Core, a cache of cron expressions.

## Setup

```csharp
using TickerNet.Caching.StackExchangeRedis.DependencyInjection;

builder.Services.AddTickerNet(options =>
{
    options.AddStackExchangeRedis(redis =>
    {
        redis.Configuration = builder.Configuration.GetConnectionString("Redis");
    });
});
```

Set one of `Configuration`, `ConfigurationOptions`, `ConnectionMultiplexer` or `ConnectionMultiplexerFactory`.
The options extend `RedisCacheOptions` and add `NodeHeartbeatInterval` (1 minute by default),
`ConnectionMultiplexer` (yours to dispose) and `JsonSerializerContext` (for ahead-of-time compilation).
`InstanceName` defaults to `tickernet:`. Apps moving from TickerQ can keep TickerQ's prefix with
`InstanceName = "tickerq:"`.

Read the [getting started guide](https://tickernet.github.io/TickerNet/getting-started.html) for the other stores.
