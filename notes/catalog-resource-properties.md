# Catalog Resource Properties

Catalogs can define optional resource UI metadata through `catalog.properties.resources`.

## Availability

`resources.availability` limits which resource representation rows are available for the currently selected time range.

```json
{
  "resources": {
    "availability": [
      {
        "pattern": "^/SAMPLE/LOCAL/P1/1_s#base=1_s$",
        "begin": "2020-01-01T00:00:00Z",
        "end": null
      },
      {
        "pattern": "^/SAMPLE/LOCAL/P1/100_ms#base=100_ms$",
        "begin": null,
        "end": "2020-01-01T00:00:00Z"
      }
    ]
  }
}
```

Rules are evaluated per representation row. The `pattern` value is a regular expression matched against the canonical representation path, not just the resource path. A base representation path has this shape:

```text
/{catalog-id}/{resource-id}/{sample-period}#base={sample-period}
```

Examples:

```text
/SAMPLE/LOCAL/P1/1_s#base=1_s
/SAMPLE/LOCAL/P1/100_ms#base=100_ms
/SAMPLE/LOCAL/P1/20_ms#base=20_ms
```

This allows one resource to expose multiple representations with different availability windows, such as the sample `P1` resource exposing a `100 ms` (`10 Hz`) representation before 2020 and a `1 s` representation from 2020 onward.

To match all representations of a resource, use a broader expression:

```json
{
  "pattern": "^/SAMPLE/LOCAL/P1/",
  "begin": "2024-01-01T00:00:00Z",
  "end": "2024-02-01T00:00:00Z"
}
```

Semantics:

- `resources.availability` must be an array; malformed metadata is ignored.
- `pattern` must be a valid regular expression; invalid rules are ignored.
- `begin` is inclusive. Missing or `null` means unbounded past.
- `end` is exclusive. Missing or `null` means unbounded future.
- If no rule matches a representation path, that representation is available.
- If one or more rules match a representation path, the representation is available only when at least one matching rule overlaps the selected time range.
- The overlap check is `ruleBegin < selectedEnd && selectedBegin < ruleEnd`.
- If the selected time range is invalid, availability metadata is ignored and all representations remain available.
