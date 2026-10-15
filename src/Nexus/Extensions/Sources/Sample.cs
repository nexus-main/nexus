// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.DataModel;
using Nexus.Extensibility;

namespace Nexus.Sources;

[ExtensionDescription(
    "Provides catalogs with sample data.",
    "https://github.com/nexus-main/nexus",
    "https://github.com/nexus-main/nexus/blob/master/src/Nexus/Extensions/Sources/Sample.cs")]
internal class Sample : IDataSource<object?>
{
    public static readonly Guid PipelineId = new("c2c724ab-9002-4879-9cd9-2147844bee96");

    public const string LocalCatalogId = "/SAMPLE/LOCAL";

    public const string RemoteCatalogId = "/SAMPLE/REMOTE";

    private const string LocalCatalogTitle = "Simulates a local catalog";

    private const string RemoteCatalogTitle = "Simulates a remote catalog";

    public const string RemoteUsername = "test";

    public const string RemotePassword = "1234";

    private DataSourceContext<object?> Context { get; set; } = default!;

    public Task SetContextAsync(
        DataSourceContext<object?> context,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        Context = context;

        return Task.CompletedTask;
    }

    public Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (path == "/")
        {
            var registrations = new List<CatalogRegistration>()
            {
                new(LocalCatalogId, LocalCatalogTitle)
            };

            if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
            {
                registrations.Add(new(RemoteCatalogId, RemoteCatalogTitle));
            }

            return Task.FromResult(registrations.ToArray());
        }

        else
        {
            return Task.FromResult(Array.Empty<CatalogRegistration>());
        }
    }

    public Task<ResourceCatalog> EnrichCatalogAsync(
        ResourceCatalog catalog,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(catalog.Merge(LoadCatalog(catalog.Id)));
    }

    public Task<CatalogTimeRange> GetTimeRangeAsync(
        string catalogId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new CatalogTimeRange(DateTime.MinValue, DateTime.MaxValue));
    }

    public Task<double> GetAvailabilityAsync(
        string catalogId,
        DateTime begin,
        DateTime end,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(1.0);
    }

    public Task ReadAsync(
        DateTime begin,
        DateTime end,
        ReadRequest[] requests,
        ReadDataHandler readData,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        return SampleDataHelper.ReadAsync(
            begin,
            requests,
            progress,
            cancellationToken,
            validateRequest: request =>
            {
                var catalog = request.CatalogItem.Catalog;

                if (catalog.Id == RemoteCatalogId)
                {
                    var user = Context.RequestConfiguration?.GetStringValue(typeof(Sample).FullName!, "user");
                    var password = Context.RequestConfiguration?.GetStringValue(typeof(Sample).FullName!, "password");

                    if (user != RemoteUsername || password != RemotePassword)
                        throw new Exception("The provided credentials are invalid.");
                }
            });
    }

    internal static ResourceCatalog LoadCatalog(
        string catalogId)
    {
        var catalog = SampleDataHelper.BuildDefaultCatalog(catalogId);

        if (catalogId == RemoteCatalogId)
        {
            var readmeCatalog = new ResourceCatalogBuilder(catalogId)
                .WithReadme(
"""
This catalog demonstrates how to access data sources that require additional credentials. These can be appended in the user settings menu (on the top right). In case of this example catalog, the JSON string to be added would look like the following:

```json
{
    "Nexus.Sources.Sample": {
        "user": "test",
        "password": "1234"
    }
}
```

As soon as these credentials have been added, you should be granted full access to the data.
""")
                .Build();

            catalog = catalog.Merge(readmeCatalog);
        }

        return catalog;
    }
}
