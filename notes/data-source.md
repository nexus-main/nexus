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

With the collection of read requests passed to the `IDataSource`, the implementation may decide to load the data sequentially or in parallel and when everything is read, to return the results all at once.

(1) A `IDataSource` instance is disposed automatically by Nexus when it implements the `IDisposable` interface.

# Batch Streaming

The public v2 data stream API preserves source batching for multiple resources. A client first registers a batch session with `POST /api/v2/data/streams/batch` and then opens one channel stream per returned channel at `GET /api/v2/data/streams/batch/{sessionId}/channel/{channelId}`.

The existing single-resource v1 endpoint remains unchanged:

```http
GET /api/v1/data
```

Existing C#, Python, and Matlab clients rely on this endpoint, so it must not be overloaded with batch semantics.

The v2 batch registration request is:

```http
POST /api/v2/data/streams/batch
Content-Type: application/json
Accept: application/json
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

The response contains one channel per resource path:

```json
{
  "sessionId": "00000000-0000-0000-0000-000000000000",
  "channels": [
    {
      "channelId": "00000000-0000-0000-0000-000000000001",
      "resourcePath": "/catalog/a/1_s"
    }
  ]
}
```

Each channel is streamed independently:

```http
GET /api/v2/data/streams/batch/{sessionId}/channel/{channelId}
Accept: application/octet-stream
```

Each channel response is the same raw double stream format used by the v1 endpoint and has an exact `Content-Length`.

The server creates one `Pipe` per requested resource and groups pipe writers by `CatalogContainer`, matching the export topology. The existing static `DataSourceController.ReadAsync(...)` method is still responsible for validation, memory-aware chunking, cache behavior, processing, and dispatching batched `ReadRequest[]` values to sources.

The source read starts only after all expected channel streams have attached. This prevents an unconsumed pipe from blocking the batch read before its HTTP response exists.

Because each resource uses a separate HTTP channel, the v2 batch streaming endpoints require HTTP/2. HTTP/1.1-only clients or reverse proxies can queue channel requests behind per-origin connection limits; if not all channels attach, the server will not start the source read and the batch can hang until timeout. Proxies must also avoid buffering the channel responses.
