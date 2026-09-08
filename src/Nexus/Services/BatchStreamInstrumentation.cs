// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Diagnostics;

namespace Nexus.Services;

internal readonly struct BatchStreamInstrumentation
{
    private static readonly AsyncLocal<BatchStreamInstrumentation> CurrentInstrumentation = new();

    private readonly long _startTimestamp;

    private BatchStreamInstrumentation(bool isEnabled)
    {
        IsEnabled = isEnabled;
        _startTimestamp = isEnabled ? Stopwatch.GetTimestamp() : 0;
    }

    public bool IsEnabled { get; }

    public static BatchStreamInstrumentation Current =>
        CurrentInstrumentation.Value;

    public static BatchStreamInstrumentation CreateFromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable("NEXUS_BATCH_STREAM_DEBUG");
        var isEnabled = string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

        return new BatchStreamInstrumentation(isEnabled);
    }

    public static IDisposable BeginScope(BatchStreamInstrumentation instrumentation)
    {
        var previous = CurrentInstrumentation.Value;
        CurrentInstrumentation.Value = instrumentation;

        return new Scope(previous);
    }

    public long GetTimestamp() =>
        IsEnabled ? Stopwatch.GetTimestamp() : 0;

    public double GetElapsedMilliseconds(long startTimestamp) =>
        IsEnabled ? (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency : 0;

    public double TotalElapsedMilliseconds =>
        IsEnabled ? (Stopwatch.GetTimestamp() - _startTimestamp) * 1000.0 / Stopwatch.Frequency : 0;

    public void Log(string message)
    {
        if (IsEnabled)
            Console.WriteLine($"[nexus batch stream] {DateTimeOffset.Now:HH:mm:ss.fff} elapsedMs={TotalElapsedMilliseconds:F1} {message}");
    }

    private sealed class Scope(BatchStreamInstrumentation previous) : IDisposable
    {
        public void Dispose()
        {
            CurrentInstrumentation.Value = previous;
        }
    }
}
