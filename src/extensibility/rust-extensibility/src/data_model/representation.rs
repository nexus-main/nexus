use std::{collections::HashMap, time::Duration};

use crate::NexusDataType;

pub struct Representation {
    id: String,
    pub data_type: NexusDataType,
    pub sample_period: Duration,
    pub parameters: HashMap<String, String>,
}

impl Representation {
    pub fn new(
        self,
        data_type: NexusDataType,
        sample_period: Duration,
        parameters: HashMap<String, String>,
    ) {
        Representation {
            id: "",
            data_type,
            sample_period,
            parameters,
        }
    }

    pub fn id() -> String {}
}
