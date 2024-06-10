// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Extensibility;

namespace TestExtensionProject;

[ExtensionDescription("A data source for unit tests.", default!, default!)]
public class TestDataSource : IDataSource
{
    public Task SetContextAsync(DataSourceContext context, ILogger logger, CancellationToken cancellationToken)
    {
        throw new NotImplementedException(nameof(SetContextAsync));
    }

    public Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(string path, CancellationToken cancellationToken)
    {
        throw new NotImplementedException(nameof(GetCatalogAsync));
    }

    public Task<ResourceCatalog> GetCatalogAsync(string catalogId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException(nameof(GetCatalogAsync));
    }

    public Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException(nameof(GetTimeRangeAsync));
    }

    public Task<double> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken)
    {
        throw new NotImplementedException(nameof(GetAvailabilityAsync));
    }

    public Task ReadAsync(DateTime begin, DateTime end, ReadRequest[] requests, ReadDataHandler readData, IProgress<double> progress, CancellationToken cancellationToken)
    {
        throw new NotImplementedException(nameof(ReadAsync));
    }
}
