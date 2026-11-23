using System;

namespace TickerNet.Utilities.Interfaces
{
    public interface ITickerClock
    {
        DateTime UtcNow { get; }
    }
}
