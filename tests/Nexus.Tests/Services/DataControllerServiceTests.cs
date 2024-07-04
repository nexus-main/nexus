// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.Services;
using Nexus.Sources;
using Nexus.Writers;
using System.IO.Pipelines;
using System.Text.Json;
using Xunit;

namespace Services;

public class DataControllerServiceTests
{
    [Fact]
    public async Task CanCreateAndInitializeDataSourceController()
    {
        // Arrange
        var extensionHive = Mock.Of<IExtensionHive>();

        Mock.Get(extensionHive)
            .Setup(extensionHive => extensionHive.GetInstance<IDataSource>(It.IsAny<string>()))
            .Returns(new Sample());

        var registration = new DataSourceRegistration(
            Type: default!,
            new Uri("A", UriKind.Relative),
            Configuration: default);

        var pipeline = new Pipeline([registration]);

        var expectedCatalog = Sample.LoadCatalog("/A/B/C");

        var catalogState = new CatalogState(
            Root: default!,
            Cache: new CatalogCache()
        );

        var appState = new AppState()
        {
            Project = new NexusProject(default, default!),
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
            extensionHive,
            default!,
            default!,
            Options.Create(new DataOptions()),
            default!,
            loggerFactory);

        // Act
        var actual = await dataControllerService.GetDataSourceControllerAsync(pipeline, CancellationToken.None);

        // Assert
        var actualCatalog = await actual.GetCatalogAsync("/A/B/C", CancellationToken.None);

        Assert.Equal(expectedCatalog.Id, actualCatalog.Id);

        var expectedConfig = JsonSerializer.Serialize(requestConfiguration);
        var actualConfig = JsonSerializer.Serialize(((DataSourceController)actual).RequestConfiguration);

        Assert.Equal(expectedConfig, actualConfig);
    }

    [Fact]
    public async Task CanCreateAndInitializeDataWriterController()
    {
        // Arrange
        var appState = new AppState()
        {
            Project = new NexusProject(default, default!)
        };

        var extensionHive = Mock.Of<IExtensionHive>();

        Mock.Get(extensionHive)
            .Setup(extensionHive => extensionHive.GetInstance<IDataWriter>(It.IsAny<string>()))
            .Returns(new Csv());

        var loggerFactory = Mock.Of<ILoggerFactory>();
        var resourceLocator = new Uri("A", UriKind.Relative);
        var exportParameters = new ExportParameters(default, default, default, "dummy", default!, default);

        // Act
        var dataControllerService = new DataControllerService(
            appState,
            default!,
            extensionHive,
            default!,
            default!,
            Options.Create(new DataOptions()),
            default!,
            loggerFactory);

        var actual = await dataControllerService.GetDataWriterControllerAsync(
            resourceLocator,
            exportParameters,
            CancellationToken.None);

        // Assert
        /* nothing to assert */
    }
}