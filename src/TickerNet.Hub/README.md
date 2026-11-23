# TickerNet.Hub

Lets a [TickerNet](https://github.com/TickerNet/TickerNet) scheduler run jobs on separate worker processes. Workers
use [`TickerNet.Worker`](https://www.nuget.org/packages/TickerNet.Worker) and connect straight to the scheduler: the
scheduler is the hub, and no hosted service is involved.

## Setup

```csharp
using TickerNet.DependencyInjection;
using TickerNet.Hub;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTickerNet(options =>
{
    // Plus your job store, for example options.AddOperationalStore(...).
    options.AddTickerNetHub(hub =>
    {
        // At least 32 characters. Keep it in configuration or a secret store; workers use the same value.
        hub.SharedSecret = builder.Configuration["TickerNet:SharedSecret"]!;
    });
});

var app = builder.Build();
app.UseTickerNet();
app.MapTickerNetHub();
app.Run();
```

Workers connect with gRPC over HTTP/2. Use HTTPS. In a trusted private network without TLS, give workers a port of its
own that speaks only HTTP/2.

## How it works

- Each worker declares the `[TickerFunction]` functions it can run. The scheduler registers them as `function@node`,
  for example `SendReport@reports`, so they appear in the dashboard and can be scheduled like local functions.
- When a remote job is due, the scheduler sends one attempt to a connected worker of that node and records the result.
  Retries, cancelling, status, history and tracing stay on the scheduler, so a retry can go to another replica.
- With no worker connected, a run waits `WorkerWaitTimeout` (30 seconds by default) and is then recorded as `Skipped`.
- Cron expressions on worker functions become cron jobs on the scheduler, paused while no connected worker offers
  the function. The hub never deletes them; remove one from the dashboard when it's no longer needed.
- Runs go only to workers that offer the function. A run that never reached a worker, for example because the worker
  was stopping, goes to another worker without using a retry.

## Options

| Option | Default | Meaning |
| --- | --- | --- |
| `SharedSecret` | (required) | Secret workers prove they know, at least 32 characters. |
| `WorkerWaitTimeout` | 30 s | How long a due run waits for a worker of its node. |
| `ExecutionTimeout` | none | Longest a run may take on a worker. |
| `WorkerTimeout` | 45 s | A worker that sends nothing for this long is dropped. |
| `SeedCronTickers` | `true` | Create cron jobs for worker functions with a cron expression. |
| `PauseCronTickersWhileOffline` | `true` | Pause those cron jobs while their node is offline. |
| `MaxMessageSize` | 16 MB | Largest message either side may send. |

`MapTickerNetHub()` returns an endpoint convention builder, so you can add rules such as
`.RequireHost("workers.example.internal")`.

## Security

- Workers sign their call headers with the shared secret, and the hub checks them before it reads anything else,
  with a 5-minute clock window and a check against replayed calls. The hub signs its reply, so workers know they
  reached a real hub.
- A worker can only add, change or remove jobs for its own node, can change only a job's schedule fields, and can
  read the stored request only of a run it is executing.
- Use HTTPS. Workers refuse a plain `http` hub on another machine unless they set `AllowUnencryptedHttp`.

## Coming from TickerQ

`TickerNet.Hub` takes the place of `TickerQ.RemoteExecutor`, without TickerQ Hub. Jobs stored as `function@node` run on
a TickerNet worker that uses the same node name. `SdkOfflineSkipException` still works but is obsolete; use
`WorkerOfflineException`.

Read the [remote workers guide](https://tickernet.github.io/TickerNet/remote-workers.html) for the full picture.
