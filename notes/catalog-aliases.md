# Catalog Aliases

Catalog registrations can define aliases with `LinkTarget`. An alias is a public catalog path that resolves its catalog content through another catalog.

The alias path is the public identifier. API callers request the alias path, search results and read requests expose the alias path, and read authorization claims are evaluated against the alias path.

The resolved target provides catalog content. Data-source calls use the resolved source catalog id, and catalog metadata, README/license attachments, package references, pipeline information, title, and transient behavior come from the resolved target.

Alias registrations can define optional `Begin` and `End` values. These values limit only the alias catalog itself. They do not automatically limit child catalogs exposed under the alias. Nested aliases intersect their configured ranges when the nested alias itself is resolved.

Availability and reads preserve the caller's requested output shape. Buckets or samples outside the alias range are returned as unavailable or invalid. Source availability and read calls are clipped to the configured alias range where possible. Cached derived data is keyed by the resolved source catalog id rather than the alias path, so aliases share the source-backed cache for equivalent derived reads.

Constrained resampled reads round source reads inward and never read outside the alias range. If the alias range is too narrow or unaligned to contain a full source sample interval, the corresponding output remains invalid.

Alias authorization is intentionally alias-path-based. A user can read `/ALIAS` when their claims allow `/ALIAS`, even if they do not have direct permission for the resolved source path. Alias definitions therefore are security policy and must be trusted/admin-controlled.

Aliases are read-only views. Clients may read source-backed catalog content and attachments through the alias, but metadata edits and attachment uploads/deletes are not allowed through aliases. Mutating the underlying catalog requires using the underlying catalog path and satisfying that catalog's write authorization.

License text is source-backed like other catalog attachments, but license acceptance is recorded against the public alias path. Accepting a license through `/ALIAS_A` does not automatically accept the same source-backed license through `/ALIAS_B` or the underlying source path. License retrieval and license acceptance intentionally do not require current read permission for the alias/catalog, so callers can inspect and accept a required license before license-gated read access is granted.

If a catalog needs to expose the same raw data with a different license text, it should not be modeled as an alias. Create a separate, non-alias catalog registration that points to the same raw data instead. This currently also means copying the catalog metadata to the new catalog, which is often undesirable because metadata includes units and channel descriptions. Until there is a simple way to share raw data while separating license attachments from other metadata, this duplication is an accepted limitation.

`DateTimeKind.Unspecified` alias range values and availability query values are interpreted as UTC without local-time conversion. Local or offset date/time values are converted to UTC using .NET's normal `ToUniversalTime()` behavior, so the effective UTC value depends on the supplied offset or, for local values, the server's local time zone. Registrations with `Begin > End` are not rejected, but the server logs a warning and the effective exposed range is empty.
