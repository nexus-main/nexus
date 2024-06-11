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
internal class Sample : IDataSource
{
    public static Guid RegistrationId = new("c2c724ab-9002-4879-9cd9-2147844bee96");

    private static readonly double[] DATA =
    [
        6.5,
        6.7,
        7.9,
        8.1,
        7.5,
        7.6,
        7.0,
        6.5,
        6.0,
        5.9,
        5.8,
        5.2,
        4.6,
        5.0,
        5.1,
        4.9,
        5.3,
        5.8,
        5.9,
        6.1,
        5.9,
        6.3,
        6.5,
        6.9,
        7.1,
        6.9,
        7.1,
        7.2,
        7.6,
        7.9,
        8.2,
        8.1,
        8.2,
        8.0,
        7.5,
        7.7,
        7.6,
        8.0,
        7.5,
        7.2,
        6.8,
        6.5,
        6.6,
        6.6,
        6.7,
        6.2,
        5.9,
        5.7,
        5.9,
        6.3,
        6.6,
        6.7,
        6.9,
        6.5,
        6.0,
        5.8,
        5.3,
        5.8,
        6.1,
        6.8
    ];

    public const string LocalCatalogId = "/SAMPLE/LOCAL";
    public const string RemoteCatalogId = "/SAMPLE/REMOTE";

    private const string LocalCatalogTitle = "Simulates a local catalog";
    private const string RemoteCatalogTitle = "Simulates a remote catalog";

    public const string RemoteUsername = "test";
    public const string RemotePassword = "1234";

    private DataSourceContext Context { get; set; } = default!;

    public Task SetContextAsync(
        DataSourceContext context,
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
            return Task.FromResult(new CatalogRegistration[]
                {
                    new(LocalCatalogId, LocalCatalogTitle),
                    new(RemoteCatalogId, RemoteCatalogTitle),
                });

        else
            return Task.FromResult(Array.Empty<CatalogRegistration>());
    }

    public Task<ResourceCatalog> GetCatalogAsync(
        string catalogId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Sample.LoadCatalog(catalogId));
    }

    public Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(
        string catalogId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult((DateTime.MinValue, DateTime.MaxValue));
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
            var (catalogItem, data, status) = request;

            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (catalog, resource, representation, parameters) = catalogItem;

                // check credentials
                if (catalog.Id == RemoteCatalogId)
                {
                    var user = Context.RequestConfiguration?.GetStringValue($"{typeof(Sample).FullName}/user");
                    var password = Context.RequestConfiguration?.GetStringValue($"{typeof(Sample).FullName}/password");

                    if (user != RemoteUsername || password != RemotePassword)
                        throw new Exception("The provided credentials are invalid.");
                }

                double[] dataDouble;

                var beginTime = ToUnixTimeStamp(begin);
                var endTime = ToUnixTimeStamp(end);
                var elementCount = data.Length / representation.ElementSize;

                // unit time
                if (resource.Id.Contains("unix_time"))
                {
                    var dt = representation.SamplePeriod.TotalSeconds;
                    dataDouble = Enumerable.Range(0, elementCount).Select(i => i * dt + beginTime).ToArray();
                }

                // temperature or wind speed
                else
                {
                    var offset = (long)beginTime;
                    var dataLength = DATA.Length;

                    dataDouble = new double[elementCount];

                    for (int i = 0; i < elementCount; i++)
                    {
                        dataDouble[i] = DATA[(offset + i) % dataLength];
                    }
                }

                MemoryMarshal
                    .AsBytes(dataDouble.AsSpan())
                    .CopyTo(data.Span);

                status.Span
                    .Fill(1);
            });
        }).ToList();

        var finishedTasks = 0;

        while (tasks.Count != 0)
        {
            var task = await Task.WhenAny(tasks);
            cancellationToken.ThrowIfCancellationRequested();

            if (task.Exception is not null && task.Exception.InnerException is not null)
                throw task.Exception.InnerException;

            finishedTasks++;
            progress.Report(finishedTasks / (double)requests.Length);
            tasks.Remove(task);
        }
    }

    internal static ResourceCatalog LoadCatalog(
        string catalogId)
    {
        var resourceBuilderA = new ResourceBuilder(id: "T1")
            .WithUnit("°C")
            .WithDescription("Test Resource A")
            .WithGroups("Group 1")
            .AddRepresentation(new Representation(dataType: NexusDataType.FLOAT64, samplePeriod: TimeSpan.FromSeconds(1)));

        var resourceBuilderB = new ResourceBuilder(id: "V1")
            .WithUnit("m/s")
            .WithDescription("Test Resource B")
            .WithGroups("Group 1")
            .AddRepresentation(new Representation(dataType: NexusDataType.FLOAT64, samplePeriod: TimeSpan.FromSeconds(1)));

        var resourceBuilderC = new ResourceBuilder(id: "unix_time1")
            .WithDescription("Test Resource C")
            .WithGroups("Group 2")
            .AddRepresentation(new Representation(dataType: NexusDataType.FLOAT64, samplePeriod: TimeSpan.FromMilliseconds(40)));

        var resourceBuilderD = new ResourceBuilder(id: "unix_time2")
            .WithDescription("Test Resource D")
            .WithGroups("Group 2")
            .AddRepresentation(new Representation(dataType: NexusDataType.FLOAT64, samplePeriod: TimeSpan.FromSeconds(1)));

        var catalogBuilder = new ResourceCatalogBuilder(catalogId);

        catalogBuilder.AddResources(new List<Resource>()
        {
            resourceBuilderA.Build(),
            resourceBuilderB.Build(),
            resourceBuilderC.Build(),
            resourceBuilderD.Build()
        });

        if (catalogId == RemoteCatalogId)
            catalogBuilder.WithReadme(
@"This catalog demonstrates how to access data sources that require additional credentials. These can be appended in the user settings menu (on the top right). In case of this example catalog, the JSON string to be added would look like the following:

```json
{
    ""Nexus.Sources.Sample"": {
        ""user"": ""test"",
        ""password"": ""1234""
    }
}
```

As soon as these credentials have been added, you should be granted full access to the data.
");

        return catalogBuilder.Build();
    }

    private static double ToUnixTimeStamp(
        DateTime value)
    {
        return value.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
    }
}
