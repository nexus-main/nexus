# V2 Batch Data Stream

The `/api/v2/data` endpoint returns one Apache Arrow IPC stream for all requested resources.
The response media type is `application/vnd.apache.arrow.stream`.

The request body is JSON:

```json
{
  "begin": "2026-01-01T00:00:00.0000000Z",
  "end": "2026-01-01T01:00:00.0000000Z",
  "resourcePaths": [
    "/catalog/a/1_s",
    "/catalog/b/1_s"
  ],
  "precision": "Float32"
}
```

`resourcePaths` must be non-empty, unique, and contain at most 100 paths. All requested resources must have the same sample period. `precision` is common to all resources in the request and is either `Float32` or `Float64`.

The Arrow IPC stream schema is:

| Field | Arrow type | Nullable | Description |
|---|---|---:|---|
| `resourceIndex` | `int32` | no | Zero-based index into the request `resourcePaths` array. |
| `offset` | `int64` | no | Element offset for the resource, not byte offset. |
| `values` | `list<float32>` or `list<float64>` | no | Sample values for this chunk. The child type matches `precision`. |

Each Arrow record batch contains one row per emitted chunk. A row can contain many samples in the `values` list; a row does not represent a single sample. Chunks from different resources may be interleaved, but chunks for one resource must arrive in increasing contiguous `offset` order.

The server creates one internal `Pipe` per resource and multiplexes completed pipe segments into one bounded Arrow output stream. `ReadRequest.CompleteAsync()` allows a data source to publish an individual resource before the complete batch read returns. Typical emitted chunk payloads are limited by the internal pipe segment size, currently about 4 MiB.

Initial validation, authorization, and missing-resource failures are normal HTTP error responses. Once the Arrow response has started, mid-stream failures fault or terminate the response stream; Nexus does not write custom error frames inside the Arrow stream.

Clients validate the schema, field names, field types, resource indices, offsets, precision type, and expected byte counts. A stream succeeds only when every requested resource receives exactly the expected number of elements. If the Arrow stream is truncated, malformed, out of order, or contains more/fewer values than expected, clients fail the load.

The generated .NET client reads with `ArrowStreamReader`, disposes each `RecordBatch` after copying its values, and copies Arrow value buffers into either client-owned arrays or chunks provided by the caller's buffer provider. `ArrowBuffer` instances are owned by the Arrow array/record-batch graph and are not disposed directly by the copy routine.

The generated Python client reads with `pyarrow.ipc.open_stream`. The sync path adapts `response.iter_bytes()` to a small file-like stream; the async path buffers the response into `io.BytesIO` before opening the Arrow stream. Python copies Arrow value buffers into byte arrays and returns typed memory views.

The OpenAPI document should expose the v2 data response as binary content with media type `application/vnd.apache.arrow.stream`. NSwag currently reports `FileStreamResult` responses as `application/octet-stream`, so `NexusOpenApiExtensions` contains a targeted post-process workaround for `/api/v2/data` until https://github.com/RicoSuter/NSwag/issues/3920 is resolved.
