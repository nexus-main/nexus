# Progressive GPU Streaming Plan

## Goal

Stream large resource data progressively through the Blazor WebAssembly client so the browser can start chart/GPU decimation before the full download has completed.

This plan addresses two related limits:

- Avoid single huge .NET arrays or single huge WASM-backed memory views.
- Reduce latency between the final network byte and the completed visualization by processing chunks as they arrive.

The solution must keep downsampling/decimation in the browser. It must not move visualization downsampling to the server.

## Current State

The current OOM fix introduced caller-owned memory for the generated C# client:

```csharp
Func<string, int, Memory<T>>? bufferProvider = default
```

Current semantics:

- The generated client computes the full required element count for each resource.
- The UI may provide one writable `Memory<T>` per resource.
- In the Blazor UI, `DataView` rents `IMemoryOwner<float>` values from `MemoryPool<float>.Shared`.
- `DataView` owns and disposes the memory owners when the visualization is invalidated or the component is disposed.
- `DataResponse<T>.Values` is `ReadOnlyMemory<T>`.

This fixes reload OOMs caused by hidden generated-client `new float[...]` allocations and old/new visualization memory overlap, but it still uses one contiguous memory block per resource. That still runs into array/object/WASM/browser limits for very large resources.

## Browser And Runtime Constraints

- .NET arrays and `Memory<T>` lengths are `int`-bounded.
- A single very large managed object or WASM-backed view is not portable across browsers.
- Firefox/SpiderMonkey has practical hard limits around 2 GB `ArrayBuffer` usage.
- Firefox WebGPU currently has failures when `GPUQueue.writeBuffer()`, `writeTexture`, `setBindGroup`, or `setImmediateData` receives an `ArrayBufferView` from a WASM heap larger than 2 GB.
- Chromium/V8 has better wasm32 memory support, but relying on Chromium-only behavior is not acceptable.
- WebGPU uploads should stay comfortably below browser and adapter limits.

## Design Principle

Extend the existing caller-owned buffer-provider model. Do not introduce generated-client-owned chunk objects.

The UI must remain the owner of all rented memory:

- UI rents chunks from `MemoryPool<T>`.
- UI returns `Memory<T>` to the generated client.
- Generated client writes into the provided memory only.
- Generated client never disposes chunk memory.
- UI publishes completed chunks to the chart source.
- UI disposes all chunk owners when the visualization is invalidated, cancelled, replaced, or disposed.

## Chunk-Aware Buffer Provider Contract

Replace the current full-resource provider shape for the progressive path with a chunk-aware provider:

```csharp
Func<string, int, long, Memory<T>>? bufferProvider = default
```

Parameters:

- `resourcePath`: identifies the logical resource/series.
- `chunkLength`: exact number of elements requested for the next chunk.
- `remainingLength`: number of elements still to be delivered for this resource, including the requested chunk.

Semantics:

- The first callback for a resource has `remainingLength == fullResourceLength`.
- `chunkLength <= remainingLength` is always true.
- `remainingLength == chunkLength` means this is the final chunk for that resource.
- `chunkLength` is exact. The generated client writes exactly this many elements into the returned memory on success.
- For a given `resourcePath`, a subsequent `bufferProvider(...)` call means the previous chunk for that resource has been fully written.
- When `LoadAsync` returns successfully, the current final chunk for every resource has been fully written.
- If `LoadAsync` throws or is cancelled, the in-flight visualization is invalid and the UI must discard/dispose its buffers.
- The UI infers per-resource chunk offsets by accumulating previous `chunkLength` values. No `absoluteOffset` parameter is required for strictly sequential resource delivery.

Example for a resource with `10_000_000` floats and a max chunk size of `4_194_304`:

```csharp
bufferProvider(path, chunkLength: 4_194_304, remainingLength: 10_000_000);
bufferProvider(path, chunkLength: 4_194_304, remainingLength: 5_805_696);
bufferProvider(path, chunkLength: 1_611_392, remainingLength: 1_611_392);
```

No separate `bufferCompleted(resourcePath, offset, values)` callback is required under this contract.

## Chunk Size

Use 16 MiB as the initial byte-based chunk target and derive element counts from `Unsafe.SizeOf<T>()`:

