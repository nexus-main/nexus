import base64
import json

from httpx import Client, MockTransport, Request, Response, codes
from nexus_api import NexusClient

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
