using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using TickerNet.DependencyInjection;
using TickerNet.EntityFrameworkCore.DbContextFactory;
using TickerNet.EntityFrameworkCore.DependencyInjection;
using TickerNet.Instrumentation.OpenTelemetry;
using TickerNet.Utilities;
using TickerNet.Utilities.Base;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Interfaces.Managers;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddTickerNet(options =>
        {
            options.AddOperationalStore(efOptions =>
            {
                efOptions.UseTickerNetDbContext<TickerNetDbContext>(dbOptions =>
                {
                    dbOptions.UseSqlite("Data Source=tickernet-opentelemetry.db");
                });
            });

            // Emit TickerNet's activities through OpenTelemetry.
            options.AddOpenTelemetryInstrumentation();
        });

        // Send the spans to the console, so the sample needs no collector.
        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddTickerNetSource()
                .AddConsoleExporter());

        services.AddHostedService<SampleScheduler>();
    })
    .Build();

using (var scope = host.Services.CreateScope())
{
    // Create the schema from the model, so the sample needs no migrations.
    var db = scope.ServiceProvider.GetRequiredService<TickerNetDbContext>();
    db.Database.EnsureCreated();
}

// Build function metadata so TickerFunctionProvider.TickerFunctions is initialized
TickerFunctionProvider.Build();

await host.RunAsync();

public class OpenTelemetrySampleJobs
{
    [TickerFunction("OpenTelemetrySample_HelloWorld")]
    public Task HelloWorldAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[Sample] Hello from TickerNet. Id={context.Id}, ScheduledFor={context.ScheduledFor:O}");
        return Task.CompletedTask;
    }
}

public class SampleScheduler : IHostedService
{
    private readonly ITimeTickerManager<TimeTickerEntity> _timeTickerManager;

    public SampleScheduler(ITimeTickerManager<TimeTickerEntity> timeTickerManager)
    {
        _timeTickerManager = timeTickerManager;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var result = await _timeTickerManager.AddAsync(new TimeTickerEntity
        {
            Function = "OpenTelemetrySample_HelloWorld",
            ExecutionTime = DateTime.UtcNow.AddSeconds(5)
        }, cancellationToken);

        if (!result.IsSucceeded)
        {
            Console.WriteLine($"Failed to schedule sample job. Exception: {result.Exception}");
            return;
        }

        Console.WriteLine($"Scheduled sample job with Id={result.Result.Id}, ScheduledFor={result.Result.ExecutionTime:O}");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
