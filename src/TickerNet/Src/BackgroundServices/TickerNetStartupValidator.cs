using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerNet.Utilities;
using TickerNet.Utilities.Enums;

namespace TickerNet.BackgroundServices;

internal class TickerNetStartupValidator : IHostedService
{
    private readonly TickerExecutionContext _executionContext;
    private readonly TickerNetInitializerHostedService _initializer;
    private readonly ILogger<TickerNetStartupValidator> _logger;

    public TickerNetStartupValidator(
        TickerExecutionContext executionContext,
        TickerNetInitializerHostedService initializer,
        ILogger<TickerNetStartupValidator> logger)
    {
        _executionContext = executionContext;
        _initializer = initializer;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_initializer.InitializationRequested)
        {
            const string message = "TickerNet — UseTickerNet() was not called. Call app.UseTickerNet() before app.Run() to initialize the scheduler.";
            _logger.LogWarning(message);
            _executionContext.NotifyCoreAction?.Invoke(message, CoreNotifyActionType.NotifyHostExceptionMessage);
        }
        else if (TickerFunctionProvider.TickerFunctions.Count == 0)
        {
            const string message = "TickerNet — No ticker functions registered. Ensure you have methods decorated with [TickerFunction].";
            _logger.LogWarning(message);
            _executionContext.NotifyCoreAction?.Invoke(message, CoreNotifyActionType.NotifyHostExceptionMessage);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
