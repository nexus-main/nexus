// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.DataModel;
using Nexus.Extensibility;
using System.Runtime.InteropServices;

namespace Nexus.Sources;

[ExtensionDescription(
    "Provides catalogs with sample data.",
    "https://github.com/nexus-main/nexus",
    "https://github.com/nexus-main/nexus/blob/master/src/Nexus/Extensions/Sources/Sample.cs")]
internal class Sample : IDataSource<object?>
{
    public static readonly Guid PipelineId = new("c2c724ab-9002-4879-9cd9-2147844bee96");

    private static readonly float[] DATA =
    [
        6.5f,
        6.7f,
        7.9f,
        8.1f,
        7.5f,
        7.6f,
        7.0f,
        6.5f,
        6.0f,
        5.9f,
        5.8f,
        5.2f,
        4.6f,
        5.0f,
        5.1f,
        4.9f,
        5.3f,
        5.8f,
        5.9f,
        6.1f,
        5.9f,
        6.3f,
        6.5f,
        6.9f,
        7.1f,
        6.9f,
        7.1f,
        7.2f,
        7.6f,
        7.9f,
        8.2f,
        8.1f,
        8.2f,
        8.0f,
        7.5f,
        7.7f,
        7.6f,
        8.0f,
        7.5f,
        7.2f,
        6.8f,
        6.5f,
        6.6f,
        6.6f,
        6.7f,
        6.2f,
        5.9f,
        5.7f,
        5.9f,
        6.3f,
        6.6f,
        6.7f,
        6.9f,
        6.5f,
        6.0f,
        5.8f,
        5.3f,
        5.8f,
        6.1f,
        6.8f
    ];

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
            return Task.FromResult(new CatalogRegistration[]
            {
                new(LocalCatalogId, LocalCatalogTitle),
                new(RemoteCatalogId, RemoteCatalogTitle)
            });
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

    public async Task ReadAsync(
        DateTime begin,
        DateTime end,
        ReadRequest[] requests,
        ReadDataHandler readData,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        var tasks = requests.Select(request =>
        {
            var catalogItem = request.CatalogItem;
            var data = request.Data;
            var status = request.Status;

            return Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var catalog = catalogItem.Catalog;
                var resource = catalogItem.Resource;
                var representation = catalogItem.Representation;

                // check credentials
                if (catalog.Id == RemoteCatalogId)
                {
                    var user = Context.RequestConfiguration?.GetStringValue(typeof(Sample).FullName!, "user");
                    var password = Context.RequestConfiguration?.GetStringValue(typeof(Sample).FullName!, "password");

                    if (user != RemoteUsername || password != RemotePassword)
                        throw new Exception("The provided credentials are invalid.");
                }

                double[] dataFloat64;
                float[] dataFloat32;

                var beginTime = ToUnixTimeStamp(begin);
                var elementCount = data.Length / representation.ElementSize;

                // unix time
                if (resource.Id.Contains("unix_time"))
                {
                    var dt = representation.SamplePeriod.TotalSeconds;
                    dataFloat64 = Enumerable.Range(0, elementCount).Select(i => i * dt + beginTime).ToArray();

                    MemoryMarshal
                    .AsBytes(dataFloat64.AsSpan())
                    .CopyTo(data.Span);
                }

                // temperature or wind speed
                else
                {
                    var offset = (long)beginTime;
                    var dataLength = DATA.Length;

                    dataFloat32 = new float[elementCount];

                    for (int i = 0; i < elementCount; i++)
                    {
                        dataFloat32[i] = DATA[(offset + i) % dataLength];
                    }

                    MemoryMarshal
                        .AsBytes(dataFloat32.AsSpan())
                        .CopyTo(data.Span);
                }

                status.Span
                    .Fill(1);

                await request.CompleteAsync();
            });
        }).ToList();

        try
        {
            var finishedTasks = 0;

            while (tasks.Count != 0)
            {
                var task = await Task.WhenAny(tasks);
                await task;
                finishedTasks++;
                progress.Report(finishedTasks / (double)requests.Length);
                tasks.Remove(task);
            }
        }
        finally
        {
            try
            {
                await Task.WhenAll(tasks);
            }
            catch
            {
                // Preserve the first failure after every worker has unwound.
            }
        }
    }

    internal static ResourceCatalog LoadCatalog(
        string catalogId)
    {
        var resourceA = new ResourceBuilder(id: "T1")
            .WithUnit("°C")
            .WithDescription("Test Resource A")
            .WithGroups("Group 1")
            .AddRepresentation(new Representation(dataType: NexusDataType.FLOAT32, samplePeriod: TimeSpan.FromSeconds(1)))
            .Build();

        var resourceB = new ResourceBuilder(id: "V1")
            .WithUnit("m/s")
            .WithDescription("Test Resource B")
            .WithGroups("Group 1")
            .AddRepresentation(new Representation(dataType: NexusDataType.FLOAT32, samplePeriod: TimeSpan.FromSeconds(1)))
            .Build();

        var resourceC = new ResourceBuilder(id: "unix_time1")
            .WithDescription("Test Resource C")
            .WithGroups("Group 2")
            .AddRepresentation(new Representation(dataType: NexusDataType.FLOAT64, samplePeriod: TimeSpan.FromMilliseconds(40)))
            .Build();

        var resourceD = new ResourceBuilder(id: "unix_time2")
            .WithDescription("Test Resource D")
            .WithGroups("Group 2")
            .AddRepresentation(new Representation(dataType: NexusDataType.FLOAT64, samplePeriod: TimeSpan.FromSeconds(1)))
            .Build();

        var catalogBuilder = new ResourceCatalogBuilder(catalogId);

        catalogBuilder.AddResources(new List<Resource>()
        {
            resourceA,
            resourceB,
            resourceC,
            resourceD
        });

        if (catalogId == RemoteCatalogId)
            catalogBuilder.WithReadme(
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
""");

        return catalogBuilder.Build();
    }

    private static double ToUnixTimeStamp(
        DateTime value)
    {
        return value.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
    }
}
