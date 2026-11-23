using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TickerNet.Hub.Protocol.V1;

namespace TickerNet.Hub.Protocol;

/// <summary>
/// Signs and checks the worker's call headers, its <see cref="Hello"/>, and the hub's
/// <see cref="HelloAck"/>. Compiled into both TickerNet.Hub and TickerNet.Worker so the two sides
/// always build the same text. Every signed field is free of control characters (the hub rejects
/// any that aren't), so the newline and tab separators are unambiguous.
/// </summary>
internal static class HelloSignature
{
    /// <summary>The protocol version described by <c>worker.proto</c>.</summary>
    public const int ProtocolVersion = 1;

    /// <summary>Shortest shared secret either side accepts.</summary>
    public const int MinimumSecretLength = 32;

    public const string NodeHeader = "tickernet-node";
    public const string NonceHeader = "tickernet-nonce";
    public const string TimestampHeader = "tickernet-timestamp";
    public const string SignatureHeader = "tickernet-signature";

    /// <summary>Signature for the call headers, checked before the hub reads the stream.</summary>
    public static string ForCall(string sharedSecret, string nodeName, string nonce, long unixSeconds)
        => Sign(sharedSecret, $"tickernet-worker-call\n{nodeName}\n{nonce}\n{unixSeconds}");

    /// <summary>
    /// The text the Hello HMAC covers: a fixed label, the node name, worker version, protocol
    /// version, nonce, timestamp, concurrency limit, then one line per function (sorted by name)
    /// with its name, cron expression, priority and concurrency limit.
    /// </summary>
    public static string Canonical(Hello hello)
    {
        var builder = new StringBuilder()
            .Append("tickernet-worker-hello\n")
            .Append(hello.NodeName).Append('\n')
            .Append(hello.WorkerVersion).Append('\n')
            .Append(hello.ProtocolVersion).Append('\n')
            .Append(hello.Nonce).Append('\n')
            .Append(hello.UnixSeconds).Append('\n')
            .Append(hello.MaxConcurrency);

        foreach (var function in hello.Functions.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            builder.Append('\n')
                .Append(function.Name).Append('\t')
                .Append(function.CronExpression).Append('\t')
                .Append(function.Priority).Append('\t')
                .Append(function.MaxConcurrency);
        }

        return builder.ToString();
    }

    public static string ForHello(string sharedSecret, Hello hello) => Sign(sharedSecret, Canonical(hello));

    /// <summary>Signature the hub puts on its HelloAck: the worker's nonce and the id it was given.</summary>
    public static string ForAck(string sharedSecret, string workerNonce, string workerId)
        => Sign(sharedSecret, $"tickernet-hub-ack\n{workerNonce}\n{workerId}");

    public static bool Matches(string expected, string? actual)
        => !string.IsNullOrEmpty(actual)
           && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));

    /// <summary>True when <paramref name="value"/> has no control characters and is at most <paramref name="maxLength"/> long.</summary>
    public static bool IsPlainText(string? value, int maxLength)
        => value != null && value.Length <= maxLength && !value.Any(char.IsControl);

    private static string Sign(string sharedSecret, string text)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sharedSecret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(text)));
    }
}
