using Grpc.AspNetCore.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TickerNet.Hub.Internal;
using TickerNet.Utilities;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Interfaces;

namespace TickerNet.Hub;

/// <summary>
/// Sets up a TickerNet scheduler as a hub that worker processes connect to.
/// </summary>
public static class TickerNetHubServiceExtensions
{
    /// <summary>
    /// Lets worker processes running TickerNet.Worker connect to this scheduler and run jobs for it.
    /// Each worker declares the functions it can run; the scheduler stores and schedules them as
    /// <c>function@node</c> and sends each run to a connected worker of that node. Retries, status,
    /// history and the dashboard stay on the scheduler. Also call <c>app.MapTickerNetHub()</c>.
    /// </summary>
    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddTickerNetHub<TTimeTicker, TCronTicker>(
        this TickerOptionsBuilder<TTimeTicker, TCronTicker> builder,
        Action<TickerNetHubOptions> configure)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TickerNetHubOptions();
        configure(options);
        options.Validate();

        builder.ExternalProviderConfigServiceAction += services =>
        {
            services.AddSingleton(options);
            services.AddSingleton<WorkerRegistry>();
            services.AddSingleton<RemoteFunctionCatalog>();
            services.AddSingleton<HelloReplayGuard>();
            services.AddSingleton<IRemotePayloadLoader, RemotePayloadLoader<TTimeTicker, TCronTicker>>();
            services.AddSingleton<CronWriteGate>();
            services.AddSingleton<RemoteCronSeeder>();
            services.AddSingleton<HubEndpointMapper, HubEndpointMapper<TTimeTicker, TCronTicker>>();

            services.AddGrpc().AddServiceOptions<WorkerStreamService<TTimeTicker, TCronTicker>>(grpc =>
            {
                grpc.MaxReceiveMessageSize = options.MaxMessageSize;
                grpc.MaxSendMessageSize = options.MaxMessageSize;
            });

            services.AddHostedService<WorkerLivenessMonitor>();
            // Always added: with PauseCronTickersWhileOffline off it clears earlier pauses once and stops.
            services.AddHostedService<WorkerPauseMonitor<TTimeTicker, TCronTicker>>();

            DecorateTaskHandler(services);
        };

        return builder;
    }

    /// <summary>
    /// Maps the endpoint workers connect to (gRPC over HTTP/2). Call it on the scheduler app after
    /// <c>AddTickerNetHub</c>. The returned builder lets you add conventions, for example
    /// <c>.RequireHost("workers.example.internal")</c>.
    /// </summary>
    public static IEndpointConventionBuilder MapTickerNetHub(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var mapper = endpoints.ServiceProvider.GetService<HubEndpointMapper>()
            ?? throw new InvalidOperationException(
                "MapTickerNetHub needs AddTickerNetHub: call options.AddTickerNetHub(...) inside AddTickerNet(...).");
        return mapper.Map(endpoints);
    }

    private static void DecorateTaskHandler(IServiceCollection services)
    {
        var existing = services.LastOrDefault(d => d.ServiceType == typeof(ITickerExecutionTaskHandler))
            ?? throw new InvalidOperationException("TickerNet.Hub must be added inside AddTickerNet(...).");

        services.Remove(existing);
        services.AddSingleton<ITickerExecutionTaskHandler>(provider =>
        {
            var inner = existing switch
            {
                { ImplementationInstance: ITickerExecutionTaskHandler instance } => instance,
                { ImplementationFactory: { } factory } => (ITickerExecutionTaskHandler)factory(provider),
                _ => (ITickerExecutionTaskHandler)ActivatorUtilities.CreateInstance(provider, existing.ImplementationType!)
            };
            return new HubTaskHandler(inner);
        });
    }
}

/// <summary>Maps the worker endpoint without knowing the entity types at the call site.</summary>
internal abstract class HubEndpointMapper
{
    public abstract IEndpointConventionBuilder Map(IEndpointRouteBuilder endpoints);
}

internal sealed class HubEndpointMapper<TTimeTicker, TCronTicker> : HubEndpointMapper
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public override IEndpointConventionBuilder Map(IEndpointRouteBuilder endpoints)
        => endpoints.MapGrpcService<WorkerStreamService<TTimeTicker, TCronTicker>>();
}
