import base64
import json
import struct
from datetime import datetime

import pytest
from httpx import Client, MockTransport, Request, Response, SyncByteStream, codes
from nexus_api import NexusClient, NexusException
from nexus_api.V2 import BatchStreamRequest

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


def _frame(index: int, *values: float):
    payload = struct.pack(f"<{len(values)}d", *values)
    return struct.pack("<ii", index, len(payload)) + payload


def _header(index: int, payload_length: int):
    return struct.pack("<ii", index, payload_length)


def can_load_framed_response_over_http_test():
    paths = ["/A/B/C", "/A/B/D"]
    content = _frame(1, 3, 4) + _frame(0, 1, 2)

    def handler(request: Request):
        if request.url.path == "/api/v1/catalogs/search-items":
            return Response(codes.OK, content=json.dumps(_catalog_item_map(paths)))
        return Response(codes.OK, content=content)

    with NexusClient(Client(base_url="http://localhost", transport=MockTransport(handler))) as client:
        result = client.load(datetime(2020, 1, 1), datetime(2020, 1, 1, 0, 0, 2), paths, None)

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

    request = BatchStreamRequest(datetime(2020, 1, 1), datetime(2020, 1, 1, 0, 0, 1), ["/A/B/C"])
    with pytest.raises(NexusException, match="stream failed"):
        client.v2.data.get_stream(request)

    assert stream.closed


def rejects_invalid_batch_frame_test():
    response = Response(codes.OK, stream=_TrackingStream(_frame(1, 1)))
    client = NexusClient(Client(base_url="http://localhost"))

    with pytest.raises(Exception, match="resource index"):
        client._read_batch(response, [8])


def rejects_truncated_batch_frame_header_test():
    response = Response(codes.OK, stream=_TrackingStream(b"\x00"))
    client = NexusClient(Client(base_url="http://localhost"))

    with pytest.raises(Exception, match="middle of a frame"):
        client._read_batch(response, [8])


def rejects_truncated_batch_frame_payload_test():
    response = Response(codes.OK, stream=_TrackingStream(_header(0, 8) + b"\x00\x00"))
    client = NexusClient(Client(base_url="http://localhost"))

    with pytest.raises(Exception, match="middle of a frame"):
        client._read_batch(response, [8])


def rejects_incomplete_batch_stream_test():
    response = Response(codes.OK, stream=_TrackingStream(_frame(0, 1)))
    client = NexusClient(Client(base_url="http://localhost"))

    with pytest.raises(Exception, match="before all data was received"):
        client._read_batch(response, [16])
