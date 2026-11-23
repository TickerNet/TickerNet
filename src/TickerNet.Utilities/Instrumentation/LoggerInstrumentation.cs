using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TickerNet.Utilities.Models;

namespace TickerNet.Utilities.Instrumentation
{
    /// <summary>
    /// No-operation implementation of ITickerNetInstrumentation
    /// </summary>
    public sealed class LoggerInstrumentation : TickerNetBaseLoggerInstrumentation, ITickerNetInstrumentation
    {
        public LoggerInstrumentation(ILogger<LoggerInstrumentation> logger, SchedulerOptionsBuilder optionsBuilder) : base(logger,  optionsBuilder.NodeIdentifier)
        {
        }

        public override Activity StartJobActivity(string activityName, InternalFunctionContext context)
            => null;
    }
}