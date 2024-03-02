from typing import Dict, Optional


def _get_representation_parameter_string(parameters: Optional[Dict[str, str]]) -> Optional[str]:
    
    if (parameters is None):
        return None

    serialized_parameters = (f"{parameter[0]}={parameter[1]}" for parameter in parameters.items())
    parameter_string = f"({','.join(serialized_parameters)})"

    return parameter_string