```csharp
const int ChunkByteLength = 16 * 1024 * 1024;
var maxChunkLength = ChunkByteLength / Unsafe.SizeOf<T>();
```

Typical chunk lengths:

- `float`: `4_194_304` elements, 16 MiB.
- `double`: `2_097_152` elements, 16 MiB.
- `int`: `4_194_304` elements, 16 MiB.
- `byte`: `16_777_216` elements, 16 MiB.

This is a sensible initial default because it matches the existing chart upload chunk size in `Chart.razor.cs`, keeps WebGPU write inputs far below problematic browser limits, and avoids excessive provider calls or JS interop overhead. Keep it tunable if measurements show a different browser/device-specific sweet spot.

## Generated Client Changes

Durable generated-client changes must be made in the parallel generator repository:

```text
/home/vincent/Documents/Git/github/openapi-client-generator
```

Then regenerate Nexus clients from the Nexus repo. Do not manually edit generated `NexusClient.g.cs` except for temporary chicken-egg compile fixes.

Required C# generator changes:

- Update Nexus-special `Load<T>` and `LoadAsync<T>` signatures to use the chunk-aware provider shape.
- Keep the parameter optional.
- Compute per-resource total element counts as `long`.
- Keep each individual chunk length as `int` because `Memory<T>.Length` is `int`.
- Split each resource into exact chunks of `maxChunkLength`, with a smaller exact final chunk.
- Request chunks with `bufferProvider(resourcePath, chunkLength, remainingLength)`.
- If `bufferProvider` is null, preserve simple caller behavior by allocating memory internally. This fallback can still allocate a complete resource for non-progressive callers unless/until `DataResponse<T>` is redesigned for chunked values.
- Validate returned memory has at least `chunkLength` elements.
- Slice returned memory to `chunkLength` before writing.
- Continue to throw on stream errors, cancellation, invalid framing, or length mismatches.

Important: the progressive UI path relies on callback side effects and UI-owned chunk state. The existing `DataResponse<T>.Values` full-memory return model is not sufficient to represent resource data larger than one contiguous `Memory<T>`.

Open API decision:

- Either keep `LoadAsync` as the progressive entry point and evolve `DataResponse<T>` to expose chunk metadata/chunks, or keep current full-memory `LoadAsync` semantics for general callers and add a progressive variant using the same chunk-aware provider semantics.
- In both cases, do not introduce generated-client-owned `IMemoryOwner<T>` or disposable chunk ownership.
- For the Blazor visualization path, `LoadAsync` success is the finalization signal for the last in-flight chunk.

## UI Buffer Ownership

`DataView` should evolve from one owner per resource to many owners per resource, scoped to one visualization.

Conceptual owner container:

```csharp
private sealed class VisualizationBuffers : IDisposable
{
    private readonly Dictionary<string, ResourceState> _resources = [];

    public Memory<float> ProvideBuffer(string resourcePath, int chunkLength, long remainingLength)
    {
        var state = GetOrCreateResourceState(resourcePath, fullLength: remainingLength);

        state.PublishPreviousChunk();

        var offset = state.NextOffset;
        var owner = MemoryPool<float>.Shared.Rent(chunkLength);
        var memory = owner.Memory[..chunkLength];

        state.TrackCurrentChunk(owner, offset, chunkLength, memory);
        state.NextOffset += chunkLength;

        return memory;
    }

    public void Complete()
    {
        foreach (var state in _resources.Values)
            state.PublishPreviousChunk();
    }

    public void Dispose()
    {
        foreach (var state in _resources.Values)
            state.Dispose();

        _resources.Clear();
    }
}
```

Rules:

- The first `remainingLength` for a resource initializes the full logical series length.
- Later `remainingLength` values are used to identify the final chunk and for validation/progress.
- `ProvideBuffer` publishes the previous chunk for the same resource before renting/tracking the next chunk.
- `Complete()` publishes the final current chunks after `LoadAsync` returns successfully.
- `Dispose()` returns all rented owners to the pool.
- Failed/cancelled loads dispose without calling `Complete()`.

## Chunk-Aware Chart Source

`LineSeriesSource` must become chunk-aware instead of wrapping a single `ReadOnlyMemory<float>`.

