# Configuration options

This page lists the Nexus server options that can be set in `settings.json`, an INI file, environment variables, or command-line arguments.

Nexus uses the ASP.NET Core configuration system. Nested configuration sections are written with `:` in JSON paths and with double underscores in environment variables.

For example, the JSON setting:

```json
{
  "Paths": {
    "Config": "/var/lib/nexus/config"
  }
}
```

is the same as this environment variable:

```bash
NEXUS_PATHS__CONFIG=/var/lib/nexus/config
```

## Loading order

Configuration is loaded in this order. Later sources override earlier sources:

1. `appsettings.json`
2. `appsettings.{ASPNETCORE_ENVIRONMENT}.json`
3. optional settings file selected by `NEXUS_PATHS__SETTINGS`
4. environment variables prefixed with `NEXUS_`
5. command-line arguments

If `NEXUS_PATHS__SETTINGS` is not set, Nexus uses the platform-specific default settings file:

- Linux: `~/.local/share/nexus/settings.json`
- Windows: `%LOCALAPPDATA%/nexus/settings.json`

This means that on a default Linux installation, users can put their `settings.json` at:

```text
/home/<user>/.local/share/nexus/settings.json
```

On Windows, users can put their `settings.json` at:

```text
%LOCALAPPDATA%\nexus\settings.json
```

The settings file can be JSON or INI. The file extension decides which parser is used.

## Environment variable naming

Use these rules:

- prefix Nexus options with `NEXUS_`,
- replace `:` with `__` for nested sections,
- use the same option names as in JSON.

Examples:

```bash
NEXUS_GENERAL__APPLICATIONNAME="Production Nexus"
NEXUS_PATHS__CONFIG=/var/lib/nexus/config
NEXUS_DATA__VISUALIZATION__ENABLESERVERSIDEDECIMATION=true
NEXUS_GIT__REMOTEURL=https://example.invalid/nexus-config.git
NEXUS_SECURITY__ADMINISTRATORGROUP=nexus-admins
```

## Example settings.json

```json
{
  "General": {
    "ApplicationName": "Production Nexus",
    "HelpLink": "https://example.org/nexus-help",
    "DisableSampleCatalog": true
  },
  "Paths": {
    "Config": "/var/lib/nexus/config",
    "Cache": "/var/cache/nexus",
    "Catalogs": "/var/lib/nexus/catalogs",
    "Artifacts": "/var/lib/nexus/artifacts",
    "Packages": "/var/lib/nexus/packages"
  },
  "Data": {
    "TotalBufferMemoryConsumption": 1073741824,
    "AggregationNaNThreshold": 0.99,
    "Visualization": {
      "EnableServerSideDecimation": true,
      "MaxConcurrentReads": 8,
      "MaxComputeWorkers": 2,
      "MaxConcurrentRequests": 4,
      "MaxAggregatePoints": 262144,
      "MaxDatasetCacheBytes": 1073741824,
      "MaxTotalCacheBytes": 1073741824,
      "CacheTtl": "00:10:00"
    }
  },
  "Git": {
    "Branch": "main",
    "CommitThrottleSeconds": 300,
    "RemoteUrl": "https://example.invalid/nexus-config.git",
    "CommitAuthorName": "Nexus",
    "CommitAuthorEmail": "nexus@localhost"
  },
  "Security": {
    "UserHeader": "X-Forwarded-User",
    "NameHeader": "X-Forwarded-Preferred-Username",
    "GroupsHeader": "X-Forwarded-Groups",
    "AdministratorGroup": "admins"
  }
}
```

## General

Options under `General` control basic UI and sample-catalog behavior.

| JSON path | Environment variable | Type | Default | Description |
| --- | --- | --- | --- | --- |
| `General:ApplicationName` | `NEXUS_GENERAL__APPLICATIONNAME` | string | empty | Optional application name shown by the server/UI. |
| `General:HelpLink` | `NEXUS_GENERAL__HELPLINK` | string | empty | Optional help/documentation link exposed by the server/UI. |
| `General:DisableSampleCatalog` | `NEXUS_GENERAL__DISABLESAMPLECATALOG` | boolean | `false` | Disables the built-in sample catalog. |

## Paths

