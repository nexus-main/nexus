// MIT License
// Copyright (c) [2024] [nexus-main]

using Moq;
using Nexus.Core;
using Nexus.Core.V1;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using Xunit;

namespace Other;

public class CatalogContainersExtensionsTests
{
    [Fact]
    public async Task CanTryFindCatalogContainer()
    {
        // Arrange
        var catalogManager = Mock.Of<ICatalogManager>();

        Mock.Get(catalogManager)
            .Setup(catalogManager => catalogManager.GetCatalogContainersAsync(
                It.IsAny<CatalogContainer>(),
                It.IsAny<CancellationToken>()))
            .Returns<CatalogContainer, CancellationToken>((parent, token) =>
            {
                return Task.FromResult(parent.Id switch
                {
                    "/" => new CatalogContainer[]
                    {
                        new (new CatalogRegistration("/A", default), default, default!, default!, default!, catalogManager, default!, default!),
                        new (new CatalogRegistration("/SOFT/A", default, LinkTarget: "/A/B/C"), default, default!, default!, default!, catalogManager, default!, default!),
                        new (new CatalogRegistration("/SOFT/B", default, LinkTarget: "/SOFT/A"), default, default!, default!, default!, catalogManager, default!, default!),
                    },
                    "/A" =>
                    [
                        new (new CatalogRegistration("/A/C", default), default, default!, default!, default!, catalogManager, default!, default!),
                        new (new CatalogRegistration("/A/B", default), default, default!, default!, default!, catalogManager, default!, default!),
                        new (new CatalogRegistration("/A/D", default), default, default!, default!, default!, catalogManager, default!, default!)
                    ],
                    "/A/B" =>
                    [
                        new (new CatalogRegistration("/A/B/D", default), default, default!, default!, default!, catalogManager, default!, default!),
                        new (new CatalogRegistration("/A/B/C", default), default, default!, default!, default!, catalogManager, default!, default!)
                    ],
                    "/A/D" =>
                    [
                        new (new CatalogRegistration("/A/D/F", default), default, default!, default!, default!, catalogManager, default!, default!),
                        new (new CatalogRegistration("/A/D/E", default), default, default!, default!, default!, catalogManager, default!, default!),
                        new (new CatalogRegistration("/A/D/E2", default), default, default!, default!, default!, catalogManager, default!, default!)
                    ],
                    "/A/F" =>
                    [
                        new (new CatalogRegistration("/A/F/H", default), default, default!, default!, default!, catalogManager, default!, default!)
                    ],
                    _ => throw new Exception($"Unsupported combination: {parent.Id}.")
                });
            });

        var root = CatalogContainer.CreateRoot(catalogManager, default!);

        // Act
        var catalogContainerA = await root.TryResolveCatalogContainerAsync(root, "/A/B/C", CancellationToken.None);
        var catalogContainerB = await root.TryResolveCatalogContainerAsync(root, "/A/D/E", CancellationToken.None);
        var catalogContainerB2 = await root.TryResolveCatalogContainerAsync(root, "/A/D/E2", CancellationToken.None);
        var catalogContainerC = await root.TryResolveCatalogContainerAsync(root, "/A/F/G", CancellationToken.None);
        var catalogContainerSoft = await root.TryResolveCatalogContainerAsync(root, "/SOFT/B", CancellationToken.None);

        // Assert
        Assert.NotNull(catalogContainerA);
        Assert.Equal("/A/B/C", catalogContainerA?.Id);

        Assert.NotNull(catalogContainerB);
        Assert.Equal("/A/D/E", catalogContainerB?.Id);

        Assert.NotNull(catalogContainerB2);
        Assert.Equal("/A/D/E2", catalogContainerB2?.Id);

        Assert.Null(catalogContainerC);

        Assert.NotNull(catalogContainerSoft);
        Assert.Equal("/SOFT/B", catalogContainerSoft?.Id);
        Assert.Equal("/A/B/C", catalogContainerSoft?.BackingSourceId);
    }

