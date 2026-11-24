using System;
using System.Linq;
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
/// Tests for the code-first registration API: MapTicker, MapTickerGroup and
/// TickerFunctionBuilder (issue #19). Because the provider is fully static, each
/// test resets state via <see cref="ResetProvider"/> (same pattern as
/// <see cref="TickerFunctionProviderTests"/>).
/// </summary>
[Collection("TickerFunctionProviderState")]
public class MapTickerTests : IDisposable
{
    public MapTickerTests()
    {
        ResetProvider();
    }

    public void Dispose()
    {
        ResetProvider();
    }

    /// <summary>
    /// Clears every static field (public frozen dictionaries + private callback delegates)
    /// so that each test starts with a clean slate.
    /// </summary>
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

    private sealed class SimpleFunction : ITickerFunction
    {
        public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class OtherFunction : ITickerFunction
    {
        public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class RequestFunction : ITickerFunction<SampleRequest>
    {
        public Task ExecuteAsync(TickerFunctionContext<SampleRequest> context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class SampleRequest
    {
        public string Name { get; set; }
    }

    // ---------------------------------------------------------------
    // Function name building
    // ---------------------------------------------------------------
    [Fact]
    public void MapTicker_RegistersFunctionUnderTypeName()
    {
        var services = new ServiceCollection();

        services.MapTicker<SimpleFunction>();
        TickerFunctionProvider.Build();

        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey("SimpleFunction"));
        Assert.Equal("SimpleFunction", TickerFunctionProvider.GetFunctionName<SimpleFunction>());
        Assert.Equal(string.Empty, TickerFunctionProvider.TickerFunctions["SimpleFunction"].cronExpression);
        Assert.Equal(TickerTaskPriority.Normal, TickerFunctionProvider.TickerFunctions["SimpleFunction"].Priority);
        Assert.Equal(0, TickerFunctionProvider.TickerFunctions["SimpleFunction"].MaxConcurrency);
        Assert.Contains(services, d => d.ServiceType == typeof(SimpleFunction) && d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void MapTickerGroup_NamesFunctionsWithGroupPrefix()
    {
        var services = new ServiceCollection();

        services.MapTickerGroup("Billing").MapTicker<SimpleFunction>();
        TickerFunctionProvider.Build();

        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey("Billing.SimpleFunction"));
        Assert.Equal("Billing.SimpleFunction", TickerFunctionProvider.GetFunctionName<SimpleFunction>());
    }

    [Fact]
    public void MapTickerGroup_NameOverride_ReplacesClassName()
    {
        var services = new ServiceCollection();

        services.MapTickerGroup("Billing").MapTicker<SimpleFunction>("Custom");
        TickerFunctionProvider.Build();

        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey("Billing.Custom"));
        Assert.Equal("Billing.Custom", TickerFunctionProvider.GetFunctionName<SimpleFunction>());
    }

    [Fact]
    public void MapTickerGroup_CallbackOverload_RegistersFunctions()
    {
        var services = new ServiceCollection();

        services.MapTickerGroup("Billing", group => group.MapTicker<SimpleFunction>());
        TickerFunctionProvider.Build();

        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey("Billing.SimpleFunction"));
    }

    // ---------------------------------------------------------------
    // Group defaults
    // ---------------------------------------------------------------
    [Fact]
    public void MapTickerGroup_Defaults_AppliedToMappedFunctions()
    {
        var services = new ServiceCollection();

        services.MapTickerGroup("Billing")
            .WithPriority(TickerTaskPriority.High)
            .WithMaxConcurrency(3)
            .MapTicker<SimpleFunction>();
        TickerFunctionProvider.Build();

        var registered = TickerFunctionProvider.TickerFunctions["Billing.SimpleFunction"];
        Assert.Equal(TickerTaskPriority.High, registered.Priority);
        Assert.Equal(3, registered.MaxConcurrency);
    }

    [Fact]
    public void MapTickerGroup_DefaultLifetime_AppliedToServiceRegistration()
    {
        var services = new ServiceCollection();

        services.MapTickerGroup("Billing")
            .WithLifetime(ServiceLifetime.Singleton)
            .MapTicker<SimpleFunction>();

        Assert.Contains(services, d => d.ServiceType == typeof(SimpleFunction) && d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void MapTickerGroup_Defaults_DoNotLeakOutsideGroup()
    {
        var services = new ServiceCollection();

        services.MapTickerGroup("Billing")
            .WithPriority(TickerTaskPriority.High)
            .WithMaxConcurrency(3)
            .MapTicker<SimpleFunction>();
        services.MapTicker<OtherFunction>();
        TickerFunctionProvider.Build();

        Assert.Equal(TickerTaskPriority.High, TickerFunctionProvider.TickerFunctions["Billing.SimpleFunction"].Priority);
        Assert.Equal(3, TickerFunctionProvider.TickerFunctions["Billing.SimpleFunction"].MaxConcurrency);
        Assert.Equal(TickerTaskPriority.Normal, TickerFunctionProvider.TickerFunctions["OtherFunction"].Priority);
        Assert.Equal(0, TickerFunctionProvider.TickerFunctions["OtherFunction"].MaxConcurrency);
    }

    // ---------------------------------------------------------------
    // Request-type registration
    // ---------------------------------------------------------------
    [Fact]
    public void MapTicker_WithRequest_RegistersRequestType()
    {
        var services = new ServiceCollection();

        services.MapTicker<RequestFunction, SampleRequest>();
        TickerFunctionProvider.Build();

        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey("RequestFunction"));
        Assert.True(TickerFunctionProvider.TickerFunctionRequestTypes.ContainsKey("RequestFunction"));
        Assert.Equal(typeof(SampleRequest).FullName, TickerFunctionProvider.TickerFunctionRequestTypes["RequestFunction"].Item1);
        Assert.Equal(typeof(SampleRequest), TickerFunctionProvider.TickerFunctionRequestTypes["RequestFunction"].Item2);
    }

    [Fact]
    public void MapTickerGroup_WithRequest_RegistersPrefixedRequestType()
    {
        var services = new ServiceCollection();

        services.MapTickerGroup("Billing").MapTicker<RequestFunction, SampleRequest>();
        TickerFunctionProvider.Build();

        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey("Billing.RequestFunction"));
        Assert.True(TickerFunctionProvider.TickerFunctionRequestTypes.ContainsKey("Billing.RequestFunction"));
        Assert.Equal(typeof(SampleRequest), TickerFunctionProvider.TickerFunctionRequestTypes["Billing.RequestFunction"].Item2);
    }

    // ---------------------------------------------------------------
    // Lambda-based MapTicker
    // ---------------------------------------------------------------
    [Fact]
    public void MapTicker_Lambda_RegistersFunctionUnderGivenName()
    {
        var services = new ServiceCollection();

        services.MapTicker("LambdaFunc", (TickerFunctionContext _, CancellationToken _) => Task.CompletedTask);
        TickerFunctionProvider.Build();

        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey("LambdaFunc"));
    }

