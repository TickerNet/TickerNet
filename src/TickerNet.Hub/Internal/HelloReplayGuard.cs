using System.Collections.Concurrent;

namespace TickerNet.Hub.Internal;

/// <summary>
/// Remembers worker nonces for twice the allowed clock skew, so a captured call can't be replayed
/// to this hub process. Nonces are kept in memory: a restarted hub forgets them, which the
/// 5-minute timestamp window bounds.
/// </summary>
internal sealed class HelloReplayGuard
{
    public static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

    private const int MaxNonceLength = 100;

    private readonly ConcurrentDictionary<string, long> _seen = new(StringComparer.Ordinal);
    private long _nextPruneTicks;

    public bool IsFresh(long unixSeconds, DateTimeOffset now)
    {
        // Compared as bounds rather than a difference, so extreme values can't overflow.
        var current = now.ToUnixTimeSeconds();
        var skew = (long)AllowedClockSkew.TotalSeconds;
        return unixSeconds >= current - skew && unixSeconds <= current + skew;
    }

    public static bool IsWellFormedNonce(string? nonce)
        => !string.IsNullOrEmpty(nonce)
           && nonce.Length <= MaxNonceLength
           && nonce.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    /// <summary>Records <paramref name="nonce"/>; false if it is malformed or was already used.</summary>
    public bool TryUse(string nonce, DateTimeOffset now)
    {
        if (!IsWellFormedNonce(nonce)) return false;

        var nowTicks = now.UtcTicks;
        if (nowTicks >= Interlocked.Read(ref _nextPruneTicks))
        {
            Interlocked.Exchange(ref _nextPruneTicks, nowTicks + TimeSpan.FromMinutes(1).Ticks);
            foreach (var entry in _seen)
            {
                if (entry.Value < nowTicks) _seen.TryRemove(entry);
            }
        }

        return _seen.TryAdd(nonce, nowTicks + (AllowedClockSkew * 2).Ticks);
    }
}
