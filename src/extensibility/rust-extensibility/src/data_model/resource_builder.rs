use std::collections::HashMap;

use super::{
    Representation, Resource,
    resource::{Representations, RepresentationsError, ResourceId},
};

const DESCRIPTION: &str = "description";
const WARNING: &str = "warning";
const UNIT: &str = "unit";
const GROUPS: &str = "groups";

/// A resource builder simplifies building a resource.
pub struct ResourceBuilder {
    id: ResourceId,
    properties: Option<HashMap<String, String>>,
    representations: Option<Vec<Representation>>,
}

impl ResourceBuilder {
    /// Initializes a new instance of the ResourceBuilder
    pub fn new(id: ResourceId) -> Self {
        ResourceBuilder {
            id,
            properties: None,
            representations: None,
        }
    }

    /// Adds a property.
    pub fn with_property(&mut self, key: String, value: String) -> &mut Self {
        self.properties
            .get_or_insert_with(HashMap::new)
            .insert(key, value);

        self
    }

    /// Adds a unit.
    pub fn with_unit(&mut self, unit: String) -> &mut Self {
        self.with_property(UNIT.to_string(), unit);
        self
    }

    /// Adds a description.
    pub fn with_description(&mut self, description: String) -> &mut Self {
        self.with_property(DESCRIPTION.to_string(), description);
        self
    }

    /// Adds a warning.
    pub fn with_warning(&mut self, warning: String) -> &mut Self {
        self.with_property(WARNING.to_string(), warning);
        self
    }

    /// Adds groups.
    pub fn with_groups(&mut self, groups: Vec<String>) -> &mut Self {
        let groups_value = groups.join(",");
        self.with_property(GROUPS.to_string(), groups_value);
        self
    }

    /// Adds a representation.
    pub fn add_representation(&mut self, representation: Representation) -> &mut Self {
        if self.representations.is_none() {
            self.representations = Some(vec![]);
        }

        self.representations.as_mut().unwrap().push(representation);
        self
    }

    /// Adds a list of representations.
    pub fn add_representations(&mut self, representations: Vec<Representation>) -> &mut Self {
        self.representations
            .get_or_insert_with(Vec::new)
            .extend(representations);

        self
    }

    /// Builds the resource.
    pub fn build(self) -> Result<Resource, RepresentationsError> {
        let representations = self
            .representations
            .map(Representations::try_new)
            .transpose()?;

        Ok(Resource {
            id: self.id,
            properties: self.properties,
            representations,
        })
    }
}
