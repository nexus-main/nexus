// MIT License
// Copyright (c) [2024] [nexus-main]

using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Security.Claims;
using Nexus.Core;
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
    ILoggerFactory loggerFactory) : IDataService
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
       CancellationToken cancellationToken)
    {
        begin = DateTime.SpecifyKind(begin, DateTimeKind.Utc);
        end = DateTime.SpecifyKind(end, DateTimeKind.Utc);

        // find representation
        var root = _appState.CatalogState.Root;
        var catalogItemRequest = await root.TryFindAsync(resourcePath, cancellationToken) ?? throw new Exception($"Could not find resource path {resourcePath}.");
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

    public async Task ReadAsDoubleAsync(
       string resourcePath,
       DateTime begin,
       DateTime end,
       Memory<double> buffer,
       CancellationToken cancellationToken)
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
        var exportParameters = exportContext.ExportParameters;
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
