# Background

Since data may be stored in very heterogeneous databases, Nexus implements an extensibility mechanism to support load data from custom data sources. Whenever data from a certain data source is requested, a `DataSourceController` is instantiated which wraps a data source instance that implements the `IDataSource` interface.

# IDataSource

The interface is defined as follows:

```cs
/* called right after instantiation to provide the source URL, parameters and a logger instance */
Task SetContextAsync(...);

/* called whenever Nexus needs the catalog registrations */
Task<string[]> GetCatalogRegistrationsAsync(...);

/* called the first time the catalog is accessed */
Task<ResourceCatalog> GetCatalogAsync(...);

/* called whenever the database time range is requested */
Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(...);

/* called whenever the data availability is requested */
Task<double> GetAvailabilityAsync(...);

/* called whenever data is requested */
Task ReadAsync(...);
```

# Life Cycle
`IDataSource` instances are short-lived to make them thread-safe and enable them to cache open connections or files handles but at the same time make them free all resources when they are disposed (1). 

When the database is reloaded, the user-defined data source registrations are used to instantiate `IDataSources` are instantiated and then asked to provided available catalog identifiers. The catalogs itself are lazy-loaded upon the first access.

When, for instance, a user later asks for the data availability of a catalog, the `IDataSource` is instantiated again.

A read operation may be triggered by either streaming or exporting of the data of one or multiple catalog items. Grouped by the corresponding `IDataSource`, all read requests first arrive in a static method called `ReadAsync`, which is located in the `DataSourceController` type. From there the method distributes the read requests to the actual `DataSourceController` instances which forward it to the wrapped IDataSource instance. To keep the memory consumption low, the controller may decide to reduce the time period per request and repeat the reading step until all data has been loaded.

The implementation may load requests sequentially or in parallel. It may call `CompleteAsync()` as each request is populated to stream that resource before `ReadAsync` returns; otherwise Nexus flushes it after `ReadAsync` returns.

(1) A `IDataSource` instance is disposed automatically by Nexus when it implements the `IDisposable` interface.

# Batch Streaming

The single-resource v1 endpoint remains unchanged:

```http
GET /api/v1/data
```

Current C# and Python high-level load methods use one v2 request:

```http
POST /api/v2/data
Content-Type: application/json
Accept: application/octet-stream
```

```json
{
  "begin": "2026-01-01T00:00:00.0000000Z",
  "end": "2026-01-01T01:00:00.0000000Z",
  "resourcePaths": [
    "/catalog/a/1_s",
    "/catalog/b/1_s"
  ]
}
```

The request accepts at most 100 unique resource paths with one common sample period. The response contains repeated binary frames:

| Field | Size | Encoding |
|---|---:|---|
| Resource index | 4 bytes | signed little-endian integer |
| Payload length | 4 bytes | signed little-endian integer |
| Payload | up to 64 KiB | little-endian `FLOAT64` values |

The resource index refers to the path's position in `resourcePaths`. End-of-stream marks successful completion; clients validate that every resource received the expected number of bytes.

The server creates one internal `Pipe` per resource and multiplexes them into one bounded output pipe. `ReadRequest.CompleteAsync()` allows a source to publish an individual resource before its complete batch returns. Reverse proxies should disable response buffering to preserve back-pressure.
