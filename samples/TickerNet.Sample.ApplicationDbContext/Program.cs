using Microsoft.EntityFrameworkCore;
using TickerNet.DependencyInjection;
using TickerNet.EntityFrameworkCore.DependencyInjection;
using TickerNet.Sample.ApplicationDbContext.Data;
using TickerNet.Utilities.Base;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Interfaces.Managers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(dbOptions =>
{
    dbOptions.UseSqlite(
        "Data Source=tickernet-webapi.db",
        b => b.MigrationsAssembly("TickerNet.Sample.ApplicationDbContext"));
});

// TickerNet setup with SQLite operational store (file-based)
builder.Services.AddTickerNet(options =>
{
    options.AddOperationalStore(efOptions =>
    {
        efOptions.UseApplicationDbContext<AppDbContext>(TickerNet.EntityFrameworkCore.Customizer.ConfigurationType.UseModelCustomizer);
    });
});

var app = builder.Build();

// Ensure TickerNet operational store schema is applied
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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
