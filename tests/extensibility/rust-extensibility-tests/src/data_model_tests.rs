#[cfg(test)]
mod tests {
    use chrono::NaiveTime;
    use nexus_extensibility::data_model::SamplePeriod;
    use rstest::rstest;

    #[rstest]
    #[case("00:00:00.000000100", "100_ns")]
    #[case("00:00:00.000000200", "200_ns")]
    #[case("00:00:00.000001500", "1500_ns")]
    #[case("00:00:00.000001000", "1_us")]
    #[case("00:00:00.000010000", "10_us")]
    #[case("00:00:00.000100000", "100_us")]
    #[case("00:00:00.001500000", "1500_us")]
    #[case("00:00:00.001000000", "1_ms")]
    #[case("00:00:00.010000000", "10_ms")]
    #[case("00:00:00.100000000", "100_ms")]
    #[case("00:00:01.500000000", "1500_ms")]
    #[case("00:00:01.000000000", "1_s")]
    #[case("00:00:15.000000000", "15_s")]
    #[case("00:01:00.000000000", "1_min")]
    #[case("00:15:00.000000000", "15_min")]

    fn can_create_unit_strings(#[case] period_string: &str, #[case] expected: &str) {
        // Arrange
        let naive_time = NaiveTime::parse_from_str(period_string, "%H:%M:%S.%f");
        let time_delta = naive_time.unwrap() - NaiveTime::MIN;
        let duration = time_delta.to_std().unwrap();
        let sample_period = SamplePeriod::try_new(duration).unwrap();

        // Act
        let actual = sample_period.to_unit_string();

        // Assert
        assert_eq!(expected, actual);
    }
}
