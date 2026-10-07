# Server-side visualization decimation

Branch: `feature/server-side-decimation`.

## Goal and agreed contracts

Offer a single server-reduction checkbox without changing signal selection,
exports, axes, colors, or navigation. Reduce network traffic for multi-gigabyte
signals while exploiting NVMe parallelism, SIMD, and available CPU capacity.
Independent plugin instances are safe to execute concurrently. Never issue
concurrent reads on the same instance. Reuse one initialized pipeline per worker.

## Canonical algorithm

Normalize requested representation values to Float32 before reduction. Use
sample-aligned buckets, deterministic earliest-index extrema ties, first/last
samples, ordered min/max, and explicit nonfinite gap markers. NaN and infinity
are gaps. Keep incomplete coverage and read failure separate from source NaNs.
Keep the existing conservative suppression of buckets containing multiple gap
runs. Use the same documented projection rules in browser and server modes.

Internal summaries retain interval boundaries, first/last values, finite
extrema and indices, first gap index, finite presence, and a gap-run count
saturated at two. Merge adjacent runs across nonfinite boundaries before
saturating. Empty summaries are identities. Never construct parent summaries
by reducing emitted drawing points. Send time-ordered sample indices and values
to the renderer, not falsely uniformly spaced extrema.

## Read and reduction pipeline

Reuse authorization, representation resolution, source controllers, Float32
normalization, and bounded memory below existing Arrow serialization. Do not
HTTP-call or decode the existing data endpoint internally.

Schedule disjoint time slices with a bounded global read budget and independent
plugin pipelines. Batch related channels per call. Align slices with base
buckets and representation periods. Estimate bytes across native/status/output
buffers; bound active and queued work. Parallel slice completion is allowed;
merge by source coordinates, not completion order. Do not retain raw slices
while waiting for unrelated earlier slices.

SIMD base reduction uses runtime AVX2 dispatch and a scalar reference/tail path.
Do not assume AVX-512 (current KVM environment exposes AVX2 and 96 logical CPUs).
Bound CPU parallelism independently from source concurrency; avoid nested
resource/level/bucket Parallel.For fan-out. Reduce small chunks inline. Check
cancellation between blocks and await workers before returning pooled memory.
Avoid recursive-source deadlocks when readData callbacks acquire resources.

Expose normal Data:Visualization .NET options and NEXUS_DATA__VISUALIZATION__*
environment overrides for read concurrency, compute concurrency, target slice
bytes, cache and work limits. Initial values are tunable, not performance claims.
Measure real source cold/warm throughput before claiming optimal concurrency.

## Pyramid and cache

Base stride 256, parent factor four. Retain complete base summaries and ancestors
from every scan, so intermediate warm zoom does not reread raw history. Store
many summaries per page, not a file per bucket. Construct all levels from one
bounded source pass; transmit only requested views. Handle partial viewport
boundaries by descending children and reading only unresolved base fragments.
Fine detail reads only the narrow visible range, with a bounded raw hot cache.

Authorize every request including hits. Cache identity includes resolved
representation/base, parameters, sampling origin, source/request configuration,
content isolation, normalization/algorithm version, and freshness epoch/revision.
Do not inherit unsafe derived-cache collisions or cache failed reads as gaps.
Unversioned sources require bounded expiry/invalidation, not invented snapshot
guarantees. Deduplicate concurrent builds; cancellation detaches one consumer
without canceling work still needed by another. Apply memory/disk quotas.

## API and progress

Keep POST /api/v2/data exact and compatible. Add
POST /api/v2/data/visualization with domain begin/end, resourcePaths, and views
(id, begin, end, maxPoints). Float32 chart output. Enforce total point/byte/work
budgets, including endpoints and halos. Server selects levels, not the user.

Use a separately versioned fixed Arrow schema for data, progress, completion,
and error records. Include resource/view identity, resolution, coverage,
generation, sample coordinates and values. Explicit completion is required;
EOF alone does not prove success. Progress tracks completed source work and
coverage and is flushed even before a coarse bucket completes. Throttle updates.
Regenerate C#, Python, TypeScript and both OpenAPI documents after API changes.

## Frontend

Keep WebGPU drawing and existing chart interactions. Add a provider boundary for
local and remote data, preserving chart identity during refinement. Add one
persisted PrimeNG checkbox. Remote mode caches coarse coverage and recent detail,
coalesces viewport changes, ignores stale responses, and atomically replaces
completed coverage. Vertical-only zoom does not fetch. Main and both navigators
share data and request suitable resolutions. Prefetch is small and low priority.
Use local integer origins before converting x to Float32. Raw-detail transfer
uses network budgets, not the existing two-million-sample GPU threshold.
Fetch debounced cursor neighborhoods when exact raw values are absent; do not
present extrema as exact cursor samples. Keep a usable chart interactive during
refinement with nonblocking progress/cancel/error feedback.

## Verification and execution checklist

