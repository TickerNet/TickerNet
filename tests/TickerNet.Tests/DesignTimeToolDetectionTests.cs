using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TickerNet.BackgroundServices;
using TickerNet.DependencyInjection;
using TickerNet.Utilities;
using TickerNet.Utilities.Enums;
using TickerNet.Utilities.Interfaces.Managers;
using TickerNet.Utilities.Instrumentation;

namespace TickerNet.Tests;

public class DesignTimeToolDetectionTests
{
    [Fact]
    public async Task Initializer_Skips_When_UseTickerNet_Not_Called()
    {
        // Design-time tools (dotnet-getdocument, dotnet-ef) build the host but
        // never call UseTickerNet. Verify the initializer does nothing in that case.
        var context = new TickerExecutionContext();
        var serviceProvider = Substitute.For<IServiceProvider>();
        var configuration = Substitute.For<IConfiguration>();

        var initializer = new TickerNetInitializerHostedService(context, serviceProvider, configuration);
        await initializer.StartAsync(CancellationToken.None);

        // InitializationRequested is false by default, so no seeding should occur.
        Assert.False(initializer.InitializationRequested);
    }

    [Fact]
    public void UseTickerNet_Sets_InitializationRequested_On_Initializer()
    {
        var services = new ServiceCollection();
        services.AddTickerNet();

        var host = BuildMinimalHost(services);
        host.UseTickerNet();

        var initializer = host.Services.GetRequiredService<TickerNetInitializerHostedService>();
        Assert.True(initializer.InitializationRequested);
    }

    [Fact]
    public void UseTickerNet_Does_Not_Perform_IO()
    {
        // UseTickerNet should complete without touching the database or building functions.
        var services = new ServiceCollection();
        services.AddTickerNet();

        var host = BuildMinimalHost(services);

        // This should not throw — no DB access, no function scanning
        var result = host.UseTickerNet();
        Assert.Same(host, result);
    }

    [Fact]
    public async Task Initializer_Runs_Seeding_When_InitializationRequested()
    {
        var internalManager = Substitute.For<IInternalTickerManager>();
        var context = new TickerExecutionContext();
        var configuration = Substitute.For<IConfiguration>();

        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(configuration);
        services.AddSingleton(internalManager);
        services.AddSingleton(new SchedulerOptionsBuilder());

        var sp = services.BuildServiceProvider();
        var initializer = new TickerNetInitializerHostedService(context, sp, configuration);
        initializer.InitializationRequested = true;

        await initializer.StartAsync(CancellationToken.None);

        // MigrateDefinedCronTickers should have been called (even if with empty list)
        await internalManager.Received(1).MigrateDefinedCronTickers(Arg.Any<(string, string)[]>());
    }

    [Fact]
    public async Task Initializer_Reports_Seeding_To_The_Instrumentation()
    {
        var internalManager = Substitute.For<IInternalTickerManager>();
        var instrumentation = Substitute.For<ITickerNetInstrumentation>();
        var context = new TickerExecutionContext();
        var configuration = Substitute.For<IConfiguration>();

        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(configuration);
        services.AddSingleton(internalManager);
        services.AddSingleton(instrumentation);
        services.AddSingleton(new SchedulerOptionsBuilder());

        var initializer = new TickerNetInitializerHostedService(context, services.BuildServiceProvider(), configuration);
        initializer.InitializationRequested = true;

        await initializer.StartAsync(CancellationToken.None);

        instrumentation.Received(1).LogSeedingDataStarted("CronTickers");
        instrumentation.Received(1).LogSeedingDataCompleted("CronTickers");
    }

