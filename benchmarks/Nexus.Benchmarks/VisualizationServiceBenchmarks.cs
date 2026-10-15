// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text.Json;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Nexus.Core;
using Nexus.Core.V1;
using Nexus.Core.V2;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;

namespace Nexus.Benchmarks;

// Run: dotnet run -c Release --project benchmarks/Nexus.Benchmarks -- --filter '*VisualizationServiceBenchmarks*'
// Measures synthetic visualization latency: service orchestration, strict
// DataSourceController reads, Float32 normalization, pyramid construction, projection,
// Arrow output and decoding/validation. Source plugins, catalog/controller lookup,
// license/cache services and the memory tracker are mocked; no HTTP or source I/O.
[MemoryDiagnoser]
public class VisualizationServiceBenchmarks
{
    private const int Count = 4 * 1024 * 1024;
    private static readonly DateTime Begin = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private SyntheticVisualizationFixture? _fixture;
    private VisualizationRequest? _request;

    [Params(0, 2)]
    public int DelayMilliseconds { get; set; }

    [Params(1, 2, 4, 8)]
    public int ReadWorkers { get; set; }

    [Params(1, 2, 4)]
    public int ComputeWorkers { get; set; }

    [GlobalSetup]
    public async Task WarmupAsync()
    {
        using var warmup = new SyntheticVisualizationFixture(readWorkers: 1, computeWorkers: 1)
        {
            Delay = TimeSpan.Zero
        };
        var request = warmup.Request([warmup.View("main", 0, 65536, 100)]);
        await warmup.WriteAsync(request);
    }

    [IterationSetup]
    public void Setup()
    {
        _fixture = new SyntheticVisualizationFixture(ReadWorkers, ComputeWorkers)
        {
            Delay = TimeSpan.FromMilliseconds(DelayMilliseconds)
        };
        _request = _fixture.Request([_fixture.View("main", 0, Count, 1000)]) with
        {
            End = Begin.AddSeconds(Count)
        };
    }

    [IterationCleanup]
    public void Cleanup()
    {
        _fixture?.Dispose();
        _fixture = null;
        _request = null;
    }

    [Benchmark]
    public Task<long> SyntheticSourceThroughput() => _fixture!.WriteAsync(_request!);

    private sealed class SyntheticVisualizationFixture : IDisposable
    {
        public DateTime Begin { get; } = VisualizationServiceBenchmarks.Begin;
        public TimeSpan Delay = TimeSpan.FromMilliseconds(5);

        private readonly Mock<IDataControllerService> _controllers = new();
        private readonly Mock<IMemoryTracker> _memoryTracker = new();
        private readonly VisualizationCache _cache;
        private readonly VisualizationService _service;
        private long _samples;
        private int _instances;
        private int _disposed;
        private int _active;

