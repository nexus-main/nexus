// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Core.V1;
using Nexus.DataModel;
using System.Collections.Concurrent;

namespace Nexus.Core;

internal class CatalogCache : ConcurrentDictionary<DataSourcePipeline, ConcurrentDictionary<string, ResourceCatalog>>
{
    // This cache is required for DataSourceController.ReadAsync method to store original catalog items.
}
