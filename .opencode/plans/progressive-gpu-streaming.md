# Progressive GPU Streaming Plan

## Goal
Stream data progressively to GPU/WebGPU for chart rendering so the GPU starts
decimation as data arrives, instead of waiting for the full download to complete.

## Background / Motivation
The current data pipeline is sequential and blocking:

1. `DataView.razor:153` fully awaits `Client.LoadAsync<float>(...)` — ALL data
   must arrive before any processing.
2. `NexusClient.g.cs:490` `ReadBatchAsync<T>` reads the framed stream into
   pre-allocated `T[][]` arrays (zero-copy via `CastMemoryManager`).
3. `DataView.razor:166-184` constructs `LineSeries` with `float[]` from
   `dataResponse.Values`, wraps in `LineSeriesData`, passes to `<Chart>`.
4. `Chart.razor.cs:546-557` `PrepareSeriesAsync` uploads to GPU in 4M-float
   chunks via `beginChunkedSeries` / `appendChunkedSeries` / `completeChunkedSeries`
   JS interop. GPU does per-chunk decimation during upload.

**The barrier**: the GPU sits idle during the entire download, then does all
the work after. The server already streams frames (`DataService.cs:115`
`ReadBatchAsStreamAsync` flushes per frame), and the JS side already supports
progressive per-chunk upload + decimation. Only the C# client + UI block.

## Constraints & Preferences
- Do **not** manually patch generated files; fix the generator template and
  regenerate.
- Generator lives in `/home/wilvin/dev/openapi-client-generator` (separate repo,
  branch `master`).
- The `onFrame` callback must be **non-breaking** — an optional parameter that
  defaults to null. Existing callers unaffected.
- Blazor WASM is single-threaded; async/await interleaving provides natural
  pipelining between network reads and GPU uploads. No new threading.
- `float[]` remains the backing store for chart data — needed for raw chunk
  access (zoom/pan) and zero-copy WebGPU upload.
- V1 API must NOT be changed.

## Callback Design: `T[]` not `Memory<T>`
The `onFrame` callback passes the **full pre-allocated backing buffer** as
`T[]`, not a per-frame `Memory<T>` slice, because:

1. `ReadBatchAsync` allocates `T[][] values` internally; the arrays live for
   the whole stream. The callback hands the caller a reference to
   `values[resourceIndex]` so it can construct `LineSeriesSource(buffer)` on
   the first frame and keep reading from it as more frames arrive.
2. `LineSeriesSource(float[] values)` takes `float[]`; `Memory<T>` would force
   an unwrap via `MemoryMarshal.TryGetArray` on every first-frame path.
3. The same array is returned in `DataResponse<T>.Values` at completion — one
   backing store, no copy.
4. `Span<T>` can't cross async (`ReadBatchAsync` is async, callback fires
   inside it; `Span<T>` is a ref struct).
5. The GPU needs the total `length` for `beginChunkedSeries` overview buffer
   allocation, and `LineSeriesSource` wraps the full array. A per-frame
   `Memory<T>` slice couldn't build `LineSeries` early.

The caller derives any slice it needs via
`buffer.AsMemory(0, writtenByteLength / Unsafe.SizeOf<T>())`.

## Phases

### Phase 1 — Generator template (`CSharpTemplate_Main.cs`)
Add `onFrame` callback to `Load<T>`, `LoadAsync<T>` (interface + impl), and
`ReadBatchAsync<T>` in the openapi-client-generator C# template.

- **Signature** (on `ReadBatchAsync` and `Load`/`LoadAsync`):
  `Action<int, T[], int>? onFrame = default`
  invoked as `onFrame?.Invoke(resourceIndex, values[resourceIndex], offsets[resourceIndex]);`
  — `(resourceIndex, buffer, writtenByteLength)`.
- Fire after each frame write inside the `ReadBatchAsync` while-loop, right
  after `offsets[resourceIndex] += payloadLength;` and before/after
  `reportProgress?.Invoke(payloadLength);`.
- Non-breaking (optional param, defaults to null). Existing callers unaffected.
- Regenerate `NexusClient.g.cs` + `openapi.json` via
  `dotnet run --project src/Nexus.ClientGenerator/Nexus.ClientGenerator.csproj -- ./ openapi.json`.

**Files**:
- `/home/wilvin/dev/openapi-client-generator/src/Apollo3zehn.OpenApiClientGenerator/Templates/CSharpTemplate_Main.cs`
  (interface declarations ~line 63-85, impl ~line 362-432, `ReadBatchAsync`
  ~line 499-565)
- `/home/wilvin/dev/nexus/src/clients/dotnet/NexusClient.g.cs` (regenerated)

### Phase 2 — `LineSeriesSource` stream-awareness (`ChartTypes.cs`)
Add stream-awareness to `LineSeriesSource` so `PrepareSeriesAsync` can wait
for data to arrive rather than assuming the whole `float[]` is populated.

- `public int WrittenLength { get; private set; }` — float count written so
  far (0 → Length).
- `public void ReportWritten(int byteLength)` — updates `WrittenLength` and
  signals waiters. `WrittenLength += byteLength / sizeof(float)` (or set to
  `byteLength / sizeof(float)` if passing cumulative bytes — decide during
  impl; current plan: cumulative byte offset from `onFrame`).
