// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.Utilities;

namespace Nexus.Services;

internal sealed class VisualizationDataset(long begin, long end, int resources)
{
    public long Begin { get; } = begin;
    public long End { get; } = end;
    public List<VisualizationSummary[][]> Levels { get; } = [];
    public long Bytes => Levels.Sum(level => level.Sum(resource => (long)resource.Length * Marshal.SizeOf<VisualizationSummary>()));

    public static long EstimateBytes(long begin, long end, int resources, long limit)
    {
        long first = begin / 256;
        long last = (end - 1) / 256;
        long bytes = 0;
        long elementBytes = resources * (long)Marshal.SizeOf<VisualizationSummary>();

        while (true)
        {
            long count = last - first + 1;

            if (count > int.MaxValue || count > (limit - bytes) / elementBytes)
                throw new System.ComponentModel.DataAnnotations.ValidationException("The visualization domain exceeds Data:Visualization:MaxDatasetBytes or the supported summary array size.");

            bytes += count * elementBytes;

            if (first == last)
                return bytes;

            first /= 4;
            last /= 4;
        }
    }

    public void AllocateBase()
    {
        int count = checked((int)((End - 1) / 256 - Begin / 256 + 1));
        Levels.Add(Enumerable.Range(0, resources).Select(_ => new VisualizationSummary[count]).ToArray());
    }

    public void BuildParents(CancellationToken cancellationToken)
    {
        long first = Begin / 256;

        while (Levels[^1][0].Length > 1)
        {
            var children = Levels[^1];
            int count = checked((int)((first + children[0].Length - 1) / 4 - first / 4 + 1));
            var parents = Enumerable.Range(0, resources).Select(_ => new VisualizationSummary[count]).ToArray();

            for (int resource = 0; resource < resources; resource++)
            {
                for (int i = 0; i < children[resource].Length; i++)
                {
                    if (i % 4096 == 0)
                        cancellationToken.ThrowIfCancellationRequested();

                    int parent = (int)((first + i) / 4 - first / 4);
                    parents[resource][parent] = VisualizationReduction.Merge(parents[resource][parent], children[resource][i]);
                }
            }

            Levels.Add(parents);
            first /= 4;
        }
    }
}