- [x] Create branch before writing this plan.
- [x] Implement scalar mergeable summaries and canonical projection tests.
- [x] Implement SIMD reduction and scalar/SIMD randomized parity tests.
- [x] Align browser reduction semantics and numerical fixtures.
- [x] Implement bounded independent-instance parallel reads and pyramid cache.
- [x] Implement visualization transport, progress, authorization and limits.
- [x] Integrate checkbox, providers, refinement, navigators and cursor detail.
- [x] Regenerate clients/OpenAPI, run backend/UI/client tests and lint.
- [x] Benchmark kernels and parallel source reads; record measured limitations.

Required tests: arbitrary chunk partitions, ties/signed zero, all-invalid and
multiple gaps, boundary gap merging, large coordinates, partial buckets,
single-read multilevel building, out-of-order slices, cancellation/backpressure,
cache identity/expiry/reauthorization, stale UI responses, slow fragmented
streams, unchanged exact v2 behavior, real GPU numerical parity where available.
Cold exact overviews necessarily scan the source once. Do not claim zero-latency
uncached zoom or measured throughput without an actual benchmark.

## Implementation outcome (2026-10-07)

Core feature implemented on `feature/server-side-decimation`. Independent plugin instances run
concurrently; reads on each instance are serialized. Local GPU summaries are
paged to avoid the single-storage-binding capacity regression for 10 GiB signals.
The canonical hierarchy/projection executes on the GPU locally and AVX2/scalar
on the server. Exact remote cursor neighborhoods and mode-switch state retention
are implemented.

Verification: 330 backend tests excluding the four CSV tests that require the
unavailable frictionless executable; 171 UI tests; 58 JS tests with software
Vulkan; 9 C# client, 10 TypeScript client, and 56 Python tests. Angular development
build, ESLint, Prettier, TypeScript checks and diff checks passed. Scalar-disabled
intrinsics tests also passed. Python pyright could not start because its installed
Python environment lacks typing_extensions. Physical-GPU performance and live
desktop/mobile browser interaction are not verified.

The isolated real-host smoke uses production frontend Arrow decoders and passes
cold loading, warm multi-view refinement, exact cursor equality, 403/404/422 error
responses, cancellation and recovery. It found and fixed error negotiation that
previously returned 406 for an Arrow-only Accept header.

Measured kernel and synthetic pipeline results are recorded in
benchmarks/Nexus.Benchmarks/VisualizationReduction.md and
tests/Nexus.Tests/Services/VisualizationReview.md. No production NVMe benchmark was
run. Configuration and a 10 GiB large-server example are in notes/visualization.md.

### Deliberate limits and remaining follow-ups

- Backend cache is dataset-scoped (domain and ordered resources), not shared
  globally across overlapping domains. It retains full pyramid arrays within
  configured limits and writes atomic snapshots, not incrementally paged disk
  records. Restart invalidates reusable snapshots.
- Cold scans stream progress but only emit chart points when the scan completes.
  Incremental completed-coverage drawing remains a follow-up.
- Fine reads are coalesced and bounded but have no cross-request server raw hot
  cache. Browser view and exact-cursor caches are bounded and implemented.
- Disk cache I/O is synchronous under its lock. Production storage benchmarks
  should guide whether async/page-granular persistence is needed.
- TTL is not a source snapshot guarantee; dynamic plugin dependencies bypass
  caching to preserve authorization and identity correctness.
- Local persistent summary memory is about 6.25% of raw Float32 size (~640 MiB
  for 10 GiB), versus the old overview's ~240 MiB. GPU pages remove individual
  binding limits, not total memory requirements. Hardware performance validation
  remains necessary before claiming no throughput regression on an integrated GPU.

### Remaining task checklist

- [ ] Benchmark real NVMe-backed plugins with cold and warm reads; tune read
  concurrency, compute limits, slice sizes, and memory budgets for the deployment.
- [ ] Benchmark 10 GiB local rendering on the physical integrated GPU, including
  upload time, zoom latency, total memory, and comparison with the previous renderer.
- [ ] Run live desktop/mobile browser interaction tests for mode switches, all
  navigators, exact cursor reads, cancellation, and slow-network refinement.
- [ ] Install the missing test prerequisites in the verification environment and
  rerun the four CSV tests (`frictionless`) and `pyright` (`typing_extensions`).
- [ ] Stream completed chart coverage during cold scans instead of waiting for
  the entire scan; preserve gaps between incomplete regions.
- [ ] Add a bounded cross-request server raw-detail cache with freshness and
  authorization isolation consistent with the summary cache.
- [ ] Add reusable cache pages across overlapping domains and resource selections,
  with incremental persistence instead of full-dataset arrays and snapshots.
- [ ] Define safe persistent cache reuse across restarts and source-revision
  invalidation; do not treat TTL as a transactional snapshot guarantee.
- [ ] Profile cache lock contention and move disk I/O outside the shared lock or
  use asynchronous page I/O where measurements justify it.
- [ ] Evaluate small, lower-priority adjacent-view prefetch under a strict network
  budget; current remote loading is demand-driven without speculative prefetch.

These tasks remain open; the completed core implementation checklist above does
not imply that these performance, cache, progressive-display, or verification
follow-ups have been delivered.
