using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerNet.DependencyInjection;
using TickerNet.EntityFrameworkCore.DbContextFactory;
using TickerNet.EntityFrameworkCore.DependencyInjection;
using TickerNet.Utilities;
using TickerNet.Utilities.Base;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Interfaces.Managers;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Configure TickerNet with SQLite operational store (file-based)
        services.AddTickerNet(options =>
        {
            options.AddOperationalStore(efOptions =>
            {
                efOptions.UseTickerNetDbContext<TickerNetDbContext>(dbOptions =>
                {
                    dbOptions.UseSqlite(
                        "Data Source=tickernet-console.db",
                        b => b.MigrationsAssembly("TickerNet.Sample.Console"));
                });
            });
        });

        services.AddHostedService<SampleScheduler>();
    })
    .Build();

// Ensure TickerNet operational store schema is applied
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TickerNetDbContext>();
    db.Database.Migrate();
}

// Build function metadata so TickerFunctionProvider.TickerFunctions is initialized
TickerFunctionProvider.Build();

await host.RunAsync();

// Simple sample job
public class ConsoleSampleJobs
{
    [TickerFunction("ConsoleSample_HelloWorld")]
    public Task HelloWorldAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[Console] Hello from TickerNet! Id={context.Id}, ScheduledFor={context.ScheduledFor:O}");
        return Task.CompletedTask;
    }
}

// Hosted service that schedules a single job on startup
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
            Function = "ConsoleSample_HelloWorld",
            ExecutionTime = DateTime.UtcNow.AddSeconds(5)
        }, cancellationToken);

        if (!result.IsSucceeded)
        {
            Console.WriteLine($"Failed to schedule console sample job. Exception: {result.Exception}");
            return;
        }

        Console.WriteLine($"Scheduled console sample job with Id={result.Result.Id}, ScheduledFor={result.Result.ExecutionTime:O}");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
