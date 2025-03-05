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
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Services;

public class CatalogManagerTests
{
    delegate bool GobbleReturns(string catalogId, out string catalogMetadata);

    [Fact]
    public async Task CanCreateCatalogHierarchy()
    {
        // Test case:
        // User A, admin,
        //      /   => /A, /B/A
        //      /A/ => /A/B, /A/B/C (ignore), /A/C/A
        //
        // User B, no admin,
        //      /  => /A (ignore because no admin), /B/B, /B/B2, /C/A, /D (ignore because of missing NexusClaims.CanUseResourceLocator), /E (ignore because of no matching EnabledCatalogsPattern)

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
                            ("E", "/") => Task.FromResult(new CatalogRegistration[] { new("/E", string.Empty) }),
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

        /* serviceProvider / dbService */
        var dbService = Mock.Of<IDBService>();
        var scope = Mock.Of<IServiceScope>();
        var scopeFactory = Mock.Of<IServiceScopeFactory>();
        var serviceProvider = Mock.Of<IServiceProvider>();

        Mock.Get(scope)
            .SetupGet(scope => scope.ServiceProvider)
            .Returns(serviceProvider);

        Mock.Get(scopeFactory)
            .Setup(scopeFactory => scopeFactory.CreateScope())
            .Returns(scope);

        Mock.Get(serviceProvider)
            .Setup(serviceProvider => serviceProvider.GetService(
                It.Is<Type>(value => value == typeof(IDBService))))
            .Returns(dbService);

        Mock.Get(serviceProvider)
            .Setup(serviceProvider => serviceProvider.GetService(
                It.Is<Type>(value => value == typeof(IServiceScopeFactory))))
            .Returns(scopeFactory);

        /* => User A */
        var usernameA = "UserA";
        var schemeA = "scheme-A";

        var userAClaims = new List<NexusClaim>
        {
            new(Guid.NewGuid(), Claims.Name, usernameA),
            new(Guid.NewGuid(), Claims.Role, nameof(NexusRoles.Administrator))
        };

        var userA = new NexusUser(
            id: "userA@" + schemeA,
            name: usernameA)
        {
            Claims = userAClaims
        };

        /* => User B */
        var usernameB = "UserB";
        var schemeB = "scheme-B";

        var userBClaims = new List<NexusClaim>
        {
            new(Guid.NewGuid(), Claims.Name, usernameB),
            new(Guid.NewGuid(), nameof(NexusClaims.CanWriteCatalog), ""),
            new(Guid.NewGuid(), nameof(NexusClaims.CanUseResourceLocator), "match-me-1"),
            new(Guid.NewGuid(), nameof(NexusClaims.CanUseResourceLocator), "match-me-2")
        };

        var userB = new NexusUser(
            id: "userB@" + schemeB,
            name: usernameB)
        {
            Claims = userBClaims
        };

        Mock.Get(dbService)
            .Setup(dbService => dbService.FindUserAsync(It.IsAny<string>()))
            .Returns<string>(userId =>
            {
                var result = userId switch
                {
                    "UserA" => Task.FromResult<NexusUser?>(userA),
                    "UserB" => Task.FromResult<NexusUser?>(userB),
                    _ => Task.FromResult<NexusUser?>(default)
                };

                return result;
            });

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
                return new Dictionary<string, IReadOnlyDictionary<Guid, DataSourcePipeline>>
                {
                    ["UserA"] = new Dictionary<Guid, DataSourcePipeline>()
                    {
                        [Guid.NewGuid()] = new DataSourcePipeline([registrationA])
                    },

                    ["UserB"] = new Dictionary<Guid, DataSourcePipeline>()
                    {
                        [Guid.NewGuid()] = new DataSourcePipeline([registrationB]),
                        [Guid.NewGuid()] = new DataSourcePipeline([registrationC]),
                        [Guid.NewGuid()] = new DataSourcePipeline([registrationD]),
                        [Guid.NewGuid()] = new DataSourcePipeline([registrationE])
                    }
                };
            });

        /* SecurityOptions */
        var securityOptions = Options.Create(new SecurityOptions
        {
            OidcProviders = [
                new OpenIdConnectProvider(
                    Scheme: schemeA,
                    default!,
                    default!,
                    default!,
                    default!
                ),

                new OpenIdConnectProvider(
                    Scheme: schemeB,
                    default!,
                    default!,
                    default!,
                    default!,
                    EnabledCatalogsPattern: "^/(?:A|B|C|D)"
                )
            ]
        });

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
        var root = CatalogContainer.CreateRoot(catalogManager, default!);
        var rootCatalogContainers = (await root.GetChildCatalogContainersAsync(CancellationToken.None)).ToArray();
        var ACatalogContainers = (await rootCatalogContainers[0].GetChildCatalogContainersAsync(CancellationToken.None)).ToArray();

        // assert '/'
        Assert.Equal(5, rootCatalogContainers.Length);

        Assert.Contains(
            rootCatalogContainers,
            container => container.Id == "/A" && container.Pipeline.Registrations.Count == 1 && container.Pipeline.Registrations[0] == registrationA && container.Owner!.Identity!.Name! == userA.Name);

        Assert.Contains(
            rootCatalogContainers,
            container => container.Id == "/B/A" && container.Pipeline.Registrations.Count == 1 && container.Pipeline.Registrations[0] == registrationA && container.Owner!.Identity!.Name! == userA.Name);

        Assert.Contains(
            rootCatalogContainers,
            container => container.Id == "/B/B" && container.Pipeline.Registrations.Count == 1 && container.Pipeline.Registrations[0] == registrationB && container.Owner!.Identity!.Name! == userB.Name);

        Assert.Contains(
            rootCatalogContainers,
            container => container.Id == "/B/B2" && container.Pipeline.Registrations.Count == 1 && container.Pipeline.Registrations[0] == registrationB && container.Owner!.Identity!.Name! == userB.Name);

        Assert.Contains(
            rootCatalogContainers,
            container => container.Id == "/C/A" && container.Pipeline.Registrations.Count == 1 && container.Pipeline.Registrations[0] == registrationC && container.Owner!.Identity!.Name! == userB.Name);

        // assert 'A'
        Assert.Equal(2, ACatalogContainers.Length);

        Assert.Contains(
            ACatalogContainers,
            container => container.Id == "/A/B" && container.Pipeline.Registrations.Count == 1 && container.Pipeline.Registrations[0] == registrationA && container.Owner!.Identity!.Name! == userA.Name);

        Assert.Contains(
            ACatalogContainers,
            container => container.Id == "/A/C/A" && container.Pipeline.Registrations.Count == 1 && container.Pipeline.Registrations[0] == registrationA && container.Owner!.Identity!.Name! == userA.Name);
    }

    [Fact]
    public async Task CanLoadLazyCatalogInfos()
    {
        // Arrange

        /* expected catalogs */
        var expectedCatalog = new ResourceCatalogBuilder(id: "/A")
            .AddResource(new ResourceBuilder(id: "A").AddRepresentation(new Representation(NexusDataType.INT16, TimeSpan.FromSeconds(1))).Build())
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