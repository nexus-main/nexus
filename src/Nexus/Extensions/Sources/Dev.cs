// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.DataModel;
using Nexus.Extensibility;

namespace Nexus.Sources;

[ExtensionDescription(
    "Provides development catalogs for testing.",
    "https://github.com/nexus-main/nexus",
    "https://github.com/nexus-main/nexus/blob/master/src/Nexus/Extensions/Sources/Dev.cs")]
internal class Dev : IDataSource<object?>
{
    public static readonly Guid PipelineId = new("d6a1f4e2-3b5c-4a7d-8e9f-0a1b2c3d4e5f");

    public const string LicensedCatalogId = "/DEV/LICENSED";

    public const string RangeLimitedCatalogId = "/DEV/RANGE_LIMITED";

    public const string AliasRangeCatalogId = "/DEV/ALIAS_RANGE";

    public const string AliasToAliasCatalogId = "/DEV/ALIAS_TO_ALIAS";

    public const string SourceWithChildrenCatalogId = "/DEV/SOURCE_WITH_CHILDREN";

    public const string AliasWithChildrenCatalogId = "/DEV/ALIAS_WITH_CHILDREN";

    public const string ChildACatalogId = "/DEV/SOURCE_WITH_CHILDREN/CHILD_A";

    public const string ChildBCatalogId = "/DEV/SOURCE_WITH_CHILDREN/CHILD_B";

    private const string LicensedCatalogTitle = "Simulates a licensed catalog";

    private const string RangeLimitedCatalogTitle = "Simulates a catalog with a limited registration time range";

    private const string AliasRangeCatalogTitle = "Simulates an alias with a limited registration time range";

    private const string AliasToAliasCatalogTitle = "Simulates an alias to another alias";

    private const string SourceWithChildrenCatalogTitle = "Simulates a catalog that contains child catalogs";

    private const string AliasWithChildrenCatalogTitle = "Simulates an alias to a catalog that contains child catalogs";

    private const string ChildACatalogTitle = "Simulates child catalog A";

    private const string ChildBCatalogTitle = "Simulates child catalog B";

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
            if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Development")
                return Task.FromResult(Array.Empty<CatalogRegistration>());

            var registrations = new List<CatalogRegistration>()
            {
                new(LicensedCatalogId, LicensedCatalogTitle),

                new(RangeLimitedCatalogId, RangeLimitedCatalogTitle,
                    MinBegin: new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc),
                    MaxEnd: new DateTime(2020, 02, 01, 0, 0, 0, DateTimeKind.Utc)),

                new(AliasRangeCatalogId, AliasRangeCatalogTitle,
                    LinkTarget: RangeLimitedCatalogId,
                    MinBegin: new DateTime(2020, 01, 15, 0, 0, 0, DateTimeKind.Utc),
                    MaxEnd: new DateTime(2020, 03, 01, 0, 0, 0, DateTimeKind.Utc)),

                new(AliasToAliasCatalogId, AliasToAliasCatalogTitle,
                    LinkTarget: AliasRangeCatalogId,
                    MinBegin: new DateTime(2020, 01, 20, 0, 0, 0, DateTimeKind.Utc),
                    MaxEnd: new DateTime(2020, 02, 15, 0, 0, 0, DateTimeKind.Utc)),

                new(SourceWithChildrenCatalogId, SourceWithChildrenCatalogTitle),

                new(AliasWithChildrenCatalogId, AliasWithChildrenCatalogTitle,
                    LinkTarget: SourceWithChildrenCatalogId)
            };

            return Task.FromResult(registrations.ToArray());
        }

        else if (path == SourceWithChildrenCatalogId + "/")
        {
            var registrations = new List<CatalogRegistration>()
            {
                new(ChildACatalogId, ChildACatalogTitle),
                new(ChildBCatalogId, ChildBCatalogTitle)
            };

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
        return SampleDataHelper.ReadAsync(begin, requests, progress, cancellationToken);
    }

    internal static ResourceCatalog LoadCatalog(
        string catalogId)
    {
        if (catalogId == SourceWithChildrenCatalogId)
        {
            return new ResourceCatalogBuilder(catalogId).Build();
        }

        if (catalogId == ChildACatalogId)
        {
            var resource = new ResourceBuilder(id: "T1")
                .WithUnit("°C")
                .WithDescription("Child Resource A")
                .AddRepresentation(new Representation(dataType: NexusDataType.Float32, samplePeriod: TimeSpan.FromSeconds(1)))
                .Build();

            return new ResourceCatalogBuilder(catalogId)
                .AddResources([resource])
                .Build();
        }

        if (catalogId == ChildBCatalogId)
        {
            var resource = new ResourceBuilder(id: "V1")
                .WithUnit("m/s")
                .WithDescription("Child Resource B")
                .AddRepresentation(new Representation(dataType: NexusDataType.Float32, samplePeriod: TimeSpan.FromSeconds(1)))
                .Build();

            return new ResourceCatalogBuilder(catalogId)
                .AddResources([resource])
                .Build();
        }

        return SampleDataHelper.BuildDefaultCatalog(catalogId);
    }
}
