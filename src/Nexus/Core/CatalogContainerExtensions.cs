// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.Extensions.Logging;
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

            var representation = new Representation(NexusDataType.Float64, parseResult.SamplePeriod, default, parseResult.Kind);

            catalogItem = baseCatalogItem with
            {
                Representation = representation
            };
        }

        CatalogItem? sourceItem = default;
        CatalogItem? sourceBaseItem = default;

        if (catalogContainer.SourceId != catalogContainer.Id)
        {
            sourceItem = WithCatalogId(catalogItem, catalogContainer.SourceId);
            sourceBaseItem = baseCatalogItem is null
                ? null
                : WithCatalogId(baseCatalogItem, catalogContainer.SourceId);
        }

        return new CatalogItemRequest(catalogItem, baseCatalogItem, catalogContainer, sourceItem, sourceBaseItem);

        static CatalogItem WithCatalogId(CatalogItem item, string catalogId)
        {
            return item with
            {
                Catalog = item.Catalog with { Id = catalogId }
            };
        }
    }

    public static async Task<CatalogContainer?> TryFindCatalogContainerAsync(
        this CatalogContainer parent,
        CatalogContainer root,
        string catalogId,
        CancellationToken cancellationToken,
        int recursionCounter = 0,
        HashSet<string>? visitedLinkIds = default)
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
            if (catalogContainer.LinkTarget is not null)
            {
                if (recursionCounter >= 10)
                {
                    catalogContainer.Logger?.LogWarning(
                        "Soft-link resolution stopped at catalog {CatalogId} after reaching the maximum recursion depth while resolving {CatalogPath}.",
                        catalogContainer.Id,
                        catalogId);

                    return null;
                }

                visitedLinkIds ??= new(StringComparer.OrdinalIgnoreCase);

                if (!visitedLinkIds.Add(catalogContainer.Id))
                {
                    catalogContainer.Logger?.LogWarning(
                        "Soft-link resolution stopped after detecting a cycle at catalog {CatalogId} while resolving {CatalogPath}.",
                        catalogContainer.Id,
                        catalogId);

                    return null;
                }

                var target = await root.TryFindCatalogContainerAsync(
                    root,
                    catalogContainer.LinkTarget,
                    cancellationToken,
                    recursionCounter + 1,
                    visitedLinkIds
                );

                return target is null
                    ? null
                    : catalogContainer.CreateLinkView(target);
            }

            return catalogContainer;
        }

        /* CatalogContainer is (grand)-parent of the searched one */
        else
        {
            if (catalogContainer.LinkTarget is not null)
            {
                if (recursionCounter >= 10)
                {
                    catalogContainer.Logger?.LogWarning(
                        "Soft-link resolution stopped at catalog {CatalogId} after reaching the maximum recursion depth while resolving {CatalogPath}.",
                        catalogContainer.Id,
                        catalogId);

                    return null;
                }

                visitedLinkIds ??= new(StringComparer.OrdinalIgnoreCase);

                if (!visitedLinkIds.Add(catalogContainer.Id))
                {
                    catalogContainer.Logger?.LogWarning(
                        "Soft-link resolution stopped after detecting a cycle at catalog {CatalogId} while resolving {CatalogPath}.",
                        catalogContainer.Id,
                        catalogId);

                    return null;
                }

                var target = await root.TryFindCatalogContainerAsync(
                    root,
                    catalogContainer.LinkTarget,
                    cancellationToken,
                    recursionCounter + 1,
                    visitedLinkIds
                );

                if (target is null)
                    return null;

                catalogContainer = catalogContainer.CreateLinkView(target, applyAliasRange: false);
            }

            return await catalogContainer.TryFindCatalogContainerAsync(root, catalogId, cancellationToken, recursionCounter, visitedLinkIds);
        }
    }
}
