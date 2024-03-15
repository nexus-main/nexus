# pyright: reportPrivateUsage=false

# Python <= 3.9
from __future__ import annotations

import dataclasses
import re
import typing
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from enum import Enum
from typing import (Any, Callable, ClassVar, Optional, Type, Union,
                    cast)
from uuid import UUID

from typing import TypeVar

T = TypeVar("T")

@dataclass(frozen=True)
class JsonEncoderOptions:

    property_name_encoder: Callable[[str], str] = lambda value: value
    property_name_decoder: Callable[[str], str] = lambda value: value

    encoders: dict[Type, Callable[[Any], Any]] = field(default_factory=lambda: {
        datetime:   lambda value: value.isoformat().replace("+00:00", "Z"),
        timedelta:  lambda value: _encode_timedelta(value),
        Enum:       lambda value: value.name,
        UUID:       lambda value: str(value)
    })

    decoders: dict[Type, Callable[[Type, Any], Any]] = field(default_factory=lambda: {
        datetime:   lambda       _, value: datetime.fromisoformat((value[0:26] + value[26 + 1:]).replace("Z", "+00:00")),
        timedelta:  lambda       _, value: _decode_timedelta(value),
        Enum:       lambda typeCls, value: cast(Type[Enum], typeCls)[value],
        UUID:       lambda       _, value: UUID(value)
    })

class JsonEncoder:

    @staticmethod
    def encode(value: Any, options: Optional[JsonEncoderOptions] = None) -> Any:
        options = options if options is not None else JsonEncoderOptions()
        value = JsonEncoder._try_encode(value, options)         

        return value

    @staticmethod
    def _try_encode(value: Any, options: JsonEncoderOptions) -> Any:

        # None
        if value is None:
            return None

        # list/tuple
        elif isinstance(value, list) or isinstance(value, tuple):
            value = [JsonEncoder._try_encode(current, options) for current in value]
        
        # dict
        elif isinstance(value, dict):
            value = {key:JsonEncoder._try_encode(current_value, options) for key, current_value in value.items()}

        elif dataclasses.is_dataclass(value):
            # dataclasses.asdict(value) would be good choice here, but it also converts nested dataclasses into
            # dicts, which prevents us to distinct between dict and dataclasses (important for property_name_encoder)
            value = {options.property_name_encoder(key):JsonEncoder._try_encode(value, options) for key, value in value.__dict__.items()}

        # registered encoders
        else:
            for base in value.__class__.__mro__[:-1]:
                encoder = options.encoders.get(base)

                if encoder is not None:
                    return encoder(value)

        return value

    @staticmethod
    def decode(type: Type[T], data: Any, options: Optional[JsonEncoderOptions] = None) -> T:
        options = options if options is not None else JsonEncoderOptions()
        return JsonEncoder._decode(type, data, options)

    @staticmethod
    def _decode(typeCls: Type[T], data: Any, options: JsonEncoderOptions) -> T:
       
        if data is None:
            return cast(T, None)

        if typeCls == Any:
            return data

        origin = typing.get_origin(typeCls)
        args = typing.get_args(typeCls)

        if origin is not None:

            # Optional
            if origin is Union and type(None) in args:

                baseType = args[0]
                instance3 = JsonEncoder._decode(baseType, data, options)

                return cast(T, instance3)

            # list
            elif issubclass(origin, list):

                listType = args[0]
                instance1: list = list()

                for value in data:
                    instance1.append(JsonEncoder._decode(listType, value, options))

                return cast(T, instance1)
            
            # dict
            elif issubclass(origin, dict):

                # keyType = args[0]
                valueType = args[1]

                instance2: dict = dict()

                for key, value in data.items():
                    instance2[key] = JsonEncoder._decode(valueType, value, options)

                return cast(T, instance2)

            # default
            else:
                raise Exception(f"Type {str(origin)} cannot be decoded.")
        
        # dataclass
        elif dataclasses.is_dataclass(typeCls):

            parameters = {}
            type_hints = typing.get_type_hints(typeCls)

            for key, value in data.items():

                key = options.property_name_decoder(key)
                parameter_type = cast(Type, type_hints.get(key))
                
                if (parameter_type is not None):
                    value = JsonEncoder._decode(parameter_type, value, options)
                    parameters[key] = value

            # ensure default values if JSON does not serialize default fields
            for key, value in type_hints.items():
                if not key in parameters and not typing.get_origin(value) == ClassVar:
                    
                    if (value == int):
                        parameters[key] = 0

                    elif (value == float):
                        parameters[key] = 0.0

                    else:
                        parameters[key] = None
              
            instance = cast(T, typeCls(**parameters))

            return instance

        # registered decoders
        for base in typeCls.__mro__[:-1]:
            decoder = options.decoders.get(base)

            if decoder is not None:
                return decoder(typeCls, data)

        # default
        return data

# timespan is always serialized with 7 subsecond digits (https://github.com/dotnet/runtime/blob/a6cb7705bd5317ab5e9f718b55a82444156fc0c8/src/libraries/System.Text.Json/tests/System.Text.Json.Tests/Serialization/Value.WriteTests.cs#L178-L189)
def _encode_timedelta(value: timedelta):
    hours, remainder = divmod(value.seconds, 3600)
    minutes, seconds = divmod(remainder, 60)
    result = f"{int(value.days)}.{int(hours):02}:{int(minutes):02}:{int(seconds):02}.{value.microseconds:06d}0"
    return result

timedelta_pattern = re.compile(r"^(?:([0-9]+)\.)?([0-9]{2}):([0-9]{2}):([0-9]{2})(?:\.([0-9]+))?$")

def _decode_timedelta(value: str):
    # 12:08:07
    # 12:08:07.1250000
    # 3000.00:08:07
    # 3000.00:08:07.1250000
    match = timedelta_pattern.match(value)

    if match:
        days = int(match.group(1)) if match.group(1) else 0
        hours = int(match.group(2))
        minutes = int(match.group(3))
        seconds = int(match.group(4))
        microseconds = int(match.group(5)) / 10.0 if match.group(5) else 0

        return timedelta(days=days, hours=hours, minutes=minutes, seconds=seconds, microseconds=microseconds)

    else:
        raise Exception(f"Unable to decode {value} into value of type timedelta.")

def to_camel_case(value: str) -> str:
    components = value.split("_")
    return components[0] + ''.join(x.title() for x in components[1:])

snake_case_pattern = re.compile('((?<=[a-z0-9])[A-Z]|(?!^)[A-Z](?=[a-z]))')

def to_snake_case(value: str) -> str:
    return snake_case_pattern.sub(r'_\1', value).lower()


import asyncio
import base64
import json
import time
from array import array
from dataclasses import dataclass
from datetime import datetime, timedelta
from enum import Enum
from tempfile import NamedTemporaryFile
from typing import (Any, AsyncIterable, Awaitable, Callable, Iterable,
                    Optional, Type, Union, cast)
from urllib.parse import quote
from uuid import UUID
from zipfile import ZipFile

from httpx import AsyncClient, Client, Request, Response

def _to_string(value: Any) -> str:

    if type(value) is datetime:
        return value.isoformat()

    elif type(value) is str:
        return value

    else:
        return str(value)

_json_encoder_options: JsonEncoderOptions = JsonEncoderOptions(
    property_name_encoder=lambda value: to_camel_case(value) if value != "class_" else "class",
    property_name_decoder=lambda value: to_snake_case(value) if value != "class" else "class_"
)

_json_encoder_options.encoders[Enum] = lambda value: to_camel_case(value.name)
_json_encoder_options.decoders[Enum] = lambda typeCls, value: cast(Type[Enum], typeCls)[to_snake_case(value).upper()]

class NexusException(Exception):
    """A NexusException."""

    def __init__(self, status_code: str, message: str):
        self.status_code = status_code
        self.message = message

    status_code: str
    """The exception status code."""

    message: str
    """The exception message."""

@dataclass(frozen=True)
class CatalogItem:
    """
    A catalog item consists of a catalog, a resource and a representation.

    Args:
        catalog: The catalog.
        resource: The resource.
        representation: The representation.
        parameters: The optional dictionary of representation parameters and its arguments.
    """

    catalog: ResourceCatalog
    """The catalog."""

    resource: Resource
    """The resource."""

    representation: Representation
    """The representation."""

    parameters: Optional[dict[str, str]]
    """The optional dictionary of representation parameters and its arguments."""