It should know:

- Full logical length.
- Published chunk ranges.
- Whether the source is complete.
- Whether the source failed/cancelled.

Likely API shape:

```csharp
internal sealed class LineSeriesSource
{
    public long Length { get; }

    public void AddChunk(long offset, ReadOnlyMemory<float> values);
    public ValueTask WaitForDataAsync(long offset, int count, CancellationToken cancellationToken);
    public ReadOnlyMemory<float> Read(long offset, int count);
    public void Complete();
    public void Fail(Exception exception);
}
```

Use `long` for logical offsets and total length. Use `int` for individual chunk lengths and read counts.

The chart should not require one contiguous resource buffer. It should consume published chunks sequentially or by requested range.

## Chart/GPU Pipeline Changes

Current `Chart.razor.cs` already uploads to WebGPU in 4M-float chunks after the full resource has loaded.

Change it so upload/decimation starts as soon as the first chunks are available:

- `DataView` creates `LineSeriesData` and chunk-aware `LineSeriesSource` after the first provider call for each selected resource, because the first `remainingLength` gives the full logical length.
- `PrepareSeriesAsync` starts `beginChunkedSeries` using the full logical length.
- Before each upload chunk, wait until the source has enough data for that range.
- Upload the chunk to JS/WebGPU as soon as it is available.
- Continue until all chunks are uploaded and the source is complete.
- Call `completeChunkedSeries` only after the final chunk has been published and uploaded.

The JS side already has chunked upload concepts:

- `beginChunkedSeriesAsync`
- `appendChunkedSeriesAsync`
- `completeChunkedSeriesAsync`

Keep each JS/WebGPU upload chunk small, normally 16 MiB.

## DataView Flow

Target load flow:

```csharp
var buffers = new VisualizationBuffers(...);

try
{
    await ReleaseCurrentVisualizationAsync();

    await Client.LoadAsync<float>(
        begin,
        end,
        resourcePaths,
        buffers.ProvideBuffer,
        progress,
        cancellationToken);

    buffers.Complete();
    _visualizationBuffers = buffers;
    buffers = null;
}
finally
{
    buffers?.Dispose();
}
```

The actual implementation may need to set `_lineSeriesData` earlier than the `LoadAsync` return so the chart can start uploading while the download continues.

Important sequencing:

- Before a new visualization starts, remove/dispose the old chart data and old visualization buffers.
- During an in-flight load, publish chunks as provider calls advance per resource.
- On successful load completion, publish final chunks.
- On cancellation/error, dispose in-flight buffers and do not publish incomplete chunks.
- On component disposal, cancel the load and dispose all visualization buffers.

## Validation

Add/update tests for generated C# client behavior:

- Provider receives first `remainingLength` equal to full resource length.
- Provider receives exact full chunk sizes and exact final chunk size.
- `remainingLength == chunkLength` on the final chunk.
- Subsequent provider call for the same resource occurs only after previous chunk bytes were written.
- Interleaved resource frames preserve independent per-resource chunk state.
- Successful `LoadAsync` writes the final current chunk completely before returning.
- Failed/cancelled streams throw and do not require finalization.
- Provided memory shorter than `chunkLength` throws a clear `ArgumentException`.

Add/update UI tests where feasible:

- `VisualizationBuffers` publishes previous chunk on the next provider call.
- `VisualizationBuffers.Complete()` publishes final chunks.
- `VisualizationBuffers.Dispose()` disposes all owners.
- Cancelled load disposes in-flight owners without completing the source.
- Chunk-aware `LineSeriesSource` waits for missing data and returns published ranges correctly.

Manual validation:

- Load a resource larger than one chunk.
- Load a resource large enough to exceed one contiguous array/object limit if loaded as a single buffer.
- Verify chart starts processing before the full load completes.
- Verify repeated large visualizations do not OOM.
- Test Firefox and Chromium separately because their WASM/WebGPU limits differ.

## Non-Goals

- Do not add server-side visualization downsampling.
- Do not make the generated client own or dispose UI memory.
- Do not rely on browser-specific >2 GB WASM behavior.
- Do not require a single contiguous buffer for one logical resource.
