# https://stackoverflow.com/questions/33533148/how-do-i-type-hint-a-method-with-the-type-of-the-enclosing-class
from __future__ import annotations

import enum
import re
from dataclasses import dataclass
from datetime import timedelta
from typing import Any, ClassVar, Optional, Pattern

from ._data_model_extensions import to_unit_string
from ._data_model_utilities import _get_representation_parameter_string

_README = "readme"
_LICENSE = "license"
_DESCRIPTION = "description"
_WARNING = "warning"
_UNIT = "unit"
_GROUPS = "groups"

# TODO: Make object and list readonly, e.g. by using tuple instead of list 
# or adapt this solution: https://stackoverflow.com/questions/19022868/how-to-make-dictionary-read-only-in-python

################# DATA MODEL TYPES ###############

class NexusDataType(enum.IntEnum):
    """Specifies the Nexus data type."""

    UINT8 = 0x108
    """Unsigned 8-bit integer."""

    INT8 = 0x208
    """Signed 8-bit integer."""

    UINT16 = 0x110
    """Unsigned 16-bit integer."""

    INT16 = 0x210
    """Signed 16-bit integer."""

    UINT32 = 0x120
    """Unsigned 32-bit integer."""

    INT32 = 0x220
    """Signed 32-bit integer."""

    UINT64 = 0x140
    """Unsigned 64-bit integer."""

    INT64 = 0x240
    """Signed 64-bit integer."""

    FLOAT32 = 0x320
    """32-bit floating-point number."""

    FLOAT64 = 0x340
    """64-bit floating-point number."""

@dataclass(frozen=True)
class CatalogItem:
    """
    A catalog item consists of a catalog, a resource and a representation.
    """

    catalog: ResourceCatalog
    """The catalog."""

    resource: Resource
    """The resource."""

    representation: Representation
    """The representation."""

    parameters: Optional[dict[str, str]]
    """The optional dictionary of representation parameters and its arguments."""

    def to_path(self) -> str:
        """
        Construct a fully qualified path.
        """
        parameter_string = _get_representation_parameter_string(self.parameters)
        return f"{self.catalog.id}/{self.resource.id}/{self.representation.id}{parameter_string}"

@dataclass(frozen=True)
class CatalogRegistration:
    """
    A catalog registration.
    """

    def __post_init__(self):
        # path
        current_path = self.path

        if not current_path.startswith("/"):
            current_path = "/" + current_path

        if self.path != "/":
            if not ResourceCatalog.valid_id_expression.match(current_path):
                raise Exception(f"The catalog path {self.path} is not valid.")

    path: str
    """The absolute or relative path of the catalog."""

    title: Optional[str]
    """A nullable title."""

    is_transient: bool = False
    """A boolean which indicates if the catalog and its children should be reloaded on each request."""

################# DATA MODEL ###############

_nexus_data_type_values: set[int] = set(item.value for item in NexusDataType) 

@dataclass(frozen=True)
class Representation:
    """
    A representation is part of a resource.
    """

    def __post_init__(self):
        # data type
        if not self.data_type in _nexus_data_type_values:
            raise Exception(f"The data type {self.data_type} is not valid.")

        # sample period
        if self.sample_period <= timedelta(0):
            raise Exception(f"The sample period {self.sample_period} is not valid.")

        # parameters
        if self.parameters is not None:
            self._validate_parameters(self.parameters)

    data_type: NexusDataType
    """The data type."""

    sample_period: timedelta
    """The sample period."""

    parameters: Optional[dict[str, Any]] = None
    """The optional list of parameters."""

    @property
    def id(self) -> str:
        """The identifer of the representation. It is constructed using the sample period."""
        return to_unit_string(self.sample_period)

    @property
    def element_size(self) -> int:
        """The number of bits per element."""
        return (int(self.data_type) & 0xFF) >> 3

    def _validate_parameters(self, parameters: dict[str, Any]):

        for key in parameters.keys():

            # resources and parameter have the same requirements regarding their IDs
            if not Resource.valid_id_expression.match(key):
                raise Exception("The representation parameter identifier is not valid.")

@dataclass(frozen=True)
class Resource:
    """
    A resource is part of a resource catalog and holds a list of representations.
    """

    valid_id_expression : ClassVar[Pattern[str]] = re.compile(r"^[a-zA-Z_][a-zA-Z_0-9]*$")
    """Gets a regular expression to validate a resource identifier."""

    invalid_id_chars_expression : ClassVar[Pattern[str]] = re.compile(r"[^a-zA-Z_0-9]")
    """Gets a regular expression to find invalid characters in a resource identifier."""

    invalid_id_start_chars_expression: ClassVar[Pattern[str]] = re.compile(r"^[^a-zA-Z_]+")
    """Gets a regular expression to find invalid start characters in a resource identifier."""

    def __post_init__(self):
        if not Resource.valid_id_expression.match(self.id):
            raise Exception(f"The resource catalog identifier {self.id} is not valid.")

        if self.representations is not None:
            self._validate_representations(self.representations)

    id: str
    """Gets the identifier."""

    properties: Optional[dict[str, object]] = None
    """Gets the properties."""

    representations: Optional[list[Representation]] = None
    """Gets list of representations."""

    def _validate_representations(self, representations: list[Representation]):
        unique_ids = set([representation.id for representation in representations])

        if len(unique_ids) != len(representations):
            raise Exception("There are multiple representations with the same identifier.")

