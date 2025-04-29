use super::representation::RepresentationParameters;

pub struct DataModelUtilities;

impl DataModelUtilities {
    pub fn get_representation_parameter_string(
        parameters: &Option<RepresentationParameters>,
    ) -> Option<String> {
        match parameters {
            Some(value) => {
                let serialized_parameters = value
                    .into_iter()
                    .map(|(key, value)| format!("{}={}", key, value))
                    .collect::<Vec<String>>();

                let parameters_string = format!("({})", serialized_parameters.join(","));

                Some(parameters_string)
            }
            None => None,
        }
    }
}
