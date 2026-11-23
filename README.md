<h1 align="center">TickerNet</h1>

<p align="center">
  <strong>An open-source background job scheduler for .NET</strong><br />
  Source-generated jobs, cron and one-off scheduling, your own database, and a live dashboard.
</p>

<p align="center">
  <a href="https://github.com/TickerNet/TickerNet/actions/workflows/ci.yaml"><img src="https://img.shields.io/github/actions/workflow/status/TickerNet/TickerNet/ci.yaml?branch=main&style=flat-square" alt="Build status" /></a>
  <a href="https://tickernet.github.io/TickerNet/"><img src="https://img.shields.io/badge/docs-tickernet.github.io-blue?style=flat-square" alt="Documentation" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT%20OR%20Apache--2.0-green?style=flat-square" alt="License: MIT OR Apache-2.0" /></a>
</p>

<p align="center">
  <sub>MAINTAINED AND SPONSORED BY</sub><br />
  <a href="https://omniscient.to">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="site/assets/sponsor/omniscient-logo-horizontal-dark-bg.svg" />
      <img src="site/assets/sponsor/omniscient-logo-horizontal-light-bg.svg" alt="Omniscient AI" height="44" />
    </picture>
  </a>
</p>

---

TickerNet is a fork of [TickerQ](https://github.com/Arcenox-co/TickerQ), taken from its last open-source commit before
TickerQ moved to a commercial license. [Omniscient AI](https://omniscient.to) set up the fork, maintains it and funds
its development, and makes two promises on the project's behalf:

> [!TIP]
> **Build anything on it.** Use, change and redistribute TickerNet under **MIT or Apache-2.0**, whichever you choose.
> Fork it, build closed-source or commercial products on it, and keep your changes private. Nothing in TickerNet's
> governance adds conditions to that. See [your freedom to build on TickerNet](GOVERNANCE.md#your-freedom-to-build-on-tickernet).

> [!IMPORTANT]
> **Open source, forever.** Every TickerNet release stays **MIT OR Apache-2.0**: no paid edition, no source-available
> version, no license keys and no CLA. Omniscient AI has made this an
> [irrevocable pledge](GOVERNANCE.md#open-source-pledge-tickernet-stays-open-source-forever).

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

The source generator (`src/TickerNet.SourceGenerator`) ships inside the `TickerNet` package as an analyzer, so there's
nothing extra to install. All packages share one version. Keep them on the same version.

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

<a href="https://omniscient.to"><img src="site/assets/sponsor/omniscient-mark-gradient.svg" alt="" height="28" align="left" /></a>
TickerNet is maintained and sponsored by **[Omniscient AI](https://omniscient.to)** (Omniscient AI Private Limited).
Omniscient AI set up the fork, funds maintainer time and the project's running costs, and holds the TickerNet name,
GitHub organization, docs site and package IDs for the community under the open-source pledge.

[GOVERNANCE.md](GOVERNANCE.md) sets out your freedom to build on TickerNet, the pledge and how decisions are made.
[TRADEMARKS.md](TRADEMARKS.md) explains how the TickerNet name may be used. The Omniscient AI name and logos are
Omniscient AI's trademarks and are not covered by the project license.

## License

Licensed under either the [MIT License or the Apache License 2.0](LICENSE), at your option.

Copyright 2025-present Arcenox LLC (the original TickerQ code).
Copyright 2026-present Omniscient AI Private Limited and the TickerNet contributors.

TickerNet is not affiliated with or endorsed by Arcenox LLC.
