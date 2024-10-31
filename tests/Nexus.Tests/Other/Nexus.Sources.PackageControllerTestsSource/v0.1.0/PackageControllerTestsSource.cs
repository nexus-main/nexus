using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Extensibility;

namespace Nexus.Sources;

public class PackageControllerTestsSource : IDataSource
{
    public Task SetContextAsync(DataSourceContext context, ILogger logger, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(string path, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<ResourceCatalog> EnrichCatalogAsync(ResourceCatalog catalog, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<double> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task ReadAsync(DateTime begin, DateTime end, ReadRequest[] requests, ReadDataHandler readData, IProgress<double> progress, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}