# TickerNet

An open-source background job scheduler for .NET: source-generated jobs, cron and one-off scheduling, your own
database, a live dashboard, and optional remote workers.

TickerNet is a fork of TickerQ, taken from its last open-source commit before TickerQ moved to a commercial license.
[Omniscient AI](https://omniscient.to) set up the fork, maintains it and funds its development, and makes two
promises on the project's behalf:

- **Build anything on it.** Use, change and redistribute TickerNet under MIT or Apache-2.0, whichever you choose,
  including in closed-source and commercial products.
- **Open source, forever.** Every TickerNet release stays MIT OR Apache-2.0: no paid edition, no source-available
  version, no license keys and no CLA. See the
  [open-source pledge](https://github.com/TickerNet/TickerNet/blob/main/GOVERNANCE.md).

## Quick start

```csharp
using TickerNet.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTickerNet();

var app = builder.Build();
app.UseTickerNet();
app.Run();
```

```csharp
using TickerNet.Utilities.Base;

public class MyJobs
{
    [TickerFunction("HelloWorld")]
    public Task HelloWorld(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Hello from TickerNet! Job ID: {context.Id}");
        return Task.CompletedTask;
    }
}
```

Schedule it with `ITimeTickerManager<TimeTickerEntity>`:

```csharp
await manager.AddAsync(new TimeTickerEntity
{
    Function = "HelloWorld",
    ExecutionTime = DateTime.UtcNow.AddSeconds(10)
});
```

This package includes the source generator that registers `[TickerFunction]` methods at compile time.

## Packages

| Package | What it does |
| --- | --- |
| `TickerNet` | Core scheduler engine (includes the source generator) |
| `TickerNet.Utilities` | Shared types, entities and interfaces |
| `TickerNet.EntityFrameworkCore` | EF Core store |
| `TickerNet.Caching.StackExchangeRedis` | Redis store and coordination between nodes |
| `TickerNet.MongoDB` | MongoDB store |
| `TickerNet.Dashboard` | Live dashboard |
| `TickerNet.Instrumentation.OpenTelemetry` | OpenTelemetry tracing |
| `TickerNet.Hub` | Lets worker processes connect to the scheduler and run its jobs |
| `TickerNet.Worker` | Runs jobs in a worker process connected to a scheduler with `TickerNet.Hub` |

Keep all TickerNet packages on the same version.

## Coming from TickerQ

Moving from TickerQ 10.4 or earlier is mostly a rename (`AddTickerQ` becomes `AddTickerNet`), and TickerNet keeps
working with your stored data, old dashboard links and saved sign-ins. The
[migration guide](https://tickernet.github.io/TickerNet/migrating-from-tickerq.html) lists every change.

## Links

- Documentation: https://tickernet.github.io/TickerNet/
- Source and issues: https://github.com/TickerNet/TickerNet
- License: MIT OR Apache-2.0, at your option.

TickerNet is not affiliated with or endorsed by Arcenox LLC, the maker of TickerQ.
