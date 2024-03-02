# Nexus.Writers.Csv

This data writer supports the following optional request configuration parameters:

| key                   | value                                                          | data type | default   |
| --------------------- | -------------------------------------------------------------- | --------- | --------- |
| `row-index-format`    | `"index"` &#124; `"unix"` &#124; `"excel"` &#124; `"iso-8601"` | string    | `"index"` |
| `significant-figures` | [0, 30]                                                        | string    | `"4"`     |