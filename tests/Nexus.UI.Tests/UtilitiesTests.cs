// MIT License
// Copyright (c) [2024] [nexus-main]

using Moq;
using Nexus.Api;
using Nexus.Api.V1;
using Nexus.UI.Core;
using Xunit;

namespace Nexus.UI.Tests;

public class UtilitiesTests
{
    [Fact]
    public async Task CanPrepareChildCatalogsAsync()
    {
        /* This methods creates intermediate fake catalogs (marked with a *)
         * to group child catalogs. Example:
         *
         *   /A/A/A
         *   /A/A/B
         *   /A/B
         *   -> /* + /A* (/A/A, /A/B) + /A/A* (/A/A/A, /A/A/B)
         */

        // Arrange
        var nexusClient = Mock.Of<INexusClient>();
        var nexusClientV1 = Mock.Of<IV1>();
        var catalogsClient = Mock.Of<ICatalogsClient>();

        Mock.Get(nexusClient)
            .SetupGet(x => x.V1)
            .Returns(nexusClientV1);

        Mock.Get(nexusClientV1)
            .SetupGet(x => x.Catalogs)
            .Returns(catalogsClient);

        var AAchildCatalogInfos = (IReadOnlyList<CatalogInfo>)new List<CatalogInfo>()
        {
            GetCatalogInfo("/A/A/A"),
            GetCatalogInfo("/A/A/B")
        };

        Mock.Get(catalogsClient)
            .Setup(x => x.GetChildCatalogInfosAsync("/A/A", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(AAchildCatalogInfos));

        var ABchildCatalogInfos = (IReadOnlyList<CatalogInfo>)new List<CatalogInfo>()
        {
            // empty
        };

        Mock.Get(catalogsClient)
            .Setup(x => x.GetChildCatalogInfosAsync("/A/B", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(ABchildCatalogInfos));

        var appState = Mock.Of<IAppState>();
        var id_root = "/";

        var childCatalogInfos_1 = new string[]
        {
            "/A/A/A",
            "/A/A/B",
            "/A/B"
        }
        .Select(GetCatalogInfo)
        .ToList();

        // Act

        /* root */
        var actual_root = Utilities.PrepareChildCatalogs(id_root, childCatalogInfos_1, nexusClient, appState);

        /* A */
        var id_A = actual_root[0].Id;
        var children_A = await actual_root[0].ChildrenTask.Value;

        var childCatalogInfos_A = children_A
            .Select(x => x.Id)
            .Select(GetCatalogInfo)
            .ToList();

        var actual_A = Utilities.PrepareChildCatalogs(id_A, childCatalogInfos_A, nexusClient, appState);

        /* AA */
        var id_AA = actual_A[0].Id;
        var children_AA = await actual_A[0].ChildrenTask.Value;

        var childCatalogInfos_AA = children_AA
            .Select(x => x.Id)
            .Select(GetCatalogInfo)
            .ToList();

        var actual_AA = Utilities.PrepareChildCatalogs(id_AA, childCatalogInfos_AA, nexusClient, appState);

        /* AB */
        var id_AB = actual_A[1].Id;
        var children_AB = await actual_A[1].ChildrenTask.Value;

        var childCatalogInfos_AB = children_AB
            .Select(x => x.Id)
            .Select(GetCatalogInfo)
            .ToList();

        var actual_AB = Utilities.PrepareChildCatalogs(id_AB, childCatalogInfos_AB, nexusClient, appState);

        // Assert
        Assert.Single(actual_root);
        Assert.Equal("/A", actual_root[0].Id);

        Assert.Collection(actual_A,

            x =>
            {
                Assert.Equal("/A/A", x.Id);

                Assert.Collection(actual_AA,
                    x => Assert.Equal("/A/A/A", x.Id),
                    x => Assert.Equal("/A/A/B", x.Id)
                );
            },

            x =>
            {
                Assert.Equal("/A/B", x.Id);
                Assert.Empty(actual_AB);
            }

        );
    }

    private CatalogInfo GetCatalogInfo(string id)
    {
        return new CatalogInfo(
            id,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            IsOwner: true,
            default!,
            default!
        );
    }
}