use std::time::Duration;

use nutype::nutype;

#[nutype(
    derive(AsRef),
    validate(predicate = |x| *x != Duration::ZERO),
)]
pub struct SamplePeriod(Duration);
