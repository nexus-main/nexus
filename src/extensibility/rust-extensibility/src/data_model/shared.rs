use chrono::TimeDelta;
use nutype::nutype;

#[nutype(
    derive(AsRef),
    validate(predicate = |x| *x > TimeDelta::zero()),
)]
pub struct SamplePeriod(TimeDelta);
