# API clients

Nexus exposes the same data model through the web interface, REST API, and generated clients. Use a client when you want to automate data access, load time ranges into an analysis environment, or create export jobs from scripts.

## Client packages

| Environment | Package | Notes |
| --- | --- | --- |
| Python | [`nexus-api`](https://pypi.org/project/nexus-api/) | Synchronous and asynchronous clients for loading and exporting data. |
| C# / .NET | [`Nexus.Api`](https://www.nuget.org/packages/Nexus.Api) | Generated .NET client for applications, services, notebooks, and tests. |
| TypeScript | [`@apollo3zehn/nexus-api`](https://www.npmjs.com/package/@apollo3zehn/nexus-api) | ESM package for browser or server-side TypeScript code. |
| Matlab | repository scripts | Limited support through the Matlab examples in this repository. |

## Authentication

Scripts usually authenticate with a personal access token created in the Nexus web interface. Tokens can be restricted to the catalog paths they need.

Keep tokens outside source code. Prefer environment variables, local configuration files, or secret stores used by your execution environment.

## Streaming behavior

The current generated clients use the Nexus streaming APIs for large reads. This avoids first materializing the full response on the server. For deployments behind reverse proxies, disable response buffering on streaming endpoints so back-pressure and cancellation can propagate correctly.

## Precision

Clients can request 32-bit or 64-bit floating-point data where supported. Use 32-bit precision for visualization and exploratory work when it is sufficient; use 64-bit precision when analysis requires it.
