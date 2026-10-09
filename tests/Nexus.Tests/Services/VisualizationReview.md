# Backend visualization review

Historical review and measurements from 2026-10-07. Cache and memory notes below
reflect the memory-only operational model; see the [visualization overview](../../../notes/visualization.md)
for current configuration and shared backend/GPU semantics. Measurements have not
been rerun for the cache/configuration change.

## Reproduction

Run backend service regressions:

```sh
dotnet test tests/Nexus.Tests/Nexus.Tests.csproj -c Release --filter 'FullyQualifiedName~VisualizationServiceTests|FullyQualifiedName~DataControllerServiceTests'
```

Run the opt-in synthetic integration throughput measurement:

```sh
NEXUS_VISUALIZATION_BENCHMARK=1 dotnet test tests/Nexus.Tests/Nexus.Tests.csproj -c Release --filter FullyQualifiedName~SyntheticSourceThroughputBenchmark --logger 'console;verbosity=detailed'
```

The benchmark normally returns immediately unless explicitly enabled. It runs the
real visualization service, strict DataSourceController, normalization, pyramid,
projection and Arrow output. Only the plugin is mocked: it counts reads, detects
same-instance overlap, fills deterministic Float32 data, and optionally awaits
a 2 ms delay. No external user source or source files are accessed. This is not
an NVMe device benchmark or a measurement of a production extension.

## Measurements

2026-10-07, Release, .NET 9.0.17, Linux/KVM, AMD EPYC 7763, 96 exposed CPUs, AVX2.
Each case builds a fresh dataset of 4,194,304 timestamps and two resources
(8,388,608 scalar samples). The historical read-budget setting was 1 MiB, resulting
in 607 batched plugin reads; this is not a current worker memory cap. Those runs
excluded disk persistence. Times include source
initialization, scan, reduction, parents, projection and Arrow serialization.
One small warmup precedes the matrix; results below are medians of three cold
dataset runs per case, not isolated kernel measurements. JIT, GC and scheduling
noise remain, especially at short durations.

| Read workers | Compute limit | No-delay ms | No-delay Msamples/s | Async-delay ms | Async-delay Msamples/s |
| --- | --- | --- | --- | --- | --- |
| 1 | 1 | 122.7 | 68.36 | 2429.9 | 3.45 |
| 1 | 2 | 113.4 | 73.99 | 2429.8 | 3.45 |
| 1 | 4 | 117.6 | 71.36 | 2430.1 | 3.45 |
| 2 | 1 | 69.3 | 121.12 | 1218.2 | 6.89 |
| 2 | 2 | 68.5 | 122.38 | 1218.5 | 6.88 |
| 2 | 4 | 67.1 | 125.02 | 1219.6 | 6.88 |
| 4 | 1 | 45.3 | 185.23 | 614.2 | 13.66 |
| 4 | 2 | 44.4 | 188.94 | 613.9 | 13.66 |
| 4 | 4 | 45.4 | 184.61 | 610.3 | 13.75 |
| 8 | 1 | 36.7 | 228.88 | 310.0 | 27.06 |
| 8 | 2 | 37.3 | 225.13 | 315.1 | 26.62 |
| 8 | 4 | 34.8 | 240.98 | 309.1 | 27.14 |

Independent read workers genuinely overlap; earlier instrumented matrix runs
observed peak active plugin instances matching the configured read count in the
async case. Each plugin asserts its own active count never exceeds one. Increasing
compute permits is not equivalent to spawning compute workers: base reduction runs
on the existing Task.Run read-worker tasks, bounded by the compute semaphore.
Within one request its parallelism is at most min(read workers, compute limit).
Parent construction and view projection remain serial per request. No nested
Parallel.For or unbounded queue was added. The latency-dominated case scales with
read concurrency; this workload does not establish an optimal compute setting.

## Review findings and current cache behavior

- Cache leases pin resident entries through output completion. Eviction and expiry
  cannot subtract pinned arrays from resident accounting. If all eviction
  candidates are pinned, or the dataset exceeds the cache quota, admission returns
  an uncached lease rather than exceeding the resident quota. The memory-only
  cache uses fixed TTL and LRU eviction of unpinned entries.
- Exact pyramid sizing checks each level before allocation, including Int32 array
  bounds, without overflowing a multiplied estimate. Dependency memory validation
  uses division rather than an overflow-prone multiplication.
- Cache identity preserves authentication scheme, identity boundaries, claim
  types/issuers/value types/properties and name/role claim mappings. Authorization
  still runs before every request, including hits.
- The cache does not read, write, or delete disk snapshots, including old snapshots
  left by earlier versions. Historical snapshot validation is no longer applicable.
- Same-key consumers share build cancellation independently of their response
  tokens. Cancelling the owner does not cancel a scan still needed by a waiter.
  The owner scope remains alive until the scan drains; this intentionally delays
  completion of its cancelled task. Last-consumer cancellation cancels the scan.
  Completed results can be handed to existing waiters even without cache retention.
  Dynamic readData dependencies still bypass both summary caching and handoff
  so each request reauthorizes dependencies.
- Warm detail merges adjacent missing buckets into byte-budgeted reads, instead
  of issuing one plugin call per 256 samples.
- Partial pipeline construction/initialization cleanup attempts disposal of every
  created plugin even when an earlier Dispose throws, preserving the original
  construction/initialization exception.

## Bounds and remaining limitations

`MemoryLimitBytes` defaults to 1 GiB and is the process-global resident-summary
cache quota, shared by all users and requests per server process. `MaxDatasetBytes`
also defaults to 1 GiB and limits each dataset's summaries and separately its raw
detail payload. Active summaries (including uncached leases), raw detail, worker
buffers, and Arrow output must be budgeted in addition to the cache. Shared arrays
need not be allocated twice. `TargetReadBytes` sizes output slices, not worker
working memory: estimates also include native/status buffers, normalization,
copies, resampling halos, and dependencies. Recursive reads retain ancestor
buffers; depth is capped at eight, with sibling callbacks serialized and no
additional global read permit acquired. Managed overhead, pipe pool retention,
and plugin-owned allocations are additional. Neither these quotas nor request
concurrency promise a whole-process memory or RSS bound. Disconnected owners retain
their admission until shared work drains, avoiding unbounded detached builds.

Cross-request raw hot caching and provisional drawing are not implemented. Cached
summaries and later raw detail can still reflect different source revisions within
the TTL. In-memory cache maintenance holds the cache lock, and build gates still
use hash stripes; unrelated keys can contend. The historical measurements do not
justify claiming maximum NVMe throughput. No UI,
kernel or generated contract changes were made by this review.
