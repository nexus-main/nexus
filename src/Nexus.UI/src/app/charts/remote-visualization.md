# Server Visualization

The persisted `nexus.visualizationServerReduction` checkbox selects the remote
provider when constructing a dataset. Local mode still downloads raw Float32
chunks and uses GPU reduction. Switching modes constructs a new dataset; viewport
refinement does not change dataset or chart identity, hidden series, colors, or
vertical zoom. Matching domains, sample periods, and resource identities also
preserve viewport and hidden-series state across mode switches.

`NexusService.loadVisualization` uses the same `invoke` path as other service
requests, including same-origin credentials and the development-role header. No
additional configuration header is currently installed by `invoke`.

## Transport And Scheduling

- Domain timestamps remain fixed. Main, overview, and visible precision navigator
  views use sample-aligned bounds with a one-sample halo. Budgets include all
  points, cap each view at 32768, and target at most 4 MiB of point data per request.
- Horizontal gestures debounce for 150 ms. Vertical-only changes do not schedule
  requests. A new request immediately aborts the previous one and advances a
  generation, guarding progress, previews, errors, and cache publication.
- Arrow schema/types, optional version metadata, identities, list sizes/nulls,
  ordered domain/view coordinates, progress, errors, and explicit completion are
  validated. `offset` is validated as a domain sample offset, not added to the
  already domain-relative indices. EOF is not completion.
- Partial and completed streaming replacements become previews once every series
  in the view is present. Cache publication waits for successful explicit stream
  completion. Errors/cancellation discard previews and retain completed coverage.
- The cache retains one full-domain fallback and recent views, bounded by eight
  entries and 32 MiB of point payload. Repositioned arrays retain at most one view
  per target/resource. There is no raw network prefetch or automatic two-million
  sample download. Exact cursor neighborhoods debounce for 120 ms, use the same
  authenticated `NexusService.v2.data.getStream` route, and fetch at most 32 samples
  per resource (12.8 kB for 100 resources). Eight neighborhoods are retained. Cursor
  motion to another neighborhood or pointer leave cancels the previous read;
  generation guards reject late results. Pending and failed exact reads are
  explicitly labeled. The raw decoder checks schema, offsets, counts, nulls,
  truncation, cancellation and a 1 MiB response ceiling.
- Integer domain/sample ticks are subtracted before conversion to viewport-local
  Float32 x coordinates. The existing GPU line/fill pipeline consumes these points
  directly, without a reduction pass or raw-data callbacks. Progress/cancel/retry
  controls never cover the plot.

## Canonical Semantics And Deviations

The production local GPU pyramid uses the same summary and projection rules as
the server: first/last samples,
earliest-index finite minimum/maximum, and first nonfinite gap, sorted and
deduplicated by index. Infinity is normalized to NaN. All-invalid or two-or-more
gap-run buckets emit NaN only. Fixed GPU output uses five slots and repeats the
last point to pad; the server emits a variable number of unique points. Padding
adds only zero-length segments. Extrema ties, including signed zero, choose the
earliest source index.

`chart.webgpu.pyramid.js` replaces the local chunked overview path. Base summaries
are reduced in 256-thread workgroups using adjacent ordered merges; split upload
buckets merge with their preceding fragment. Parents merge four aligned children
on the GPU. The CPU plans coordinates only, using bigint absolute sample origins.
It selects the same smallest power-of-four stride satisfying the server's
worst-case point budget, descends retained levels for partial boundaries, and
uploads only unresolved raw fragments from existing local CPU chunks. Coarse
queries need at most 510 raw boundary samples; fine queries remain point-budget
bounded. No production parent or viewport summary is built from drawing points.
All reductions, including boundary fragments, run on the GPU. Three target query
outputs are cached; vertical-only zoom reuses them.

Remaining representation differences: GPU output pads each bucket with its last
point rather than emitting a variable count. Coordinates are converted to Float32
only after subtracting a local integer origin. Local indices are bounded to
4,294,966,784 samples and device storage limits, with an explicit error directing
larger domains to server mode. The server supports larger domains subject to its
own configured limits. GPU driver signed-zero/subnormal behavior still requires
numerical hardware verification. The old point reducers remain as legacy helper
implementations but are not used by production chunked-series rendering.

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

Local mode still retains the entire raw dataset in CPU chunks. On an integrated
GPU these chunks and GPU allocations compete for system memory: 10 GiB raw plus
roughly 640 MiB summaries, up to 16 MiB transient storage per concurrent series
upload, bounded query outputs/scratch, and browser/driver overhead. The chart's GPU
budget does not include CPU chunks or driver allocations. Fine queries reread and
copy bounded CPU fragments rather than retaining the old raw GPU chunk cache;
physical-iGPU upload/query throughput has not been benchmarked. Mode switches
dispose the old chart and release raw chunks or remote caches; pending target
reads are prevented from republishing GPU query buffers after target release.

## Verification

`npm test` includes fragmented Arrow, schema/count/coordinate/error/completion,
cancellation, large-coordinate, preview rollback, latest-wins, and cache tests.
`node --test tests/js/*.test.js` includes CPU-oracle query/merge tests across huge
absolute origins, boundary gaps, ties and levels, production pyramid upload/query
and memory-lifetime tests, plus direct remote point uploads and interactions.

Optional `tests/js/chart.webgpu.numeric.test.js` uses the Dawn `webgpu` Node module
specified by `NEXUS_WEBGPU_MODULE` (absolute module URL). It compares all three GPU
reducers and the actual production pyramid upload/parent/query path to canonical
fixtures. `NEXUS_WEBGPU_COMPILE_ONLY=1` selects Dawn's null backend and checks all
eight WGSL modules plus creation of the three summary compute pipelines, not
numerical parity. Numerical execution was also verified using Dawn with Mesa
lavapipe loaded from an isolated ignored `node_modules/.nexus-gpu-check` directory.
This includes split uploads, five absolute origins (one beyond Number's safe
integer range), multiple strides and clipped boundaries, plus an artificial 4 KiB
binding/allocation cap forcing multi-page parents, queries and transient uploads.
It is software Vulkan
execution, not a physical-GPU throughput measurement. Live desktop/mobile browser
interaction and backend-to-browser smoke testing remain unverified.
