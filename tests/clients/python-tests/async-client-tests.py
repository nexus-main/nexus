import base64
import io
import json
from datetime import datetime

import pyarrow as pa
import pyarrow.ipc as pa_ipc
import pytest
from httpx import AsyncByteStream, AsyncClient, MockTransport, Request, Response, codes
from nexus_api import NexusAsyncClient, NexusException
from nexus_api.V2 import BatchStreamRequest, Precision

nexus_configuration_header_key = "Nexus-Configuration"

try_count: int = 0


@pytest.fixture
def anyio_backend():
    return "asyncio"

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

@pytest.mark.anyio
async def can_add_configuration_test():

    # arrange
    catalog_id = "my-catalog-id"

    configuration = {
        "foo1": "bar1",
        "foo2": "bar2"
    }

    http_client = AsyncClient(base_url="http://localhost", transport=MockTransport(_handler))

    async with NexusAsyncClient(http_client) as client:

        # act
        _ = await client.v1.catalogs.get(catalog_id)

        with client.attach_configuration(configuration):
            _ = await client.v1.catalogs.get(catalog_id)

        _ = await client.v1.catalogs.get(catalog_id)

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


@pytest.mark.anyio
async def can_load_interleaved_arrow_rows_test():
    paths = ["/A/B/C", "/A/B/D"]
    content = _arrow_stream((1, 0, [3, 4]), (0, 0, [1, 2]))

    def handler(request: Request):
        if request.url.path == "/api/v1/catalogs/search-items":
            return Response(codes.OK, content=json.dumps(_catalog_item_map(paths)))
        return Response(codes.OK, content=content)

    async with NexusAsyncClient(AsyncClient(base_url="http://localhost", transport=MockTransport(handler))) as client:
        result = await client.load(datetime(2020, 1, 1), datetime(2020, 1, 1, 0, 0, 2), paths, Precision.FLOAT32, None)

    assert list(result[paths[0]].values) == [1, 2]
    assert list(result[paths[1]].values) == [3, 4]


class _TrackingAsyncStream(AsyncByteStream):
    def __init__(self, content: bytes):
        self.content = content
        self.closed = False

    async def __aiter__(self):
        yield self.content

    async def aclose(self):
        self.closed = True


@pytest.mark.anyio
async def streamed_unsuccessful_response_has_body_and_closes_test():
    stream = _TrackingAsyncStream(b"stream failed")

    def handler(_: Request):
        return Response(codes.INTERNAL_SERVER_ERROR, stream=stream)

    http_client = AsyncClient(base_url="http://localhost", transport=MockTransport(handler))
    client = NexusAsyncClient(http_client)

    request = BatchStreamRequest(datetime(2020, 1, 1), datetime(2020, 1, 1, 0, 0, 1), ["/A/B/C"], Precision.FLOAT32)
    with pytest.raises(NexusException, match="stream failed"):
        await client.v2.data.get_stream(request)

    assert stream.closed


@pytest.mark.anyio
async def rejects_invalid_arrow_resource_index_test():
    response = Response(codes.OK, stream=_TrackingAsyncStream(_arrow_stream((1, 0, [1]))))
    client = NexusAsyncClient(AsyncClient(base_url="http://localhost"))

    with pytest.raises(Exception, match="resource index"):
        await client._read_batch(response, [4], Precision.FLOAT32)


@pytest.mark.anyio
async def rejects_invalid_arrow_stream_test():
    response = Response(codes.OK, stream=_TrackingAsyncStream(b"not an arrow stream"))
    client = NexusAsyncClient(AsyncClient(base_url="http://localhost"))

    with pytest.raises(Exception, match="Arrow data stream failed"):
        await client._read_batch(response, [4], Precision.FLOAT32)


@pytest.mark.anyio
async def rejects_invalid_arrow_schema_test():
    response = Response(codes.OK, stream=_TrackingAsyncStream(_invalid_schema_arrow_stream()))
    client = NexusAsyncClient(AsyncClient(base_url="http://localhost"))

    with pytest.raises(Exception, match="schema"):
        await client._read_batch(response, [4], Precision.FLOAT32)


@pytest.mark.anyio
async def rejects_incomplete_arrow_stream_test():
    content = _arrow_stream((0, 0, [1]))[:-4]
    response = Response(codes.OK, stream=_TrackingAsyncStream(content))
    client = NexusAsyncClient(AsyncClient(base_url="http://localhost"))

    with pytest.raises(Exception, match="Arrow data stream failed|ended before all data"):
        await client._read_batch(response, [8], Precision.FLOAT32)


@pytest.mark.anyio
async def rejects_out_of_order_arrow_stream_test():
    response = Response(codes.OK, stream=_TrackingAsyncStream(_arrow_stream((0, 1, [1]))))
    client = NexusAsyncClient(AsyncClient(base_url="http://localhost"))

    with pytest.raises(Exception, match="out-of-order"):
        await client._read_batch(response, [4], Precision.FLOAT32)
