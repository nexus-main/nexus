# Extensibility

Nexus is extended with plugins. A plugin hides storage-specific details and exposes data through the same catalogs, resource paths, REST API, generated clients, and UI.

There are two main extension points:

- **Data sources** read catalogs and time-series samples from existing systems.
- **Data writers** export selected data into target formats.

The .NET extension contracts are published as [`Nexus.Extensibility`](https://www.nuget.org/packages/Nexus.Extensibility) on NuGet. The Python extension contracts are published as [`nexus-extensibility`](https://pypi.org/project/nexus-extensibility/) on PyPI and focus on data sources.

Community and project-specific plugins can be hosted under the [`nexus-contrib`](https://github.com/nexus-contrib) GitHub organization.

## When to write a plugin

Write a data source plugin when data should stay in its existing location but be visible in Nexus. Common cases are:

- vendor-specific files that require special readers,
- folder structures that encode project-specific metadata,
- databases,
- remote services or APIs,
- another Nexus instance.

Write a data writer when users need an export format that is not built in.

## User experience goal

A good plugin should make the original storage system disappear from the user's everyday workflow. Users should be able to browse catalogs, inspect resources, view time ranges, stream data, and export selections without knowing the details of the underlying file format or service API.

For a minimal source skeleton, see [Data source plugins](data-source-plugins.md#minimal-data-source-shape).
