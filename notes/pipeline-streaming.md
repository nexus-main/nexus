# Pipeline Streaming

This note describes the end-to-end data path from an HTTP request to the data source `ReadAsync` call and back, with emphasis on how `System.IO.Pipelines` and the per-resource `ReadRequest.CompleteAsync` callback fit together.

# Overview

Nexus streams time-series data to clients through `System.IO.Pipelines`, which limits intermediate copying and provides threshold-based back-pressure. It is not a strictly zero-copy path: Nexus converts source data into pipe buffers, and the HTTP stack or a reverse proxy may copy or buffer it again. A `Pipe` has a `PipeWriter` (producer side) and a `PipeReader` (consumer side). The producer calls `GetMemory`, writes into the returned `Memory<byte>`, calls `Advance`, then `FlushAsync`. The consumer reads via `ReadAsync` or `AsStream()`.

There are two HTTP entry points:

| Endpoint | Protocol | Resources | Pipe topology |
|---|---|---|---|
| `GET /api/v1/data` | any | 1 | one `Pipe`, wrapped in `DataSourceDoubleStream` |
| `POST /api/v2/data/streams/batch` + `GET .../channel/{id}` | HTTP/2 | 1–100 | one `Pipe` per resource, held by `DataStreamSession` |

Both paths ultimately invoke the same static `DataSourceController.ReadAsync` method, which chunks the time range, dispatches `ReadRequest[]` arrays to `IDataSource.ReadAsync`, and flushes results into the pipe writers.

# v1 Single-Resource Path

```
HTTP GET /api/v1/data?resourcePath=...&begin=...&end=...
  -> DataController.GetStreamAsync
    -> DataService.ReadAsStreamAsync
      -> DataSourceController.ReadAsStream (extension method)
        -> new Pipe()                         // one pipe for the single resource
        -> DataSourceDoubleStream(reader)     // wraps PipeReader.AsStream()
        -> controller.ReadSingleAsync(...)    // starts background read task
        -> return stream                      // controller disposed after task completes
      -> File(stream, "application/octet-stream")
        -> ASP.NET copies stream to HTTP response body
```

The `DataSourceDoubleStream` (`src/Nexus/Extensibility/DataSource/DataSourceDoubleStream.cs`) exists so the browser can observe download progress: it exposes `Length` (total byte count) while forwarding `ReadAsync` calls to the underlying `PipeReader.AsStream()`. The pipe is completed when the background read task finishes.

The controller is disposed in the `finally` block of that background task — not in the HTTP handler — because the stream outlives the request handler.

# v2 Batch Streaming Path

```
HTTP POST /api/v2/data/streams/batch
  -> DataController.RegisterBatchStreamAsync
    -> DataService.RegisterBatchStreamAsync
      -> one Pipe per resource (grouped by CatalogContainer)
      -> DataStreamSession(sessionId, readingGroups, channels, ...)
      -> streamSessionManager.Register(session)
      -> return BatchStreamResponse { sessionId, channels[] }

HTTP GET /api/v2/data/streams/batch/{sessionId}/channel/{channelId}  (one per channel)
  -> DataController.GetBatchStreamChannelAsync
    -> streamSessionManager.Attach(sessionId, channelId)
    -> Response.StartAsync()
    -> lease.Channel.Reader.CopyToAsync(Response.BodyWriter)
    -> finally: session.CompleteChannelAsync(channelId, faulted, ...)
```

