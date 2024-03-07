import base64
import json

import pytest
from httpx import AsyncClient, MockTransport, Request, Response, codes
from nexus_api import NexusAsyncClient

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

    elif "refresh-token" in request.url.path:
        requestContent = request.content.decode("utf-8")
        assert '{"refreshToken": "456"}' == requestContent

        new_token_pair_json_string = '{ "accessToken": "123", "refreshToken": "456" }'
        return Response(codes.OK, content=new_token_pair_json_string)

    else:
        raise Exception("Unsupported path.")

@pytest.mark.asyncio
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
        _ = await client.catalogs.get(catalog_id)

        with client.attach_configuration(configuration):
            _ = await client.catalogs.get(catalog_id)

        _ = await client.catalogs.get(catalog_id)

        # assert (already asserted in _handler)
