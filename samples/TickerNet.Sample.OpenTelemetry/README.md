# TickerNet OpenTelemetry sample

A console app that schedules one job and exports TickerNet's spans with the
OpenTelemetry console exporter, so no collector or backend is needed.

```bash
cd samples/TickerNet.Sample.OpenTelemetry
dotnet run
```

The job runs five seconds after startup. The output contains the TickerNet
spans, for example:

```text
Activity.DisplayName:        tickernet.job.enqueued
Activity.DisplayName:        tickernet.job.completed
Activity.DisplayName:        tickernet.job.execute.timeticker
```

The setup follows `src/TickerNet.Instrumentation.OpenTelemetry/README.md`:
`options.AddOpenTelemetryInstrumentation()` turns the instrumentation on and
`tracing.AddTickerNetSource()` subscribes the pipeline to the `TickerNet`
`ActivitySource`. Replace `AddConsoleExporter()` with any exporter you use.

The SQLite file `tickernet-opentelemetry.db` is created next to the working
directory on first run; delete it to start over.
