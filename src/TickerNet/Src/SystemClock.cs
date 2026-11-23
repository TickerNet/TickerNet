using System;
using TickerNet.Utilities.Interfaces;

namespace TickerNet
{
    internal class TickerSystemClock : ITickerClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
