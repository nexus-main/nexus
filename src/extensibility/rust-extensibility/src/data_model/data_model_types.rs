use std::collections::HashMap;

use super::{
    Representation, Resource, ResourceCatalog, SamplePeriod, resource_catalog::VALID_ID_EXPRESSION,
};

enum RepresentationKind {
    Original = 0,
    Resampled = 10,
    Mean = 20,
    MeanPolarDeg = 30,
    Min = 40,
    Max = 50,
    Std = 60,
    Rms = 70,
    MinBitwise = 80,
    MaxBitwise = 90,
    Sum = 100,
}

/// Specifies the Nexus data type.
#[repr(u16)]
pub enum NexusDataType {
    /// Unsigned 8-bit integer.
    UINT8 = 0x108,

    /// Signed 8-bit integer.
    INT8 = 0x208,

    /// Unsigned 16-bit integer.
    UINT16 = 0x110,

    /// Signed 16-bit integer.
    INT16 = 0x210,

    /// Unsigned 32-bit integer.
    UINT32 = 0x120,

    /// Signed 32-bit integer.
    INT32 = 0x220,

    /// Unsigned 64-bit integer.
    UINT64 = 0x140,

    /// Signed 64-bit integer.
    INT64 = 0x240,

    /// 32-bit floating-point number.
    FLOAT32 = 0x320,

    /// 64-bit floating-point number.
    FLOAT64 = 0x340,
}

/// <summary>
/// A catalog item consists of a catalog, a resource and a representation.
/// </summary>
pub struct CatalogItem {
    /// The catalog.
    pub catalog: ResourceCatalog,

    /// The resource.
    pub resource: Resource,

    /// The representation.
    pub representation: Representation,

    /// The optional dictionary of representation parameters and its arguments.
    pub parameters: Option<HashMap<String, String>>,
}

impl CatalogItem {
    // /// Construct a fully qualified path.
    // pub fn to_path(&self) -> String {
    //     let parameters_string =
    //         DataModelUtilities.get_representation_parameter_string(self.parameters);

    //     format!(
    //         "{}/{}/{}{}",
    //         self.catalog.id,
    //         self.resource.id,
    //         self.representation.id(),
    //         parameters_string
    //     )
    // }
}

#[derive(PartialEq, PartialOrd, Eq, Ord)]
pub struct CatalogPath(String);

impl CatalogPath {
    pub fn new(path: String) -> Result<Self, &'static str> {
        if CatalogPath::is_valid_path(&path) {
            Ok(Self(path))
        } else {
            Err("The catalog path {Path} is not valid.")
        }
    }

    fn is_valid_path(path: &str) -> bool {
        if path == "/" {
            return true;
        }

        let real_path = if path.starts_with("/") {
            path
        } else {
            &format!("/{path}")
        };

        VALID_ID_EXPRESSION.is_match(real_path)
    }
}

/// A catalog registration.
pub struct CatalogRegistration {
    /// The absolute or relative path of the catalog.
    pub path: CatalogPath,

    /// An optional title.
    pub title: Option<String>,

    /// A boolean which indicates if the catalog and its children should be reloaded on each request.
    pub is_transient: bool,

    /// An optional link target (i.e. another absolute catalog path) which makes this catalog a softlink.
    pub link_target: Option<String>,
}

struct ResourcePathParseResult {
    catalog_id: String,
    resource_id: String,
    sample_period: SamplePeriod,
    kind: RepresentationKind,
    parameters: Option<String>,
    base_period: Option<SamplePeriod>,
}
