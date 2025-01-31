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
using Nexus.Extensibility;
using Nexus.Services;
using Nexus.Sources;
using Nexus.Writers;
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
            Configuration: default);

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
            loggerFactory);

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
        var exportParameters = new ExportParameters(default, default, default, "dummy", default!, default);

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