@dataclass(frozen=True)
class ResourceCatalog:
    """
    A catalog is a top level element and holds a list of resources.

    Args:
        id: Gets the identifier.
        properties: Gets the properties.
        resources: Gets the list of representations.
    """

    id: str
    """Gets the identifier."""

    properties: Optional[dict[str, object]]
    """Gets the properties."""

    resources: Optional[list[Resource]]
    """Gets the list of representations."""


@dataclass(frozen=True)
class Resource:
    """
    A resource is part of a resource catalog and holds a list of representations.

    Args:
        id: Gets the identifier.
        properties: Gets the properties.
        representations: Gets the list of representations.
    """

    id: str
    """Gets the identifier."""

    properties: Optional[dict[str, object]]
    """Gets the properties."""

    representations: Optional[list[Representation]]
    """Gets the list of representations."""


@dataclass(frozen=True)
class Representation:
    """
    A representation is part of a resource.

    Args:
        data_type: The data type.
        sample_period: The sample period.
        parameters: The optional list of parameters.
    """

    data_type: NexusDataType
    """The data type."""

    sample_period: timedelta
    """The sample period."""

    parameters: Optional[dict[str, object]]
    """The optional list of parameters."""


class NexusDataType(Enum):
    """Specifies the Nexus data type."""

    UINT8 = "UINT8"
    """UINT8"""

    UINT16 = "UINT16"
    """UINT16"""

    UINT32 = "UINT32"
    """UINT32"""

    UINT64 = "UINT64"
    """UINT64"""

    INT8 = "INT8"
    """INT8"""

    INT16 = "INT16"
    """INT16"""

    INT32 = "INT32"
    """INT32"""

    INT64 = "INT64"
    """INT64"""

    FLOAT32 = "FLOAT32"
    """FLOAT32"""

    FLOAT64 = "FLOAT64"
    """FLOAT64"""


@dataclass(frozen=True)
class CatalogInfo:
    """
    A structure for catalog information.

    Args:
        id: The identifier.
        title: A nullable title.
        contact: A nullable contact.
        readme: A nullable readme.
        license: A nullable license.
        is_readable: A boolean which indicates if the catalog is accessible.
        is_writable: A boolean which indicates if the catalog is editable.
        is_released: A boolean which indicates if the catalog is released.
        is_visible: A boolean which indicates if the catalog is visible.
        is_owner: A boolean which indicates if the catalog is owned by the current user.
        data_source_info_url: A nullable info URL of the data source.
        data_source_type: The data source type.
        data_source_registration_id: The data source registration identifier.
        package_reference_id: The package reference identifier.
    """

    id: str
    """The identifier."""

    title: Optional[str]
    """A nullable title."""

    contact: Optional[str]
    """A nullable contact."""

    readme: Optional[str]
    """A nullable readme."""

    license: Optional[str]
    """A nullable license."""

    is_readable: bool
    """A boolean which indicates if the catalog is accessible."""

    is_writable: bool
    """A boolean which indicates if the catalog is editable."""

    is_released: bool
    """A boolean which indicates if the catalog is released."""

    is_visible: bool
    """A boolean which indicates if the catalog is visible."""

    is_owner: bool
    """A boolean which indicates if the catalog is owned by the current user."""

    data_source_info_url: Optional[str]
    """A nullable info URL of the data source."""

    data_source_type: str
    """The data source type."""

    data_source_registration_id: UUID
    """The data source registration identifier."""

    package_reference_id: UUID
    """The package reference identifier."""


@dataclass(frozen=True)
class CatalogTimeRange:
    """
    A catalog time range.

    Args:
        begin: The date/time of the first data in the catalog.
        end: The date/time of the last data in the catalog.
    """

    begin: datetime
    """The date/time of the first data in the catalog."""

    end: datetime
    """The date/time of the last data in the catalog."""


@dataclass(frozen=True)
class CatalogAvailability:
    """
    The catalog availability.

    Args:
        data: The actual availability data.
    """

    data: list[float]
    """The actual availability data."""


@dataclass(frozen=True)
class CatalogMetadata:
    """
    A structure for catalog metadata.

    Args:
        contact: The contact.
        group_memberships: A list of groups the catalog is part of.
        overrides: Overrides for the catalog.
    """

    contact: Optional[str]
    """The contact."""

    group_memberships: Optional[list[str]]
    """A list of groups the catalog is part of."""

    overrides: Optional[ResourceCatalog]
    """Overrides for the catalog."""


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
class JobStatus:
    """
    Describes the status of the job.

    Args:
        start: The start date/time.
        status: The status.
        progress: The progress from 0 to 1.
        exception_message: The nullable exception message.
        result: The nullable result.
    """

    start: datetime
    """The start date/time."""

    status: TaskStatus
    """The status."""

    progress: float
    """The progress from 0 to 1."""

    exception_message: Optional[str]
    """The nullable exception message."""

    result: Optional[object]
    """The nullable result."""


class TaskStatus(Enum):
    """"""

    CREATED = "CREATED"
    """Created"""

    WAITING_FOR_ACTIVATION = "WAITING_FOR_ACTIVATION"
    """WaitingForActivation"""

    WAITING_TO_RUN = "WAITING_TO_RUN"
    """WaitingToRun"""

    RUNNING = "RUNNING"
    """Running"""

    WAITING_FOR_CHILDREN_TO_COMPLETE = "WAITING_FOR_CHILDREN_TO_COMPLETE"
    """WaitingForChildrenToComplete"""

    RAN_TO_COMPLETION = "RAN_TO_COMPLETION"
    """RanToCompletion"""

    CANCELED = "CANCELED"
    """Canceled"""

    FAULTED = "FAULTED"
    """Faulted"""


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


@dataclass(frozen=True)
class PackageReference:
    """
    A package reference.

    Args:
        provider: The provider which loads the package.
        configuration: The configuration of the package reference.
    """

    provider: str
    """The provider which loads the package."""

    configuration: dict[str, str]
    """The configuration of the package reference."""


@dataclass(frozen=True)
class ExtensionDescription:
    """
    An extension description.

    Args:
        type: The extension type.
        version: The extension version.
        description: A nullable description.
        project_url: A nullable project website URL.
        repository_url: A nullable source repository URL.
        additional_information: Additional information about the extension.
    """

    type: str
    """The extension type."""

    version: str
    """The extension version."""

    description: Optional[str]
    """A nullable description."""

    project_url: Optional[str]
    """A nullable project website URL."""

    repository_url: Optional[str]
    """A nullable source repository URL."""

    additional_information: Optional[dict[str, object]]
    """Additional information about the extension."""


@dataclass(frozen=True)
class DataSourceRegistration:
    """
    A data source registration.

    Args:
        type: The type of the data source.
        resource_locator: An optional URL which points to the data.
        configuration: Configuration parameters for the instantiated source.
        info_url: An optional info URL.
        release_pattern: An optional regular expressions pattern to select the catalogs to be released. By default, all catalogs will be released.
        visibility_pattern: An optional regular expressions pattern to select the catalogs to be visible. By default, all catalogs will be visible.
    """

    type: str
    """The type of the data source."""

    resource_locator: Optional[str]
    """An optional URL which points to the data."""

    configuration: Optional[dict[str, object]]
    """Configuration parameters for the instantiated source."""

    info_url: Optional[str]
    """An optional info URL."""

    release_pattern: Optional[str]
    """An optional regular expressions pattern to select the catalogs to be released. By default, all catalogs will be released."""

    visibility_pattern: Optional[str]
    """An optional regular expressions pattern to select the catalogs to be visible. By default, all catalogs will be visible."""


@dataclass(frozen=True)
class MeResponse:
    """
    A me response.

    Args:
        user_id: The user id.
        user: The user.
        is_admin: A boolean which indicates if the user is an administrator.
        personal_access_tokens: A list of personal access tokens.
    """

    user_id: str
    """The user id."""

    user: NexusUser
    """The user."""

    is_admin: bool
    """A boolean which indicates if the user is an administrator."""

    personal_access_tokens: dict[str, PersonalAccessToken]
    """A list of personal access tokens."""


