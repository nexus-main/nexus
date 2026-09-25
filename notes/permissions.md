## Catalog Permissions

A catalog can be readable or writable.

- A user that can read a catalog has access to catalog details, resources, metadata, and attachments, but cannot modify anything.
- A catalog is readable if the configured read rules allow access, for example administrator access, direct `CanReadCatalog` claims, `CanReadCatalogGroup` claims, or implicit access where supported.
- A user with write permissions to a catalog can create or delete attachments and modify the metadata of that catalog.
- A catalog is writable if the configured write rules allow access, for example administrator access, direct `CanWriteCatalog` claims, or `CanWriteCatalogGroup` claims.

Read and write permissions are enforced by the API. UI visibility does not grant access to protected catalog details or data.

## Catalog Visibility

A data source pipeline has an optional `VisibilityPattern` regex that controls whether matching catalogs appear in the UI. `VisibilityPattern = null` or an empty pattern means all catalogs from that pipeline are visible by default.

- Readable catalogs are always visible to users that can access them.
- Visible but unreadable catalogs may appear in the catalog tree as restricted entries.
- Restricted entries can show catalog metadata such as title, readme, and contact person, but selecting them must not load protected catalog details or data.
- When contact information is available, the UI should show it so users know whom to ask for access.
- Non-visible catalogs should not appear in the UI unless a future owner/manageability signal explicitly allows showing them for diagnostics.
- Users with direct API access can interact with non-visible catalogs only if the corresponding read or write permissions allow it.

`ReleasePattern` and released/unreleased catalog status were removed for now. Readiness or publishing gates can be reintroduced later as a separate concept if needed.
