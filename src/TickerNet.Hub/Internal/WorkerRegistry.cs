using System.Collections.Concurrent;

namespace TickerNet.Hub.Internal;

/// <summary>
/// Connected workers, keyed by worker id. Several workers (replicas) can share a node name; each
/// run goes to one that declared the function and isn't stopping, preferring the least busy and
/// rotating between equals.
/// </summary>
internal sealed class WorkerRegistry
{
    private readonly ConcurrentDictionary<string, WorkerConnection> _byWorkerId = new();
    private readonly ConcurrentDictionary<string, int> _rotation = new(StringComparer.OrdinalIgnoreCase);
    private TaskCompletionSource _changed = NewSignal();

    /// <summary>Raised after a worker connects, disconnects or starts draining.</summary>
    public event Action? Changed;

    public void Register(WorkerConnection connection)
    {
        _byWorkerId[connection.WorkerId] = connection;
        OnChanged();
    }

    public void Unregister(WorkerConnection connection)
    {
        if (_byWorkerId.TryRemove(new KeyValuePair<string, WorkerConnection>(connection.WorkerId, connection)))
            OnChanged();
    }

    public void NotifyDraining(WorkerConnection connection)
    {
        connection.MarkDraining();
        OnChanged();
    }

    public IReadOnlyCollection<WorkerConnection> All() => _byWorkerId.Values.ToArray();

    /// <summary>True when a worker of <paramref name="nodeName"/> that declared <paramref name="functionName"/> can take runs.</summary>
    public bool CanRun(string nodeName, string functionName)
        => _byWorkerId.Values.Any(c => SameNode(c, nodeName) && c.CanRun(functionName));

    public WorkerConnection? Pick(string nodeName, string functionName, IReadOnlySet<string>? exclude = null)
    {
        if (string.IsNullOrWhiteSpace(nodeName)) return null;

        var candidates = _byWorkerId.Values
            .Where(c => SameNode(c, nodeName) && c.CanRun(functionName) && exclude?.Contains(c.WorkerId) != true)
            .OrderBy(c => c.WorkerId, StringComparer.Ordinal)
            .ToArray();

        if (candidates.Length == 0) return null;
        if (candidates.Length == 1) return candidates[0];

        var leastBusy = candidates.Min(c => c.InFlight);
        var idle = candidates.Where(c => c.InFlight == leastBusy).ToArray();
        var turn = _rotation.AddOrUpdate(nodeName, 0, (_, previous) => unchecked(previous + 1));
        return idle[(int)((uint)turn % (uint)idle.Length)];
    }

    /// <summary>
    /// Returns a worker that can run <paramref name="functionName"/> on <paramref name="nodeName"/>,
    /// waiting up to <paramref name="timeout"/> for one to connect. Returns null if none does.
    /// </summary>
    public async Task<WorkerConnection?> WaitForWorkerAsync(
        string nodeName, string functionName, TimeSpan timeout, IReadOnlySet<string>? exclude, CancellationToken cancellationToken)
    {
        var connection = Pick(nodeName, functionName, exclude);
        if (connection != null || timeout <= TimeSpan.Zero) return connection;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        while (true)
        {
            var signal = Volatile.Read(ref _changed);
            connection = Pick(nodeName, functionName, exclude);
            if (connection != null) return connection;

            try
            {
                await signal.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Pick(nodeName, functionName, exclude);
            }
        }
    }

    private static bool SameNode(WorkerConnection connection, string nodeName)
        => string.Equals(connection.NodeName, nodeName, StringComparison.OrdinalIgnoreCase);

    private void OnChanged()
    {
        Interlocked.Exchange(ref _changed, NewSignal()).TrySetResult();
        Changed?.Invoke();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