    [Fact]
    public async Task CanRejectSelfReferencingSoftLink()
    {
        // Arrange
        var catalogManager = Mock.Of<ICatalogManager>();

        Mock.Get(catalogManager)
            .Setup(manager => manager.GetCatalogContainersAsync(
                It.IsAny<CatalogContainer>(),
                It.IsAny<CancellationToken>()))
            .Returns<CatalogContainer, CancellationToken>((parent, _) => Task.FromResult<CatalogContainer[]>(parent.Id switch
            {
                "/" => [
                    new(new CatalogRegistration("/A", default, LinkTarget: "/A"), default, default!, default!, default!, catalogManager, default!, default!, "/A")
                ],
                _ => throw new Exception($"Unsupported combination: {parent.Id}.")
            }));

        var root = CatalogContainer.CreateRoot(catalogManager, default!);

        // Act
        var actual = await root.TryResolveCatalogContainerAsync(root, "/A", CancellationToken.None);

        // Assert
        Assert.Null(actual);
    }

    [Fact]
    public async Task CanRejectSoftLinkCycle()
    {
        // Arrange
        var catalogManager = Mock.Of<ICatalogManager>();

        Mock.Get(catalogManager)
            .Setup(manager => manager.GetCatalogContainersAsync(
                It.IsAny<CatalogContainer>(),
                It.IsAny<CancellationToken>()))
            .Returns<CatalogContainer, CancellationToken>((parent, _) => Task.FromResult<CatalogContainer[]>(parent.Id switch
            {
                "/" => [
                    new(new CatalogRegistration("/A", default, LinkTarget: "/B"), default, default!, default!, default!, catalogManager, default!, default!, "/A"),
                    new(new CatalogRegistration("/B", default, LinkTarget: "/A"), default, default!, default!, default!, catalogManager, default!, default!, "/B")
                ],
                _ => throw new Exception($"Unsupported combination: {parent.Id}.")
            }));

        var root = CatalogContainer.CreateRoot(catalogManager, default!);

        // Act
        var actual = await root.TryResolveCatalogContainerAsync(root, "/A", CancellationToken.None);

        // Assert
        Assert.Null(actual);
    }

    [Fact]
    public async Task CanResolveStableSoftLinkTargetOnlyOnce()
    {
        var catalogManager = Mock.Of<ICatalogManager>();
        var sourceLoadCount = 0;

        Mock.Get(catalogManager)
            .Setup(manager => manager.GetCatalogContainersAsync(
                It.IsAny<CatalogContainer>(),
                It.IsAny<CancellationToken>()))
            .Returns<CatalogContainer, CancellationToken>((parent, _) =>
            {
                if (parent.Id == "/SOURCE")
                    sourceLoadCount++;

                return Task.FromResult<CatalogContainer[]>(parent.Id switch
                {
                    "/" =>
                    [
                        new(new CatalogRegistration("/SOURCE", default), default, default!, default!, default!, catalogManager, default!, default!),
                        new(new CatalogRegistration("/ALIAS", default, LinkTarget: "/SOURCE/CHILD"), default, default!, default!, default!, catalogManager, default!, default!)
                    ],
                    "/SOURCE" =>
                    [
                        new(new CatalogRegistration("/SOURCE/CHILD", default), default, default!, default!, default!, catalogManager, default!, default!)
                    ],
                    _ => throw new Exception($"Unsupported combination: {parent.Id}.")
                });
            });

        var root = CatalogContainer.CreateRoot(catalogManager, default!);

        var first = await root.TryResolveCatalogContainerAsync(root, "/ALIAS", CancellationToken.None);
        var second = await root.TryResolveCatalogContainerAsync(root, "/ALIAS", CancellationToken.None);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal("/SOURCE/CHILD", first!.BackingSourceId);
        Assert.Equal(1, sourceLoadCount);
    }

    [Fact]
    public void CanDetermineWhenSoftLinkTargetMustBeResolved()
    {
        var stableAlias = new CatalogContainer(
            new CatalogRegistration("/ALIAS", default, LinkTarget: "/SOURCE"),
            default,
            default!,
            default!,
            default!,
            default!,
            default!,
            default!);
        var stableTarget = new CatalogContainer(
            new CatalogRegistration("/SOURCE", default),
            default,
            default!,
            default!,
            default!,
            default!,
            default!,
            default!);
        var transientAlias = new CatalogContainer(
            new CatalogRegistration("/TRANSIENT_ALIAS", default, IsTransient: true, LinkTarget: "/SOURCE"),
            default,
            default!,
            default!,
            default!,
            default!,
            default!,
            default!);
        var transientTarget = new CatalogContainer(
            new CatalogRegistration("/TRANSIENT_SOURCE", default, IsTransient: true),
            default,
            default!,
            default!,
            default!,
            default!,
            default!,
            default!);

        Assert.True(stableAlias.ShouldResolveLinkTarget);

        stableAlias.ResolveLinkTarget(stableTarget);
        Assert.False(stableAlias.ShouldResolveLinkTarget);

        transientAlias.ResolveLinkTarget(stableTarget);
        Assert.True(transientAlias.ShouldResolveLinkTarget);

        stableAlias.ResolveLinkTarget(transientTarget);
        Assert.True(stableAlias.ShouldResolveLinkTarget);
    }

