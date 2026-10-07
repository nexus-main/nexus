# Backend visualization

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
precedes reduction. Nonfinite samples are gaps; multigap buckets are conservatively
suppressed by the shared visualization kernel rather than connected across gaps.

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

| Setting               | Default                                     |
| --------------------- | ------------------------------------------- |
| MaxConcurrentReads    | 4                                           |
| MaxComputeWorkers     | 2                                           |
| MaxConcurrentRequests | 4                                           |
| TargetReadBytes       | 4194304                                     |
| MaxAggregatePoints    | 262144                                      |
| MaxSamples            | 1000000000 (domain samples times resources) |
| MaxDatasetBytes       | 67108864                                    |
| MemoryLimitBytes      | 268435456                                   |
| DiskLimitBytes        | 2147483648                                  |
| CacheTtl              | 00:10:00                                    |

Read/compute permits are process-global, not per-request. Each worker creates and
initializes its own pipeline controllers, reuses them across slices, and batches
original resources sharing a pipeline. Strict visualization controllers serialize
original/derived branches within each instance, propagate failures, and bypass the
older derived cache. No plugin concurrency opt-in is required. Recursive reads
reauthorize, create independent strict controllers, serialize sibling callbacks,
and stay inside the owning worker permit to avoid recursive semaphore deadlocks.
Dependencies are capped at depth eight and checked against the read-byte budget.

Slices align to the absolute sample lattice and base stride 256. Byte estimates
include native/status buffers, normalization, slice copies, and resampling halos.
All requested views share the cold scan; raw detail needed for those views is
retained during that scan. Base summaries and factor-four ancestors are retained.
Compute permits cover base reduction, ancestor construction, and view projection.

`MemoryLimitBytes` limits resident cached summaries, not the entire process. Active
requests additionally retain at most `MaxDatasetBytes` of summaries and that same
limit of raw viewport samples, plus bounded worker buffers and Arrow output.
`MaxConcurrentRequests` bounds these active allocations. Plugin-internal allocations
cannot be controlled by the host. Actual managed allocation overhead is additional.
Large domains that exceed the configured summary budget receive 422; tune limits
for the deployment rather than assuming multi-gigabyte cold scans are free.

## Cache and limitations

The cache is dataset-scoped: domain, ordered resource set, resolved/source/base
catalog items, effective catalog ranges, pipeline configuration/IDs, package IDs,
catalog metadata, user claims, captured request configuration, normalization version,
and catalog/process generations all enter the key. Views do not. Summary arrays
are stored together under `Paths:Cache/visualization-v1`, with memory/disk quotas
and fixed expiry. Disk writes use temporary files and atomic rename. Quotas and
expiry are maintained on access. The raw binary cache is process-generation scoped;
it intentionally does not survive restart as a reusable cache.

Warm overview/intermediate zoom uses summaries; partial base boundaries and fine
zoom read only the visible base buckets, coalesced into byte-budgeted slices.
There is no cross-request raw hot cache. Overlapping
domains or different ordered resource sets do not share cached summaries. A cold
main-only request still scans its whole domain to establish the global range and
reusable pyramid. No provisional drawing frames are emitted during that scan.

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
recorded in `benchmarks/Nexus.Benchmarks/VisualizationReduction.md` and
`tests/Nexus.Tests/Services/VisualizationReview.md`; these are not NVMe benchmarks.

## Opt-in server timings

Enable `localStorage.setItem("nexus.visualizationTrace", "true")` in the browser
console. Subsequent HTTP loads, including retries and viewport reloads, create a
fresh random UUID and send it as `X-Nexus-Visualization-Trace`. UUIDs use
`crypto.getRandomValues` so tracing also works on HTTP deployments. Remove the
localStorage key to disable tracing. The server enables timings
only for a single canonical GUID-D value (36 characters with hyphens, validated
with `Guid.TryParseExact(..., "D", ...)`). Uppercase hex is accepted and normalized
to lowercase in the same response header. Missing, malformed, whitespace-padded,
or multiple values do not enable timings and are not echoed. This is only a
diagnostic header; request JSON, OpenAPI models, generated clients and Arrow
schemas are unchanged.

