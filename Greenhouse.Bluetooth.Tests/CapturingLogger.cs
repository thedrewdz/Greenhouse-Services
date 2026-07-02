using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Greenhouse.Bluetooth.Tests;

/// <summary>Captures every formatted log message so tests can assert on log content.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly ConcurrentBag<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _messages.Add(formatter(state, exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
