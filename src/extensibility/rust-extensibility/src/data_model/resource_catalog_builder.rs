use std::collections::HashMap;

use super::{
    Resource, ResourceCatalog,
    resource_catalog::{ResourceCatalogId, Resources, ResourcesError},
};

const README: &str = "readme";
const LICENSE: &str = "license";

/// A resource catalog builder simplifies building a resource catalog.
pub struct ResourceCatalogBuilder {
    id: ResourceCatalogId,
    properties: Option<HashMap<String, String>>,
    resources: Option<Vec<Resource>>,
}

impl ResourceCatalogBuilder {
    /// Initializes a new instance of ResourceCatalogBuilder.
    pub fn new(id: ResourceCatalogId) -> Self {
        ResourceCatalogBuilder {
            id,
            properties: None,
            resources: None,
        }
    }

    /// Adds a property.
    pub fn with_property(&mut self, key: String, value: String) -> &mut Self {
        self.properties
            .get_or_insert_with(HashMap::new)
            .insert(key, value);

        self
    }

    /// Adds a readme.
    pub fn with_readme(&mut self, readme: String) -> &mut Self {
        self.with_property(README.to_string(), readme);
        self
    }

    /// Adds a license.
    pub fn with_license(&mut self, license: String) -> &mut Self {
        self.with_property(LICENSE.to_string(), license);
        self
    }

    /// Adds a resource.
    pub fn add_resource(&mut self, resource: Resource) -> &mut Self {
        self.resources.get_or_insert_with(Vec::new).push(resource);
        self
    }

    /// Adds a list of resources.
    pub fn add_resources(&mut self, resources: Vec<Resource>) -> &mut Self {
        self.resources
            .get_or_insert_with(Vec::new)
            .extend(resources);

        self
    }

    /// Builds the resource catalog.
    pub fn build(self) -> Result<ResourceCatalog, ResourcesError> {
        let resources = self.resources.map(Resources::try_new).transpose()?;

        Ok(ResourceCatalog {
            id: self.id,
            properties: self.properties,
            resources,
        })
    }
}
