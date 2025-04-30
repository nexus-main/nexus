use super::super::data_model::{CatalogItem, CatalogRegistration, ResourceCatalog};
use chrono::{DateTime, Utc};
use std::collections::HashMap;
use url::Url;

/// Defines logging severity levels.
pub enum LogLevel {
    /// Logs that contain the most detailed messages. These messages may contain sensitive application data. These messages are disabled by default and should never be enabled in a production environment.
    Trace = 0,

    /// Logs that are used for interactive investigation during development. These logs should primarily contain information useful for debugging and have no long-term value.
    Debug = 1,

    /// Logs that track the general flow of the application. These logs should have long-term value.
    Information = 2,

    /// Logs that highlight an abnormal or unexpected event in the application flow, but do not otherwise cause the application execution to stop.
    Warning = 3,

    /// Logs that highlight when the current flow of execution is stopped due to a failure. These should indicate a failure in the current activity, not an application-wide failure.
    Error = 4,

    /// Logs that describe an unrecoverable application or system crash, or a catastrophic failure that requires immediate attention.
    Critical = 5,
}

/// A logger.
pub trait Logger {
    /// Logs a given message.
    fn log(&self, log_level: LogLevel, message: &str);
}

/// The starter package for a data source.
pub struct DataSourceContext<T> {
    /// An optional URL which points to the data.
    pub resource_locator: Option<Url>,

    /// The source configuration.
    pub source_configuration: T,

    /// The request configuration.
    pub request_configuration: Option<HashMap<String, String>>,
}

/// A catalog time range.
pub struct CatalogTimeRange {
    /// The date/time of the first data in the catalog.
    pub begin: DateTime<Utc>,

    /// The date/time of the last data in the catalog.
    pub end: DateTime<Utc>,
}

/// A read request.
pub struct ReadRequest {
    /// The original resource name.
    pub original_resource_name: String,

    /// The CatalogItem to be read.
    pub catalog_item: CatalogItem,

    /// The data buffer.
    pub data: Vec<u8>,

    /// The status buffer. A value of 0x01 ('1') indicates that the corresponding value in the data buffer is valid, otherwise it is treated as float("NaN").
    pub status: Vec<u8>,
}

/// A type alias for the `ReadDataHandler` in Rust.
pub type ReadDataHandler = fn(
    resource_path: &str,    // The path to the resource data to stream.
    begin: DateTime<Utc>,   // Start date/time.
    end: DateTime<Utc>,     // End date/time.
    buffer: &mut [f64],     // The buffer to read to the data into.
    cancel_flag: &mut bool, // A cancellation token.
);

/// A data source.
pub trait DataSource<T> {
    /// Invoked by Nexus right after construction to provide the context.
    fn set_context(
        &mut self,
        context: DataSourceContext<T>,
        logger: Box<dyn Logger>,
    ) -> impl Future;

    /// Gets the catalog registrations that are located under path.
    fn get_catalog_registrations(
        &self,
        path: &str,
    ) -> impl Future<Output = Vec<CatalogRegistration>>;

    /// Enriches the provided ResourceCatalog.
    fn enrich_catalog(
        &self,
        catalog: ResourceCatalog,
    ) -> impl Future<Output = Vec<ResourceCatalog>>;

    /// Gets the time range of the ResourceCatalog.
    fn get_time_range(&self, catalog_id: &str) -> impl Future<Output = Vec<CatalogTimeRange>>;

    /// Gets the availability of the ResourceCatalog.
    fn get_availability(
        &self,
        catalog_id: &str,
        begin: DateTime<Utc>,
        end: DateTime<Utc>,
    ) -> impl Future<Output = Vec<f64>>;

    /// Performs a number of read requests.
    fn read(
        &self,
        begin: DateTime<Utc>,
        end: DateTime<Utc>,
        requests: Vec<ReadRequest>,
        read_data: ReadDataHandler,
        report_progress: &dyn Fn(f64),
    ) -> impl Future;
}

/// Data sources which have configuration data to be upgraded should implement this interface.
pub trait UpgradableDataSource {
    /// Upgrades the source configuration.
    fn upgrade_source_configuration(&self, configuration: String) -> impl Future;
}

/* pub trait SimpleDataSource<T>: DataSource<T> {}
 * ...
 *
 * Cannot be implemented until rust supports specialization
 * https://github.com/rust-lang/rust/issues/31844
 *
 * Solution: https://rust-lang.github.io/rfcs/1210-impl-specialization.html#reuse
 *
 * Example showing the problem:
 */

// trait A {
//     fn virtual_method(self);
//     fn abstract_method(self);
// }

// trait B: A {}

// impl<T: B> A for T {
//     fn virtual_method(self) {
//         todo!();
//     }
// }

// struct C {}

// impl C {
//     fn new() {
//         let bbbbb = C {};
//         bbbbb.virtual_method();
//     }
// }

// impl B for C {}