@dataclass(frozen=True)
class ResourceCatalog:
    """
    A catalog is a top level element and holds a list of resources.
    """

    valid_id_expression : ClassVar[Pattern[str]] = re.compile(r"(?:\/[a-zA-Z_][a-zA-Z_0-9]*)+$")
    """Gets a regular expression to validate a resource catalog identifier."""

    def __post_init__(self):
        if not ResourceCatalog.valid_id_expression.match(self.id):
            raise Exception(f"The resource catalog identifier {self.id} is not valid.")

        if self.resources is not None:
            self._validate_resources(self.resources)

    id: str
    """Gets the identifier."""

    properties: Optional[dict[str, object]] = None
    """Gets the properties."""

    resources: Optional[list[Resource]]  = None
    """Gets the list of resources."""

    def _validate_resources(self, resources: list[Resource]):
        unique_ids = set([resource.id for resource in resources])

        if len(unique_ids) != len(resources):
            raise Exception("There are multiple resources with the same identifier.")

class ResourceCatalogBuilder:
    """
    A resource catalog builder simplifies building a resource catalog.
    """

    def __init__(self, id: str):
        """
        Initializes a new instance of the ResourceCatalogBuilder
        
            Args:
                id: The identifier of the resource catalog to be built.
        """
        self._id: str = id
        self._properties: Optional[dict[str, object]] = None
        self._resources: Optional[list[Resource]] = None

    def with_property(self, key: str, value: Any) -> ResourceCatalogBuilder:
        """
        Adds a property.
        
            Args:
                key: The key of the property.
                value: The value of the property.
        """

        if self._properties is None:
            self._properties = {}

        self._properties[key] = value

        return self

    def with_readme(self, readme: str) -> ResourceCatalogBuilder:
        """
        Adds a readme.
        
            Args:
                description: The markdown readme to add.
        """
        return self.with_property(_README, readme)

    def with_license(self, license: str) -> ResourceCatalogBuilder:
        """
        Adds a license.
        
            Args:
                license: The markdown license to add.
        """
        return self.with_property(_LICENSE, license)

    def add_resource(self, resource: Resource) -> ResourceCatalogBuilder:
        """
        Adds a resource.
        
            Args:
                resource: The resource.
        """

        if self._resources is None:
            self._resources = []

        self._resources.append(resource)

        return self

    def add_resources(self, resources: list[Resource]) -> ResourceCatalogBuilder:
        """
        Adds a list of resources.
        
            Args:
                resource: The list of resources.
        """

        if self._resources is None:
            self._resources = []

        for resource in resources:
            self._resources.append(resource)

        return self

    def build(self) -> ResourceCatalog:
        """
        Builds the resource catalog.
        """
        return ResourceCatalog(self._id, self._properties, self._resources)

class ResourceBuilder:
    """
    A resource builder simplifies building a resource.
    """

    def __init__(self, id: str):
        """
        Initializes a new instance of the ResourceBuilder
        
            Args:
                id: The identifier of the resource to be built.
        """
        self._id: str = id
        self._properties: Optional[dict[str, object]] = None
        self._representations: Optional[list[Representation]] = None

    def with_property(self, key: str, value: Any) -> ResourceBuilder:
        """
        Adds a property.
        
            Args:
                key: The key of the property.
                value: The value of the property.
        """

        if self._properties is None:
            self._properties = {}

        self._properties[key] = value

        return self

    def with_unit(self, unit: str) -> ResourceBuilder:
        """
        Adds a unit.
        
            Args:
                unit: The unit to add.
        """
        return self.with_property(_UNIT, unit)

    def with_description(self, description: str) -> ResourceBuilder:
        """
        Adds a description.
        
            Args:
                description: The description to add.
        """
        return self.with_property(_DESCRIPTION, description)

    def with_warning(self, warning: str) -> ResourceBuilder:
        """
        Adds a warning.
        
            Args:
                warning: The warning to add.
        """
        return self.with_property(_WARNING, warning)

    def with_groups(self, groups: list[str]) -> ResourceBuilder:
        """
        Adds groups.
        
            Args:
                groups: The groups to add.
        """
        return self.with_property(_GROUPS, groups)

    def add_representation(self, representation: Representation) -> ResourceBuilder:
        """
        Adds a representation.
        
            Args:
                representation: The representation.
        """

        if self._representations is None:
            self._representations = []

        self._representations.append(representation)

        return self

    def add_representations(self, representations: list[Representation]) -> ResourceBuilder:
        """
        Adds a list of representations.
        
            Args:
                representations: The list of representations.
        """

        if self._representations is None:
            self._representations = []

        for representation in representations:
            self._representations.append(representation)

        return self

    def build(self) -> Resource:
        """
        Builds the resource.
        """

        return Resource(self._id, self._properties, self._representations)
