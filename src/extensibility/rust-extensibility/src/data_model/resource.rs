use std::{
    collections::{HashMap, HashSet},
    sync::LazyLock,
};

use nutype::nutype;
use regex::Regex;

use super::Representation;

/// A regular expression to validate a resource identifier.
pub static VALID_ID_EXPRESSION: LazyLock<Regex> =
    LazyLock::new(|| Regex::new(r"^[a-zA-Z_][a-zA-Z_0-9]*$").unwrap());

/// A regular expression to find invalid characters in a resource identifier.
pub static INVALID_ID_CHARS_EXPRESSION: LazyLock<Regex> =
    LazyLock::new(|| Regex::new(r"[^a-zA-Z_0-9]").unwrap());

/// A regular expression to find invalid start characters in a resource identifier.
pub static INVALID_ID_START_CHARS_EXPRESSION: LazyLock<Regex> =
    LazyLock::new(|| Regex::new(r"^[^a-zA-Z_]+").unwrap());

/// A resource id.
#[nutype(
    derive(Display, Eq, Hash, PartialEq),
    validate(regex = VALID_ID_EXPRESSION)
)]
pub struct ResourceId(String);

/// A list of representations
#[nutype(
    validate(predicate = |x| Representations::validate_representations(x)),
)]
pub struct Representations(Vec<Representation>);

impl Representations {
    fn validate_representations(representations: &[Representation]) -> bool {
        representations
            .iter()
            .map(|x| x.id())
            .collect::<HashSet<_>>()
            .len()
            == representations.len()
    }
}

/// A resource is part of a resource catalog and holds a list of representations.
pub struct Resource {
    /// The resource identifier.
    pub id: ResourceId,

    /// The properties.
    pub properties: Option<HashMap<String, String>>,

    /// The list of representations.
    pub representations: Option<Representations>,
}
