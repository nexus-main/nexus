# C# / .NET client

The .NET client package is published as [`Nexus.Api`](https://www.nuget.org/packages/Nexus.Api).

```bash
dotnet add package Nexus.Api
```

The client is generated from the Nexus OpenAPI description and contains models for both browsing and data access. It is useful for services, command-line tools, .NET notebooks, and automated tests.

## Samples

- [Load sample](https://github.com/nexus-main/nexus/blob/dev/samples/C%23/sample_load.dib)
- [Export sample](https://github.com/nexus-main/nexus/blob/dev/samples/C%23/sample_export.ipynb)

## Typical usage

Use the generated client when you need typed access to catalogs, resource metadata, export jobs, and streamed time-series data. The exact generated method names can change with the API surface, so prefer the package version that matches the Nexus server version.

## Version matching

The generated client is built from the API description in this repository. When the server API changes, the repository regenerates and tests the clients in CI. For production deployments, keep server and client versions aligned.
