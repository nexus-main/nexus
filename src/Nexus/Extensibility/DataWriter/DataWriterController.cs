using Nexus.Core;
using Nexus.DataModel;
using Nexus.Utilities;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Nexus.Extensibility
{
    internal interface IDataWriterController : IDisposable
    {
        Task InitializeAsync(
            ILogger logger,
            CancellationToken cancellationToken);

        Task WriteAsync(
            DateTime begin,
            DateTime end,
            TimeSpan samplePeriod,
            TimeSpan filePeriod,
            CatalogItemRequestPipeReader[] catalogItemRequestPipeReaders,
            IProgress<double> progress,
            CancellationToken cancellationToken);
    }

    // TODO: Add "CheckFileSize" method (e.g. for Famos).

    internal class DataWriterController : IDataWriterController
    {
        #region Constructors

        public DataWriterController(
            IDataWriter dataWriter,
            Uri resourceLocator,
            IReadOnlyDictionary<string, JsonElement>? systemConfiguration,
            IReadOnlyDictionary<string, JsonElement>? requestConfiguration,
            ILogger<DataWriterController> logger)
        {
            DataWriter = dataWriter;
            ResourceLocator = resourceLocator;
            SystemConfiguration = systemConfiguration;
            RequestConfiguration = requestConfiguration;
            Logger = logger;
        }

        #endregion

        #region Properties

        private IReadOnlyDictionary<string, JsonElement>? SystemConfiguration { get; }

        private IReadOnlyDictionary<string, JsonElement>? RequestConfiguration { get; }

        private IDataWriter DataWriter { get; }

        private Uri ResourceLocator { get; }

        private ILogger Logger { get; }

        #endregion

        #region Methods

        public async Task InitializeAsync(
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var context = new DataWriterContext(
                ResourceLocator: ResourceLocator,
                SystemConfiguration: SystemConfiguration,
                RequestConfiguration: RequestConfiguration);

            await DataWriter.SetContextAsync(context, logger, cancellationToken);
        }

        public async Task WriteAsync(
            DateTime begin,
            DateTime end,
            TimeSpan samplePeriod,
            TimeSpan filePeriod,
            CatalogItemRequestPipeReader[] catalogItemRequestPipeReaders,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            /* validation */
            if (!catalogItemRequestPipeReaders.Any())
                return;

            foreach (var catalogItemRequestPipeReader in catalogItemRequestPipeReaders)
            {
                if (catalogItemRequestPipeReader.Request.Item.Representation.SamplePeriod != samplePeriod)
                    throw new ValidationException("All representations must be of the same sample period.");
            }

            DataWriterController.ValidateParameters(begin, samplePeriod, filePeriod);

            /* periods */
            var totalPeriod = end - begin;
            Logger.LogDebug("The total period is {TotalPeriod}", totalPeriod);

            var consumedPeriod = TimeSpan.Zero;
            var currentPeriod = default(TimeSpan);

            /* progress */
            var dataWriterProgress = new Progress<double>();

            /* no need to remove handler because of short lifetime of IDataWriter */
            dataWriterProgress.ProgressChanged += (sender, progressValue) =>
            {
                var baseProgress = consumedPeriod.Ticks / (double)totalPeriod.Ticks;
                var relativeProgressFactor = currentPeriod.Ticks / (double)totalPeriod.Ticks;
                var relativeProgress = progressValue * relativeProgressFactor;
                progress?.Report(baseProgress + relativeProgress);
            };

            /* catalog items */
            var catalogItems = catalogItemRequestPipeReaders
                .Select(catalogItemRequestPipeReader => catalogItemRequestPipeReader.Request.Item)
                .ToArray();

            /* go */
            var lastFileBegin = default(DateTime);

            await NexusUtilities.FileLoopAsync(begin, end, filePeriod,
                async (fileBegin, fileOffset, duration) =>
            {
                /* Concept: It never happens that the data of a read operation is spreaded over 
                 * multiple buffers. However, it may happen that the data of multiple read 
                 * operations are copied into a single buffer (important to ensure that multiple 
                 * bytes of a single value are always copied together). When the first buffer
                 * is (partially) read, call the "PipeReader.Advance" function to tell the pipe
                 * the number of bytes we have consumed. This way we slice our way through 
                 * the buffers so it is OK to only ever read the first buffer of a read result.
                 */

                cancellationToken.ThrowIfCancellationRequested();

                var currentBegin = fileBegin + fileOffset;
                Logger.LogTrace("Process period {CurrentBegin} to {CurrentEnd}", currentBegin, currentBegin + duration);

                /* close / open */
                if (fileBegin != lastFileBegin)
                {
                    /* close */
                    if (lastFileBegin != default)
                        await DataWriter.CloseAsync(cancellationToken);

                    /* open */
                    await DataWriter.OpenAsync(
                        fileBegin,
                        filePeriod,
                        samplePeriod,
                        catalogItems,
                        cancellationToken);
                }

                lastFileBegin = fileBegin;

                /* loop */
                var consumedFilePeriod = TimeSpan.Zero;
                var remainingPeriod = duration;

                while (remainingPeriod > TimeSpan.Zero)
                {
                    /* read */
                    var readResultTasks = catalogItemRequestPipeReaders
                        .Select(catalogItemRequestPipeReader => catalogItemRequestPipeReader.DataReader.ReadAsync(cancellationToken))
                        .ToArray();

                    var readResults = await NexusUtilities.WhenAll(readResultTasks);
                    var bufferPeriod = readResults.Min(readResult => readResult.Buffer.First.Cast<byte, double>().Length) * samplePeriod;

                    if (bufferPeriod == default)
                        throw new ValidationException("The pipe is empty.");

                    /* write */
                    currentPeriod = new TimeSpan(Math.Min(remainingPeriod.Ticks, bufferPeriod.Ticks));
                    var currentLength = (int)(currentPeriod.Ticks / samplePeriod.Ticks);

                    var requests = catalogItemRequestPipeReaders.Zip(readResults).Select(zipped =>
                    {
                        var (catalogItemRequestPipeReader, readResult) = zipped;

                        var request = catalogItemRequestPipeReader.Request;
                        var catalogItem = request.Item;

                        if (request.BaseItem is not null)
                        {
                            var originalResource = request.Item.Resource;

                            var newResource = new ResourceBuilder(originalResource.Id)
                                .WithProperty(DataModelExtensions.BasePathKey, request.BaseItem.ToPath())
                                .Build();

                            var augmentedResource = originalResource.Merge(newResource);

                            catalogItem = request.Item with
                            {
                                Resource = augmentedResource
                            };
                        }

                        var writeRequest = new WriteRequest(
                            catalogItem,
                            readResult.Buffer.First.Cast<byte, double>()[..currentLength]);

                        return writeRequest;
                    }).ToArray();

                    await DataWriter.WriteAsync(
                        fileOffset + consumedFilePeriod,
                        requests,
                        dataWriterProgress,
                        cancellationToken);

                    /* advance */
                    foreach (var ((_, dataReader), readResult) in catalogItemRequestPipeReaders.Zip(readResults))
                    {
                        dataReader.AdvanceTo(readResult.Buffer.GetPosition(currentLength * sizeof(double)));
                    }

                    /* update loop state */
                    consumedPeriod += currentPeriod;
                    consumedFilePeriod += currentPeriod;
                    remainingPeriod -= currentPeriod;

                    progress?.Report(consumedPeriod.Ticks / (double)totalPeriod.Ticks);
                }
            });

            /* close */
            await DataWriter.CloseAsync(cancellationToken);

            foreach (var (_, dataReader) in catalogItemRequestPipeReaders)
            {
                await dataReader.CompleteAsync();
            }
        }

        private static void ValidateParameters(
            DateTime begin,
            TimeSpan samplePeriod,
            TimeSpan filePeriod)
        {
            if (begin.Ticks % samplePeriod.Ticks != 0)
                throw new ValidationException("The begin parameter must be a multiple of the sample period.");

            if (filePeriod.Ticks % samplePeriod.Ticks != 0)
                throw new ValidationException("The file period parameter must be a multiple of the sample period.");
        }

        #endregion

        #region IDisposable

        private bool _disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    var disposable = DataWriter as IDisposable;
                    disposable?.Dispose();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}