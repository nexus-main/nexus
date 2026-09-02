// MIT License
// Copyright (c) [2024] [nexus-main]

using System.ComponentModel.DataAnnotations;
using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Security.Claims;
using Nexus.Core;
using Nexus.Core.V1;
using Nexus.Core.V2;
using Nexus.Extensibility;
using Nexus.Utilities;

namespace Nexus.Services;

internal interface IDataService
{
    Progress<double> ReadProgress { get; }
    Progress<double> WriteProgress { get; }

    Task<Stream> ReadAsStreamAsync(
       string resourcePath,
       DateTime begin,
       DateTime end,
       CancellationToken cancellationToken);

    Task<Stream> ReadBatchAsStreamAsync(
       BatchStreamRequest request,
       CancellationToken cancellationToken);

    Task ReadAsDoubleAsync(
       string resourcePath,
       DateTime begin,
       DateTime end,
       Memory<double> buffer,
       CancellationToken cancellationToken);

    Task<string> ExportAsync(
        Guid exportId,
        IEnumerable<CatalogItemRequest> catalogItemRequests,
        ReadDataHandler readDataHandler,
        ExportParameters exportParameters,
        CancellationToken cancellationToken);
}

internal class DataService(
    AppState appState,
    ClaimsPrincipal user,
    IDataControllerService dataControllerService,
    IDatabaseService databaseService,
    IMemoryTracker memoryTracker,
    ILogger<DataService> logger,
    ILoggerFactory loggerFactory
) : IDataService
{
    private readonly AppState _appState = appState;
    private readonly IMemoryTracker _memoryTracker = memoryTracker;
    private readonly ClaimsPrincipal _user = user;
    private readonly ILogger _logger = logger;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private readonly IDatabaseService _databaseService = databaseService;
    private readonly IDataControllerService _dataControllerService = dataControllerService;

    public Progress<double> ReadProgress { get; } = new Progress<double>();

    public Progress<double> WriteProgress { get; } = new Progress<double>();

    public async Task<Stream> ReadAsStreamAsync(
       string resourcePath,
       DateTime begin,
       DateTime end,
       CancellationToken cancellationToken
    )
    {
        begin = DateTime.SpecifyKind(begin, DateTimeKind.Utc);
        end = DateTime.SpecifyKind(end, DateTimeKind.Utc);

        // find representation
        var root = _appState.CatalogState.Root;

        var catalogItemRequest = await root.TryFindAsync(root, resourcePath, cancellationToken)
            ?? throw new Exception($"Could not find resource path {resourcePath}.");

        var catalogContainer = catalogItemRequest.Container;

        // security check
        if (!AuthUtilities.IsCatalogReadable(catalogContainer.Id, catalogContainer.Metadata, catalogContainer.Owner, _user))
            throw new Exception($"The current user is not permitted to access the catalog {catalogContainer.Id}.");

        // controller

        /* IMPORTANT: controller cannot be disposed here because it needs to
         * stay alive until the stream has finished. Therefore it will be dipose
         * in the DataSourceControllerExtensions.ReadAsStream method which monitors that.
         */
        var controller = await _dataControllerService.GetDataSourceControllerAsync(
            catalogContainer.Pipeline,
            cancellationToken);

        // read data
        var stream = controller.ReadAsStream(
            begin,
            end,
            catalogItemRequest,
            readDataHandler: ReadAsDoubleAsync,
            _memoryTracker,
            _loggerFactory.CreateLogger<DataSourceController>(),
            cancellationToken);

        return stream;
    }

    public async Task<Stream> ReadBatchAsStreamAsync(
       BatchStreamRequest request,
       CancellationToken cancellationToken)
    {
        var begin = DateTime.SpecifyKind(request.Begin, DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(request.End, DateTimeKind.Utc);

        ValidateResourcePaths(request.ResourcePaths);

        var root = _appState.CatalogState.Root;
        var catalogItemRequests = new List<(int Index, string ResourcePath, CatalogItemRequest Request)>(request.ResourcePaths.Length);

        for (var index = 0; index < request.ResourcePaths.Length; index++)
        {
            var resourcePath = request.ResourcePaths[index];
            var catalogItemRequest = await root.TryFindAsync(root, resourcePath, cancellationToken)
                ?? throw new Exception($"Could not find resource path {resourcePath}.");

            var catalogContainer = catalogItemRequest.Container;

            if (!AuthUtilities.IsCatalogReadable(catalogContainer.Id, catalogContainer.Metadata, catalogContainer.Owner, _user))
                throw new Exception($"The current user is not permitted to access the catalog {catalogContainer.Id}.");

            catalogItemRequests.Add((index, resourcePath, catalogItemRequest));
        }

        var samplePeriods = catalogItemRequests
            .Select(catalogItemRequest => catalogItemRequest.Request.Item.Representation.SamplePeriod)
            .Distinct()
            .ToList();

        if (samplePeriods.Count != 1)
            throw new ValidationException("All representations must be of the same sample period.");

        var samplePeriod = samplePeriods.First();
        DataSourceController.ValidateParameters(begin, end, samplePeriod);

        var outputPipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: 1024 * 1024,
            resumeWriterThreshold: 512 * 1024));
        var dataReaders = new List<(int Index, PipeReader Reader)>();
        var readingGroups = new List<DataReadingGroup>();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            foreach (var group in catalogItemRequests.GroupBy(current => current.Request.Container))
            {
                var controller = await _dataControllerService.GetDataSourceControllerAsync(group.Key.Pipeline, cancellationToken);
                var catalogItemRequestPipeWriters = new List<CatalogItemRequestPipeWriter>();

                try
                {
                    foreach (var (index, _, catalogItemRequest) in group)
                    {
                        var pipe = new Pipe();

                        catalogItemRequestPipeWriters.Add(new CatalogItemRequestPipeWriter(catalogItemRequest, pipe.Writer));
                        dataReaders.Add((index, pipe.Reader));
                    }

                    readingGroups.Add(new DataReadingGroup(controller, catalogItemRequestPipeWriters.ToArray()));
                }
                catch
                {
                    try
                    {
                        controller.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Disposing a failed batch data controller failed");
                    }

                    foreach (var writer in catalogItemRequestPipeWriters)
                    {
                        try
                        {
                            await writer.DataWriter.CompleteAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Completing a failed batch data writer failed");
                        }
                    }

                    throw;
                }
            }

            var reading = DataSourceController.ReadAsync(
                begin,
                end,
                samplePeriod,
                readingGroups.ToArray(),
                ReadAsDoubleAsync,
                _memoryTracker,
                ReadProgress,
                _loggerFactory.CreateLogger<DataSourceController>(),
                cts.Token,
                DataSourceErrorHandling.Propagate);
            var writeGate = new SemaphoreSlim(1, 1);
            var pumping = dataReaders
                .Select(current => PumpAsync(current.Index, current.Reader, outputPipe.Writer, writeGate, cts.Token))
                .ToArray();

            _ = CompleteAsync(reading, pumping, readingGroups, dataReaders, outputPipe.Writer, writeGate, cts);
            return outputPipe.Reader.AsStream();
        }
        catch
        {
            await cts.CancelAsync().ConfigureAwait(false);
            cts.Dispose();

            foreach (var readingGroup in readingGroups)
            {
                try
                {
                    readingGroup.Controller.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Disposing a failed batch data controller failed");
                }

                foreach (var writer in readingGroup.CatalogItemRequestPipeWriters)
                {
                    try
                    {
                        await writer.DataWriter.CompleteAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Completing a failed batch data writer failed");
                    }
                }
            }

            foreach (var (_, reader) in dataReaders)
            {
                try
                {
                    await reader.CompleteAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Completing a failed batch data reader failed");
                }
            }

            await outputPipe.Writer.CompleteAsync().ConfigureAwait(false);
            await outputPipe.Reader.CompleteAsync().ConfigureAwait(false);

            throw;
        }

        static async Task PumpAsync(
            int resourceIndex,
            PipeReader input,
            PipeWriter output,
            SemaphoreSlim writeGate,
            CancellationToken cancellationToken)
        {
            const int maximumPayloadLength = 64 * 1024;

            while (true)
            {
                var result = await input.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buffer = result.Buffer;

                try
                {
                    foreach (var segment in buffer)
                    {
                        var remaining = segment;

                        while (!remaining.IsEmpty)
                        {
                            var payload = remaining[..Math.Min(remaining.Length, maximumPayloadLength)];
                            await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

                            try
                            {
                                var header = output.GetSpan(8);
                                BinaryPrimitives.WriteInt32LittleEndian(header, resourceIndex);
                                BinaryPrimitives.WriteInt32LittleEndian(header[4..], payload.Length);
                                output.Advance(8);
                                payload.CopyTo(output.GetMemory(payload.Length));
                                output.Advance(payload.Length);

                                var flushResult = await output.FlushAsync(cancellationToken).ConfigureAwait(false);

                                if (flushResult.IsCanceled)
                                    throw new OperationCanceledException(cancellationToken);

                                if (flushResult.IsCompleted)
                                    throw new IOException("The batch output pipe completed before all data was written.");
                            }
                            finally
                            {
                                writeGate.Release();
                            }

                            remaining = remaining[payload.Length..];
                        }
                    }
                }
                finally
                {
                    input.AdvanceTo(buffer.End);
                }

                if (result.IsCompleted)
                    return;
            }
        }

        async Task CompleteAsync(
            Task reading,
            Task[] pumping,
            List<DataReadingGroup> groups,
            List<(int Index, PipeReader Reader)> readers,
            PipeWriter output,
            SemaphoreSlim writeGate,
            CancellationTokenSource cts)
        {
            Exception? error = null;

            try
            {
                await NexusUtilities.WhenAllFailFastAsync([reading, .. pumping], cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                error = ex;
                await cts.CancelAsync().ConfigureAwait(false);

                foreach (var writer in groups.SelectMany(group => group.CatalogItemRequestPipeWriters))
                {
                    try
                    {
                        await writer.DataWriter.CompleteAsync(ex).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                try
                {
                    await Task.WhenAll(pumping).ConfigureAwait(false);
                }
                catch
                {
                }
            }
            finally
            {
                foreach (var group in groups)
                    group.Controller.Dispose();

                foreach (var (_, reader) in readers)
                    await reader.CompleteAsync().ConfigureAwait(false);

                await output.CompleteAsync(error).ConfigureAwait(false);
                writeGate.Dispose();
                cts.Dispose();
            }
        }
    }

    internal static void ValidateResourcePaths(string[]? resourcePaths)
    {
        if (resourcePaths is null || resourcePaths.Length == 0)
            throw new ValidationException("At least one resource path is required.");

        if (resourcePaths.Length > 100)
            throw new ValidationException("A maximum of 100 resource paths is allowed.");

        if (resourcePaths.Any(string.IsNullOrWhiteSpace))
            throw new ValidationException("Resource paths must not be blank.");

        if (resourcePaths.Distinct(StringComparer.Ordinal).Count() != resourcePaths.Length)
            throw new ValidationException("Resource paths must be unique.");
    }

    public async Task ReadAsDoubleAsync(
       string resourcePath,
       DateTime begin,
       DateTime end,
       Memory<double> buffer,
       CancellationToken cancellationToken
    )
    {
        var stream = await ReadAsStreamAsync(
            resourcePath,
            begin,
            end,
            cancellationToken);

        var byteBuffer = new CastMemoryManager<double, byte>(buffer).Memory;

        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(byteBuffer, cancellationToken)) > 0)
        {
            byteBuffer = byteBuffer[bytesRead..];
        }
    }

    public async Task<string> ExportAsync(
        Guid exportId,
        IEnumerable<CatalogItemRequest> catalogItemRequests,
        ReadDataHandler readDataHandler,
        ExportParameters exportParameters,
        CancellationToken cancellationToken)
    {
        if (!catalogItemRequests.Any() || exportParameters.Begin == exportParameters.End)
            return string.Empty;

        // find sample period
        var samplePeriods = catalogItemRequests
            .Select(catalogItemRequest => catalogItemRequest.Item.Representation.SamplePeriod)
            .Distinct()
            .ToList();

        if (samplePeriods.Count != 1)
            throw new ValidationException("All representations must be of the same sample period.");

        var samplePeriod = samplePeriods.First();

        // validate file period
        if (exportParameters.FilePeriod.Ticks % samplePeriod.Ticks != 0)
            throw new ValidationException("The file period must be a multiple of the sample period.");

        // start
        var zipFileName = string.Empty;
        IDataWriterController? controller = default!;

        var tmpFolderPath = Path.Combine(Path.GetTempPath(), "Nexus", Guid.NewGuid().ToString());

        if (exportParameters.Type is not null)
        {
            // create tmp/target directory
            Directory.CreateDirectory(tmpFolderPath);

            // copy available licenses
            var catalogIds = catalogItemRequests
                .Select(request => request.Container.Id)
                .Distinct();

            foreach (var catalogId in catalogIds)
            {
                CopyLicenseIfAvailable(catalogId, tmpFolderPath);
            }

            // get data writer controller
            var resourceLocator = new Uri(tmpFolderPath, UriKind.Absolute);
            controller = await _dataControllerService.GetDataWriterControllerAsync(resourceLocator, exportParameters, cancellationToken);
        }

        // write data files
        try
        {
            var exportContext = new ExportContext(samplePeriod, catalogItemRequests, readDataHandler, exportParameters);
            await CreateFilesAsync(exportContext, controller, cancellationToken);
        }
        finally
        {
            controller?.Dispose();
        }

        if (exportParameters.Type is not null)
        {
            // write zip archive
            zipFileName = $"{Guid.NewGuid()}.zip";
            var zipArchiveStream = _databaseService.WriteArtifact(zipFileName);
            using var zipArchive = new ZipArchive(zipArchiveStream, ZipArchiveMode.Create);
            WriteZipArchiveEntries(zipArchive, tmpFolderPath, cancellationToken);
        }

        return zipFileName;
    }

    private void CopyLicenseIfAvailable(string catalogId, string targetFolder)
    {
        var enumeratonOptions = new EnumerationOptions() { MatchCasing = MatchCasing.CaseInsensitive };

        if (_databaseService.TryReadFirstAttachment(catalogId, "license.md", enumeratonOptions, out var licenseStream))
        {
            try
            {
                var prefix = catalogId.TrimStart('/').Replace('/', '_');
                var targetFileName = $"{prefix}_LICENSE.md";
                var targetFile = Path.Combine(targetFolder, targetFileName);

                using var targetFileStream = new FileStream(targetFile, FileMode.OpenOrCreate);
                licenseStream.CopyTo(targetFileStream);
            }
            finally
            {
                licenseStream.Dispose();
            }
        }
    }

    private async Task CreateFilesAsync(
        ExportContext exportContext,
        IDataWriterController? dataWriterController,
        CancellationToken cancellationToken)
    {
        var exportParameters = exportContext.ExportParameters;
        DataSourceController.ValidateParameters(
            exportParameters.Begin,
            exportParameters.End,
            exportContext.SamplePeriod);

        /* reading groups */
        var catalogItemRequestPipeReaders = new List<CatalogItemRequestPipeReader>();
        var readingGroups = new List<DataReadingGroup>();

        foreach (var group in exportContext.CatalogItemRequests.GroupBy(request => request.Container))
        {
            var registration = group.Key.Pipeline;
            var controller = await _dataControllerService.GetDataSourceControllerAsync(registration, cancellationToken);
            var catalogItemRequestPipeWriters = new List<CatalogItemRequestPipeWriter>();

            foreach (var catalogItemRequest in group)
            {
                var pipe = new Pipe();
                catalogItemRequestPipeWriters.Add(new CatalogItemRequestPipeWriter(catalogItemRequest, pipe.Writer));
                catalogItemRequestPipeReaders.Add(new CatalogItemRequestPipeReader(catalogItemRequest, pipe.Reader));
            }

            readingGroups.Add(new DataReadingGroup(controller, catalogItemRequestPipeWriters.ToArray()));
        }

        /* cancellation */
        var cts = new CancellationTokenSource();
        cancellationToken.Register(cts.Cancel);

        /* read */
        var logger = _loggerFactory.CreateLogger<DataSourceController>();

        var reading = DataSourceController.ReadAsync(
            exportParameters.Begin,
            exportParameters.End,
            exportContext.SamplePeriod,
            readingGroups.ToArray(),
            exportContext.ReadDataHandler,
            _memoryTracker,
            ReadProgress,
            logger,
            cts.Token);

        /* write */
        Task writing;

        /* There is not data writer, so just advance through the pipe. */
        if (dataWriterController is null)
        {
            var writingTasks = catalogItemRequestPipeReaders.Select(current =>
            {
                return Task.Run(async () =>
                {
                    while (true)
                    {
                        var result = await current.DataReader.ReadAsync(cts.Token);

                        if (result.IsCompleted)
                            return;

                        else
                            current.DataReader.AdvanceTo(result.Buffer.End);
                    }
                }, cts.Token);
            });

            writing = Task.WhenAll(writingTasks);
        }

        /* Normal operation. */
        else
        {
            var singleFile = exportParameters.FilePeriod == default;

            var filePeriod = singleFile
                ? exportParameters.End - exportParameters.Begin
                : exportParameters.FilePeriod;

            writing = dataWriterController.WriteAsync(
                exportParameters.Begin,
                exportParameters.End,
                exportContext.SamplePeriod,
                filePeriod,
                catalogItemRequestPipeReaders.ToArray(),
                WriteProgress,
                cts.Token
            );
        }

        var tasks = new List<Task>() { reading, writing };

        try
        {
            await NexusUtilities.WhenAllFailFastAsync(tasks, cts.Token);
        }
        catch
        {
            await cts.CancelAsync();
            throw;
        }
    }

    private void WriteZipArchiveEntries(ZipArchive zipArchive, string sourceFolderPath, CancellationToken cancellationToken)
    {
        ((IProgress<double>)WriteProgress).Report(0);

        try
        {
            // write zip archive entries
            var filePaths = Directory.GetFiles(sourceFolderPath, "*", SearchOption.AllDirectories);
            var fileCount = filePaths.Length;
            var currentCount = 0;

            foreach (var filePath in filePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogTrace("Write content of {FilePath} to the ZIP archive", filePath);

                var zipArchiveEntry = zipArchive.CreateEntry(Path.GetFileName(filePath), CompressionLevel.Optimal);

                using var fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read);
                using var zipArchiveEntryStream = zipArchiveEntry.Open();

                fileStream.CopyTo(zipArchiveEntryStream);

                currentCount++;
                ((IProgress<double>)WriteProgress).Report(currentCount / (double)fileCount);
            }
        }
        finally
        {
            CleanUp(sourceFolderPath);
        }
    }

    private static void CleanUp(string directoryPath)
    {
        try
        {
            Directory.Delete(directoryPath, true);
        }
        catch
        {
            //
        }
    }
}