    [Fact]
    public void MapTicker_LambdaWithRequest_RegistersRequestType()
    {
        var services = new ServiceCollection();

        services.MapTicker<SampleRequest>("LambdaReqFunc", (TickerFunctionContext<SampleRequest> _, CancellationToken _) => Task.CompletedTask);
        TickerFunctionProvider.Build();

        Assert.True(TickerFunctionProvider.TickerFunctions.ContainsKey("LambdaReqFunc"));
        Assert.True(TickerFunctionProvider.TickerFunctionRequestTypes.ContainsKey("LambdaReqFunc"));
        Assert.Equal(typeof(SampleRequest), TickerFunctionProvider.TickerFunctionRequestTypes["LambdaReqFunc"].Item2);
    }

    // ---------------------------------------------------------------
    // TickerFunctionBuilder: WithCron / WithMaxConcurrency / WithPriority
    // ---------------------------------------------------------------
    [Fact]
    public void WithCron_ValidFivePartExpression_NormalizedAndStored()
    {
        var services = new ServiceCollection();

        services.MapTicker<SimpleFunction>().WithCron("0 0 * * *");
        TickerFunctionProvider.Build();

        Assert.Equal("0 0 0 * * *", TickerFunctionProvider.TickerFunctions["SimpleFunction"].cronExpression);
    }

    [Fact]
    public void WithCron_SixPartExpression_StoredAsIs()
    {
        var services = new ServiceCollection();

        services.MapTicker<SimpleFunction>().WithCron("30 0 0 * * *");
        TickerFunctionProvider.Build();

        Assert.Equal("30 0 0 * * *", TickerFunctionProvider.TickerFunctions["SimpleFunction"].cronExpression);
    }

    [Fact]
    public void WithCron_InvalidExpression_Throws()
    {
        var services = new ServiceCollection();
        var builder = services.MapTicker<SimpleFunction>();

        Assert.Throws<ArgumentException>(() => builder.WithCron("not a cron"));
    }

    [Fact]
    public void WithMaxConcurrency_And_WithPriority_AppliedAfterBuild()
    {
        var services = new ServiceCollection();

        services.MapTicker<SimpleFunction>()
            .WithMaxConcurrency(2)
            .WithPriority(TickerTaskPriority.Low);
        TickerFunctionProvider.Build();

        var registered = TickerFunctionProvider.TickerFunctions["SimpleFunction"];
        Assert.Equal(2, registered.MaxConcurrency);
        Assert.Equal(TickerTaskPriority.Low, registered.Priority);
    }
}
