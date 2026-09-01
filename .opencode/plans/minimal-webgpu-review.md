# `minimal-webgpu` branch code review

Read-only static review of all effective changes since branch creation. No
edits were made to source files during the review.

- Branch: `minimal-webgpu` (clean, tracking `origin/minimal-webgpu`)
- Merge base against `origin/dev`: `2c34b747b904417c7f79c5bf87fa2eef782d7953`
- Commits: 12 (`f08e2f4` through `a295ccc`)
- Effective diff: 13 files, 2905 insertions / 317 deletions

Verification limitations:

- `dotnet` is unavailable in this environment (`/bin/sh: dotnet: not found`),
  so the solution was not built.
- No browser/GPU execution was performed.
- No chart or WebGPU tests exist in the repo.
- `git diff --check` passed; worktree remained clean.

## Findings

### Critical

1. **No fallback when WebGPU is unavailable or fails.**
   `Chart.razor.cs:355-364,406-445` always sends series rendering through
   WebGPU. `chart.webgpu.js:650-652,1513-1517` silently returns without
   drawing if WebGPU is unavailable. Users get axes but no data. Keep the
   previous Skia renderer as fallback, or expose a clear unsupported/error
   state.

### High

2. **Initialization failures permanently poison the chart instance.**
   `chart.webgpu.js:647-761` stores the initialization promise, but exceptions
   from adapter, device, shader, or pipeline creation bypass
   `pendingInstances.delete`. Every subsequent render reuses the rejected
   promise. Clear pending state in `finally`, dispose partial resources, and
   trigger fallback/error reporting.

3. **Device loss is not handled.**
   No `device.lost` recovery. After GPU reset, sleep/resume, driver failure, or
   browser intervention, cached buffers and pipelines remain unusable. Add
   centralized device-loss handling and either recreate resources or switch to
   fallback rendering.

4. **Every chart creates a separate adapter, device, and pipeline set.**
   `chart.webgpu.js:636-763` duplicates expensive shared resources per chart.
   Multiple charts amplify memory consumption and can encounter browser/device
   limits. Share the device and immutable pipelines globally, retaining only
   canvas and data resources per chart.

5. **The advertised GPU memory budget is not a real upper bound.**
   `chart.webgpu.js:788-800` can evict only raw synthetic chunks. Persistent
   real-series buffers, overview buffers, uniforms, and auxiliary allocations
   can exceed the budget. Lowering the budget cannot reclaim these. This
   contradicts "Maximum GPU memory retained by each chart" in
   `UserSettingsView.razor:57-63`. Either enforce a hard total limit with
   visible failure behavior, or describe it accurately as a raw-chunk cache
   budget. The 512 MiB default is also aggressive per chart.

6. **Real-data ingestion has substantial temporary memory and UI-thread costs.**
   `Chart.razor.cs:609-650` converts the complete `double[]` into another
   complete float byte array. `chart.webgpu.js:1488-1504` then materializes the
   complete stream as a JS `ArrayBuffer` before GPU upload. Peak memory
   includes the source doubles, C# bytes, JS bytes, and GPU allocation. This
   is unsuitable for arbitrary huge real datasets. Support chunked uploads,
   direct float inputs, pooled buffers, or worker-side conversion.

7. **No automated verification covers the new rendering architecture.**
   There are no chart/WebGPU tests. Synthetic demo data is not sufficient
   verification. At minimum, browser integration tests should cover unsupported
   WebGPU, initialization failure, device loss, rapid render/dispose, resizing
   and DPR changes, NaNs, mixed signs, memory pressure, and deep zoom chunk
   transitions.

### Medium

8. **Mutable in-place data changes leave stale GPU data.**
   `Chart.razor.cs:604-607` versions real series using
   `RuntimeHelpers.GetHashCode(series.Data)`. In-place mutation does not change
   identity, so neither data nor range is recalculated. Add an explicit
   producer-controlled `DataVersion`, or formally require immutable array
   replacement.

9. **Fill/line ordering is visually incorrect for multiple series.**
   `chart.webgpu.js:1599-1615` renders each series' fill followed by its line.
   A later fill can cover an earlier line. Render all fills first and all
   lines afterward.

10. **NaN behavior is inconsistent and can misrepresent data.**
    The raw f32 decimator at `chart.webgpu.js:481-484` hides an entire bucket
    when any sample is NaN. Other decimators at `218-280` and `298-348` record
    `nanSeen` but ignore it when valid values are present, potentially bridging
    gaps. Define explicit gap semantics and preserve valid runs or gap
    boundaries during decimation.

11. **Render sequencing is unfinished.**
    `renderGeneration` is declared at `chart.webgpu.js:753` but never used. Most
    established-instance renders are synchronous after the initial await, so
    this is not an unrestricted race on every frame. However, concurrent
    initial renders and dispose/recreate transitions can still become stale.
    Either implement generation checks after asynchronous boundaries and
    before submission, or remove the dead field.

12. **Stale draw resources remain allocated.**
    `getDrawResources` grows resources by draw index at
    `chart.webgpu.js:1121-1151`. If fewer series are later rendered, surplus
    buffers and bind groups remain until full chart disposal. Destroy entries
    beyond the final draw index after rendering.

13. **Requesting adapter-maximum device limits reduces portability.**
    `chart.webgpu.js:662-667` requests maximum `maxBufferSize` and
    `maxStorageBufferBindingSize`. This does not improve performance and may
    make device creation fail unnecessarily. Request only concretely required
    limits, or omit them.

