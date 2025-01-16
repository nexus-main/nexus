import dataclasses
import re
import typing
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from enum import Enum
from typing import (Any, Callable, ClassVar, Optional, Type, TypeVar, Union,
                    cast)
from uuid import UUID

T = TypeVar("T")

@dataclass(frozen=True)
class JsonEncoderOptions:

    property_name_encoder: Callable[[str], str] = lambda value: value
    property_name_decoder: Callable[[str], str] = lambda value: value

    encoders: dict[Type, Callable[[Any], Any]] = field(default_factory=lambda: {
        datetime:   lambda value: value.isoformat().replace("+00:00", "Z"),
        timedelta:  lambda value: _encode_timedelta(value),
        Enum:       lambda value: value.name
    })

    decoders: dict[Type, Callable[[Type, Any], Any]] = field(default_factory=lambda: {
        datetime:   lambda       _, value: datetime.fromisoformat((value[0:26] + value[26 + 1:]).replace("Z", "+00:00")),
        timedelta:  lambda       _, value: _decode_timedelta(value),
        Enum:       lambda typeCls, value: cast(Type[Enum], typeCls)[value]
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
        elif isinstance(value, UUID):
            value = str(value)

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

        if typeCls == UUID:
            return cast(T, UUID(data))

        origin = typing.get_origin(typeCls)
        args = typing.get_args(typeCls)

        if origin is not None:

            # Optional
            if origin is Union and type(None) in args:

                baseType = args[0]
                instance3 = JsonEncoder._decode(baseType, data, options)

                return cast(T, instance3)

            # list
            elif issubclass(cast(type, origin), list):

                listType = args[0]
                instance1: list = list()

                for value in data:
                    instance1.append(JsonEncoder._decode(listType, value, options))

                return cast(T, instance1)
            
            # dict
            elif issubclass(cast(type, origin), dict):

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