@dataclass(frozen=True)
class NexusUser:
    """
    Represents a user.

    Args:
        name: The user name.
    """

    name: str
    """The user name."""


@dataclass(frozen=True)
class PersonalAccessToken:
    """
    A personal access token.

    Args:
        description: The token description.
        expires: The date/time when the token expires.
        claims: The claims that will be part of the token.
    """

    description: str
    """The token description."""

    expires: datetime
    """The date/time when the token expires."""

    claims: list[TokenClaim]
    """The claims that will be part of the token."""


@dataclass(frozen=True)
class TokenClaim:
    """
    A revoke token request.

    Args:
        type: The claim type.
        value: The claim value.
    """

    type: str
    """The claim type."""

    value: str
    """The claim value."""


@dataclass(frozen=True)
class NexusClaim:
    """
    Represents a claim.

    Args:
        type: The claim type.
        value: The claim value.
    """

    type: str
    """The claim type."""

    value: str
    """The claim value."""



class ArtifactsAsyncClient:
    """Provides methods to interact with artifacts."""

    ___client: NexusAsyncClient
    
    def __init__(self, client: NexusAsyncClient):
        self.___client = client

    def download(self, artifact_id: str) -> Awaitable[Response]:
        """
        Gets the specified artifact.

        Args:
            artifact_id: The artifact identifier.
        """

        __url = "/api/v1/artifacts/{artifactId}"
        __url = __url.replace("{artifactId}", quote(str(artifact_id), safe=""))

        return self.___client._invoke(Response, "GET", __url, "application/octet-stream", None, None)


