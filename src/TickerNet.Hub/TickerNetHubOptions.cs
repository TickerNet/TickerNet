using TickerNet.Hub.Protocol;

namespace TickerNet.Hub;

/// <summary>
/// Settings for <see cref="TickerNetHubServiceExtensions.AddTickerNetHub{TTimeTicker,TCronTicker}"/>.
/// </summary>
public sealed class TickerNetHubOptions
{
    /// <summary>
    /// Secret every worker must prove it knows when it connects. Required, at least 32 characters.
    /// Use a long random value, keep it in your secret store or configuration, and give every
    /// worker the same value.
    /// </summary>
    public string SharedSecret { get; set; } = string.Empty;

    /// <summary>
    /// How long a due job waits for a worker of its node to be connected. After that the run is
    /// recorded as <c>Skipped</c> with a "worker node offline" reason. Default: 30 seconds.
    /// </summary>
    public TimeSpan WorkerWaitTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Longest a single run may take on a worker before the scheduler stops waiting and records a
    /// failure. <c>null</c> (the default) means no limit, the same as a local job.
    /// </summary>
    public TimeSpan? ExecutionTimeout { get; set; }

    /// <summary>
    /// A worker that sends nothing for this long is treated as gone, and its connection is closed.
    /// Workers send a heartbeat every 15 seconds by default. Default: 45 seconds.
    /// </summary>
    public TimeSpan WorkerTimeout { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Create cron jobs for worker functions that have a cron expression in
    /// <c>[TickerFunction]</c>, as the scheduler does for its own functions. Default: true.
    /// </summary>
    public bool SeedCronTickers { get; set; } = true;

    /// <summary>
    /// Pause a remote function's cron jobs while no worker for its node is connected, and resume
    /// them when one connects, so the scheduler doesn't create runs that can only be skipped.
    /// Default: true.
    /// </summary>
    public bool PauseCronTickersWhileOffline { get; set; } = true;

    /// <summary>Largest message, in bytes, either side may send. Default: 16 MB.</summary>
    public int MaxMessageSize { get; set; } = 16 * 1024 * 1024;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(SharedSecret) || SharedSecret.Length < HelloSignature.MinimumSecretLength)
            throw new InvalidOperationException(
                $"TickerNet.Hub needs a SharedSecret of at least {HelloSignature.MinimumSecretLength} characters. " +
                "Set TickerNetHubOptions.SharedSecret from your secret store or configuration.");
        if (WorkerWaitTimeout < TimeSpan.Zero)
            throw new InvalidOperationException("TickerNetHubOptions.WorkerWaitTimeout can't be negative.");
        if (ExecutionTimeout is { } timeout && timeout <= TimeSpan.Zero)
            throw new InvalidOperationException("TickerNetHubOptions.ExecutionTimeout must be positive, or null for no limit.");
        if (WorkerTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("TickerNetHubOptions.WorkerTimeout must be positive.");
        if (MaxMessageSize <= 0)
            throw new InvalidOperationException("TickerNetHubOptions.MaxMessageSize must be positive.");
    }
}
