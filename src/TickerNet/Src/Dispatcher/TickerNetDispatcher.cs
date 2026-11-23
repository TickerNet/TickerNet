using System;
using System.Threading;
using System.Threading.Tasks;
using TickerNet.Utilities.Interfaces;
using TickerNet.Utilities.Models;

namespace TickerNet.Dispatcher
{
    internal class TickerNetDispatcher : ITickerNetDispatcher
    {
        private readonly ITickerNetTaskScheduler _taskScheduler;
        private readonly ITickerExecutionTaskHandler _taskHandler;
        private readonly ITickerFunctionConcurrencyGate _concurrencyGate;

        public bool IsEnabled => true;

        public TickerNetDispatcher(ITickerNetTaskScheduler taskScheduler, ITickerExecutionTaskHandler taskHandler, ITickerFunctionConcurrencyGate concurrencyGate)
        {
            _taskScheduler = taskScheduler ?? throw new ArgumentNullException(nameof(taskScheduler));
            _taskHandler = taskHandler ?? throw new ArgumentNullException(nameof(taskHandler));
            _concurrencyGate = concurrencyGate ?? throw new ArgumentNullException(nameof(concurrencyGate));
        }

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
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
