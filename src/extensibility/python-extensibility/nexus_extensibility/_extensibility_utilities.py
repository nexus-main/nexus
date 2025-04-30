from abc import ABC
from datetime import datetime, timedelta
from typing import Tuple

from ._data_model import Representation


class ExtensibilityUtilities(ABC):
    """
    A class with methods to help with buffers.
    """

    @staticmethod
    def create_buffers(representation: Representation, begin: datetime, end: datetime) -> Tuple[memoryview, memoryview]:
        """
        Creates data and status buffers for the given input data.
        """
        element_count = ExtensibilityUtilities._calculate_element_count(begin, end, representation.sample_period)

        data = bytearray(element_count * representation.element_size)
        status = bytearray(element_count)

        return (memoryview(data), memoryview(status))

    @staticmethod
    def _calculate_element_count(begin: datetime, end: datetime, sample_period: timedelta) -> int:
        return int((end - begin).total_seconds() / sample_period.total_seconds())
