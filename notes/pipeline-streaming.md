# Pipeline Streaming

Nexus uses `System.IO.Pipelines` between data sources and HTTP responses. The path is not strictly zero-copy because source values are converted to `FLOAT64`, but pipes provide bounded buffering and asynchronous back-pressure.

# HTTP Paths

The v1 endpoint streams one resource through one pipe:

```text
GET /api/v1/data
  -> DataService.ReadAsStreamAsync
  -> DataSourceController.ReadAsync
  -> PipeReader-backed response stream
```

The v2 endpoint reads up to 100 resources and returns one multiplexed response:

```text
POST /api/v2/data
  -> DataService.ReadBatchAsStreamAsync
  -> one internal pipe per resource
  -> one bounded output pipe
  -> framed response stream
```

Each frame has an eight-byte little-endian header containing the resource index and payload length, followed by at most 64 KiB of `FLOAT64` data. Resource indices correspond to request order. EOF indicates success.

# Source Completion

`DataSourceController.ReadOriginalAsync` creates a `ReadRequest` for each resource. A source calls `await request.CompleteAsync()` after filling its data and status buffers. The completion callback converts the values to `FLOAT64`, applies status flags, and flushes that resource into its pipe immediately.

Sources that do not call `CompleteAsync()` remain supported. Nexus calls it after `IDataSource.ReadAsync` returns. Completion is idempotent, so a request is never flushed twice.

Aggregated and resampled reads flush their processed result rather than their intermediate source data.

# Back-Pressure And Failure

Resource pipes prevent a producer from outrunning the multiplexer. The bounded output pipe prevents the complete batch from outrunning the HTTP consumer. A slow client therefore propagates pressure back to the data sources without allocating one HTTP stream or large output buffer per resource.

The batch is all-or-nothing. A source failure, response failure, or client cancellation cancels the remaining work and completes the response with an error. Reverse-proxy buffering should be disabled because it moves buffering outside Nexus and weakens this feedback.

# Limitations

- Sources only provide early results when they call `CompleteAsync()` before returning.
- Aggregated and resampled resources cannot publish their final values until processing completes.
- Very short reads may finish before streaming provides a measurable latency benefit.
- Remote-source behavior also depends on its external transport implementation.
