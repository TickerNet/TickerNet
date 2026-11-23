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

### Basic Setup

```csharp
using TickerNet.Instrumentation.OpenTelemetry;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Configure OpenTelemetry with TickerNet ActivitySource
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource("TickerNet") // Add TickerNet ActivitySource
               .AddConsoleExporter()
               .AddJaegerExporter();
    });

// Add TickerNet with OpenTelemetry instrumentation
builder.Services.AddTickerNet<MyTimeTicker, MyCronTicker>(options => { })
    .AddOperationalStore(ef => { })
    .AddOpenTelemetryInstrumentation(); // 👈 Enable tracing

var app = builder.Build();
app.Run();
```

### With Jaeger

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddTickerNetInstrumentation()
               .AddJaegerExporter(options =>
               {
                   options.Endpoint = new Uri("http://localhost:14268/api/traces");
               });
    });
```

### With Application Insights

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddTickerNetInstrumentation()
               .AddAzureMonitorTraceExporter();
    });
```

## Trace Structure

### Job Execution Activities
```
tickernet.job.execute.timeticker (main job execution span)
├── tickernet.job.enqueued (when job starts execution)
├── tickernet.job.completed (on successful completion)
├── tickernet.job.failed (on failure)
├── tickernet.job.cancelled (on cancellation)
├── tickernet.job.skipped (when skipped)
├── tickernet.seeding.started (for data seeding)
└── tickernet.seeding.completed (seeding completion)
```

### Tags Added to Activities

| Tag | Description | Example |
|-----|-------------|---------|
| `tickernet.job.id` | Unique job identifier | `123e4567-e89b-12d3-a456-426614174000` |
| `tickernet.job.type` | Type of ticker | `TimeTicker`, `CronTicker` |
| `tickernet.job.function` | Function name being executed | `ProcessEmails` |
| `tickernet.job.priority` | Job priority | `Normal`, `High`, `LongRunning` |
| `tickernet.job.machine` | Machine executing the job | `web-server-01` |
| `tickernet.job.parent_id` | Parent job ID (for child jobs) | `parent-job-guid` |
| `tickernet.job.enqueued_from` | Where the job was enqueued from | `UserController.CreateUser (Program.cs:42)` |
| `tickernet.job.is_due` | Whether the job was due | `true`, `false` |
| `tickernet.job.is_child` | Whether this is a child job | `true`, `false` |
| `tickernet.job.retries` | Maximum retry attempts | `3` |
| `tickernet.job.current_attempt` | Current retry attempt | `1`, `2`, `3` |
| `tickernet.job.final_status` | Final execution status | `Done`, `Failed`, `Cancelled`, `Skipped` |
| `tickernet.job.final_retry_count` | Final retry count reached | `2` |
| `tickernet.job.execution_time_ms` | Execution time in milliseconds | `1250` |
| `tickernet.job.success` | Whether execution was successful | `true`, `false` |
| `tickernet.job.error_type` | Exception type for failures | `SqlException`, `TimeoutException` |
| `tickernet.job.error_message` | Error message | `Connection timeout` |
| `tickernet.job.error_stack_trace` | Full stack trace | `at MyService.ProcessData()...` |
| `tickernet.job.cancellation_reason` | Reason for cancellation | `Task was cancelled` |
| `tickernet.job.skip_reason` | Reason for skipping | `Another instance is already running` |

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

- .NET 8.0 or later
- OpenTelemetry 1.15.3 or later
- TickerNet.Utilities (automatically included)
