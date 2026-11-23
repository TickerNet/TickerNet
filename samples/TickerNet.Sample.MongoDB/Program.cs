using TickerNet.Dashboard.DependencyInjection;
using TickerNet.DependencyInjection;
using TickerNet.MongoDB.DependencyInjection;
using TickerNet.Utilities.Base;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Interfaces.Managers;

var builder = WebApplication.CreateBuilder(args);

// TickerNet setup with MongoDB operational store.
// Start a local Mongo with: docker run -d -p 27017:27017 mongo:7
builder.Services.AddTickerNet(options =>
{
    options.AddOperationalStore(mongoOptions =>
    {
        mongoOptions.UseTickerNetMongoClient(
            connectionString: builder.Configuration["Mongo:ConnectionString"] ?? "mongodb://localhost:27017",
            databaseName: builder.Configuration["Mongo:Database"] ?? "tickernet");
    });
    options.AddDashboard();
});

var app = builder.Build();

// Activate TickerNet job processor (indexes are provisioned automatically by IHostedService)
app.UseTickerNet();

// Minimal endpoint to schedule the sample job
app.MapPost("/schedule-sample", async (ITimeTickerManager<TimeTickerEntity> manager) =>
{
    var result = await manager.AddAsync(new TimeTickerEntity
    {
        Function = "MongoSample_HelloWorld",
        ExecutionTime = DateTime.UtcNow.AddSeconds(5)
    });

    return Results.Ok(new { result.Result.Id, ScheduledFor = result.Result.ExecutionTime });
});

app.Run();

public class SampleJobs
{
    [TickerFunction("MongoSample_HelloWorld")]
    public Task HelloWorldAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[Mongo] Hello from TickerNet! Id={context.Id}, ScheduledFor={context.ScheduledFor:O}");
        return Task.CompletedTask;
    }
}
