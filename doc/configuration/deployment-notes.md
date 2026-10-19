# Deployment notes

This page collects deployment-specific notes that are not part of the full option reference.

For the complete list of Nexus settings, default values, JSON paths, environment-variable names, and the default `settings.json` location, see [Configuration options](options.md).

## Reverse proxies

For streaming endpoints, disable response buffering in the reverse proxy. Buffering can cause the proxy to keep reading data after a downstream client has slowed down or disconnected. With buffering disabled, back-pressure and cancellation propagate correctly.

This is important for:

- data streaming,
- large exports,
- long-running client downloads,
- cancellation when a browser, notebook, or script disconnects.

## Authentication and authorization headers

Nexus can run behind a trusted authentication proxy. In that setup, the proxy authenticates the user and forwards identity and authorization information to Nexus through HTTP headers.

Only trust these headers when they are set by infrastructure you control. Do not expose a deployment where external clients can send or spoof these headers directly.

By default, Nexus listens to these headers:

| Purpose | Default header | Matching option |
| --- | --- | --- |
| User identity | `X-Forwarded-User` | `Security:UserHeader` |
| Display name | `X-Forwarded-Preferred-Username` | `Security:NameHeader` |
| User groups | `X-Forwarded-Groups` | `Security:GroupsHeader` |
| Enabled catalog pattern | `X-Forwarded-EnabledCatalogsPattern` | `Security:EnabledCatalogsPatternHeader` |
| Readable catalog pattern | `X-Forwarded-CanReadCatalog` | `Security:CanReadCatalogHeader` |
| Writable catalog pattern | `X-Forwarded-CanWriteCatalog` | `Security:CanWriteCatalogHeader` |
| Group-readable catalog pattern | `X-Forwarded-CanReadCatalogGroup` | `Security:CanReadCatalogGroupHeader` |
| Group-writable catalog pattern | `X-Forwarded-CanWriteCatalogGroup` | `Security:CanWriteCatalogGroupHeader` |

Nexus treats users as administrators when their forwarded group list contains the configured administrator group. The default administrator group is `admins` and is configured with `Security:AdministratorGroup`.

Catalog permission headers contain catalog path patterns. Deployments can use them to restrict which parts of the catalog tree a user may see, read, or write.

One common setup is to put Nexus behind Nginx and OAuth2 Proxy, with Keycloak as the identity provider:

```text
 +---------+                                +------------+
 | Browser |                                |  Keycloak  |
 +---------+                                +------------+
      |                                           ^
      |                                           |
      | HTTPS                                     | OIDC
      |                                           |
      v                                           v
 +---------+                               +--------------+
 |         |         auth_request          |              |
 |         |         /oauth2/auth          |              |
 |  Nginx  | ----------------------------> | OAuth2 Proxy |
 |         | <---------------------------- |              |
 |         |     user, groups, claims      |              |
 +---------+                               +--------------+
      |
      |
      |
      | adds trusted X-Forwarded-* headers
      |
      v
 +---------+
 |  Nexus  |
 +---------+
```

In this model, external clients can only reach Nexus through Nginx. OAuth2 Proxy validates the login session against Keycloak. Nginx then forwards only trusted identity and permission headers to Nexus, for example `X-Forwarded-User`, `X-Forwarded-Groups`, and the catalog permission headers listed above.

The exact mapping depends on the deployment. For example, Keycloak roles or groups can be mapped by OAuth2 Proxy or Nginx into `X-Forwarded-Groups`, while catalog-specific permissions can be provided through the `X-Forwarded-CanReadCatalog` and `X-Forwarded-CanWriteCatalog` headers.

## Persistent storage

Production deployments should use persistent storage for Nexus configuration and package state. At minimum, review the configured `Paths` values for:

- configuration,
- cache,
- catalogs,
- export artifacts,
- restored extension packages.

The configuration directory is especially important when Git-backed configuration history is enabled, because it is the directory that Nexus tracks and commits.
