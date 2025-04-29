use chrono::NaiveTime;
use nexus_extensibility::data_model::Resource;
use nexus_extensibility::data_model::ResourceCatalogId;
use nexus_extensibility::data_model::ResourceId;
use nexus_extensibility::data_model::Resources;
use nexus_extensibility::data_model::SamplePeriod;
use rstest::rstest;

#[rstest]
#[case("/a", true)]
#[case("/_a", true)]
#[case("/ab_c", true)]
#[case("/a9_b/c__99", true)]
#[case("", false)]
#[case("/", false)]
#[case("/a/", false)]
#[case("/9", false)]
#[case("a", false)]
fn can_validate_catalog_id(#[case] id: &str, #[case] is_valid: bool) {
    // Arrange

    // Act
    let result = ResourceCatalogId::try_new(id);

    // Assert
    assert_eq!(is_valid, result.is_ok());
}

#[rstest]
#[case("_temp", true)]
#[case("temp", true)]
#[case("Temp", true)]
#[case("Temp_1", true)]
#[case("", false)]
#[case("1temp", false)]
#[case("teßp", false)]
#[case("ª♫", false)]
#[case("tem p", false)]
#[case("tem-p", false)]
#[case("tem*p", false)]
fn can_validate_resource_id(#[case] id: &str, #[case] is_valid: bool) {
    // Arrange

    // Act
    let result = ResourceId::try_new(id);

    // Assert
    assert_eq!(is_valid, result.is_ok());
}

#[rstest]
#[case("00:01:00", true)]
#[case("00:00:00", false)]
fn can_validate_representation_sample_period(
    #[case] sample_period_string: &str,
    #[case] is_valid: bool,
) {
    // Arrange
    let duration = NaiveTime::parse_from_str(sample_period_string, "%H:%M:%S")
        .unwrap()
        .signed_duration_since(NaiveTime::MIN)
        .to_std()
        .unwrap();

    // Act
    let result = SamplePeriod::try_new(duration);

    // Assert
    assert_eq!(is_valid, result.is_ok());
}

#[rstest]
fn can_validate_resources() {
    // Arrange
    let id = ResourceId::try_new("R1").unwrap();

    // Act
    let result = Resources::try_new(vec![Resource {
        id,
        properties: None,
        representations: None,
    }]);

    // Assert
    assert!(result.is_ok());
}
