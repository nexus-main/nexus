# Plugin lifecycle

Nexus loads extension packages during startup. Administrators decide which package references are trusted and available. After package restore and loading, Nexus searches for implementations of the data source and data writer contracts.

## Startup

At startup Nexus:

1. restores configured package references,
2. loads package assemblies,
3. discovers data sources and writers,
4. instantiates configured data source pipelines,
5. asks sources for top-level catalog registrations.

Catalog details are loaded lazily. A source is usually asked for child catalogs or catalog content only when a user or API request needs it.

## Instance lifetime

Data source instances are short-lived enough to avoid global mutable state, but Nexus may reuse an instance for concurrent operations. Implementations must be thread-safe for availability and read calls.

If a .NET source implements `IDisposable`, Nexus disposes it when the instance is no longer needed.

## Errors

Fail clearly. If a source cannot read a requested file, database row, or remote object, throw an exception with enough context for administrators to diagnose the problem. Do not return fabricated values.

When only parts of a resource are unavailable, mark samples invalid where possible rather than failing the whole read.

## Performance

For large data sets:

- avoid loading full campaigns into memory,
- read only the requested time range,
- stream resources as soon as they are ready,
- cache expensive metadata carefully,
- keep file and network handles bounded,
- respect cancellation tokens.
