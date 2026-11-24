using NSubstitute;
using TickerNet.Utilities.Enums;
using TickerNet.Utilities.Interfaces;
using TickerNet.Utilities.Interfaces.Managers;
using Microsoft.Extensions.DependencyInjection;
using TickerNet.Utilities.Instrumentation;
using TickerNet.Utilities;
using TickerNet.Utilities.Models;

namespace TickerNet.Tests;

[Collection("TickerCancellationTokenState")]
public class RetryBehaviorTests : IDisposable
{
    public void Dispose()
    {
        TickerCancellationTokenManager.CleanUpTickerCancellationTokens();
    }

    // End-to-end unit tests that call the public ExecuteTaskAsync with a CronTickerOccurrence
    // so RunContextFunctionAsync + retry logic is exercised. Tests use short intervals (1..3s).

    [Fact()]
    public async Task ExecuteTaskAsync_CronTickerOccurrence_AppliesRetryIntervals_AndUpdatesRetryCount()
    {
        // Arrange: cron occurrence -> RunContextFunctionAsync path
        // Use three distinct short intervals so we can verify mapping without overly long waits
        var (handler, context, _, attempts) = SetupRetryTestFixture([1, 2, 3], retries: 3);

        // Act
        await handler.ExecuteTaskAsync(context, isDue: true);

        // Assert - initial + 3 retries = 4 attempts
        Assert.Equal(4, attempts.Count);
        for (int i = 0; i < 4; i++)
            Assert.Equal(i, attempts[i].RetryCount);

        // Verify mapped retry intervals produced the expected spacing between attempts
        var timeDiffs = new[]
        {
            (attempts[1].Timestamp - attempts[0].Timestamp).TotalSeconds,
            (attempts[2].Timestamp - attempts[1].Timestamp).TotalSeconds,
            (attempts[3].Timestamp - attempts[2].Timestamp).TotalSeconds,
        };

        // Each spacing reflects its configured interval: never shorter (minus a little clock slack),
        // and in increasing order. The upper bound is generous because a busy CI runner can delay a
        // timer by several hundred milliseconds; it only guards against a much longer wait.
        Assert.InRange(timeDiffs[0], 0.8, 2.0); // first retry uses 1s
        Assert.InRange(timeDiffs[1], 1.8, 3.0); // second retry uses 2s
        Assert.InRange(timeDiffs[2], 2.8, 4.0); // third retry uses 3s
        Assert.True(timeDiffs[0] < timeDiffs[1] && timeDiffs[1] < timeDiffs[2],
            $"Retry spacings should grow with the configured intervals: {string.Join(", ", timeDiffs)}");
    }

    [Fact]
    public async Task ExecuteTaskAsync_CronTickerOccurrence_UsesLastInterval_WhenRetriesExceedArrayLength()
    {
        // Use zero intervals for speed
        var (handler, context, _, attempts) = SetupRetryTestFixture([0, 0], retries: 4);

        await handler.ExecuteTaskAsync(context, isDue: true);

        // initial + 4 retries = 5 attempts
        Assert.Equal(5, attempts.Count);

        // Ensure we captured attempts and they happened in order. Timing is intentionally tiny.
        var timestamps = attempts.Select(a => a.Timestamp).ToList();
        for (int i = 1; i < timestamps.Count; i++)
            Assert.True(timestamps[i] >= timestamps[i - 1]);
    }

    [Fact]
    public async Task ExecuteTaskAsync_CronTickerOccurrence_StopsRetrying_WhenFunctionSucceeds()
    {
        // Arrange: succeed on RetryCount==2
        // Use zero intervals for speed; succeed at retry=2
        var (handler, context, _, attempts) = SetupRetryTestFixture([0, 0, 0, 0], retries: 4, succeedOnRetryCount: 2);

        await handler.ExecuteTaskAsync(context, isDue: true);

        // Should stop after success on attempt with RetryCount=2 => initial + retry1 + retry2 = 3 attempts
        Assert.Equal(3, attempts.Count);
        Assert.Equal(2, attempts.Last().RetryCount);
    }

    private record Attempt(DateTime Timestamp, int RetryCount);

    // Helpers
    private static (TickerExecutionTaskHandler handler, InternalFunctionContext context, IInternalTickerManager manager, List<Attempt> attempts) SetupRetryTestFixture(
        int[] retryIntervals,
        int retries,
        int? succeedOnRetryCount = null)
    {
        var services = new ServiceCollection();
        var clock = Substitute.For<ITickerClock>();
        var internalManager = Substitute.For<IInternalTickerManager>();
        var instrumentation = Substitute.For<ITickerNetInstrumentation>();

        clock.UtcNow.Returns(DateTime.UtcNow);

        services.AddSingleton(internalManager);
        services.AddSingleton(instrumentation);
        var serviceProvider = services.BuildServiceProvider();

        var handler = new TickerExecutionTaskHandler(serviceProvider, clock, instrumentation, internalManager);

        var attempts = new List<Attempt>();

        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "TestFunction",
            Type = TickerType.CronTickerOccurrence,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = retryIntervals,
            Retries = retries,
            RetryCount = 0,
            Status = TickerStatus.Idle,
            CachedDelegate = (ct, sp, tctx) =>
            {
                attempts.Add(new Attempt(DateTime.UtcNow, tctx.RetryCount));

                if (succeedOnRetryCount.HasValue && tctx.RetryCount >= succeedOnRetryCount.Value)
                    return Task.CompletedTask;

                throw new InvalidOperationException("Fail for retry test");
            }
        };

        return (handler, context, internalManager, attempts);
    }
}
