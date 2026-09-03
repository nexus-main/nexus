from __future__ import annotations

import json
from dataclasses import dataclass
from datetime import datetime, timedelta
from enum import Enum
from typing import AsyncIterable, Awaitable, Iterable, Optional, TypeVar, Union
from urllib.parse import quote
from uuid import UUID

from httpx import Response

from ._encoder import JsonEncoder
from ._shared import (HttpRequestHandler, HttpRequestHandlerAsync,
                      _json_encoder_options, _to_string)

T = TypeVar("T")

class V2:
    """A client for version V2."""
    
    _data: DataClient


    def __init__(self, invoke: HttpRequestHandler):
        """
        Initializes a new instance of V2
        
            Args:
                client: The client to use.
        """

        self._data = DataClient(invoke)


    @property
    def data(self) -> DataClient:
        """Gets the DataClient."""
        return self._data



class DataClient:
    """Provides methods to interact with data."""

    ___invoke: HttpRequestHandler
    
    def __init__(self, invoke: HttpRequestHandler):
        self.___invoke = invoke

    def get_stream(self, request: BatchStreamRequest) -> Response:
        """
        Streams multiple resources in a framed binary response.

        Args:
        """

        __url = "/api/v2/data"

        return self.___invoke(Response, "POST", __url, "application/octet-stream", "application/json", json.dumps(JsonEncoder.encode(request, _json_encoder_options)))



class V2Async:
    """A client for version V2."""
    
    _data: DataAsyncClient


    def __init__(self, invoke: HttpRequestHandlerAsync):
        """
        Initializes a new instance of V2Async
        
            Args:
                client: The client to use.
        """

        self._data = DataAsyncClient(invoke)


    @property
    def data(self) -> DataAsyncClient:
        """Gets the DataAsyncClient."""
        return self._data



class DataAsyncClient:
    """Provides methods to interact with data."""

    ___invoke: HttpRequestHandlerAsync
    
    def __init__(self, invoke: HttpRequestHandlerAsync):
        self.___invoke = invoke

    def get_stream(self, request: BatchStreamRequest) -> Awaitable[Response]:
        """
        Streams multiple resources in a framed binary response.

        Args:
        """

        __url = "/api/v2/data"

        return self.___invoke(Response, "POST", __url, "application/octet-stream", "application/json", json.dumps(JsonEncoder.encode(request, _json_encoder_options)))



@dataclass(frozen=True)
class BatchStreamRequest:
    """
    A request to stream multiple resources.

    Args:
        begin: The start date/time.
        end: The end date/time.
        resource_paths: The resource paths to stream.
    """

    begin: datetime
    """The start date/time."""

    end: datetime
    """The end date/time."""

    resource_paths: list[str]
    """The resource paths to stream."""



