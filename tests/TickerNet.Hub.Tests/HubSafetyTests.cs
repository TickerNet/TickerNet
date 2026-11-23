extern alias worker;

using System.Diagnostics;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerNet.Utilities;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Enums;
using TickerNet.Utilities.Interfaces.Managers;
using RawHello = worker::TickerNet.Hub.Protocol.V1.Hello;
using RawWorkerEvent = worker::TickerNet.Hub.Protocol.V1.WorkerEvent;
using WorkerHubClient = worker::TickerNet.Hub.Protocol.V1.WorkerHub.WorkerHubClient;

namespace TickerNet.Hub.Tests;

/// <summary>
/// Safety properties of the hub and worker: who may do what, and what survives restarts,
/// redeploys and replicas on different versions.
/// </summary>
public sealed class HubSafetyTests
{
    [Fact]
    public async Task A_call_without_authentication_headers_is_refused_before_the_hub_reads_it()
    {
        await using var hub = await HubTestHost.StartAsync();
        using var channel = GrpcChannel.ForAddress(hub.Address);
        var client = new WorkerHubClient(channel);

        using var call = client.OpenWorkerStream();
        // Even a Hello that looks fine is never read without valid headers.
        try { await call.RequestStream.WriteAsync(new RawWorkerEvent { Hello = new RawHello { NodeName = "intruder", ProtocolVersion = 1 } }); }
        catch (RpcException) { /* the hub may already have closed the call */ }

        var error = await Assert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(CancellationToken.None));
        Assert.Equal(StatusCode.Unauthenticated, error.StatusCode);
        Assert.False(hub.IsNodeConnected("intruder"));
    }

    [Fact]
    public async Task A_worker_cant_schedule_another_nodes_functions()
    {
        await using var hub = await HubTestHost.StartAsync();
        var ownFunction = HubTestHost.UniqueName("Own");
        var otherFunction = HubTestHost.UniqueName("Other");
        var nodeA = HubTestHost.UniqueName("a");
        var nodeB = HubTestHost.UniqueName("b");
        HubTestHost.RegisterFunction(ownFunction, (_, _, _) => Task.CompletedTask);
        HubTestHost.RegisterFunction(otherFunction, (_, _, _) => Task.CompletedTask);

        using var workerA = await hub.StartWorkerAsync(nodeA, offers: name => name == ownFunction);
        using var workerB = await hub.StartWorkerAsync(nodeB, offers: name => name == otherFunction);

        // Worker A names node B's function directly. (In one test process its own registry can see
        // the name, so the hub's check is what's being tested.)
        var managerOnA = workerA.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        var attempt = await managerOnA.AddAsync(new TimeTickerEntity
        {
            Function = $"{otherFunction}@{nodeB}",
            ExecutionTime = DateTime.UtcNow.AddHours(1)
        });

        Assert.False(attempt.IsSucceeded);
        var stored = await hub.Store.GetTimeTickers(t => t.Function == $"{otherFunction}@{nodeB}");
        Assert.Empty(stored);

        await workerA.StopAsync();
        await workerB.StopAsync();
    }

    [Fact]
    public async Task A_job_keeps_running_when_the_caller_that_added_it_goes_away()
    {
        await using var hub = await HubTestHost.StartAsync();
        var function = HubTestHost.UniqueName("Survives");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        HubTestHost.RegisterFunction(function, async (ct, _, _) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
        });

        // The caller's token (an HTTP request, a worker's connection) ends right after the job is added.
        using var callerToken = new CancellationTokenSource();
        var manager = hub.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        var added = await manager.AddAsync(new TimeTickerEntity { Function = function, ExecutionTime = DateTime.UtcNow }, callerToken.Token);
        Assert.True(added.IsSucceeded, added.Exception?.ToString());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(20));
        callerToken.Cancel();

        await Task.Delay(200);
        release.TrySetResult();

        var stored = await HubTestHost.EventuallyAsync(
            () => hub.Store.GetTimeTickerById(added.Result.Id),
            t => t?.Status is TickerStatus.Done or TickerStatus.Cancelled or TickerStatus.Failed);
        Assert.Equal(TickerStatus.Done, stored.Status);
    }

    [Fact]
    public async Task Stopping_a_worker_lets_its_running_job_finish()
    {
        await using var hub = await HubTestHost.StartAsync();
        var function = HubTestHost.UniqueName("Drain");
        var node = HubTestHost.UniqueName("node");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        HubTestHost.RegisterFunction(function, async (ct, _, _) =>
        {
            started.TrySetResult();
            await Task.Delay(TimeSpan.FromSeconds(1.5), ct);
        });

        var workerHost = await hub.StartWorkerAsync(node);
        var manager = hub.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        var added = await manager.AddAsync(new TimeTickerEntity { Function = $"{function}@{node}", ExecutionTime = DateTime.UtcNow });
        Assert.True(added.IsSucceeded, added.Exception?.ToString());

        await started.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await workerHost.StopAsync();
        workerHost.Dispose();

        var stored = await HubTestHost.EventuallyAsync(
            () => hub.Store.GetTimeTickerById(added.Result.Id),
            t => t?.Status is TickerStatus.Done or TickerStatus.Failed or TickerStatus.Cancelled);
        Assert.Equal(TickerStatus.Done, stored.Status);
    }

    [Fact]
    public async Task Runs_only_go_to_replicas_that_have_the_function()
    {
        await using var hub = await HubTestHost.StartAsync();
        var common = HubTestHost.UniqueName("Common");
        var added = HubTestHost.UniqueName("NewInV2");
        var node = HubTestHost.UniqueName("node");
        HubTestHost.RegisterFunction(common, (_, _, _) => Task.CompletedTask);
        HubTestHost.RegisterFunction(added, (_, _, _) => Task.CompletedTask);

        // v1 doesn't have the new function; v2 does.
        using var v1 = await hub.StartWorkerAsync(node, offers: name => name == common);
        using var v2 = await hub.StartWorkerAsync(node, offers: name => name == common || name == added);

        var manager = hub.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        var ids = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            var result = await manager.AddAsync(new TimeTickerEntity { Function = $"{added}@{node}", ExecutionTime = DateTime.UtcNow });
            Assert.True(result.IsSucceeded, result.Exception?.ToString());
            ids.Add(result.Result.Id);
        }

        foreach (var id in ids)
        {
            var stored = await HubTestHost.EventuallyAsync(
                () => hub.Store.GetTimeTickerById(id),
                t => t?.Status is TickerStatus.Done or TickerStatus.Failed or TickerStatus.Skipped);
            Assert.Equal(TickerStatus.Done, stored.Status);
        }

        await v1.StopAsync();
        await v2.StopAsync();
    }

    [Fact]
    public async Task A_worker_without_functions_doesnt_remove_seeded_cron_jobs()
    {
        await using var hub = await HubTestHost.StartAsync();
        var function = HubTestHost.UniqueName("Kept");
        var node = HubTestHost.UniqueName("node");
        var remoteName = $"{function}@{node}";
        HubTestHost.RegisterFunction(function, (_, _, _) => Task.CompletedTask, cronExpression: "0 4 * * *");

        var full = await hub.StartWorkerAsync(node);
        await HubTestHost.EventuallyAsync(
            () => hub.Store.GetCronTickers(c => c.Function == remoteName, CancellationToken.None),
            crons => crons.Length == 1);
        await full.StopAsync();
        full.Dispose();

        // A replica started without its functions (for example, UseTickerNet() forgotten).
        using var empty = await hub.StartWorkerAsync(node, offers: _ => false);
        await Task.Delay(TimeSpan.FromSeconds(3));

        var crons = await hub.Store.GetCronTickers(c => c.Function == remoteName, CancellationToken.None);
        Assert.Single(crons);
        Assert.True(crons[0].IsSystemPaused);
        await empty.StopAsync();
    }

    [Fact]
    public async Task The_hub_stops_promptly_with_workers_connected()
    {
        var hub = await HubTestHost.StartAsync();
        var function = HubTestHost.UniqueName("Idle");
        var node = HubTestHost.UniqueName("node");
        HubTestHost.RegisterFunction(function, (_, _, _) => Task.CompletedTask);
        using var workerHost = await hub.StartWorkerAsync(node);

        var stopwatch = Stopwatch.StartNew();
        await hub.DisposeAsync();
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Stopping took {stopwatch.Elapsed}.");
        await workerHost.StopAsync();
    }

    [Fact]
    public void A_plain_http_hub_on_another_machine_needs_an_explicit_opt_in()
    {
        var options = new worker::TickerNet.Worker.TickerNetWorkerOptions
        {
            HubAddress = new Uri("http://scheduler.internal:5081"),
            SharedSecret = HubTestHost.Secret,
            NodeName = "reports"
        };
        Assert.Throws<InvalidOperationException>(options.Validate);

        options.AllowUnencryptedHttp = true;
        options.Validate();
    }
}
