using System.Collections.Frozen;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using TickerNet.Utilities;

namespace TickerNet.Benchmarks.Comparisons;

/// <summary>
/// Compares function dispatch mechanisms:
/// - TickerNet: pre-compiled delegate via FrozenDictionary (source-generated at build time)
/// - Reflection: MethodInfo.Invoke (traditional approach used by older schedulers)
/// - Compiled delegate from MethodInfo (middle ground)
///
/// This isolates the per-invocation cost of finding and calling a job method.
///
/// Results Overview (Apple M4 Pro, .NET 10.0):
/// ┌──────────────────────────────────┬───────────┬───────┬──────────────┐
/// │ Method                           │ Time      │ Alloc │ vs TickerNet   │
/// ├──────────────────────────────────┼───────────┼───────┼──────────────┤
/// │ TickerNet: Lookup + invoke         │ 1.38 ns   │ 0 B   │ 1x (baseline)│
/// │ TickerNet: Invoke cached delegate  │ ~0 ns     │ 0 B   │ -            │
/// │ Compiled: CreateDelegate+invoke  │ ~0 ns     │ 0 B   │ -            │
/// │ Reflection: MethodInfo.Invoke    │ 14.6 ns   │ 64 B  │ 10.6x slower │
/// └──────────────────────────────────┴───────────┴───────┴──────────────┘
/// Winner: TickerNet — 10.6x faster than reflection, zero allocations.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.HostProcess)]
public class DelegateInvocationComparison
{
    private TickerFunctionDelegate _tickernetDelegate = null!;
    private FrozenDictionary<string, TickerFunctionDelegate> _tickernetRegistry = null!;
    private MethodInfo _reflectionMethod = null!;
    private object _reflectionTarget = null!;
    private Func<string, int, Task> _compiledDelegate = null!;

    private const string FunctionKey = "MyApp.Jobs.ProcessOrder";

    [GlobalSetup]
    public void Setup()
    {
        // TickerNet: source-generated delegate in FrozenDictionary
        _tickernetDelegate = (_, _, _) => Task.CompletedTask;
        var dict = new Dictionary<string, TickerFunctionDelegate>
        {
            [FunctionKey] = _tickernetDelegate,
            ["MyApp.Jobs.SendEmail"] = (_, _, _) => Task.CompletedTask,
            ["MyApp.Jobs.GenerateReport"] = (_, _, _) => Task.CompletedTask,
        };
        _tickernetRegistry = dict.ToFrozenDictionary();

        // Reflection: traditional approach
        _reflectionTarget = new SampleJobClass();
        _reflectionMethod = typeof(SampleJobClass).GetMethod(nameof(SampleJobClass.ProcessOrder))!;

        // Compiled delegate: middle ground
        _compiledDelegate = _reflectionMethod.CreateDelegate<Func<string, int, Task>>(_reflectionTarget);
    }

    // ── TickerNet: lookup + invoke pre-compiled delegate ──

    [Benchmark(Baseline = true, Description = "TickerNet: Lookup + invoke delegate")]
    public Task TickerNet_LookupAndInvoke()
    {
        _tickernetRegistry.TryGetValue(FunctionKey, out var del);
        return del!(CancellationToken.None, null!, null!);
    }

    // ── TickerNet: invoke cached delegate (no lookup) ──

    [Benchmark(Description = "TickerNet: Invoke cached delegate")]
    public Task TickerNet_InvokeCached() =>
        _tickernetDelegate(CancellationToken.None, null!, null!);

    // ── Reflection: MethodInfo.Invoke ──

    [Benchmark(Description = "Reflection: MethodInfo.Invoke")]
    public object? Reflection_Invoke() =>
        _reflectionMethod.Invoke(_reflectionTarget, ["order-123", 1]);

    // ── Compiled delegate from reflection ──

    [Benchmark(Description = "Compiled: CreateDelegate + invoke")]
    public Task Compiled_Invoke() =>
        _compiledDelegate("order-123", 1);

    public class SampleJobClass
    {
        public Task ProcessOrder(string orderId, int priority) => Task.CompletedTask;
    }
}
