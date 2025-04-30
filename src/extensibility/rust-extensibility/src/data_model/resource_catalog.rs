use nutype::nutype;
use regex::Regex;
use std::{
    collections::{HashMap, HashSet},
    sync::LazyLock,
};

use super::Resource;

/// A regular expression to validate a resource catalog identifier.
pub static VALID_ID_EXPRESSION: LazyLock<Regex> =
    LazyLock::new(|| Regex::new(r"^(?:\/[a-zA-Z_][a-zA-Z_0-9]*)+$").unwrap());

/// A resource catalog id.
#[nutype(
    derive(Display),
    validate(regex = VALID_ID_EXPRESSION)
)]
pub struct ResourceCatalogId(String);

// A list of resources.
#[nutype(
    validate(predicate = |x| Resources::validate_resources(x)),
)]
pub struct Resources(Vec<Resource>);

impl Resources {
    fn validate_resources(resources: &[Resource]) -> bool {
        resources
            .iter()
            .map(|x| &x.id)
            .collect::<HashSet<_>>()
            .len()
            == resources.len()
    }
}

/// A catalog is a top level element and holds a list of resources.
pub struct ResourceCatalog {
    /// The catalog identifier.
    pub id: ResourceCatalogId,

    /// The properties.
    pub properties: Option<HashMap<String, String>>,

    /// The list of resources.
    pub resources: Option<Resources>,
}

impl ResourceCatalog {}
