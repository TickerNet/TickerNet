extern alias worker;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerNet.Utilities;
using TickerNet.Utilities.Base;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Enums;
using TickerNet.Utilities.Interfaces.Managers;
using WorkerClient = worker::TickerNet.Worker.Internal.WorkerClient;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TickerNet.Hub.Tests;

/// <summary>
/// End-to-end tests: a scheduler with TickerNet.Hub on a local port and workers that connect to it.
/// Function registration is process-wide, so tests run one at a time and use unique names.
/// </summary>
public sealed class HubAndWorkerTests
{
    [Fact]
    public async Task Worker_registers_its_functions_with_the_hub()
    {
        await using var hub = await HubTestHost.StartAsync();
        var function = HubTestHost.UniqueName("Register");
        var node = HubTestHost.UniqueName("node");
        HubTestHost.RegisterFunction(function, (_, _, _) => Task.CompletedTask);

        using var workerHost = await hub.StartWorkerAsync(node);

        // The hub registers the functions right after acknowledging the worker.
        await HubTestHost.EventuallyAsync(
            () => Task.FromResult(TickerFunctionProvider.TickerFunctions.ContainsKey($"{function}@{node}")), found => found);
        Assert.True(hub.IsNodeConnected(node));
        await workerHost.StopAsync();
    }

    [Fact]
    public async Task Hub_sends_a_due_job_to_the_worker_and_records_it_as_done()
    {
        await using var hub = await HubTestHost.StartAsync();
        var function = HubTestHost.UniqueName("Echo");
        var node = HubTestHost.UniqueName("node");
        var received = new TaskCompletionSource<(Guid Id, string Request, string Name)>(TaskCreationOptions.RunContinuationsAsynchronously);
        HubTestHost.RegisterFunction(function, async (ct, _, context) =>
        {
            var request = await TickerRequestProvider.GetRequestAsync<string>(context, ct);
            received.TrySetResult((context.Id, request, context.FunctionName));
        });

        using var workerHost = await hub.StartWorkerAsync(node);

        var manager = hub.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        var added = await manager.AddAsync(new TimeTickerEntity
        {
            Function = $"{function}@{node}",
            ExecutionTime = DateTime.UtcNow,
            Request = TickerHelper.CreateTickerRequest("hello from the scheduler")
        });
        Assert.True(added.IsSucceeded, added.Exception?.ToString());

        var run = await received.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(added.Result.Id, run.Id);
        Assert.Equal("hello from the scheduler", run.Request);
        Assert.Equal(function, run.Name);

        var stored = await HubTestHost.EventuallyAsync(
            () => hub.Store.GetTimeTickerById(added.Result.Id),
            t => t?.Status == TickerStatus.Done);
        Assert.Equal(TickerStatus.Done, stored.Status);
        await workerHost.StopAsync();
    }

    [Fact]
    public async Task A_failure_on_the_worker_is_recorded_with_the_worker_error()
    {
        await using var hub = await HubTestHost.StartAsync();
        var function = HubTestHost.UniqueName("Fails");
        var node = HubTestHost.UniqueName("node");
        HubTestHost.RegisterFunction(function, (_, _, _) => throw new InvalidOperationException("boom on the worker"));

        using var workerHost = await hub.StartWorkerAsync(node);

        var manager = hub.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        var added = await manager.AddAsync(new TimeTickerEntity { Function = $"{function}@{node}", ExecutionTime = DateTime.UtcNow });
        Assert.True(added.IsSucceeded, added.Exception?.ToString());

        var stored = await HubTestHost.EventuallyAsync(
            () => hub.Store.GetTimeTickerById(added.Result.Id),
            t => t?.Status == TickerStatus.Failed);
        Assert.Contains("boom on the worker", stored.ExceptionMessage);
        await workerHost.StopAsync();
    }

