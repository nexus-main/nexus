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
using System.Runtime.InteropServices;
using System.Text.Json;
using Xunit;

namespace Services;

public class DataControllerServiceTests
{
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
        // Arrange
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

        // Act
        var captured = service.CaptureRequestConfiguration();

        context.Request.Headers[DataControllerService.NexusConfigurationHeaderKey] = "not-base64";

        using var worker1 = await service.GetVisualizationDataSourceControllerAsync(pipeline, captured, [], CancellationToken.None);
        using var worker2 = await service.GetVisualizationDataSourceControllerAsync(pipeline, captured, [], CancellationToken.None);

        // Assert
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
    public async Task FailedVisualizationConstructionDisposesEveryCreatedSourceEvenIfDisposeThrows(bool initialization)
    {
        // Arrange
        DisposalSource.Disposals = 0;
        var hive = new Mock<IExtensionHive<IDataSource>>();

        hive.Setup(current => current.GetExtensionType("source")).Returns(typeof(DisposalSource));
        hive.Setup(current => current.GetExtensionType("failure")).Throws(new InvalidOperationException("construction failed"));

        var service = new DataControllerService(
            new AppState(),
            default!,
            hive.Object,
            default!,
            default!,
            default!,
            Options.Create(new DataOptions()), NullLoggerFactory.Instance
        );

        var types = initialization ? new[] { "source", "source" } : ["source", "source", "failure"];

        var pipeline = new DataSourcePipeline(types
            .Select(type => new DataSourceRegistration(type, null, JsonSerializer.SerializeToElement<object?>(null)))
            .ToArray()
        );

        // Act
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetVisualizationDataSourceControllerAsync(pipeline, null, [], CancellationToken.None));

        // Assert
        Assert.Equal(initialization ? "initialization failed" : "construction failed", error.Message);
        Assert.Equal(2, DisposalSource.Disposals);
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

        var dataControllerService = new DataControllerService(
            appState,
            default!,
            default!,
            writersExtensionHive,
            default!,
            default!,
            Options.Create(new DataOptions()),
            loggerFactory
        );

        // Act
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

        public override async Task ReadAsync(
            DateTime begin,
            DateTime end,
            ReadRequest[] requests,
            ReadDataHandler readData,
            IProgress<double> progress,
            CancellationToken cancellationToken
        )
        {
            await BeforeRead();

            foreach (var request in requests)
            {
                MemoryMarshal.Cast<byte, double>(request.Data.Span).Fill(7);
                request.Status.Span.Fill(1);
            }
        }
    }
}