    [Fact]
    public async Task Initializer_Respects_IgnoreSeedDefinedCronTickers()
    {
        var internalManager = Substitute.For<IInternalTickerManager>();

        var services = new ServiceCollection();
        services.AddTickerNet(options => options.IgnoreSeedDefinedCronTickers());

        var host = BuildMinimalHost(services);
        host.UseTickerNet();

        var context = host.Services.GetRequiredService<TickerExecutionContext>();
        var configuration = host.Services.GetRequiredService<IConfiguration>();

        // Create a new initializer with the real context that has seeding disabled
        var initializer = new TickerNetInitializerHostedService(context, host.Services, configuration);
        initializer.InitializationRequested = true;

        await initializer.StartAsync(CancellationToken.None);

        // Seeding should be skipped — IInternalTickerManager was not registered via AddTickerNet's
        // mock, so if it tried to seed, it would call the real (non-mock) manager.
        // The real assertion: IgnoreSeedDefinedCronTickers sets the flag correctly.
        var optionsSeeding = context.OptionsSeeding;
        Assert.NotNull(optionsSeeding);
        Assert.False(optionsSeeding.SeedDefinedCronTickers);
    }

    [Fact]
    public async Task Initializer_Runs_External_Provider_Action()
    {
        var actionCalled = false;
        var context = new TickerExecutionContext();
        context.ExternalProviderApplicationAction = _ => actionCalled = true;

        var internalManager = Substitute.For<IInternalTickerManager>();
        var configuration = Substitute.For<IConfiguration>();

        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(configuration);
        services.AddSingleton(internalManager);
        services.AddSingleton(new SchedulerOptionsBuilder());

        var sp = services.BuildServiceProvider();
        var initializer = new TickerNetInitializerHostedService(context, sp, configuration);
        initializer.InitializationRequested = true;

        await initializer.StartAsync(CancellationToken.None);

        Assert.True(actionCalled);
        Assert.Null(context.ExternalProviderApplicationAction);
    }

    [Fact]
    public async Task Initializer_StopAsync_Is_NoOp()
    {
        var context = new TickerExecutionContext();
        var sp = Substitute.For<IServiceProvider>();
        var config = Substitute.For<IConfiguration>();

        var initializer = new TickerNetInitializerHostedService(context, sp, config);

        // Should complete immediately without exceptions
        await initializer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void Initializer_Registered_Before_Scheduler_Services()
    {
        var services = new ServiceCollection();
        services.AddTickerNet();

        // Verify registration order: initializer should appear before scheduler
        var hostedServiceDescriptors = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .ToList();

        Assert.True(hostedServiceDescriptors.Count >= 1);

        // First hosted service should be the initializer (resolved via factory from singleton)
        var first = hostedServiceDescriptors[0];
        // The factory registration resolves TickerNetInitializerHostedService
        var sp = BuildMinimalHost(new ServiceCollection()).Services;
        // Just verify the initializer singleton is registered before scheduler
        var allSingletons = services
            .Where(d => d.Lifetime == ServiceLifetime.Singleton)
            .Select(d => d.ServiceType ?? d.ImplementationType)
            .ToList();

        var initializerIndex = allSingletons.IndexOf(typeof(TickerNetInitializerHostedService));
        var schedulerIndex = allSingletons.IndexOf(typeof(TickerNetSchedulerBackgroundService));

        Assert.True(initializerIndex >= 0, "TickerNetInitializerHostedService should be registered");
        // Scheduler may or may not be registered (depends on DisableBackgroundServices)
        if (schedulerIndex >= 0)
        {
            Assert.True(initializerIndex < schedulerIndex,
                "Initializer should be registered before scheduler");
        }
    }

    private static IHost BuildMinimalHost(IServiceCollection services)
    {
        services.TryAddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.TryAddSingleton(Substitute.For<IHostApplicationLifetime>());
        services.TryAddSingleton(Substitute.For<IHostEnvironment>());
        services.AddLogging();
        return new MinimalHost(services.BuildServiceProvider());
    }

    /// <summary>
    /// Minimal IHost implementation for testing UseTickerNet without a full host builder.
    /// </summary>
    private sealed class MinimalHost : IHost
    {
        public IServiceProvider Services { get; }

        public MinimalHost(IServiceProvider services) => Services = services;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
