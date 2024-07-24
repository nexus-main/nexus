// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Services;
using Nexus.Utilities;

namespace Nexus.Extensibility;

internal interface IDataSourceController : IDisposable
{
    Task InitializeAsync(
        ConcurrentDictionary<string, ResourceCatalog> catalogs,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken);

    Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(
        string path,
        CancellationToken cancellationToken);

    Task<ResourceCatalog> GetCatalogAsync(
        string catalogId,
        CancellationToken cancellationToken);

    Task<CatalogAvailability> GetAvailabilityAsync(
        string catalogId,
        DateTime begin,
        DateTime end,
        TimeSpan step,
        CancellationToken cancellationToken);

    Task<CatalogTimeRange> GetTimeRangeAsync(
        string catalogId,
        CancellationToken cancellationToken);

    Task ReadAsync(
        DateTime begin,
        DateTime end,
        TimeSpan samplePeriod,
        CatalogItemRequestPipeWriter[] catalogItemRequestPipeWriters,
        ReadDataHandler readDataHandler,
        IProgress<double> progress,
        CancellationToken cancellationToken);
}

internal class DataSourceController(
    IDataSource[] dataSources,
    DataSourceRegistration[] registrations,
    IReadOnlyDictionary<string, JsonElement>? systemConfiguration,
    IReadOnlyDictionary<string, JsonElement>? requestConfiguration,
    IProcessingService processingService,
    ICacheService cacheService,
    DataOptions dataOptions,
    ILogger<DataSourceController> logger) : IDataSourceController
{
    private static readonly string[] _pipelinePositionPath = [DataModelExtensions.NEXUS_KEY, DataModelExtensions.PIPELINE_POSITION_KEY];

    internal readonly IReadOnlyDictionary<string, JsonElement>? _requestConfiguration = requestConfiguration;

    private readonly IProcessingService _processingService = processingService;

    private readonly ICacheService _cacheService = cacheService;

    private readonly DataOptions _dataOptions = dataOptions;

    private readonly IDataSource[] _dataSources = dataSources;

    private readonly DataSourceRegistration[] _registrations = registrations;

    private readonly IReadOnlyDictionary<string, JsonElement>? _systemConfiguration = systemConfiguration;

    private ConcurrentDictionary<string, ResourceCatalog> _catalogCache = default!;

    private readonly ILogger _logger = logger;

    public async Task InitializeAsync(
        ConcurrentDictionary<string, ResourceCatalog> catalogCache,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        _catalogCache = catalogCache;

        foreach (var (dataSource, registration) in _dataSources
            .Zip(_registrations))
        {
            var dataSourceLogger = loggerFactory
                .CreateLogger($"{registration.Type} - {registration.ResourceLocator?.ToString() ?? "<null>"}");

            var clonedSourceConfiguration = registration.Configuration is null
                ? default
                : registration.Configuration.ToDictionary(entry => entry.Key, entry => entry.Value.Clone());

            var context = new DataSourceContext(
                ResourceLocator: registration.ResourceLocator,
                SystemConfiguration: _systemConfiguration,
                SourceConfiguration: clonedSourceConfiguration,
                RequestConfiguration: _requestConfiguration);

            await dataSource.SetContextAsync(context, dataSourceLogger, cancellationToken);
        }
    }

    public async Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(
        string path,
        CancellationToken cancellationToken)
    {
        // collect all catalog registrations
        var catalogRegistrations = new List<CatalogRegistration>();

        foreach (var dataSource in _dataSources)
        {
            var newCatalogRegistrations = await dataSource.GetCatalogRegistrationsAsync(path, cancellationToken);
            catalogRegistrations.AddRange(newCatalogRegistrations);
        }

        // validation and sanitization
        for (int i = 0; i < catalogRegistrations.Count; i++)
        {
            // absolute
            if (catalogRegistrations[i].Path.StartsWith('/'))
            {
                if (!catalogRegistrations[i].Path.StartsWith(path))
                    throw new Exception($"The catalog path {catalogRegistrations[i].Path} is not a sub path of {path}.");
            }

            // relative
            else
            {
                catalogRegistrations[i] = catalogRegistrations[i] with
                {
                    Path = path + catalogRegistrations[i].Path
                };
            }
        }

        if (catalogRegistrations.Any(catalogRegistration => !catalogRegistration.Path.StartsWith(path)))
            throw new Exception($"The returned catalog identifier is not a child of {path}.");

        return catalogRegistrations
            .DistinctBy(catalogRegistration => catalogRegistration.Path)
            .ToArray();
    }

    public async Task<ResourceCatalog> GetCatalogAsync(
        string catalogId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Load catalog {CatalogId}", catalogId);

        var catalog = new ResourceCatalog(catalogId);

        for (var pipelinePosition = 0; pipelinePosition < _dataSources.Length; pipelinePosition++)
        {
            catalog = await _dataSources[pipelinePosition].EnrichCatalogAsync(catalog, cancellationToken);

            // TODO: Is it the best solution to inject these additional properties here? Similar code exists in SourcesController.GetExtensionDescriptions()
            catalog = catalog.EnsureAndSanitizeMandatoryProperties(pipelinePosition, _dataSources);
        }

        if (catalog.Id != catalogId)
            throw new Exception("The id of the returned catalog does not match the requested catalog id.");

        catalog = catalog with
        {
            Resources = catalog.Resources?.OrderBy(resource => resource.Id).ToList()
        };

        /* GetOrAdd is not working because it requires a synchronous delegate */
        _catalogCache.TryAdd(catalogId, catalog);

        return catalog;
    }

    public async Task<CatalogAvailability> GetAvailabilityAsync(
        string catalogId,
        DateTime begin,
        DateTime end,
        TimeSpan step,
        CancellationToken cancellationToken)
    {
        var stepCount = (int)Math.Ceiling((end - begin).Ticks / (double)step.Ticks);
        var availabilities = new double[stepCount, _dataSources.Length];

        for (int dataSourceIndex = 0; dataSourceIndex < _dataSources.Length; dataSourceIndex++)
        {
            var tasks = new List<Task>(capacity: stepCount);
            var currentBegin = begin;
            var dataSource = _dataSources[dataSourceIndex];

            for (int i = 0; i < stepCount; i++)
            {
                var currentEnd = currentBegin + step;
                var currentBegin_captured = currentBegin;
                var i_captured = i;

                tasks.Add(Task.Run(async () =>
                {
                    var availability = await dataSource.GetAvailabilityAsync(catalogId, currentBegin_captured, currentEnd, cancellationToken);
                    availabilities[i_captured, dataSourceIndex] = availability;
                }, cancellationToken));

                currentBegin = currentEnd;
            }

            await Task.WhenAll(tasks);

        }

        // calculate average (but ignore double.NaN values)
        var averagedAvailabilities = new double[stepCount];

        for (int i = 0; i < averagedAvailabilities.Length; i++)
        {
            var sum = double.NaN;
            var count = 0;

            for (int j = 0; j < _dataSources.Length; j++)
            {
                var value = availabilities[i, j];

                sum = (sum, value) switch
                {
                    /* NaNs everywhere */
                    (double.NaN, double.NaN) => double.NaN,

                    /* Replace sum with value */
                    (double.NaN, _) => value,

                    /* Do not change sum */
                    (_, double.NaN) => sum,

                    /* Add value to sum */
                    _ => sum + value
                };

                if (!double.IsNaN(value))
                    count++;
            }

            averagedAvailabilities[i] = count == 0
                ? sum
                : sum / count;
        }

        return new CatalogAvailability(Data: averagedAvailabilities);
    }

    public async Task<CatalogTimeRange> GetTimeRangeAsync(
        string catalogId,
        CancellationToken cancellationToken)
    {
        var begin = DateTime.MaxValue;
        var end = DateTime.MinValue;

        foreach (var dataSource in _dataSources)
        {
            (var currentBegin, var currentEnd) = await dataSource
                .GetTimeRangeAsync(catalogId, cancellationToken);

            if (currentBegin < begin)
                begin = currentBegin;

            if (currentEnd > end)
                end = currentEnd;
        }

        return new CatalogTimeRange(
            Begin: begin,
            End: end);
    }

    public async Task ReadAsync(
        DateTime begin,
        DateTime end,
        TimeSpan samplePeriod,
        CatalogItemRequestPipeWriter[] catalogItemRequestPipeWriters,
        ReadDataHandler readDataHandler,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        /* This method reads data from the data source or from the cache and optionally
         * processes the data (aggregation, resampling).
         *
         * Normally, all data would be loaded at once using a single call to
         * DataSource.ReadAsync(). But with caching involved, it is not uncommon
         * to have only parts of the requested data available in cache. The rest needs to
         * be loaded and processed as usual. This leads to fragmented read periods and thus
         * often more than a single call to DataSource.ReadAsync() is necessary.
         *
         * However, during the first request the cache is filled and subsequent identical
         * requests will from now on be served from the cache only.
         */

        /* preparation */
        var readUnits = PrepareReadUnits(catalogItemRequestPipeWriters);
        var readingTasks = new List<Task>(capacity: readUnits.Length);
        var targetElementCount = ExtensibilityUtilities.CalculateElementCount(begin, end, samplePeriod);
        var targetByteCount = sizeof(double) * targetElementCount;

        // TODO: access to totalProgress (see below) is not thread safe
        var totalProgress = 0.0;

        /* 'Original' branch
            *  - Read data into readUnit.ReadRequest (rented buffer)
            *  - Merge data / status and copy result into readUnit.DataWriter
            */
        var originalReadUnits = readUnits
            .Where(readUnit => readUnit.CatalogItemRequest.BaseItem is null)
            .ToArray();

        _logger.LogTrace("Load {RepresentationCount} original representations", originalReadUnits.Length);

        var originalProgress = new Progress<double>();
        var originalProgressFactor = originalReadUnits.Length / (double)readUnits.Length;
        var originalProgressValue_old = 0.0;

        originalProgress.ProgressChanged += (sender, originalProgressValue) =>
        {
            var actualProgress = originalProgressValue - originalProgressValue_old;
            originalProgressValue_old = originalProgressValue;
            totalProgress += actualProgress * originalProgressFactor;
            progress.Report(totalProgress);
        };

        var originalTask = ReadOriginalAsync(
            begin,
            end,
            originalReadUnits,
            readDataHandler,
            targetElementCount,
            targetByteCount,
            originalProgress,
            cancellationToken);

        readingTasks.Add(originalTask);

        /* 'Processing' branch
            *  - Read cached data into readUnit.DataWriter
            *  - Read remaining data into readUnit.ReadRequest
            *  - Process readUnit.ReadRequest data and copy result into readUnit.DataWriter
            */
        var processingReadUnits = readUnits
            .Where(readUnit => readUnit.CatalogItemRequest.BaseItem is not null)
            .ToArray();

        _logger.LogTrace("Load {RepresentationCount} processing representations", processingReadUnits.Length);

        var processingProgressFactor = 1 / (double)readUnits.Length;

        foreach (var processingReadUnit in processingReadUnits)
        {
            var processingProgress = new Progress<double>();
            var processingProgressValue_old = 0.0;

            processingProgress.ProgressChanged += (sender, progressValue) =>
            {
                var actualProgress = progressValue - processingProgressValue_old;
                processingProgressValue_old = progressValue;
                totalProgress += actualProgress * processingProgressFactor;
                progress.Report(totalProgress);
            };

            var kind = processingReadUnit.CatalogItemRequest.Item.Representation.Kind;

            var processingTask = kind == RepresentationKind.Resampled

                ? ReadResampledAsync(
                    begin,
                    end,
                    processingReadUnit,
                    readDataHandler,
                    targetByteCount,
                    processingProgress,
                    cancellationToken)

                : ReadAggregatedAsync(
                    begin,
                    end,
                    processingReadUnit,
                    readDataHandler,
                    targetByteCount,
                    processingProgress,
                    cancellationToken);

            readingTasks.Add(processingTask);
        }

        /* wait for tasks to finish */
        await NexusUtilities.WhenAllFailFastAsync(readingTasks, cancellationToken);
    }

    private async Task ReadOriginalAsync(
        DateTime begin,
        DateTime end,
        ReadUnit[] originalUnits,
        ReadDataHandler readDataHandler,
        int targetElementCount,
        int targetByteCount,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        var tuples = originalUnits
            .Select(readUnit => (readUnit, new ReadRequestManager(readUnit.CatalogItemRequest.Item, targetElementCount)))
            .ToArray();

        try
        {
            var readRequests = tuples
                .Select(manager => manager.Item2.Request)
                .ToArray();

            try
            {
                var pipelinePosition = 0;

                foreach (var dataSource in _dataSources)
                {
                    var currentReadRequests = readRequests
                        .Where(request =>
                            request.CatalogItem.Resource.Properties is null ||
                            request.CatalogItem.Resource.Properties.GetIntValue(_pipelinePositionPath) == pipelinePosition
                        )
                        .ToArray();

                    await dataSource.ReadAsync(
                        begin,
                        end,
                        currentReadRequests,
                        readDataHandler,
                        progress,
                        cancellationToken);

                    pipelinePosition++;
                }
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Read original data period {Begin} to {End} failed", begin, end);
            }

            var readingTasks = new List<Task>(capacity: originalUnits.Length);

            foreach (var (readUnit, readRequestManager) in tuples)
            {
                var (catalogItemRequest, dataWriter) = readUnit;
                var (_, _, data, status) = readRequestManager.Request;

                using var scope = _logger.BeginScope(new Dictionary<string, object>()
                {
                    ["ResourcePath"] = catalogItemRequest.Item.ToPath()
                });

                cancellationToken.ThrowIfCancellationRequested();

                var buffer = dataWriter
                    .GetMemory(targetByteCount)[..targetByteCount];

                var targetBuffer = new CastMemoryManager<byte, double>(buffer).Memory;

                readingTasks.Add(Task.Run(async () =>
                {
                    BufferUtilities.ApplyRepresentationStatusByDataType(
                        catalogItemRequest.Item.Representation.DataType,
                        data,
                        status,
                        target: targetBuffer);

                    /* update progress */
                    _logger.LogTrace("Advance data pipe writer by {DataLength} bytes", targetByteCount);
                    dataWriter.Advance(targetByteCount);
                    await dataWriter.FlushAsync();
                }, cancellationToken));
            }

            /* wait for tasks to finish */
            await NexusUtilities.WhenAllFailFastAsync(readingTasks, cancellationToken);
        }
        finally
        {
            foreach (var (readUnit, readRequestManager) in tuples)
            {
                readRequestManager.Dispose();
            }
        }
    }

    private async Task ReadAggregatedAsync(
       DateTime begin,
       DateTime end,
       ReadUnit readUnit,
       ReadDataHandler readDataHandler,
       int targetByteCount,
       IProgress<double> progress,
       CancellationToken cancellationToken)
    {
        var item = readUnit.CatalogItemRequest.Item;
        var baseItem = readUnit.CatalogItemRequest.BaseItem!;
        var samplePeriod = item.Representation.SamplePeriod;
        var baseSamplePeriod = baseItem.Representation.SamplePeriod;

        /* target buffer */
        var buffer = readUnit.DataWriter
           .GetMemory(targetByteCount)[..targetByteCount];

        var targetBuffer = new CastMemoryManager<byte, double>(buffer).Memory;

        /* read request */
        var readElementCount = ExtensibilityUtilities.CalculateElementCount(begin, end, baseSamplePeriod);

        using var readRequestManager = new ReadRequestManager(baseItem, readElementCount);
        var readRequest = readRequestManager.Request;

        /* go */
        try
        {
            /* load data from cache */
            _logger.LogTrace("Load data from cache");

            List<Interval> uncachedIntervals;

            var disableCache = _dataOptions.CachePattern is not null && !Regex.IsMatch(readUnit.CatalogItemRequest.Item.Catalog.Id, _dataOptions.CachePattern);

            if (disableCache)
            {
                uncachedIntervals = [new Interval(begin, end)];
            }

            else
            {
                uncachedIntervals = await _cacheService.ReadAsync(
                    item,
                    begin,
                    targetBuffer,
                    cancellationToken);
            }

            /* load and process remaining data from source */
            _logger.LogTrace("Load and process {PeriodCount} uncached periods from source", uncachedIntervals.Count);

            var elementSize = baseItem.Representation.ElementSize;
            var sourceSamplePeriod = baseSamplePeriod;
            var targetSamplePeriod = samplePeriod;

            var blockSize = item.Representation.Kind == RepresentationKind.Resampled
                ? (int)(sourceSamplePeriod.Ticks / targetSamplePeriod.Ticks)
                : (int)(targetSamplePeriod.Ticks / sourceSamplePeriod.Ticks);

            foreach (var interval in uncachedIntervals)
            {
                var offset = interval.Begin - begin;
                var length = interval.End - interval.Begin;

                var slicedReadRequest = readRequest with
                {
                    Data = readRequest.Data.Slice(
                        start: NexusUtilities.Scale(offset, sourceSamplePeriod) * elementSize,
                        length: NexusUtilities.Scale(length, sourceSamplePeriod) * elementSize),

                    Status = readRequest.Status.Slice(
                        start: NexusUtilities.Scale(offset, sourceSamplePeriod),
                        length: NexusUtilities.Scale(length, sourceSamplePeriod)),
                };

                /* read */
                foreach (var dataSource in _dataSources)
                {
                    await dataSource.ReadAsync(
                        interval.Begin,
                        interval.End,
                        [slicedReadRequest],
                        readDataHandler,
                        progress,
                        cancellationToken);
                }

                /* process */
                var slicedTargetBuffer = targetBuffer.Slice(
                    start: NexusUtilities.Scale(offset, targetSamplePeriod),
                    length: NexusUtilities.Scale(length, targetSamplePeriod));

                _processingService.Aggregate(
                    baseItem.Representation.DataType,
                    item.Representation.Kind,
                    slicedReadRequest.Data,
                    slicedReadRequest.Status,
                    targetBuffer: slicedTargetBuffer,
                    blockSize);
            }

            /* update cache */
            if (!disableCache)
            {
                await _cacheService.UpdateAsync(
                    item,
                    begin,
                    targetBuffer,
                    uncachedIntervals,
                    cancellationToken);
            }
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Read aggregation data period {Begin} to {End} failed", begin, end);
        }
        finally
        {
            /* update progress */
            _logger.LogTrace("Advance data pipe writer by {DataLength} bytes", targetByteCount);
            readUnit.DataWriter.Advance(targetByteCount);
            await readUnit.DataWriter.FlushAsync(cancellationToken);
        }
    }

    private async Task ReadResampledAsync(
       DateTime begin,
       DateTime end,
       ReadUnit readUnit,
       ReadDataHandler readDataHandler,
       int targetByteCount,
       IProgress<double> progress,
       CancellationToken cancellationToken)
    {
        var item = readUnit.CatalogItemRequest.Item;
        var baseItem = readUnit.CatalogItemRequest.BaseItem!;
        var samplePeriod = item.Representation.SamplePeriod;
        var baseSamplePeriod = baseItem.Representation.SamplePeriod;

        /* target buffer */
        var buffer = readUnit.DataWriter
           .GetMemory(targetByteCount)[..targetByteCount];

        var targetBuffer = new CastMemoryManager<byte, double>(buffer).Memory;

        /* Calculate rounded begin and end values.
         *
         * Example:
         *
         * - sample period = 1 s
         * - extract data from 00:00:00.200 to 00:00:01:700 @ sample period = 100 ms
         *
         *  _    ___ <- roundedBegin
         * |      |
         * | 1 s  x  <- offset: 200 ms
         * |      |
         * |_    ___ <- roundedEnd
         * |      |
         * | 1 s  x  <- end: length: 1500 ms
         * |      |
         * |_    ___
         *
         * roundedBegin = 00:00:00
         * roundedEnd   = 00:00:02
         * offset       =  200 ms ==  2 elements
         * length       = 1500 ms == 15 elements
         */

        var roundedBegin = begin.RoundDown(baseSamplePeriod);
        var roundedEnd = end.RoundUp(baseSamplePeriod);
        var roundedElementCount = ExtensibilityUtilities.CalculateElementCount(roundedBegin, roundedEnd, baseSamplePeriod);

        /* read request */
        using var readRequestManager = new ReadRequestManager(baseItem, roundedElementCount);
        var readRequest = readRequestManager.Request;

        /* go */
        try
        {
            /* load and process data from source */
            var elementSize = baseItem.Representation.ElementSize;
            var sourceSamplePeriod = baseSamplePeriod;
            var targetSamplePeriod = samplePeriod;

            var blockSize = item.Representation.Kind == RepresentationKind.Resampled
                ? (int)(sourceSamplePeriod.Ticks / targetSamplePeriod.Ticks)
                : (int)(targetSamplePeriod.Ticks / sourceSamplePeriod.Ticks);

            /* read */
            foreach (var dataSource in _dataSources)
            {
                await dataSource.ReadAsync(
                    roundedBegin,
                    roundedEnd,
                    [readRequest],
                    readDataHandler,
                    progress,
                    cancellationToken);
            }

            /* process */
            var offset = NexusUtilities.Scale(begin - roundedBegin, targetSamplePeriod);

            _processingService.Resample(
                baseItem.Representation.DataType,
                readRequest.Data,
                readRequest.Status,
                targetBuffer,
                blockSize,
                offset);
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Read resampling data period {Begin} to {End} failed", roundedBegin, roundedEnd);
        }

        /* update progress */
        _logger.LogTrace("Advance data pipe writer by {DataLength} bytes", targetByteCount);
        readUnit.DataWriter.Advance(targetByteCount);
        await readUnit.DataWriter.FlushAsync(cancellationToken);
    }

    private ReadUnit[] PrepareReadUnits(
        CatalogItemRequestPipeWriter[] catalogItemRequestPipeWriters)
    {
        var readUnits = new List<ReadUnit>();

        foreach (var catalogItemRequestPipeWriter in catalogItemRequestPipeWriters)
        {
            var (catalogItemRequest, dataWriter) = catalogItemRequestPipeWriter;

            var item = catalogItemRequest.BaseItem is null
                ? catalogItemRequest.Item
                : catalogItemRequest.BaseItem;

            /* _catalogMap is guaranteed to contain the current catalog
             * because GetCatalogAsync is called before ReadAsync */
            if (_catalogCache.TryGetValue(item.Catalog.Id, out var catalog))
            {
                var readUnit = new ReadUnit(catalogItemRequest, dataWriter);
                readUnits.Add(readUnit);
            }

            else
            {
                throw new Exception($"Cannot find catalog {item.Catalog.Id}.");
            }
        }

        return [.. readUnits];
    }

    public static async Task ReadAsync(
        DateTime begin,
        DateTime end,
        TimeSpan samplePeriod,
        DataReadingGroup[] readingGroups,
        ReadDataHandler readDataHandler,
        IMemoryTracker memoryTracker,
        IProgress<double>? progress,
        ILogger<DataSourceController> logger,
        CancellationToken cancellationToken)
    {
        /* validation */
        ValidateParameters(begin, end, samplePeriod);

        var catalogItemRequestPipeWriters = readingGroups.SelectMany(readingGroup => readingGroup.CatalogItemRequestPipeWriters);

        if (!catalogItemRequestPipeWriters.Any())
            return;

        foreach (var catalogItemRequestPipeWriter in catalogItemRequestPipeWriters)
        {
            /* All frequencies are required to be multiples of each other, namely these are:
             *
             * - begin
             * - end
             * - item -> representation -> sample period
             * - base item -> representation -> sample period
             *
             * This makes aggregation and caching much easier.
             */

            var request = catalogItemRequestPipeWriter.Request;
            var itemSamplePeriod = request.Item.Representation.SamplePeriod;

            if (itemSamplePeriod != samplePeriod)
                throw new ValidationException("All representations must be based on the same sample period.");

            if (request.BaseItem is not null)
            {
                var baseItemSamplePeriod = request.BaseItem.Representation.SamplePeriod;

                // resampling is only possible if base sample period < sample period
                if (request.Item.Representation.Kind == RepresentationKind.Resampled)
                {
                    if (baseItemSamplePeriod < samplePeriod)
                        throw new ValidationException("Unable to resample data if the base sample period is <= the sample period.");

                    if (baseItemSamplePeriod.Ticks % itemSamplePeriod.Ticks != 0)
                        throw new ValidationException("For resampling, the base sample period must be a multiple of the sample period.");
                }

                // aggregation is only possible if sample period > base sample period
                else
                {
                    if (samplePeriod < baseItemSamplePeriod)
                        throw new ValidationException("Unable to aggregate data if the sample period is <= the base sample period.");

                    if (itemSamplePeriod.Ticks % baseItemSamplePeriod.Ticks != 0)
                        throw new ValidationException("For aggregation, the sample period must be a multiple of the base sample period.");
                }
            }
        }

        /* total period */
        var totalPeriod = end - begin;
        logger.LogTrace("The total period is {TotalPeriod}", totalPeriod);

        /* bytes per row */

        // If the user requests /xxx/10_min_mean#base=10_ms, then the algorithm below will assume a period
        // of 10 minutes and a sample period of 10 ms, which leads to an estimated row size of 8 * 60000 = 480000 bytes.
        // The algorithm works this way because it cannot know if the data are already cached. It also does not know
        // if the data source will request more data which further increases the memory consumption.

        var bytesPerRow = 0L;
        var largestSamplePeriod = samplePeriod;

        foreach (var catalogItemRequestPipeWriter in catalogItemRequestPipeWriters)
        {
            var request = catalogItemRequestPipeWriter.Request;

            var elementSize = request.Item.Representation.ElementSize;
            var elementCount = 1L;

            if (request.BaseItem is not null)
            {
                var baseItemSamplePeriod = request.BaseItem.Representation.SamplePeriod;
                var itemSamplePeriod = request.Item.Representation.SamplePeriod;

                if (request.Item.Representation.Kind == RepresentationKind.Resampled)
                {
                    if (largestSamplePeriod < baseItemSamplePeriod)
                        largestSamplePeriod = baseItemSamplePeriod;
                }

                else
                {
                    elementCount =
                        itemSamplePeriod.Ticks /
                        baseItemSamplePeriod.Ticks;
                }
            }

            bytesPerRow += Math.Max(1, elementCount) * elementSize;
        }

        logger.LogTrace("A single row has a size of {BytesPerRow} bytes", bytesPerRow);

        /* total memory consumption */
        var totalRowCount = totalPeriod.Ticks / samplePeriod.Ticks;
        var totalByteCount = totalRowCount * bytesPerRow;

        /* actual memory consumption / chunk size */
        var allocationRegistration = await memoryTracker.RegisterAllocationAsync(
            minimumByteCount: bytesPerRow, maximumByteCount: totalByteCount, cancellationToken);

        /* go */
        var chunkSize = allocationRegistration.ActualByteCount;
        logger.LogTrace("The chunk size is {ChunkSize} bytes", chunkSize);

        var rowCount = chunkSize / bytesPerRow;
        logger.LogTrace("{RowCount} rows can be processed per chunk", rowCount);

        var maxPeriodPerRequest = TimeSpan
            .FromTicks(samplePeriod.Ticks * rowCount)
            .RoundDown(largestSamplePeriod);

        if (maxPeriodPerRequest == TimeSpan.Zero)
            throw new ValidationException("Unable to load the requested data because the available chunk size is too low.");

        logger.LogTrace("The maximum period per request is {MaxPeriodPerRequest}", maxPeriodPerRequest);

        try
        {
            await ReadCoreAsync(
                begin,
                totalPeriod,
                maxPeriodPerRequest,
                samplePeriod,
                readingGroups,
                readDataHandler,
                progress,
                logger,
                cancellationToken);
        }
        finally
        {
            allocationRegistration.Dispose();
        }
    }

    private static Task ReadCoreAsync(
        DateTime begin,
        TimeSpan totalPeriod,
        TimeSpan maxPeriodPerRequest,
        TimeSpan samplePeriod,
        DataReadingGroup[] readingGroups,
        ReadDataHandler readDataHandler,
        IProgress<double>? progress,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        /* periods */
        var consumedPeriod = TimeSpan.Zero;
        var remainingPeriod = totalPeriod;
        var currentPeriod = default(TimeSpan);

        /* progress */
        var currentDataSourceProgress = new ConcurrentDictionary<IDataSourceController, double>();

        return Task.Run(async () =>
        {
            while (consumedPeriod < totalPeriod)
            {
                cancellationToken.ThrowIfCancellationRequested();

                currentDataSourceProgress.Clear();
                currentPeriod = TimeSpan.FromTicks(Math.Min(remainingPeriod.Ticks, maxPeriodPerRequest.Ticks));

                var currentBegin = begin + consumedPeriod;
                var currentEnd = currentBegin + currentPeriod;

                logger.LogTrace("Process period {CurrentBegin} to {CurrentEnd}", currentBegin, currentEnd);

                var readingTasks = readingGroups.Select(async readingGroup =>
                {
                    var (controller, catalogItemRequestPipeWriters) = readingGroup;

                    try
                    {
                        /* no need to remove handler because of short lifetime of IDataSource */
                        var dataSourceProgress = new Progress<double>();

                        dataSourceProgress.ProgressChanged += (sender, progressValue) =>
                        {
                            if (progressValue <= 1)
                            {
                                // https://stackoverflow.com/a/62768272 (currentDataSourceProgress)
                                currentDataSourceProgress.AddOrUpdate(controller, progressValue, (_, _) => progressValue);

                                var baseProgress = consumedPeriod.Ticks / (double)totalPeriod.Ticks;
                                var relativeProgressFactor = currentPeriod.Ticks / (double)totalPeriod.Ticks;
                                var relativeProgress = currentDataSourceProgress.Sum(entry => entry.Value) * relativeProgressFactor;

                                progress?.Report(baseProgress + relativeProgress);
                            }
                        };

                        await controller.ReadAsync(
                            currentBegin,
                            currentEnd,
                            samplePeriod,
                            catalogItemRequestPipeWriters,
                            readDataHandler,
                            dataSourceProgress,
                            cancellationToken);
                    }
                    catch (OutOfMemoryException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Process period {Begin} to {End} failed", currentBegin, currentEnd);
                    }
                }).ToList();

                await NexusUtilities.WhenAllFailFastAsync(readingTasks, cancellationToken);

                /* continue in time */
                consumedPeriod += currentPeriod;
                remainingPeriod -= currentPeriod;

                progress?.Report(consumedPeriod.Ticks / (double)totalPeriod.Ticks);
            }

            /* complete */
            foreach (var readingGroup in readingGroups)
            {
                foreach (var catalogItemRequestPipeWriter in readingGroup.CatalogItemRequestPipeWriters)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await catalogItemRequestPipeWriter.DataWriter.CompleteAsync();
                }
            }
        }, cancellationToken);
    }

    private static void ValidateParameters(
        DateTime begin,
        DateTime end,
        TimeSpan samplePeriod)
    {
        /* When the user requests two time series of the same frequency, they will be aligned to the sample
         * period. With the current implementation, it simply not possible for one data source to provide an
         * offset which is smaller than the sample period. In future a solution could be to have time series
         * data with associated time stamps, which is not yet implemented.
         */

        /* Examples
         *
         *   OK: from 2020-01-01 00:00:01.000 to 2020-01-01 00:00:03.000 @ 1 s
         *
         * FAIL: from 2020-01-01 00:00:00.000 to 2020-01-02 00:00:00.000 @ 130 ms
         *   OK: from 2020-01-01 00:00:00.050 to 2020-01-02 00:00:00.000 @ 130 ms
         *
         */


        if (begin >= end)
            throw new ValidationException("The begin datetime must be less than the end datetime.");

        if (begin.Ticks % samplePeriod.Ticks != 0)
            throw new ValidationException("The begin parameter must be a multiple of the sample period.");

        if (end.Ticks % samplePeriod.Ticks != 0)
            throw new ValidationException("The end parameter must be a multiple of the sample period.");
    }

    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                foreach (var dataSource in _dataSources)
                {
                    var disposable = dataSource as IDisposable;
                    disposable?.Dispose();
                }
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
