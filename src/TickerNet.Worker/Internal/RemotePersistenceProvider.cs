using System.Linq.Expressions;
using System.Text.Json;
using Google.Protobuf;
using TickerNet.Hub.Protocol.V1;
using TickerNet.Utilities;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Interfaces;
using TickerNet.Utilities.Models;

namespace TickerNet.Worker.Internal;

/// <summary>
/// The worker's job store: sends requests to add, change or remove jobs to the hub over the worker
/// stream, naming this worker's functions <c>function@node</c>. Everything else lives on the
/// scheduler, so those methods throw <see cref="NotSupportedException"/>.
/// </summary>
internal sealed class RemotePersistenceProvider<TTimeTicker, TCronTicker>(
    TickerNetWorkerOptions options,
    WorkerClient client) : ITickerPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static NotSupportedException NotOnWorker(string what)
        => new($"{what} isn't available on a TickerNet worker. Jobs are stored on the scheduler; query them there.");

    private string Qualify(string function) => RemoteFunctionName.Qualify(function, options.NodeName);

    private void QualifyTimeTicker(TTimeTicker ticker)
    {
        ticker.Function = Qualify(ticker.Function);
        if (ticker.Children == null) return;
        foreach (var child in ticker.Children) QualifyTimeTicker(child);
    }

    private static string NewRequestId() => Guid.NewGuid().ToString("N");

    private async Task<int> SendAsync(string requestId, WorkerEvent message, CancellationToken cancellationToken)
    {
        var result = await client.SendOperationAsync(requestId, message, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException(string.IsNullOrEmpty(result.Error) ? "The scheduler refused the request." : result.Error);
        return result.Affected;
    }

    private async Task<byte[]> GetBytesAsync(string requestId, WorkerEvent message, CancellationToken cancellationToken)
    {
        var result = await client.SendBytesRequestAsync(requestId, message, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException(string.IsNullOrEmpty(result.Error) ? "The scheduler refused the request." : result.Error);
        return result.Found ? result.Payload.ToByteArray() : null!;
    }

    // ---- Adding, changing and removing jobs ----

    public Task<int> AddTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default)
    {
        if (tickers is not { Length: > 0 }) return Task.FromResult(0);
        foreach (var ticker in tickers) QualifyTimeTicker(ticker);
        var id = NewRequestId();
        return SendAsync(id, new WorkerEvent
        {
            AddTimeTickers = new AddTimeTickersRequest { RequestId = id, EntitiesJson = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(tickers, Json)) }
        }, cancellationToken);
    }

    public Task<int> UpdateTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default)
    {
        if (tickers is not { Length: > 0 }) return Task.FromResult(0);
        foreach (var ticker in tickers) QualifyTimeTicker(ticker);
        var id = NewRequestId();
        return SendAsync(id, new WorkerEvent
        {
            UpdateTimeTickers = new UpdateTimeTickersRequest { RequestId = id, EntitiesJson = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(tickers, Json)) }
        }, cancellationToken);
    }

    public Task<int> RemoveTimeTickers(Guid[] tickerIds, CancellationToken cancellationToken = default)
    {
        if (tickerIds is not { Length: > 0 }) return Task.FromResult(0);
        var id = NewRequestId();
        var request = new RemoveTimeTickersRequest { RequestId = id };
        request.Ids.AddRange(tickerIds.Select(t => t.ToString()));
        return SendAsync(id, new WorkerEvent { RemoveTimeTickers = request }, cancellationToken);
    }

    public Task<int> InsertCronTickers(TCronTicker[] tickers, CancellationToken cancellationToken)
    {
        if (tickers is not { Length: > 0 }) return Task.FromResult(0);
        foreach (var ticker in tickers) ticker.Function = Qualify(ticker.Function);
        var id = NewRequestId();
        return SendAsync(id, new WorkerEvent
        {
            InsertCronTickers = new InsertCronTickersRequest { RequestId = id, EntitiesJson = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(tickers, Json)) }
        }, cancellationToken);
    }

    public Task<int> UpdateCronTickers(TCronTicker[] cronTicker, CancellationToken cancellationToken)
    {
        if (cronTicker is not { Length: > 0 }) return Task.FromResult(0);
        foreach (var ticker in cronTicker) ticker.Function = Qualify(ticker.Function);
        var id = NewRequestId();
        return SendAsync(id, new WorkerEvent
        {
            UpdateCronTickers = new UpdateCronTickersRequest { RequestId = id, EntitiesJson = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(cronTicker, Json)) }
        }, cancellationToken);
    }

    public Task<int> RemoveCronTickers(Guid[] cronTickerIds, CancellationToken cancellationToken)
    {
        if (cronTickerIds is not { Length: > 0 }) return Task.FromResult(0);
        var id = NewRequestId();
        var request = new RemoveCronTickersRequest { RequestId = id };
        request.Ids.AddRange(cronTickerIds.Select(t => t.ToString()));
        return SendAsync(id, new WorkerEvent { RemoveCronTickers = request }, cancellationToken);
    }

    // ---- Stored requests ----

    public Task<byte[]> GetTimeTickerRequest(Guid id, CancellationToken cancellationToken)
    {
        if (client.TryGetPayload(id, out var payload)) return Task.FromResult(payload);
        var requestId = NewRequestId();
        return GetBytesAsync(requestId, new WorkerEvent
        {
            GetTimeTickerRequest = new GetTimeTickerRequestRequest { RequestId = requestId, TickerId = id.ToString() }
        }, cancellationToken);
    }

    public Task<byte[]> GetCronTickerOccurrenceRequest(Guid tickerId, CancellationToken cancellationToken = default)
    {
        if (client.TryGetPayload(tickerId, out var payload)) return Task.FromResult(payload);
        var requestId = NewRequestId();
        return GetBytesAsync(requestId, new WorkerEvent
        {
            GetCronOccurrenceRequest = new GetCronTickerOccurrenceRequestRequest { RequestId = requestId, TickerId = tickerId.ToString() }
        }, cancellationToken);
    }

    // ---- Scheduler-only operations ----

    public IAsyncEnumerable<TimeTickerEntity> QueueTimeTickers(TimeTickerEntity[] timeTickers, CancellationToken cancellationToken = default) => throw NotOnWorker("Queueing jobs");
    public IAsyncEnumerable<TimeTickerEntity> QueueTimedOutTimeTickers(CancellationToken cancellationToken = default) => throw NotOnWorker("Queueing jobs");
    public Task ReleaseAcquiredTimeTickers(Guid[] timeTickerIds, CancellationToken cancellationToken = default) => throw NotOnWorker("Releasing jobs");
    public Task<TimeTickerEntity[]> GetEarliestTimeTickers(CancellationToken cancellationToken = default) => throw NotOnWorker("Polling for jobs");
    public Task<int> UpdateTimeTicker(InternalFunctionContext functionContext, CancellationToken cancellationToken = default) => throw NotOnWorker("Recording run status");
    public Task UpdateTimeTickersWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default) => throw NotOnWorker("Recording run status");
    public Task<TimeTickerEntity[]> AcquireImmediateTimeTickersAsync(Guid[] ids, CancellationToken cancellationToken = default) => throw NotOnWorker("Acquiring jobs");
    public Task MigrateDefinedCronTickers((string Function, string Expression)[] cronTickers, CancellationToken cancellationToken = default) => throw NotOnWorker("Seeding cron jobs");
    public Task<CronTickerEntity[]> GetAllCronTickerExpressions(CancellationToken cancellationToken) => throw NotOnWorker("Reading cron schedules");
    public Task ReleaseDeadNodeTimeTickerResources(string instanceIdentifier, CancellationToken cancellationToken = default) => throw NotOnWorker("Releasing jobs");
    public Task<CronTickerOccurrenceEntity<TCronTicker>> GetEarliestAvailableCronOccurrence(Guid[] ids, CancellationToken cancellationToken = default) => throw NotOnWorker("Polling for jobs");
    public IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueCronTickerOccurrences((DateTime Key, InternalManagerContext[] Items) cronTickerOccurrences, CancellationToken cancellationToken = default) => throw NotOnWorker("Queueing jobs");
    public IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueTimedOutCronTickerOccurrences(CancellationToken cancellationToken = default) => throw NotOnWorker("Queueing jobs");
    public Task UpdateCronTickerOccurrence(InternalFunctionContext functionContext, CancellationToken cancellationToken = default) => throw NotOnWorker("Recording run status");
    public Task ReleaseAcquiredCronTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default) => throw NotOnWorker("Releasing jobs");
    public Task UpdateCronTickerOccurrencesWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default) => throw NotOnWorker("Recording run status");
    public Task ReleaseDeadNodeOccurrenceResources(string instanceIdentifier, CancellationToken cancellationToken = default) => throw NotOnWorker("Releasing jobs");
    public ITickerQueryable<TTimeTicker> TimeTickersQuery() => throw NotOnWorker("Querying jobs");
    public ITickerQueryable<TCronTicker> CronTickersQuery() => throw NotOnWorker("Querying jobs");
    public ITickerQueryable<CronTickerOccurrenceEntity<TCronTicker>> CronTickerOccurrencesQuery() => throw NotOnWorker("Querying jobs");
    public Task<TTimeTicker> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default) => throw NotOnWorker("Querying jobs");
    public Task<TTimeTicker[]> GetTimeTickers(Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken = default) => throw NotOnWorker("Querying jobs");
    public Task<PaginationResult<TTimeTicker>> GetTimeTickersPaginated(Expression<Func<TTimeTicker, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default) => throw NotOnWorker("Querying jobs");
    public Task<TCronTicker> GetCronTickerById(Guid id, CancellationToken cancellationToken) => throw NotOnWorker("Querying jobs");
    public Task<TCronTicker[]> GetCronTickers(Expression<Func<TCronTicker, bool>> predicate, CancellationToken cancellationToken) => throw NotOnWorker("Querying jobs");
    public Task<PaginationResult<TCronTicker>> GetCronTickersPaginated(Expression<Func<TCronTicker, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default) => throw NotOnWorker("Querying jobs");
    public Task<CronTickerOccurrenceEntity<TCronTicker>[]> GetAllCronTickerOccurrences(Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, CancellationToken cancellationToken = default) => throw NotOnWorker("Querying jobs");
    public Task<PaginationResult<CronTickerOccurrenceEntity<TCronTicker>>> GetAllCronTickerOccurrencesPaginated(Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default) => throw NotOnWorker("Querying jobs");
    public Task<int> InsertCronTickerOccurrences(CronTickerOccurrenceEntity<TCronTicker>[] cronTickerOccurrences, CancellationToken cancellationToken) => throw NotOnWorker("Creating cron occurrences");
    public Task<int> RemoveCronTickerOccurrences(Guid[] cronTickerOccurrences, CancellationToken cancellationToken) => throw NotOnWorker("Removing cron occurrences");
    public Task<CronTickerOccurrenceEntity<TCronTicker>[]> AcquireImmediateCronOccurrencesAsync(Guid[] occurrenceIds, CancellationToken cancellationToken = default) => throw NotOnWorker("Acquiring jobs");
}
