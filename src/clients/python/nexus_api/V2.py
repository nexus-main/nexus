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
    _jobs: JobsClient


    def __init__(self, invoke: HttpRequestHandler):
        """
        Initializes a new instance of V2
        
            Args:
                client: The client to use.
        """

        self._data = DataClient(invoke)
        self._jobs = JobsClient(invoke)


    @property
    def data(self) -> DataClient:
        """Gets the DataClient."""
        return self._data

    @property
    def jobs(self) -> JobsClient:
        """Gets the JobsClient."""
        return self._jobs



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


class JobsClient:
    """Provides methods to interact with jobs."""

    ___invoke: HttpRequestHandler
    
    def __init__(self, invoke: HttpRequestHandler):
        self.___invoke = invoke

    def export(self, parameters: ExportParameters) -> Job:
        """
        Creates a new export job.

        Args:
        """

        __url = "/api/v2/jobs/export"

        return self.___invoke(Job, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(parameters, _json_encoder_options)))



class V2Async:
    """A client for version V2."""
    
    _data: DataAsyncClient
    _jobs: JobsAsyncClient


    def __init__(self, invoke: HttpRequestHandlerAsync):
        """
        Initializes a new instance of V2Async
        
            Args:
                client: The client to use.
        """

        self._data = DataAsyncClient(invoke)
        self._jobs = JobsAsyncClient(invoke)


    @property
    def data(self) -> DataAsyncClient:
        """Gets the DataAsyncClient."""
        return self._data

    @property
    def jobs(self) -> JobsAsyncClient:
        """Gets the JobsAsyncClient."""
        return self._jobs



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


class JobsAsyncClient:
    """Provides methods to interact with jobs."""

    ___invoke: HttpRequestHandlerAsync
    
    def __init__(self, invoke: HttpRequestHandlerAsync):
        self.___invoke = invoke

    def export(self, parameters: ExportParameters) -> Awaitable[Job]:
        """
        Creates a new export job.

        Args:
        """

        __url = "/api/v2/jobs/export"

        return self.___invoke(Job, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(parameters, _json_encoder_options)))



@dataclass(frozen=True)
class BatchStreamRequest:
    """
    A request to stream multiple resources.

    Args:
        begin: The start date/time.
        end: The end date/time.
        resource_paths: The resource paths to stream.
        precision: The floating point precision used for streamed sample values.
    """

    begin: datetime
    """The start date/time."""

    end: datetime
    """The end date/time."""

    resource_paths: list[str]
    """The resource paths to stream."""

    precision: Precision
    """The floating point precision used for streamed sample values."""


class Precision(Enum):
    """Specifies floating point precision for API output values."""

    FLOAT32 = 4
    """Float32"""

    FLOAT64 = 8
    """Float64"""


@dataclass(frozen=True)
class Job:
    """
    Description of a job.

    Args:
        id: The global unique identifier.
        type: The job type.
        owner: The owner of the job.
        parameters: The job parameters.
    """

    id: UUID
    """The global unique identifier."""

    type: str
    """The job type."""

    owner: str
    """The owner of the job."""

    parameters: Optional[object]
    """The job parameters."""


@dataclass(frozen=True)
class ExportParameters:
    """
    A structure for export parameters.

    Args:
        begin: The start date/time.
        end: The end date/time.
        file_period: The file period.
        type: The writer type. If null, data will be read (and possibly cached) but not returned. This is useful for data pre-aggregation.
        resource_paths: The resource paths to export.
        configuration: The configuration.
        precision: The floating point precision used for exported sample values.
    """

    begin: datetime
    """The start date/time."""

    end: datetime
    """The end date/time."""

    file_period: timedelta
    """The file period."""

    type: Optional[str]
    """The writer type. If null, data will be read (and possibly cached) but not returned. This is useful for data pre-aggregation."""

    resource_paths: list[str]
    """The resource paths to export."""

    configuration: Optional[dict[str, object]]
    """The configuration."""

    precision: Precision
    """The floating point precision used for exported sample values."""



