# Catalog And Resource Properties

This note documents catalog and resource properties that currently have behavior in the Nexus server, clients, or UI.

## Catalog Properties

Catalog properties live in `catalog.properties` and can enrich behavior beyond the first-class catalog fields.

| Property | Expected Type | Behavior |
| --- | --- | --- |
| `title` | string | Displayed as the selected catalog title. Overrides/falls back before catalog-info title in the UI. |
| `readme` | string | Rendered as Markdown in the selected-catalog details panel. Preferred over catalog-info readme. |
| `resources` | object | Reserved for catalog-level metadata about resources. The planned resource availability structure lives here. |
| `nexus` | object | Catalog provenance info populated by the server. Shown in the catalog About dialog. See below. |

### Resource Availability

Planned catalog-level metadata for filtering or reasoning about resources by query time range:

```json
{
  "resources": {
    "availability": [
      {
        "pattern": "^/SAMPLE/LOCAL/P1$",
        "begin": "2020-01-01T00:00:00Z",
        "end": "2021-01-01T00:00:00Z"
      }
    ]
  }
}
```

Semantics:

- Metadata path: `catalog.properties.resources.availability`.
- `availability` is an array of rules.
- `pattern` is a regular expression matched against the full resource path.
- `begin` is inclusive.
- `end` is exclusive.
- Missing or `null` `begin` means unbounded past.
- Missing or `null` `end` means unbounded future.
- If no rule matches a resource, the resource is available.
- If one or more rules match, the resource is available only when at least one matching rule overlaps the selected/query time range.
- Invalid rules should be ignored by consumers.

Overlap check:

```text
ruleBegin < selectedEnd && selectedBegin < ruleEnd
```

### Nexus Provenance

The `nexus` property is injected by the server during `EnsureAndSanitizeMandatoryProperties` and records which Nexus and extension versions produced the catalog. The UI renders it in the catalog About dialog.

```json
{
  "nexus": {
    "version": "2.0.0-beta.57.925+sha",
    "pipeline": [
      {
        "repository-url": "https://example.com/repo/-/tree/main/src/Extensions/SomeSource",
        "version": "1.0.0+sha"
      }
    ]
  }
}
```

Semantics:

- Metadata path: `catalog.properties.nexus`.
- `version` is the `AssemblyInformationalVersion` of the Nexus extensibility package.
- `pipeline` is an array with one entry per data source in the catalog's pipeline.
- Each pipeline entry has:
  - `repository-url`: the `ExtensionDescriptionAttribute.RepositoryUrl` of the data source type.
  - `version`: the `AssemblyInformationalVersion` of the data source assembly.
- The property is written once (when absent) and not overwritten on subsequent passes.

## Resource Properties

Resource properties live in `resource.properties`.

| Property | Expected Type | Behavior |
| --- | --- | --- |
| `unit` | string | Displayed in the resource matrix, used by visualization series, written by CSV export metadata, and exposed by generated clients. |
| `description` | string | Displayed/searchable in the resource matrix and exposed by generated clients. Editable through catalog metadata overrides when the catalog is writable. |
| `warning` | string | Displayed/searchable as a resource warning in the UI. Editable through catalog metadata overrides when the catalog is writable. |
| `groups` | string array | Groups resources in the resource matrix. Also participates in resource search. |
| `originalName` | string | Used internally by data-source request handling for derived/processed resources. |

The UI resource metadata editor currently edits only `unit`, `description`, and `warning`. Blank edited values remove the corresponding override rather than storing empty strings.
