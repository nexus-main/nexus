# Data writer plugins

A data writer plugin implements `IDataWriter`. It receives selected catalog items and writes export files into a target folder provided by Nexus.

Reference page: <xref:Nexus.Extensibility.IDataWriter>.

## Contract overview

Important methods are:

- `SetContextAsync(...)`: receives the writer context and logger.
- `OpenAsync(fileBegin, filePeriod, samplePeriod, catalogItems, ...)`: prepares the output file or files for a time slice.
- `WriteAsync(fileOffset, requests, progress, ...)`: writes data buffers for the current slice.
- `CloseAsync(...)`: flushes and closes output.

## Writer descriptions

Writers can expose options to the UI. The built-in CSV writer uses a JSON description to define settings such as timestamp format and significant figures. Keep option labels understandable for users, because they appear in export workflows.

Example: the built-in [CSV writer](https://github.com/nexus-main/nexus/blob/master/src/Nexus/Extensions/Writers/Csv.cs) shows how a writer implements `IDataWriter`, exposes UI options with `DataWriterDescription`, prepares output in `OpenAsync(...)`, writes buffers in `WriteAsync(...)`, and closes the generated files in `CloseAsync(...)`.

## Output expectations

A writer should produce files that can be archived and downloaded by Nexus. If possible, include metadata that helps downstream tools understand units, resource names, sample periods, and catalog context.

## When to write a writer

Write a writer when an organization needs a specific exchange format or when downstream tools expect a format not built into Nexus.
