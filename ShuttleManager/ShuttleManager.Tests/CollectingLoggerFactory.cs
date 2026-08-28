using Microsoft.Extensions.Logging;

namespace ShuttleManager.Tests;

public sealed class CollectingLoggerFactory : ILoggerFactory
{
    public List<string> Entries { get; } = [];

    private readonly object _lock = new();

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName) => new CollectingLogger(categoryName, this);

    public void Dispose()
    {
    }

    private sealed class CollectingLogger(string category, CollectingLoggerFactory owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (owner._lock)
            {
                owner.Entries.Add($"[{logLevel}] {category}: {formatter(state, exception)}");
            }
        }
    }
}
