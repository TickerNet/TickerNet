using System;

namespace TickerNet.Utilities.Exceptions
{
    /// <summary>
    /// Earlier name for <see cref="WorkerOfflineException"/>, from TickerQ's remote executor, where remote
    /// worker processes were called SDK nodes. It still works: the scheduler handles it exactly like
    /// <see cref="WorkerOfflineException"/>.
    /// </summary>
    [Obsolete("Use WorkerOfflineException. SdkOfflineSkipException is kept so code written against TickerQ's remote executor keeps compiling.")]
    public sealed class SdkOfflineSkipException : WorkerOfflineException
    {
        public SdkOfflineSkipException(string message) : base(message) { }
    }
}
