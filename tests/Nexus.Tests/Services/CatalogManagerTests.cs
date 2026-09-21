// MIT License
// Copyright (c) [2024] [nexus-main]

using Apollo3zehn.PackageManagement.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Nexus.Core;
using Nexus.Core.V1;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using Nexus.Sources;
using Nexus.Utilities;
using Xunit;

namespace Services;

public class CatalogManagerTests
{
    delegate bool GobbleReturns(string catalogId, out string catalogMetadata);

    [Fact]
    public async Task CanCreateCatalogHierarchy()
    {
        // Test case:
        // Pipeline A: / => /A, /B/A
        //             /A/ => /A/B, /A/B/C (ignored - duplicate), /A/C/A
        // Pipeline B: / => /A (ignored - duplicate), /B/B, /B/B2, /C/A, /D
        // Pipeline C: / => /C/A (ignored - duplicate)
        // Pipeline D: / => /D (ignored - duplicate)
        // Pipeline E: throws (ignored)

        /* dataControllerService */
        var dataControllerService = Mock.Of<IDataControllerService>();

        Mock.Get(dataControllerService)
            .Setup(s => s.GetDataSourceControllerAsync(It.IsAny<DataSourcePipeline>(), It.IsAny<CancellationToken>()))
            .Returns<DataSourcePipeline, CancellationToken>((pipeline, cancellationToken) =>
            {
                var dataSourceController = Mock.Of<IDataSourceController>();

                Mock.Get(dataSourceController)
                    .Setup(s => s.GetCatalogRegistrationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns<string, CancellationToken>((path, cancellationToken) =>
                    {
                        var type = pipeline.Registrations[0].Type;

                        return (type, path) switch
                        {
                            ("A", "/") => Task.FromResult(new CatalogRegistration[] { new("/A", string.Empty), new("/B/A", string.Empty) }),
                            ("A", "/A/") => Task.FromResult(new CatalogRegistration[] { new("/A/B", string.Empty), new("/A/B/C", string.Empty), new("/A/C/A", string.Empty) }),
                            ("B", "/") => Task.FromResult(new CatalogRegistration[] { new("/A", string.Empty), new("/B/B", string.Empty), new("/B/B2", string.Empty) }),
                            ("C", "/") => Task.FromResult(new CatalogRegistration[] { new("/C/A", string.Empty) }),
                            ("D", "/") => Task.FromResult(new CatalogRegistration[] { new("/D", string.Empty) }),
                            ("Nexus.Sources." + nameof(Sample), "/") => Task.FromResult(Array.Empty<CatalogRegistration>()),
                            _ => throw new Exception("Unsupported combination.")
                        };
                    });

                return Task.FromResult(dataSourceController);
            });

        // databaseService
        var databaseService = Mock.Of<IDatabaseService>();

        Mock.Get(databaseService)
            .Setup(databaseService => databaseService.TryReadCatalogMetadata(
                It.IsAny<string>(),
                out It.Ref<string?>.IsAny))
            .Returns(new GobbleReturns((string catalogId, out string catalogMetadataString) =>
            {
                catalogMetadataString = "{}";
                return true;
            }));

        /* serviceProvider */
        var serviceProvider = Mock.Of<IServiceProvider>();

        /* extensionHive */
        var extensionHive = Mock.Of<IExtensionHive<IDataSource>>();

        /* pipelineService */
        var registrationA = new DataSourceRegistration(Type: "A", new Uri("https://match-me-1"), default);
        var registrationB = new DataSourceRegistration(Type: "B", new Uri("https://match-me-1"), default);
        var registrationC = new DataSourceRegistration(Type: "C", new Uri("https://match-me-2"), default);
        var registrationD = new DataSourceRegistration(Type: "D", new Uri("https://do-not-match-me"), default);
        var registrationE = new DataSourceRegistration(Type: "E", new Uri("https://match-me-1"), default);

        var pipelineService = Mock.Of<IPipelineService>();

        Mock.Get(pipelineService)
            .Setup(pipelineService => pipelineService.GetAllAsync())
            .ReturnsAsync(() =>
            {
                return new Dictionary<Guid, DataSourcePipeline>()
                {
                    [Guid.NewGuid()] = new DataSourcePipeline([registrationA]),
                    [Guid.NewGuid()] = new DataSourcePipeline([registrationB]),
                    [Guid.NewGuid()] = new DataSourcePipeline([registrationC]),
                    [Guid.NewGuid()] = new DataSourcePipeline([registrationD]),
                    [Guid.NewGuid()] = new DataSourcePipeline([registrationE])
                };
            });

        /* SecurityOptions */
        var securityOptions = Options.Create(new SecurityOptions());

        /* catalogManager */
        var catalogManager = new CatalogManager(
            dataControllerService,
            databaseService,
            serviceProvider,
            extensionHive,
            pipelineService,
            securityOptions,
            NullLogger<CatalogManager>.Instance
        );

        // act
        var root = CatalogContainer.CreateRoot(catalogManager, databaseService);
        var rootCatalogContainers = (await root.GetChildCatalogContainersAsync(CancellationToken.None)).ToArray();
        var ACatalogContainers = (await rootCatalogContainers[0].GetChildCatalogContainersAsync(CancellationToken.None)).ToArray();

        // assert '/'
        Assert.Equal(6, rootCatalogContainers.Length);

        Assert.Contains(
            rootCatalogContainers,
            container => container.Id == "/A" && container.Pipeline.Registrations.Count == 1 && container.Pipeline.Registrations[0] == registrationA);

        Assert.Contains(
            rootCatalogContainers,
            container => container.Id == "/B/A" && container.Pipeline.Registrations.Count == 1 && container.Pipeline.Registrations[0] == registrationA);

        Assert.Contains(
            rootCatalogContainers,
            container => container.Id == "/B/B" && container.Pipeline.Registrations.Count == 1 && container.Pipeline.Registrations[0] == registrationB);

        Assert.Contains(
            rootCatalogContainers,
            container => container.Id == "/B/B2" && container.Pipeline.Registrations.Count == 1 && container.Pipeline.Registrations[0] == registrationB);

        Assert.Contains(
            rootCatalogContainers,
            container => container.Id == "/C/A" && container.Pipeline.Registrations.Count == 1 && container.Pipeline.Registrations[0] == registrationC);

        Assert.Contains(
            rootCatalogContainers,
            container => container.Id == "/D" && container.Pipeline.Registrations.Count == 1 && container.Pipeline.Registrations[0] == registrationD);

        // assert 'A'
        Assert.Equal(2, ACatalogContainers.Length);

        Assert.Contains(
            ACatalogContainers,
            container => container.Id == "/A/B" && container.Pipeline.Registrations.Count == 1 && container.Pipeline.Registrations[0] == registrationA);

        Assert.Contains(
            ACatalogContainers,
            container => container.Id == "/A/C/A" && container.Pipeline.Registrations.Count == 1 && container.Pipeline.Registrations[0] == registrationA);
    }

    [Fact]
    public async Task CanLoadLazyCatalogInfos()
    {
        // Arrange

        /* expected catalogs */
        var expectedCatalog = new ResourceCatalogBuilder(id: "/A")
            .AddResource(new ResourceBuilder(id: "A").AddRepresentation(new Representation(NexusDataType.Int16, TimeSpan.FromSeconds(1))).Build())
            .WithReadme("v2")
            .Build();

        /* expected time range response */
        var expectedTimeRange = new CatalogTimeRange(new DateTime(2020, 01, 01), new DateTime(2020, 01, 02));

        /* data controller service */
        var dataControllerService = Mock.Of<IDataControllerService>();

        Mock.Get(dataControllerService)
            .Setup(s => s.GetDataSourceControllerAsync(It.IsAny<DataSourcePipeline>(), It.IsAny<CancellationToken>()))
            .Returns<DataSourcePipeline, CancellationToken>((_, _) =>
            {
                var dataSourceController = Mock.Of<IDataSourceController>();

                Mock.Get(dataSourceController)
                    .Setup(s => s.GetCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(expectedCatalog);

                Mock.Get(dataSourceController)
                    .Setup(s => s.GetTimeRangeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(expectedTimeRange);

                return Task.FromResult(dataSourceController);
            });

        /* catalog metadata */
        var catalogMetadata = new CatalogMetadata(
            default,
            default,
            Overrides: new ResourceCatalogBuilder(id: "/A")
                .WithReadme("v2")
                .Build()
        );

        /* pipeline */
        var registration = new DataSourceRegistration(
            Type: "A",
            ResourceLocator: default,
            Configuration: default!
        );

        var pipeline = new DataSourcePipeline([registration]);

        /* catalog container */
        var catalogContainer = new CatalogContainer(
            new CatalogRegistration("/A", string.Empty),
            default,
            pipeline,
            default!,
            catalogMetadata,
            default!,
            default!,
            dataControllerService);

        // Act
        var lazyCatalogInfo = await catalogContainer.GetLazyCatalogInfoAsync(CancellationToken.None);

        // Assert
        var actualJsonString = JsonSerializerHelper.SerializeIndented(lazyCatalogInfo.Catalog);
        var expectedJsonString = JsonSerializerHelper.SerializeIndented(expectedCatalog);

        Assert.Equal(actualJsonString, expectedJsonString);
        Assert.Equal(new DateTime(2020, 01, 01), lazyCatalogInfo.Begin);
        Assert.Equal(new DateTime(2020, 01, 02), lazyCatalogInfo.End);
    }
}