using Microsoft.EntityFrameworkCore;
using TickerNet.Dashboard.DependencyInjection;
using TickerNet.DependencyInjection;
using TickerNet.EntityFrameworkCore.DbContextFactory;
using TickerNet.EntityFrameworkCore.DependencyInjection;
using TickerNet.Utilities.Base;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Interfaces.Managers;

var builder = WebApplication.CreateBuilder(args);

// TickerNet setup with SQLite operational store (file-based)
builder.Services.AddTickerNet(options =>
{
    options.AddOperationalStore(efOptions =>
    {
        efOptions.UseTickerNetDbContext<TickerNetDbContext>(dbOptions =>
        {
            dbOptions.UseSqlite(
                "Data Source=tickernet-webapi.db",
                b => b.MigrationsAssembly("TickerNet.Sample.WebApi"));
        });
    });
    options.AddDashboard();
});

var app = builder.Build();

// Ensure TickerNet operational store schema is applied
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TickerNetDbContext>();
    db.Database.Migrate();
}

// Activate TickerNet job processor (mirrors docs' minimal setup)
app.UseTickerNet();

// Minimal endpoint to schedule the sample job
app.MapPost("/schedule-sample", async (ITimeTickerManager<TimeTickerEntity> manager) =>
{
    var result = await manager.AddAsync(new TimeTickerEntity
    {
        Function = "WebApiSample_HelloWorld",
        ExecutionTime = DateTime.UtcNow.AddSeconds(5)
    });

    return Results.Ok(new { result.Result.Id, ScheduledFor = result.Result.ExecutionTime });
});

app.Run();

// Simple sample job
public class SampleJobs
{
    [TickerFunction("WebApiSample_HelloWorld")]
    public Task HelloWorldAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[WebApi] Hello from TickerNet! Id={context.Id}, ScheduledFor={context.ScheduledFor:O}");
        return Task.CompletedTask;
    }
}
