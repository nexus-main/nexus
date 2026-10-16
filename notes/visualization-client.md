# Visualization Client

This document describes how the UI loads, schedules, caches, and renders
visualization data in local GPU and server-reduced modes. See the
[visualization algorithm](visualization-algorithm.md) for the shared reduction
rules, backend endpoint contract, server execution limits, and server cache
behavior.

## Mode Selection And Loading

The persisted `nexus.visualizationServerReduction` checkbox selects the remote
provider when constructing a dataset. Local mode downloads raw Float32 chunks and
reduces them on the GPU. Switching modes constructs a new dataset. Viewport
refinement does not change dataset or chart identity, hidden series, colors, or
vertical zoom. Matching domains, sample periods, and resource identities preserve
viewport and hidden-series state across mode switches.

`NexusService.loadVisualization` uses the same `invoke` path as other service
requests, including same-origin credentials. When Angular dev mode is enabled,
`invoke` also sends the development-mode role header. No separate visualization
configuration header is added by `invoke`.

## Remote Transport And Scheduling

- **Domains and budgets.** Domain timestamps remain fixed. Main, overview, and
  visible precision navigator views use sample-aligned bounds with a one-sample
  halo. Budgets include all points and target four extrema buckets per physical
  pixel, with five slots per bucket and one boundary bucket. Twofold LOD rounding
  retains at least two buckets per pixel before caps. Server limits take
  precedence: 32768 points per view and 262144 across resources and three views
  (the default server aggregate limit).
- **Request coalescing.** Horizontal gestures coalesce for 50 ms without restarting
  the timer on each update; each dispatch uses the latest viewport. Vertical-only
  changes do not schedule requests. One foreground request runs at a time. A
  running request is retained if it contains the missing main view (or first missing
  auxiliary view) at no more than twice its canonical stride. Larger resolution
  changes, uncovered views, explicit cancel, and disposal abort it and advance a
  generation guarding progress, previews, errors, and cache publication. Immediate
  requests bypass the coalescing delay but still reuse useful running work.
- **Reloads.** Explicit Reload bypasses same-viewport reuse, aborts active foreground
  and speculative work, and invalidates every cached resolution covering a requested
  view. Late results from aborted work cannot restore invalidated coverage. Unrelated
  cached history remains available.
- **Retained work.** Retained results may provide temporary coverage with clipped
  boundary buckets; completion rechecks the latest views and schedules any remaining
  refinement. Boundary-correct cache-hit rules are unchanged. Failed unchanged
  requests do not retry automatically; if the viewport changed while a retained
  request ran, its latest missing views receive their own attempt.
- **Stream validation.** Arrow schema/types, optional version metadata, identities,
  list sizes/nulls, ordered domain/view coordinates, progress, errors, and explicit
  completion are validated. `offset` is validated as a domain sample offset, not
  added to the already domain-relative indices. EOF is not completion.
- **Previews.** Partial and completed streaming replacements become previews once
  every series in the view is present. Cache publication waits for successful
  explicit stream completion. A viewport supersession can retain one fully delivered
  view (every resource marked complete) as a display-only fallback, bounded by 32768
  points per resource and 262144 resource-points total. It never satisfies a cache
  hit or suppresses refinement. Incomplete previews are discarded. The fallback is
  cleared on foreground failure, explicit cancel/disposal, loss of coverage, or when
  the latest views are satisfied by the completed cache.
- **Progress and ranges.** Foreground progress is monotonic within a load. Finite
  global ranges publish independently of point-cache completion and survive
  failure/cancel; speculative and aborted frames do not change them. Completed
  full-domain points supply a missing range, but never replace an authoritative
  server range. Nonfinite range frames do not erase a previously published range.
- **Remote cache.** The remote cache is the client-side cache of server-reduced
  visualization results. It lets remote mode reuse previously loaded views instead
  of requesting the same coverage from `/api/v2/data/visualization` again. It
  retains one full-domain fallback and recent views, targeting at most 1024 entries
  and the cache budget captured when constructing the dataset (the UI's configured
  cache size, currently 2048 MiB by default). Payload accounting includes Int64
  indices and Float32 values, 12 bytes per point. Full-domain and current protected
  coverage are exempt from eviction, so these are soft limits.
- **Repositioned arrays.** Repositioned arrays retain at most one view per
  target/resource. There is no raw network prefetch or automatic two-million sample
  download.
