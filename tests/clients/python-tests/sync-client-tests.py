import base64
import io
import json
from datetime import datetime

import pyarrow as pa
import pyarrow.ipc as pa_ipc
import pytest
from httpx import Client, MockTransport, Request, Response, SyncByteStream, codes
from nexus_api import NexusClient, NexusException
from nexus_api.V2 import BatchStreamRequest, Precision

nexus_configuration_header_key = "Nexus-Configuration"

try_count: int = 0

def _handler(request: Request):
    global try_count

    if "catalogs" in request.url.path:
        try_count += 1

        if (try_count == 1):
            assert not nexus_configuration_header_key in request.headers

        elif (try_count == 2):

            configuration = {
                "foo1": "bar1",
                "foo2": "bar2"
            }

            expected = base64.b64encode(json.dumps(configuration).encode("utf-8")).decode("utf-8")
            actual = request.headers[nexus_configuration_header_key]

            assert expected == actual

        elif (try_count == 3):
            assert not nexus_configuration_header_key in request.headers

        catalog_json_string = '{"Id":"my-catalog-id","Properties":null,"Resources":null}'
        return Response(codes.OK, content=catalog_json_string)

    else:
        raise Exception("Unsupported path.")

def can_add_configuration_test():

    # arrange
    catalog_id = "my-catalog-id"

    configuration = {
        "foo1": "bar1",
        "foo2": "bar2"
    }

    http_client = Client(base_url="http://localhost", transport=MockTransport(_handler))

    with NexusClient(http_client) as client:

        # act
        _ = client.v1.catalogs.get(catalog_id)

        with client.attach_configuration(configuration):
            _ = client.v1.catalogs.get(catalog_id)

        _ = client.v1.catalogs.get(catalog_id)

        # assert (already asserted in _handler)


def _catalog_item_map(paths: list[str]):
    return {path: {
        "catalog": {"id": "my-catalog", "properties": None, "resources": None},
        "resource": {"id": path.rsplit("/", 1)[-1], "properties": None, "representations": None},
        "representation": {"dataType": "float64", "samplePeriod": "0.00:00:01.0000000", "parameters": None},
        "parameters": None
    } for path in paths}


def _arrow_stream(*rows: tuple[int, int, list[float]], value_type: pa.DataType = pa.float32()):
    schema = pa.schema([
        pa.field("resourceIndex", pa.int32(), nullable=False),
        pa.field("offset", pa.int64(), nullable=False),
        pa.field("values", pa.list_(value_type), nullable=False)
    ])

    batch = pa.RecordBatch.from_arrays([
        pa.array([row[0] for row in rows], type=pa.int32()),
        pa.array([row[1] for row in rows], type=pa.int64()),
        pa.array([row[2] for row in rows], type=pa.list_(value_type))
    ], schema=schema)

    stream = io.BytesIO()

    with pa_ipc.new_stream(stream, schema) as writer:
        writer.write_batch(batch)

    return stream.getvalue()


def _invalid_schema_arrow_stream():
    schema = pa.schema([pa.field("unexpected", pa.int32(), nullable=False)])
    batch = pa.RecordBatch.from_arrays([pa.array([1], type=pa.int32())], schema=schema)
    stream = io.BytesIO()

    with pa_ipc.new_stream(stream, schema) as writer:
        writer.write_batch(batch)

    return stream.getvalue()


def can_load_interleaved_arrow_rows_test():
    paths = ["/A/B/C", "/A/B/D"]
    content = _arrow_stream((1, 0, [3, 4]), (0, 0, [1, 2]))

    def handler(request: Request):
        if request.url.path == "/api/v1/catalogs/search-items":
            return Response(codes.OK, content=json.dumps(_catalog_item_map(paths)))
        return Response(codes.OK, content=content)

    with NexusClient(Client(base_url="http://localhost", transport=MockTransport(handler))) as client:
        result = client.load(datetime(2020, 1, 1), datetime(2020, 1, 1, 0, 0, 2), paths, Precision.FLOAT32, None)

    assert list(result[paths[0]].values) == [1, 2]
    assert list(result[paths[1]].values) == [3, 4]


class _TrackingStream(SyncByteStream):
    def __init__(self, content: bytes):
        self.content = content
        self.closed = False

    def __iter__(self):
        yield self.content

    def close(self):
        self.closed = True


def streamed_unsuccessful_response_has_body_and_closes_test():
    stream = _TrackingStream(b"stream failed")

    def handler(_: Request):
        return Response(codes.INTERNAL_SERVER_ERROR, stream=stream)

    http_client = Client(base_url="http://localhost", transport=MockTransport(handler))
    client = NexusClient(http_client)

    request = BatchStreamRequest(datetime(2020, 1, 1), datetime(2020, 1, 1, 0, 0, 1), ["/A/B/C"], Precision.FLOAT32)
    with pytest.raises(NexusException, match="stream failed"):
        client.v2.data.get_stream(request)

    assert stream.closed


def rejects_invalid_arrow_resource_index_test():
    response = Response(codes.OK, stream=_TrackingStream(_arrow_stream((1, 0, [1]))))
    client = NexusClient(Client(base_url="http://localhost"))

    with pytest.raises(Exception, match="resource index"):
        client._read_batch(response, [4], Precision.FLOAT32)


def rejects_invalid_arrow_stream_test():
    response = Response(codes.OK, stream=_TrackingStream(b"not an arrow stream"))
    client = NexusClient(Client(base_url="http://localhost"))

    with pytest.raises(Exception, match="Arrow data stream failed"):
        client._read_batch(response, [4], Precision.FLOAT32)


def rejects_invalid_arrow_schema_test():
    response = Response(codes.OK, stream=_TrackingStream(_invalid_schema_arrow_stream()))
    client = NexusClient(Client(base_url="http://localhost"))

    with pytest.raises(Exception, match="schema"):
        client._read_batch(response, [4], Precision.FLOAT32)


def rejects_incomplete_arrow_stream_test():
    content = _arrow_stream((0, 0, [1]))[:-4]
    response = Response(codes.OK, stream=_TrackingStream(content))
    client = NexusClient(Client(base_url="http://localhost"))

    with pytest.raises(Exception, match="Arrow data stream failed|ended before all data"):
        client._read_batch(response, [8], Precision.FLOAT32)


def rejects_out_of_order_arrow_stream_test():
    response = Response(codes.OK, stream=_TrackingStream(_arrow_stream((0, 1, [1]))))
    client = NexusClient(Client(base_url="http://localhost"))

    with pytest.raises(Exception, match="out-of-order"):
        client._read_batch(response, [4], Precision.FLOAT32)
