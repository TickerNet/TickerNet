using System.Collections.Concurrent;
using System.Reflection;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerNet.Exceptions;
using TickerNet.Hub.Protocol;
using TickerNet.Hub.Protocol.V1;
using TickerNet.Utilities;
using TickerNet.Utilities.Base;
using TickerNet.Utilities.Enums;
using TickerNet.Utilities.Interfaces;

namespace TickerNet.Worker.Internal;

/// <summary>
/// Keeps one stream open to each hub (each scheduler instance), reconnecting with backoff. Runs the
/// functions the hubs send, one attempt each, reporting every result on the connection the run came
/// from, and carries this worker's requests to add, change or remove jobs over any connected hub.
/// On shutdown it drains: it tells every hub it is stopping, rejects new runs, and finishes the runs
/// it has (up to <see cref="TickerNetWorkerOptions.ShutdownTimeout"/>) before disconnecting.
/// </summary>
internal sealed class WorkerClient : BackgroundService
{
    private static readonly string WorkerVersion = ReadWorkerVersion();

    private readonly TickerNetWorkerOptions _options;
    private readonly IServiceProvider _services;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<WorkerClient> _logger;
    private readonly HubLink[] _links;
    private readonly SemaphoreSlim? _workerGate;
    private readonly ConcurrentDictionary<Guid, RunningJob> _running = new();
    private readonly CancellationTokenSource _shutdown = new();
    private volatile bool _draining;
    private int _drainStarted;
    private int _nextLink;

    public WorkerClient(
        TickerNetWorkerOptions options,
        IServiceProvider services,
        IHostApplicationLifetime lifetime,
        ILogger<WorkerClient> logger)
    {
        _options = options;
        _services = services;
        _lifetime = lifetime;
        _logger = logger;
        _workerGate = options.MaxConcurrency > 0 ? new SemaphoreSlim(options.MaxConcurrency) : null;
        _links = options.EffectiveHubAddresses.Select(address => new HubLink(this, address)).ToArray();
    }

    /// <summary>Id the first hub gave this worker on its current connection, or null when disconnected.</summary>
    public string? WorkerId => _links[0].WorkerId;

    /// <summary>Ids this worker has on each hub it is connected to.</summary>
    public IReadOnlyList<string> WorkerIds => _links.Select(l => l.WorkerId).OfType<string>().ToArray();

    /// <summary>Completes when the worker is connected and registered with at least one hub.</summary>
    public Task Connected => Task.WhenAny(_links.Select(l => l.Connected));

    /// <summary>Completes when the worker is connected and registered with every hub.</summary>
    public Task AllConnected => Task.WhenAll(_links.Select(l => l.Connected));

    /// <summary>The stored request sent with a run that is in progress on this worker.</summary>
    public bool TryGetPayload(Guid tickerId, out byte[] payload)
    {
        if (_running.TryGetValue(tickerId, out var job) && job.Payload is { Length: > 0 } bytes)
        {
            payload = bytes;
            return true;
        }
        payload = [];
        return false;
    }

    public Task<OperationResult> SendOperationAsync(string requestId, WorkerEvent message, CancellationToken cancellationToken)
        => WithinOperationTimeoutAsync(async token =>
        {
            var link = await PickConnectedLinkAsync(token).ConfigureAwait(false);
            return await link.SendOperationAsync(requestId, message, token).ConfigureAwait(false);
        }, cancellationToken);

    public Task<BytesResult> SendBytesRequestAsync(string requestId, WorkerEvent message, CancellationToken cancellationToken)
        => WithinOperationTimeoutAsync(async token =>
        {
            var link = await PickConnectedLinkAsync(token).ConfigureAwait(false);
            return await link.SendBytesRequestAsync(requestId, message, token).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>Runs <paramref name="operation"/>, including any wait for a connection, within OperationTimeout.</summary>
    private async Task<T> WithinOperationTimeoutAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.OperationTimeout);
        try
        {
            return await operation(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"No TickerNet hub answered within {_options.OperationTimeout.TotalSeconds:0.#} seconds. Check that this worker is connected.");
        }
    }

    /// <summary>A connected hub, rotating between them; waits for one to connect if none is.</summary>
    private async Task<HubLink> PickConnectedLinkAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var start = Interlocked.Increment(ref _nextLink);
            for (var i = 0; i < _links.Length; i++)
            {
                var link = _links[(int)((uint)(start + i) % (uint)_links.Length)];
                if (link.IsConnected) return link;
            }

