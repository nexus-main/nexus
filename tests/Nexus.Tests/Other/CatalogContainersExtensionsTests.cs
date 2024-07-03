// MIT License
// Copyright (c) [2024] [nexus-main]

using Moq;
using Nexus.Core;
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
        // arrange
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
                       new(new CatalogRegistration("/A", string.Empty), default!, default!, default!, default!, catalogManager, default!, default!),
                   },
                   "/A" =>
                   [
                       new CatalogContainer(new CatalogRegistration("/A/C", string.Empty), default!, default!,  default!, default!, catalogManager, default!, default!),
                       new CatalogContainer(new CatalogRegistration("/A/B", string.Empty), default!, default!, default!, default!, catalogManager, default!, default!),
                       new CatalogContainer(new CatalogRegistration("/A/D", string.Empty), default!, default!, default!, default!, catalogManager, default!, default!)
                   ],
                   "/A/B" =>
                   [
                       new CatalogContainer(new CatalogRegistration("/A/B/D", string.Empty), default!, default!, default!, default!, catalogManager, default!, default!),
                       new CatalogContainer(new CatalogRegistration("/A/B/C", string.Empty), default!, default!, default!, default!, catalogManager, default!, default!)
                   ],
                   "/A/D" =>
                   [
                       new CatalogContainer(new CatalogRegistration("/A/D/F", string.Empty), default!, default!, default!, default!, catalogManager, default!, default!),
                       new CatalogContainer(new CatalogRegistration("/A/D/E", string.Empty), default!, default!, default!, default!, catalogManager, default!, default!),
                       new CatalogContainer(new CatalogRegistration("/A/D/E2", string.Empty), default!, default!, default!, default!, catalogManager, default!, default!)
                   ],
                   "/A/F" =>
                   [
                       new CatalogContainer(new CatalogRegistration("/A/F/H", string.Empty), default!, default!, default!, default!, catalogManager, default!, default!)
                   ],
                   _ => throw new Exception($"Unsupported combination {container.Id}.")
               });
           });

        var root = CatalogContainer.CreateRoot(catalogManager, default!);

        // act
        var catalogContainerA = await root.TryFindCatalogContainerAsync("/A/B/C", CancellationToken.None);
        var catalogContainerB = await root.TryFindCatalogContainerAsync("/A/D/E", CancellationToken.None);
        var catalogContainerB2 = await root.TryFindCatalogContainerAsync("/A/D/E2", CancellationToken.None);
        var catalogContainerC = await root.TryFindCatalogContainerAsync("/A/F/G", CancellationToken.None);

        // assert
        Assert.NotNull(catalogContainerA);
        Assert.Equal("/A/B/C", catalogContainerA?.Id);

        Assert.NotNull(catalogContainerB);
        Assert.Equal("/A/D/E", catalogContainerB?.Id);

        Assert.NotNull(catalogContainerB2);
        Assert.Equal("/A/D/E2", catalogContainerB2?.Id);

        Assert.Null(catalogContainerC);
    }

    [Fact]
    public async Task CanTryFind()
    {
        // arrange
        var representation1 = new Representation(NexusDataType.FLOAT64, TimeSpan.FromMilliseconds(1));
        var representation2 = new Representation(NexusDataType.FLOAT64, TimeSpan.FromMilliseconds(100));

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
               It.IsAny<DataSourceRegistration>(),
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
                    new(new CatalogRegistration("/A/B/C", string.Empty), default!, default!, default!, default!, default!, default!, dataControllerService),
                  },
                  _ => throw new Exception("Unsupported combination.")
              });
          });

        var root = CatalogContainer.CreateRoot(catalogManager, default!);

        // act
        var request1 = await root.TryFindAsync("/A/B/C/T1/1_ms", CancellationToken.None);
        var request2 = await root.TryFindAsync("/A/B/C/T1/10_ms", CancellationToken.None);
        var request3 = await root.TryFindAsync("/A/B/C/T1/100_ms", CancellationToken.None);
        var request4 = await root.TryFindAsync("/A/B/C/T1/1_s_mean_polar_deg", CancellationToken.None);
        var request5 = await root.TryFindAsync("/A/B/C/T1/1_s_min_bitwise#base=1_ms", CancellationToken.None);
        var request6 = await root.TryFindAsync("/A/B/C/T1/1_s_max_bitwise#base=100_ms", CancellationToken.None);

        // assert
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
    }
}