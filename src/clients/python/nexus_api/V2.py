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

    def register_batch_stream(self, request: BatchStreamRequest) -> BatchStreamResponse:
        """
        Registers a batch data stream session.

        Args:
        """

        __url = "/api/v2/data/streams/batch"

        return self.___invoke(BatchStreamResponse, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(request, _json_encoder_options)))

    def get_batch_stream_channel(self, session_id: UUID, channel_id: UUID) -> Response:
        """
        Gets a single channel of a registered batch data stream session.

        Args:
            session_id: The session identifier.
            channel_id: The channel identifier.
        """

        __url = "/api/v2/data/streams/batch/{sessionId}/channel/{channelId}"
        __url = __url.replace("{sessionId}", quote(str(session_id), safe=""))
        __url = __url.replace("{channelId}", quote(str(channel_id), safe=""))

        return self.___invoke(Response, "GET", __url, "application/octet-stream", None, None)

    def get_batch_stream_session_status(self, session_id: UUID) -> BatchStreamSessionStatus:
        """
        Gets the status of a batch data stream session.

        Args:
            session_id: The session identifier.
        """

        __url = "/api/v2/data/streams/batch/{sessionId}/status"
        __url = __url.replace("{sessionId}", quote(str(session_id), safe=""))

        return self.___invoke(BatchStreamSessionStatus, "GET", __url, "application/json", None, None)



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

    def register_batch_stream(self, request: BatchStreamRequest) -> Awaitable[BatchStreamResponse]:
        """
        Registers a batch data stream session.

        Args:
        """

        __url = "/api/v2/data/streams/batch"

        return self.___invoke(BatchStreamResponse, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(request, _json_encoder_options)))

    def get_batch_stream_channel(self, session_id: UUID, channel_id: UUID) -> Awaitable[Response]:
        """
        Gets a single channel of a registered batch data stream session.

        Args:
            session_id: The session identifier.
            channel_id: The channel identifier.
        """

        __url = "/api/v2/data/streams/batch/{sessionId}/channel/{channelId}"
        __url = __url.replace("{sessionId}", quote(str(session_id), safe=""))
        __url = __url.replace("{channelId}", quote(str(channel_id), safe=""))

        return self.___invoke(Response, "GET", __url, "application/octet-stream", None, None)

    def get_batch_stream_session_status(self, session_id: UUID) -> Awaitable[BatchStreamSessionStatus]:
        """
        Gets the status of a batch data stream session.

        Args:
            session_id: The session identifier.
        """

        __url = "/api/v2/data/streams/batch/{sessionId}/status"
        __url = __url.replace("{sessionId}", quote(str(session_id), safe=""))

        return self.___invoke(BatchStreamSessionStatus, "GET", __url, "application/json", None, None)



@dataclass(frozen=True)
class BatchStreamResponse:
    """
    A registered batch data stream session.

    Args:
        session_id: The session identifier.
        channels: The channels to open and consume concurrently.
    """

    session_id: UUID
    """The session identifier."""

    channels: list[BatchStreamChannel]
    """The channels to open and consume concurrently."""


@dataclass(frozen=True)
class BatchStreamChannel:
    """
    A single channel of a registered batch data stream session.

    Args:
        channel_id: The channel identifier.
        resource_path: The resource path streamed by this channel.
    """

    channel_id: UUID
    """The channel identifier."""

    resource_path: str
    """The resource path streamed by this channel."""


@dataclass(frozen=True)
class BatchStreamRequest:
    """
    A request to register a batch data stream session.

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


@dataclass(frozen=True)
class BatchStreamSessionStatus:
    """
    The status of a batch data stream session.

    Args:
        state: The session state.
        faulted_channel_id: The channel identifier that caused the fault, or null when the fault is not channel-specific.
        faulted_channel_resource_path: The resource path of the channel that caused the fault, or null when the fault is not channel-specific.
        fault_reason: A description of why the session failed, or null when the session has not faulted.
    """

    state: BatchStreamSessionState
    """The session state."""

    faulted_channel_id: Optional[UUID]
    """The channel identifier that caused the fault, or null when the fault is not channel-specific."""

    faulted_channel_resource_path: Optional[str]
    """The resource path of the channel that caused the fault, or null when the fault is not channel-specific."""

    fault_reason: Optional[str]
    """A description of why the session failed, or null when the session has not faulted."""


class BatchStreamSessionState(Enum):
    """The state of a batch data stream session."""

    ACTIVE = "ACTIVE"
    """Active"""

    COMPLETED = "COMPLETED"
    """Completed"""

    FAULTED = "FAULTED"
    """Faulted"""



