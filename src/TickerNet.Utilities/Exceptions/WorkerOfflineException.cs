using System;

namespace TickerNet.Utilities.Exceptions
{
    /// <summary>
    /// Signals that a job can't run now because the worker node that hosts its function is offline.
    /// The scheduler records the run as <c>Skipped</c>, with this exception's message as the reason,
    /// instead of <c>Failed</c>, and doesn't spend the job's retries: retrying is pointless while the
    /// node is down.
    /// </summary>
    /// <remarks>
    /// Remote functions are named <c>function@node</c> (see <see cref="RemoteFunctionName"/>). TickerNet.Hub
    /// throws this when no worker for <c>node</c> connects in time. It lives in TickerNet.Utilities so
    /// packages that reference only Utilities can throw it.
    /// </remarks>
    public class WorkerOfflineException : Exception
    {
        public WorkerOfflineException(string message) : base(message) { }

        public WorkerOfflineException(string message, Exception innerException) : base(message, innerException) { }
    }
}
