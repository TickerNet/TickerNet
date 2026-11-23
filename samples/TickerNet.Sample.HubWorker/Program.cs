using TickerNet.DependencyInjection;
using TickerNet.Utilities.Base;
using TickerNet.Worker;

// A worker node: it connects to the scheduler in TickerNet.Sample.Hub, offers the functions below,
// and runs them when the scheduler sends them. It stores no jobs itself.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddTickerNet(options => options.AddTickerNetWorker(worker =>
{
    worker.HubAddress = new Uri(builder.Configuration["TickerNet:HubAddress"] ?? "http://localhost:5081");
    worker.SharedSecret = builder.Configuration["TickerNet:SharedSecret"]
        ?? throw new InvalidOperationException("Set TickerNet:SharedSecret to the scheduler's secret.");
    // Every replica of this worker uses the same node name. The scheduler names its functions
    // "SendReport@reports" and "NightlyCleanup@reports".
    worker.NodeName = "reports";
}));

var host = builder.Build();

// Ensure TickerFunction registrations in hosting scenarios where ModuleInitializer may be skipped
global::TickerNet.Sample.HubWorker.TickerNetInstanceFactoryExtensions.Initialize();

host.UseTickerNet();
await host.RunAsync();

public class ReportJobs(ILogger<ReportJobs> logger)
{
    [TickerFunction("SendReport")]
    public async Task SendReportAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Sending report for job {Id} (attempt {Attempt})", context.Id, context.RetryCount + 1);
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
    }

    // Cron functions on a worker get a cron job on the scheduler, paused while no worker is connected.
    [TickerFunction("NightlyCleanup", "0 3 * * *")]
    public Task NightlyCleanupAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Nightly cleanup for occurrence {Id}", context.Id);
        return Task.CompletedTask;
    }
}
