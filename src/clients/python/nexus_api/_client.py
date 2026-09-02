from __future__ import annotations

import asyncio
import base64
from concurrent.futures import ThreadPoolExecutor
from threading import Lock
import json
import time
from array import array
from dataclasses import dataclass
from datetime import datetime, timedelta
from tempfile import NamedTemporaryFile
from typing import Callable
from typing import (Any, AsyncIterable, Callable, Iterable, NoReturn, Optional, Type,
                    TypeVar, Union, cast)
from zipfile import ZipFile
from uuid import UUID

from httpx import AsyncClient, Client, Request, Response

from ._encoder import JsonEncoder
from ._shared import NexusException, _json_encoder_options
from .V1 import V1, V1Async
from .V1 import CatalogItem, ExportParameters, TaskStatus
from .V2 import V2, V2Async
from .V2 import BatchStreamRequest, BatchStreamSessionState


T = TypeVar("T")

class _DisposableConfiguration:
    ___client : NexusClient

    def __init__(self, client: NexusClient):
        self.___client = client

    # "disposable" methods
    def __enter__(self):
        pass

    def __exit__(self, exc_type, exc_value, exc_traceback):
        self.___client.clear_configuration()

class NexusClient:
    """A client for the Nexus system."""
    
    ___configuration_header_key: str = "Nexus-Configuration"
    ___authorization_header_key: str = "Authorization"

    ___token: Optional[str]
    ___http_client: Client

    _v1: V1
    _v2: V2


    @classmethod
    def create(cls, base_url: str) -> NexusClient:
        """
        Initializes a new instance of the NexusClient
        
            Args:
                base_url: The base URL to use.
        """
        return NexusClient(Client(base_url=base_url, timeout=60.0, http2=True))

    def __init__(self, http_client: Client):
        """
        Initializes a new instance of the NexusClient
        
            Args:
                http_client: The HTTP client to use.
        """

        if http_client.base_url is None:
            raise Exception("The base url of the HTTP client must be set.")

        self.___http_client = http_client
        self.___token = None

        self._v1 = V1(self._invoke)
        self._v2 = V2(self._invoke)


    @property
    def is_authenticated(self) -> bool:
        """Gets a value which indicates if the user is authenticated."""
        return self.___token is not None

    @property
    def v1(self) -> V1:
        """Gets the client for version V1."""
        return self._v1

    @property
    def v2(self) -> V2:
        """Gets the client for version V2."""
        return self._v2



    def sign_in(self, access_token: str):
        """Signs in the user.

        Args:
            access_token: The access token.
        """

        authorization_header_value = f"Bearer {access_token}"

        if self.___authorization_header_key in self.___http_client.headers:
            del self.___http_client.headers[self.___authorization_header_key]

        self.___http_client.headers[self.___authorization_header_key] = authorization_header_value
        self.___token = access_token

    def attach_configuration(self, configuration: Any) -> Any:
        """Attaches configuration data to subsequent API requests.
        
        Args:
            configuration: The configuration data.
        """

        encoded_json = base64.b64encode(json.dumps(configuration).encode("utf-8")).decode("utf-8")

        if self.___configuration_header_key in self.___http_client.headers:
            del self.___http_client.headers[self.___configuration_header_key]

        self.___http_client.headers[self.___configuration_header_key] = encoded_json

        return _DisposableConfiguration(self)

    def clear_configuration(self) -> None:
        """Clears configuration data for all subsequent API requests."""

        if self.___configuration_header_key in self.___http_client.headers:
            del self.___http_client.headers[self.___configuration_header_key]

    def _invoke(self, typeOfT: Optional[Type[T]], method: str, relative_url: str, accept_header_value: Optional[str], content_type_value: Optional[str], content: Union[None, str, bytes, Iterable[bytes], AsyncIterable[bytes]]) -> T:

        # prepare request
        request = self._build_request_message(method, relative_url, content, content_type_value, accept_header_value)

        # send request
        response = self.___http_client.send(request, stream=typeOfT is Response)

        # process response
        if not response.is_success:
            try:
                response.read()
                message = response.text
                status_code = f"N00.{response.status_code}"

                if not message:
                    raise NexusException(status_code, f"The HTTP request failed with status code {response.status_code}.")

                else:
                    raise NexusException(status_code, f"The HTTP request failed with status code {response.status_code}. The response message is: {message}")
            finally:
                response.close()

        try:

            if typeOfT is type(None):
                return cast(T, type(None))

            elif typeOfT is Response:
                return cast(T, response)

            else:

                jsonObject = json.loads(response.text)
                return_value = JsonEncoder.decode(cast(Type[T], typeOfT), jsonObject, _json_encoder_options)

                if return_value is None:
                    raise NexusException("N01", "Response data could not be deserialized.")

                return return_value

        finally:
            if typeOfT is not Response:
                response.close()
    
    def _build_request_message(self, method: str, relative_url: str, content: Any, content_type_value: Optional[str], accept_header_value: Optional[str]) -> Request:
       
        request_message = self.___http_client.build_request(method, relative_url, content = content)

        if content_type_value is not None:
            request_message.headers["Content-Type"] = content_type_value

        if accept_header_value is not None:
            request_message.headers["Accept"] = accept_header_value

        return request_message

    # "disposable" methods
    def __enter__(self) -> NexusClient:
        return self

    def __exit__(self, exc_type, exc_value, exc_traceback):
        if (self.___http_client is not None):
            self.___http_client.close()

    def load(
        self,
        begin: datetime, 
        end: datetime, 
        resource_paths: Iterable[str],
        on_progress: Optional[Callable[[float], None]]) -> dict[str, DataResponse]:
        """This high-level methods simplifies loading multiple resources at once.

        Args:
            begin: Start date/time.
            end: End date/time.
            resource_paths: The resource paths.
            onProgress: A callback which accepts the current progress.
        """

        resource_path_list = list(resource_paths)

        if not resource_path_list:
            return {}

        if self.___http_client.base_url.scheme != "https":
            raise NexusException("N02", "Batch loading requires an HTTPS URL so HTTP/2 can be negotiated.")

        catalog_item_map = self.v1.catalogs.search_catalog_items(resource_path_list)
        session = self.v2.data.register_batch_stream(BatchStreamRequest(begin, end, resource_path_list))
        total_length = sum(
            int((end - begin) / catalog_item_map[path].representation.sample_period) * 8
            for path in resource_path_list)
        consumed = [0]
        progress_lock = Lock()

        def _read_channel(channel):
            response = self.v2.data.get_batch_stream_channel(session.session_id, channel.channel_id)

            try:
                def report_progress(bytes_read):
                    with progress_lock:
                        consumed[0] += bytes_read
                        if total_length > 0 and on_progress is not None:
                            on_progress(min(1, consumed[0] / total_length))

                return (channel.resource_path, self._read_as_double(response, report_progress), None)
            except Exception as ex:
                return (channel.resource_path, None, ex)
            finally:
                response.close()

        with ThreadPoolExecutor(max_workers=len(session.channels)) as executor:
            results = list(executor.map(_read_channel, session.channels))

        errors = [(rp, ex) for (rp, _, ex) in results if ex is not None]

        if errors:
            self._create_channel_exception(session.session_id, errors[0][0], errors[0][1])

        result: dict[str, DataResponse] = {}

        for (resource_path, value, _) in results:

            catalog_item = catalog_item_map[resource_path]

            resource = catalog_item.resource

            unit = cast(str, resource.properties["unit"]) \
                if resource.properties is not None and "unit" in resource.properties and type(resource.properties["unit"]) == str \
                else None

            description = cast(str, resource.properties["description"]) \
                if resource.properties is not None and "description" in resource.properties and type(resource.properties["description"]) == str \
                else None

            sample_period = catalog_item.representation.sample_period

            result[resource_path] = DataResponse(
                catalog_item=catalog_item,
                name=resource.id,
                unit=unit,
                description=description,
                sample_period=sample_period,
                values=cast(array[float], value)
            )

        if on_progress is not None:
            on_progress(1)
                
        return result

    def _read_as_double(self, response: Response, report_progress: Optional[Callable[[int], None]] = None):
        content_length_value = response.headers.get("Content-Length")

        if content_length_value is None:
            raise Exception("The data length is unknown.")

        if not content_length_value.isascii() or not content_length_value.isdigit():
            raise Exception("The data length is invalid.")

        content_length = int(content_length_value)

        if content_length < 0 or content_length % 8 != 0:
            raise Exception("The data length is invalid.")

        buffer = bytearray(content_length)
        offset = 0

        for data in response.iter_bytes():
            end_offset = offset + len(data)

            if end_offset > content_length:
                raise Exception("The stream is longer than the declared data length.")

            buffer[offset:end_offset] = data
            offset = end_offset

            if report_progress is not None:
                report_progress(len(data))

        if offset != content_length:
            raise Exception("The stream ended early.")

        doubleBuffer = array("d")
        doubleBuffer.frombytes(buffer)

        return doubleBuffer 

    def _create_channel_exception(self, session_id: UUID, resource_path: str, error: Exception) -> NoReturn:
        try:
            status = self.v2.data.get_batch_stream_session_status(session_id)
        except:
            status = None

        if status is not None and \
            status.state == BatchStreamSessionState.FAULTED and \
            status.fault_reason:

            root_cause_path = status.faulted_channel_resource_path or resource_path
            message = f"The batch stream session faulted. Root cause channel: {root_cause_path}. Reason: {status.fault_reason}"
            raise NexusException("N02", message) from error

        raise NexusException("N02", f"The batch stream session faulted. Channel: {resource_path}. Reason: {error}") from error

    def export(
        self,
        begin: datetime, 
        end: datetime, 
        file_period: timedelta,
        file_format: Optional[str],
        resource_paths: Iterable[str],
        configuration: dict[str, object],
        target_folder: str,
        on_progress: Optional[Callable[[float, str], None]]) -> None:
        """This high-level methods simplifies exporting multiple resources at once.

        Args:
            begin: Start date/time.
            end: End date/time.
            filePeriod: The file period. Use timedelta(0) to get a single file.
            fileFormat: The target file format. If null, data will be read (and possibly cached) but not returned. This is useful for data pre-aggregation.
            resource_paths: The resource paths to export.
            configuration: The configuration.
            targetFolder: The target folder for the files to extract.
            onProgress: A callback which accepts the current progress and the progress message.
        """

        export_parameters = ExportParameters(
            begin,
            end,
            file_period,
            file_format,
            list(resource_paths),
            configuration
        )

        # Start job
        job = self.v1.jobs.export(export_parameters)

        # Wait for job to finish
        artifact_id: Optional[str] = None

        while True:
            time.sleep(1)
            
            job_status = self.v1.jobs.get_job_status(job.id)

            if (job_status.status == TaskStatus.CANCELED):
                raise Exception("The job has been cancelled.")

            elif (job_status.status == TaskStatus.FAULTED):
                raise Exception(f"The job has failed. Reason: {job_status.exception_message}")

            elif (job_status.status == TaskStatus.RAN_TO_COMPLETION):

                if (job_status.result is not None and \
                    type(job_status.result) == str):

                    artifact_id = cast(Optional[str], job_status.result)

                    break

            if job_status.progress < 1 and on_progress is not None:
                on_progress(job_status.progress, "export")

        if on_progress is not None:
            on_progress(1, "export")

        if artifact_id is None:
            raise Exception("The job result is invalid.")

        if file_format is None:
            return

        # Download zip file
        with NamedTemporaryFile() as target_stream:

            response = self.v1.artifacts.download(artifact_id)
            
            try:

                length: Optional[int] = None

                try:
                    length = int(response.headers["Content-Length"])
                except:
                    pass

                consumed = 0.0

                for data in response.iter_bytes():

                    target_stream.write(data)
                    consumed += len(data)

                    if length is not None and on_progress is not None:
                        if consumed < length:
                            on_progress(consumed / length, "download")

            finally:
                response.close()

            if on_progress is not None:
                on_progress(1, "download")

            # Extract file
            with ZipFile(target_stream, "r") as zipFile:
                zipFile.extractall(target_folder)

        if on_progress is not None:
            on_progress(1, "extract")