Options under `Paths` control where Nexus stores configuration, caches, catalog attachments, export artifacts, and restored extension packages.

The defaults are based on a platform-specific Nexus root:

- Linux root: `~/.local/share/nexus`
- Windows root: `%LOCALAPPDATA%/nexus`

| JSON path | Environment variable | Type | Default | Description |
| --- | --- | --- | --- | --- |
| `Paths:Config` | `NEXUS_PATHS__CONFIG` | string | `{root}/config` | Administrative configuration directory. This is the directory tracked by Git history. |
| `Paths:Cache` | `NEXUS_PATHS__CACHE` | string | `{root}/cache` | Cache directory. |
| `Paths:Catalogs` | `NEXUS_PATHS__CATALOGS` | string | `{root}/catalogs` | Directory for catalog-related stored files. |
| `Paths:Artifacts` | `NEXUS_PATHS__ARTIFACTS` | string | `{root}/artifacts` | Directory for generated export artifacts. |
| `Paths:Packages` | `NEXUS_PATHS__PACKAGES` | string | `{root}/packages` | Directory for restored extension packages. |
| special: settings file path | `NEXUS_PATHS__SETTINGS` | string | platform default settings path | Selects the optional JSON or INI settings file loaded before environment variables. This is read before `Paths` is bound and is not stored as a normal `Paths` option. |

## Data

Options under `Data` control data reading, aggregation, cache behavior, and visualization-related memory limits.

| JSON path | Environment variable | Type | Default | Description |
| --- | --- | --- | --- | --- |
| `Data:CachePattern` | `NEXUS_DATA__CACHEPATTERN` | string | empty | Optional cache pattern used by data/cache services. |
| `Data:TotalBufferMemoryConsumption` | `NEXUS_DATA__TOTALBUFFERMEMORYCONSUMPTION` | integer bytes | `1073741824` | Total buffer memory budget, default 1 GiB. |
| `Data:AggregationNaNThreshold` | `NEXUS_DATA__AGGREGATIONNANTHRESHOLD` | number | `0.99` | Threshold used when aggregating data with missing/invalid values. |

## Data visualization

Options under `Data:Visualization` control server-side visualization decimation and its cache/scheduling limits.

| JSON path | Environment variable | Type | Default | Description |
| --- | --- | --- | --- | --- |
| `Data:Visualization:EnableServerSideDecimation` | `NEXUS_DATA__VISUALIZATION__ENABLESERVERSIDEDECIMATION` | boolean | `false` | Enables server-side visualization decimation and exposes the UI option to use it. |
| `Data:Visualization:MaxConcurrentReads` | `NEXUS_DATA__VISUALIZATION__MAXCONCURRENTREADS` | integer | `8` | Maximum number of source-read workers used by one visualization scan. |
| `Data:Visualization:MaxComputeWorkers` | `NEXUS_DATA__VISUALIZATION__MAXCOMPUTEWORKERS` | integer | `2` | Maximum number of CPU workers used for summary reduction, parent construction, and view projection. |
| `Data:Visualization:MaxConcurrentRequests` | `NEXUS_DATA__VISUALIZATION__MAXCONCURRENTREQUESTS` | integer | `4` | Maximum number of visualization requests admitted concurrently. |
| `Data:Visualization:MaxAggregatePoints` | `NEXUS_DATA__VISUALIZATION__MAXAGGREGATEPOINTS` | integer | `262144` | Maximum total number of projected visualization points accepted across all views and resources in one request. |
| `Data:Visualization:MaxDatasetCacheBytes` | `NEXUS_DATA__VISUALIZATION__MAXDATASETCACHEBYTES` | integer bytes | `1073741824` | Maximum retained visualization cache bytes for one dataset, including summaries and raw detail. |
| `Data:Visualization:MaxTotalCacheBytes` | `NEXUS_DATA__VISUALIZATION__MAXTOTALCACHEBYTES` | integer bytes | `1073741824` | Maximum retained visualization cache bytes shared by all datasets in this server process. |
| `Data:Visualization:CacheTtl` | `NEXUS_DATA__VISUALIZATION__CACHETTL` | duration | `00:10:00` | Time after last use before a visualization cache entry is eligible for expiration. |

