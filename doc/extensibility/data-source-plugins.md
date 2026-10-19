# Data source plugins

A data source plugin implements the `IDataSource<T>` contract. It contributes catalog registrations, enriches catalogs with resources and metadata, reports time ranges and availability, and reads sample data when Nexus streams or exports a selection.

Reference pages:

- <xref:Nexus.Extensibility.IDataSource>
- <xref:Nexus.Extensibility.IDataSource`1>
- <xref:Nexus.Extensibility.SimpleDataSource`1>

## Contract overview

Important methods are:

- `SetContextAsync(...)`: receives the resource locator, source configuration, request configuration, and logger.
- `GetCatalogRegistrationsAsync(path, ...)`: returns child catalogs below a parent path.
- `EnrichCatalogAsync(catalog, ...)`: adds resources, representations, metadata, readme text, and other catalog details.
- `GetTimeRangeAsync(catalogId, ...)`: returns the covered time range.
- `GetAvailabilityAsync(catalogId, begin, end, ...)`: returns availability for a requested interval.
- `ReadAsync(begin, end, requests, readData, progress, ...)`: fills the requested data buffers.

## SimpleDataSource<T>

<xref:Nexus.Extensibility.SimpleDataSource`1> is a small convenience base class for very simple data sources or for sources with fully custom discovery and read logic. It is not meant to be the default base for every file-based plugin.

`SimpleDataSource<T>` handles the repetitive setup work:

- it implements `SetContextAsync(...)`,
- it stores the typed source context in `Context`,
- it stores the logger in `Logger`,
- it provides default implementations for optional behavior such as time range and availability.

The type parameter `T` is the source configuration type. Use `object?` if the source has no custom configuration yet. A plugin still overrides the methods that define its actual behavior, especially `GetCatalogRegistrationsAsync(...)`, `EnrichCatalogAsync(...)`, and `ReadAsync(...)`.

For regularly organized structured files, [`nexus-sources-structured-file`](https://github.com/nexus-main/nexus-sources-structured-file) is usually a better starting point. It provides a base for readers where files are arranged by time-based folder or file-name patterns. Derived sources implement the actual parsing, while the structured-file base handles file discovery, time-range slicing, file-source selection, offsets, and read-buffer metadata.

## Catalog registrations

Catalog registrations define where the plugin mounts data in the Nexus catalog tree. A plugin can return registrations only below the requested parent path. This allows large catalog trees to be discovered lazily instead of loading every child at startup.

For example, a source may return `/MEASUREMENTS/2026` below `/`, and later return `/MEASUREMENTS/2026/RUN_001` when Nexus asks for children below `/MEASUREMENTS/2026`.

## Catalog enrichment

`EnrichCatalogAsync` receives a catalog shell and returns a catalog with details. This is where the plugin adds resources and representations.

Resources usually represent signals or channels. Representations describe how the signal can be read, for example at a particular sample period and data type.

## Reading data

`ReadAsync` receives one or more `ReadRequest` objects. Each request contains a data buffer and a status buffer. The plugin fills the buffers for the requested time range.

The status buffer marks valid values. Invalid or missing values should be marked as invalid so Nexus can expose them as missing data instead of silently returning wrong values.

Nexus may call `ReadAsync` and `GetAvailabilityAsync` concurrently on the same data source instance. Implementations must therefore be thread-safe.

## Minimal data source shape

Most data source plugins follow this shape:

1. reference the language-specific extensibility package,
2. expose one class that implements the data source contract,
3. return catalog registrations for the part of the tree that was requested,
4. enrich a catalog with resources and representations,
5. fill the buffers in read requests.

The following C# example is intentionally small. It shows the structure, not production-quality parsing or buffering:

```csharp
using Nexus.DataModel;
using Nexus.Extensibility;

[ExtensionDescription(
    "Example data source",
    "https://github.com/nexus-contrib",
    "https://github.com/nexus-contrib/example-source")]
public sealed class ExampleSource : SimpleDataSource<object?>
{
    public override Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var registrations = path == "/"
            ? [new CatalogRegistration("/EXAMPLE", "Example measurements")]
            : [];

        return Task.FromResult(registrations);
    }

    public override Task<ResourceCatalog> EnrichCatalogAsync(
        ResourceCatalog catalog,
        CancellationToken cancellationToken)
    {
        var resource = new ResourceBuilder("voltage")
            .WithProperty("unit", "V")
            .AddRepresentation(new Representation(NexusDataType.Float32, TimeSpan.FromMilliseconds(1)))
            .Build();

        var enriched = new ResourceCatalogBuilder(catalog.Id)
            .AddResource(resource)
            .Build();

        return Task.FromResult(enriched);
    }

    public override async Task ReadAsync(
        DateTime begin,
        DateTime end,
        ReadRequest[] requests,
        ReadDataHandler readData,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        foreach (var request in requests)
        {
            request.Data.Span.Clear();
            request.Status.Span.Fill(0x01);
            await request.CompleteAsync();
        }

        progress.Report(1.0);
    }
}
```

This sample skips important production details, especially time ranges, availability, thread safety, source configuration, request configuration, and correct conversion into the requested data buffer.

## Completing resources early

Read requests can be completed individually by calling <xref:Nexus.Extensibility.ReadRequest.CompleteAsync>. This allows Nexus to stream one resource before the whole batch has finished. Use this when a source reads many resources and some are ready earlier than others.

## Configuration

The source configuration belongs to the data source pipeline and is controlled by administrators. Request configuration can be supplied per request, for example for user-specific credentials to an external system.

Do not store user passwords or long-lived secrets in catalog metadata. Use request configuration or a deployment-specific secret mechanism.
