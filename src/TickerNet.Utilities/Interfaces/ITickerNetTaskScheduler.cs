using System;
using System.Threading;
using System.Threading.Tasks;
using TickerNet.Utilities.Enums;

namespace TickerNet.Utilities.Interfaces
{
    /// <summary>
    /// Abstraction for the internal TickerNet task scheduler.
    /// </summary>
    public interface ITickerNetTaskScheduler
    {
        ValueTask QueueAsync(
            Func<CancellationToken, Task> work,
            TickerTaskPriority priority,
            CancellationToken cancellationToken = default);

        void Freeze();

        void Resume();

        bool IsFrozen { get; }

        int ActiveWorkers { get; }

        int TotalQueuedTasks { get; }

        bool IsDisposed { get; }

        string GetDiagnostics();

        Task<bool> WaitForRunningTasksAsync(TimeSpan? timeout = null);
    }
}