- `public Task<bool> WaitForDataAsync(int minimumLength,
  CancellationToken cancellationToken = default)` — returns `true` when
  `WrittenLength >= minimumLength` or stream completes, `false` on
  error/cancellation.
- `public void ReportComplete()` / `public void ReportError(Exception)` —
  unblock all waiters.
- `TaskCompletionSource` pattern with `RunContinuationsAsynchronously` (avoids
  re-entrancy in single-threaded WASM). Multiple waiters at different
  thresholds: a single `TaskCompletionSource<bool>` plus re-check on signal,
  OR a list of `(threshold, TCS)` pairs woken on each `ReportWritten`.

**Files**:
- `/home/wilvin/dev/nexus/src/Nexus.UI/Charts/ChartTypes.cs` (`LineSeriesSource`
  ~line 47-71)

### Phase 3 — `DataView.LoadDataAsync` restructure
Restructure `LoadDataAsync` to create `LineSeries` early (before the download
finishes) and feed `onFrame` data into `LineSeriesSource`.

- Pre-create `LineSeriesSource[]` (null initially). Call `LoadAsync<float>`
  with `onFrame`.
- In `onFrame(resourceIndex, buffer, writtenBytes)`:
  - First frame per resource: create `LineSeriesSource(buffer)` + `LineSeries`
    (name/unit from local `CatalogItemSelectionViewModel.BaseItem` which has
    `Resource.Id`, `Resource.Properties["unit"]`, `Representation.SamplePeriod`
    — no server lookup needed).
  - When all resources have their first frame: set `_lineSeriesData` **once**
    + `StateHasChanged` (avoids `ResetZoom` re-triggering on every frame).
  - Every frame: `sources[resourceIndex].ReportWritten(writtenBytes)`.
- After `LoadAsync` completes: `ReportComplete()` on all sources.
- In `catch (OperationCanceledException)`: `ReportError(...)` on all sources
  (unblocks `PrepareSeriesAsync` waiters).
- `LoadAsync` return value used only for completion/error detection —
  `LineSeries` metadata comes from local viewmodels.

**Files**:
- `/home/wilvin/dev/nexus/src/Nexus.UI/Components/DataView.razor`
  (`LoadDataAsync` ~line 95-215)

### Phase 4 — `PrepareSeriesAsync` stream-aware (`Chart.razor.cs:551`)
Make the GPU upload loop wait for data to arrive before reading each chunk.

- Before each chunk read:
  `await series.Source.WaitForDataAsync(offset + count, cancellationToken)`.
- `length` param stays full `Length` (GPU needs total size for
  `beginChunkedSeries` overview buffer).
- Stream end/error handled by `WaitForDataAsync` return —
  `PrepareSeriesAsync` bails gracefully (returns `SeriesRange` with
  `hasValue: false`).
- Cancellation: `WaitForDataAsync` accepts a `CancellationToken` tied to the
  chart's disposal/webGpuGeneration changes.

**Files**:
- `/home/wilvin/dev/nexus/src/Nexus.UI/Charts/Chart.razor.cs`
  (`PrepareSeriesAsync` ~line 535-595)

### Phase 5 (optional) — Progressive rendering
Call `_skiaView.Invalidate()` after each chunk upload for progressive visual
updates. JS-side: allow rendering from partial `overviewBuffer` during an
active upload session.

- Only worth doing if Phases 1-4 don't already give a good enough perceived
  latency win. Decide after measuring.

**Files**:
- `/home/wilvin/dev/nexus/src/Nexus.UI/Charts/Chart.razor.cs`
- `/home/wilvin/dev/nexus/src/Nexus/wwwroot/js/chart.webgpu.data.js`

## Verification
- Build the solution: `dotnet build src/Nexus/Nexus.csproj` and
  `dotnet build src/Nexus.UI/Nexus.UI.csproj`.
- Regenerate clients + `openapi.json` and confirm
  `diff --strip-trailing-cr openapi.json openapi_new.json` is empty.
- Run .NET tests: `dotnet test tests/Nexus.Tests/Nexus.Tests.csproj`.
- Run UI tests: `dotnet test tests/Nexus.UI.Tests/Nexus.UI.Tests.csproj`.
- Manual: load a large dataset in the running app and confirm the chart begins
  rendering before the download completes (GPU upload interleaves with
  network reads).

## Key Decisions
- `onFrame` callback signature: `Action<int, T[], int>?` =
  `(resourceIndex, buffer, writtenByteLength)` — `T[]` not `Memory<T>`
  (stable full-buffer reference; matches `DataResponse.Values`; `Span<T>`
  can't cross async).
- `GetSeriesLength` returns full `Length`; `beginChunkedSeries` needs total
  size. `HasSeriesVersion` tracks `(0, fullLength)`. No change needed.
- UI has all metadata locally via `CatalogItemSelectionViewModel.BaseItem` —
  no server lookup needed for early `LineSeries` creation.
- `TaskCompletionSource` with `RunContinuationsAsynchronously` for
  single-threaded WASM safety.
- Cancellation is safe: `DataView._cts` cancellation → `LoadAsync` throws →
  `ReportError` on sources → `WaitForDataAsync` unblocks →
  `PrepareSeriesAsync` bails.