/// <summary>Process-wide admission, worker budgets and bounded, expiring summary storage.</summary>
internal sealed class VisualizationCache : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _memory = [];
    private readonly Dictionary<string, BuildInterest> _interests = [];
    private long _bytes;
    private long _access;
    private readonly ConditionalWeakTable<CatalogState, CatalogEpoch> _catalogEpochs = new();
    public VisualizationOptions Options { get; }
    public SemaphoreSlim Reads { get; }
    public SemaphoreSlim Compute { get; }
    public SemaphoreSlim Requests { get; }
    public SemaphoreSlim[] Builds { get; } = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1)).ToArray();
    public string Epoch { get; } = Guid.NewGuid().ToString("N");
    public string GetCatalogEpoch(CatalogState state) => _catalogEpochs.GetValue(state, _ => new()).Value;

    private sealed class Entry(VisualizationDataset dataset, DateTime expires)
    {
        public VisualizationDataset Dataset { get; } = dataset;
        public DateTime Expires { get; } = expires;
        public long LastAccess;
        public int Pins;
    }

    internal sealed class Lease(VisualizationDataset dataset, Action release) : IDisposable
    {
        private Action? _release = release;
        public VisualizationDataset Dataset { get; } = dataset;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }

    // Keep the owner's scope alive until its scan drains, but cancel that scan only
    // when no registered consumer needs it. Unrelated hash-stripe waiters do not count.
    internal sealed class BuildInterest
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public int Consumers;
        public int Scopes;
        public VisualizationDataset? Dataset;
    }

    public (BuildInterest Interest, IDisposable Registration) JoinBuild(string key, CancellationToken token)
    {
        BuildInterest interest;

        lock (_gate)
        {
            if (!_interests.TryGetValue(key, out interest!))
                _interests[key] = interest = new();

            interest.Consumers++;
            interest.Scopes++;
        }

        var lease = new Lease(null!, () =>
        {
            lock (_gate)
            {
                if (--interest.Consumers != 0)
                    return;

                _interests.Remove(key);
            }

            interest.Cancellation.Cancel();
        });
        var registration = token.Register(lease.Dispose);
        return (interest, new Lease(null!, () =>
        {
            registration.Dispose();
            lease.Dispose();

            lock (_gate)
            {
                if (--interest.Scopes == 0)
                    interest.Cancellation.Dispose();
            }
        }));
    }

    private sealed class CatalogEpoch
    {
        public string Value { get; } = Guid.NewGuid().ToString("N");
    }

    public VisualizationCache(IOptions<DataOptions> options)
    {
        Options = options.Value.Visualization;

        if (Options.MaxConcurrentReads < 1 ||
            Options.MaxComputeWorkers < 1 || Options.MaxComputeWorkers > 64 ||
            Options.MaxConcurrentRequests < 1 || Options.MaxConcurrentRequests > 64 ||
            Options.TargetReadBytes < 65536 || Options.TargetReadBytes > 64 * 1024 * 1024 ||
            Options.MaxDatasetBytes < 65536 || Options.MemoryLimitBytes < 0 ||
            Options.CacheTtl <= TimeSpan.Zero || Options.CacheTtl > TimeSpan.FromDays(365) ||
            Options.MaxAggregatePoints < 5)
            throw new InvalidOperationException("Invalid Data:Visualization limits.");

        Reads = new(Options.MaxConcurrentReads);
        Compute = new(Options.MaxComputeWorkers);
        Requests = new(Options.MaxConcurrentRequests);
    }

    public Lease? Get(string key)
    {
        lock (_gate)
        {
            Prune();

            if (_memory.TryGetValue(key, out var entry) && entry.Expires > DateTime.UtcNow)
                return Pin(entry);

            return null;
        }
    }

    public Lease Put(string key, VisualizationDataset dataset)
    {
        lock (_gate)
        {
            Prune();
            return Remember(key, dataset, DateTime.UtcNow + Options.CacheTtl);
        }
    }

    private Lease Pin(Entry entry)
    {
        entry.LastAccess = ++_access;
        entry.Pins++;
        return new Lease(entry.Dataset, () => { lock (_gate) { entry.Pins--; } });
    }

    internal long ResidentBytes { get { lock (_gate) { return _bytes; } } }

    private Lease Remember(string key, VisualizationDataset dataset, DateTime expires)
    {
        if (dataset.Bytes > Options.MemoryLimitBytes || _memory.ContainsKey(key))
            return new Lease(dataset, () => { });

        while (dataset.Bytes > Options.MemoryLimitBytes - _bytes && _memory.Count > 0)
        {
            string? oldest = _memory.Where(pair => pair.Value.Pins == 0).OrderBy(pair => pair.Value.LastAccess).Select(pair => pair.Key).FirstOrDefault();

            if (oldest is null)
                return new Lease(dataset, () => { });
            _bytes -= _memory[oldest].Dataset.Bytes;
            _memory.Remove(oldest);
        }

        var entry = new Entry(dataset, expires);
        _memory[key] = entry;
        _bytes += dataset.Bytes;
        return Pin(entry);
    }

    private void Prune()
    {
        var now = DateTime.UtcNow;

        foreach (var key in _memory.Where(pair => pair.Value.Pins == 0 && pair.Value.Expires <= now).Select(pair => pair.Key).ToArray())
        {
            _bytes -= _memory[key].Dataset.Bytes;
            _memory.Remove(key);
        }
    }

    public void Dispose()
    {
        Reads.Dispose();
        Compute.Dispose();
        Requests.Dispose();

        foreach (var gate in Builds)
            gate.Dispose();
    }
}
