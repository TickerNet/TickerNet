using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using TickerNet.Utilities.Interfaces;
using TickerNet.Utilities.Models;

namespace TickerNet.Dispatcher
{
    internal class TickerNetDispatcher : ITickerNetDispatcher
    {
        private readonly ITickerNetTaskScheduler _taskScheduler;
        private readonly ITickerExecutionTaskHandler _taskHandler;
        private readonly ITickerFunctionConcurrencyGate _concurrencyGate;
        private readonly CancellationToken _hostStopping;

        public bool IsEnabled => true;

        public TickerNetDispatcher(
            ITickerNetTaskScheduler taskScheduler,
            ITickerExecutionTaskHandler taskHandler,
            ITickerFunctionConcurrencyGate concurrencyGate,
            IHostApplicationLifetime lifetime = null)
        {
            _taskScheduler = taskScheduler ?? throw new ArgumentNullException(nameof(taskScheduler));
            _taskHandler = taskHandler ?? throw new ArgumentNullException(nameof(taskHandler));
            _concurrencyGate = concurrencyGate ?? throw new ArgumentNullException(nameof(concurrencyGate));
            _hostStopping = lifetime?.ApplicationStopping ?? CancellationToken.None;
        }

        /// <remarks>
        /// <paramref name="cancellationToken"/> belongs to the caller (an HTTP request, a manager call, a worker's
        /// connection) and is not used for the jobs: by the time they are dispatched they are already marked
        /// InProgress, so dropping or cancelling them with the caller would cancel or strand them. Jobs run with
        /// the host's stopping token; cancel a single job with <c>TickerCancellationTokenManager</c>.
        /// </remarks>
        public async Task DispatchAsync(InternalFunctionContext[] contexts, CancellationToken cancellationToken = default)
        {
            if (contexts == null || contexts.Length == 0)
                return;

            foreach (var context in contexts)
            {
                var semaphore = _concurrencyGate.GetSemaphoreOrNull(context.FunctionName, context.CachedMaxConcurrency);

                await _taskScheduler.QueueAsync(
                    async ct =>
                    {
                        if (semaphore != null)
                            await semaphore.WaitAsync(ct).ConfigureAwait(false);

                        try
                        {
                            await _taskHandler.ExecuteTaskAsync(context, false, ct).ConfigureAwait(false);
                        }
                        finally
                        {
                            semaphore?.Release();
                        }
                    },
                    context.CachedPriority,
                    _hostStopping).ConfigureAwait(false);
            }
        }
    }
}