            await Task.WhenAny(_links.Select(l => l.Connected)).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Functions are registered while the host starts, so wait until it has.
        try
        {
            await Task.Delay(Timeout.Infinite, _lifetime.ApplicationStarted).WaitAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.ApplicationStarted.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            // Started.
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var stopping = stoppingToken.Register(() => _ = DrainAsync());

        await Task.WhenAll(_links.Select(link => link.RunAsync(_shutdown.Token))).ConfigureAwait(false);

        // Stopping: wait for the drain to finish before the host disposes services.
        try { await Task.Delay(Timeout.Infinite, _shutdown.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    /// <summary>Stops taking runs, finishes the ones in progress, then closes the connections.</summary>
    private async Task DrainAsync()
    {
        if (Interlocked.Exchange(ref _drainStarted, 1) == 1) return;
        _draining = true;

        await Task.WhenAll(_links.Select(link => link.SayDrainingAsync())).ConfigureAwait(false);

        var deadline = DateTime.UtcNow + _options.ShutdownTimeout;
        while (!_running.IsEmpty && DateTime.UtcNow < deadline)
            await Task.Delay(50).ConfigureAwait(false);

        if (!_running.IsEmpty)
            _logger.LogWarning("Stopping with {Count} runs still in progress after {Seconds} seconds; they are cancelled",
                _running.Count, _options.ShutdownTimeout.TotalSeconds);
        else
            await Task.WhenAll(_links.Select(link => link.CloseStreamAsync(TimeSpan.FromSeconds(2)))).ConfigureAwait(false);

        try { _shutdown.Cancel(); } catch (ObjectDisposedException) { }
    }

    private GrpcChannelOptions BuildChannelOptions()
    {
        var channelOptions = new GrpcChannelOptions
        {
            MaxReceiveMessageSize = _options.MaxMessageSize,
            MaxSendMessageSize = _options.MaxMessageSize,
            HttpHandler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
                KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan
            }
        };
        _options.ConfigureChannel?.Invoke(channelOptions);
        return channelOptions;
    }

    private Hello BuildHello()
    {
        var hello = new Hello
        {
            NodeName = _options.NodeName,
            WorkerVersion = WorkerVersion,
            MaxConcurrency = _options.MaxConcurrency,
            Nonce = Guid.NewGuid().ToString("N"),
            UnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ProtocolVersion = HelloSignature.ProtocolVersion
        };
        hello.Functions.AddRange(FunctionManifest.Build(_options.FunctionFilter));

        if (hello.Functions.Count == 0)
            _logger.LogWarning(
                "This TickerNet worker has no [TickerFunction] functions to offer. Check that the functions are in this app and that UseTickerNet() is called.");

        hello.Signature = HelloSignature.ForHello(_options.SharedSecret, hello);
        return hello;
    }

    /// <summary>
    /// Registers the run before returning, so a cancel that arrives right after it is never lost,
    /// then runs it in the background. The result goes back on <paramref name="link"/>.
    /// </summary>
    private void Accept(ExecuteFunction request, HubLink link, CancellationToken connectionToken)
    {
        string? rejection = null;
        if (_draining)
            rejection = "The worker is stopping.";
        else if (!Guid.TryParse(request.TickerId, out _))
            rejection = "ticker_id must be a GUID.";
        else if (!FunctionManifest.Offers(request.FunctionName, _options.FunctionFilter))
            rejection = $"Function '{request.FunctionName}' isn't registered on this worker of node '{_options.NodeName}'.";

        if (rejection != null)
        {
            _ = link.SendQuietlyAsync(new WorkerEvent
            {
                ExecutionResult = new ExecutionResult { RequestId = request.RequestId, Rejected = true, Error = rejection }
            }, connectionToken);
            return;
        }

        var tickerId = Guid.Parse(request.TickerId);
        Guid? parentId = Guid.TryParse(request.ParentId, out var parsedParent) ? parsedParent : null;
        var job = new RunningJob(link, parentId, request.RequestPayload.ToByteArray(), connectionToken);
        _running[tickerId] = job;
        _ = Task.Run(() => RunAsync(request, tickerId, job, connectionToken), CancellationToken.None);
    }

    private void Cancel(Guid tickerId)
    {
        if (_running.TryGetValue(tickerId, out var job)) job.Cancel();
    }

    /// <summary>The connection to <paramref name="link"/> closed: its results can't be delivered, so its runs stop.</summary>
    private void CancelRunsFrom(HubLink link)
    {
        foreach (var job in _running.Values)
        {
            if (ReferenceEquals(job.Link, link)) job.Cancel();
        }
    }

    private async Task RunAsync(ExecuteFunction request, Guid tickerId, RunningJob job, CancellationToken connectionToken)
    {
        var result = new ExecutionResult { RequestId = request.RequestId };
        var function = TickerFunctionProvider.TickerFunctions[request.FunctionName];
        var functionGate = _services.GetService<ITickerFunctionConcurrencyGate>()
            ?.GetSemaphoreOrNull(request.FunctionName, function.MaxConcurrency);
        var holdsWorkerGate = false;
        var holdsFunctionGate = false;

        try
        {
            if (_workerGate != null)
            {
                await _workerGate.WaitAsync(job.Token).ConfigureAwait(false);
                holdsWorkerGate = true;
            }
            if (functionGate != null)
            {
                await functionGate.WaitAsync(job.Token).ConfigureAwait(false);
                holdsFunctionGate = true;
            }

            await using var scope = _services.CreateAsyncScope();
            var context = new TickerFunctionContext
            {
                Id = tickerId,
                ParentId = job.ParentId,
                Type = (TickerType)request.Type,
                RetryCount = request.RetryCount,
                IsDue = request.IsDue,
                ScheduledFor = request.ScheduledFor?.ToDateTime() ?? DateTime.UtcNow,
                FunctionName = request.FunctionName,
                RequestCancelOperationAction = job.Cancel,
                CronOccurrenceOperations = new CronOccurrenceOperations
                {
                    SkipIfAlreadyRunningAction = () =>
                    {
                        if (job.ParentId is { } parent && _running.Any(r => r.Key != tickerId && r.Value.ParentId == parent))
                            throw new TerminateExecutionException("Another CronOccurrence is already running!");
                    }
                }
            };
            context.SetServiceScope(scope);

            await function.Delegate(job.Token, scope.ServiceProvider, context).ConfigureAwait(false);
            result.Success = true;
        }
        catch (TerminateExecutionException ex)
        {
            result.Terminated = true;
            result.TerminatedStatus = (int)ex.Status;
            result.Error = ex.InnerException?.Message ?? ex.Message;
        }
        catch (TaskCanceledException ex)
        {
            result.Cancelled = true;
            result.Error = ex.Message;
        }
        catch (OperationCanceledException) when (job.Token.IsCancellationRequested)
        {
            result.Cancelled = true;
            result.Error = "The run was cancelled.";
        }
        catch (Exception ex)
        {
            var root = ex;
            while (root.InnerException != null) root = root.InnerException;
            result.Error = ex.Message;
            result.ErrorType = ex.GetType().FullName ?? ex.GetType().Name;
            result.ErrorStackTrace = root.StackTrace ?? string.Empty;
        }
        finally
        {
            if (holdsFunctionGate) functionGate!.Release();
            if (holdsWorkerGate) _workerGate!.Release();
        }

        try
        {
            await job.Link.SendQuietlyAsync(new WorkerEvent { ExecutionResult = result }, connectionToken).ConfigureAwait(false);
        }
        finally
        {
            // Removed only after the result is sent, so a draining worker waits for it.
            _running.TryRemove(new KeyValuePair<Guid, RunningJob>(tickerId, job));
            job.Dispose();
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string ReadWorkerVersion()
    {
        var version = typeof(WorkerClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? typeof(WorkerClient).Assembly.GetName().Version?.ToString()
                      ?? "unknown";
        // The hub accepts up to 100 characters of plain text.
        return version.Length > 100 ? version[..100] : version;
    }

    public override void Dispose()
    {
        _shutdown.Dispose();
        base.Dispose();
    }

    /// <summary>The connection to one hub: its stream, its reconnect loop and its outstanding requests.</summary>
    private sealed class HubLink(WorkerClient client, Uri address)
    {
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<OperationResult>> _pendingOperations = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<BytesResult>> _pendingBytes = new();
        private TaskCompletionSource _connected = NewSignal();
        private IClientStreamWriter<WorkerEvent>? _writer;

        private ILogger Logger => client._logger;
        private TickerNetWorkerOptions Options => client._options;

        public Uri Address { get; } = address;
        public string? WorkerId { get; private set; }
        public bool IsConnected => WorkerId != null && _writer != null;
        public Task Connected => Volatile.Read(ref _connected).Task;

        public async Task RunAsync(CancellationToken shutdownToken)
        {
            var delay = TimeSpan.FromSeconds(1);
            while (!shutdownToken.IsCancellationRequested && !client._draining)
            {
                var registered = false;
                try
                {
                    await RunConnectionAsync(() => registered = true, shutdownToken).ConfigureAwait(false);
                    if (!client._draining)
                        Logger.LogInformation("The TickerNet hub at {Hub} closed the worker stream; reconnecting", Address);
                }
                catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
                {
                    return;
                }
                catch (RpcException ex) when (ex.StatusCode is StatusCode.Unauthenticated or StatusCode.InvalidArgument or StatusCode.FailedPrecondition)
                {
                    Logger.LogError("The TickerNet hub at {Hub} rejected this worker: {Detail}", Address, ex.Status.Detail);
                    delay = Options.MaxReconnectDelay;
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Unimplemented)
                {
                    Logger.LogError("{Hub} has no TickerNet worker endpoint. Call app.MapTickerNetHub() on the scheduler.", Address);
                    delay = Options.MaxReconnectDelay;
                }
                catch (HubNotTrustedException ex)
                {
                    Logger.LogError("{Message}", ex.Message);
                    delay = Options.MaxReconnectDelay;
                }
                catch (Exception ex) when (!client._draining)
                {
                    Logger.LogWarning(ex, "Lost the connection to the TickerNet hub at {Hub}; reconnecting in {Delay}", Address, delay);
                }
                catch (Exception)
                {
                    // Draining: the connection ended while stopping.
                }

                if (client._draining) break;
                if (registered) delay = TimeSpan.FromSeconds(1);

                try
                {
                    var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
                    await Task.Delay(delay + jitter, shutdownToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (!registered)
                    delay = TimeSpan.FromTicks(Math.Min(Options.MaxReconnectDelay.Ticks, delay.Ticks * 2));
            }
        }

        private async Task RunConnectionAsync(Action onRegistered, CancellationToken shutdownToken)
        {
            using var channel = GrpcChannel.ForAddress(Address, client.BuildChannelOptions());
            var hub = new WorkerHub.WorkerHubClient(channel);

            var hello = client.BuildHello();
            var headers = new Metadata
            {
                { HelloSignature.NodeHeader, hello.NodeName },
                { HelloSignature.NonceHeader, hello.Nonce },
                { HelloSignature.TimestampHeader, hello.UnixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { HelloSignature.SignatureHeader, HelloSignature.ForCall(Options.SharedSecret, hello.NodeName, hello.Nonce, hello.UnixSeconds) }
            };

            using var connection = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
            using var call = hub.OpenWorkerStream(headers, cancellationToken: connection.Token);
            _writer = call.RequestStream;

            try
            {
                await call.RequestStream.WriteAsync(new WorkerEvent { Hello = hello }).ConfigureAwait(false);

                if (!await call.ResponseStream.MoveNext(connection.Token).ConfigureAwait(false))
                    throw new InvalidOperationException("The hub closed the stream before accepting this worker.");
                var first = call.ResponseStream.Current;
                if (first.PayloadCase != HubCommand.PayloadOneofCase.HelloAck)
                    throw new InvalidOperationException($"Expected HelloAck from the hub, got {first.PayloadCase}.");
                if (!HelloSignature.Matches(HelloSignature.ForAck(Options.SharedSecret, hello.Nonce, first.HelloAck.WorkerId), first.HelloAck.Signature))
                    throw new HubNotTrustedException(Address);

                WorkerId = first.HelloAck.WorkerId;
                onRegistered();
                Volatile.Read(ref _connected).TrySetResult();
                Logger.LogInformation("Connected to the TickerNet hub at {Hub} as worker {WorkerId} of node {Node}",
                    Address, WorkerId, Options.NodeName);

                var heartbeat = SendHeartbeatsAsync(connection.Token);
                try
                {
                    while (await call.ResponseStream.MoveNext(connection.Token).ConfigureAwait(false))
                        Handle(call.ResponseStream.Current, connection.Token);
                }
                finally
                {
                    connection.Cancel();
                    try { await heartbeat.ConfigureAwait(false); } catch { /* ended with the stream */ }
                }
            }
            finally
            {
                _writer = null;
                WorkerId = null;
                // Callers waiting to send keep waiting for the next connection; only a completed
                // signal is replaced.
                if (Volatile.Read(ref _connected).Task.IsCompleted)
                    Interlocked.Exchange(ref _connected, NewSignal());

                var closed = new InvalidOperationException("The connection to the TickerNet hub closed before it answered.");
                foreach (var pending in _pendingOperations) pending.Value.TrySetException(closed);
                foreach (var pending in _pendingBytes) pending.Value.TrySetException(closed);

                // This hub can't receive these results any more and will retry the runs if they
                // have retries left, so stop them here rather than run them twice.
                client.CancelRunsFrom(this);
            }
        }

        private async Task SendHeartbeatsAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(Options.HeartbeatInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await SendAsync(new WorkerEvent
                {
                    Heartbeat = new Heartbeat { UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
                }, cancellationToken).ConfigureAwait(false);
            }
        }

        private void Handle(HubCommand command, CancellationToken connectionToken)
        {
            switch (command.PayloadCase)
            {
                case HubCommand.PayloadOneofCase.ExecuteFunction:
                    client.Accept(command.ExecuteFunction, this, connectionToken);
                    return;

                case HubCommand.PayloadOneofCase.CancelExecution:
                    if (Guid.TryParse(command.CancelExecution.TickerId, out var tickerId))
                        client.Cancel(tickerId);
                    _ = SendQuietlyAsync(new WorkerEvent
                    {
                        Ack = new CommandAck { RequestId = command.CancelExecution.RequestId, Success = true }
                    }, connectionToken);
                    return;

                case HubCommand.PayloadOneofCase.OperationResult:
                    if (_pendingOperations.TryRemove(command.OperationResult.RequestId, out var operation))
                        operation.TrySetResult(command.OperationResult);
                    return;

                case HubCommand.PayloadOneofCase.BytesResult:
                    if (_pendingBytes.TryRemove(command.BytesResult.RequestId, out var bytes))
                        bytes.TrySetResult(command.BytesResult);
                    return;

                case HubCommand.PayloadOneofCase.Disconnect:
                    Logger.LogInformation("The TickerNet hub at {Hub} is closing this worker's connection: {Reason}", Address, command.Disconnect.Reason);
                    return;
            }
        }

        public async Task<OperationResult> SendOperationAsync(string requestId, WorkerEvent message, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingOperations[requestId] = completion;
            try
            {
                await SendAsync(message, cancellationToken).ConfigureAwait(false);
                return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _pendingOperations.TryRemove(requestId, out _);
            }
        }

        public async Task<BytesResult> SendBytesRequestAsync(string requestId, WorkerEvent message, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<BytesResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingBytes[requestId] = completion;
            try
            {
                await SendAsync(message, cancellationToken).ConfigureAwait(false);
                return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _pendingBytes.TryRemove(requestId, out _);
            }
        }

        public async Task SayDrainingAsync()
        {
            if (!IsConnected) return;
            using var notice = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await SendQuietlyAsync(new WorkerEvent { Draining = new Draining { Reason = "The worker is stopping." } }, notice.Token).ConfigureAwait(false);
        }

        /// <summary>Ends this worker's side of the stream, so the hub sees an orderly close rather than a reset.</summary>
        public async Task CloseStreamAsync(TimeSpan timeout)
        {
            using var limit = new CancellationTokenSource(timeout);
            try
            {
                await _writeLock.WaitAsync(limit.Token).ConfigureAwait(false);
                try
                {
                    if (_writer is { } writer) await writer.CompleteAsync().WaitAsync(limit.Token).ConfigureAwait(false);
                }
                finally
                {
                    _writeLock.Release();
                }

                // The hub ends the call once it has read the end of the stream.
                while (WorkerId != null && !limit.IsCancellationRequested)
                    await Task.Delay(20, limit.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or RpcException)
            {
                // Closed anyway when the connection is cancelled.
            }
        }

        public async Task SendQuietlyAsync(WorkerEvent message, CancellationToken cancellationToken)
        {
            try
            {
                await SendAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Couldn't send {Message} to the TickerNet hub at {Hub}", message.PayloadCase, Address);
            }
        }

        private async Task SendAsync(WorkerEvent message, CancellationToken waitToken)
        {
            await Volatile.Read(ref _connected).Task.WaitAsync(waitToken).ConfigureAwait(false);
            await _writeLock.WaitAsync(waitToken).ConfigureAwait(false);
            try
            {
                var writer = _writer ?? throw new InvalidOperationException($"This worker isn't connected to the TickerNet hub at {Address}.");
                // No token on the write itself: cancelling a write under way resets the whole stream.
                // The call is already bound to the connection's lifetime.
                await writer.WriteAsync(message).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }

    private sealed class RunningJob(HubLink link, Guid? parentId, byte[] payload, CancellationToken connectionToken) : IDisposable
    {
        private readonly CancellationTokenSource _cancel = CancellationTokenSource.CreateLinkedTokenSource(connectionToken);

        public HubLink Link { get; } = link;
        public Guid? ParentId { get; } = parentId;
        public byte[] Payload { get; } = payload;
        public CancellationToken Token => _cancel.Token;

        public void Cancel()
        {
            try { _cancel.Cancel(); } catch (ObjectDisposedException) { }
        }

        public void Dispose() => _cancel.Dispose();
    }

    private sealed class HubNotTrustedException(Uri hub)
        : Exception($"The server at {hub} didn't prove it knows the shared secret, so this worker won't take runs from it.");
}
