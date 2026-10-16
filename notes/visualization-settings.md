# Visualization Settings

The values shown below are the visualization settings and how they fit into the overall server-side visualization pipeline.

`EnableServerSideDecimation = false` enables the server-side visualization path and exposes the UI option when set to `true`. It is disabled unless explicitly configured.

```text
                        incoming visualization traffic
                                       |
                                       v
                  +-----------------------------------------+
                  | MaxConcurrentRequests = 4 (default)     |
                  | concurrently running requests           |
                  +--------------------+--------------------+
                                       |
                                       v
                             one admitted request
                                       |
                  +--------------------+--------------------+
                  |                                         |
                  v                                         v
+-----------------------------------+     +-----------------------------------+
| MaxConcurrentReads = 8 (default)  |     | MaxComputeWorkers = 2 (default)   |
| parallel source reads             |     | parallel CPU aggregation          |
+-----------------+-----------------+     +-----------------+-----------------+
                  |                                         |
                  +--------------------+--------------------+
                                       |
                                       v
                  +-----------------------------------------+
                  | MaxAggregatePoints = 256 Ki (default)   |
                  | returned / retained aggregate points    |
                  +--------------------+--------------------+
                                       |
                                       v
                  +-----------------------------------------+
                  | shared visualization cache              |
                  |                                         |
                  | MaxDatasetCacheBytes = 1 GiB (default)  |
                  | cap per dataset                         |
                  |                                         |
                  | MaxTotalCacheBytes = 1 GiB (default)    |
                  | cap across all datasets                 |
                  |                                         |
                  | CacheTtl = 10 min (default)             |
                  | idle age before expiry                  |
                  +-----------------------------------------+
```
