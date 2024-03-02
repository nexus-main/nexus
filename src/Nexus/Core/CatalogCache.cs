using Nexus.DataModel;
using System.Collections.Concurrent;

namespace Nexus.Core
{
    internal class CatalogCache : ConcurrentDictionary<InternalDataSourceRegistration, ConcurrentDictionary<string, ResourceCatalog>>
    {
        // This cache is required for DataSourceController.ReadAsync method to store original catalog items.
    }
}
