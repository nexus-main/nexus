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
        var catalogContainer = await parent.TryResolveCatalogContainerAsync(root, parseResult.CatalogId, cancellationToken);

        if (catalogContainer is null)
            return default;

        var catalog = await catalogContainer.GetCatalogAsync(cancellationToken);

        // find base item
        CatalogItem? catalogItem;
        CatalogItem? baseCatalogItem = default;

        if (parseResult.Kind == RepresentationKind.Original)
        {
            if (!catalog.TryFind(parseResult, out catalogItem))
                return default;
        }

        else
        {
            if (!catalog.TryFind(parseResult, out baseCatalogItem))
                return default;

            var representation = new Representation(NexusDataType.Float64, parseResult.SamplePeriod, default, parseResult.Kind);

            catalogItem = baseCatalogItem with
            {
                Representation = representation
            };
        }

        CatalogItem? sourceItem = default;
        CatalogItem? sourceBaseItem = default;

        if (catalogContainer.BackingSourceId != catalogContainer.Id)
        {
            sourceItem = WithCatalogId(catalogItem, catalogContainer.BackingSourceId);
            sourceBaseItem = baseCatalogItem is null
                ? null
                : WithCatalogId(baseCatalogItem, catalogContainer.BackingSourceId);
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

    public static async Task<CatalogContainer?> TryResolveCatalogContainerAsync(
        this CatalogContainer parent,
        CatalogContainer root,
        string catalogId,
        CancellationToken cancellationToken,
        int recursionCounter = 0,
        HashSet<string>? visitedLinkIds = default)
    {
        var childCatalogContainers = await parent.GetRegisteredChildCatalogContainersAsync(cancellationToken);
        var catalogIdWithTrailingSlash = catalogId + "/"; /* The slashes are important to correctly find /A/D/E2 in the tests */

        var catalogContainer = childCatalogContainers
            .FirstOrDefault(current => catalogIdWithTrailingSlash.StartsWith(current.Id + "/"));

        /* Nothing found */
        if (catalogContainer is null)
            return default;

        /* Soft-links must be resolved for both the searched container and its (grand)-parents */
        if (catalogContainer.LinkTarget is not null)
        {
            var resolveResult = await TryResolveLinkTargetAsync(
                catalogContainer,
                root,
                catalogId,
                cancellationToken,
                recursionCounter,
                visitedLinkIds);

            if (!resolveResult.Success)
                return null;

            visitedLinkIds = resolveResult.VisitedLinkIds;
        }

        /* CatalogContainer is the searched one */
        if (catalogContainer.Id == catalogId)
            return catalogContainer;

        /* CatalogContainer is (grand)-parent of the searched one */
        return await catalogContainer.TryResolveCatalogContainerAsync(
            root, catalogId, cancellationToken, recursionCounter, visitedLinkIds);
    }

    public static async Task<IEnumerable<CatalogContainer>> GetChildCatalogContainersAsync(
        this CatalogContainer parent,
        CatalogContainer root,
        CancellationToken cancellationToken)
    {
        var childCatalogContainers = await parent.GetRegisteredChildCatalogContainersAsync(cancellationToken);
        var resolvedChildCatalogContainers = new List<CatalogContainer>();

        foreach (var childCatalogContainer in childCatalogContainers)
        {
            if (childCatalogContainer.LinkTarget is null)
            {
                resolvedChildCatalogContainers.Add(childCatalogContainer);
                continue;
            }

            var resolvedChildCatalogContainer = await root.TryResolveCatalogContainerAsync(
                root,
                childCatalogContainer.Id,
                cancellationToken
            );

            if (resolvedChildCatalogContainer is not null)
                resolvedChildCatalogContainers.Add(resolvedChildCatalogContainer);
        }

        return resolvedChildCatalogContainers;
    }

    private static async Task<(bool Success, HashSet<string>? VisitedLinkIds)> TryResolveLinkTargetAsync(
        CatalogContainer catalogContainer,
        CatalogContainer root,
        string catalogId,
        CancellationToken cancellationToken,
        int recursionCounter,
        HashSet<string>? visitedLinkIds)
    {
        if (catalogContainer.LinkTarget is null)
            return (true, visitedLinkIds);

        if (recursionCounter >= 10)
        {
            catalogContainer.Logger?.LogWarning(
                "Soft-link resolution stopped at catalog {CatalogId} after reaching the maximum recursion depth while resolving {CatalogPath}.",
                catalogContainer.Id,
                catalogId);

            return (false, visitedLinkIds);
        }

        visitedLinkIds ??= new(StringComparer.OrdinalIgnoreCase);

        if (!visitedLinkIds.Add(catalogContainer.Id))
        {
            catalogContainer.Logger?.LogWarning(
                "Soft-link resolution stopped after detecting a cycle at catalog {CatalogId} while resolving {CatalogPath}.",
                catalogContainer.Id,
                catalogId);

            return (false, visitedLinkIds);
        }

        if (!catalogContainer.ShouldResolveLinkTarget)
            return (true, visitedLinkIds);

        var target = await root.TryResolveCatalogContainerAsync(
            root,
            catalogContainer.LinkTarget!,
            cancellationToken,
            recursionCounter + 1,
            visitedLinkIds
        );

        if (target is null)
            return (false, visitedLinkIds);

        catalogContainer.ResolveLinkTarget(target);
        return (true, visitedLinkIds);
    }
}
