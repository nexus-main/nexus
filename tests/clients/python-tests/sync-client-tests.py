import base64
import json
from datetime import datetime

from uuid import UUID

import pytest
from httpx import Client, MockTransport, Request, Response, SyncByteStream, codes
from nexus_api import NexusClient, NexusException

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


def _load_with_channel_fault_handler(request: Request):
    path = request.url.path

    if path == "/api/v1/catalogs/search-items":
        catalog_item_map = {
            "/A/B/C": {
                "catalog": {
                    "id": "my-catalog",
                    "properties": None,
                    "resources": None
                },
                "resource": {
                    "id": "C",
                    "properties": None,
                    "representations": None
                },
                "representation": {
                    "dataType": "float64",
                    "samplePeriod": "0.00:00:01.0000000",
                    "parameters": None
                },
                "parameters": None
            }
        }
        return Response(codes.OK, content=json.dumps(catalog_item_map))

    elif path == "/api/v2/data/streams/batch":
        batch_stream_response = {
            "sessionId": "00000000-0000-0000-0000-000000000001",
            "channels": [
                {
                    "channelId": "00000000-0000-0000-0000-000000000002",
                    "resourcePath": "/A/B/C"
                }
            ]
        }
        return Response(codes.OK, content=json.dumps(batch_stream_response))

    elif "/channel/" in path:
        return Response(codes.OK, content=b"\x00" * 17, headers={"Content-Length": "17"})

    elif path.endswith("/status"):
        status = {
            "state": "faulted",
            "faultedChannelId": "00000000-0000-0000-0000-000000000002",
            "faultedChannelResourcePath": "/A/B/C",
            "faultReason": "The data source could not read the resource."
        }
        return Response(codes.OK, content=json.dumps(status))

    else:
        raise Exception(f"Unsupported path: {path}")


def can_load_with_channel_fault_test():
    http_client = Client(base_url="https://localhost", transport=MockTransport(_load_with_channel_fault_handler))

    with NexusClient(http_client) as client:
        try:
            client.load(
                begin=datetime(2020, 1, 1),
                end=datetime(2020, 1, 2),
                resource_paths=["/A/B/C"],
                on_progress=None
            )
            assert False, "Expected NexusException"
        except NexusException as ex:
            assert ex.status_code == "N02"
            assert "/A/B/C" in ex.message
            assert "The data source could not read the resource." in ex.message


class _TrackingStream(SyncByteStream):
    def __init__(self, content: bytes):
        self.content = content
        self.closed = False

    def __iter__(self):
        yield self.content

    def close(self):
        self.closed = True


def streamed_unsuccessful_channel_has_body_and_closes_test():
    stream = _TrackingStream(b"channel failed")

    def handler(_: Request):
        return Response(codes.INTERNAL_SERVER_ERROR, stream=stream)

    http_client = Client(base_url="http://localhost", transport=MockTransport(handler))
    client = NexusClient(http_client)

    with pytest.raises(NexusException, match="channel failed"):
        client.v2.data.get_batch_stream_channel(UUID(int=1), UUID(int=2))

    assert stream.closed


@pytest.mark.parametrize(("content", "content_length", "succeeds"), [
    (b"\x00" * 8, None, False),
    (b"\x00" * 7, "7", False),
    (b"\x00" * 8, "16", False),
    (b"\x00" * 16, "8", False),
    (b"\x00" * 8, "8", True),
])
def exact_content_length_test(content: bytes, content_length: str | None, succeeds: bool):
    headers = {} if content_length is None else {"Content-Length": content_length}
    response = Response(codes.OK, headers=headers, stream=_TrackingStream(content))
    client = NexusClient(Client(base_url="http://localhost"))

    if succeeds:
        assert len(client._read_as_double(response)) == 1
    else:
        with pytest.raises(Exception):
            client._read_as_double(response)