    [Fact]
    public async Task CanTryFind()
    {
        // Arrange
        var representation1 = new Representation(NexusDataType.Float64, TimeSpan.FromMilliseconds(1));
        var representation2 = new Representation(NexusDataType.Float64, TimeSpan.FromMilliseconds(100));

        var resource = new ResourceBuilder("T1")
            .AddRepresentation(representation1)
            .AddRepresentation(representation2)
            .Build();

        var catalog = new ResourceCatalogBuilder("/A/B/C")
            .AddResource(resource)
            .Build();

        var dataSourceController = Mock.Of<IDataSourceController>();

        Mock.Get(dataSourceController)
           .Setup(dataSourceController => dataSourceController.GetCatalogAsync(
               It.IsAny<string>(),
               It.IsAny<CancellationToken>()))
           .ReturnsAsync(catalog);

        Mock.Get(dataSourceController)
          .Setup(dataSourceController => dataSourceController.GetTimeRangeAsync(
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new CatalogTimeRange(default, default));

        var dataControllerService = Mock.Of<IDataControllerService>();

        Mock.Get(dataControllerService)
           .Setup(dataControllerService => dataControllerService.GetDataSourceControllerAsync(
               It.IsAny<DataSourcePipeline>(),
               It.IsAny<CancellationToken>()))
           .ReturnsAsync(dataSourceController);

        var catalogManager = Mock.Of<ICatalogManager>();

        Mock.Get(catalogManager)
            .Setup(catalogManager => catalogManager.GetCatalogContainersAsync(
                It.IsAny<CatalogContainer>(),
                It.IsAny<CancellationToken>()))
            .Returns<CatalogContainer, CancellationToken>((container, token) =>
            {
                return Task.FromResult(container.Id switch
                {
                    "/" => new CatalogContainer[]
                    {
                        new (new CatalogRegistration("/A/B/C", default), default, default!, Array.Empty<Guid>(), default!, default!, default!, dataControllerService),
                        new (new CatalogRegistration("/ALIAS", default, LinkTarget: "/A/B/C"), default, default!, Array.Empty<Guid>(), default!, default!, default!, dataControllerService),
                    },
                    _ => throw new Exception("Unsupported combination.")
                });
            });

        var root = CatalogContainer.CreateRoot(catalogManager, default!);

        // Act
        var request1 = await root.TryFindAsync(root, "/A/B/C/T1/1_ms", CancellationToken.None);
        var request2 = await root.TryFindAsync(root, "/A/B/C/T1/10_ms", CancellationToken.None);
        var request3 = await root.TryFindAsync(root, "/A/B/C/T1/100_ms", CancellationToken.None);
        var request4 = await root.TryFindAsync(root, "/A/B/C/T1/1_s_mean_polar_deg", CancellationToken.None);
        var request5 = await root.TryFindAsync(root, "/A/B/C/T1/1_s_min_bitwise#base=1_ms", CancellationToken.None);
        var request6 = await root.TryFindAsync(root, "/A/B/C/T1/1_s_max_bitwise#base=100_ms", CancellationToken.None);
        var request7 = await root.TryFindAsync(root, "/ALIAS/T1/1_ms", CancellationToken.None);

        // Assert
        Assert.NotNull(request1);
        Assert.Null(request2);
        Assert.NotNull(request3);
        Assert.NotNull(request4);
        Assert.NotNull(request5);
        Assert.NotNull(request6);

        Assert.Null(request1!.BaseItem);
        Assert.Null(request3!.BaseItem);
        Assert.NotNull(request4!.BaseItem);
        Assert.NotNull(request5!.BaseItem);
        Assert.NotNull(request6!.BaseItem);

        Assert.Equal("/A/B/C/T1/1_ms", request1.Item.ToPath());
        Assert.Equal("/A/B/C/T1/100_ms", request3.Item.ToPath());
        Assert.Equal("/A/B/C/T1/1_s_mean_polar_deg", request4.Item.ToPath());
        Assert.Equal("/A/B/C/T1/1_s_min_bitwise", request5.Item.ToPath());
        Assert.Equal("/A/B/C/T1/1_s_max_bitwise", request6.Item.ToPath());

        Assert.Equal("/A/B/C/T1/1_ms", request4.BaseItem!.ToPath());
        Assert.Equal("/A/B/C/T1/1_ms", request5.BaseItem!.ToPath());
        Assert.Equal("/A/B/C/T1/100_ms", request6.BaseItem!.ToPath());

        Assert.NotNull(request7);
        Assert.Equal("/ALIAS/T1/1_ms", request7!.Item.ToPath());
        Assert.Equal("/A/B/C/T1/1_ms", request7.SourceItem!.ToPath());
    }

    [Fact]
    public async Task CanTryFindDescendantThroughSoftLink()
    {
        // Arrange
        var representation = new Representation(NexusDataType.Float64, TimeSpan.FromMilliseconds(1));
        var resource = new ResourceBuilder("T1")
            .AddRepresentation(representation)
            .Build();
        var catalog = new ResourceCatalogBuilder("/SOURCE/CHILD")
            .AddResource(resource)
            .Build();
        var pipeline = new DataSourcePipeline([]);
        var dataSourceController = Mock.Of<IDataSourceController>();

        Mock.Get(dataSourceController)
            .Setup(controller => controller.GetCatalogAsync("/SOURCE/CHILD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalog);

        Mock.Get(dataSourceController)
            .Setup(controller => controller.GetTimeRangeAsync("/SOURCE/CHILD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogTimeRange(default, default));

        var dataControllerService = Mock.Of<IDataControllerService>();

        Mock.Get(dataControllerService)
            .Setup(service => service.GetDataSourceControllerAsync(pipeline, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dataSourceController);

        var catalogManager = Mock.Of<ICatalogManager>();

        Mock.Get(catalogManager)
            .Setup(manager => manager.GetCatalogContainersAsync(It.IsAny<CatalogContainer>(), It.IsAny<CancellationToken>()))
            .Returns<CatalogContainer, CancellationToken>((container, _) =>
            {
                return Task.FromResult<CatalogContainer[]>((container.Id, container.BackingSourceId) switch
                {
                    ("/", "/") =>
                    [
                        new(new CatalogRegistration("/SOURCE", default), default, pipeline, Array.Empty<Guid>(), default!, catalogManager, default!, dataControllerService, "/SOURCE"),
                        new(new CatalogRegistration("/ALIAS", default, LinkTarget: "/SOURCE"), default, default!, Array.Empty<Guid>(), default!, catalogManager, default!, dataControllerService, "/ALIAS")
                    ],
                    ("/SOURCE", "/SOURCE") =>
                    [
                        new(new CatalogRegistration("/SOURCE/CHILD", default), default, pipeline, Array.Empty<Guid>(), default!, catalogManager, default!, dataControllerService)
                    ],
                    ("/ALIAS", "/SOURCE") =>
                    [
                        new(new CatalogRegistration("/ALIAS/CHILD", default), default, pipeline, Array.Empty<Guid>(), default!, catalogManager, default!, dataControllerService, backingSourceId: "/SOURCE/CHILD")
                    ],
                    _ => throw new Exception($"Unsupported combination: {container.Id} / {container.BackingSourceId}.")
                });
            });

        var root = CatalogContainer.CreateRoot(catalogManager, default!);

        // Act
        var request = await root.TryFindAsync(root, "/ALIAS/CHILD/T1/1_ms", CancellationToken.None);

        // Assert
        Assert.NotNull(request);
        Assert.Equal("/ALIAS/CHILD/T1/1_ms", request!.Item.ToPath());
        Assert.Equal("/SOURCE/CHILD/T1/1_ms", request.SourceItem!.ToPath());
    }

    [Fact]
    public async Task CanRejectDescendantSoftLinkCycle()
    {
        // Arrange
        var pipeline = new DataSourcePipeline([]);
        var catalogManager = Mock.Of<ICatalogManager>();

        Mock.Get(catalogManager)
            .Setup(manager => manager.GetCatalogContainersAsync(It.IsAny<CatalogContainer>(), It.IsAny<CancellationToken>()))
            .Returns<CatalogContainer, CancellationToken>((container, _) =>
            {
                return Task.FromResult<CatalogContainer[]>((container.Id, container.BackingSourceId) switch
                {
                    ("/", "/") =>
                    [
                        new(new CatalogRegistration("/SOURCE", default), default, pipeline, Array.Empty<Guid>(), default!, catalogManager, default!, default!, "/SOURCE"),
                        new(new CatalogRegistration("/ALIAS", default, LinkTarget: "/SOURCE"), default, pipeline, Array.Empty<Guid>(), default!, catalogManager, default!, default!, "/ALIAS")
                    ],
                    ("/SOURCE", "/SOURCE") =>
                    [
                        new(new CatalogRegistration("/SOURCE/LOOP", default, LinkTarget: "/ALIAS"), default, pipeline, Array.Empty<Guid>(), default!, catalogManager, default!, default!)
                    ],
                    ("/ALIAS", "/SOURCE") =>
                    [
                        new(new CatalogRegistration("/ALIAS/LOOP", default, LinkTarget: "/ALIAS"), default, pipeline, Array.Empty<Guid>(), default!, catalogManager, default!, default!)
                    ],
                    _ => throw new Exception($"Unsupported combination: {container.Id} / {container.BackingSourceId}.")
                });
            });

        var root = CatalogContainer.CreateRoot(catalogManager, default!);

        // Act
        var actual = await root.TryResolveCatalogContainerAsync(root, "/ALIAS/LOOP", CancellationToken.None);

        // Assert
        Assert.Null(actual);
    }

    [Fact]
    public void CanIntersectNestedSoftLinkRanges()
    {
        // Arrange
        var targetBegin = new DateTime(2024, 01, 03, 0, 0, 0, DateTimeKind.Utc);
        var targetEnd = new DateTime(2024, 01, 06, 0, 0, 0, DateTimeKind.Utc);
        var aliasBegin = new DateTime(2024, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var aliasEnd = new DateTime(2024, 01, 10, 0, 0, 0, DateTimeKind.Utc);
        var source = new CatalogContainer(
            new CatalogRegistration("/SOURCE", default),
            default,
            default!,
            default!,
            default!,
            default!,
            default!,
            default!);
        var target = new CatalogContainer(
            new CatalogRegistration("/LIMITED", default, LinkTarget: "/SOURCE", MinBegin: targetBegin, MaxEnd: targetEnd),
            default,
            default!,
            default!,
            default!,
            default!,
            default!,
            default!);
        var alias = new CatalogContainer(
            new CatalogRegistration("/ALIAS", default, LinkTarget: "/LIMITED", MinBegin: aliasBegin, MaxEnd: aliasEnd),
            default,
            default!,
            default!,
            default!,
            default!,
            default!,
            default!);

        // Act
        target.ResolveLinkTarget(source);
        alias.ResolveLinkTarget(target);

        // Assert
        Assert.Equal("/ALIAS", alias.Id);
        Assert.Equal("/SOURCE", alias.BackingSourceId);
        Assert.Equal(targetBegin, alias.MinBegin);
        Assert.Equal(targetEnd, alias.MaxEnd);
    }

    [Fact]
    public void CanTreatUnspecifiedRegistrationRangeAsUtc()
    {
        // Arrange
        var begin = new DateTime(2024, 01, 02, 0, 0, 0, DateTimeKind.Unspecified);
        var end = new DateTime(2024, 01, 04, 0, 0, 0, DateTimeKind.Unspecified);

        var container = new CatalogContainer(
            new CatalogRegistration("/ALIAS", default, MinBegin: begin, MaxEnd: end),
            default,
            default!,
            default!,
            default!,
            default!,
            default!,
            default!);

        // Assert
        Assert.Equal(DateTimeKind.Utc, container.MinBegin!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, container.MaxEnd!.Value.Kind);
        Assert.Equal(begin.Ticks, container.MinBegin.Value.Ticks);
        Assert.Equal(end.Ticks, container.MaxEnd.Value.Ticks);
    }

    [Fact]
    public async Task CanRejectAliasMetadataUpdate()
    {
        // Arrange
        var databaseService = new Mock<IDatabaseService>();
        var container = new CatalogContainer(
            new CatalogRegistration("/ALIAS", default, LinkTarget: "/SOURCE"),
            default,
            default!,
            default!,
            default!,
            default!,
            databaseService.Object,
            default!);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            container.UpdateMetadataAsync(new CatalogMetadata(default, default, default)));

        // Assert
        databaseService.Verify(service => service.WriteCatalogMetadata(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void CanJoinAliasChildPathForRootSource()
    {
        // Act / Assert
        Assert.Equal("/ALIAS/A", CatalogManager.JoinCatalogPath("/ALIAS", "/A"));
        Assert.Equal("/ALIAS/A", CatalogManager.JoinCatalogPath("/ALIAS", "A"));
    }
}
