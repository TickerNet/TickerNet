# TickerNet.Instrumentation.OpenTelemetry

OpenTelemetry instrumentation package for TickerNet job scheduler with distributed tracing support.

## Features

- **Distributed Tracing**: Full OpenTelemetry activity/span creation for job execution lifecycle
- **Structured Logging**: Rich logging with job context through ILogger integration
- **Parent-Child Relationships**: Maintains trace relationships between parent and child jobs
- **Retry Tracking**: Tracks retry attempts with detailed context
- **Performance Metrics**: Comprehensive execution time and outcome tracking
- **Error Tracking**: Detailed exception and cancellation tracking
- **Caller Information**: Automatic detection of where jobs are enqueued from

## Installation

```bash
dotnet add package TickerNet.Instrumentation.OpenTelemetry
```

## Usage

Turn on the instrumentation in TickerNet's options, then tell OpenTelemetry to listen to the `TickerNet`
ActivitySource and send spans to the exporter you use:

```csharp
using OpenTelemetry.Trace;
using TickerNet.DependencyInjection;
using TickerNet.Instrumentation.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTickerNet(options =>
{
    options.AddOpenTelemetryInstrumentation();
});

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("TickerNet")
        .AddOtlpExporter());

var app = builder.Build();
app.UseTickerNet();
app.Run();
```

`AddOtlpExporter` comes from the `OpenTelemetry.Exporter.OpenTelemetryProtocol` package. Any OpenTelemetry trace
exporter works in its place.

`tracing.AddTickerNetSource()` does the same as `AddSource("TickerNet")`.

## Keeping TickerQ's trace names

If you are moving from TickerQ and have trace queries, dashboards or alerts built on its names, export TickerNet's
spans with those names:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddTickerNetSource(useTickerQNames: true) // before any exporter
        .AddOtlpExporter());
```

With `useTickerQNames: true`, span names and tag keys that start with `tickernet.` start with `tickerq.` instead
(for example `tickernet.job.execute.timeticker` becomes `tickerq.job.execute.timeticker`, and `tickernet.job.id`
becomes `tickerq.job.id`). Only spans from the `TickerNet` ActivitySource change. It is off by default.

Order matters: the renaming happens when a span ends, in a processor that has to run before the exporter.
Processors run in the order you add them, so call `AddTickerNetSource(useTickerQNames: true)` before
`AddOtlpExporter()` or any other exporter. If you build the pipeline yourself, add `TickerQTraceNamesProcessor`
with `AddProcessor` before the exporter.

What stays the same either way:

- The instrumentation scope (the ActivitySource name) is `TickerNet`. Filters on the scope name `TickerQ` need to
  change.
- Log messages (`TickerNet Job enqueued: …`) and logger categories (`TickerNet.*`) keep their TickerNet names.

## Trace Structure

### Job Execution Activities

The main span is `tickernet.job.execute.timeticker` for a time ticker and
`tickernet.job.execute.crontickeroccurrence` for a cron occurrence.

```
tickernet.job.execute.timeticker (or .crontickeroccurrence)
├── tickernet.job.enqueued (when the job starts execution)
├── tickernet.job.completed (on successful completion)
├── tickernet.job.failed (on failure)
├── tickernet.job.cancelled (on cancellation)
├── tickernet.job.skipped (when skipped)
└── tickernet.job_request_serialization.failed (when the request cannot be deserialized)
```

Seeding spans are separate roots, not children of a job span:

```
tickernet.seeding.started
tickernet.seeding.completed
```

### Tags Added to Activities

| Tag | Description | Example |
|-----|-------------|---------|
| `tickernet.job.id` | Unique job identifier | `123e4567-e89b-12d3-a456-426614174000` |
| `tickernet.job.type` | Type of ticker | `TimeTicker`, `CronTickerOccurrence` |
| `tickernet.job.function` | Function name being executed | `ProcessEmails` |
| `tickernet.job.priority` | Job priority | `Normal`, `High`, `LongRunning` |
| `tickernet.job.machine` | Machine executing the job | `web-server-01` |
| `tickernet.job.parent_id` | Parent job ID (for child jobs) | `parent-job-guid` |
| `tickernet.job.enqueued_from` | Where the job was enqueued from | `UserController.CreateUser (Program.cs:42)` |
| `tickernet.job.is_due` | Whether the job was due | `true`, `false` |
| `tickernet.job.is_child` | Whether this is a child job | `true`, `false` |
| `tickernet.job.retries` | Maximum retry attempts | `3` |
| `tickernet.job.retry_count` | Retry attempt that failed (on `tickernet.job.failed`) | `1`, `2`, `3` |
| `tickernet.job.current_attempt` | Current retry attempt | `1`, `2`, `3` |
| `tickernet.job.run_condition` | Run condition of a time-ticker child job | `OnSuccess`, `Always` |
| `tickernet.job.final_status` | Final execution status | `Done`, `Failed`, `Cancelled`, `Skipped` |
| `tickernet.job.final_retry_count` | Final retry count reached | `2` |
| `tickernet.job.execution_time_ms` | Execution time in milliseconds | `1250` |
| `tickernet.job.success` | Whether execution was successful | `true`, `false` |
| `tickernet.job.error_type` | Exception type for failures | `SqlException`, `TimeoutException` |
| `tickernet.job.error_message` | Error message | `Connection timeout` |
| `tickernet.job.error_stack_trace` | Full stack trace | `at MyService.ProcessData()...` |
| `tickernet.job.cancellation_reason` | Reason for cancellation | `Task was cancelled` |
| `tickernet.job.skip_reason` | Reason for skipping | `Another instance is already running` |
| `tickernet.seeding.type` | Ticker type being seeded | `TimeTicker` |
| `tickernet.seeding.environment` | Machine running the seeding | `production-node-01` |

## Logging Output

The instrumentation provides structured logging for all job events:

```
[INF] TickerNet Job enqueued: TimeTicker - ProcessEmails (123e4567-e89b-12d3-a456-426614174000) from ExecutionTaskHandler
[INF] TickerNet Job completed: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) in 1250ms - Success: True
[ERR] TickerNet Job failed: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) - Retry 1 - Connection timeout
[INF] TickerNet Job completed: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) in 2500ms - Success: False
[WRN] TickerNet Job cancelled: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) - Task was cancelled
[INF] TickerNet Job skipped: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) - Another CronOccurrence is already running!
[INF] TickerNet start seeding data: TimeTicker (production-node-01)
[INF] TickerNet completed seeding data: TimeTicker (production-node-01)
```

## Integration with Logging Frameworks

This package works seamlessly with any logging framework that integrates with `ILogger`:

### Serilog
```csharp
builder.Host.UseSerilog((context, config) =>
{
    config.WriteTo.Console()
          .WriteTo.File("logs/tickernet-.txt", rollingInterval: RollingInterval.Day)
          .Enrich.FromLogContext();
});
```

### NLog
```csharp
builder.Logging.ClearProviders();
builder.Logging.AddNLog();
```

## Performance Impact

- **Minimal Overhead**: Activities are only created when OpenTelemetry listeners are active
- **Efficient Logging**: Uses structured logging with minimal string allocations
- **Conditional Tracing**: No performance impact when tracing is disabled

## Requirements

- .NET 10 or .NET 11 (the packages target `net10.0` and `net11.0`)
- OpenTelemetry 1.15.3 or later
- TickerNet.Utilities (automatically included)
