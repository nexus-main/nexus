*Note:* This needs to be revised when API Versioning is implemented (see "Super issue").

Nexus offers two interfaces: one to support extensions and one for the REST API. Thus, the following application parts should be versioned:

- (1) Nexus application ([SemVer](https://semver.org/))
- (2) Extension interface (single number)
- (3) REST API (single number)

In case that the version of (2) or (3) increased, the major version of (1) is also increased.

#### How does the version affect (2)?
The extension interface version is not part of the interface name or any other class. Instead, the current version is documented in the table below. When an assembly with incompatible extensions is loaded (because the interface definition changed), a `TypeLoadException` is thrown and the assembly is skipped. The exception will be catched and the error logged.

#### How does the version affect (3)?
The REST API version is part of the URL, i.e. `/api/v{version}`. The API documentation is located under `/api/v{version}/docs`. This ensures that normal users of Nexus have a stable interface which may be deprecated only in the long term.

#### Current versions:
| Name                | Version |
|---------------------|---------|
| Application         | 1.0.0   |
| Extension interface | 1       |
| REST API            | 2       |

#### Data Stream Versioning

The v1 data stream endpoint remains unchanged:

```http
GET /api/v1/data
```

The v1 single-resource raw stream contract remains for compatibility. Current C# and Python high-level load methods use the v2 batch contract.

The v2 batch stream contract is:

```http
POST /api/v2/data/streams/batch
GET /api/v2/data/streams/batch/{sessionId}/channel/{channelId}
```

The hosted UI and current C# and Python high-level load methods use v2. Legacy or low-level clients may continue using v1.
