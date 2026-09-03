# V2 Batch Data Stream

The `/api/v2/data` endpoint returns one binary response body containing frames for all requested resources. Frames can be interleaved between resources, but each individual resource stream preserves byte order.

Each frame has this layout:

| Field | Size | Encoding | Description |
| --- | --- | --- | --- |
| `resourceIndex` | 4 bytes | little-endian signed `int32` | Zero-based index into the request `resourcePaths` array. |
| `payloadLength` | 4 bytes | little-endian signed `int32` | Number of payload bytes following the header. |
| `payload` | `payloadLength` bytes | raw bytes | Data bytes for the resource. |

The response has no in-band header, version marker, resource count, expected length, or final success frame. Clients must use the request and catalog metadata to know the expected byte count for each resource.

EOF is considered successful only when the client has received exactly the expected byte count for every requested resource. EOF before that point is a truncated stream. Any bytes received after a resource reaches its expected byte count, negative payload lengths, truncated frame headers, truncated payloads, or resource indices outside the requested resource range are malformed-stream errors.
