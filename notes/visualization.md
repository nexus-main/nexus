# Visualization overview

The backend and local GPU already share the summary, merge, and projection
algorithm described below. See the [frontend visualization guide](../src/Nexus.UI/src/app/charts/remote-visualization.md)
for rendering and scheduling details, and the [reduction benchmarks](../benchmarks/Nexus.Benchmarks/VisualizationReduction.md)
for backend kernel implementation and measurements.

## Shared backend/GPU algorithm

- Each aligned 256-sample base bucket retains its first/last samples, finite
  minimum/maximum with the earliest index on ties (including signed zero), first
  nonfinite gap, and exact invalid sample count. NaN and infinities are invalid.
- Parents merge four adjacent summaries in source order, preserving extrema,
  endpoints, and invalid counts. Coarser levels are never built from projected drawing points.
- Projection emits up to five index-sorted, deduplicated points: endpoints, finite
  extrema, and the first gap. Nonfinite values become NaN. Buckets with at least
  50% invalid samples emit only a NaN at the first invalid sample's index. Below
  50%, endpoints/extrema and the first gap marker are retained, even with multiple gaps.
- Queries return raw samples when they fit the point budget. Otherwise they choose
  the smallest power-of-two stride that fits the worst-case five points per bucket,
  reserving boundary capacity independently of alignment (except when only one
  bucket fits). Queries merge retained factor-four levels and resolve partial base
  boundaries from raw samples.

The backend uses Int64 sample indices and CPU summary storage; the GPU uses
48-byte summaries in device-limited pages (at most 16 MiB), u32 local indices,
and bigint absolute origins for CPU coordinate planning. GPU local domains are
limited to 4,294,966,784 samples and available device storage. Coordinates become
Float32 only after subtracting a local integer origin. GPU projection pads each
reduced bucket to five slots by repeating its last point; the server emits only
unique points. Server-reduced points go directly to the GPU line/fill renderer,
bypassing GPU decimation.

## Why a summary retains up to five points

A line chart connects emitted points in time order. A summary retains first,
last, finite minimum, finite maximum, and a gap marker because each protects
against a different visual distortion. These examples use small buckets for
clarity; the same reasoning applies to larger buckets.

In each plot, sample times are four character columns apart. `*` marks a sample;
connecting strokes approximate straight lines between samples. The tables give
the exact values and timestamps.

### Min/max preserve spikes and dips

One bucket contains:

```text
Time:   0   1   2   3   4   5   6
Value:  2   2   4   2   0   2   2
```

Raw data:

```text
 4 |        *
 3 |      /   \
 2 |*---*       *       *---*
 1 |              \   /
 0 |                *
   +-------------------------
    0   1   2   3   4   5   6
```

Keeping only first and last hides both extremes:

```text
 4 |
 3 |
 2 |*-----------------------*
 1 |
 0 |
   +-------------------------
    0   1   2   3   4   5   6
```

Keeping min/max as well preserves the spike and dip at their original timestamps:

```text
 4 |        *
 3 |    /     \
 2 |*           \           *
 1 |              \     /
 0 |                *
   +-------------------------
    0   1   2   3   4   5   6
```

The emitted samples are `(0, 2), (2, 4), (4, 0), (6, 2)`. The extremes survive,
but the original intermediate plateaus are not reconstructed: this is still a
reduced approximation, not lossless compression.

### First/last preserve transitions between buckets

Bucket A contains `t=0..2`; bucket B contains `t=3..5`:

```text
Time:   0   1   2   3   4   5
Value:  0   4   1   1   4   0
Bucket: A   A   A   B   B   B
```

Raw data has two peaks separated by a valley:

```text
 4 |    *           *
 3 |   / \         / \
 2 |  /   \       /   \
 1 | /      *---*      \
 0 |*                   *
   +---------------------
    0   1   2   3   4   5
```

Keeping only min/max retains `t=0,1` from A and `t=4,5` from B. Neither boundary
sample (`t=2,3`) is an extremum of its bucket, so both disappear:

```text
 4 |    *-----------*
 3 |   /             \
 2 |  /               \
 1 | /                 \
 0 |*                   *
   +---------------------
    0   1   2   3   4   5
```

Two short peaks now look like one prolonged high signal. At `t=2` and `t=3`, the
line shows `4` even though both actual values are `1`.

Retaining last A (`t=2`) and first B (`t=3`) restores the valley:

