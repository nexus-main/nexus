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