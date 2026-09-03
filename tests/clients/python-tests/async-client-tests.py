import base64
import json
import struct
from datetime import datetime

import pytest
from httpx import AsyncByteStream, AsyncClient, MockTransport, Request, Response, codes
from nexus_api import NexusAsyncClient, NexusException
from nexus_api.V2 import BatchStreamRequest

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


def _frame(index: int, *values: float):
    payload = struct.pack(f"<{len(values)}d", *values)
    return struct.pack("<ii", index, len(payload)) + payload


def _header(index: int, payload_length: int):
    return struct.pack("<ii", index, payload_length)


@pytest.mark.anyio
async def can_load_framed_response_over_http_test():
    paths = ["/A/B/C", "/A/B/D"]
    content = _frame(1, 3, 4) + _frame(0, 1, 2)

    def handler(request: Request):
        if request.url.path == "/api/v1/catalogs/search-items":
            return Response(codes.OK, content=json.dumps(_catalog_item_map(paths)))
        return Response(codes.OK, content=content)

    async with NexusAsyncClient(AsyncClient(base_url="http://localhost", transport=MockTransport(handler))) as client:
        result = await client.load(datetime(2020, 1, 1), datetime(2020, 1, 1, 0, 0, 2), paths, None)

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

    request = BatchStreamRequest(datetime(2020, 1, 1), datetime(2020, 1, 1, 0, 0, 1), ["/A/B/C"])
    with pytest.raises(NexusException, match="stream failed"):
        await client.v2.data.get_stream(request)

    assert stream.closed


@pytest.mark.anyio
async def rejects_invalid_batch_frame_test():
    response = Response(codes.OK, stream=_TrackingAsyncStream(_frame(1, 1)))
    client = NexusAsyncClient(AsyncClient(base_url="http://localhost"))

    with pytest.raises(Exception, match="resource index"):
        await client._read_batch(response, [8])


@pytest.mark.anyio
async def rejects_truncated_batch_frame_header_test():
    response = Response(codes.OK, stream=_TrackingAsyncStream(b"\x00"))
    client = NexusAsyncClient(AsyncClient(base_url="http://localhost"))

    with pytest.raises(Exception, match="middle of a frame"):
        await client._read_batch(response, [8])


@pytest.mark.anyio
async def rejects_truncated_batch_frame_payload_test():
    response = Response(codes.OK, stream=_TrackingAsyncStream(_header(0, 8) + b"\x00\x00"))
    client = NexusAsyncClient(AsyncClient(base_url="http://localhost"))

    with pytest.raises(Exception, match="middle of a frame"):
        await client._read_batch(response, [8])


@pytest.mark.anyio
async def rejects_incomplete_batch_stream_test():
    response = Response(codes.OK, stream=_TrackingAsyncStream(_frame(0, 1)))
    client = NexusAsyncClient(AsyncClient(base_url="http://localhost"))

    with pytest.raises(Exception, match="before all data was received"):
        await client._read_batch(response, [16])
