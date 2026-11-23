# TickerNet.Worker

Runs [TickerNet](https://github.com/TickerNet/TickerNet) jobs in a separate worker process. The worker connects to a
scheduler that uses [`TickerNet.Hub`](https://www.nuget.org/packages/TickerNet.Hub), declares its `[TickerFunction]`
functions, and runs them when the scheduler sends them. It needs only outbound access to the scheduler.

## Setup

```csharp
using TickerNet.DependencyInjection;
using TickerNet.Utilities.Base;
using TickerNet.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddTickerNet(options => options.AddTickerNetWorker(worker =>
{
    worker.HubAddress = new Uri("https://scheduler.internal:5001");
    // The scheduler's shared secret, from configuration or a secret store.
    worker.SharedSecret = builder.Configuration["TickerNet:SharedSecret"]!;
    // Every replica of this worker uses the same node name.
    worker.NodeName = "reports";
}));

var host = builder.Build();
host.UseTickerNet();
await host.RunAsync();

public class ReportJobs
{
    [TickerFunction("SendReport")]
    public Task SendReport(TickerFunctionContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}
```

The scheduler knows this function as `SendReport@reports`. Schedule it there, or from the worker with
`ITimeTickerManager` and the function's own name, `SendReport`: the worker saves the job on the scheduler.

Don't add a job store (EF Core, Redis or MongoDB) to a worker. Jobs, history and the dashboard live on the scheduler.
A worker can only add, change or remove jobs for its own node, and changes from a worker apply only to a job's
schedule (time or cron expression, request, retries, description).

When the app stops, the worker tells the scheduler, takes no new runs, and finishes the runs it has before it
disconnects, so rolling deploys don't fail jobs.

## Options

| Option | Default | Meaning |
| --- | --- | --- |
| `HubAddress` | (required) | The scheduler's address. |
| `SharedSecret` | (required) | The scheduler's shared secret. |
| `NodeName` | (required) | Groups interchangeable replicas. No `@` or spaces. |
| `MaxConcurrency` | 0 (no limit) | Most runs this worker executes at once. |
| `HeartbeatInterval` | 15 s | How often the worker tells the scheduler it's alive. |
| `OperationTimeout` | 30 s | How long to wait for the scheduler to save jobs added on the worker. |
| `MaxReconnectDelay` | 30 s | Longest wait between reconnect attempts. |
| `ShutdownTimeout` | 25 s | On shutdown, how long to wait for runs in progress to finish. |
| `AllowUnencryptedHttp` | `false` | Allow a plain `http` hub address on another machine (trusted private networks only). |
| `MaxMessageSize` | 16 MB | Largest message either side may send. |
| `ConfigureChannel` | none | Changes the gRPC channel, for example for client certificates or a proxy. |

## Coming from TickerQ

`TickerNet.Worker` takes the place of `TickerQ.SDK`, without TickerQ Hub: it connects straight to your scheduler. Use
the node name your TickerQ SDK used, and jobs TickerQ stored for that node keep running.

Read the [remote workers guide](https://tickernet.github.io/TickerNet/remote-workers.html) for the full picture.
