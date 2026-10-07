// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Collections.Concurrent;
using System.Diagnostics;

namespace Nexus.Services;

/// <summary>Opt-in, request-local timings. Only fixed phase/outcome labels and numeric IDs belong here.</summary>
internal sealed class VisualizationTiming(ILogger logger, Guid requestId)
{
    public const string Header = "X-Nexus-Visualization-Trace";
    public const string Category = "Nexus.Visualization.Timing";
    private readonly long _created = Stopwatch.GetTimestamp();
    private readonly ConcurrentDictionary<(string Phase, int Worker), Aggregate> _aggregates = new();
    private int _cancellationRequested;
    public string RequestId { get; } = requestId.ToString("D");
    public string Outcome { get; set; } = "error";

    public Measurement Measure(string phase, CancellationToken token = default, int worker = -1, bool aggregate = false) =>
        new(this, phase, token, worker, aggregate ? _aggregates.GetOrAdd((phase, worker), _ => new()) : null);

    public void Mark(string phase, string outcome = "success", int worker = -1) =>
        Log(phase, "milestone", 0, outcome, worker, 1);

    public void CancellationRequested()
    {
        if (Interlocked.Exchange(ref _cancellationRequested, 1) == 0)
            Mark("cancellation-requested", "cancelled");
    }

    public void FlushAggregates(int worker = -1)
    {
        foreach (var key in _aggregates.Keys.Where(key => key.Worker == worker))
        {
            if (_aggregates.TryRemove(key, out var value))
                Log(key.Phase, "aggregate", value.Milliseconds, value.Outcome, worker, value.Count);
        }
    }

    public void EndRequest() => Log("request", "end", Stopwatch.GetElapsedTime(_created).TotalMilliseconds, Outcome, -1, 1);

    private void Log(string phase, string milestone, double duration, string outcome, int worker, long count) =>
        logger.LogInformation("Visualization timing request={RequestId} phase={Phase} milestone={Milestone} elapsedMs={ElapsedMs:F3} durationMs={DurationMs:F3} outcome={Outcome} worker={WorkerId} count={Count}",
            RequestId, phase, milestone, Stopwatch.GetElapsedTime(_created).TotalMilliseconds, duration, outcome, worker, count);

    internal sealed class Aggregate
    {
        public long Count;
        public double Milliseconds;
        public string Outcome = "success";
    }

    // Aggregated phases are used serially within their worker (or the output loop).
    internal sealed class Measurement : IDisposable
    {
        private readonly VisualizationTiming _owner;
        private readonly string _phase;
        private readonly CancellationToken _token;
        private readonly int _worker;
        private readonly Aggregate? _aggregate;
        private readonly long _start = Stopwatch.GetTimestamp();
        private readonly bool _emit;
        private string? _outcome;

        internal Measurement(VisualizationTiming owner, string phase, CancellationToken token, int worker, Aggregate? aggregate)
        {
            _owner = owner;
            _phase = phase;
            _token = token;
            _worker = worker;
            _aggregate = aggregate;
            _emit = aggregate is null || aggregate.Count == 0;

            if (_emit)
                owner.Log(phase, "start", 0, "pending", worker, 0);
        }

        public void Complete(string outcome = "success") => _outcome = outcome;

        public void Dispose()
        {
            double duration = Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
            string outcome = _outcome ?? (_token.IsCancellationRequested ? "cancelled" : "error");

            if (_aggregate is not null)
            {
                _aggregate.Count++;
                _aggregate.Milliseconds += duration;

                if (outcome != "success")
                    _aggregate.Outcome = outcome;
            }

            if (_emit)
                _owner.Log(_phase, "end", duration, outcome, _worker, 1);
        }
    }
}
