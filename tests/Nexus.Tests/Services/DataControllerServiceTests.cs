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

        // Visualization workers use the captured request, not the ambient header,
        // and each factory call creates a separately initialized pipeline.
        var captured = dataControllerService.CaptureRequestConfiguration();
        httpContext.Request.Headers.Remove(DataControllerService.NexusConfigurationHeaderKey);
        using var worker1 = await dataControllerService.GetVisualizationDataSourceControllerAsync(pipeline, captured, [actualCatalog], CancellationToken.None);
        using var worker2 = await dataControllerService.GetVisualizationDataSourceControllerAsync(pipeline, captured, [actualCatalog], CancellationToken.None);
        Assert.NotSame(worker1, worker2);
        Assert.Equal(expectedConfig, JsonSerializer.Serialize(((DataSourceController)worker1)._requestConfiguration));
        Assert.Equal(expectedConfig, JsonSerializer.Serialize(((DataSourceController)worker2)._requestConfiguration));
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
