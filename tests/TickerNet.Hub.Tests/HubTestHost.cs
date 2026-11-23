extern alias worker;

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerNet.DependencyInjection;
using TickerNet.Hub.Internal;
using TickerNet.Utilities;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Enums;
using TickerNet.Utilities.Interfaces;
using worker::TickerNet.Worker;
using WorkerClient = worker::TickerNet.Worker.Internal.WorkerClient;

namespace TickerNet.Hub.Tests;

/// <summary>A scheduler running TickerNet.Hub on a local HTTP/2 port, with in-memory storage.</summary>
internal sealed class HubTestHost : IAsyncDisposable
{
    public const string Secret = "test-shared-secret-0123456789-abcdefghij";

    private HubTestHost(WebApplication app, Uri address, CapturingLoggerProvider log)
    {
        App = app;
        Address = address;
        Log = log;
    }

    /// <summary>The hub's recent TickerNet log lines, for failure messages.</summary>
    public CapturingLoggerProvider Log { get; }

    public WebApplication App { get; }
    public Uri Address { get; }
    public IServiceProvider Services => App.Services;
    public WorkerRegistry Registry => Services.GetRequiredService<WorkerRegistry>();

    public bool IsNodeConnected(string nodeName)
        => Registry.All().Any(c => string.Equals(c.NodeName, nodeName, StringComparison.OrdinalIgnoreCase));

    public ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> Store
        => Services.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

    public static async Task<HubTestHost> StartAsync(Action<TickerNetHubOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var log = new CapturingLoggerProvider();
        builder.Logging.AddProvider(log);
        if (Environment.GetEnvironmentVariable("TICKERNET_TEST_LOGS") == "1")
            builder.Logging.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Debug).AddFilter("Microsoft", LogLevel.Warning).AddFilter("Grpc", LogLevel.Debug);
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));

        builder.Services.AddTickerNet(options => options.AddTickerNetHub(hub =>
        {
            hub.SharedSecret = Secret;
            hub.WorkerWaitTimeout = TimeSpan.FromSeconds(5);
            // Test workers send a heartbeat every second.
            hub.WorkerTimeout = TimeSpan.FromSeconds(4);
            configure?.Invoke(hub);
        }));

        var app = builder.Build();
        app.UseTickerNet();
        app.MapTickerNetHub();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new HubTestHost(app, new Uri(address), log);
    }

    public async Task<IHost> StartWorkerAsync(
        string nodeName,
        string secret = Secret,
        bool waitForConnection = true,
        Func<string, bool>? offers = null,
        Action<TickerNetWorkerOptions>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        if (Environment.GetEnvironmentVariable("TICKERNET_TEST_LOGS") == "1")
            builder.Logging.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Debug).AddFilter("Microsoft", LogLevel.Warning);
        builder.Services.AddTickerNet(options => options.AddTickerNetWorker(worker =>
        {
            worker.HubAddress = Address;
            worker.SharedSecret = secret;
            worker.NodeName = nodeName;
            worker.HeartbeatInterval = TimeSpan.FromSeconds(1);
            worker.MaxReconnectDelay = TimeSpan.FromSeconds(1);
            worker.FunctionFilter = offers;
            configure?.Invoke(worker);
        }));

        var host = builder.Build();
        host.UseTickerNet();
        await host.StartAsync();

        if (waitForConnection)
        {
            var client = host.Services.GetRequiredService<WorkerClient>();
            await client.Connected.WaitAsync(TimeSpan.FromSeconds(15));
            // The hub registers the worker right after acknowledging it.
            await EventuallyAsync(() => Task.FromResult(Registry.All().Any(c => c.WorkerId == client.WorkerId)), registered => registered);
        }

        return host;
    }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
    }

    /// <summary>Registers a function in this process, as the source generator would.</summary>
    public static void RegisterFunction(string name, TickerFunctionDelegate function, string cronExpression = "")
    {
        TickerFunctionProvider.RegisterFunctions(
            new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                [name] = (cronExpression, TickerTaskPriority.Normal, function, 0)
            });
        TickerFunctionProvider.Build();
    }

    public static string UniqueName(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    public static async Task<T> EventuallyAsync<T>(Func<Task<T>> probe, Func<T, bool> done, TimeSpan? timeout = null, Func<T, string>? describe = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (true)
        {
            var value = await probe();
            if (done(value)) return value;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Condition not met in time. Last value: {(describe != null ? describe(value) : value)}");
            await Task.Delay(50);
        }
    }
}
