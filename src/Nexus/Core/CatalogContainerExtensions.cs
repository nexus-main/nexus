// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.DataModel;

namespace Nexus.Core;

internal static class CatalogContainerExtensions
{
    public static async Task<CatalogItemRequest?> TryFindAsync(
        this CatalogContainer parent,
        CatalogContainer root,
        string resourcePath,
        CancellationToken cancellationToken)
    {
        if (!DataModelUtilities.TryParseResourcePath(resourcePath, out var parseResult))
            throw new Exception("The resource path is malformed.");

        // find catalog
        var catalogContainer = await parent.TryFindCatalogContainerAsync(root, parseResult.CatalogId, cancellationToken);

        if (catalogContainer is null)
            return default;

        var lazyCatalogInfo = await catalogContainer.GetLazyCatalogInfoAsync(cancellationToken);

        if (lazyCatalogInfo is null)
            return default;

        // find base item
        CatalogItem? catalogItem;
        CatalogItem? baseCatalogItem = default;

        if (parseResult.Kind == RepresentationKind.Original)
        {
            if (!lazyCatalogInfo.Catalog.TryFind(parseResult, out catalogItem))
                return default;
        }

        else
        {
            if (!lazyCatalogInfo.Catalog.TryFind(parseResult, out baseCatalogItem))
                return default;

            var representation = new Representation(NexusDataType.FLOAT64, parseResult.SamplePeriod, default, parseResult.Kind);

            catalogItem = baseCatalogItem with
            {
                Representation = representation
            };
        }

        return new CatalogItemRequest(catalogItem, baseCatalogItem, catalogContainer);
    }

    public static async Task<CatalogContainer?> TryFindCatalogContainerAsync(
        this CatalogContainer parent,
        CatalogContainer root,
        string catalogId,
        CancellationToken cancellationToken,
        int recursionCounter = 0)
    {
        var childCatalogContainers = await parent.GetChildCatalogContainersAsync(cancellationToken);
        var catalogIdWithTrailingSlash = catalogId + "/"; /* The slashes are important to correctly find /A/D/E2 in the tests */

        var catalogContainer = childCatalogContainers
            .FirstOrDefault(current => catalogIdWithTrailingSlash.StartsWith(current.Id + "/"));

        /* Nothing found */
        if (catalogContainer is null)
            return default;

        /* CatalogContainer is the searched one */
        else if (catalogContainer.Id == catalogId)
        {
            if (catalogContainer.LinkTarget is null)
            {
                return catalogContainer;
            }

            /* It is a soft link */
            else
            {
                if (recursionCounter >= 10)
                    return null;

                return await root.TryFindCatalogContainerAsync(
                    root,
                    catalogContainer.LinkTarget,
                    cancellationToken,
                    ++recursionCounter
                );
            }
        }

        /* CatalogContainer is (grand)-parent of the searched one */
        else
            return await catalogContainer.TryFindCatalogContainerAsync(root, catalogId, cancellationToken);
    }
}
