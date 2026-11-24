using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TickerNet.DependencyInjection;
using TickerNet.Utilities;
using TickerNet.Utilities.Base;
using TickerNet.Utilities.Enums;
using TickerNet.Utilities.Interfaces;
using Xunit;

namespace TickerNet.Tests;

/// <summary>
/// Tests for the code-first MapTicker* API. The function registry is static, so
/// each test resets it (same pattern as TickerFunctionProviderTests) and the
/// tests share that collection to stay off other registry tests.
/// </summary>
[Collection("TickerFunctionProviderState")]
public class TickerEndpointExtensionsTests : IDisposable
{
    public TickerEndpointExtensionsTests() => ResetProvider();

    public void Dispose() => ResetProvider();

    private static void ResetProvider()
    {
        var type = typeof(TickerFunctionProvider);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        type.GetField("TickerFunctions", flags)!.SetValue(null,
            System.Collections.Frozen.FrozenDictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>.Empty);
        type.GetField("TickerFunctionRequestTypes", flags)!.SetValue(null,
            System.Collections.Frozen.FrozenDictionary<string, (string, Type)>.Empty);
        type.GetField("TickerFunctionRequestInfos", flags)!.SetValue(null,
            System.Collections.Frozen.FrozenDictionary<string, (string, string)>.Empty);
        type.GetField("_functionRegistrations", flags)!.SetValue(null, null);
        type.GetField("_requestTypeRegistrations", flags)!.SetValue(null, null);
        type.GetField("_requestInfoRegistrations", flags)!.SetValue(null, null);
        type.GetProperty("IsBuilt", flags)!.SetValue(null, false);
    }

    [Fact]
    public void MapTicker_uses_the_class_name()
    {
        var services = new ServiceCollection();

        services.MapTicker<MapTickerSampleJob>();
        TickerFunctionProvider.Build();

        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey(nameof(MapTickerSampleJob)));
        Assert.Equal(nameof(MapTickerSampleJob), TickerFunctionProvider.GetFunctionName<MapTickerSampleJob>());
    }

    [Fact]
    public void MapTicker_with_request_registers_the_request_type()
    {
        var services = new ServiceCollection();

        services.MapTicker<MapTickerRequestJob, MapTickerRequest>();
        TickerFunctionProvider.Build();

        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey(nameof(MapTickerRequestJob)));
        var (typeName, type) = TickerFunctionProvider.TickerFunctionRequestTypes[nameof(MapTickerRequestJob)];
        Assert.Equal(typeof(MapTickerRequest).FullName, typeName);
        Assert.Equal(typeof(MapTickerRequest), type);
    }

    [Fact]
    public void MapTickerGroup_prefixes_names_and_honours_an_override()
    {
        var services = new ServiceCollection();

        var group = services.MapTickerGroup("Reports");
        group.MapTicker<MapTickerSampleJob>();
        group.MapTicker<MapTickerSampleJob>("Nightly");
        TickerFunctionProvider.Build();

        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey("Reports.MapTickerSampleJob"));
        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey("Reports.Nightly"));
    }

    [Fact]
    public void MapTickerGroup_applies_its_defaults()
    {
        var services = new ServiceCollection();

        services.MapTickerGroup("Reports", group => group
                .WithPriority(TickerTaskPriority.High)
                .WithMaxConcurrency(3)
                .MapTicker<MapTickerSampleJob>());
        TickerFunctionProvider.Build();

        var entry = TickerFunctionProvider.TickerFunctions["Reports.MapTickerSampleJob"];
        Assert.Equal(TickerTaskPriority.High, entry.Priority);
        Assert.Equal(3, entry.MaxConcurrency);
    }

    [Fact]
    public void WithCron_sets_the_expression_and_rejects_an_invalid_one()
    {
        var services = new ServiceCollection();

        services.MapTicker<MapTickerSampleJob>().WithCron("*/5 * * * *");
        TickerFunctionProvider.Build();
        Assert.Equal("0 */5 * * * *", TickerFunctionProvider.TickerFunctions[nameof(MapTickerSampleJob)].cronExpression);

        Assert.Throws<ArgumentException>(() => services.MapTicker<MapTickerSampleJob>().WithCron("not a cron"));
    }
}

public class MapTickerSampleJob : ITickerFunction
{
    public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}

public class MapTickerRequest
{
    public string Name { get; set; } = "";
}

public class MapTickerRequestJob : ITickerFunction<MapTickerRequest>
{
    public Task ExecuteAsync(TickerFunctionContext<MapTickerRequest> context, CancellationToken cancellationToken) => Task.CompletedTask;
}