class _DisposableAsyncConfiguration:
    ___client : NexusAsyncClient

    def __init__(self, client: NexusAsyncClient):
        self.___client = client

    # "disposable" methods
    def __enter__(self):
        pass

    def __exit__(self, exc_type, exc_value, exc_traceback):
        self.___client.clear_configuration()

class NexusAsyncClient:
    """A client for the Nexus system."""
    
    ___configuration_header_key: str = "Nexus-Configuration"
    ___authorization_header_key: str = "Authorization"

    ___token: Optional[str]
    ___http_client: AsyncClient

    _v1: V1Async
    _v2: V2Async


    @classmethod
    def create(cls, base_url: str) -> NexusAsyncClient:
        """
        Initializes a new instance of the NexusAsyncClient
        
            Args:
                base_url: The base URL to use.
        """
        return NexusAsyncClient(AsyncClient(base_url=base_url, timeout=60.0, http2=True))

    def __init__(self, http_client: AsyncClient):
        """
        Initializes a new instance of the NexusAsyncClient
        
            Args:
                http_client: The HTTP client to use.
        """

        if http_client.base_url is None:
            raise Exception("The base url of the HTTP client must be set.")

        self.___http_client = http_client
        self.___token = None

        self._v1 = V1Async(self._invoke)
        self._v2 = V2Async(self._invoke)


    @property
    def is_authenticated(self) -> bool:
        """Gets a value which indicates if the user is authenticated."""
        return self.___token is not None

    @property
    def v1(self) -> V1Async:
        """Gets the client for version V1."""
        return self._v1

    @property
    def v2(self) -> V2Async:
        """Gets the client for version V2."""
        return self._v2



    def sign_in(self, access_token: str):
        """Signs in the user.

        Args:
            access_token: The access token.
        """

        authorization_header_value = f"Bearer {access_token}"

        if self.___authorization_header_key in self.___http_client.headers:
            del self.___http_client.headers[self.___authorization_header_key]

        self.___http_client.headers[self.___authorization_header_key] = authorization_header_value
        self.___token = access_token

    def attach_configuration(self, configuration: Any) -> Any:
        """Attaches configuration data to subsequent API requests.
        
        Args:
            configuration: The configuration data.
        """

        encoded_json = base64.b64encode(json.dumps(configuration).encode("utf-8")).decode("utf-8")

        if self.___configuration_header_key in self.___http_client.headers:
            del self.___http_client.headers[self.___configuration_header_key]

        self.___http_client.headers[self.___configuration_header_key] = encoded_json

        return _DisposableAsyncConfiguration(self)

    def clear_configuration(self) -> None:
        """Clears configuration data for all subsequent API requests."""

        if self.___configuration_header_key in self.___http_client.headers:
            del self.___http_client.headers[self.___configuration_header_key]

    async def _invoke(self, typeOfT: Optional[Type[T]], method: str, relative_url: str, accept_header_value: Optional[str], content_type_value: Optional[str], content: Union[None, str, bytes, Iterable[bytes], AsyncIterable[bytes]]) -> T:

        # prepare request
        request = self._build_request_message(method, relative_url, content, content_type_value, accept_header_value)

        # send request
        response = await self.___http_client.send(request, stream=typeOfT is Response)

        # process response
        if not response.is_success:
            try:
                await response.aread()
                message = response.text
                status_code = f"N00.{response.status_code}"

                if not message:
                    raise NexusException(status_code, f"The HTTP request failed with status code {response.status_code}.")

                else:
                    raise NexusException(status_code, f"The HTTP request failed with status code {response.status_code}. The response message is: {message}")
            finally:
                await response.aclose()

        try:

            if typeOfT is type(None):
                return cast(T, type(None))

            elif typeOfT is Response:
                return cast(T, response)

            else:

                jsonObject = json.loads(response.text)
                return_value = JsonEncoder.decode(cast(Type[T], typeOfT), jsonObject, _json_encoder_options)

                if return_value is None:
                    raise NexusException("N01", "Response data could not be deserialized.")

                return return_value

        finally:
            if typeOfT is not Response:
                await response.aclose()
    
    def _build_request_message(self, method: str, relative_url: str, content: Any, content_type_value: Optional[str], accept_header_value: Optional[str]) -> Request:
       
        request_message = self.___http_client.build_request(method, relative_url, content = content)

        if content_type_value is not None:
            request_message.headers["Content-Type"] = content_type_value

        if accept_header_value is not None:
            request_message.headers["Accept"] = accept_header_value

        return request_message

    # "disposable" methods
    async def __aenter__(self) -> NexusAsyncClient:
        return self

    async def __aexit__(self, exc_type, exc_value, exc_traceback):
        if (self.___http_client is not None):
            await self.___http_client.aclose()

    async def load(
        self,
        begin: datetime, 
        end: datetime, 
        resource_paths: Iterable[str],
        on_progress: Optional[Callable[[float], None]]) -> dict[str, DataResponse]:
        """This high-level methods simplifies loading multiple resources at once.

        Args:
            begin: Start date/time.
            end: End date/time.
            resource_paths: The resource paths.
            onProgress: A callback which accepts the current progress.
        """

        resource_path_list = list(resource_paths)

        if not resource_path_list:
            return {}

        if self.___http_client.base_url.scheme != "https":
            raise NexusException("N02", "Batch loading requires an HTTPS URL so HTTP/2 can be negotiated.")

        catalog_item_map = await self.v1.catalogs.search_catalog_items(resource_path_list)
        session = await self.v2.data.register_batch_stream(BatchStreamRequest(begin, end, resource_path_list))
        total_length = sum(
            int((end - begin) / catalog_item_map[path].representation.sample_period) * 8
            for path in resource_path_list)
        consumed = 0

        async def _read_channel(channel):
            nonlocal consumed
            response = await self.v2.data.get_batch_stream_channel(session.session_id, channel.channel_id)

            try:
                def report_progress(bytes_read):
                    nonlocal consumed
                    consumed += bytes_read
                    if total_length > 0 and on_progress is not None:
                        on_progress(min(1, consumed / total_length))

                return (channel.resource_path, await self._read_as_double(response, report_progress), None)
            except Exception as ex:
                return (channel.resource_path, None, ex)
            finally:
                await response.aclose()

        results = await asyncio.gather(*[_read_channel(channel) for channel in session.channels])

        errors = [(rp, ex) for (rp, _, ex) in results if ex is not None]

        if errors:
            await self._create_channel_exception(session.session_id, errors[0][0], errors[0][1])

        result: dict[str, DataResponse] = {}

        for (resource_path, value, _) in results:

            catalog_item = catalog_item_map[resource_path]

            resource = catalog_item.resource

            unit = cast(str, resource.properties["unit"]) \
                if resource.properties is not None and "unit" in resource.properties and type(resource.properties["unit"]) == str \
                else None

            description = cast(str, resource.properties["description"]) \
                if resource.properties is not None and "description" in resource.properties and type(resource.properties["description"]) == str \
                else None

            sample_period = catalog_item.representation.sample_period

            result[resource_path] = DataResponse(
                catalog_item=catalog_item,
                name=resource.id,
                unit=unit,
                description=description,
                sample_period=sample_period,
                values=cast(array[float], value)
            )

        if on_progress is not None:
            on_progress(1)
                
        return result

    async def _read_as_double(self, response: Response, report_progress: Optional[Callable[[int], None]] = None):
        content_length_value = response.headers.get("Content-Length")

        if content_length_value is None:
            raise Exception("The data length is unknown.")

        if not content_length_value.isascii() or not content_length_value.isdigit():
            raise Exception("The data length is invalid.")

        content_length = int(content_length_value)

        if content_length < 0 or content_length % 8 != 0:
            raise Exception("The data length is invalid.")

        buffer = bytearray(content_length)
        offset = 0

        async for data in response.aiter_bytes():
            end_offset = offset + len(data)

            if end_offset > content_length:
                raise Exception("The stream is longer than the declared data length.")

            buffer[offset:end_offset] = data
            offset = end_offset

            if report_progress is not None:
                report_progress(len(data))

        if offset != content_length:
            raise Exception("The stream ended early.")

        doubleBuffer = array("d")
        doubleBuffer.frombytes(buffer)

        return doubleBuffer 

    async def _create_channel_exception(self, session_id: UUID, resource_path: str, error: Exception) -> NoReturn:
        try:
            status = await self.v2.data.get_batch_stream_session_status(session_id)
        except:
            status = None

        if status is not None and \
            status.state == BatchStreamSessionState.FAULTED and \
            status.fault_reason:

            root_cause_path = status.faulted_channel_resource_path or resource_path
            message = f"The batch stream session faulted. Root cause channel: {root_cause_path}. Reason: {status.fault_reason}"
            raise NexusException("N02", message) from error

        raise NexusException("N02", f"The batch stream session faulted. Channel: {resource_path}. Reason: {error}") from error

    async def export(
        self,
        begin: datetime, 
        end: datetime, 
        file_period: timedelta,
        file_format: Optional[str],
        resource_paths: Iterable[str],
        configuration: dict[str, object],
        target_folder: str,
        on_progress: Optional[Callable[[float, str], None]]) -> None:
        """This high-level methods simplifies exporting multiple resources at once.

        Args:
            begin: Start date/time.
            end: End date/time.
            filePeriod: The file period. Use timedelta(0) to get a single file.
            fileFormat: The target file format. If null, data will be read (and possibly cached) but not returned. This is useful for data pre-aggregation.
            resource_paths: The resource paths to export.
            configuration: The configuration.
            targetFolder: The target folder for the files to extract.
            onProgress: A callback which accepts the current progress and the progress message.
        """

        export_parameters = ExportParameters(
            begin,
            end,
            file_period,
            file_format,
            list(resource_paths),
            configuration
        )

        # Start job
        job = await self.v1.jobs.export(export_parameters)

        # Wait for job to finish
        artifact_id: Optional[str] = None

        while True:
            await asyncio.sleep(1)
            
            job_status = await self.v1.jobs.get_job_status(job.id)

            if (job_status.status == TaskStatus.CANCELED):
                raise Exception("The job has been cancelled.")

            elif (job_status.status == TaskStatus.FAULTED):
                raise Exception(f"The job has failed. Reason: {job_status.exception_message}")

            elif (job_status.status == TaskStatus.RAN_TO_COMPLETION):

                if (job_status.result is not None and \
                    type(job_status.result) == str):

                    artifact_id = cast(Optional[str], job_status.result)

                    break

            if job_status.progress < 1 and on_progress is not None:
                on_progress(job_status.progress, "export")

        if on_progress is not None:
            on_progress(1, "export")

        if artifact_id is None:
            raise Exception("The job result is invalid.")

        if file_format is None:
            return

        # Download zip file
        with NamedTemporaryFile() as target_stream:

            response = await self.v1.artifacts.download(artifact_id)
            
            try:

                length: Optional[int] = None

                try:
                    length = int(response.headers["Content-Length"])
                except:
                    pass

                consumed = 0.0

                async for data in response.aiter_bytes():

                    target_stream.write(data)
                    consumed += len(data)

                    if length is not None and on_progress is not None:
                        if consumed < length:
                            on_progress(consumed / length, "download")

            finally:
                await response.aclose()

            if on_progress is not None:
                on_progress(1, "download")

            # Extract file
            with ZipFile(target_stream, "r") as zipFile:
                zipFile.extractall(target_folder)

        if on_progress is not None:
            on_progress(1, "extract")


@dataclass(frozen=True)
class DataResponse:
    """
    Result of a data request with a certain resource path.

    Args:
        catalog_item: The catalog item.
        name: The resource name.
        unit: The optional resource unit.
        description: The optional resource description.
        sample_period: The sample period.
        values: The data.
    """

    catalog_item: CatalogItem
    """The catalog item."""

    name: Optional[str]
    """The resource name."""

    unit: Optional[str]
    """The optional resource unit."""

    description: Optional[str]
    """The optional resource description."""

    sample_period: timedelta
    """The sample period."""

    values: array[float]
    """The data."""
