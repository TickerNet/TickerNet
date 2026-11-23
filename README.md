<h1 align="center">TickerNet</h1>

<p align="center">
  <strong>An open-source background job scheduler for .NET</strong><br />
  Source-generated jobs, cron and one-off scheduling, your own database, and a live dashboard.
</p>

<p align="center">
  <a href="https://github.com/tickernet/TickerNet/actions/workflows/ci.yaml"><img src="https://img.shields.io/github/actions/workflow/status/tickernet/TickerNet/ci.yaml?branch=main&style=flat-square" alt="Build status" /></a>
  <a href="https://tickernet.github.io/TickerNet/"><img src="https://img.shields.io/badge/docs-tickernet.github.io-blue?style=flat-square" alt="Documentation" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT%20OR%20Apache--2.0-green?style=flat-square" alt="License: MIT OR Apache-2.0" /></a>
</p>

---

TickerNet is a fork of [TickerQ](https://github.com/Arcenox-co/TickerQ), taken from its last open-source commit before
TickerQ moved to a commercial license. TickerNet is, and will stay, MIT OR Apache-2.0: the
[open-source pledge](GOVERNANCE.md#open-source-pledge) means no TickerNet release will ever be source-available or
paid-only. You remain free to build anything on it, proprietary products included.

> **Preview.** TickerNet is at `1.0.0-preview.1` and isn't on NuGet yet. Until the first release, reference the
> projects from source. The package names below are the ones the first release will use.

## Features

- **Time and cron scheduling**: one-off jobs at a set time, and recurring jobs from cron expressions.
- **Source-generated**: jobs are registered at compile time, with no runtime reflection, so apps can be trimmed and
  compiled ahead of time.
- **Your own storage**: EF Core (PostgreSQL, SQL Server, SQLite, MySQL), Redis or MongoDB.
- **Live dashboard**: a SignalR dashboard to watch, inspect and manage jobs.
- **Retries and throttling**: retry policies with backoff.
- **Several nodes**: Redis heartbeats, dead-node cleanup and lock-based coordination.
- **OpenTelemetry**: tracing for job runs.

## Quick start

### 1. Register services

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTickerNet();

var app = builder.Build();
app.UseTickerNet();
app.Run();
```

### 2. Write a job

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

The [getting started guide](https://tickernet.github.io/TickerNet/getting-started.html) covers storage, cron jobs, the
dashboard and OpenTelemetry.

## Packages

| Package | What it does |
| --- | --- |
| `TickerNet` | Core scheduler engine (includes the source generator) |
| `TickerNet.Utilities` | Shared types, entities and interfaces |
| `TickerNet.EntityFrameworkCore` | EF Core persistence |
| `TickerNet.Caching.StackExchangeRedis` | Redis persistence and coordination between nodes |
| `TickerNet.MongoDB` | MongoDB persistence |
| `TickerNet.Dashboard` | Live dashboard |
| `TickerNet.Instrumentation.OpenTelemetry` | OpenTelemetry tracing |
| `TickerNet.SourceGenerator` | Compile-time job registration |

All packages share one version. Keep them on the same version.

## Coming from TickerQ

TickerNet starts from TickerQ's last open-source code (see [NOTICE](NOTICE)), so moving from TickerQ 10.4 or earlier
is mostly a rename: `TickerQ` becomes `TickerNet` in package IDs, namespaces and API names (`AddTickerQ` becomes
`AddTickerNet`). EF Core table and schema names are unchanged. The
[migration guide](https://tickernet.github.io/TickerNet/migrating-from-tickerq.html) lists everything that changed.
Features from TickerQ's commercial versions are not part of TickerNet.

## Documentation

The docs site is at **[tickernet.github.io/TickerNet](https://tickernet.github.io/TickerNet/)**: getting started,
migrating from TickerQ, a contributor guide that walks through the code, governance, and the project's
architecture decision records. Its source is the [`site/`](site/) folder.

## Contributing

Issues and pull requests are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) first. In short: sign off every commit
(`git commit -s`), and never copy or adapt anything from TickerQ's commercial versions.

## Governance and sponsor

TickerNet is sponsored by **[Omniscient AI](https://omniscient.to)**, which funds its maintenance and holds the
project name on the community's behalf. [GOVERNANCE.md](GOVERNANCE.md) sets out the open-source pledge and how
decisions are made, and [TRADEMARKS.md](TRADEMARKS.md) how the name may be used.

## License

Licensed under either the [MIT License or the Apache License 2.0](LICENSE), at your option.

Copyright 2025-present Arcenox LLC (the original TickerQ code).
Copyright 2026-present Omniscient AI Private Limited and the TickerNet contributors.

TickerNet is not affiliated with or endorsed by Arcenox LLC.
