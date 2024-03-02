from datetime import datetime, timedelta

import pytest
from nexus_extensibility import (NexusDataType, Representation, Resource,
                                 ResourceCatalog)


@pytest.mark.parametrize(
    "id, is_valid", 
    [
        ("/a", True),
        ("/_a", True),
        ("/ab_c", True),
        ("/a9_b/c__99", True),

        ("", False),
        ("/", False),
        ("/a/", False),
        ("/9", False),
        ("a", False),
    ])
def can_validate_catalog_id_test(id: str, is_valid: bool):

    if is_valid:
        ResourceCatalog(id)

    else:
        with pytest.raises(Exception):
            ResourceCatalog(id)


@pytest.mark.parametrize(
    "id, is_valid", 
    [
        ("_temp", True),
        ("temp", True),
        ("Temp", True),
        ("Temp_1", True),

        ("", False),
        ("1temp", False),
        ("teßp", False),
        ("ª♫", False),
        ("tem p", False),
        ("tem-p", False),
        ("tem*p", False)
    ])
def can_validate_resource_id_test(id: str, is_valid: bool):

    if is_valid:
        Resource(id)

    else:
        with pytest.raises(Exception):
            Resource(id)

@pytest.mark.parametrize(
    "sample_period_string, is_valid", 
    [
        ("00:01:00", True),
        ("00:00:00", False),
    ])
def can_validate_representation_sample_period_test(sample_period_string: str, is_valid: bool):

    datetime_tmp = datetime.strptime(sample_period_string, "%H:%M:%S")

    sample_period = timedelta(
        hours=datetime_tmp.hour, 
        minutes=datetime_tmp.minute, 
        seconds=datetime_tmp.second)

    if is_valid:
        Representation(
            NexusDataType.FLOAT64, 
            sample_period)

    else:
        with pytest.raises(Exception):
            Representation(
                NexusDataType.FLOAT64, 
                sample_period)

@pytest.mark.parametrize(
    "data_type, is_valid", 
    [
        (NexusDataType.FLOAT32, True),
        (0, False),
        (9999, False),
    ])
def can_validate_representation_data_type_test(data_type: NexusDataType, is_valid: bool):

    if is_valid:
        Representation(
            data_type, 
            timedelta(seconds=1))

    else:
        with pytest.raises(Exception):
            Representation(
                data_type, 
                timedelta(seconds=1))

def catalog_constructor_throws_for_non_unique_resource_test():
    
    with pytest.raises(Exception):
        ResourceCatalog(id="/C", resources=[
            Resource(id="R1"),
            Resource(id="R2"),
            Resource(id="R2")
        ])