Each channel is a separate HTTP request, normally multiplexed over HTTP/2, and a batch is capped at 100 channels. This conservative cap reflects the 100-stream initial value recommended for `SETTINGS_MAX_CONCURRENT_STREAMS` by [RFC 7540 section 6.5.2](https://www.rfc-editor.org/rfc/rfc7540#section-6.5.2); the negotiated peer or an intermediary can still impose a lower limit. The source read starts after the first channel attaches. The one-minute attachment timeout is canceled by that first attachment; clients should open the remaining channels promptly to prevent their pipes from blocking the producer.

The pipe is configured with a 4 MB pause threshold and 2 MB resume threshold (`PipeOptions`). Crossing the pause threshold makes flushes wait for consumers to drain buffered data, bounding Nexus's per-pipe buffering under normal operation. This does not bound buffering performed by sources, clients, or reverse proxies.

When a channel faults (exception or cancellation), `CompleteChannelAsync` records the fault and cancels the entire session. This is the "cancel-everything" design: a single channel failure cancels all pipes and the source read. There is no partial-drain mode.

# The Pipe → Source Bridge: ReadRequest and CompleteAsync

The critical link between the pipe and the data source is the `ReadRequest` type:

```cs
record ReadRequest(
    string OriginalResourceName,
    CatalogItem CatalogItem,
    Memory<byte> Data,
    Memory<byte> Status)
{
    Task CompleteAsync();
}
```

`DataSourceController.ReadOriginalAsync` creates one `ReadRequestManager` per read unit and configures its completion callback to:

1. `GetMemory(targetByteCount)` on the pipe writer.
2. `ApplyRepresentationStatusByDataType` — converts the source-native data type to `FLOAT64` and applies status flags.
3. `Advance` + `FlushAsync` — push the converted data into the pipe.

The source implementation calls `await request.CompleteAsync()` when it has finished writing that resource's `Data` and `Status` buffers. This triggers the callback, which flushes exactly that resource's data into its pipe before `ReadAsync` returns. This enables per-resource streaming for sources that read resources sequentially or with varying latency.

# Phase-2 Fallback

Sources that do not call `CompleteAsync` are still supported. After `IDataSource.ReadAsync` returns, `ReadOriginalAsync` calls `CompleteAsync()` for every request. Completion is idempotent, so already-completed requests reuse their original completion task.

The `IsCompleted` flag also prevents double-flushing: if a source called `CompleteAsync` for some resources but not others, only the uncompleted ones are flushed in phase 2.

# Aggregated and Resampled Paths

`ReadAggregatedAsync` and `ReadResampledAsync` pass `onCompleted: null` to their `ReadRequest` constructions. These paths read base data, aggregate or resample it, and flush the *result* into the pipe — not the raw source data. The `OnCompleted` callback is only meaningful for the original-data path where the source owns the buffers.

# Remote Data Sources

Remote-source transport and streaming behavior are implemented outside this repository. Consult the corresponding remoting project and version; this repository does not establish its TCP framing, connection count, cycle detection, or API-level behavior.

# Efficient Use Cases

- **Many resources, sequential read**: a source that reads one file at a time can stream each resource to the client as it completes, rather than waiting for all files to be read. The client sees data flowing for the first resource while the source is still reading the second.
- **Heterogeneous latency**: if some resources are fast (local cache) and others slow (network fetch), `CompleteAsync` lets the fast ones flow immediately.
- **Remote sources**: streaming characteristics depend on the external remoting implementation.
- **Back-pressure**: the pipe's pause/resume thresholds (4 MB / 2 MB in v2 batch, default in v1) slow a fast source when the HTTP consumer is slow. `FlushAsync` completes asynchronously after sufficient data drains once the pause threshold is crossed; this protection does not extend to buffering outside the pipe.

# Suboptimal Edge Cases

- **Source reads all resources in parallel, then returns**: if the source ignores `CompleteAsync` and returns all data at once, the phase-2 fallback flushes everything after `ReadAsync` returns. No streaming benefit — all resources arrive simultaneously. This is the behavior of all pre-`CompleteAsync` sources.
- **Aggregated/resampled data**: these paths do not use `OnCompleted` at all. The base data is read, processed, and the result is flushed by the controller. There is no per-resource streaming for aggregated or resampled outputs.
- **Very short time ranges**: if the entire read fits in one chunk, there is only one `ReadAsync` call. The streaming benefit is limited to within that single call — if the source returns quickly, all data arrives at once anyway.
- **Single-resource v1 requests**: the v1 endpoint always creates exactly one pipe and one `ReadRequest`. `CompleteAsync` still works, but there is no multiplexing benefit — it is equivalent to the old flush-after-return behavior.
- **HTTP/1.1 clients hitting v2 batch**: Nexus rejects v2 batch requests received over HTTP/1.1 with status 426. Clients and reverse-proxy upstreams must use HTTP/2.
- **Reverse proxy buffering**: if a proxy buffers the channel responses (e.g., Nginx `proxy_buffering on`), back-pressure is defeated — the source writes at full speed into the proxy's buffer, then the proxy trickles to the client. The `Content-Length` header and `application/octet-stream` content type should discourage buffering, but proxies must be configured correctly.
- **Channel fault cancels everything**: there is no partial success. If one channel's HTTP connection drops, the entire session (all resources, all pipes) is canceled. This is intentional — it avoids complex drain semantics — but means a single flaky client connection can disrupt a large batch read.
