using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerNet.Hub.Protocol;
using TickerNet.Hub.Protocol.V1;
using TickerNet.Utilities;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Enums;
using TickerNet.Utilities.Interfaces;
using TickerNet.Utilities.Interfaces.Managers;
using TickerNet.Utilities.Models;

namespace TickerNet.Hub.Internal;

/// <summary>
/// Server side of <c>WorkerHub.OpenWorkerStream</c>. Authenticates the call from its headers before
/// reading the stream, checks the worker's Hello, registers the worker and its functions, then
/// handles the worker's messages until the stream ends.
/// </summary>
/// <remarks>
/// A worker may only touch jobs for its own node: it can add, change and remove
/// <c>function@itsnode</c> jobs, change only their schedule fields, and read the stored request only
/// of a run it is executing. System fields (status, locks, history) are always the scheduler's.
/// </remarks>
internal sealed class WorkerStreamService<TTimeTicker, TCronTicker>(
    TickerNetHubOptions options,
    WorkerRegistry registry,
    RemoteFunctionCatalog catalog,
    HelloReplayGuard replayGuard,
    RemoteCronSeeder cronSeeder,
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<WorkerStreamService<TTimeTicker, TCronTicker>> logger)
    : WorkerHub.WorkerHubBase
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private const int MaxFunctionsPerWorker = 10_000;
    private const int MaxFunctionNameLength = 200;
    private const int MaxRequestsInFlightPerWorker = 8;
    private static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(10);

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public override async Task OpenWorkerStream(
        IAsyncStreamReader<WorkerEvent> requestStream,
        IServerStreamWriter<HubCommand> responseStream,
        ServerCallContext context)
    {
        var callCancelled = context.CancellationToken;

        // 1. Authenticate from the headers, before reading anything from the stream.
        var call = AuthenticateCall(context);

        // 2. The Hello must arrive promptly and match the headers.
        var hello = await ReadHelloAsync(requestStream, callCancelled).ConfigureAwait(false);
        CheckHello(hello, call, context.Peer);

        var connection = new WorkerConnection(
            Guid.NewGuid().ToString("N"),
            hello.NodeName,
            hello.WorkerVersion,
            hello.MaxConcurrency,
            context.Peer,
            hello.Functions.ToArray(),
            responseStream);

        // 3. Acknowledge, signed, before any run can be sent to this worker.
        await connection.WriteAsync(new HubCommand
        {
            HelloAck = new HelloAck
            {
                WorkerId = connection.WorkerId,
                Signature = HelloSignature.ForAck(options.SharedSecret, hello.Nonce, connection.WorkerId)
            }
        }, callCancelled).ConfigureAwait(false);

        catalog.Add(connection.NodeName, connection.Functions);
        registry.Register(connection);

        logger.LogInformation(
            "Worker {WorkerId} of node {Node} connected from {Peer} with {Count} functions (worker version {Version})",
            connection.WorkerId, connection.NodeName, connection.Peer, connection.Functions.Count, connection.WorkerVersion);

        if (options.SeedCronTickers)
            _ = SeedCronTickersAsync(connection);

        using var requestGate = new SemaphoreSlim(MaxRequestsInFlightPerWorker);
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(callCancelled, connection.Closed, lifetime.ApplicationStopping);
        try
        {
            while (await requestStream.MoveNext(reading.Token).ConfigureAwait(false))
            {
                connection.Touch();
                var message = requestStream.Current;
                switch (message.PayloadCase)
                {
                    case WorkerEvent.PayloadOneofCase.Heartbeat:
                    case WorkerEvent.PayloadOneofCase.Ack:
                        break;
                    case WorkerEvent.PayloadOneofCase.ExecutionResult:
                        connection.CompleteExecution(message.ExecutionResult);
                        break;
                    case WorkerEvent.PayloadOneofCase.Draining:
                        logger.LogInformation("Worker {WorkerId} of node {Node} is stopping: {Reason}",
                            connection.WorkerId, connection.NodeName, message.Draining.Reason);
                        registry.NotifyDraining(connection);
                        break;
                    case WorkerEvent.PayloadOneofCase.Hello:
                        logger.LogDebug("Ignored a second Hello from worker {WorkerId}", connection.WorkerId);
                        break;
                    default:
                        // Requests touch the store; limit how many one worker can have at once.
                        await requestGate.WaitAsync(reading.Token).ConfigureAwait(false);
                        _ = HandleRequestAsync(connection, message, requestGate, reading.Token);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The worker disconnected, the hub dropped it, or the host is stopping.
        }
        catch (IOException)
        {
            // The connection broke.
        }
        finally
        {
            if (lifetime.ApplicationStopping.IsCancellationRequested && !connection.Closed.IsCancellationRequested)
            {
                var goodbye = connection.WriteAsync(new HubCommand { Disconnect = new Disconnect { Reason = "The scheduler is stopping." } });
                await Task.WhenAny(goodbye, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
            }

            registry.Unregister(connection);
            connection.Close();
            logger.LogInformation("Worker {WorkerId} of node {Node} disconnected", connection.WorkerId, connection.NodeName);
        }
    }

    private readonly record struct CallIdentity(string NodeName, string Nonce, long UnixSeconds);

    private CallIdentity AuthenticateCall(ServerCallContext context)
    {
        var headers = context.RequestHeaders;
        var node = headers.GetValue(HelloSignature.NodeHeader) ?? string.Empty;
        var nonce = headers.GetValue(HelloSignature.NonceHeader) ?? string.Empty;
        var timestamp = headers.GetValue(HelloSignature.TimestampHeader);
        var signature = headers.GetValue(HelloSignature.SignatureHeader);
        var now = DateTimeOffset.UtcNow;

        if (!RemoteFunctionName.IsValidNodeName(node)
            || !HelloReplayGuard.IsWellFormedNonce(nonce)
            || !long.TryParse(timestamp, out var unixSeconds))
        {
            logger.LogWarning("Rejected a worker call from {Peer}: missing or malformed authentication headers", context.Peer);
            throw Reject(StatusCode.Unauthenticated, "The call has no valid TickerNet worker authentication headers.");
        }

        if (!replayGuard.IsFresh(unixSeconds, now))
        {
            logger.LogWarning("Rejected worker {Node} from {Peer}: its clock is more than 5 minutes off", node, context.Peer);
            throw Reject(StatusCode.Unauthenticated, "The call's timestamp is more than 5 minutes off. Check the worker's clock.");
        }

        if (!HelloSignature.Matches(HelloSignature.ForCall(options.SharedSecret, node, nonce, unixSeconds), signature))
        {
            logger.LogWarning("Rejected worker {Node} from {Peer}: the signature doesn't match the shared secret", node, context.Peer);
            throw Reject(StatusCode.Unauthenticated, "The call's signature is not valid. Check that the worker and hub use the same shared secret.");
        }

        if (!replayGuard.TryUse(nonce, now))
        {
            logger.LogWarning("Rejected worker {Node} from {Peer}: the nonce was already used", node, context.Peer);
            throw Reject(StatusCode.Unauthenticated, "The call's nonce was already used.");
        }

        return new CallIdentity(node, nonce, unixSeconds);
    }

    private static async Task<Hello> ReadHelloAsync(IAsyncStreamReader<WorkerEvent> requestStream, CancellationToken callCancelled)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(callCancelled);
        deadline.CancelAfter(HelloTimeout);
        try
        {
            if (await requestStream.MoveNext(deadline.Token).ConfigureAwait(false)
                && requestStream.Current.PayloadCase == WorkerEvent.PayloadOneofCase.Hello)
                return requestStream.Current.Hello;
        }
        catch (OperationCanceledException) when (!callCancelled.IsCancellationRequested)
        {
            throw Reject(StatusCode.DeadlineExceeded, $"No Hello within {HelloTimeout.TotalSeconds:0} seconds.");
        }

        throw Reject(StatusCode.InvalidArgument, "The first message must be Hello.");
    }

    private void CheckHello(Hello hello, CallIdentity call, string peer)
    {
        if (hello.ProtocolVersion != HelloSignature.ProtocolVersion)
            throw Reject(StatusCode.FailedPrecondition,
                $"This hub speaks worker protocol version {HelloSignature.ProtocolVersion}; the worker sent {hello.ProtocolVersion}. Use matching TickerNet versions.");

        if (!string.Equals(hello.NodeName, call.NodeName, StringComparison.Ordinal)
            || !string.Equals(hello.Nonce, call.Nonce, StringComparison.Ordinal)
            || hello.UnixSeconds != call.UnixSeconds)
            throw Reject(StatusCode.InvalidArgument, "Hello doesn't match the call's authentication headers.");

        if (!HelloSignature.IsPlainText(hello.WorkerVersion, 100) || hello.MaxConcurrency < 0)
            throw Reject(StatusCode.InvalidArgument, "Hello.worker_version or max_concurrency is not valid.");

        if (hello.Functions.Count > MaxFunctionsPerWorker)
            throw Reject(StatusCode.InvalidArgument, $"A worker can declare at most {MaxFunctionsPerWorker} functions.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in hello.Functions)
        {
            if (string.IsNullOrWhiteSpace(function.Name)
                || !HelloSignature.IsPlainText(function.Name, MaxFunctionNameLength)
                || function.Name.Contains(RemoteFunctionName.Separator))
                throw Reject(StatusCode.InvalidArgument,
                    $"Function name '{function.Name}' is not valid: it must be 1-{MaxFunctionNameLength} characters without '@'.");
            if (!names.Add(function.Name))
                throw Reject(StatusCode.InvalidArgument, $"Function '{function.Name}' is declared twice.");
            if (!HelloSignature.IsPlainText(function.CronExpression, 200)
                || !HelloSignature.IsPlainText(function.RequestType, 1000)
                || (function.RequestExampleJson?.Length ?? 0) > 64 * 1024
                || function.MaxConcurrency < 0)
                throw Reject(StatusCode.InvalidArgument, $"Function '{function.Name}' has a field that is too long or not valid.");
        }

        if (!HelloSignature.Matches(HelloSignature.ForHello(options.SharedSecret, hello), hello.Signature))
        {
            logger.LogWarning("Rejected worker {Node} from {Peer}: the Hello signature doesn't match the shared secret", hello.NodeName, peer);
            throw Reject(StatusCode.Unauthenticated, "Hello signature is not valid. Check that the worker and hub use the same shared secret.");
        }
    }

    private static RpcException Reject(StatusCode code, string detail) => new(new Status(code, detail));

    private async Task SeedCronTickersAsync(WorkerConnection connection)
    {
        try
        {
            await cronSeeder.SeedAsync(connection.NodeName, connection.Functions, lifetime.ApplicationStopping).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
        {
            // Host is stopping.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Couldn't create cron jobs for worker node {Node}", connection.NodeName);
        }
    }

    // ---- Requests from the worker ----

    private async Task HandleRequestAsync(WorkerConnection connection, WorkerEvent message, SemaphoreSlim requestGate, CancellationToken streamToken)
    {
        try
        {
            // Store work runs with the host's lifetime, not the stream's: a job a worker adds must
            // not be cancelled because that worker later disconnects.
            var hostToken = lifetime.ApplicationStopping;
            switch (message.PayloadCase)
            {
                case WorkerEvent.PayloadOneofCase.AddTimeTickers:
                    await ReplyAsync(connection, message.AddTimeTickers.RequestId,
                        services => AddTimeTickersAsync(connection, message.AddTimeTickers.EntitiesJson, services, hostToken)).ConfigureAwait(false);
                    break;
                case WorkerEvent.PayloadOneofCase.UpdateTimeTickers:
                    await ReplyAsync(connection, message.UpdateTimeTickers.RequestId,
                        services => UpdateTimeTickersAsync(connection, message.UpdateTimeTickers.EntitiesJson, services, hostToken)).ConfigureAwait(false);
                    break;
                case WorkerEvent.PayloadOneofCase.RemoveTimeTickers:
                    await ReplyAsync(connection, message.RemoveTimeTickers.RequestId,
                        services => RemoveTimeTickersAsync(connection, message.RemoveTimeTickers.Ids, services, hostToken)).ConfigureAwait(false);
                    break;
                case WorkerEvent.PayloadOneofCase.InsertCronTickers:
                    await ReplyAsync(connection, message.InsertCronTickers.RequestId,
                        services => AddCronTickersAsync(connection, message.InsertCronTickers.EntitiesJson, services, hostToken)).ConfigureAwait(false);
                    break;
                case WorkerEvent.PayloadOneofCase.UpdateCronTickers:
                    await ReplyAsync(connection, message.UpdateCronTickers.RequestId,
                        services => UpdateCronTickersAsync(connection, message.UpdateCronTickers.EntitiesJson, services, hostToken)).ConfigureAwait(false);
                    break;
                case WorkerEvent.PayloadOneofCase.RemoveCronTickers:
                    await ReplyAsync(connection, message.RemoveCronTickers.RequestId,
                        services => RemoveCronTickersAsync(connection, message.RemoveCronTickers.Ids, services, hostToken)).ConfigureAwait(false);
                    break;
                case WorkerEvent.PayloadOneofCase.GetTimeTickerRequest:
                    await ReplyBytesAsync(connection, message.GetTimeTickerRequest.RequestId, message.GetTimeTickerRequest.TickerId,
                        (provider, id) => provider.GetTimeTickerRequest(id, hostToken)).ConfigureAwait(false);
                    break;
                case WorkerEvent.PayloadOneofCase.GetCronOccurrenceRequest:
                    await ReplyBytesAsync(connection, message.GetCronOccurrenceRequest.RequestId, message.GetCronOccurrenceRequest.TickerId,
                        (provider, id) => provider.GetCronTickerOccurrenceRequest(id, hostToken)).ConfigureAwait(false);
                    break;
            }
        }
        catch (OperationCanceledException) when (streamToken.IsCancellationRequested || lifetime.ApplicationStopping.IsCancellationRequested)
        {
            // The stream closed, or the host is stopping.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't handle {Message} from worker {WorkerId}", message.PayloadCase, connection.WorkerId);
        }
        finally
        {
            requestGate.Release();
        }
    }

    private async Task ReplyAsync(WorkerConnection connection, string requestId, Func<IServiceProvider, Task<int>> operation)
    {
        OperationResult reply;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var affected = await operation(scope.ServiceProvider).ConfigureAwait(false);
            reply = new OperationResult { RequestId = requestId, Success = true, Affected = affected };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            reply = new OperationResult { RequestId = requestId, Success = false, Error = ex.Message };
        }

        await connection.WriteAsync(new HubCommand { OperationResult = reply }).ConfigureAwait(false);
    }

    private async Task ReplyBytesAsync(
        WorkerConnection connection,
        string requestId,
        string tickerId,
        Func<ITickerPersistenceProvider<TTimeTicker, TCronTicker>, Guid, Task<byte[]>> load)
    {
        BytesResult reply;
        if (!Guid.TryParse(tickerId, out var id) || !connection.IsRunning(id))
        {
            reply = new BytesResult { RequestId = requestId, Success = false, Error = "A worker can only read the request of a run it is executing." };
        }
        else
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var provider = scope.ServiceProvider.GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>();
                var bytes = await load(provider, id).ConfigureAwait(false);
                reply = new BytesResult
                {
                    RequestId = requestId,
                    Success = true,
                    Found = bytes != null,
                    Payload = bytes != null ? ByteString.CopyFrom(bytes) : ByteString.Empty
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                reply = new BytesResult { RequestId = requestId, Success = false, Error = ex.Message };
            }
        }

        await connection.WriteAsync(new HubCommand { BytesResult = reply }).ConfigureAwait(false);
    }

    private static void RequireOwnNode(WorkerConnection connection, string? function)
    {
        if (!RemoteFunctionName.IsRemote(function!)
            || !string.Equals(RemoteFunctionName.GetNodeName(function!), connection.NodeName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Worker node '{connection.NodeName}' can only schedule its own functions; '{function}' isn't one.");
    }

    private static void PrepareNewTimeTicker(WorkerConnection connection, TTimeTicker ticker, bool isChild)
    {
        RequireOwnNode(connection, ticker.Function);
        ticker.Status = TickerStatus.Idle;
        ticker.LockHolder = null;
        ticker.LockedAt = null;
        ticker.ExecutedAt = null;
        ticker.ExceptionMessage = null;
        ticker.SkippedReason = null;
        ticker.ElapsedTime = 0;
        ticker.RetryCount = 0;
        ticker.InitIdentifier = null;
        if (!isChild) ticker.ParentId = null;

        foreach (var child in ticker.Children ?? [])
            PrepareNewTimeTicker(connection, child, isChild: true);
    }

    private static async Task<int> AddTimeTickersAsync(WorkerConnection connection, ByteString json, IServiceProvider services, CancellationToken token)
    {
        var tickers = Deserialize<TTimeTicker>(json);
        foreach (var ticker in tickers) PrepareNewTimeTicker(connection, ticker, isChild: false);

        var result = await services.GetRequiredService<ITimeTickerManager<TTimeTicker>>()
            .AddBatchAsync(tickers, token).ConfigureAwait(false);
        return Affected(result, tickers.Count);
    }

    private static async Task<int> UpdateTimeTickersAsync(WorkerConnection connection, ByteString json, IServiceProvider services, CancellationToken token)
    {
        var changes = Deserialize<TTimeTicker>(json);
        var ids = changes.Select(c => c.Id).ToList();
        var stored = (await services.GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>()
            .GetTimeTickers(t => ids.Contains(t.Id), token).ConfigureAwait(false)).ToDictionary(t => t.Id);

        var updated = new List<TTimeTicker>(changes.Count);
        foreach (var change in changes)
        {
            if (!stored.TryGetValue(change.Id, out var ticker))
                throw new InvalidOperationException($"Job {change.Id} doesn't exist.");
            RequireOwnNode(connection, ticker.Function);
            RequireOwnNode(connection, change.Function);

            // Only the schedule fields; status, locks and history stay the scheduler's.
            ticker.Function = change.Function;
            ticker.Description = change.Description;
            ticker.ExecutionTime = change.ExecutionTime;
            ticker.Request = change.Request;
            ticker.Retries = change.Retries;
            ticker.RetryIntervals = change.RetryIntervals;
            ticker.RunCondition = change.RunCondition;
            updated.Add(ticker);
        }

        var result = await services.GetRequiredService<ITimeTickerManager<TTimeTicker>>()
            .UpdateBatchAsync(updated, token).ConfigureAwait(false);
        return Affected(result, updated.Count);
    }

    private static async Task<int> RemoveTimeTickersAsync(WorkerConnection connection, IEnumerable<string> rawIds, IServiceProvider services, CancellationToken token)
    {
        var ids = ParseIds(rawIds);
        var stored = await services.GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>()
            .GetTimeTickers(t => ids.Contains(t.Id), token).ConfigureAwait(false);
        foreach (var ticker in stored) RequireOwnNode(connection, ticker.Function);

        var owned = stored.Select(t => t.Id).ToList();
        if (owned.Count == 0) return 0;
        var result = await services.GetRequiredService<ITimeTickerManager<TTimeTicker>>()
            .DeleteBatchAsync(owned, token).ConfigureAwait(false);
        return Affected(result, owned.Count);
    }

    private static async Task<int> AddCronTickersAsync(WorkerConnection connection, ByteString json, IServiceProvider services, CancellationToken token)
    {
        var tickers = Deserialize<TCronTicker>(json);
        foreach (var ticker in tickers)
        {
            RequireOwnNode(connection, ticker.Function);
            ticker.InitIdentifier = null;
            ticker.IsSystemPaused = false;
        }

        var result = await services.GetRequiredService<ICronTickerManager<TCronTicker>>()
            .AddBatchAsync(tickers, token).ConfigureAwait(false);
        return Affected(result, tickers.Count);
    }

    private static async Task<int> UpdateCronTickersAsync(WorkerConnection connection, ByteString json, IServiceProvider services, CancellationToken token)
    {
        var changes = Deserialize<TCronTicker>(json);
        var ids = changes.Select(c => c.Id).ToList();
        var stored = (await services.GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>()
            .GetCronTickers(c => ids.Contains(c.Id), token).ConfigureAwait(false)).ToDictionary(c => c.Id);

        var updated = new List<TCronTicker>(changes.Count);
        foreach (var change in changes)
        {
            if (!stored.TryGetValue(change.Id, out var cron))
                throw new InvalidOperationException($"Cron job {change.Id} doesn't exist.");
            RequireOwnNode(connection, cron.Function);
            RequireOwnNode(connection, change.Function);

            cron.Function = change.Function;
            cron.Description = change.Description;
            cron.Expression = change.Expression;
            cron.Request = change.Request;
            cron.Retries = change.Retries;
            cron.RetryIntervals = change.RetryIntervals;
            cron.IsEnabled = change.IsEnabled;
            updated.Add(cron);
        }

        var result = await services.GetRequiredService<ICronTickerManager<TCronTicker>>()
            .UpdateBatchAsync(updated, token).ConfigureAwait(false);
        return Affected(result, updated.Count);
    }

    private static async Task<int> RemoveCronTickersAsync(WorkerConnection connection, IEnumerable<string> rawIds, IServiceProvider services, CancellationToken token)
    {
        var ids = ParseIds(rawIds);
        var stored = await services.GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>()
            .GetCronTickers(c => ids.Contains(c.Id), token).ConfigureAwait(false);
        foreach (var cron in stored) RequireOwnNode(connection, cron.Function);

        var owned = stored.Select(c => c.Id).ToList();
        if (owned.Count == 0) return 0;
        var result = await services.GetRequiredService<ICronTickerManager<TCronTicker>>()
            .DeleteBatchAsync(owned, token).ConfigureAwait(false);
        return Affected(result, owned.Count);
    }

    private static List<T> Deserialize<T>(ByteString json)
        => JsonSerializer.Deserialize<List<T>>(json.Span, Json) ?? [];

    private static List<Guid> ParseIds(IEnumerable<string> ids)
        => ids.Select(id => Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty).Where(id => id != Guid.Empty).Distinct().ToList();

    private static int Affected<T>(TickerResult<T> result, int requested) where T : class
        => result.IsSucceeded
            ? (result.AffectedRows > 0 ? result.AffectedRows : requested)
            : throw new InvalidOperationException(result.Exception?.Message ?? "The scheduler couldn't save the jobs.", result.Exception);
}
