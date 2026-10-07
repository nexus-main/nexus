// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Nexus.Tests;

internal sealed class VisualizationTimingLogger : ILoggerFactory
{
    public ConcurrentQueue<Entry> Entries { get; } = new();

    public sealed record Entry(string Category, LogLevel Level, string Message, Exception? Exception,
        IReadOnlyDictionary<string, object?> Fields)
    {
        public string Phase => (string)Fields["Phase"]!;
        public string Milestone => (string)Fields["Milestone"]!;
        public string Outcome => (string)Fields["Outcome"]!;
    }

    public ILogger CreateLogger(string categoryName) => new Capture(this, categoryName);
    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
    public void Dispose() { }

    private sealed class Capture(VisualizationTimingLogger owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                owner.Entries.Enqueue(new Entry(category, logLevel, formatter(state, exception), exception,
                    ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary()));
        }
    }
}