class CatalogsAsyncClient:
    """Provides methods to interact with catalogs."""

    ___client: NexusAsyncClient
    
    def __init__(self, client: NexusAsyncClient):
        self.___client = client

    def search_catalog_items(self, resource_paths: list[str]) -> Awaitable[dict[str, CatalogItem]]:
        """
        Searches for the given resource paths and returns the corresponding catalog items.

        Args:
        """

        __url = "/api/v1/catalogs/search-items"

        return self.___client._invoke(dict[str, CatalogItem], "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(resource_paths, _json_encoder_options)))

    def get(self, catalog_id: str) -> Awaitable[ResourceCatalog]:
        """
        Gets the specified catalog.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___client._invoke(ResourceCatalog, "GET", __url, "application/json", None, None)

    def get_child_catalog_infos(self, catalog_id: str) -> Awaitable[list[CatalogInfo]]:
        """
        Gets a list of child catalog info for the provided parent catalog identifier.

        Args:
            catalog_id: The parent catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/child-catalog-infos"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___client._invoke(list[CatalogInfo], "GET", __url, "application/json", None, None)

    def get_time_range(self, catalog_id: str) -> Awaitable[CatalogTimeRange]:
        """
        Gets the specified catalog's time range.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/timerange"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___client._invoke(CatalogTimeRange, "GET", __url, "application/json", None, None)

    def get_availability(self, catalog_id: str, begin: datetime, end: datetime, step: timedelta) -> Awaitable[CatalogAvailability]:
        """
        Gets the specified catalog's availability.

        Args:
            catalog_id: The catalog identifier.
            begin: Start date/time.
            end: End date/time.
            step: Step period.
        """

        __url = "/api/v1/catalogs/{catalogId}/availability"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        __query_values: dict[str, str] = {}

        __query_values["begin"] = quote(_to_string(begin), safe="")

        __query_values["end"] = quote(_to_string(end), safe="")

        __query_values["step"] = quote(_to_string(step), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___client._invoke(CatalogAvailability, "GET", __url, "application/json", None, None)

    def get_license(self, catalog_id: str) -> Awaitable[Optional[str]]:
        """
        Gets the license of the catalog if available.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/license"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___client._invoke(str, "GET", __url, "application/json", None, None)

    def get_attachments(self, catalog_id: str) -> Awaitable[list[str]]:
        """
        Gets all attachments for the specified catalog.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/attachments"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___client._invoke(list[str], "GET", __url, "application/json", None, None)

    def upload_attachment(self, catalog_id: str, attachment_id: str, content: Union[bytes, Iterable[bytes], AsyncIterable[bytes]]) -> Awaitable[Response]:
        """
        Uploads the specified attachment.

        Args:
            catalog_id: The catalog identifier.
            attachment_id: The attachment identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/attachments/{attachmentId}"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))
        __url = __url.replace("{attachmentId}", quote(str(attachment_id), safe=""))

        return self.___client._invoke(Response, "PUT", __url, "application/octet-stream", "application/octet-stream", content)

    def delete_attachment(self, catalog_id: str, attachment_id: str) -> Awaitable[Response]:
        """
        Deletes the specified attachment.

        Args:
            catalog_id: The catalog identifier.
            attachment_id: The attachment identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/attachments/{attachmentId}"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))
        __url = __url.replace("{attachmentId}", quote(str(attachment_id), safe=""))

        return self.___client._invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def get_attachment_stream(self, catalog_id: str, attachment_id: str) -> Awaitable[Response]:
        """
        Gets the specified attachment.

        Args:
            catalog_id: The catalog identifier.
            attachment_id: The attachment identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/attachments/{attachmentId}/content"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))
        __url = __url.replace("{attachmentId}", quote(str(attachment_id), safe=""))

        return self.___client._invoke(Response, "GET", __url, "application/octet-stream", None, None)

    def get_metadata(self, catalog_id: str) -> Awaitable[CatalogMetadata]:
        """
        Gets the catalog metadata.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/metadata"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___client._invoke(CatalogMetadata, "GET", __url, "application/json", None, None)

    def set_metadata(self, catalog_id: str, metadata: CatalogMetadata) -> Awaitable[None]:
        """
        Puts the catalog metadata.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/metadata"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___client._invoke(type(None), "PUT", __url, None, "application/json", json.dumps(JsonEncoder.encode(metadata, _json_encoder_options)))


class DataAsyncClient:
    """Provides methods to interact with data."""

    ___client: NexusAsyncClient
    
    def __init__(self, client: NexusAsyncClient):
        self.___client = client

    def get_stream(self, resource_path: str, begin: datetime, end: datetime) -> Awaitable[Response]:
        """
        Gets the requested data.

        Args:
            resource_path: The path to the resource data to stream.
            begin: Start date/time.
            end: End date/time.
        """

        __url = "/api/v1/data"

        __query_values: dict[str, str] = {}

        __query_values["resourcePath"] = quote(_to_string(resource_path), safe="")

        __query_values["begin"] = quote(_to_string(begin), safe="")

        __query_values["end"] = quote(_to_string(end), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___client._invoke(Response, "GET", __url, "application/octet-stream", None, None)


class JobsAsyncClient:
    """Provides methods to interact with jobs."""

    ___client: NexusAsyncClient
    
    def __init__(self, client: NexusAsyncClient):
        self.___client = client

    def get_jobs(self) -> Awaitable[list[Job]]:
        """
        Gets a list of jobs.

        Args:
        """

        __url = "/api/v1/jobs"

        return self.___client._invoke(list[Job], "GET", __url, "application/json", None, None)

    def cancel_job(self, job_id: UUID) -> Awaitable[Response]:
        """
        Cancels the specified job.

        Args:
            job_id: 
        """

        __url = "/api/v1/jobs/{jobId}"
        __url = __url.replace("{jobId}", quote(str(job_id), safe=""))

        return self.___client._invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def get_job_status(self, job_id: UUID) -> Awaitable[JobStatus]:
        """
        Gets the status of the specified job.

        Args:
            job_id: 
        """

        __url = "/api/v1/jobs/{jobId}/status"
        __url = __url.replace("{jobId}", quote(str(job_id), safe=""))

        return self.___client._invoke(JobStatus, "GET", __url, "application/json", None, None)

    def export(self, parameters: ExportParameters) -> Awaitable[Job]:
        """
        Creates a new export job.

        Args:
        """

        __url = "/api/v1/jobs/export"

        return self.___client._invoke(Job, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(parameters, _json_encoder_options)))

    def refresh_database(self) -> Awaitable[Job]:
        """
        Creates a new job which reloads all extensions and resets the resource catalog.

        Args:
        """

        __url = "/api/v1/jobs/refresh-database"

        return self.___client._invoke(Job, "POST", __url, "application/json", None, None)

    def clear_cache(self, catalog_id: str, begin: datetime, end: datetime) -> Awaitable[Job]:
        """
        Clears the aggregation data cache for the specified period of time.

        Args:
            catalog_id: The catalog identifier.
            begin: Start date/time.
            end: End date/time.
        """

        __url = "/api/v1/jobs/clear-cache"

        __query_values: dict[str, str] = {}

        __query_values["catalogId"] = quote(_to_string(catalog_id), safe="")

        __query_values["begin"] = quote(_to_string(begin), safe="")

        __query_values["end"] = quote(_to_string(end), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___client._invoke(Job, "POST", __url, "application/json", None, None)


class PackageReferencesAsyncClient:
    """Provides methods to interact with package references."""

    ___client: NexusAsyncClient
    
    def __init__(self, client: NexusAsyncClient):
        self.___client = client

    def get(self) -> Awaitable[dict[str, PackageReference]]:
        """
        Gets the list of package references.

        Args:
        """

        __url = "/api/v1/packagereferences"

        return self.___client._invoke(dict[str, PackageReference], "GET", __url, "application/json", None, None)

    def create(self, package_reference: PackageReference) -> Awaitable[UUID]:
        """
        Creates a package reference.

        Args:
        """

        __url = "/api/v1/packagereferences"

        return self.___client._invoke(UUID, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(package_reference, _json_encoder_options)))

    def delete(self, package_reference_id: UUID) -> Awaitable[None]:
        """
        Deletes a package reference.

        Args:
            package_reference_id: The ID of the package reference.
        """

        __url = "/api/v1/packagereferences/{packageReferenceId}"
        __url = __url.replace("{packageReferenceId}", quote(str(package_reference_id), safe=""))

        return self.___client._invoke(type(None), "DELETE", __url, None, None, None)

    def get_versions(self, package_reference_id: UUID) -> Awaitable[list[str]]:
        """
        Gets package versions.

        Args:
            package_reference_id: The ID of the package reference.
        """

        __url = "/api/v1/packagereferences/{packageReferenceId}/versions"
        __url = __url.replace("{packageReferenceId}", quote(str(package_reference_id), safe=""))

        return self.___client._invoke(list[str], "GET", __url, "application/json", None, None)


class SourcesAsyncClient:
    """Provides methods to interact with sources."""

    ___client: NexusAsyncClient
    
    def __init__(self, client: NexusAsyncClient):
        self.___client = client

    def get_descriptions(self) -> Awaitable[list[ExtensionDescription]]:
        """
        Gets the list of source descriptions.

        Args:
        """

        __url = "/api/v1/sources/descriptions"

        return self.___client._invoke(list[ExtensionDescription], "GET", __url, "application/json", None, None)

    def get_registrations(self, user_id: Optional[str] = None) -> Awaitable[dict[str, DataSourceRegistration]]:
        """
        Gets the list of data source registrations.

        Args:
            user_id: The optional user identifier. If not specified, the current user will be used.
        """

        __url = "/api/v1/sources/registrations"

        __query_values: dict[str, str] = {}

        if user_id is not None:
            __query_values["userId"] = quote(_to_string(user_id), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___client._invoke(dict[str, DataSourceRegistration], "GET", __url, "application/json", None, None)

    def create_registration(self, registration: DataSourceRegistration, user_id: Optional[str] = None) -> Awaitable[UUID]:
        """
        Creates a data source registration.

        Args:
            user_id: The optional user identifier. If not specified, the current user will be used.
        """

        __url = "/api/v1/sources/registrations"

        __query_values: dict[str, str] = {}

        if user_id is not None:
            __query_values["userId"] = quote(_to_string(user_id), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___client._invoke(UUID, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(registration, _json_encoder_options)))

    def delete_registration(self, registration_id: UUID, user_id: Optional[str] = None) -> Awaitable[Response]:
        """
        Deletes a data source registration.

        Args:
            registration_id: The identifier of the registration.
            user_id: The optional user identifier. If not specified, the current user will be used.
        """

        __url = "/api/v1/sources/registrations/{registrationId}"
        __url = __url.replace("{registrationId}", quote(str(registration_id), safe=""))

        __query_values: dict[str, str] = {}

        if user_id is not None:
            __query_values["userId"] = quote(_to_string(user_id), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___client._invoke(Response, "DELETE", __url, "application/octet-stream", None, None)


class SystemAsyncClient:
    """Provides methods to interact with system."""

    ___client: NexusAsyncClient
    
    def __init__(self, client: NexusAsyncClient):
        self.___client = client

    def get_default_file_type(self) -> Awaitable[str]:
        """
        Gets the default file type.

        Args:
        """

        __url = "/api/v1/system/file-type"

        return self.___client._invoke(str, "GET", __url, "application/json", None, None)

    def get_help_link(self) -> Awaitable[str]:
        """
        Gets the configured help link.

        Args:
        """

        __url = "/api/v1/system/help-link"

        return self.___client._invoke(str, "GET", __url, "application/json", None, None)

    def get_configuration(self) -> Awaitable[Optional[dict[str, object]]]:
        """
        Gets the system configuration.

        Args:
        """

        __url = "/api/v1/system/configuration"

        return self.___client._invoke(dict[str, object], "GET", __url, "application/json", None, None)

    def set_configuration(self, configuration: Optional[dict[str, object]]) -> Awaitable[None]:
        """
        Sets the system configuration.

        Args:
        """

        __url = "/api/v1/system/configuration"

        return self.___client._invoke(type(None), "PUT", __url, None, "application/json", json.dumps(JsonEncoder.encode(configuration, _json_encoder_options)))


class UsersAsyncClient:
    """Provides methods to interact with users."""

    ___client: NexusAsyncClient
    
    def __init__(self, client: NexusAsyncClient):
        self.___client = client

    def authenticate(self, scheme: str, return_url: str) -> Awaitable[Response]:
        """
        Authenticates the user.

        Args:
            scheme: The authentication scheme to challenge.
            return_url: The URL to return after successful authentication.
        """

        __url = "/api/v1/users/authenticate"

        __query_values: dict[str, str] = {}

        __query_values["scheme"] = quote(_to_string(scheme), safe="")

        __query_values["returnUrl"] = quote(_to_string(return_url), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___client._invoke(Response, "POST", __url, "application/octet-stream", None, None)

    def sign_out(self, return_url: str) -> Awaitable[None]:
        """
        Logs out the user.

        Args:
            return_url: The URL to return after logout.
        """

        __url = "/api/v1/users/signout"

        __query_values: dict[str, str] = {}

        __query_values["returnUrl"] = quote(_to_string(return_url), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___client._invoke(type(None), "POST", __url, None, None, None)

    def delete_token_by_value(self, value: str) -> Awaitable[Response]:
        """
        Deletes a personal access token.

        Args:
            value: The personal access token to delete.
        """

        __url = "/api/v1/users/tokens/delete"

        __query_values: dict[str, str] = {}

        __query_values["value"] = quote(_to_string(value), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___client._invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def get_me(self) -> Awaitable[MeResponse]:
        """
        Gets the current user.

        Args:
        """

        __url = "/api/v1/users/me"

        return self.___client._invoke(MeResponse, "GET", __url, "application/json", None, None)

    def create_token(self, token: PersonalAccessToken, user_id: Optional[str] = None) -> Awaitable[str]:
        """
        Creates a personal access token.

        Args:
            user_id: The optional user identifier. If not specified, the current user will be used.
        """

        __url = "/api/v1/users/tokens/create"

        __query_values: dict[str, str] = {}

        if user_id is not None:
            __query_values["userId"] = quote(_to_string(user_id), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___client._invoke(str, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(token, _json_encoder_options)))

    def delete_token(self, token_id: UUID) -> Awaitable[Response]:
        """
        Deletes a personal access token.

        Args:
            token_id: The identifier of the personal access token.
        """

        __url = "/api/v1/users/tokens/{tokenId}"
        __url = __url.replace("{tokenId}", quote(str(token_id), safe=""))

        return self.___client._invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def accept_license(self, catalog_id: str) -> Awaitable[Response]:
        """
        Accepts the license of the specified catalog.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/users/accept-license"

        __query_values: dict[str, str] = {}

        __query_values["catalogId"] = quote(_to_string(catalog_id), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___client._invoke(Response, "GET", __url, "application/octet-stream", None, None)

    def get_users(self) -> Awaitable[dict[str, NexusUser]]:
        """
        Gets a list of users.

        Args:
        """

        __url = "/api/v1/users"

        return self.___client._invoke(dict[str, NexusUser], "GET", __url, "application/json", None, None)

    def create_user(self, user: NexusUser) -> Awaitable[str]:
        """
        Creates a user.

        Args:
        """

        __url = "/api/v1/users"

        return self.___client._invoke(str, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(user, _json_encoder_options)))

    def delete_user(self, user_id: str) -> Awaitable[Response]:
        """
        Deletes a user.

        Args:
            user_id: The identifier of the user.
        """

        __url = "/api/v1/users/{userId}"
        __url = __url.replace("{userId}", quote(str(user_id), safe=""))

        return self.___client._invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def get_claims(self, user_id: str) -> Awaitable[dict[str, NexusClaim]]:
        """
        Gets all claims.

        Args:
            user_id: The identifier of the user.
        """

        __url = "/api/v1/users/{userId}/claims"
        __url = __url.replace("{userId}", quote(str(user_id), safe=""))

        return self.___client._invoke(dict[str, NexusClaim], "GET", __url, "application/json", None, None)

    def create_claim(self, user_id: str, claim: NexusClaim) -> Awaitable[UUID]:
        """
        Creates a claim.

        Args:
            user_id: The identifier of the user.
        """

        __url = "/api/v1/users/{userId}/claims"
        __url = __url.replace("{userId}", quote(str(user_id), safe=""))

        return self.___client._invoke(UUID, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(claim, _json_encoder_options)))

    def delete_claim(self, claim_id: UUID) -> Awaitable[Response]:
        """
        Deletes a claim.

        Args:
            claim_id: The identifier of the claim.
        """

        __url = "/api/v1/users/claims/{claimId}"
        __url = __url.replace("{claimId}", quote(str(claim_id), safe=""))

        return self.___client._invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def get_tokens(self, user_id: str) -> Awaitable[dict[str, PersonalAccessToken]]:
        """
        Gets all personal access tokens.

        Args:
            user_id: The identifier of the user.
        """

        __url = "/api/v1/users/{userId}/tokens"
        __url = __url.replace("{userId}", quote(str(user_id), safe=""))

        return self.___client._invoke(dict[str, PersonalAccessToken], "GET", __url, "application/json", None, None)


class WritersAsyncClient:
    """Provides methods to interact with writers."""

    ___client: NexusAsyncClient
    
    def __init__(self, client: NexusAsyncClient):
        self.___client = client

    def get_descriptions(self) -> Awaitable[list[ExtensionDescription]]:
        """
        Gets the list of writer descriptions.

        Args:
        """

        __url = "/api/v1/writers/descriptions"

        return self.___client._invoke(list[ExtensionDescription], "GET", __url, "application/json", None, None)



class ArtifactsClient:
    """Provides methods to interact with artifacts."""

    ___client: NexusClient
    
    def __init__(self, client: NexusClient):
        self.___client = client

    def download(self, artifact_id: str) -> Response:
        """
        Gets the specified artifact.

        Args:
            artifact_id: The artifact identifier.
        """

        __url = "/api/v1/artifacts/{artifactId}"
        __url = __url.replace("{artifactId}", quote(str(artifact_id), safe=""))

        return self.___client._invoke(Response, "GET", __url, "application/octet-stream", None, None)


class CatalogsClient:
    """Provides methods to interact with catalogs."""

    ___client: NexusClient
    
    def __init__(self, client: NexusClient):
        self.___client = client

    def search_catalog_items(self, resource_paths: list[str]) -> dict[str, CatalogItem]:
        """
        Searches for the given resource paths and returns the corresponding catalog items.

        Args:
        """

        __url = "/api/v1/catalogs/search-items"

        return self.___client._invoke(dict[str, CatalogItem], "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(resource_paths, _json_encoder_options)))

    def get(self, catalog_id: str) -> ResourceCatalog:
        """
        Gets the specified catalog.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___client._invoke(ResourceCatalog, "GET", __url, "application/json", None, None)

    def get_child_catalog_infos(self, catalog_id: str) -> list[CatalogInfo]:
        """
        Gets a list of child catalog info for the provided parent catalog identifier.

        Args:
            catalog_id: The parent catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/child-catalog-infos"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___client._invoke(list[CatalogInfo], "GET", __url, "application/json", None, None)

    def get_time_range(self, catalog_id: str) -> CatalogTimeRange:
        """
        Gets the specified catalog's time range.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/timerange"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___client._invoke(CatalogTimeRange, "GET", __url, "application/json", None, None)

    def get_availability(self, catalog_id: str, begin: datetime, end: datetime, step: timedelta) -> CatalogAvailability:
        """
        Gets the specified catalog's availability.

        Args:
            catalog_id: The catalog identifier.
            begin: Start date/time.
            end: End date/time.
            step: Step period.
        """

        __url = "/api/v1/catalogs/{catalogId}/availability"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        __query_values: dict[str, str] = {}

        __query_values["begin"] = quote(_to_string(begin), safe="")

        __query_values["end"] = quote(_to_string(end), safe="")

        __query_values["step"] = quote(_to_string(step), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___client._invoke(CatalogAvailability, "GET", __url, "application/json", None, None)

    def get_license(self, catalog_id: str) -> Optional[str]:
        """
        Gets the license of the catalog if available.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/license"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___client._invoke(str, "GET", __url, "application/json", None, None)

    def get_attachments(self, catalog_id: str) -> list[str]:
        """
        Gets all attachments for the specified catalog.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/attachments"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___client._invoke(list[str], "GET", __url, "application/json", None, None)

    def upload_attachment(self, catalog_id: str, attachment_id: str, content: Union[bytes, Iterable[bytes], AsyncIterable[bytes]]) -> Response:
        """
        Uploads the specified attachment.

        Args:
            catalog_id: The catalog identifier.
            attachment_id: The attachment identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/attachments/{attachmentId}"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))
        __url = __url.replace("{attachmentId}", quote(str(attachment_id), safe=""))

        return self.___client._invoke(Response, "PUT", __url, "application/octet-stream", "application/octet-stream", content)

    def delete_attachment(self, catalog_id: str, attachment_id: str) -> Response:
        """
        Deletes the specified attachment.

        Args:
            catalog_id: The catalog identifier.
            attachment_id: The attachment identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/attachments/{attachmentId}"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))
        __url = __url.replace("{attachmentId}", quote(str(attachment_id), safe=""))

        return self.___client._invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def get_attachment_stream(self, catalog_id: str, attachment_id: str) -> Response:
        """
        Gets the specified attachment.

        Args:
            catalog_id: The catalog identifier.
            attachment_id: The attachment identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/attachments/{attachmentId}/content"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))
        __url = __url.replace("{attachmentId}", quote(str(attachment_id), safe=""))

        return self.___client._invoke(Response, "GET", __url, "application/octet-stream", None, None)

    def get_metadata(self, catalog_id: str) -> CatalogMetadata:
        """
        Gets the catalog metadata.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/metadata"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___client._invoke(CatalogMetadata, "GET", __url, "application/json", None, None)

    def set_metadata(self, catalog_id: str, metadata: CatalogMetadata) -> None:
        """
        Puts the catalog metadata.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/catalogs/{catalogId}/metadata"
        __url = __url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self.___client._invoke(type(None), "PUT", __url, None, "application/json", json.dumps(JsonEncoder.encode(metadata, _json_encoder_options)))


class DataClient:
    """Provides methods to interact with data."""

    ___client: NexusClient
    
    def __init__(self, client: NexusClient):
        self.___client = client

    def get_stream(self, resource_path: str, begin: datetime, end: datetime) -> Response:
        """
        Gets the requested data.

        Args:
            resource_path: The path to the resource data to stream.
            begin: Start date/time.
            end: End date/time.
        """

        __url = "/api/v1/data"

        __query_values: dict[str, str] = {}

        __query_values["resourcePath"] = quote(_to_string(resource_path), safe="")

        __query_values["begin"] = quote(_to_string(begin), safe="")

        __query_values["end"] = quote(_to_string(end), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___client._invoke(Response, "GET", __url, "application/octet-stream", None, None)


class JobsClient:
    """Provides methods to interact with jobs."""

    ___client: NexusClient
    
    def __init__(self, client: NexusClient):
        self.___client = client

    def get_jobs(self) -> list[Job]:
        """
        Gets a list of jobs.

        Args:
        """

        __url = "/api/v1/jobs"

        return self.___client._invoke(list[Job], "GET", __url, "application/json", None, None)

    def cancel_job(self, job_id: UUID) -> Response:
        """
        Cancels the specified job.

        Args:
            job_id: 
        """

        __url = "/api/v1/jobs/{jobId}"
        __url = __url.replace("{jobId}", quote(str(job_id), safe=""))

        return self.___client._invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def get_job_status(self, job_id: UUID) -> JobStatus:
        """
        Gets the status of the specified job.

        Args:
            job_id: 
        """

        __url = "/api/v1/jobs/{jobId}/status"
        __url = __url.replace("{jobId}", quote(str(job_id), safe=""))

        return self.___client._invoke(JobStatus, "GET", __url, "application/json", None, None)

    def export(self, parameters: ExportParameters) -> Job:
        """
        Creates a new export job.

        Args:
        """

        __url = "/api/v1/jobs/export"

        return self.___client._invoke(Job, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(parameters, _json_encoder_options)))

    def refresh_database(self) -> Job:
        """
        Creates a new job which reloads all extensions and resets the resource catalog.

        Args:
        """

        __url = "/api/v1/jobs/refresh-database"

        return self.___client._invoke(Job, "POST", __url, "application/json", None, None)

    def clear_cache(self, catalog_id: str, begin: datetime, end: datetime) -> Job:
        """
        Clears the aggregation data cache for the specified period of time.

        Args:
            catalog_id: The catalog identifier.
            begin: Start date/time.
            end: End date/time.
        """

        __url = "/api/v1/jobs/clear-cache"

        __query_values: dict[str, str] = {}

        __query_values["catalogId"] = quote(_to_string(catalog_id), safe="")

        __query_values["begin"] = quote(_to_string(begin), safe="")

        __query_values["end"] = quote(_to_string(end), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___client._invoke(Job, "POST", __url, "application/json", None, None)


class PackageReferencesClient:
    """Provides methods to interact with package references."""

    ___client: NexusClient
    
    def __init__(self, client: NexusClient):
        self.___client = client

    def get(self) -> dict[str, PackageReference]:
        """
        Gets the list of package references.

        Args:
        """

        __url = "/api/v1/packagereferences"

        return self.___client._invoke(dict[str, PackageReference], "GET", __url, "application/json", None, None)

    def create(self, package_reference: PackageReference) -> UUID:
        """
        Creates a package reference.

        Args:
        """

        __url = "/api/v1/packagereferences"

        return self.___client._invoke(UUID, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(package_reference, _json_encoder_options)))

    def delete(self, package_reference_id: UUID) -> None:
        """
        Deletes a package reference.

        Args:
            package_reference_id: The ID of the package reference.
        """

        __url = "/api/v1/packagereferences/{packageReferenceId}"
        __url = __url.replace("{packageReferenceId}", quote(str(package_reference_id), safe=""))

        return self.___client._invoke(type(None), "DELETE", __url, None, None, None)

    def get_versions(self, package_reference_id: UUID) -> list[str]:
        """
        Gets package versions.

        Args:
            package_reference_id: The ID of the package reference.
        """

        __url = "/api/v1/packagereferences/{packageReferenceId}/versions"
        __url = __url.replace("{packageReferenceId}", quote(str(package_reference_id), safe=""))

        return self.___client._invoke(list[str], "GET", __url, "application/json", None, None)


class SourcesClient:
    """Provides methods to interact with sources."""

    ___client: NexusClient
    
    def __init__(self, client: NexusClient):
        self.___client = client

    def get_descriptions(self) -> list[ExtensionDescription]:
        """
        Gets the list of source descriptions.

        Args:
        """

        __url = "/api/v1/sources/descriptions"

        return self.___client._invoke(list[ExtensionDescription], "GET", __url, "application/json", None, None)

    def get_registrations(self, user_id: Optional[str] = None) -> dict[str, DataSourceRegistration]:
        """
        Gets the list of data source registrations.

        Args:
            user_id: The optional user identifier. If not specified, the current user will be used.
        """

        __url = "/api/v1/sources/registrations"

        __query_values: dict[str, str] = {}

        if user_id is not None:
            __query_values["userId"] = quote(_to_string(user_id), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___client._invoke(dict[str, DataSourceRegistration], "GET", __url, "application/json", None, None)

    def create_registration(self, registration: DataSourceRegistration, user_id: Optional[str] = None) -> UUID:
        """
        Creates a data source registration.

        Args:
            user_id: The optional user identifier. If not specified, the current user will be used.
        """

        __url = "/api/v1/sources/registrations"

        __query_values: dict[str, str] = {}

        if user_id is not None:
            __query_values["userId"] = quote(_to_string(user_id), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___client._invoke(UUID, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(registration, _json_encoder_options)))

    def delete_registration(self, registration_id: UUID, user_id: Optional[str] = None) -> Response:
        """
        Deletes a data source registration.

        Args:
            registration_id: The identifier of the registration.
            user_id: The optional user identifier. If not specified, the current user will be used.
        """

        __url = "/api/v1/sources/registrations/{registrationId}"
        __url = __url.replace("{registrationId}", quote(str(registration_id), safe=""))

        __query_values: dict[str, str] = {}

        if user_id is not None:
            __query_values["userId"] = quote(_to_string(user_id), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___client._invoke(Response, "DELETE", __url, "application/octet-stream", None, None)


class SystemClient:
    """Provides methods to interact with system."""

    ___client: NexusClient
    
    def __init__(self, client: NexusClient):
        self.___client = client

    def get_default_file_type(self) -> str:
        """
        Gets the default file type.

        Args:
        """

        __url = "/api/v1/system/file-type"

        return self.___client._invoke(str, "GET", __url, "application/json", None, None)

    def get_help_link(self) -> str:
        """
        Gets the configured help link.

        Args:
        """

        __url = "/api/v1/system/help-link"

        return self.___client._invoke(str, "GET", __url, "application/json", None, None)

    def get_configuration(self) -> Optional[dict[str, object]]:
        """
        Gets the system configuration.

        Args:
        """

        __url = "/api/v1/system/configuration"

        return self.___client._invoke(dict[str, object], "GET", __url, "application/json", None, None)

    def set_configuration(self, configuration: Optional[dict[str, object]]) -> None:
        """
        Sets the system configuration.

        Args:
        """

        __url = "/api/v1/system/configuration"

        return self.___client._invoke(type(None), "PUT", __url, None, "application/json", json.dumps(JsonEncoder.encode(configuration, _json_encoder_options)))


class UsersClient:
    """Provides methods to interact with users."""

    ___client: NexusClient
    
    def __init__(self, client: NexusClient):
        self.___client = client

    def authenticate(self, scheme: str, return_url: str) -> Response:
        """
        Authenticates the user.

        Args:
            scheme: The authentication scheme to challenge.
            return_url: The URL to return after successful authentication.
        """

        __url = "/api/v1/users/authenticate"

        __query_values: dict[str, str] = {}

        __query_values["scheme"] = quote(_to_string(scheme), safe="")

        __query_values["returnUrl"] = quote(_to_string(return_url), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___client._invoke(Response, "POST", __url, "application/octet-stream", None, None)

    def sign_out(self, return_url: str) -> None:
        """
        Logs out the user.

        Args:
            return_url: The URL to return after logout.
        """

        __url = "/api/v1/users/signout"

        __query_values: dict[str, str] = {}

        __query_values["returnUrl"] = quote(_to_string(return_url), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___client._invoke(type(None), "POST", __url, None, None, None)

    def delete_token_by_value(self, value: str) -> Response:
        """
        Deletes a personal access token.

        Args:
            value: The personal access token to delete.
        """

        __url = "/api/v1/users/tokens/delete"

        __query_values: dict[str, str] = {}

        __query_values["value"] = quote(_to_string(value), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___client._invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def get_me(self) -> MeResponse:
        """
        Gets the current user.

        Args:
        """

        __url = "/api/v1/users/me"

        return self.___client._invoke(MeResponse, "GET", __url, "application/json", None, None)

    def create_token(self, token: PersonalAccessToken, user_id: Optional[str] = None) -> str:
        """
        Creates a personal access token.

        Args:
            user_id: The optional user identifier. If not specified, the current user will be used.
        """

        __url = "/api/v1/users/tokens/create"

        __query_values: dict[str, str] = {}

        if user_id is not None:
            __query_values["userId"] = quote(_to_string(user_id), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___client._invoke(str, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(token, _json_encoder_options)))

    def delete_token(self, token_id: UUID) -> Response:
        """
        Deletes a personal access token.

        Args:
            token_id: The identifier of the personal access token.
        """

        __url = "/api/v1/users/tokens/{tokenId}"
        __url = __url.replace("{tokenId}", quote(str(token_id), safe=""))

        return self.___client._invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def accept_license(self, catalog_id: str) -> Response:
        """
        Accepts the license of the specified catalog.

        Args:
            catalog_id: The catalog identifier.
        """

        __url = "/api/v1/users/accept-license"

        __query_values: dict[str, str] = {}

        __query_values["catalogId"] = quote(_to_string(catalog_id), safe="")

        __query: str = "?" + "&".join(f"{key}={value}" for (key, value) in __query_values.items())
        __url += __query

        return self.___client._invoke(Response, "GET", __url, "application/octet-stream", None, None)

    def get_users(self) -> dict[str, NexusUser]:
        """
        Gets a list of users.

        Args:
        """

        __url = "/api/v1/users"

        return self.___client._invoke(dict[str, NexusUser], "GET", __url, "application/json", None, None)

    def create_user(self, user: NexusUser) -> str:
        """
        Creates a user.

        Args:
        """

        __url = "/api/v1/users"

        return self.___client._invoke(str, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(user, _json_encoder_options)))

    def delete_user(self, user_id: str) -> Response:
        """
        Deletes a user.

        Args:
            user_id: The identifier of the user.
        """

        __url = "/api/v1/users/{userId}"
        __url = __url.replace("{userId}", quote(str(user_id), safe=""))

        return self.___client._invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def get_claims(self, user_id: str) -> dict[str, NexusClaim]:
        """
        Gets all claims.

        Args:
            user_id: The identifier of the user.
        """

        __url = "/api/v1/users/{userId}/claims"
        __url = __url.replace("{userId}", quote(str(user_id), safe=""))

        return self.___client._invoke(dict[str, NexusClaim], "GET", __url, "application/json", None, None)

    def create_claim(self, user_id: str, claim: NexusClaim) -> UUID:
        """
        Creates a claim.

        Args:
            user_id: The identifier of the user.
        """

        __url = "/api/v1/users/{userId}/claims"
        __url = __url.replace("{userId}", quote(str(user_id), safe=""))

        return self.___client._invoke(UUID, "POST", __url, "application/json", "application/json", json.dumps(JsonEncoder.encode(claim, _json_encoder_options)))

    def delete_claim(self, claim_id: UUID) -> Response:
        """
        Deletes a claim.

        Args:
            claim_id: The identifier of the claim.
        """

        __url = "/api/v1/users/claims/{claimId}"
        __url = __url.replace("{claimId}", quote(str(claim_id), safe=""))

        return self.___client._invoke(Response, "DELETE", __url, "application/octet-stream", None, None)

    def get_tokens(self, user_id: str) -> dict[str, PersonalAccessToken]:
        """
        Gets all personal access tokens.

        Args:
            user_id: The identifier of the user.
        """

        __url = "/api/v1/users/{userId}/tokens"
        __url = __url.replace("{userId}", quote(str(user_id), safe=""))

        return self.___client._invoke(dict[str, PersonalAccessToken], "GET", __url, "application/json", None, None)


class WritersClient:
    """Provides methods to interact with writers."""

    ___client: NexusClient
    
    def __init__(self, client: NexusClient):
        self.___client = client

    def get_descriptions(self) -> list[ExtensionDescription]:
        """
        Gets the list of writer descriptions.

        Args:
        """

        __url = "/api/v1/writers/descriptions"

        return self.___client._invoke(list[ExtensionDescription], "GET", __url, "application/json", None, None)




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
    
    _configuration_header_key: str = "Nexus-Configuration"
    _authorization_header_key: str = "Authorization"

    _token: Optional[str]
    _http_client: AsyncClient

    _artifacts: ArtifactsAsyncClient
    _catalogs: CatalogsAsyncClient
    _data: DataAsyncClient
    _jobs: JobsAsyncClient
    _packageReferences: PackageReferencesAsyncClient
    _sources: SourcesAsyncClient
    _system: SystemAsyncClient
    _users: UsersAsyncClient
    _writers: WritersAsyncClient


    @classmethod
    def create(cls, base_url: str) -> NexusAsyncClient:
        """
        Initializes a new instance of the NexusAsyncClient
        
            Args:
                base_url: The base URL to use.
        """
        return NexusAsyncClient(AsyncClient(base_url=base_url, timeout=60.0))

    def __init__(self, http_client: AsyncClient):
        """
        Initializes a new instance of the NexusAsyncClient
        
            Args:
                http_client: The HTTP client to use.
        """

        if http_client.base_url is None:
            raise Exception("The base url of the HTTP client must be set.")

        self._http_client = http_client
        self._token = None

        self._artifacts = ArtifactsAsyncClient(self)
        self._catalogs = CatalogsAsyncClient(self)
        self._data = DataAsyncClient(self)
        self._jobs = JobsAsyncClient(self)
        self._packageReferences = PackageReferencesAsyncClient(self)
        self._sources = SourcesAsyncClient(self)
        self._system = SystemAsyncClient(self)
        self._users = UsersAsyncClient(self)
        self._writers = WritersAsyncClient(self)


    @property
    def is_authenticated(self) -> bool:
        """Gets a value which indicates if the user is authenticated."""
        return self._token is not None

    @property
    def artifacts(self) -> ArtifactsAsyncClient:
        """Gets the ArtifactsAsyncClient."""
        return self._artifacts

    @property
    def catalogs(self) -> CatalogsAsyncClient:
        """Gets the CatalogsAsyncClient."""
        return self._catalogs

    @property
    def data(self) -> DataAsyncClient:
        """Gets the DataAsyncClient."""
        return self._data

    @property
    def jobs(self) -> JobsAsyncClient:
        """Gets the JobsAsyncClient."""
        return self._jobs

    @property
    def package_references(self) -> PackageReferencesAsyncClient:
        """Gets the PackageReferencesAsyncClient."""
        return self._packageReferences

    @property
    def sources(self) -> SourcesAsyncClient:
        """Gets the SourcesAsyncClient."""
        return self._sources

    @property
    def system(self) -> SystemAsyncClient:
        """Gets the SystemAsyncClient."""
        return self._system

    @property
    def users(self) -> UsersAsyncClient:
        """Gets the UsersAsyncClient."""
        return self._users

    @property
    def writers(self) -> WritersAsyncClient:
        """Gets the WritersAsyncClient."""
        return self._writers



    def sign_in(self, access_token: str):
        """Signs in the user.

        Args:
            access_token: The access token.
        """

        authorization_header_value = f"Bearer {access_token}"

        if self._authorization_header_key in self._http_client.headers:
            del self._http_client.headers[self._authorization_header_key]

        self._http_client.headers[self._authorization_header_key] = authorization_header_value
        self._token = access_token

    def attach_configuration(self, configuration: Any) -> Any:
        """Attaches configuration data to subsequent API requests.
        
        Args:
            configuration: The configuration data.
        """

        encoded_json = base64.b64encode(json.dumps(configuration).encode("utf-8")).decode("utf-8")

        if self._configuration_header_key in self._http_client.headers:
            del self._http_client.headers[self._configuration_header_key]

        self._http_client.headers[self._configuration_header_key] = encoded_json

        return _DisposableAsyncConfiguration(self)

    def clear_configuration(self) -> None:
        """Clears configuration data for all subsequent API requests."""

        if self._configuration_header_key in self._http_client.headers:
            del self._http_client.headers[self._configuration_header_key]

    async def _invoke(self, typeOfT: Optional[Type[T]], method: str, relative_url: str, accept_header_value: Optional[str], content_type_value: Optional[str], content: Union[None, str, bytes, Iterable[bytes], AsyncIterable[bytes]]) -> T:

        # prepare request
        request = self._build_request_message(method, relative_url, content, content_type_value, accept_header_value)

        # send request
        response = await self._http_client.send(request)

        # process response
        if not response.is_success:
            
            message = response.text
            status_code = f"N00.{response.status_code}"

            if not message:
                raise NexusException(status_code, f"The HTTP request failed with status code {response.status_code}.")

            else:
                raise NexusException(status_code, f"The HTTP request failed with status code {response.status_code}. The response message is: {message}")

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
       
        request_message = self._http_client.build_request(method, relative_url, content = content)

        if content_type_value is not None:
            request_message.headers["Content-Type"] = content_type_value

        if accept_header_value is not None:
            request_message.headers["Accept"] = accept_header_value

        return request_message

    # "disposable" methods
    async def __aenter__(self) -> NexusAsyncClient:
        return self

    async def __aexit__(self, exc_type, exc_value, exc_traceback):
        if (self._http_client is not None):
            await self._http_client.aclose()

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

        catalog_item_map = await self.catalogs.search_catalog_items(list(resource_paths))
        result: dict[str, DataResponse] = {}
        progress: float = 0

        for (resource_path, catalog_item) in catalog_item_map.items():

            response = await self.data.get_stream(resource_path, begin, end)

            try:
                double_data = await self._read_as_double(response)

            finally:
                await response.aclose()

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
                values=double_data
            )

            progress = progress + 1.0 / len(catalog_item_map)

            if on_progress is not None:
                on_progress(progress)
                
        return result

    async def _read_as_double(self, response: Response):
        
        byteBuffer = await response.aread()

        if len(byteBuffer) % 8 != 0:
            raise Exception("The data length is invalid.")

        doubleBuffer = array("d", byteBuffer)

        return doubleBuffer 

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
        job = await self.jobs.export(export_parameters)

        # Wait for job to finish
        artifact_id: Optional[str] = None

        while True:
            await asyncio.sleep(1)
            
            job_status = await self.jobs.get_job_status(job.id)

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

            response = await self.artifacts.download(artifact_id)
            
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
    
    _configuration_header_key: str = "Nexus-Configuration"
    _authorization_header_key: str = "Authorization"

    _token: Optional[str]
    _http_client: Client

    _artifacts: ArtifactsClient
    _catalogs: CatalogsClient
    _data: DataClient
    _jobs: JobsClient
    _packageReferences: PackageReferencesClient
    _sources: SourcesClient
    _system: SystemClient
    _users: UsersClient
    _writers: WritersClient


    @classmethod
    def create(cls, base_url: str) -> NexusClient:
        """
        Initializes a new instance of the NexusClient
        
            Args:
                base_url: The base URL to use.
        """
        return NexusClient(Client(base_url=base_url, timeout=60.0))

    def __init__(self, http_client: Client):
        """
        Initializes a new instance of the NexusClient
        
            Args:
                http_client: The HTTP client to use.
        """

        if http_client.base_url is None:
            raise Exception("The base url of the HTTP client must be set.")

        self._http_client = http_client
        self._token = None

        self._artifacts = ArtifactsClient(self)
        self._catalogs = CatalogsClient(self)
        self._data = DataClient(self)
        self._jobs = JobsClient(self)
        self._packageReferences = PackageReferencesClient(self)
        self._sources = SourcesClient(self)
        self._system = SystemClient(self)
        self._users = UsersClient(self)
        self._writers = WritersClient(self)


    @property
    def is_authenticated(self) -> bool:
        """Gets a value which indicates if the user is authenticated."""
        return self._token is not None

    @property
    def artifacts(self) -> ArtifactsClient:
        """Gets the ArtifactsClient."""
        return self._artifacts

    @property
    def catalogs(self) -> CatalogsClient:
        """Gets the CatalogsClient."""
        return self._catalogs

    @property
    def data(self) -> DataClient:
        """Gets the DataClient."""
        return self._data

    @property
    def jobs(self) -> JobsClient:
        """Gets the JobsClient."""
        return self._jobs

    @property
    def package_references(self) -> PackageReferencesClient:
        """Gets the PackageReferencesClient."""
        return self._packageReferences

    @property
    def sources(self) -> SourcesClient:
        """Gets the SourcesClient."""
        return self._sources

    @property
    def system(self) -> SystemClient:
        """Gets the SystemClient."""
        return self._system

    @property
    def users(self) -> UsersClient:
        """Gets the UsersClient."""
        return self._users

    @property
    def writers(self) -> WritersClient:
        """Gets the WritersClient."""
        return self._writers



    def sign_in(self, access_token: str):
        """Signs in the user.

        Args:
            access_token: The access token.
        """

        authorization_header_value = f"Bearer {access_token}"

        if self._authorization_header_key in self._http_client.headers:
            del self._http_client.headers[self._authorization_header_key]

        self._http_client.headers[self._authorization_header_key] = authorization_header_value
        self._token = access_token

    def attach_configuration(self, configuration: Any) -> Any:
        """Attaches configuration data to subsequent API requests.
        
        Args:
            configuration: The configuration data.
        """

        encoded_json = base64.b64encode(json.dumps(configuration).encode("utf-8")).decode("utf-8")

        if self._configuration_header_key in self._http_client.headers:
            del self._http_client.headers[self._configuration_header_key]

        self._http_client.headers[self._configuration_header_key] = encoded_json

        return _DisposableConfiguration(self)

    def clear_configuration(self) -> None:
        """Clears configuration data for all subsequent API requests."""

        if self._configuration_header_key in self._http_client.headers:
            del self._http_client.headers[self._configuration_header_key]

    def _invoke(self, typeOfT: Optional[Type[T]], method: str, relative_url: str, accept_header_value: Optional[str], content_type_value: Optional[str], content: Union[None, str, bytes, Iterable[bytes], AsyncIterable[bytes]]) -> T:

        # prepare request
        request = self._build_request_message(method, relative_url, content, content_type_value, accept_header_value)

        # send request
        response = self._http_client.send(request)

        # process response
        if not response.is_success:
            
            message = response.text
            status_code = f"N00.{response.status_code}"

            if not message:
                raise NexusException(status_code, f"The HTTP request failed with status code {response.status_code}.")

            else:
                raise NexusException(status_code, f"The HTTP request failed with status code {response.status_code}. The response message is: {message}")

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
       
        request_message = self._http_client.build_request(method, relative_url, content = content)

        if content_type_value is not None:
            request_message.headers["Content-Type"] = content_type_value

        if accept_header_value is not None:
            request_message.headers["Accept"] = accept_header_value

        return request_message

    # "disposable" methods
    def __enter__(self) -> NexusClient:
        return self

    def __exit__(self, exc_type, exc_value, exc_traceback):
        if (self._http_client is not None):
            self._http_client.close()

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

        catalog_item_map = self.catalogs.search_catalog_items(list(resource_paths))
        result: dict[str, DataResponse] = {}
        progress: float = 0

        for (resource_path, catalog_item) in catalog_item_map.items():

            response = self.data.get_stream(resource_path, begin, end)

            try:
                double_data = self._read_as_double(response)

            finally:
                response.close()

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
                values=double_data
            )

            progress = progress + 1.0 / len(catalog_item_map)

            if on_progress is not None:
                on_progress(progress)
                
        return result

    def _read_as_double(self, response: Response):
        
        byteBuffer = response.read()

        if len(byteBuffer) % 8 != 0:
            raise Exception("The data length is invalid.")

        doubleBuffer = array("d", byteBuffer)

        return doubleBuffer 

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
        job = self.jobs.export(export_parameters)

        # Wait for job to finish
        artifact_id: Optional[str] = None

        while True:
            time.sleep(1)
            
            job_status = self.jobs.get_job_status(job.id)

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

            response = self.artifacts.download(artifact_id)
            
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
