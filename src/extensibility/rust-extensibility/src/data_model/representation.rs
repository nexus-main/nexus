use std::collections::HashMap;

use super::{NexusDataType, SamplePeriod};

pub struct Representation {
    pub data_type: NexusDataType,
    pub sample_period: SamplePeriod,
    pub parameters: HashMap<String, String>,
}

impl Representation {
    pub fn id(&self) -> String {
        self.sample_period.to_unit_string()
    }
}
