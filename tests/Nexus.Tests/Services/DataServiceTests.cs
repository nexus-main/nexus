// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.Extensions.Logging;
using Moq;
using Nexus.Core;
using Nexus.Core.V1;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using System.IO.Compression;
using Xunit;

namespace Services;

public class DataServiceTests
{
    delegate void GobbleReturns(string catalogId, string searchPattern, EnumerationOptions enumerationOptions, out Stream attachment);

    [Fact]
    public async Task CanExportAsync()
    {
        // create dirs
        var root = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
        Directory.CreateDirectory(root);

        // misc
        var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2020, 01, 03, 0, 0, 0, DateTimeKind.Utc);
        var samplePeriod = TimeSpan.FromSeconds(1);

        var registration1 = new DataSourceRegistration(Type: "A", new Uri("a", UriKind.Relative), default, default);
        var pipeline1 = new DataSourcePipeline([registration1]);

        var registration2 = new DataSourceRegistration(Type: "B", new Uri("a", UriKind.Relative), default, default);
        var pipeline2 = new DataSourcePipeline([registration2]);

        // DI services
        var dataSourceController1 = Mock.Of<IDataSourceController>();
        var dataSourceController2 = Mock.Of<IDataSourceController>();

        var dataWriterController = Mock.Of<IDataWriterController>();
        Uri tmpUri = default!;

        Mock.Get(dataWriterController)
            .Setup(s => s.WriteAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<CatalogItemRequestPipeReader[]>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime, DateTime, TimeSpan, TimeSpan, CatalogItemRequestPipeReader[], IProgress<double>, CancellationToken>(
            (begin, end, samplePeriod, filePeriod, catalogItemRequestPipeReaders, progress, cancellationToken) =>
            {
                foreach (var catalogIdRequestPipeReaderGroup in catalogItemRequestPipeReaders.GroupBy(x => x.Request.Item.Catalog.Id))
                {
                    var prefix = catalogIdRequestPipeReaderGroup.Key.TrimStart('/').Replace('/', '_');
                    var filePath = Path.Combine(tmpUri.LocalPath, $"{prefix}.dat");
                    File.Create(filePath).Dispose();
                }
            });

        var dataControllerService = Mock.Of<IDataControllerService>();

        Mock.Get(dataControllerService)
            .Setup(s => s.GetDataSourceControllerAsync(It.IsAny<DataSourcePipeline>(), It.IsAny<CancellationToken>()))
            .Returns<DataSourcePipeline, CancellationToken>((pipeline, cancellationToken) =>
            {
                if (pipeline.Registrations[0].Type == registration1.Type)
                    return Task.FromResult(dataSourceController1);

                else if (pipeline.Registrations[0].Type == registration2.Type)
                    return Task.FromResult(dataSourceController2);

                else
                    throw new Exception("Invalid data source registration.");
            });

        Mock.Get(dataControllerService)
            .Setup(s => s.GetDataWriterControllerAsync(It.IsAny<Uri>(), It.IsAny<ExportParameters>(), It.IsAny<CancellationToken>()))
            .Returns<Uri, ExportParameters, CancellationToken>((uri, exportParameters, cancellationToken) =>
            {
                tmpUri = uri;
                return Task.FromResult(dataWriterController);
            });

        var databaseService = Mock.Of<IDatabaseService>();

        Mock.Get(databaseService)
            .Setup(databaseService => databaseService.TryReadFirstAttachment(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<EnumerationOptions>(),
                out It.Ref<Stream?>.IsAny))
            .Callback(new GobbleReturns((string catalogId, string searchPattern, EnumerationOptions enumerationOptions, out Stream attachment) =>
            {
                attachment = new MemoryStream();
            }))
            .Returns(true);

        Mock.Get(databaseService)
            .Setup(databaseService => databaseService.WriteArtifact(It.IsAny<string>()))
            .Returns<string>((fileName) => File.OpenWrite(Path.Combine(root, fileName)));

        var logger = Mock.Of<ILogger<DataService>>();
        var logger2 = Mock.Of<ILogger<DataSourceController>>();

        var loggerFactory = Mock.Of<ILoggerFactory>();

        Mock.Get(loggerFactory)
            .Setup(loggerFactory => loggerFactory.CreateLogger(It.IsAny<string>()))
            .Returns(logger2);

        var memoryTracker = Mock.Of<IMemoryTracker>();

        Mock.Get(memoryTracker)
            .Setup(memoryTracker => memoryTracker.RegisterAllocationAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync<long, long, CancellationToken, IMemoryTracker, AllocationRegistration>((minium, maximum, _) => new AllocationRegistration(memoryTracker, actualByteCount: maximum));

        // catalog items
        var representation1 = new Representation(dataType: NexusDataType.FLOAT32, samplePeriod: samplePeriod);
        var resource1 = new Resource(id: "Resource1");
        var catalog1 = new ResourceCatalog(id: "/A/B/C");
        var catalogItem1 = new CatalogItem(catalog1, resource1, representation1, Parameters: default);
        var catalogContainer1 = new CatalogContainer(new CatalogRegistration(catalog1.Id, string.Empty), default, default, pipeline1, default!, default!, default!, default!, default!);

        var representation2 = new Representation(dataType: NexusDataType.FLOAT32, samplePeriod: samplePeriod);
        var resource2 = new Resource(id: "Resource2");
        var catalog2 = new ResourceCatalog(id: "/F/G/H");
        var catalogItem2 = new CatalogItem(catalog2, resource2, representation2, Parameters: default);
        var catalogContainer2 = new CatalogContainer(new CatalogRegistration(catalog2.Id, string.Empty), default, default, pipeline2, default!, default!, default!, default!, default!);

        // export parameters
        var exportParameters = new ExportParameters(
            Begin: begin,
            End: end,
            FilePeriod: TimeSpan.FromSeconds(10),
            Type: "A",
            ResourcePaths: [catalogItem1.ToPath(), catalogItem2.ToPath()],
            Configuration: default);

        // data service
        var dataService = new DataService(
            default!,
            default!,
            dataControllerService,
            databaseService,
            memoryTracker,
            logger,
            loggerFactory);

        // act
        try
        {
            var catalogItemRequests = new[]
            {
                new CatalogItemRequest(catalogItem1, default, catalogContainer1),
                new CatalogItemRequest(catalogItem2, default, catalogContainer2)
            };

            var relativeDownloadUrl = await dataService
                .ExportAsync(Guid.NewGuid(), catalogItemRequests, default!, exportParameters, CancellationToken.None);

            // assert
            var zipFile = Path.Combine(root, relativeDownloadUrl.Split('/').Last());
            var unzipFolder = Path.GetDirectoryName(zipFile)!;

            ZipFile.ExtractToDirectory(zipFile, unzipFolder);

            Assert.True(File.Exists(Path.Combine(unzipFolder, "A_B_C.dat")));
            Assert.True(File.Exists(Path.Combine(unzipFolder, "A_B_C_LICENSE.md")));

            Assert.True(File.Exists(Path.Combine(unzipFolder, "F_G_H.dat")));
            Assert.True(File.Exists(Path.Combine(unzipFolder, "F_G_H_LICENSE.md")));

        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                //
            }
        }
    }
}