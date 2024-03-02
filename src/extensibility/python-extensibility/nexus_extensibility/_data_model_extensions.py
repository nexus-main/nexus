from datetime import timedelta

_quotients = [1000, 1000, 60, 1 ]
_post_fixes = ["us", "ms", "s", "min"]

def to_unit_string(sample_period: timedelta) -> str:
    """
    Converts period into a human readable number string with unit.

    Args:
        sample_period: The period to convert.
    """

    current_value = sample_period.total_seconds() * 1e6
    
    for i in range(len(_post_fixes)):

        quotient, remainder = divmod(current_value, _quotients[i])

        if remainder != 0:
            return f"{str(int(current_value))}_{_post_fixes[i]}"

        else:
            current_value = quotient

    return f"{str(int(current_value))}_{_post_fixes[-1]}"