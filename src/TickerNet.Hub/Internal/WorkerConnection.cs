using System.Collections.Concurrent;
using Grpc.Core;
using TickerNet.Hub.Protocol.V1;

namespace TickerNet.Hub.Internal;

/// <summary>
/// One connected worker process. Serializes writes to the gRPC stream (server streams reject
/// concurrent writes) and matches results to outstanding runs by request id.
/// </summary>
/// <remarks>
/// Writes always use the connection's own token. A caller's token only limits the wait for the
/// write lock or for a reply: cancelling a write that is under way resets the whole stream, which
/// would fail every other run on this worker.
/// </remarks>
internal sealed class WorkerConnection
{
    private readonly IServerStreamWriter<HubCommand> _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, PendingRun> _pending = new();
    private readonly HashSet<string> _functions;
    private readonly CancellationTokenSource _closed = new();
    private long _lastSeenTicks;
    private volatile bool _draining;

    public WorkerConnection(
        string workerId,
        string nodeName,
        string workerVersion,
        int maxConcurrency,
        string peer,
        IReadOnlyList<FunctionDescriptor> functions,
        IServerStreamWriter<HubCommand> writer)
    {
        WorkerId = workerId;
        NodeName = nodeName;
        WorkerVersion = workerVersion;
        MaxConcurrency = maxConcurrency;
        Peer = peer;
        Functions = functions;
        _functions = functions.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        _writer = writer;
        ConnectedAt = DateTime.UtcNow;
        Touch();
    }

    public string WorkerId { get; }
    public string NodeName { get; }
    public string WorkerVersion { get; }
    public int MaxConcurrency { get; }
    public string Peer { get; }
    public IReadOnlyList<FunctionDescriptor> Functions { get; }
    public DateTime ConnectedAt { get; }
    public DateTime LastSeenAt => new(Interlocked.Read(ref _lastSeenTicks), DateTimeKind.Utc);

    /// <summary>Cancelled when the stream ends or the hub drops the worker.</summary>
    public CancellationToken Closed => _closed.Token;

    /// <summary>The worker said it is stopping; it gets no new runs.</summary>
    public bool IsDraining => _draining;

    /// <summary>Can take a new run of <paramref name="functionName"/> now.</summary>
    public bool CanRun(string functionName)
        => !_draining && !_closed.IsCancellationRequested && _functions.Contains(functionName);

    public int InFlight => _pending.Count;

    /// <summary>True while a run of <paramref name="tickerId"/> is in progress on this worker.</summary>
    public bool IsRunning(Guid tickerId) => _pending.Values.Any(p => p.TickerId == tickerId);

    public void Touch() => Interlocked.Exchange(ref _lastSeenTicks, DateTime.UtcNow.Ticks);

    public void MarkDraining() => _draining = true;

    public async Task WriteAsync(HubCommand command, CancellationToken waitToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(waitToken, _closed.Token);
        await _writeLock.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await _writer.WriteAsync(command, _closed.Token).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Sends <see cref="ExecuteFunction"/> and waits for the matching <see cref="ExecutionResult"/>.
    /// Throws <see cref="WorkerUnavailableException"/> if the run couldn't be sent (it never reached
    /// the worker), <see cref="WorkerDisconnectedException"/> if the worker went away after receiving
    /// it, <see cref="TimeoutException"/> after <paramref name="timeout"/>, and
    /// <see cref="OperationCanceledException"/> when <paramref name="cancellationToken"/> fires.
    /// </summary>
    public async Task<ExecutionResult> ExecuteAsync(Guid tickerId, ExecuteFunction request, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        request.RequestId = Guid.NewGuid().ToString("N");
        var pending = new PendingRun(tickerId);
        _pending[request.RequestId] = pending;
        try
        {
            try
            {
                if (_closed.IsCancellationRequested) throw new WorkerUnavailableException(NodeName, WorkerId);
                await WriteAsync(new HubCommand { ExecuteFunction = request }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not WorkerUnavailableException && !cancellationToken.IsCancellationRequested)
            {
                throw new WorkerUnavailableException(NodeName, WorkerId, ex);
            }

            try
            {
                var wait = pending.Completion.Task.WaitAsync(cancellationToken);
                return timeout is { } limit
                    ? await wait.WaitAsync(limit).ConfigureAwait(false)
                    : await wait.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_closed.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new WorkerDisconnectedException(NodeName, WorkerId);
            }
        }
        finally
        {
            _pending.TryRemove(request.RequestId, out _);
        }
    }

    /// <summary>Asks the worker to cancel a running job. Best effort.</summary>
    public async Task CancelAsync(Guid tickerId)
    {
        try
        {
            await WriteAsync(new HubCommand
            {
                CancelExecution = new CancelExecution
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    TickerId = tickerId.ToString()
                }
            }).ConfigureAwait(false);
        }
        catch
        {
            // The worker may already be gone; the run ends either way.
        }
    }

    public void CompleteExecution(ExecutionResult result)
    {
        if (string.IsNullOrEmpty(result.RequestId)) return;
        if (_pending.TryRemove(result.RequestId, out var pending))
            pending.Completion.TrySetResult(result);
    }

    /// <summary>Marks the connection closed and fails every run still waiting on it.</summary>
    public void Close()
    {
        if (_closed.IsCancellationRequested) return;
        try { _closed.Cancel(); } catch (ObjectDisposedException) { }
        foreach (var pending in _pending)
            pending.Value.Completion.TrySetException(new WorkerDisconnectedException(NodeName, WorkerId));
    }

    private sealed class PendingRun(Guid tickerId)
    {
        public Guid TickerId { get; } = tickerId;
        public TaskCompletionSource<ExecutionResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

/// <summary>The worker running a job disconnected before it reported a result.</summary>
internal sealed class WorkerDisconnectedException(string nodeName, string workerId)
    : Exception($"Worker '{workerId}' of node '{nodeName}' disconnected before the run finished.");

/// <summary>A run couldn't be sent to a worker; the worker never received it.</summary>
internal sealed class WorkerUnavailableException(string nodeName, string workerId, Exception? inner = null)
    : Exception($"Worker '{workerId}' of node '{nodeName}' couldn't take the run.", inner);
