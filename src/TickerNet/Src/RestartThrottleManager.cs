using System;
using System.Threading;

namespace TickerNet;

public sealed class RestartThrottleManager : IDisposable
{
    private readonly Action _onRestartTriggered;
    private readonly object _lock = new();
    private Timer _debounceTimer;
    private volatile bool _restartPending;

    private readonly TimeSpan _debounceWindow;

    public RestartThrottleManager(Action onRestartTriggered)
        : this(onRestartTriggered, TimeSpan.FromMilliseconds(50))
    {
    }

    // Tests use a longer window so that timing on a busy machine can't split one burst of requests.
    internal RestartThrottleManager(Action onRestartTriggered, TimeSpan debounceWindow)
    {
        _onRestartTriggered = onRestartTriggered;
        _debounceWindow = debounceWindow;
    }

    public void RequestRestart()
    {
        lock (_lock)
        {
            _restartPending = true;

            // Create timer only when first needed
            if (_debounceTimer == null)
            {
                _debounceTimer = new Timer(OnTimerCallback, null,
                    _debounceWindow, Timeout.InfiniteTimeSpan);
            }
            else
            {
                // Just reset existing timer
                _debounceTimer.Change(_debounceWindow, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void OnTimerCallback(object state)
    {
        lock (_lock)
        {
            if (_restartPending)
            {
                _restartPending = false;
                _debounceTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _onRestartTriggered();
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }
}