- **Cursor labels.** Remote cursor labels use the nearest cached visualization point
  from the rendered main view. They report nearest rendered/reduced values rather
  than exact raw samples, so cursor motion does not issue additional raw-data
  requests.
- **Rendering handoff.** Remote responses carry integer sample indices so the UI
  can avoid converting large absolute timestamps directly to GPU floats. For each
  point, the chart subtracts the current view start first, then converts the small
  view-relative offset into a Float32 x coordinate for the viewport. These
  server-reduced points then go straight into the existing GPU line/fill renderer:
  the client does not run another reduction pass and cursor movement does not call
  back for raw samples. Progress, cancel, and retry controls stay outside the plot
  area so they do not cover the data while a remote view is loading.

## Zoom Prefetch

After the latest foreground views complete (or are already cached), a 250 ms idle
delay starts a bounded speculative queue for the main chart. There is no active
prefetch lane competing with missing foreground views. The queue predicts half,
quarter, double, and quadruple spans around the anchor inferred from consecutive
horizontal viewports, using the center when there is no zoom history or the gesture
is a pan. Predictions include 10% padding on each side, align to samples, and
shift/clamp to the dataset domain. Only one speculative request runs at a time; any
new viewport, foreground request, cancel, or disposal aborts it and invalidates late
results. This is client-side scheduling priority, not a server priority queue.

Each prediction receives enough points to preserve its intended canonical stride
despite padding. The 32768-point view cap and a total 262144 resource-points per
prefetch cycle still apply; predictions that cannot fit are skipped. Fine views
that would require scanning raw detail are not prefetched. Coarse queries may
still read small unresolved boundary fragments. The dataset-wide summary pyramid
already exists after the foreground load, subject to normal server cache eviction.

Prefetch never changes foreground progress or publishes incomplete previews/errors.
Successfully completed results notify chart subscribers and share the configured
cache. Speculative entries are evicted before visited history; current coverage and
the full-domain fallback remain protected. Within either class, narrower views are
evicted before broader coverage. Before each speculative request, capacity is checked
using its worst-case point payload, excluding replaceable unvisited speculative
entries. A full history cache therefore suppresses prefetch before network dispatch
(`prefetch-stop-capacity`), rather than downloading a result only to evict it. The
queue stops on failure or when its result cannot be retained, without retrying until
another viewport change.

Rendering chooses the finest canonical cached stride, not nominal points per
tick. Prefetched coverage is available immediately during a later gesture. Exact
bounds, raw coverage, and compatible bucket boundaries can avoid a new request;
clipped reduced boundary buckets still trigger foreground refinement so gap
summaries cannot permanently hide finite boundary samples. This improves warm
navigation but does not guarantee request-free or latency-free continuous zoom.

## Local GPU Semantics

The production local GPU pyramid uses the same summary and projection rules as
the server: first/last samples,
earliest-index finite minimum/maximum, and first nonfinite gap, sorted and
deduplicated by index. Infinity is normalized to NaN. Buckets with at least 50%
nonfinite samples emit NaN only at the first invalid index. Below 50%, endpoints,
finite extrema, and the first gap marker are retained, even for multiple gaps.
Exact invalid counts are summed during merges; the final query bucket uses
`invalidCount >= count - invalidCount`, including partial buckets. This fixed
rule is independent of `Data:AggregationNaNThreshold`. Fixed GPU output uses five
slots and repeats the last point to pad; the server emits a variable number of
unique points. Padding adds only zero-length segments. Extrema ties, including
signed zero, choose the earliest source index.

`chart.webgpu.pyramid.js` replaces the local chunked overview path. Base summaries
are reduced in 256-thread workgroups using adjacent ordered merges; split upload
buckets merge with their preceding fragment. Parents merge four aligned children
on the GPU. The CPU plans coordinates only, using bigint absolute sample origins.
It selects the same smallest power-of-two query stride satisfying the server's
worst-case point budget, reserving boundary capacity independently of absolute
alignment so a pan cannot coarsen LOD. Budgets fitting only one summary must use
actual alignment instead. Raw samples are emitted when they fit the point budget.
Local budgets use each target's physical plot width, not the main canvas CSS width
or the server aggregate cap, targeting four buckets per pixel capped at 8192
buckets plus one boundary bucket. Query planning descends retained levels for
partial boundaries and uploads only unresolved raw fragments from existing local
CPU chunks. Coarse queries need at most 510 raw boundary samples; fine queries
remain point-budget bounded. No production parent or viewport summary is built from drawing points.
All reductions, including boundary fragments, run on the GPU. Three target query
outputs are cached; vertical-only zoom reuses them.

