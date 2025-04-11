use std::time::Duration;

use nutype::nutype;

#[nutype(
    derive(AsRef),
    validate(predicate = |x| *x != Duration::ZERO),
)]
pub struct SamplePeriod(Duration);

static QUOTIENTS: [u128; 7] = [1000, 1000, 1000, 60, 60, 24, 1];
static POST_FIXES: [&str; 7] = ["ns", "us", "ms", "s", "min", "h", "d"];

impl SamplePeriod {
    /// Converts the period into a human readable number string with unit.
    pub fn to_unit_string(&self) -> String {
        let mut current_value = self.as_ref().as_nanos();

        for i in 0..POST_FIXES.len() {
            let quotient = current_value / QUOTIENTS[i];
            let remainder = current_value % QUOTIENTS[i];

            if remainder != 0 {
                return format!("{}_{}", current_value, POST_FIXES[i]);
            } else {
                current_value = quotient;
            }
        }

        format!("{}_{}", current_value, POST_FIXES[POST_FIXES.len() - 1])
    }
}
