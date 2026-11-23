extern alias worker;

using Microsoft.Extensions.DependencyInjection;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Enums;
using TickerNet.Utilities.Interfaces.Managers;
using WorkerClient = worker::TickerNet.Worker.Internal.WorkerClient;

namespace TickerNet.Hub.Tests;

/// <summary>
/// Several scheduler instances, each running the hub and sharing one store (the in-memory store is
/// shared within a test process), with workers connected to all of them.
/// </summary>
public sealed class SeveralSchedulersTests
{
    [Fact]
    public async Task A_worker_connected_to_every_scheduler_runs_jobs_from_each()
    {
        await using var schedulerA = await HubTestHost.StartAsync();
        await using var schedulerB = await HubTestHost.StartAsync();
        var function = HubTestHost.UniqueName("Shared");
        var node = HubTestHost.UniqueName("node");
        var ran = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        HubTestHost.RegisterFunction(function, (_, _, context) =>
        {
            ran.Add(context.Id);
            return Task.CompletedTask;
        });

        using var workerHost = await schedulerA.StartWorkerAsync(node, configure: w => w.HubAddresses.Add(schedulerB.Address));
        var client = workerHost.Services.GetRequiredService<WorkerClient>();
        await client.AllConnected.WaitAsync(TimeSpan.FromSeconds(15));
        await HubTestHost.EventuallyAsync(() => Task.FromResult(schedulerB.IsNodeConnected(node)), connected => connected);
        Assert.Equal(2, client.WorkerIds.Count);

        var ids = new List<Guid>();
        foreach (var scheduler in new[] { schedulerA, schedulerB })
        {
            var manager = scheduler.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
            var added = await manager.AddAsync(new TimeTickerEntity { Function = $"{function}@{node}", ExecutionTime = DateTime.UtcNow });
            Assert.True(added.IsSucceeded, added.Exception?.ToString());
            ids.Add(added.Result.Id);
        }

        foreach (var id in ids)
        {
            var stored = await HubTestHost.EventuallyAsync(
                () => schedulerA.Store.GetTimeTickerById(id),
                t => t?.Status is TickerStatus.Done or TickerStatus.Failed or TickerStatus.Skipped);
            Assert.Equal(TickerStatus.Done, stored.Status);
            Assert.Contains(id, ran);
        }

        await workerHost.StopAsync();
    }

    [Fact]
    public async Task A_worker_keeps_working_through_one_scheduler_when_the_other_goes_away()
    {
        var schedulerA = await HubTestHost.StartAsync();
        await using var schedulerB = await HubTestHost.StartAsync();
        var function = HubTestHost.UniqueName("Survivor");
        var node = HubTestHost.UniqueName("node");
        HubTestHost.RegisterFunction(function, (_, _, _) => Task.CompletedTask);

        using var workerHost = await schedulerA.StartWorkerAsync(node, configure: w => w.HubAddresses.Add(schedulerB.Address));
        await workerHost.Services.GetRequiredService<WorkerClient>().AllConnected.WaitAsync(TimeSpan.FromSeconds(15));
        await HubTestHost.EventuallyAsync(() => Task.FromResult(schedulerB.IsNodeConnected(node)), connected => connected);

        await schedulerA.DisposeAsync();

        // Jobs the worker adds still reach the store through scheduler B.
        var workerManager = workerHost.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        var added = await workerManager.AddAsync(new TimeTickerEntity { Function = function, ExecutionTime = DateTime.UtcNow });
        Assert.True(added.IsSucceeded, added.Exception?.ToString());

        var stored = await HubTestHost.EventuallyAsync(
            () => schedulerB.Store.GetTimeTickerById(added.Result.Id),
            t => t?.Status is TickerStatus.Done or TickerStatus.Failed or TickerStatus.Skipped);
        Assert.Equal(TickerStatus.Done, stored.Status);

        await workerHost.StopAsync();
    }

    [Fact]
    public void A_worker_needs_at_least_one_hub_address()
    {
        var options = new worker::TickerNet.Worker.TickerNetWorkerOptions { SharedSecret = HubTestHost.Secret, NodeName = "reports" };
        Assert.Throws<InvalidOperationException>(options.Validate);

        options.HubAddresses.Add(new Uri("https://scheduler-0.internal:5001"));
        options.HubAddresses.Add(new Uri("https://scheduler-1.internal:5001"));
        options.Validate();
        Assert.Equal(2, options.EffectiveHubAddresses.Count);
    }
}
