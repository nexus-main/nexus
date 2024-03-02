import enum
from abc import ABC, abstractmethod
from dataclasses import dataclass
from datetime import datetime
from typing import (Any, Awaitable, Callable, Dict, List, Optional, Protocol,
                    Tuple)
from urllib.parse import ParseResult

from ._data_model import CatalogItem, CatalogRegistration, ResourceCatalog
from ._i_extension import IExtension

################# DATA SOURCE TYPES ###############

class LogLevel(enum.IntEnum):
    """Defines logging severity levels."""

    Trace = 0
    """Logs that contain the most detailed messages. These messages may contain sensitive application data. These messages are disabled by default and should never be enabled in a production environment."""

    Debug = 1
    """Logs that are used for interactive investigation during development. These logs should primarily contain information useful for debugging and have no long-term value."""

    Information = 2
    """Logs that track the general flow of the application. These logs should have long-term value."""

    Warning = 3
    """Logs that highlight an abnormal or unexpected event in the application flow, but do not otherwise cause the application execution to stop."""

    Error = 4
    """Logs that highlight when the current flow of execution is stopped due to a failure. These should indicate a failure in the current activity, not an application-wide failure."""

    Critical = 5
    """Logs that describe an unrecoverable application or system crash, or a catastrophic failure that requires immediate attention."""

class ILogger(ABC):

    @abstractmethod
    def log(self, log_level: LogLevel, message: str):
        pass

@dataclass(frozen=True)
class DataSourceContext:
    """
    The starter package for a data source.

    Args:
        resource_locator: An optional URL which points to the data.
        system_configuration: The system configuration.
        source_configuration: The source configuration.
        request_configuration: The request configuration.
    """

    resource_locator: Optional[ParseResult]
    """The unique identifier of the package reference."""

    system_configuration: Optional[Dict[str, Any]]
    """The system configuration."""

    source_configuration: Optional[Dict[str, Any]]
    """The source configuration."""

    request_configuration: Optional[Dict[str, Any]]
    """The request configuration."""

@dataclass(frozen=True)
class ReadRequest:
    """
    A read request.

    Args:
        catalog_item: The CatalogItem to be read.
        data: The data buffer.
        status: The status buffer. A value of 0x01 ('1') indicates that the corresponding value in the data buffer is valid, otherwise it is treated as float("NaN").
    """

    catalog_item: CatalogItem
    """The CatalogItem to be read."""

    data: memoryview
    """The data buffer."""

    status: memoryview
    """The status buffer. A value of 0x01 ('1') indicates that the corresponding value in the data buffer is valid, otherwise it is treated as float("NaN")."""

class ReadDataHandler(Protocol):
    """
    A handler to read data.
    """

    def __call__(self, resource_path: str, begin: datetime, end: datetime) -> Awaitable[memoryview]:
        """
        Reads the requested data.

        Args:
            resource_path: The path to the resource data to stream.
            begin: Start date/time.
            end: End date/time.
        """
        ...


################# DATA SOURCE ###############

class IDataSource(IExtension, ABC):
    """
    A data source.
    """

    @abstractmethod
    def set_context(self, context: DataSourceContext, logger: ILogger) -> Awaitable[None]:
        """
        Invoked by Nexus right after construction to provide the context.

        Args:
            context: The context.
            logger: The logger.
        """
        pass

    @abstractmethod
    def get_catalog_registrations(self, path: str) -> Awaitable[List[CatalogRegistration]]:
        """
        Gets the catalog registrations that are located under path.

        Args:
            path: The parent path for which to return catalog registrations.
        """
        pass

    @abstractmethod
    def get_catalog(self, catalog_id: str) -> Awaitable[ResourceCatalog]:
        """
        Gets the requested ResourceCatalog.

        Args:
            catalog_id: The catalog identifier.
        """
        pass

    @abstractmethod
    def get_time_range(self, catalog_id: str) -> Awaitable[Tuple[datetime, datetime]]:
        """
        Gets the time range of the ResourceCatalog.

        Args:
            catalog_id: The catalog identifier.
        """
        pass

    @abstractmethod
    def get_availability(self, catalog_id: str, begin: datetime, end: datetime) -> Awaitable[float]:
        """
        Gets the availability of the ResourceCatalog.

        Args:
            catalog_id: The catalog identifier.
            begin: The begin of the availability period.
            end: The end of the availability period.
        """
        pass

    @abstractmethod
    def read(
        self,
        begin: datetime, 
        end: datetime,
        requests: list[ReadRequest], 
        read_data: ReadDataHandler,
        report_progress: Callable[[float], None]) -> Awaitable[None]:
        """
        Performs a number of read requests.

        Args:
            begin: The beginning of the period to read.
            end: The end of the period to read.
            requests: The array of read requests.
            read_data: A delegate to asynchronously read data from Nexus.
            report_progress: A callable to report the read progress between 0.0 and 1.0.
        """
        pass

class SimpleDataSource(IDataSource, ABC):
    """
    A simple implementation of a data source.
    """

    Context: DataSourceContext
    """Gets the data source context. This property is not accessible from within class constructors as it will bet set later."""

    Logger: ILogger
    """Gets the data logger. This property is not accessible from within class constructors as it will bet set later."""

    async def set_context(self, context: DataSourceContext, logger: ILogger):
        self.Context = context
        self.Logger = logger

    @abstractmethod
    def get_catalog_registrations(self, path: str) -> Awaitable[List[CatalogRegistration]]:
        pass

    @abstractmethod
    def get_catalog(self, catalog_id: str) -> Awaitable[ResourceCatalog]:
        pass

    async def get_time_range(self, catalog_id: str) -> Tuple[datetime, datetime]:
        return (datetime.min, datetime.max)

    async def get_availability(self, catalog_id: str, begin: datetime, end: datetime) -> float:
        return float("NaN")

    @abstractmethod
    def read(
        self,
        begin: datetime, 
        end: datetime,
        requests: list[ReadRequest], 
        read_data: ReadDataHandler,
        report_progress: Callable[[float], None]) -> Awaitable[None]:
        pass
