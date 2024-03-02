## Catalog Permissions

A catalog can be readable or writable.

- A user that can read a catalog has access to the catalog itself as well as attachments but cannot modify anything.
  - A catalog is readable if `isAdmin` || `isOwner` || `canReadCatalog` || `canReadCatalogGroup` || `implicitAccess`
- A user with write permissions to a certain catalog can create or delete attachments and modify the metadata of that catalog.
  - A catalog is writable if `isAdmin` || `canWriteCatalog` || `canWriteCatalogGroup`

## Catalog Release Status
- A data source registration has a field to specify a regex pattern to find catalogs to be released. The default pattern will match all catalogs of a data source. To become actually released, it is required that the requesting user has write permissions for the corresponding catalogs.
- A catalog that is not released should not show up in the UI for all users except the owner of that catalog.
- Non-released catalogs can be interacted with like any other catalog (e.g. via the API) as long as the user has read or write access to it. It is the own risk of the user to interact with non-released catalogs as they may not be fully prepared yet.

## Catalog Visibility
- A data source registration has a field to specify a regex pattern to find catalogs to be made visible. The default pattern will match all catalogs of a data source. To become actually visible, it is required that the requesting user has read permissions for the corresponding catalogs.
- A catalog that is not visible should not show up in the UI for all users except the owner of that catalog.
- Non-visible catalogs can be interacted with like any other catalog (e.g. via the API) as long as the user has read or write access to it. This is just a convenience parameter to allow cleaning up the catalog hierarchy.