Timings use the fixed logger category `Nexus.Visualization.Timing` at Information
level, which the normal console configuration already includes. No separate
server configuration switch is needed. For a local run, capture the console:

```sh
dotnet run -c Release --project src/Nexus/Nexus.csproj 2>&1 | tee /tmp/nexus-console.log
```

For a deployed service, collect its usual container stdout or service journal.
Search for the echoed request UUID to correlate server entries with that one
browser load. The rendered message includes `request`, `phase`, `milestone`,
`elapsedMs`, `durationMs`, `outcome`, `worker`, and `count`; these are also structured
log properties. Times use a monotonic stopwatch, relative to trace creation at
endpoint entry, not browser timestamps. Worker IDs are request-local; `-1` denotes
request-level work. Timing entries never include configuration, claims, resource
paths, cache keys, view IDs, payloads, or exception messages. Existing service/plugin
error logging is separate from these sanitized timing entries.

Measure latency without an attached debugger. Rapid zooms intentionally cancel
superseded requests; debugger first-chance exception notifications can delay other
requests as those cancellations unwind. Expected request aborts are handled after
worker cleanup rather than logged as HTTP 500 failures: an unstarted response is
marked 499 and the HTTP request is aborted, including already-started Arrow streams.
This does not suppress debugger first-chance exception notifications.

These durations measure wall time, not CPU time. They include scheduling and GC
pauses and can include synchronous logging overhead. Compare tracing enabled and
disabled if console output is slow; do not treat a long read or compute phase as
proof that the underlying I/O or computation alone caused the delay.

Phases cover preparation (including failures), immediate admission, `join-build`,
initial progress flush, build-gate wait, cache get/put, cold/detail scans, compute
wait/work, worker read-permit waits, source initialization, inclusive controller
reads, disposal, first data flush, completion flush, cancellation requested/drain,
resources released, and endpoint request end. Cache get/put and join-build durations
include monitor acquisition; cache durations also include quota/pruning and disk
I/O inside the call. Get outcomes distinguish `hit`, `shared` (completed shared
build without a cache lease), and `miss`; memory and disk hits are not separated.
Cache-put `success` means the call completed, not that the dataset was retained:
cache quotas can prevent retention without failing the request.

Repeated initialization, reads, slice computation and disposal are aggregated per
worker; projection computation is aggregated per request. Each aggregate includes
the attempted operation count and summed durations, including failed/cancelled
attempts. Only the first operation in each aggregate emits start/end milestones;
final totals are emitted when that worker/request releases resources. There are
no per-bucket logs and output is bounded by worker/phase counts, not domain size.
Start/end entries describe the same work as the aggregate and must not be added
to it. A later stalled read is not individually identified until the aggregate
finishes; a process crash can prevent final aggregates and request-end entries.

`controller-read-inclusive` measures `ReadSliceAsync`, **not device I/O**: it includes
controller/plugin work, recursive dependency initialization/reads/disposal,
normalization, pipe handling and copies inside that boundary. Top-level source
initialization and disposal have separate worker aggregates; recursive operations
are not separately instrumented. Overlapping worker durations can exceed request
wall time. Shared work stays correlated to its owner's request even when that
owner cancels and another consumer still needs the build; the owner's drain can
therefore be long. Cancellation milestones do not alter that lifetime. Flush
completion is server-side only, not proof of browser receipt/rendering. Request
end is endpoint exit after service cleanup, not transport teardown or MVC error
body serialization. Requests rejected by middleware/model binding before endpoint
entry have no visualization timings.

## Large-server example

The conservative default summary limit rejects some multi-gigabyte domains.
For approximately 10 GiB of Float32 samples across the selected resources,
the following is a starting configuration, not a measured optimum:

```json
{
  "Data": {
    "Visualization": {
      "MaxConcurrentReads": 8,
      "MaxComputeWorkers": 8,
      "TargetReadBytes": 33554432,
      "MaxSamples": 4000000000,
      "MaxDatasetBytes": 1073741824,
      "MemoryLimitBytes": 2147483648,
      "DiskLimitBytes": 17179869184
    }
  }
}
```

Budget active requests in addition to the resident cache. Tune concurrency using
the actual plugin and RAID layout, including cold storage reads, rather than
extrapolating the synthetic in-memory benchmark.
