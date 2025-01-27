// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Extensibility;

namespace DataSource;

[ExtensionDescription(
    "Augments existing catalogs with more awesome data.",
    "https://github.com/nexus-main/nexus",
    "https://github.com/nexus-main/nexus/blob/master/tests/Nexus.Tests/DataSource/TestSource.cs")]
public class TestSource : IDataSource
{
    public const string LocalCatalogId = "/SAMPLE/LOCAL";

    public Task SetContextAsync(
        DataSourceContext context,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        return Task.CompletedTask;
    }

    public Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(string path, CancellationToken cancellationToken)
    {
        return Task.FromResult(Array.Empty<CatalogRegistration>());
    }

    public Task<ResourceCatalog> EnrichCatalogAsync(
        ResourceCatalog catalog,
        CancellationToken cancellationToken
    )
    {
        if (catalog.Resources is null)
            return Task.FromResult(catalog);

        var representation = new Representation(NexusDataType.UINT8, TimeSpan.FromSeconds(1));
        var resource = new Resource(id: "foo", representations: [representation]);

        var newCatalog = catalog with
        {
            Resources = [
                catalog.Resources[0],
                catalog.Resources[2],
                catalog.Resources[3],

                /* test modifying */
                catalog.Resources[1] with { Id = "V1_renamed" }, 

                /* test adding */
                resource
            ]
        };

        return Task.FromResult(newCatalog);
    }

    public Task<double> GetAvailabilityAsync(
        string catalogId,
        DateTime begin,
        DateTime end,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult(double.NaN);
    }

    public Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(
        string catalogId,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult((DateTime.MaxValue, DateTime.MinValue));
    }

    public Task ReadAsync(
        DateTime begin,
        DateTime end,
        ReadRequest[] requests,
        ReadDataHandler readData,
        IProgress<double> progress,
        CancellationToken cancellationToken
    )
    {
        foreach (var request in requests)
        {
            request.Data.Span.Fill(1);
            request.Status.Span.Fill(1);
        }

        return Task.CompletedTask;
    }
}