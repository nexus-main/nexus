use std::collections::HashMap;

use nutype::nutype;

use super::{
    NexusDataType, SamplePeriod, data_model_extensions::DataModelExtensions,
    resource::VALID_ID_EXPRESSION,
};

#[nutype(
    derive(IntoIterator),
    validate(predicate = |x| RepresentationParameters::validate_parameters(x))
)]
pub struct RepresentationParameters(HashMap<String, String>);

impl RepresentationParameters {
    fn validate_parameters(parameters: &HashMap<String, String>) -> bool {
        for key in parameters.keys() {
            if !VALID_ID_EXPRESSION.is_match(key) {
                return false;
            }
        }

        true
    }
}

/// A representation is part of a resource.
pub struct Representation {
    /// The data type.
    pub data_type: NexusDataType,

    /// The sample period.
    pub sample_period: SamplePeriod,

    /// The optional list of parameters.
    pub parameters: RepresentationParameters,
}

impl Representation {
    /// Gets the identifer of the representation. It is constructed using the sample period.
    pub fn id(&self) -> String {
        DataModelExtensions::to_unit_string(&self.sample_period)
    }

    /// The number of bits per element.
    pub fn element_size(&self) -> i32 {
        ((self.data_type as i32) & 0xFF) >> 3
    }
}