```text
 4 |    *           *
 3 |   / \         / \
 2 |  /   \       /   \
 1 | /      *---*      \
 0 |*                   *
   +---------------------
    0   1   2   3   4   5
```

First A already equals min A, and last B already equals min B, so those samples
are not emitted twice. First/last anchor the connection between adjacent buckets
to their actual boundary samples instead of connecting distant extrema directly.
They prevent this distortion, not every possible loss of interior detail.

### Gap markers prevent false continuity

One bucket contains a missing sample at `t=3`:

```text
Time:   0   1   2    3    4   5   6
Value:  2   2   2   NaN   2   2   2
```

Raw data has a break:

```text
 2 |*---*---*       *---*---*
   +-------------------------
    0   1   2   3   4   5   6
```

Keeping finite endpoints/extrema but omitting the gap falsely joins the sides:

```text
 2 |*-----------------------*
   +-------------------------
    0   1   2   3   4   5   6
```

The summary instead emits `(0, 2), (3, NaN), (6, 2)`. The renderer must break the
line at NaN, not skip it and connect the remaining finite points:

```text
 2 |*                       *
   +-------------------------
    0   1   2   3   4   5   6
                ^
              NaN: break, not a plotted value
```

Here `*` identifies the retained finite samples; isolated samples may not be
visible in a line-only renderer. This coarse summary preserves the break's
existence, not its exact duration or the valid segments on either side. With
multiple gaps, only the first gap is explicitly marked; this approximation may
connect across later gaps. Sparse dropouts no longer discard all finite points.

### The 50% invalid-sample rule

For each final query bucket, count all invalid samples, regardless of whether
they form one gap or many. NaN, positive infinity, and negative infinity count
equally. The threshold is fixed, independent of `Data:AggregationNaNThreshold`.

| Invalid samples | Bucket samples | Projection                                  |
| --------------- | -------------- | ------------------------------------------- |
| 127             | 256            | Endpoints, finite extrema, first gap marker |
| 128             | 256            | One NaN at the first invalid index          |
| 129             | 256            | One NaN at the first invalid index          |
| 2               | 5              | Endpoints, finite extrema, first gap marker |
| 3               | 5              | One NaN at the first invalid index          |

The exact integer test is `invalidCount >= count - invalidCount`, avoiding
overflow from doubling the count. Partial edge buckets use their actual sample
count, not the nominal stride. Empty summaries emit nothing. Raw views still
return individual samples (nonfinite values normalized to NaN).

Summaries retain finite extrema even when a child bucket is mostly invalid.
Parents add sample counts and invalid counts; the threshold is applied only when
projecting the final bucket, never while constructing the pyramid. This makes
the result independent of merge order and avoids averaging child percentages.
CPU scalar/SIMD reduction and client GPU reduction use the same rule.

For example, two isolated NaNs among 4096 samples no longer suppress that
bucket's finite points. A gap marker is still emitted below 50%, so a line-only
chart can still show fragmented or isolated points. This rule preserves finite
summary data; it does not guarantee a continuous overview line.

### Why the point budget starts at five

First/last anchor boundaries, min/max preserve finite extremes, and the gap
marker breaks continuity. They can occupy five distinct timestamps in one bucket.
Projection sorts them by timestamp and deduplicates coincident indices, not equal
values at different times.

Five is therefore the worst-case output size of one complete summary under this
algorithm, not a universal minimum for plotting. `MaxPoints >= 5` lets the planner
fit at least one such summary without discarding one of these features. Lower
budgets would need an explicit policy for which information to sacrifice.

## Backend endpoint

`POST /api/v2/data/visualization` is separate from the exact `POST /api/v2/data`
endpoint. It requires the same catalog authorization, including on cache hits.

```json
{
  "begin": "2020-01-01T00:00:00Z",
  "end": "2020-01-02T00:00:00Z",
  "resourcePaths": ["/SAMPLE/LOCAL/T1/1_s"],
  "views": [
    {
      "id": "main",
      "begin": "2020-01-01T00:00:00Z",
      "end": "2020-01-02T00:00:00Z",
      "maxPoints": 1000
    }
  ]
}
```

Ranges are half-open, contained in the domain, and aligned to the common
representation sample period. There must be 1-100 unique resource paths and 1-3
unique nonblank view IDs. Each point budget is 5-32768, including endpoints; a
separate aggregate budget applies across resources and views. Float32 normalization
precedes reduction. Nonfinite samples count as invalid; buckets with at least
50% invalid samples are suppressed by the shared visualization kernel. Other
buckets retain endpoints, finite extrema, and the first gap marker.

