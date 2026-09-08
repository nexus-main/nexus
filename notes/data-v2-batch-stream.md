# V2 Batch Data Stream

The `/api/v2/data` endpoint returns one binary response body containing frames for all requested resources. Frames can be interleaved between resources, but each individual resource stream preserves byte order.

The response starts with a one-byte protocol version. The current version is `1`. There is no magic marker.

After the version byte, the stream contains typed frames:

| Frame | Type byte | Layout                                              |
| ----- | --------- | --------------------------------------------------- |
| Data  | `1`       | `type`, `resourceIndex`, `payloadLength`, `payload` |
| Error | `2`       | `type`, `messageLength`, `message`                  |
| End   | `3`       | `type`                                              |

Data frame fields are:

| Field           | Size                  | Encoding                     | Description                                              |
| --------------- | --------------------- | ---------------------------- | -------------------------------------------------------- |
| `type`          | 1 byte                | unsigned byte                | Must be `1`.                                             |
| `resourceIndex` | 1 byte                | unsigned byte                | Zero-based index into the request `resourcePaths` array. |
| `payloadLength` | 4 bytes               | little-endian signed `int32` | Number of payload bytes following the header.            |
| `payload`       | `payloadLength` bytes | raw bytes                    | Data bytes for the resource.                             |

Error frame fields are:

| Field           | Size                  | Encoding                     | Description                                                                |
| --------------- | --------------------- | ---------------------------- | -------------------------------------------------------------------------- |
| `type`          | 1 byte                | unsigned byte                | Must be `2`.                                                               |
| `messageLength` | 4 bytes               | little-endian signed `int32` | Number of UTF-8 message bytes following the header.                        |
| `message`       | `messageLength` bytes | UTF-8                        | Server-side failure message. The current server caps this field at 64 KiB. |

End frame fields are:

| Field  | Size   | Encoding      | Description  |
| ------ | ------ | ------------- | ------------ |
| `type` | 1 byte | unsigned byte | Must be `3`. |

Clients must reject unsupported versions, unknown frame types, invalid resource indices, negative lengths, truncated headers, truncated payloads, and data beyond the expected byte count for a resource. Payload lengths must be aligned to the requested precision size.

EOF is not a success signal. A stream succeeds only after an explicit end frame and exactly the expected byte count for every requested resource. If the stream ends before the version byte, in the middle of a frame, or before the end frame, clients must treat it as a truncated stream. If an error frame is received, clients must fail the load with that message.
