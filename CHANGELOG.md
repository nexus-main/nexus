## v2.0.0-beta.43 - 2025-01-22
- Follow Apollo3zehn.PackageManagement changes

## v2.0.0-beta.42 - 2025-01-16
- Make Nexus.PackageManagement indepenent of Nexus.Extensibility

## v2.0.0-beta.41 - 2025-01-15
- Update Python libs

## v2.0.0-beta.40 - 2025-01-15
- Allow using claims other than `sub` to derive the user id (#183)

## v2.0.0-beta.39 - 2025-01-06
- The datetime pickers were not working properly.

## v2.0.0-beta.38 - 2025-01-05
- The behavior of the newly introduced data grid has been improved.

## v2.0.0-beta.37 - 2024-12-20
- Folders being mounted in containers will be precreated to avoid them being owned by root.

## v2.0.0-beta.36 - 2024-12-20
- Fixed incorrect delete pipeline API parameter 

## v2.0.0-beta.35 - 2024-12-19
- Fixed a bug that prevented catalogs plugins to be loaded properly

## v2.0.0-beta.34 - 2024-12-18
- Fix Dockerfile

## v2.0.0-beta.33 - 2024-12-17
- Fix publish process

## v2.0.0-beta.32 - 2024-12-17
- Switch to .NET 9
- Add support catalog soft links
- UI improvement: Use Mudblazor's data grid to show catalog resources.
- Make "local" package references compile the source code (like "git-tag")

## v2.0.0-beta.31 - 2024-10-30
- Add support for chained data sources (= pipelines)

## v2.0.0-beta.30 - 2024-03-28
- Personal Access Tokens can now also be granted administrator privileges.

## v2.0.0-beta.29 - 2024-03-18
- Fixed wrong `users` folder location.

## v2.0.0-beta.28 - 2024-03-16
- Fixed a bug where applications could not access the `/api/v1/jobs` endpoint using the new personal access tokens.

## v2.0.0-beta.27 - 2024-03-16
- Fix faulty personal access token creation.

## v2.0.0-beta.26 - 2024-03-16
- Fix equality handling of `CatalogItem`.

### Bugs fixed:
- Fixed wrong Docker base image.

## v2.0.0-beta.25 - 2024-03-15

### Bugs fixed:
- Fixed wrong Docker base image.

## v2.0.0-beta.24 - 2024-03-15

### Bugs fixed:
- Writer addons did not write all resources to the file(s).

## v2.0.0-beta.23 - 2024-02-29

### Bugs fixed:
- Do not store all tokens in cookie to solve the "Missing parameters: id_token_hint" error because that makes the cookie very large (> 8 kB). Now only the `id_token` is stored there.

## v2.0.0-beta.22 - 2024-02-28

### Bugs fixed:
- Fix log out error "Missing parameters: id_token_hint".

## v2.0.0-beta.21 - 2023-09-29

### Features:
- Simplify containerization.

## v2.0.0-beta.20 - 2023-09-29

### Features:
- Simplify containerization.

## v2.0.0-beta.19 - 2023-09-26

### Bugs fixed:
- Fixed a bug where a buffer was accessed after free.

## v2.0.0-beta.18 - 2023-09-20

### Bugs fixed:
- Reverse multithreading changes, there was no bug luckily

## v2.0.0-beta.17 - 2023-09-18

### Bugs fixed:
- Fixed a bug in the mean-polar-degree algorithm which caused the result of be offset by a random value.

## v2.0.0-beta.16 - 2023-09-18

### Bugs fixed:
- Fixed a multithreading bug affecting the aggregation calculations. This bug very likely caused incorrect aggregation data probably for a long period of time for datasets with many NaN values.

## v2.0.0-beta.15 - 2023-07-13

### Bugs fixed:
- Git checkout also checks out submodules.

## v2.0.0-beta.14 - 2023-05-09

### Features:
- It is now possible to configure a default file type.

## v2.0.0-beta.13 - 2023-05-08

### Features:
- Show data writer descriptions in UI.

## v2.0.0-beta.12 - 2023-04-25

### Bugs Fixed:
- Add missing env variable.

## v2.0.0-beta.11 - 2023-04-25

### Bugs Fixed:
- The git tag provider now actually uses the specified tag :-/

## v2.0.0-beta.10 - 2023-04-25

### Features:
- The git tag provider now requires the user to specify the path of the .csproj file.

## v2.0.0-beta.9 - 2023-04-25

### Bugs fixed:
- The Dockerfile still used .NET 6.

## v2.0.0-beta.8 - 2023-04-24

### Features:
- .NET 7.
- MudBlazor as UI framework.

## v2.0.0-beta.7 - 2023-03-29

### Bugs fixed:
- Improve cancellation.

## v2.0.0-beta.6 - 2023-03-28

### Bugs fixed:
- Jobs were not cancelled properly.

## v2.0.0-beta.5 - 2023-03-21

### Bugs fixed:
- Updated the client to reflect the REST API changes.

## v2.0.0-beta.4 - 2023-03-21

### Bugs fixed:
- Fixed a bug that prevented normal users to delete their refresh token.

## v2.0.0-beta.3 - 2023-03-09

### Features added:
- Added a sync client for Python.

## v2.0.0-beta.2 - 2023-02-06

### Bugs fixed:
- Fixed a bug with incorrect dependency injection.

## v2.0.0-beta.1 - 2023-02-06

### Breaking change:
- There was an option to limit the memory consumption per request but there was no option to limit the total consumption of the buffers of all requests running in parallel. The old option `DataOptions.ReadChunkSize` is deprecated and has been replaced with `DataOptions.TotalBufferMemoryConsumption`. Nexus will possibly consume more than this value since it only limits the size of allocated buffers. Additionally, Nexus can't control the memory consumption of loaded extensions.

## v1.0.6 - 2023-01-31

### Features added:
- Fix missing environment variable in Dockerfile.

## v1.0.5 - 2023-01-31

### Features added:
- Nexus can now restore from pure git repositories and will compile it during the restore process.

## v1.0.4 - 2023-01-26

### Features added:
- Added the new claim `CanWriteCatalogGroup`.

## v1.0.3 - 2022-11-15

### Bugs Fixed:
    - Fixed a bug which caused access token lifetime to be very short (2 seconds).

## v1.0.2 - 2022-11-04

### Features added:
    - Enable exporting data without writing a zip file to allow efficient pre-aggregation.

## v1.0.1 - 2022-10-29

### Bugs fixed:
    - There was a hard coded listen address in the startup code.

## v1.0.0 - 2022-10-29

This is the first release of Nexus.