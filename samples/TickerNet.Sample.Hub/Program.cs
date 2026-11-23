using Microsoft.AspNetCore.Server.Kestrel.Core;
using TickerNet.Dashboard.DependencyInjection;
using TickerNet.DependencyInjection;
using TickerNet.Hub;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Interfaces.Managers;

// The scheduler: it stores and schedules jobs, shows the dashboard, and sends each run of a remote
// function ("function@node") to a connected worker. Run TickerNet.Sample.HubWorker next to it.
//
// Both apps need the same shared secret, from configuration or the environment, for example:
//   export TickerNet__SharedSecret="$(openssl rand -base64 48)"
var builder = WebApplication.CreateBuilder(args);

var sharedSecret = builder.Configuration["TickerNet:SharedSecret"]
    ?? throw new InvalidOperationException("Set TickerNet:SharedSecret (for example the TickerNet__SharedSecret environment variable).");

builder.WebHost.ConfigureKestrel(kestrel =>
{
    // Dashboard and API.
    kestrel.ListenLocalhost(5080);
    // Workers speak gRPC over HTTP/2. Without TLS, HTTP/2 needs a port of its own. Use https in production.
    kestrel.ListenLocalhost(5081, listen => listen.Protocols = HttpProtocols.Http2);
});

builder.Services.AddTickerNet(options =>
{
    options.AddTickerNetHub(hub => hub.SharedSecret = sharedSecret);
    options.AddDashboard();
});

var app = builder.Build();

app.UseTickerNet();
app.MapTickerNetHub();

// Schedules SendReport on the "reports" worker node. The worker's own name for it is "SendReport".
app.MapPost("/send-report", async (ITimeTickerManager<TimeTickerEntity> manager) =>
{
    var result = await manager.AddAsync(new TimeTickerEntity
    {
        Function = "SendReport@reports",
        ExecutionTime = DateTime.UtcNow.AddSeconds(5)
    });

    return result.IsSucceeded
        ? Results.Ok(new { result.Result.Id, ScheduledFor = result.Result.ExecutionTime })
        : Results.Problem(result.Exception?.Message);
});

app.Run();