## Arrow contract

Content type: `application/vnd.apache.arrow.stream`. Schema metadata:
`visualizationVersion=1`. All columns and list elements are nonnullable.

| Column        | Type          | Meaning                                                             |
| ------------- | ------------- | ------------------------------------------------------------------- |
| kind          | int32         | 0 data, 1 progress/range, 2 complete, 3 error                       |
| resourceIndex | int32         | Request array index, or -1                                          |
| viewIndex     | int32         | Request array index, or -1                                          |
| offset        | int64         | View start relative to domain begin; completion uses domain length  |
| indices       | list<int64>   | Ordered sample indices relative to domain begin                     |
| values        | list<float32> | Drawing values, NaN for gaps                                        |
| progress      | float64       | Completed slice fraction, 1 on success, NaN on error                |
| minimum       | float32       | Finite minimum over the entire domain when known, else NaN          |
| maximum       | float32       | Finite maximum over the entire domain when known, else NaN          |
| message       | utf8          | Empty for pending progress, `complete` for final data/range/success |

Each batch currently contains one row. Nondrawing rows have empty lists. Pending
progress has offset zero and does not claim contiguous coverage. Progress is
flushed every 250 ms while waiting, including before the first slice completes.
One final, complete replacement is sent for each requested resource/view. No
partial data is exposed, so incomplete/out-of-order slices cannot create phantom
connections. Global range rows precede the corresponding view rows. Errors after
the header produce kind 3 with a generic message; details are logged server-side.
Kind 2 is emitted only on success. EOF without kind 2 is not success.

Invalid inputs or busy admission return 422 before the Arrow header; missing
resources return 404 and denied catalogs return 403. Disconnect/cancellation drains
workers before releasing permits and disposing plugin controllers.

## Execution and limits

Normal configuration binding uses `Data:Visualization`, including environment
variables such as `NEXUS_DATA__VISUALIZATION__MAXCONCURRENTREADS`.

| Setting               | Default            |
| --------------------- | ------------------ |
| MaxConcurrentReads    | 8                  |
| MaxComputeWorkers     | 2                  |
| MaxConcurrentRequests | 4                  |
| MaxAggregatePoints    | 262144             |
| MaxDatasetCacheBytes  | 1073741824 (1 GiB) |
| MaxTotalCacheBytes    | 1073741824 (1 GiB) |
| CacheTtl              | 00:10:00           |

Read/compute permits are process-global, not per-request. Each worker creates and
initializes its own pipeline controllers, reuses them across slices, and batches
original resources sharing a pipeline. Strict visualization controllers serialize
original/derived branches within each instance, propagate failures, and bypass the
older derived cache. No plugin concurrency opt-in is required. Recursive reads
reauthorize, create independent strict controllers, serialize sibling callbacks,
and stay inside the owning worker permit to avoid recursive semaphore deadlocks.
Dependencies are capped at depth eight and their working memory is estimated
separately from output sizing.

Slices align to the absolute sample lattice and base stride 256. Read workers use
the shared data memory tracker to admit each slice and derive its sample count.
The estimate covers raw source/status buffers, normalization, slice copies,
resampling halos, output buffers, and dependency reads. The shared memory tracker
determines the granted slice size. All requested views share the cold scan; raw
detail needed for those views is retained during that scan. Base summaries and
factor-four ancestors are retained. Compute permits cover base reduction,
ancestor construction, and view projection.

`MaxDatasetCacheBytes` limits one dataset's summary payload and separately its retained
raw viewport payload. `MaxTotalCacheBytes` is a **process-global** resident-summary
cache quota shared by all visualization datasets, users, and requests in one
server process, not a per-user or per-request allowance. Both default to 1 GiB. Active requests additionally hold
summaries (including uncached leases), raw viewport samples, worker buffers, and
Arrow output. `MaxConcurrentRequests` limits active request concurrency, not total
memory. Managed overhead, pool retention, and plugin-internal allocations are
additional; these settings are not a hard process memory or RSS cap.
Large domains that exceed the configured summary budget receive 422; tune limits
for the deployment rather than assuming multi-gigabyte cold scans are free.

## Cache and limitations