    [Fact]
    public async Task Retries_run_on_the_scheduler_and_each_attempt_goes_to_the_worker()
    {
        await using var hub = await HubTestHost.StartAsync();
        var function = HubTestHost.UniqueName("Flaky");
        var node = HubTestHost.UniqueName("node");
        var attempts = new List<int>();
        HubTestHost.RegisterFunction(function, (_, _, context) =>
        {
            lock (attempts) attempts.Add(context.RetryCount);
            return context.RetryCount == 0
                ? throw new InvalidOperationException("first attempt fails")
                : Task.CompletedTask;
        });

        using var workerHost = await hub.StartWorkerAsync(node);

        var manager = hub.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        var added = await manager.AddAsync(new TimeTickerEntity
        {
            Function = $"{function}@{node}",
            ExecutionTime = DateTime.UtcNow,
            Retries = 1,
            RetryIntervals = [1]
        });
        Assert.True(added.IsSucceeded, added.Exception?.ToString());

        var stored = await HubTestHost.EventuallyAsync(
            () => hub.Store.GetTimeTickerById(added.Result.Id),
            t => t?.Status == TickerStatus.Done);
        Assert.Equal(TickerStatus.Done, stored.Status);
        lock (attempts) Assert.Equal([0, 1], attempts);
        await workerHost.StopAsync();
    }

    [Fact]
    public async Task Cancelling_a_run_on_the_scheduler_stops_it_on_the_worker()
    {
        await using var hub = await HubTestHost.StartAsync();
        var function = HubTestHost.UniqueName("Slow");
        var node = HubTestHost.UniqueName("node");
        var started = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedByCancel = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        HubTestHost.RegisterFunction(function, async (ct, _, context) =>
        {
            started.TrySetResult(context.Id);
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                stoppedByCancel.TrySetResult();
                throw;
            }
        });

        using var workerHost = await hub.StartWorkerAsync(node);

        var manager = hub.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        var added = await manager.AddAsync(new TimeTickerEntity { Function = $"{function}@{node}", ExecutionTime = DateTime.UtcNow });
        Assert.True(added.IsSucceeded, added.Exception?.ToString());

        var id = await started.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(TickerCancellationTokenManager.RequestTickerCancellationById(id));