Durations are parsed by .NET. In JSON, use strings such as `"00:10:00"` for ten minutes or `"1.00:00:00"` for one day.

## Git

Options under `Git` control Git-backed configuration history. See also [Git-backed configuration history](git-history.md).

| JSON path | Environment variable | Type | Default | Description |
| --- | --- | --- | --- | --- |
| `Git:Branch` | `NEXUS_GIT__BRANCH` | string | `main` | Branch used for the configuration repository. |
| `Git:CommitThrottleSeconds` | `NEXUS_GIT__COMMITTHROTTLESECONDS` | integer seconds | `300` | Delay used to group rapid configuration changes into one commit. |
| `Git:RemoteUrl` | `NEXUS_GIT__REMOTEURL` | string | empty | Optional remote repository URL for pushing configuration history. |
| `Git:Username` | `NEXUS_GIT__USERNAME` | string | empty | Optional username for remote authentication. |
| `Git:Token` | `NEXUS_GIT__TOKEN` | string | empty | Optional token/password for remote authentication. Treat this as a secret. |
| `Git:SshPrivateKey` | `NEXUS_GIT__SSHPRIVATEKEY` | string | empty | Optional SSH private key for remote authentication. Treat this as a secret. |
| `Git:CommitAuthorName` | `NEXUS_GIT__COMMITAUTHORNAME` | string | `Nexus` | Author name used for automatic commits. |
| `Git:CommitAuthorEmail` | `NEXUS_GIT__COMMITAUTHOREMAIL` | string | `nexus@localhost` | Author email used for automatic commits. |

## Security

Options under `Security` define which reverse-proxy headers Nexus reads for user identity, groups, and catalog permissions.

| JSON path | Environment variable | Type | Default | Description |
| --- | --- | --- | --- | --- |
| `Security:UserHeader` | `NEXUS_SECURITY__USERHEADER` | string | `X-Forwarded-User` | Header containing the authenticated user identifier. |
| `Security:NameHeader` | `NEXUS_SECURITY__NAMEHEADER` | string | `X-Forwarded-Preferred-Username` | Header containing the display/preferred user name. |
| `Security:GroupsHeader` | `NEXUS_SECURITY__GROUPSHEADER` | string | `X-Forwarded-Groups` | Header containing user groups. |
| `Security:AdministratorGroup` | `NEXUS_SECURITY__ADMINISTRATORGROUP` | string | `admins` | Group whose members receive administrator permissions. |
| `Security:EnabledCatalogsPattern` | `NEXUS_SECURITY__ENABLEDCATALOGSPATTERN` | string | empty | Default enabled-catalogs pattern. Empty means all catalogs. |
| `Security:EnabledCatalogsPatternHeader` | `NEXUS_SECURITY__ENABLEDCATALOGSPATTERNHEADER` | string | `X-Forwarded-EnabledCatalogsPattern` | Header that can provide the enabled-catalogs pattern for a request. |
| `Security:CanReadCatalogHeader` | `NEXUS_SECURITY__CANREADCATALOGHEADER` | string | `X-Forwarded-CanReadCatalog` | Header used for per-catalog read permissions. |
| `Security:CanWriteCatalogHeader` | `NEXUS_SECURITY__CANWRITECATALOGHEADER` | string | `X-Forwarded-CanWriteCatalog` | Header used for per-catalog write permissions. |
| `Security:CanReadCatalogGroupHeader` | `NEXUS_SECURITY__CANREADCATALOGGROUPHEADER` | string | `X-Forwarded-CanReadCatalogGroup` | Header used for group-based catalog read permissions. |
| `Security:CanWriteCatalogGroupHeader` | `NEXUS_SECURITY__CANWRITECATALOGGROUPHEADER` | string | `X-Forwarded-CanWriteCatalogGroup` | Header used for group-based catalog write permissions. |
| `Security:LogoutUrl` | `NEXUS_SECURITY__LOGOUTURL` | string | empty | Optional logout URL used by the UI. |

## Other application configuration

The same configuration system also sees standard ASP.NET Core and Serilog settings, such as `AllowedHosts` and `Serilog`. Those settings are not Nexus-specific options, but they can be provided in the same files or through environment variables according to the normal ASP.NET Core rules.
