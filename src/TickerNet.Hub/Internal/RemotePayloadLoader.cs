using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Enums;
using TickerNet.Utilities.Interfaces;

namespace TickerNet.Hub.Internal;

/// <summary>
/// Non-generic access to a job's stored request bytes, so <see cref="RemoteDispatch"/> can send
/// them with the run and the worker doesn't have to ask for them.
/// </summary>
internal interface IRemotePayloadLoader
{
    Task<byte[]?> LoadAsync(Guid tickerId, TickerType type, CancellationToken cancellationToken);
}

internal sealed class RemotePayloadLoader<TTimeTicker, TCronTicker>(ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider)
    : IRemotePayloadLoader
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public async Task<byte[]?> LoadAsync(Guid tickerId, TickerType type, CancellationToken cancellationToken)
        => type == TickerType.CronTickerOccurrence
            ? await provider.GetCronTickerOccurrenceRequest(tickerId, cancellationToken).ConfigureAwait(false)
            : await provider.GetTimeTickerRequest(tickerId, cancellationToken).ConfigureAwait(false);
}