The cache is dataset-scoped: domain, ordered resource set, resolved/source/base
catalog items, effective catalog ranges, pipeline configuration/IDs, package IDs,
catalog metadata, user claims, captured request configuration, normalization version,
and catalog/process generations all enter the key. Views do not. The summary cache
is memory-only, with fixed TTL (hits do not extend expiry) and LRU eviction of
unpinned entries. Leases pin entries through output completion; pinned arrays
remain accounted for until released. If a dataset exceeds the cache quota or
pinned entries prevent admission, the caller receives an uncached lease instead
of exceeding the resident quota. Its active allocation is additional to the cache
quota. Restart discards the cache. No disk snapshots are read, written, or deleted,
including old snapshots left by earlier versions.

Warm overview/intermediate zoom uses summaries; partial base boundaries and fine
zoom read only the visible base buckets, coalesced into byte-budgeted slices.
There is no cross-request raw hot cache. Overlapping
domains or different ordered resource sets do not share cached summaries. A cold
main-only request still scans its whole domain to establish the global range and
reusable pyramid. No provisional drawing frames are emitted during that scan.

### Edge buckets

The summary pyramid is bucket-aligned, but requested views usually are not. Base
summaries cover fixed 256-sample buckets, so a view that starts or ends in the
middle of a bucket cannot use that bucket summary exactly: the summary may include
samples outside the requested half-open interval. For exact min/max at the view
edges, the backend resolves those partial base boundaries from raw samples.

These edge reads invoke the normal data-source pipeline. On a warm summary-cache
hit, a request with unaligned summary-level views can still instantiate plugin
controllers and call plugin `ReadAsync` for the affected 256-sample edge buckets.
The raw edge data is retained only for the active request; it is not part of the
cross-request summary cache. A later request with the same dataset key can hit the
summary cache and still reread its own edge buckets.

Example with a four-sample base bucket:

```text
Raw samples: 0 1 2 3 4 5 6 7 8 9 10 11

Cached summaries:
bucket A = samples 0..3
bucket B = samples 4..7
bucket C = samples 8..11

Requested view: samples 2..10
Exact interval: samples 2..9
```

Bucket B is fully contained in the view and can be answered from the cached
summary. Buckets A and C are partial edges. Current exact behavior reads raw
samples `2..3` for the left edge, uses cached bucket B for `4..7`, and reads raw
samples `8..9` for the right edge. This prevents samples `0..1` and `10..11`
from affecting the returned edge extrema.

One possible latency optimization is to widen, or snap, summary-level view ranges
to bucket boundaries and use the cached edge summaries directly. In the example,
the backend would answer the request from buckets A, B, and C, effectively using
samples `0..11` instead of exactly `2..9`, and would not need plugin reads for
those edge buckets.

That optimization is approximate and mathematically incorrect for the requested
range. If sample `1` or sample `11` contains an outlier, it can change the first
or last displayed min/max even though it is outside the view. With the production
base bucket size of 256 samples, a 20 us representation can over-include up to
5.12 ms before the left edge and up to 5.12 ms after the right edge. Interior
bucket summaries remain exact because they are fully contained in the requested
range.

Scans invoking plugin `readData` callbacks are not cached: arbitrary dynamic
dependencies are not represented by the outer cache key, and every subsequent
request must reauthorize and resolve those dependencies. Built-in aggregation and
resampling without such callbacks still use the visualization summary cache.

Concurrent identical builds share their work and completed results. Cancellation
of one consumer does not cancel work still required by another. The owner scope
remains alive until shared work drains; the last consumer's cancellation stops
the scan. Unversioned sources use TTL freshness,
not snapshot guarantees. Warm raw fragments can reflect newer source data than
the cached pyramid until expiry. Kernel and synthetic pipeline measurements are
recorded in the [reduction benchmarks](../benchmarks/Nexus.Benchmarks/VisualizationReduction.md)
and [synthetic service benchmarks](../benchmarks/Nexus.Benchmarks/VisualizationService.md);
these are not NVMe benchmarks.

## Development example

This high-memory development configuration raises read concurrency to 64 and both
payload quotas to 20 GiB, retaining two compute workers and four concurrent
requests. Read slice sizing still comes from the shared `Data:TotalBufferMemoryConsumption`
memory tracker. It is not a measured optimum or a process memory limit:

```json
{
  "Data": {
    "Visualization": {
      "MaxConcurrentReads": 64,
      "MaxComputeWorkers": 2,
      "MaxConcurrentRequests": 4,
      "MaxDatasetCacheBytes": 21474836480,
      "MaxTotalCacheBytes": 21474836480
    }
  }
}
```

Budget active requests in addition to the resident cache. Tune concurrency using
the actual plugin and RAID layout, including cold storage reads, rather than
extrapolating the synthetic in-memory benchmark.
