// MIT License
// Copyright (c) [2024] [nexus-main]

using Apollo3zehn.PackageManagement.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Nexus.Core;
using Nexus.Core.V1;
using Nexus.DataModel;
using ExportParameters = Nexus.Core.V2.ExportParameters;
using Nexus.Extensibility;
using Nexus.Services;
using Nexus.Sources;
using Nexus.Writers;
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Text.Json;
using Xunit;

namespace Services;

public class DataControllerServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedVisualizationConstructionDisposesEveryCreatedSourceEvenIfDisposeThrows(bool initialization)
    {
        DisposalSource.Disposals = 0;
        var hive = new Mock<IExtensionHive<IDataSource>>();
        hive.Setup(current => current.GetExtensionType("source")).Returns(typeof(DisposalSource));
        hive.Setup(current => current.GetExtensionType("failure")).Throws(new InvalidOperationException("construction failed"));
        var service = new DataControllerService(new AppState(), default!, hive.Object, default!, default!, default!,
            Options.Create(new DataOptions()), NullLoggerFactory.Instance);
        var types = initialization ? new[] { "source", "source" } : new[] { "source", "source", "failure" };
        var pipeline = new DataSourcePipeline(types.Select(type =>
            new DataSourceRegistration(type, null, JsonSerializer.SerializeToElement<object?>(null))).ToArray());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetVisualizationDataSourceControllerAsync(pipeline, null, [], CancellationToken.None));
        Assert.Equal(initialization ? "initialization failed" : "construction failed", error.Message);
        Assert.Equal(2, DisposalSource.Disposals);
    }

    public sealed class DisposalSource : SimpleDataSource<object?>, IDataSource<object?>, IDisposable
    {
        public static int Disposals;
        Task IDataSource<object?>.SetContextAsync(DataSourceContext<object?> context, ILogger logger, CancellationToken cancellationToken)
            => Task.FromException(new InvalidOperationException("initialization failed"));
        public void Dispose()
        {
            Interlocked.Increment(ref Disposals);
            throw new InvalidOperationException("dispose failed");
        }

        public override Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(string path, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public override Task<ResourceCatalog> EnrichCatalogAsync(ResourceCatalog catalog, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public override Task ReadAsync(DateTime begin, DateTime end, ReadRequest[] requests, ReadDataHandler readData,
            IProgress<double> progress, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    [Fact]
    public async Task CanCreateAndInitializeDataSourceController()
    {
        // Arrange
        var sourcesExtensionHive = Mock.Of<IExtensionHive<IDataSource>>();

        Mock.Get(sourcesExtensionHive)
            .Setup(extensionHive => extensionHive.GetExtensionType(It.IsAny<string>()))
            .Returns(typeof(Sample));

        var registration = new DataSourceRegistration(
            Type: default!,
            new Uri("A", UriKind.Relative),
            Configuration: JsonSerializer.SerializeToElement<object?>(null)
        );

        var pipeline = new DataSourcePipeline([registration]);

        var expectedCatalog = Sample.LoadCatalog("/A/B/C");

        var catalogState = new CatalogState(
            Root: default!,
            Cache: new CatalogCache()
        );

        var appState = new AppState()
        {
            CatalogState = catalogState
        };

        var requestConfiguration = new Dictionary<string, string>
        {
            ["foo"] = "bar",
            ["foo2"] = "baz",
        };

        var encodedRequestConfiguration = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(requestConfiguration));

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Append(DataControllerService.NexusConfigurationHeaderKey, encodedRequestConfiguration);

        var httpContextAccessor = Mock.Of<IHttpContextAccessor>();

        Mock.Get(httpContextAccessor)
            .SetupGet(httpContextAccessor => httpContextAccessor.HttpContext)
            .Returns(httpContext);

        var loggerFactory = Mock.Of<ILoggerFactory>();

        Mock.Get(loggerFactory)
            .Setup(loggerFactory => loggerFactory.CreateLogger(It.IsAny<string>()))
            .Returns(NullLogger.Instance);

        var dataControllerService = new DataControllerService(
            appState,
            httpContextAccessor,
            sourcesExtensionHive,
            default!,
            default!,
            default!,
            Options.Create(new DataOptions()),
            loggerFactory
        );

        // Act
        var actual = await dataControllerService.GetDataSourceControllerAsync(pipeline, CancellationToken.None);

        // Assert
        var actualCatalog = await actual.GetCatalogAsync("/A/B/C", CancellationToken.None);

        Assert.Equal(expectedCatalog.Id, actualCatalog.Id);

        var expectedConfig = JsonSerializer.Serialize(requestConfiguration);
        var actualConfig = JsonSerializer.Serialize(((DataSourceController)actual)._requestConfiguration);

        Assert.Equal(expectedConfig, actualConfig);
    }

    [Fact]
    public async Task VisualizationFactoryInitializesFreshPluginsWithCapturedConfiguration()
    {
        VisualizationSource.Instances.Clear();
        var hive = new Mock<IExtensionHive<IDataSource>>();
        hive.Setup(current => current.GetExtensionType("source")).Returns(typeof(VisualizationSource));
        var context = new DefaultHttpContext();
        var requestConfiguration = new Dictionary<string, string> { ["request"] = "captured" };
        context.Request.Headers[DataControllerService.NexusConfigurationHeaderKey] =
            Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(requestConfiguration));
        var service = new DataControllerService(new AppState(), Mock.Of<IHttpContextAccessor>(accessor => accessor.HttpContext == context),
            hive.Object, default!, default!, default!, Options.Create(new DataOptions()), NullLoggerFactory.Instance);
        var locator = new Uri("source", UriKind.Relative);
        var pipeline = new DataSourcePipeline([new DataSourceRegistration("source", locator,
            JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["source"] = "registered" }))]);

        var captured = service.CaptureRequestConfiguration();
        context.Request.Headers[DataControllerService.NexusConfigurationHeaderKey] = "not-base64";
        using var worker1 = await service.GetVisualizationDataSourceControllerAsync(pipeline, captured, [], CancellationToken.None);
        using var worker2 = await service.GetVisualizationDataSourceControllerAsync(pipeline, captured, [], CancellationToken.None);

        Assert.Equal(2, VisualizationSource.Instances.Count);
        var source1 = VisualizationSource.Instances[0];
        var source2 = VisualizationSource.Instances[1];
        Assert.NotSame(source1, source2);
        Assert.NotSame(source1.InitializedContext, source2.InitializedContext);
        Assert.NotSame(source1.InitializedContext.SourceConfiguration, source2.InitializedContext.SourceConfiguration);
        Assert.All(VisualizationSource.Instances, source =>
        {
            Assert.Equal(locator, source.InitializedContext.ResourceLocator);
            Assert.Equal("registered", source.InitializedContext.SourceConfiguration!["source"]);
            Assert.Equal("captured", source.InitializedContext.RequestConfiguration!["request"].GetString());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VisualizationFactoryEnablesSerialUncachedReadsAndPropagatesFailures(bool fail)
    {
        VisualizationSource.Instances.Clear();
        var hive = new Mock<IExtensionHive<IDataSource>>();
        hive.Setup(current => current.GetExtensionType("source")).Returns(typeof(VisualizationSource));
        var cache = new Mock<ICacheService>(MockBehavior.Strict);
        var options = Options.Create(new DataOptions { CachePattern = ".*" });
        var service = new DataControllerService(new AppState(), default!, hive.Object, default!,
            new ProcessingService(options), cache.Object, options, NullLoggerFactory.Instance);
        var pipeline = new DataSourcePipeline([new DataSourceRegistration("source", null,
            JsonSerializer.SerializeToElement<object?>(null))]);
        var samplePeriod = TimeSpan.FromSeconds(2);
        var original = new Representation(NexusDataType.Float64, samplePeriod);
        var baseRepresentation = new Representation(NexusDataType.Float64, TimeSpan.FromSeconds(1));
        var aggregate = new Representation(NexusDataType.Float64, samplePeriod, parameters: null, RepresentationKind.Mean);
        var catalog = new ResourceCatalogBuilder("/TEST")
            .AddResource(new ResourceBuilder("original").AddRepresentation(original).Build())
            .AddResource(new ResourceBuilder("derived1").AddRepresentation(baseRepresentation).Build())
            .AddResource(new ResourceBuilder("derived2").AddRepresentation(baseRepresentation).Build())
            .Build()
            .EnsureAndSanitizeMandatoryProperties(pipelinePosition: 0, dataSources: Array.Empty<IDataSource>());
        using var worker = await service.GetVisualizationDataSourceControllerAsync(pipeline, null, [catalog], CancellationToken.None);
        var source = Assert.Single(VisualizationSource.Instances);
        var entered = Enumerable.Range(0, 3).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var release = Enumerable.Range(0, 3).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var failure = new InvalidOperationException("source read failed");
        int calls = 0;
        source.BeforeRead = async () =>
        {
            int index = Interlocked.Increment(ref calls) - 1;
            if (index > 0)
                Assert.True(release[index - 1].Task.IsCompleted, "Source reads overlapped.");
            entered[index].SetResult();
            await release[index].Task;
            if (fail && index == 2)
                throw failure;
        };
        var pipes = Enumerable.Range(0, 3).Select(_ => new Pipe()).ToArray();
        var requests = catalog.Resources!.Select((resource, index) =>
        {
            var baseItem = new CatalogItem(catalog, resource, index == 0 ? original : baseRepresentation, Parameters: null);
            var request = index == 0
                ? new CatalogItemRequest(baseItem, null, null!)
                : new CatalogItemRequest(baseItem with { Representation = aggregate }, baseItem, null!);
            return new CatalogItemRequestPipeWriter(request, pipes[index].Writer);
        }).ToArray();
        var begin = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var reading = worker.ReadAsync(begin, begin.AddSeconds(4), samplePeriod, Precision.Float64,
            requests, default!, new Progress<double>(), CancellationToken.None);

        try
        {
            // Each source call stays pending until released, exposing overlap without sleeps.
            for (int i = 0; i < entered.Length; i++)
            {
                await entered[i].Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(i + 1, Volatile.Read(ref calls));
                Assert.False(reading.IsCompleted);
                release[i].SetResult();
            }

            if (fail)
                Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => reading.WaitAsync(TimeSpan.FromSeconds(5))));
            else
                await reading.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(3, calls);
            cache.VerifyNoOtherCalls();
            foreach (var pipe in pipes.Take(fail ? 2 : 3))
            {
                Assert.True(pipe.Reader.TryRead(out var result));
                Assert.Equal(new double[] { 7, 7 }, MemoryMarshal.Cast<byte, double>(result.Buffer.ToArray()).ToArray());
                pipe.Reader.AdvanceTo(result.Buffer.End);
            }
        }
        finally
        {
            foreach (var gate in release)
                gate.TrySetResult();
            await Record.ExceptionAsync(() => reading.WaitAsync(TimeSpan.FromSeconds(5)));
            foreach (var pipe in pipes)
            {
                await pipe.Writer.CompleteAsync();
                await pipe.Reader.CompleteAsync();
            }
        }
    }

    public sealed class VisualizationSource : SimpleDataSource<Dictionary<string, string>?>
    {
        public static List<VisualizationSource> Instances { get; } = [];
        public VisualizationSource() => Instances.Add(this);
        public DataSourceContext<Dictionary<string, string>?> InitializedContext => Context;
        public Func<Task> BeforeRead { get; set; } = () => Task.CompletedTask;

        public override Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(string path, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public override Task<ResourceCatalog> EnrichCatalogAsync(ResourceCatalog catalog, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public override async Task ReadAsync(DateTime begin, DateTime end, ReadRequest[] requests, ReadDataHandler readData,
            IProgress<double> progress, CancellationToken cancellationToken)
        {
            await BeforeRead();
            foreach (var request in requests)
            {
                MemoryMarshal.Cast<byte, double>(request.Data.Span).Fill(7);
                request.Status.Span.Fill(1);
            }
        }
    }

    [Fact]
    public async Task CanCreateAndInitializeDataWriterController()
    {
        // Arrange
        var appState = new AppState();
        var writersExtensionHive = Mock.Of<IExtensionHive<IDataWriter>>();

        Mock.Get(writersExtensionHive)
            .Setup(extensionHive => extensionHive.GetExtensionType(It.IsAny<string>()))
            .Returns(typeof(Csv));

        var loggerFactory = Mock.Of<ILoggerFactory>();
        var resourceLocator = new Uri("A", UriKind.Relative);
        var exportParameters = new ExportParameters(default, default, default, "dummy", default!, default, Precision.Float32);

        // Act
        var dataControllerService = new DataControllerService(
            appState,
            default!,
            default!,
            writersExtensionHive,
            default!,
            default!,
            Options.Create(new DataOptions()),
            loggerFactory);

        async Task action()
        {
            var _ = await dataControllerService.GetDataWriterControllerAsync(
                resourceLocator,
                exportParameters,
                CancellationToken.None);
        }
        ;

        var actual = await Record.ExceptionAsync(action);

        // Assert
        Assert.Null(actual);
    }
}
