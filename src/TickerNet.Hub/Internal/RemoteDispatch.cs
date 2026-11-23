using System.Collections.Concurrent;
using System.Diagnostics;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using TickerNet.Exceptions;
using TickerNet.Hub.Protocol.V1;
using TickerNet.Utilities;
using TickerNet.Utilities.Base;
using TickerNet.Utilities.Enums;
using TickerNet.Utilities.Exceptions;

namespace TickerNet.Hub.Internal;

/// <summary>
/// Builds the function delegate the scheduler calls for a remote function (<c>function@node</c>).
/// The scheduler's normal job runner calls it like a local function, so retries, cancelling,
/// status and tracing work the same. The delegate sends one attempt to a worker of the node and
/// turns the worker's result back into the outcome the runner expects: return for success,
/// <see cref="TaskCanceledException"/> for a cancel, <see cref="TerminateExecutionException"/>
/// for a skip, <see cref="WorkerOfflineException"/> when no worker takes the run in time, and
/// <see cref="RemoteExecutionException"/> for a failure.
/// </summary>
/// <remarks>
/// A run that never reached a worker (the connection closed first, or the worker rejected it
/// because it is stopping) goes to another worker of the node within
/// <see cref="TickerNetHubOptions.WorkerWaitTimeout"/>, without using a retry.
/// </remarks>
internal static class RemoteDispatch
{
    private static readonly ConcurrentDictionary<string, TickerFunctionDelegate> Delegates = new(StringComparer.OrdinalIgnoreCase);

    public static TickerFunctionDelegate For(string nodeName)
        => Delegates.GetOrAdd(nodeName, static node => (cancellationToken, serviceProvider, context)
            => RunAsync(node, cancellationToken, serviceProvider, context));

    private static async Task RunAsync(string nodeName, CancellationToken cancellationToken, IServiceProvider serviceProvider, TickerFunctionContext context)
    {
        var registry = serviceProvider.GetRequiredService<WorkerRegistry>();
        var options = serviceProvider.GetRequiredService<TickerNetHubOptions>();
        var payloads = serviceProvider.GetRequiredService<IRemotePayloadLoader>();

        var functionName = RemoteFunctionName.GetFunctionName(context.FunctionName);
        var payload = await payloads.LoadAsync(context.Id, context.Type, cancellationToken).ConfigureAwait(false);
        var waited = Stopwatch.StartNew();
        var tried = new HashSet<string>(StringComparer.Ordinal);

        while (true)
        {
            var remaining = options.WorkerWaitTimeout - waited.Elapsed;
            var connection = await registry.WaitForWorkerAsync(
                nodeName, functionName, remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining, tried, cancellationToken).ConfigureAwait(false);

            if (connection == null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new WorkerOfflineException(
                    $"Worker node '{nodeName}' is offline: no worker took the run within {options.WorkerWaitTimeout.TotalSeconds:0.#} seconds.");
            }

            var request = new ExecuteFunction
            {
                TickerId = context.Id.ToString(),
                FunctionName = functionName,
                Type = (int)context.Type,
                RetryCount = context.RetryCount,
                IsDue = context.IsDue,
                ScheduledFor = Timestamp.FromDateTime(DateTime.SpecifyKind(context.ScheduledFor, DateTimeKind.Utc)),
                RequestPayload = payload is { Length: > 0 } ? ByteString.CopyFrom(payload) : ByteString.Empty,
                ParentId = context.ParentId?.ToString() ?? string.Empty
            };

            ExecutionResult result;
            try
            {
                result = await connection.ExecuteAsync(context.Id, request, options.ExecutionTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (WorkerUnavailableException)
            {
                tried.Add(connection.WorkerId);
                continue;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Sent from here rather than from a token callback: the wait above ends on the same
                // token and can unregister such a callback before it runs.
                _ = connection.CancelAsync(context.Id);
                throw new TaskCanceledException("The run was cancelled.");
            }
            catch (TimeoutException)
            {
                _ = connection.CancelAsync(context.Id);
                throw new RemoteExecutionException(nodeName, connection.WorkerId,
                    $"The worker didn't finish within the hub's ExecutionTimeout of {options.ExecutionTimeout}.",
                    nameof(TimeoutException), string.Empty);
            }
            catch (WorkerDisconnectedException ex)
            {
                throw new RemoteExecutionException(nodeName, connection.WorkerId, ex.Message,
                    nameof(WorkerDisconnectedException), string.Empty);
            }

            if (result.Rejected)
            {
                tried.Add(connection.WorkerId);
                continue;
            }

            if (result.Success)
                return;

            if (result.Cancelled)
                throw new TaskCanceledException(string.IsNullOrEmpty(result.Error) ? "The run was cancelled." : result.Error);

            if (result.Terminated)
            {
                var status = System.Enum.IsDefined(typeof(TickerStatus), result.TerminatedStatus)
                    ? (TickerStatus)result.TerminatedStatus
                    : TickerStatus.Skipped;
                throw new TerminateExecutionException(status, string.IsNullOrEmpty(result.Error) ? "The function stopped the run." : result.Error);
            }

            throw new RemoteExecutionException(nodeName, connection.WorkerId,
                string.IsNullOrEmpty(result.Error) ? "The function failed on the worker." : result.Error,
                result.ErrorType, result.ErrorStackTrace);
        }
    }
}
