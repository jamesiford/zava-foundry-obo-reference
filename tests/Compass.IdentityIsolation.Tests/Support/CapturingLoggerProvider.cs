using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Compass.IdentityIsolation.Tests.Support;

/// <summary>
/// Records every log line, including the full exception text, from both the bot and the agents. Exceptions are
/// included on purpose: <c>Exception.ToString()</c> is a classic place for a token to leak into a log.
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines;

    public void Add(string line) => _lines.Enqueue(line);

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _lines);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var text = $"{category}: {formatter(state, exception)}";
            lines.Enqueue(exception is null ? text : $"{text} {exception}");
        }
    }
}
