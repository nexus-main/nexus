using Nexus.DataModel;

namespace Nexus.Core
{
    internal static class CatalogContainerExtensions
    {
        public static async Task<CatalogItemRequest?> TryFindAsync(
            this CatalogContainer parent,
            string resourcePath,
            CancellationToken cancellationToken)
        {
            if (!DataModelUtilities.TryParseResourcePath(resourcePath, out var parseResult))
                throw new Exception("The resource path is malformed.");

            // find catalog
            var catalogContainer = await parent.TryFindCatalogContainerAsync(parseResult.CatalogId, cancellationToken);

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
            string catalogId,
            CancellationToken cancellationToken)
        {
            var childCatalogContainers = await parent.GetChildCatalogContainersAsync(cancellationToken);
            var catalogIdWithTrailingSlash = catalogId + "/"; /* the slashes are important to correctly find /A/D/E2 in the tests */

            var catalogContainer = childCatalogContainers
                .FirstOrDefault(current => catalogIdWithTrailingSlash.StartsWith(current.Id + "/"));

            /* nothing found */
            if (catalogContainer is null)
                return default;

            /* catalogContainer is searched one */
            else if (catalogContainer.Id == catalogId)
                return catalogContainer;

            /* catalogContainer is (grand)-parent of searched one */
            else
                return await catalogContainer.TryFindCatalogContainerAsync(catalogId, cancellationToken);
        }
    }
}
