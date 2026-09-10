// MIT License
// Copyright (c) [2024] [nexus-main]

using Apollo3zehn.PackageManagement.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Nexus.Core;
using Nexus.Core.V2;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using Nexus.Sources;
using System.Diagnostics;
using System.Security.Claims;

// ============================================================================
// Setup
// ============================================================================

var tempRoot = Path.Combine(Path.GetTempPath(), $"nexus-profiling-{Guid.NewGuid():N}");

var pathsOptions = new PathsOptions
{
    Config = Path.Combine(tempRoot, "config"),
    Cache = Path.Combine(tempRoot, "cache"),
    Catalogs = Path.Combine(tempRoot, "catalogs"),
    Artifacts = Path.Combine(tempRoot, "artifacts"),
    Packages = Path.Combine(tempRoot, "packages")
};

foreach (var prop in typeof(PathsOptions).GetProperties())
{
    if (prop.PropertyType == typeof(string) && prop.GetValue(pathsOptions) is string path)
        Directory.CreateDirectory(path);
}

var databaseService = new DatabaseService(Options.Create(pathsOptions));
var pipelineService = new PipelineService(databaseService);
var processingService = new ProcessingService(Options.Create(new DataOptions()));
var cacheService = new CacheService(databaseService);

var sourcesExtensionHive = Mock.Of<IExtensionHive<IDataSource>>();

Mock.Get(sourcesExtensionHive)
    .Setup(h => h.GetExtensionType(It.IsAny<string>()))
    .Returns(typeof(Sample));

var writersExtensionHive = Mock.Of<IExtensionHive<IDataWriter>>();

var httpContextAccessor = Mock.Of<IHttpContextAccessor>();

Mock.Get(httpContextAccessor)
    .SetupGet(a => a.HttpContext)
    .Returns((HttpContext?)null);

var serviceCollection = new ServiceCollection();
serviceCollection.AddScoped<IDBService>(_ => Mock.Of<IDBService>());
var serviceProvider = serviceCollection.BuildServiceProvider();

var appState = new AppState();

var dataControllerService = new DataControllerService(
    appState,
    httpContextAccessor,
    sourcesExtensionHive,
    writersExtensionHive,
    processingService,
    cacheService,
    Options.Create(new DataOptions()),
    NullLoggerFactory.Instance);

var catalogManager = new CatalogManager(
    dataControllerService,
    databaseService,
    serviceProvider,
    sourcesExtensionHive,
    pipelineService,
    Options.Create(new SecurityOptions()),
    NullLogger<CatalogManager>.Instance);

appState.CatalogState = new CatalogState(
    CatalogContainer.CreateRoot(catalogManager, databaseService),
    new CatalogCache());

var memoryTracker = Mock.Of<IMemoryTracker>();

Mock.Get(memoryTracker)
    .Setup(t => t.RegisterAllocationAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync<long, long, CancellationToken, IMemoryTracker, AllocationRegistration>(
        (_, maximum, _) => new AllocationRegistration(memoryTracker, maximum));

var user = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "profiling"));

var dataService = new DataService(
    appState,
    user,
    dataControllerService,
    databaseService,
    memoryTracker,
    NullLogger<DataService>.Instance,
    NullLoggerFactory.Instance);

// ============================================================================
// Profiling
// ============================================================================

var resourcePaths = new[] { "/SAMPLE/LOCAL/T1/1_s", "/SAMPLE/LOCAL/V1/1_s" };
var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);

var profileMode = args.Contains("--profile");
var largeDuration = TimeSpan.FromDays(30);
var largeRequest = new BatchStreamRequest(begin, begin + largeDuration, resourcePaths, Precision.Float32);

Console.WriteLine("Warming up...");

await using (var warmupStream = await dataService.ReadBatchAsStreamAsync(
    new BatchStreamRequest(begin, begin + TimeSpan.FromSeconds(10), resourcePaths, Precision.Float32),
    CancellationToken.None))
{
    var sink = new MemoryStream();
    await warmupStream.CopyToAsync(sink);
}

if (profileMode)
{
    Console.WriteLine("Profile mode: Large (1 month) x 200 runs, Stream.Null sink");
    Console.WriteLine();

    var sw = Stopwatch.StartNew();

    for (int i = 0; i < 200; i++)
    {
        await using var stream = await dataService.ReadBatchAsStreamAsync(largeRequest, CancellationToken.None);
        await stream.CopyToAsync(Stream.Null);

        if ((i + 1) % 10 == 0)
            Console.WriteLine($"  Run {i + 1}/200 ({sw.Elapsed.TotalSeconds:F1}s elapsed)");
    }

    sw.Stop();
    Console.WriteLine();
    Console.WriteLine($"Done: 200 runs in {sw.Elapsed.TotalSeconds:F1}s");
}

else
{
    Console.WriteLine();

    var variants = new (string Name, TimeSpan Duration)[]
    {
        ("Small  (1 day)  ", TimeSpan.FromDays(1)),
        ("Medium (1 week) ", TimeSpan.FromDays(7)),
        ("Large  (1 month)", TimeSpan.FromDays(30)),
    };

    foreach (var (name, duration) in variants)
    {
        var end = begin + duration;
        var request = new BatchStreamRequest(begin, end, resourcePaths, Precision.Float32);

        await using (var warmupVariantStream = await dataService.ReadBatchAsStreamAsync(request, CancellationToken.None))
        {
            var sink = new MemoryStream();
            await warmupVariantStream.CopyToAsync(sink);
        }

        const int runs = 5;
        var elapsedMs = new double[runs];
        long bytes = 0;

        for (int i = 0; i < runs; i++)
        {
            var sw = Stopwatch.StartNew();

            await using var stream = await dataService.ReadBatchAsStreamAsync(request, CancellationToken.None);
            var sink = new MemoryStream();
            await stream.CopyToAsync(sink);

            sw.Stop();
            elapsedMs[i] = sw.Elapsed.TotalMilliseconds;
            bytes = sink.Length;
        }

        var avgMs = elapsedMs.Average();
        var minMs = elapsedMs.Min();
        var maxMs = elapsedMs.Max();

        Console.WriteLine($"{name}: {avgMs:F1} ms avg (min {minMs:F1}, max {maxMs:F1}) — {bytes:N0} bytes, {runs} runs");
    }
}

// ============================================================================
// Cleanup
// ============================================================================

serviceProvider.Dispose();

if (Directory.Exists(tempRoot))
    Directory.Delete(tempRoot, recursive: true);
