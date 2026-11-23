namespace TickerNet.Hub;

/// <summary>
/// A remote function failed on a worker node, or the worker went away during the run.
/// </summary>
public sealed class RemoteExecutionException : Exception
{
    private readonly string _remoteStackTrace;

    internal RemoteExecutionException(string nodeName, string workerId, string message, string errorType, string remoteStackTrace)
        : base(string.IsNullOrEmpty(errorType) ? message : $"{errorType}: {message}")
    {
        NodeName = nodeName;
        WorkerId = workerId;
        ErrorType = errorType ?? string.Empty;
        _remoteStackTrace = remoteStackTrace ?? string.Empty;
    }

    /// <summary>The worker node the run was sent to.</summary>
    public string NodeName { get; }

    /// <summary>The id the hub gave the worker process when it connected.</summary>
    public string WorkerId { get; }

    /// <summary>Full name of the exception type thrown on the worker, if any.</summary>
    public string ErrorType { get; }

    /// <summary>The stack trace from the worker, when the worker sent one.</summary>
    public override string? StackTrace => string.IsNullOrEmpty(_remoteStackTrace) ? base.StackTrace : _remoteStackTrace;
}
