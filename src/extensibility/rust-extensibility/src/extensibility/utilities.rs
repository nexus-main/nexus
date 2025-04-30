use chrono::{DateTime, Utc};

use super::super::data_model::{Representation, SamplePeriod};

/// A class with methods to help with buffers.
pub struct ExtensibilityUtilities;

impl ExtensibilityUtilities {
    /// Creates data and status buffers for the given input data.
    pub fn create_buffers(
        representation: &Representation,
        begin: DateTime<Utc>,
        end: DateTime<Utc>,
    ) -> (Vec<u8>, Vec<u8>) {
        let element_count = ExtensibilityUtilities::calculate_element_count(
            begin,
            end,
            &representation.sample_period,
        );

        let data = vec![0u8; element_count * representation.element_size() as usize];
        let status = vec![0u8; element_count];

        (data, status)
    }

    fn calculate_element_count(
        begin: DateTime<Utc>,
        end: DateTime<Utc>,
        sample_period: &SamplePeriod,
    ) -> usize {
        let duration = end.signed_duration_since(begin);
        let duration_ns = duration.num_nanoseconds().unwrap();
        let sample_period_ns = sample_period.as_ref().num_nanoseconds().unwrap();

        (duration_ns / sample_period_ns) as usize
    }
}
