import base64
import json
import uuid

from httpx import Client, MockTransport, Request, Response, codes
from nexus_api import NexusClient, ResourceCatalog

nexus_configuration_header_key = "Nexus-Configuration"

refresh_token = str(uuid.uuid1())
refresh_token_try_count: int = 0
catalog_try_count: int = 0

def _handler1(request: Request):
    global refresh_token
    global catalog_try_count
    global refresh_token_try_count

    if "catalogs" in request.url.path:
        catalog_try_count += 1
        actual = request.headers["Authorization"]

        if catalog_try_count == 1:
            assert f"Bearer 111" == actual
            return Response(codes.UNAUTHORIZED, headers={"WWW-Authenticate" : "Bearer The token expired at ..."})

        else:
            catalog_json_string = '{"Id":"my-catalog-id","Properties":null,"Resources":null}'
            assert f"Bearer 333" == actual
            return Response(codes.OK, content=catalog_json_string)

    elif "tokens/refresh" in request.url.path:
        refresh_token_try_count += 1
        requestContent = request.content.decode("utf-8")

        if refresh_token_try_count == 1:
            assert f'{{"refreshToken": "{refresh_token}"}}' == requestContent
            return Response(codes.OK, content='{ "accessToken": "111", "refreshToken": "222" }')

        else:
            assert '{"refreshToken": "222"}' == requestContent
            return Response(codes.OK, content='{ "accessToken": "333", "refreshToken": "444" }')

    else:
        raise Exception("Unsupported path.")

def can_authenticate_and_refresh_test():

    # arrange
    catalog_id = "my-catalog-id"
    expected_catalog = ResourceCatalog(catalog_id, None, None)
    http_client = Client(base_url="http://localhost", transport=MockTransport(_handler1))

    with NexusClient(http_client) as client:

        # act
        client.sign_in(refresh_token)
        actual_catalog = client.catalogs.get(catalog_id)
       
        # assert
        assert expected_catalog == actual_catalog

try_count2: int = 0

def _handler2(request: Request):
    global try_count2

    if "catalogs" in request.url.path:
        try_count2 += 1

        if (try_count2 == 1):
            assert not nexus_configuration_header_key in request.headers

        elif (try_count2 == 2):

            configuration = {
                "foo1": "bar1",
                "foo2": "bar2"
            }

            expected = base64.b64encode(json.dumps(configuration).encode("utf-8")).decode("utf-8")
            actual = request.headers[nexus_configuration_header_key]

            assert expected == actual

        elif (try_count2 == 3):
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

def can_add_configuration_test():

    # arrange
    catalog_id = "my-catalog-id"

    configuration = {
        "foo1": "bar1",
        "foo2": "bar2"
    }

    http_client = Client(base_url="http://localhost", transport=MockTransport(_handler2))

    with NexusClient(http_client) as client:

        # act
        _ = client.catalogs.get(catalog_id)

        with client.attach_configuration(configuration):
            _ = client.catalogs.get(catalog_id)

        _ = client.catalogs.get(catalog_id)

        # assert (already asserted in _handler2)
