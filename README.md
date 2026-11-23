<h1 align="center">
  <img src="https://tickernet.net/tickernet-logo.svg" alt="TickerNet Logo" width="140" />
  <br />
  TickerNet
</h1>

<p align="center">
  <strong>The modern job scheduler for .NET</strong><br />
  Source-generated task scheduling with built-in persistence, cron & time-based execution, and real-time monitoring.
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/tickernet"><img src="https://img.shields.io/nuget/dt/tickernet.svg?style=flat-square" alt="NuGet Downloads" /></a>
  <a href="https://www.nuget.org/packages/tickernet"><img src="https://img.shields.io/nuget/vpre/tickernet.svg?style=flat-square" alt="NuGet Version" /></a>
  <a href="https://github.com/Arcenox-co/TickerNet/actions/workflows/build.yml"><img src="https://img.shields.io/github/actions/workflow/status/Arcenox-co/TickerNet/build.yml?branch=main&style=flat-square" alt="Build Status" /></a>
  <a href="https://tickernet.net"><img src="https://img.shields.io/badge/docs-tickernet.net-blue?style=flat-square" alt="Documentation" /></a>
  <a href="https://discord.gg/ZJemWvp9MK"><img src="https://img.shields.io/badge/discord-TickerNet-5865F2?style=flat-square&logo=discord&logoColor=white" alt="Discord" /></a>
  <a href="https://opencollective.com/tickernet"><img src="https://opencollective.com/tickernet/tiers/badge.svg?style=flat-square" alt="OpenCollective" /></a>
</p>

---

## Why TickerNet?

| | |
|---|---|
| **Zero reflection, AOT ready** | Source generators at compile time. No runtime reflection, no magic strings, fully trimmable. |
| **Your database** | EF Core (PostgreSQL, SQL Server, SQLite, MySQL) or Redis. No separate storage. |
| **Real-time dashboard** | Built-in SignalR dashboard. Monitor, inspect, manage — no paid add-ons. |
| **Multi-node** | Redis heartbeats, dead-node cleanup, lock-based coordination. Just add instances. |
| **Minimal setup** | `AddTickerNet()` → decorate a method → schedule. Minutes, not hours. |

## Features

- **Time & cron scheduling** — one-off and recurring jobs
- **Source-generated** — compile-time function registration for maximum performance
- **Dual persistence** — EF Core (PostgreSQL, SQL Server, SQLite, MySQL) or Redis
- **Live dashboard** — real-time UI with SignalR — [screenshots](https://tickernet.net/features/dashboard.html#dashboard-screenshots)
- **Retry & throttling** — configurable retry policies with backoff
- **Dependency injection** — first-class DI support
- **Multi-node** — distributed coordination via Redis heartbeats and dead-node cleanup
- **Hub** — centralized scheduling across applications via [TickerNet Hub](https://hub.tickernet.net)

## Quick Start

```bash
dotnet add package TickerNet
```

### 1. Register services

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTickerNet();

var app = builder.Build();
app.UseTickerNet();
app.Run();
```

### 2. Create a job

```csharp
using TickerNet.Utilities.Base;

public class MyJobs
{
    [TickerFunction("HelloWorld")]
    public async Task HelloWorld(
        TickerFunctionContext context,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Hello from TickerNet! Job ID: {context.Id}");
    }
}
```

### 3. Schedule it

```csharp
public class MyService(ITimeTickerManager<TimeTickerEntity> manager)
{
    public async Task Schedule()
    {
        await manager.AddAsync(new TimeTickerEntity
        {
            Function = "HelloWorld",
            ExecutionTime = DateTime.UtcNow.AddSeconds(10)
        });
    }
}
```

## Packages

| Package | Description |
|---------|------------|
| [`TickerNet`](https://www.nuget.org/packages/TickerNet) | Core scheduler engine |
| [`TickerNet.Utilities`](https://www.nuget.org/packages/TickerNet.Utilities) | Shared types, entities, and interfaces |
| [`TickerNet.EntityFrameworkCore`](https://www.nuget.org/packages/TickerNet.EntityFrameworkCore) | EF Core persistence provider |
| [`TickerNet.Caching.StackExchangeRedis`](https://www.nuget.org/packages/TickerNet.Caching.StackExchangeRedis) | Redis persistence and distributed coordination |
| [`TickerNet.Dashboard`](https://www.nuget.org/packages/TickerNet.Dashboard) | Real-time dashboard UI |
| [`TickerNet.Instrumentation.OpenTelemetry`](https://www.nuget.org/packages/TickerNet.Instrumentation.OpenTelemetry) | OpenTelemetry tracing |
| [`TickerNet.SourceGenerator`](https://www.nuget.org/packages/TickerNet.SourceGenerator) | Compile-time function registration |

> **Note:** All packages are versioned together. Always update all packages to the same version.

## TickerNet Hub

Centralized scheduling across applications — [hub.tickernet.net](https://hub.tickernet.net)

## Documentation

Full documentation at **[tickernet.net](https://tickernet.net)** — docs are open-source at [TickerNet-UI](https://github.com/Arcenox-co/TickerNet-UI).

## Sponsors & Backers

Support TickerNet through [OpenCollective](https://opencollective.com/tickernet).

<a href="https://opencollective.com/tickernet"><img src="https://opencollective.com/tickernet/backers.svg?width=890" /></a>

## Contributing

PRs, ideas, and issues are welcome! Please read our [Contributing Guide](CONTRIBUTING.md) and sign the [CLA](CLA.md) before submitting a pull request.

## Contributors

Thanks to all our wonderful contributors! See [CONTRIBUTORS.md](CONTRIBUTORS.md) for details.

<a href="https://github.com/Arcenox-co/TickerNet/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=Arcenox-co/TickerNet" />
</a>

## License

Dual licensed under **MIT** and **Apache 2.0** © [Arcenox LLC](https://arcenox.com)
