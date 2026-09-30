# Sample Catalog

Nexus ships with a built-in sample catalog (`/SAMPLE/LOCAL`, `/SAMPLE/REMOTE`) provided by the `Sample` data source. It is hardcoded as a builtin pipeline in `CatalogManager` and is always present at the root unless explicitly disabled.

The sample catalog is required for the bundled samples (C#, Matlab, Python) to work, because the samples reference it as their data source. Disabling the sample catalog causes all sample scripts to fail with a not-found error.

## Disabling

Set `DisableSampleCatalog` to `true` in the `General` options section to disable the sample catalog:

```json
{
  "General": {
    "DisableSampleCatalog": true
  }
}
```

Or via environment variable:

```
NEXUS_GENERAL__DISABLESAMPLECATALOG=true
```

The default is `false` (sample catalog enabled).

When disabled, the sample catalog container is never enumerated. It is hidden from catalog listings and direct access returns 404, not just filtered from the UI.

## Why not `EnabledCatalogsPattern`

`EnabledCatalogsPattern` is a security/access-control mechanism sourced from a reverse proxy header (`X-Forwarded-EnabledCatalogsPattern`). It is not a visibility toggle:

- Administrators bypass the pattern check.
- The sample catalog has implicit read access that is granted before the enabled-catalogs check runs.

Using `EnabledCatalogsPattern` to hide the sample catalog would not prevent direct access by administrators or by anyone who has the implicit sample access claim. `DisableSampleCatalog` is a general application behavior toggle that removes the catalog at the `CatalogManager` level instead.
