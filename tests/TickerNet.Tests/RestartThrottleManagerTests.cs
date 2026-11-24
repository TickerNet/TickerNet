namespace TickerNet.Tests;

public class RestartThrottleManagerTests
{
    // The debounce window is 50 ms. On a busy machine (CI runs test classes in parallel) the timer
    // can fire much later, so these tests wait for the callback with a generous timeout instead of
    // sleeping a fixed time, and then check that no second callback follows.
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    [Fact]
    public async Task RequestRestart_TriggersCallback_AfterDebounceWindow()
    {
        var triggerCount = 0;
        using var manager = new RestartThrottleManager(() => Interlocked.Increment(ref triggerCount));

        manager.RequestRestart();

        await WaitUntilAsync(() => Volatile.Read(ref triggerCount) > 0, Generous);
        Assert.Equal(1, Volatile.Read(ref triggerCount));
    }

    [Fact]
    public async Task MultipleRequests_CoalesceIntoSingleCallback()
    {
        var triggerCount = 0;
        using var manager = new RestartThrottleManager(() => Interlocked.Increment(ref triggerCount));

        // Multiple rapid requests should coalesce
        manager.RequestRestart();
        manager.RequestRestart();
        manager.RequestRestart();

        await WaitUntilAsync(() => Volatile.Read(ref triggerCount) > 0, Generous);
        await Task.Delay(Settle);

        Assert.Equal(1, Volatile.Read(ref triggerCount));
    }

    [Fact]
    public async Task RequestRestart_ResetsTimer_OnSubsequentCalls()
    {
        var triggerCount = 0;
        // A 1 s window: the second request comes well inside it even when the machine is busy.
        using var manager = new RestartThrottleManager(
            () => Interlocked.Increment(ref triggerCount), TimeSpan.FromSeconds(1));

        manager.RequestRestart();
        await Task.Delay(30); // Less than the debounce window
        Assert.Equal(0, Volatile.Read(ref triggerCount));
        manager.RequestRestart(); // Resets the timer

        // Requests inside one window still produce a single callback.
        await WaitUntilAsync(() => Volatile.Read(ref triggerCount) > 0, Generous);
        await Task.Delay(Settle);

        Assert.Equal(1, Volatile.Read(ref triggerCount));
    }

    [Fact]
    public void Dispose_CanBeCalledSafely()
    {
        var manager = new RestartThrottleManager(() => { });
        var exception = Record.Exception(() => manager.Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_BeforeAnyRequest_DoesNotThrow()
    {
        var manager = new RestartThrottleManager(() => { });

        // Timer hasn't been created yet (lazy creation)
        var exception = Record.Exception(() => manager.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_AfterRequest_DoesNotThrow()
    {
        var manager = new RestartThrottleManager(() => { });
        manager.RequestRestart();

        var exception = Record.Exception(() => manager.Dispose());
        Assert.Null(exception);
    }
}
