from datetime import datetime
from enum import Enum
from typing import (Any, AsyncIterable, Awaitable, Iterable, Optional,
                    Protocol, Type, TypeVar, Union, cast)

from ._encoder import JsonEncoderOptions, to_camel_case, to_snake_case


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

T = TypeVar("T")

class HttpRequestHandler(Protocol):
    """
    A handler to execute HTTP requests.
    """

    def __call__(self, typeOfT: Optional[Type[T]], method: str, relative_url: str, accept_header_value: Optional[str], content_type_value: Optional[str], content: Union[None, str, bytes, Iterable[bytes], AsyncIterable[bytes]]) -> T:
        """
        Execute the HTTP request.

        Args:
            typeOfT: The return type.
            method: The HTTP method.
            relative_url: The relative URL.
            accept_header_value: The value of the accept header.
            content_type_value: The value of the content type.
            content: The content.
        """
        ...

class HttpRequestHandlerAsync(Protocol):
    """
    A handler to execute HTTP requests.
    """

    def __call__(self, typeOfT: Optional[Type[T]], method: str, relative_url: str, accept_header_value: Optional[str], content_type_value: Optional[str], content: Union[None, str, bytes, Iterable[bytes], AsyncIterable[bytes]]) -> Awaitable[T]:
        """
        Execute the HTTP request.

        Args:
            typeOfT: The return type.
            method: The HTTP method.
            relative_url: The relative URL.
            accept_header_value: The value of the accept header.
            content_type_value: The value of the content type.
            content: The content.
        """
        ...

class NexusException(Exception):
    """A NexusException."""

    def __init__(self, status_code: str, message: str):
        self.status_code = status_code
        self.message = message

    status_code: str
    """The exception status code."""

    message: str
    """The exception message."""
