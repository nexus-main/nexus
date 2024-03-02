from datetime import datetime, timedelta

import pytest
from nexus_extensibility import to_unit_string


@pytest.mark.parametrize(
    "sample_period_string, expected", 
    [
        ("00:00:00.000001", "1_us"),
        ("00:00:00.000010", "10_us"),
        ("00:00:00.000100", "100_us"),
        ("00:00:00.001500", "1500_us"),

        ("00:00:00.001000", "1_ms"),
        ("00:00:00.010000", "10_ms"),
        ("00:00:00.100000", "100_ms"),
        ("00:00:01.500000", "1500_ms"),

        ("00:00:01.000000", "1_s"),
        ("00:00:15.000000", "15_s"),

        ("00:01:00.000000", "1_min")
    ])
def can_create_unit_strings_test(sample_period_string: str, expected: str):

    datetime_tmp = datetime.strptime(sample_period_string, "%H:%M:%S.%f")

    timedelta_tmp = timedelta(
        hours=datetime_tmp.hour, 
        minutes=datetime_tmp.minute, 
        seconds=datetime_tmp.second, 
        microseconds=datetime_tmp.microsecond)

    actual = to_unit_string(timedelta_tmp)

    assert actual == expected
    
