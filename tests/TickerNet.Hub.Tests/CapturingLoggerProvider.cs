using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace TickerNet.Hub.Tests;

/// <summary>Keeps a hub's recent log lines so a failing test can show what the hub did.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines.ToArray();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose() { }

    private void Add(string line)
    {
        _lines.Enqueue(line);
        while (_lines.Count > 300 && _lines.TryDequeue(out _)) { }
    }

    private sealed class Logger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel)
            => logLevel >= LogLevel.Information && category.StartsWith("TickerNet", StringComparison.Ordinal);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var text = $"{DateTime.UtcNow:HH:mm:ss.fff} {logLevel} {category.Split('.').Last()}: {formatter(state, exception)}";
            if (exception != null) text += $" [{exception.GetType().Name}: {exception.Message}]";
            owner.Add(text);
        }
    }
}