        public SyntheticVisualizationFixture(int readWorkers, int computeWorkers)
        {
            var options = new DataOptions
            {
                Visualization = new VisualizationOptions
                {
                    MaxConcurrentReads = readWorkers,
                    MaxComputeWorkers = computeWorkers,
                    MaxTotalCacheBytes = 16 * 1024 * 1024
                }
            };
            _cache = new VisualizationCache(Options.Create(options));
            _memoryTracker.Setup(current => current.RegisterAllocationAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync<long, long, CancellationToken, IMemoryTracker, AllocationRegistration>((minimum, maximum, _) =>
                    new AllocationRegistration(_memoryTracker.Object, Math.Max(minimum, Math.Min(1024 * 1024, maximum))));

            var pipeline = new DataSourcePipeline([new DataSourceRegistration("test", null, JsonSerializer.SerializeToElement<object?>(null))]);
            var representation = new Representation(NexusDataType.Float32, TimeSpan.FromSeconds(1));
            // The mocked catalog lookup bypasses DataSourceController's metadata sanitization.
            var pipelineProperties = new Dictionary<string, int> { ["pipeline-position"] = 0 };
            var catalog = new ResourceCatalogBuilder("/A/B/C")
                .AddResource(new ResourceBuilder("T1").WithOriginalName("T1").WithProperty("nexus", pipelineProperties).AddRepresentation(representation).Build())
                .AddResource(new ResourceBuilder("T2").WithOriginalName("T2").WithProperty("nexus", pipelineProperties).AddRepresentation(representation).Build())
                .Build();
            var lookup = new Mock<IDataSourceController>();
            lookup.Setup(current => current.GetCatalogAsync(catalog.Id, It.IsAny<CancellationToken>())).ReturnsAsync(catalog);
            _controllers.Setup(current => current.GetDataSourceControllerAsync(pipeline, It.IsAny<CancellationToken>())).ReturnsAsync(lookup.Object);
            _controllers.Setup(current => current.GetVisualizationDataSourceControllerAsync(pipeline,
                    It.IsAny<IReadOnlyDictionary<string, JsonElement>?>(), It.IsAny<ResourceCatalog[]>(), It.IsAny<CancellationToken>()))
                .Returns<DataSourcePipeline, IReadOnlyDictionary<string, JsonElement>?, ResourceCatalog[], CancellationToken>(
                    async (_, configuration, catalogs, token) =>
                    {
                        Interlocked.Increment(ref _instances);
                        int reading = 0;
                        var source = new Mock<IDataSource<object?>>();
                        source.As<IDisposable>().Setup(current => current.Dispose()).Callback(() => Interlocked.Increment(ref _disposed));
                        source.Setup(current => current.ReadAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<ReadRequest[]>(),
                                It.IsAny<ReadDataHandler>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
                            .Returns<DateTime, DateTime, ReadRequest[], ReadDataHandler, IProgress<double>, CancellationToken>(async (from, to, requests, _, _, ct) =>
                            {
                                if (Interlocked.Increment(ref reading) != 1)
                                    throw new InvalidOperationException("Concurrent reads on one source instance.");
                                Interlocked.Increment(ref _active);
                                try
                                {
                                    await Task.Delay(Delay, ct);

                                    foreach (var request in requests)
                                    {
                                        var values = MemoryMarshal.Cast<byte, float>(request.Data.Span);
                                        if (values.Length != (to - from).Ticks / TimeSpan.TicksPerSecond)
                                            throw new InvalidOperationException("Unexpected source buffer size.");
                                        Interlocked.Add(ref _samples, values.Length);

                                        for (int i = 0; i < values.Length; i++)
                                        {
                                            long index = (long)(from - Begin).TotalSeconds + i;
                                            values[i] = index is >= 500 and < 510 ? float.PositiveInfinity : index % 1000;
                                        }

                                        request.Status.Span.Fill(1);
                                    }
                                }
                                finally
                                {
                                    Interlocked.Decrement(ref _active);
                                    Interlocked.Decrement(ref reading);
                                }
                            });
                        var controller = new DataSourceController([source.Object], pipeline.Registrations, configuration,
                            new ProcessingService(Options.Create(options)), Mock.Of<ICacheService>(), options,
                            NullLogger<DataSourceController>.Instance, strictReads: true);
                        await controller.InitializeAsync(new ConcurrentDictionary<string, ResourceCatalog>(catalogs.Select(current => KeyValuePair.Create(current.Id, current))), NullLoggerFactory.Instance, token);
                        return controller;
                    });
            var manager = new Mock<ICatalogManager>();
            manager.Setup(current => current.GetCatalogContainersAsync(It.IsAny<CatalogContainer>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new CatalogContainer(new CatalogRegistration(catalog.Id, ""), Guid.Empty, pipeline, [],
                    new CatalogMetadata(null, null, null), manager.Object, default!, _controllers.Object)]);
            var state = new AppState { CatalogState = new CatalogState(CatalogContainer.CreateRoot(manager.Object, default!), new CatalogCache()) };
            var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, nameof(NexusRoles.Administrator))], "test"));
            _service = new VisualizationService(state, user, _controllers.Object,
                Mock.Of<IAcceptedLicenseService>(), _cache, _memoryTracker.Object, NullLogger<VisualizationService>.Instance);
        }

        public VisualizationView View(string id, int begin, int end, int points) => new(id, Begin.AddSeconds(begin), Begin.AddSeconds(end), points);

        public VisualizationRequest Request(VisualizationView[] views) =>
            new(Begin, Begin.AddSeconds(65536), ["/A/B/C/T1/1_s", "/A/B/C/T2/1_s"], views);

        public async Task<long> WriteAsync(VisualizationRequest request)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var write = await _service.PrepareAsync(request, timeout.Token);
            using var stream = new MemoryStream();
            await write(stream, timeout.Token);
            stream.Position = 0;
            using var reader = new ArrowStreamReader(stream, leaveOpen: true);
            var completed = new HashSet<(int Resource, int View)>();
            bool terminal = false;
            RecordBatch batch;

            while ((batch = await reader.ReadNextRecordBatchAsync(timeout.Token)) is not null)
            {
                using (batch)
                {
                    for (int row = 0; row < batch.Length; row++)
                    {
                        int kind = ((Int32Array)batch.Column(0)).GetValue(row)!.Value;
                        if (terminal || kind is < 0 or > 2)
                            throw new InvalidOperationException("Visualization failed or emitted records after completion.");
                        if (kind == 2)
                            terminal = true;
                        if (kind != 0)
                            continue;

                        int resource = ((Int32Array)batch.Column(1)).GetValue(row)!.Value;
                        int view = ((Int32Array)batch.Column(2)).GetValue(row)!.Value;
                        if (resource < 0 || resource >= request.ResourcePaths.Length || view < 0 || view >= request.Views.Length ||
                            ((StringArray)batch.Column(9)).GetString(row) != "complete" || !completed.Add((resource, view)))
                            throw new InvalidOperationException("Missing, duplicate or invalid completed view.");
                        var values = (FloatArray)((ListArray)batch.Column(5)).GetSlicedValues(row);
                        var indices = (Int64Array)((ListArray)batch.Column(4)).GetSlicedValues(row);
                        if (values.Length == 0 || values.Length > request.Views[view].MaxPoints || indices.Length != values.Length ||
                            ((FloatArray)batch.Column(7)).GetValue(row) != 0 || ((FloatArray)batch.Column(8)).GetValue(row) != 999)
                            throw new InvalidOperationException("Incorrect visualization points or domain extrema.");
                    }
                }
            }

            long expectedSamples = (request.End - request.Begin).Ticks / TimeSpan.TicksPerSecond * request.ResourcePaths.Length;
            if (!terminal || completed.Count != request.ResourcePaths.Length * request.Views.Length ||
                _samples != expectedSamples || _instances == 0 || _instances != _disposed || _active != 0)
                throw new InvalidOperationException($"Incomplete benchmark workload: samples={_samples}/{expectedSamples}, sources={_disposed}/{_instances}, active={_active}.");
            return stream.Length;
        }

        public void Dispose() => _cache.Dispose();
    }
}