        await stoppedByCancel.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var stored = await HubTestHost.EventuallyAsync(
            () => hub.Store.GetTimeTickerById(id),
            t => t?.Status == TickerStatus.Cancelled);
        Assert.Equal(TickerStatus.Cancelled, stored.Status);
        await workerHost.StopAsync();
    }

    [Fact]
    public async Task A_job_for_an_offline_node_waits_then_is_skipped()
    {
        await using var hub = await HubTestHost.StartAsync(options => options.WorkerWaitTimeout = TimeSpan.FromSeconds(1));
        var function = HubTestHost.UniqueName("Offline");
        var node = HubTestHost.UniqueName("node");
        HubTestHost.RegisterFunction(function, (_, _, _) => Task.CompletedTask);

        // Connect once so the hub knows the function, then take the worker away.
        var workerHost = await hub.StartWorkerAsync(node);
        await workerHost.StopAsync();
        workerHost.Dispose();
        await HubTestHost.EventuallyAsync(() => Task.FromResult(hub.IsNodeConnected(node)), connected => !connected);

        var manager = hub.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        var added = await manager.AddAsync(new TimeTickerEntity { Function = $"{function}@{node}", ExecutionTime = DateTime.UtcNow });
        Assert.True(added.IsSucceeded, added.Exception?.ToString());

        var stored = await HubTestHost.EventuallyAsync(
            () => hub.Store.GetTimeTickerById(added.Result.Id),
            t => t?.Status == TickerStatus.Skipped);
        Assert.Contains("offline", stored.SkippedReason ?? stored.ExceptionMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_worker_with_the_wrong_secret_is_refused()
    {
        await using var hub = await HubTestHost.StartAsync();
        var function = HubTestHost.UniqueName("Refused");
        var node = HubTestHost.UniqueName("node");
        HubTestHost.RegisterFunction(function, (_, _, _) => Task.CompletedTask);

        using var workerHost = await hub.StartWorkerAsync(node, secret: "a-different-secret-that-is-long-enough-000", waitForConnection: false);
        var client = workerHost.Services.GetRequiredService<WorkerClient>();

        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.False(client.Connected.IsCompleted);
        Assert.False(hub.IsNodeConnected(node));
        Assert.False(TickerFunctionProvider.TickerFunctions.ContainsKey($"{function}@{node}"));
        await workerHost.StopAsync();
    }

    [Fact]
    public async Task Jobs_added_on_the_worker_are_stored_on_the_scheduler_and_run_on_the_worker()
    {
        await using var hub = await HubTestHost.StartAsync();
        var function = HubTestHost.UniqueName("FromWorker");
        var node = HubTestHost.UniqueName("node");
        var ran = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        HubTestHost.RegisterFunction(function, (_, _, context) =>
        {
            ran.TrySetResult(context.Id);
            return Task.CompletedTask;
        });

        using var workerHost = await hub.StartWorkerAsync(node);

        var workerManager = workerHost.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        var added = await workerManager.AddAsync(new TimeTickerEntity { Function = function, ExecutionTime = DateTime.UtcNow });
        Assert.True(added.IsSucceeded, added.Exception?.ToString());

        var id = await ran.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(added.Result.Id, id);

        var stored = await HubTestHost.EventuallyAsync(
            () => hub.Store.GetTimeTickerById(id),
            t => t?.Status == TickerStatus.Done);
        Assert.Equal($"{function}@{node}", stored.Function);
        await workerHost.StopAsync();
    }

    [Fact]
    public async Task Worker_cron_functions_are_seeded_and_paused_while_the_node_is_offline()
    {
        await using var hub = await HubTestHost.StartAsync();
        var function = HubTestHost.UniqueName("Nightly");
        var node = HubTestHost.UniqueName("node");
        var remoteName = $"{function}@{node}";
        HubTestHost.RegisterFunction(function, (_, _, _) => Task.CompletedTask, cronExpression: "0 3 * * *");

        var workerHost = await hub.StartWorkerAsync(node);

        var seeded = await HubTestHost.EventuallyAsync(
            () => hub.Store.GetCronTickers(c => c.Function == remoteName, CancellationToken.None),
            crons => crons.Length == 1);
        Assert.Equal("0 3 * * *", seeded[0].Expression);
        Assert.False(seeded[0].IsSystemPaused);

        await workerHost.StopAsync();
        workerHost.Dispose();

        var paused = await HubTestHost.EventuallyAsync(
            () => hub.Store.GetCronTickers(c => c.Function == remoteName, CancellationToken.None),
            crons => crons.Length == 1 && crons[0].IsSystemPaused);
        Assert.True(paused[0].IsSystemPaused);
    }

    [Fact]
    public async Task The_obsolete_TickerQ_exception_still_skips_a_run()
    {
        await using var hub = await HubTestHost.StartAsync();
        var function = HubTestHost.UniqueName("LegacySkip");
#pragma warning disable CS0618 // SdkOfflineSkipException is obsolete; this checks it still works.
        HubTestHost.RegisterFunction(function, (_, _, _) => throw new Utilities.Exceptions.SdkOfflineSkipException("node is away"));
#pragma warning restore CS0618

        var manager = hub.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        var added = await manager.AddAsync(new TimeTickerEntity { Function = function, ExecutionTime = DateTime.UtcNow });
        Assert.True(added.IsSucceeded, added.Exception?.ToString());

        var stored = await HubTestHost.EventuallyAsync(
            () => hub.Store.GetTimeTickerById(added.Result.Id),
            t => t?.Status == TickerStatus.Skipped);
        Assert.Equal(TickerStatus.Skipped, stored.Status);
    }
}
