using System;

namespace TickerNet.Utilities.Interfaces;

internal interface ITickerOptionsBuilder
{
    internal TimeSpan TimeOutChecker { get; }
}