14. **Rendering errors are observable only in the console.**
    `chart.webgpu.js:1651-1653` catches and logs failures, while .NET cannot
    show an error or activate a fallback. Propagate status through the promise
    or an interop callback.

15. **f32 conversion is a material precision limitation.**
    `Chart.razor.cs:641-647` downcasts doubles before upload, and range
    reduction also operates on f32 data. Small variations around large offsets
    can disappear. This is a reasonable bandwidth choice, but it must be
    documented or addressed with per-series offset normalization.

16. **Worker usage creates avoidable overhead.**
    Workers are created for generation and each requested raw chunk around
    `chart.webgpu.js:927,1328`, including prefetches. This can cause worker
    proliferation and repeated startup costs. Prefer one reusable worker or a
    small pool. The cancellation protocol is mostly redundant because workers
    are immediately terminated after cancellation is posted.

17. **Line tessellation prioritizes speed over fidelity.**
    The shader at `chart.webgpu.js:115-192` expands segments independently
    without proper joins or caps. Sharp turns can exhibit cracks, bulges, or
    uneven opacity. This may be acceptable for a high-performance chart, but
    should be an explicit quality tradeoff.

18. **Synthetic detail changes abruptly at a fixed threshold.**
    `chart.webgpu.js:1387-1394` requests exact chunks only when the visible
    span is at most roughly two million samples; wider ranges use the
    overview. The strategy is valid, but the transition should be configurable
    or represented as a quality/loading state if visual differences are
    noticeable.

19. **The synthetic test page has inconsistent timing.**
    `ChartTestPage.razor.cs:34-58` gives all synthetic series the same point
    count even though WindSpeed uses 500 ms sampling and others use 1 second.
    The global end uses 500 ms, while rendering maps each equal-length series
    over the same domain. This makes that test semantically wrong. Production
    `DataView.razor:120-147` currently requests a common sample period, so
    this is not demonstrated as a general production regression. Fix the test
    lengths or support per-series temporal domains explicitly.

20. **Wheel zoom may also scroll the page.**
    The main Blazor wheel handler lacks `@onwheel:preventDefault`. The JS
    navigator handles this correctly, but the main chart does not. Add the
    event modifier or a non-passive JS listener.

21. **Animation-frame interop failures can become unhandled rejections.**
    `chart.js:66-85` correctly coalesces updates, but a rejected
    `invokeMethodAsync` escapes the asynchronous rAF callback. Add an error
    path while preserving state cleanup.

### Low / cleanup

22. **Dead and unused fields/code.**
    - `Surface` constructed in `Chart.razor.cs:411-415` is unused by JS.
    - `instance.canvas` at `chart.webgpu.js:731` is unused after creation.
    - `renderGeneration` at `chart.webgpu.js:753` is unused.
    - `nanSeen` is unused in the overview (`218-280`) and point (`298-348`)
      shaders.
    - Serialized color alpha is ignored because `colorOf` hardcodes alpha to 1
      (`chart.webgpu.js:594-601`) despite `Chart.razor.cs:389-395` sending
      `Alpha`.
    - `toFloat32Array` (`Chart.razor.cs:609-620`) should validate that byte
      length is divisible by four.

23. **Per-render canvas context reconfiguration.**
    `chart.webgpu.js:1541-1545` reconfigures the canvas context every render.
    Configure once per canvas/device unless there is a concrete reason.

24. **Duplicated and misleading UI text.**
    Cache-budget normalization is duplicated in UI settings and chart code.
    `UserSettingsView.razor:57-63` describes the budget as per-chart maximum
    GPU memory, which is inaccurate (see finding 5).

25. **Typo.**
    `index.demo.html:1` says `compontents/App.razor` instead of
    `Components/App.razor`.

26. **Maintainability.**
    The 1,688-line WebGPU module with embedded shaders is difficult to
    maintain. Split it into lifecycle/cache/shaders/render pieces after
    lifecycle and correctness behavior is stabilized.

## Architecture verdict

The overall GPU strategy is sound:

- Persistent GPU buffers avoid repeated transfers.
- GPU min/max decimation makes wide-view rendering proportional to pixel
  width rather than dataset size.
- Worker generation avoids billion-element managed arrays.
- Persistent overviews plus on-demand raw chunks are a legitimate out-of-core
  design.
- Keeping axes and text in Skia is pragmatic.

The strongest part is steady-state pan and zoom performance after data is
resident. The weakest parts are portability, failure recovery, memory
guarantees, real-data ingestion, and edge-case fidelity.

I would not yet describe the branch as production-trustworthy or generally
high quality. It is a promising and technically valid performance prototype
that needs findings 1-7 addressed before relying on it as the primary renderer.

## Recommended changes before merging

1. Add a real non-WebGPU fallback or a clear unsupported-browser UI state.
2. Fix initialization promise poisoning with try/finally and partial resource
   cleanup.
3. Handle `device.lost` centrally; share device/pipelines across charts.
4. Enforce or accurately describe the GPU memory budget.
5. Chunk or stream real-data uploads; avoid whole-series byte copies.
6. Replace identity-based data versioning with explicit `DataVersion`.
7. Draw all fills before all lines.
8. Define and implement NaN/gap-preserving decimation semantics.
9. Add render generation/cancellation checks to async render paths.
10. Add automated chart/WebGPU tests.
