# Data source configuration

Data source configuration connects a data source plugin to a concrete source of data. The plugin defines how to read a type of source; the pipeline configuration tells Nexus which instance to use, where it is mounted, and which options it needs.

## Source types

Plugins can provide access to different source types, including:

- files in open or vendor-specific formats,
- databases,
- remote services and APIs,
- other Nexus instances,
- project- or organization-specific storage structures.

## Pipeline configuration

A data source pipeline usually contains:

- the selected source implementation,
- an optional resource locator such as a folder, URI, database endpoint, or service URL,
- source-specific configuration,
- enabled/disabled state,
- catalog registrations exposed by the source.

Exact configuration fields are source-specific. A file-based source may need a root folder and parsing options. A remote source may need a base URL and authentication configuration. A database-backed source may need connection information and mapping rules.

## Editing in the web UI

Data source pipelines are configured in the Nexus web UI. Administrators can open the data-source configuration area, select a pipeline, and edit its configuration with the built-in YAML editor.

The YAML editor is useful because the exact fields depend on the selected plugin. Nexus stores the resulting configuration as part of the server configuration, so changes can be reviewed through the Git-backed configuration history when that feature is enabled.

## Catalog mounting

Sources expose catalog registrations. These registrations determine where data appears in the Nexus tree. Choose catalog paths that users understand, for example project, campaign, location, or measurement-system oriented paths.

Avoid exposing implementation details such as server hostnames or temporary folder names unless those names are meaningful to users.

## User-specific request configuration

Some sources need user-specific settings or credentials. Nexus supports request configuration that can be passed with a request and merged with the source configuration. This is useful when an external system should authenticate as the current user instead of using one shared service credential.

The exact keys depend on the data source implementation.

## Operational guidance

- Start with one small catalog path and verify browsing, time ranges, visualization, and export.
- Check permissions before exposing a catalog broadly.
- Keep source configuration minimal and documented.
- Prefer source-side lazy loading for large catalog trees.
- Use Git history to review configuration changes.
