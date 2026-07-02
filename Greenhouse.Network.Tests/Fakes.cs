using Microsoft.Extensions.Logging;

namespace Greenhouse.Network.Tests;

/// <summary>Configurable <see cref="INmcliCommandRunner"/> that records its invocations.</summary>
internal sealed class FakeNmcliCommandRunner : INmcliCommandRunner
{
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<NmcliResult>> _behavior;

    public FakeNmcliCommandRunner(Func<IReadOnlyList<string>, CancellationToken, Task<NmcliResult>> behavior)
    {
        _behavior = behavior;
    }

    public FakeNmcliCommandRunner(NmcliResult result)
        : this((_, _) => Task.FromResult(result))
    {
    }

    public List<IReadOnlyList<string>> Invocations { get; } = new();

    public Task<NmcliResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        Invocations.Add(arguments);
        return _behavior(arguments, cancellationToken);
    }
}

/// <summary>Captures log messages so tests can assert on their content (e.g. no password).</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
