# Background

To not only provide a streaming API for the data but also a possibility to store the data into files with a certain scientific format for further processing, Nexus provides an extensibility mechanism similar to the one for [Data Source](data-source.md). For each export request, a `DataWriterController` is instantiated which wraps an instance of the `IDataWriter`. The `DataWriterController` is mainly responsible to ensure the correct file period. E.g. the user wants the time-series data to be written into files of 1 hour each, the `DataWriterController` takes care of it.

# IDataWriter

The interface is defined as follows:

```cs
/* called right after instantiation to provide the target URL, parameters and a logger instance */
Task SetContextAsync(...);

/* called whenever the wrapping DataSourceController decides to create a new file */
Task OpenAsync(...)

/* called whenever a data portion should be written to the file */
Task WriteAsync(...)

/* called when all data have been written into the file */
Task CloseAsync(...)
```

# Life Cycle
`IDataWriter` instances are short-lived to make them thread-safe and enable them to cache open connections or files handles but at the same time make them free all resources when they are disposed (1).

When data shall be written into files of a certain type (e.g. CSV files), the corresponding `IDataWriter` is instantiated as described above and the `OpenAsync` is called. All data of catalog items to be written in subsequent `WriteAsync` calls share the same sample period but not necessarily the same parental catalogs. An `IDataWriter` is supposed to write all provided catalog metadata into the files or associate them in any other way (e.g. with separate metadata files). Most of the time this means that the `IDataWriter` needs to create separate files per catalog (e.g. one CSV file per catalog). Other file formats may allow a hierarchical organisation of data and metadata. In those cases a single file for all catalogs may be feasible.

For better performance, the data from the data sources is pipelined to the data writer. Thus the size per write request depends on the data source settings. This may lead to more than one invocation of the `WriteAsync` method.

When the current file period is done, the `CloseAsync` method is invoked. This gives the `IDataWriter` the chance to flush the data to disk (if not already done) and close all open resources.

The described process repeats until all file periods are processed.

All data provided are of type `float64` to avoid the need of a generic data writer. Another advantage of float numbers is that unlike integers they allow the expression of invalid numbers as `NaN`.

(1) A `IDataWriter` instance is disposed automatically by Nexus when it implements the `IDisposable` interface.