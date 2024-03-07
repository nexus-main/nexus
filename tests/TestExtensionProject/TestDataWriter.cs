using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Extensibility;

namespace TestExtensionProject;

[ExtensionDescription("A data writer for unit tests.", default!, default!)]
public class TestDataWriter : IDataWriter
{
    public Task CloseAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException(nameof(CloseAsync));
    }

    public Task OpenAsync(DateTime fileBegin, TimeSpan filePeriod, TimeSpan samplePeriod, CatalogItem[] catalogItems, CancellationToken cancellationToken)
    {
        throw new NotImplementedException(nameof(OpenAsync));
    }

    public Task SetContextAsync(DataWriterContext context, ILogger logger, CancellationToken cancellationToken)
    {
        throw new NotImplementedException(nameof(SetContextAsync));
    }

    public Task WriteAsync(TimeSpan fileOffset, WriteRequest[] requests, IProgress<double> progress, CancellationToken cancellationToken)
    {
        throw new NotImplementedException(nameof(WriteAsync));
    }
}
