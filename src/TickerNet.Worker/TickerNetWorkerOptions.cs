using Grpc.Net.Client;
using TickerNet.Hub.Protocol;
using TickerNet.Utilities;

namespace TickerNet.Worker;

/// <summary>
/// Settings for <see cref="TickerNetWorkerServiceExtensions.AddTickerNetWorker{TTimeTicker,TCronTicker}"/>.
/// </summary>
public sealed class TickerNetWorkerOptions
{
    /// <summary>
    /// Address of the scheduler that runs TickerNet.Hub, for example <c>https://scheduler.internal:5001</c>.
    /// Required unless <see cref="HubAddresses"/> is set. The endpoint speaks gRPC over HTTP/2.
    /// </summary>
    public Uri? HubAddress { get; set; }

    /// <summary>
    /// Addresses of every scheduler instance that runs TickerNet.Hub, when there is more than one.
    /// The worker keeps a connection to each, so whichever instance picks up a job can send it here.
    /// Each address must reach one instance (not a load balancer in front of several), for example
    /// one DNS name per pod. Used together with <see cref="HubAddress"/> if both are set.
    /// </summary>
    public IList<Uri> HubAddresses { get; } = new List<Uri>();

    /// <summary><see cref="HubAddress"/> and <see cref="HubAddresses"/>, without duplicates.</summary>
    internal IReadOnlyList<Uri> EffectiveHubAddresses
        => (HubAddress is null ? HubAddresses : HubAddresses.Prepend(HubAddress)).Distinct().ToArray();

    /// <summary>
    /// The secret configured on the hub (<c>TickerNetHubOptions.SharedSecret</c>). Required, at least
    /// 32 characters. Keep it in your secret store or configuration.
    /// </summary>
    public string SharedSecret { get; set; } = string.Empty;

    /// <summary>
    /// Name of this worker's node. Required. Workers with the same node name are interchangeable
    /// replicas: the scheduler stores their functions as <c>function@node</c> and sends each run to
    /// one of them. Use the same name for every replica of a deployment, and only letters, digits and
    /// punctuation other than <c>@</c>. Node names are matched without regard to case; spell them the
    /// same way everywhere.
    /// </summary>
    public string NodeName { get; set; } = string.Empty;

    /// <summary>Most runs this worker executes at once. 0 (the default) means no limit.</summary>
    public int MaxConcurrency { get; set; }

    /// <summary>How often the worker tells the hub it is alive. Default: 15 seconds.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How long to wait for the hub to answer a request to add, change or remove jobs. Default: 30 seconds.</summary>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Longest wait between reconnect attempts. Default: 30 seconds.</summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// On shutdown, how long the worker waits for runs in progress to finish and report before it
    /// cancels them. Keep it below the host's shutdown timeout (30 seconds by default). Default: 25 seconds.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>
    /// Allow a plain <c>http</c> <see cref="HubAddress"/> that isn't on this machine. Without TLS the
    /// connection, including job requests, can be read and changed on the network, so only use this
    /// inside a trusted private network. Default: false.
    /// </summary>
    public bool AllowUnencryptedHttp { get; set; }

    /// <summary>Largest message, in bytes, either side may send. Default: 16 MB.</summary>
    public int MaxMessageSize { get; set; } = 16 * 1024 * 1024;

    /// <summary>
    /// Changes the gRPC channel before it connects, for example to set
    /// <see cref="GrpcChannelOptions.HttpHandler"/> for client certificates or a proxy.
    /// </summary>
    public Action<GrpcChannelOptions>? ConfigureChannel { get; set; }

    /// <summary>Limits which local functions this worker offers. Tests use it to model replicas on different versions.</summary>
    internal Func<string, bool>? FunctionFilter { get; set; }

    internal void Validate()
    {
        var addresses = EffectiveHubAddresses;
        if (addresses.Count == 0)
            throw new InvalidOperationException("Set TickerNetWorkerOptions.HubAddress (or HubAddresses for several scheduler instances).");
        foreach (var address in addresses)
        {
            if (address is null || !address.IsAbsoluteUri
                || (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp))
                throw new InvalidOperationException($"Hub address '{address}' must be an absolute http or https address.");
            if (address.Scheme == Uri.UriSchemeHttp && !address.IsLoopback && !AllowUnencryptedHttp)
                throw new InvalidOperationException(
                    $"Hub address '{address}' uses http to another machine. Use https, or set AllowUnencryptedHttp inside a trusted private network.");
        }
        if (string.IsNullOrWhiteSpace(SharedSecret) || SharedSecret.Length < HelloSignature.MinimumSecretLength)
            throw new InvalidOperationException(
                $"TickerNet.Worker needs a SharedSecret of at least {HelloSignature.MinimumSecretLength} characters, the same as the hub's.");
        if (!RemoteFunctionName.IsValidNodeName(NodeName))
            throw new InvalidOperationException(
                "TickerNetWorkerOptions.NodeName is required: 1-100 characters without '@' or whitespace.");
        if (MaxConcurrency < 0)
            throw new InvalidOperationException("TickerNetWorkerOptions.MaxConcurrency can't be negative.");
        if (HeartbeatInterval <= TimeSpan.Zero || OperationTimeout <= TimeSpan.Zero || MaxReconnectDelay <= TimeSpan.Zero
            || ShutdownTimeout < TimeSpan.Zero)
            throw new InvalidOperationException("TickerNetWorkerOptions intervals and timeouts must be positive.");
        if (MaxMessageSize <= 0)
            throw new InvalidOperationException("TickerNetWorkerOptions.MaxMessageSize must be positive.");
    }
}