Remaining representation differences: GPU output pads each bucket with its last
point rather than emitting a variable count. Coordinates are converted to Float32
only after subtracting a local integer origin. Local indices are bounded to
4,294,966,784 samples and device storage limits, with an explicit error directing
larger domains to server mode. The server supports larger domains subject to its
own configured limits. GPU driver signed-zero/subnormal behavior still requires
numerical hardware verification.

## Local Memory Limits

Each base summary is 48 bytes per 256 samples; factor-four ancestors add about
one third. Persistent GPU storage is therefore approximately 6.25% of raw Float32
bytes, versus 2.34% for the previous three-point overview (about 2.67 times larger).
These mergeable summaries retain information that projected overview points lose;
the additional storage is a cost of this layout, not a duplicate raw GPU cache.

The logical pyramid is paged into GPU buffers of at most 16 MiB (or the device's
smaller binding/buffer cap), rounded down to whole 48-byte records. All levels keep
their canonical global alignment and logical offsets. Parent construction copies
bounded child ranges between GPU pages and a scratch buffer, then merges on the
GPU. Query planning gathers only selected summaries into a compact GPU buffer;
the final query merges them in source order before projection. Neither operation
reads summary values back to the CPU or scans raw data on the CPU.

A 10 GiB Float32 series still needs approximately 640 MiB of persistent summaries,
compared with 240 MiB previously, but no binding is that large. Mocked allocation
and parent construction succeed at 128/256/512 MiB device caps within a 2 GiB chart
budget. Page and scratch allocations are tracked, including partial-allocation
failure cleanup. Uploads split through a device-sized transient buffer; parent
scratch is capped at one page. Bounded viewport buffers must also fit the device
cap (ordinary 128 MiB and larger tiers readily fit the 32768-point request cap).
Total GPU budget, host memory, and u32 local-index limits still apply; sharding
does not reduce total summary memory or promise that the driver can supply it.

Local mode retains the raw dataset in CPU chunks. On an integrated GPU these
chunks and GPU allocations compete for system memory: 10 GiB raw plus
roughly 640 MiB summaries, up to 16 MiB transient storage per concurrent series
upload, bounded query outputs/scratch, and browser/driver overhead. The chart's GPU
budget does not include CPU chunks or driver allocations. Fine queries reread and
copy bounded CPU fragments rather than retaining the old raw GPU chunk cache;
physical-iGPU upload/query throughput has not been benchmarked. Mode switches
dispose the old chart and release raw chunks or remote caches; pending target
reads are prevented from republishing GPU query buffers after target release.

## Verification

The `src/Nexus.UI` `npm test` script type-checks the UI test project and runs the
TypeScript UI tests from `tests/Nexus.UI.Tests/app`, including the chart
decoder/provider tests under `tests/Nexus.UI.Tests/app/charts/*.test.ts`. Those
tests cover fragmented multi-batch Arrow decoding,
version/schema/count/coordinate/error/completion validation, resource-major
multi-view decoder/provider integration, cancellation,
large coordinates, preview rollback, latest-wins, force reload, progress/ranges,
and byte/entry cache limits. Provider scheduling tests use mocked timers and drain
promise continuations instead of sleeping in real time.

`node --test tests/js/*.test.js` is a separate JavaScript chart test suite. It
covers query/merge results checked against a straightforward CPU reference
implementation across large absolute origins, boundary gaps, ties and levels,
production pyramid upload/query and memory-lifetime paths, and chart interaction
behavior.

`tests/js/chart.webgpu.numeric.test.js` uses the Dawn `webgpu` Node module when
`NEXUS_WEBGPU_MODULE` points to an absolute module URL. Without that variable, the
numerical WebGPU checks are skipped. When enabled, the suite compares the GPU
reducers and production pyramid upload/parent/query path against canonical
fixtures, including split uploads, five absolute origins, multiple strides,
clipped boundaries, and an artificial 4 KiB binding/allocation cap that forces
multi-page parents, queries, and transient uploads. This is software Vulkan
execution, not a physical-GPU throughput measurement.

Live desktop/mobile browser interaction and full backend-to-browser rendering
smoke testing are not covered by these commands.
