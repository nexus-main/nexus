// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.Extensions.Logging;
using Moq;
using Nexus.Core;
using Nexus.Core.V1;
using Nexus.Core.V2;
using Nexus.DataModel;
using ExportParameters = Nexus.Core.V2.ExportParameters;
using Nexus.Extensibility;
using Nexus.Services;
using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Pipelines;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Xunit;

namespace Services;

public class DataServiceTests
{
    delegate void GobbleReturns(string catalogId, string searchPattern, EnumerationOptions enumerationOptions, out Stream attachment);

    [Fact]
    public void AllowsOneHundredResourcePaths()
    {
        var paths = Enumerable.Range(0, 100).Select(index => $"/A/R{index}").ToArray();

        DataService.ValidateResourcePaths(paths);
    }

    [Fact]
    public void RejectsOneHundredAndOneResourcePaths()
    {
        var paths = Enumerable.Range(0, 101).Select(index => $"/A/R{index}").ToArray();

        Assert.Throws<ValidationException>(() => DataService.ValidateResourcePaths(paths));
    }

    [Fact]
    public void RejectsUnsupportedPrecision()
    {
        Assert.Throws<ValidationException>(() => DataService.ValidatePrecision((Precision)123));
    }

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
            Configuration: default,
            Precision: Precision.Float32
        );

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

    [Fact]
    public async Task CompletesBatchOutputWhenControllerDisposeThrows()
    {
        var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var end = begin + TimeSpan.FromSeconds(1);
        var samplePeriod = TimeSpan.FromSeconds(1);
        var registration = new DataSourceRegistration(Type: "A", new Uri("a", UriKind.Relative), default, default);
        var pipeline = new DataSourcePipeline([registration]);
        var representation = new Representation(NexusDataType.FLOAT64, samplePeriod);
        var resource = new ResourceBuilder("T1").AddRepresentation(representation).Build();
        var catalog = new ResourceCatalogBuilder("/A/B/C").AddResource(resource).Build();
        catalog = catalog.EnsureAndSanitizeMandatoryProperties(0, []);
        resource = catalog.Resources![0];

        var lookupController = Mock.Of<IDataSourceController>();

        Mock.Get(lookupController)
            .Setup(current => current.GetCatalogAsync(catalog.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalog);

        Mock.Get(lookupController)
            .Setup(current => current.GetTimeRangeAsync(catalog.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogTimeRange(begin, end));

        var streamingController = Mock.Of<IDataSourceController>();

        Mock.Get(streamingController)
            .Setup(current => current.ReadAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                samplePeriod,
                It.IsAny<Precision>(),
                It.IsAny<CatalogItemRequestPipeWriter[]>(),
                It.IsAny<ReadDataHandler>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()))
            .Returns<DateTime, DateTime, TimeSpan, Precision, CatalogItemRequestPipeWriter[], ReadDataHandler, IProgress<double>, CancellationToken>(
                async (_, _, _, _, writers, _, progress, cancellationToken) =>
                {
                    foreach (var writer in writers)
                    {
                        await writer.DataWriter.WriteAsync(BitConverter.GetBytes(1f), cancellationToken);
                    }

                    progress.Report(1);
                });

        Mock.Get(streamingController)
            .Setup(current => current.Dispose())
            .Throws(new InvalidOperationException("dispose failed"));

        var dataControllerService = Mock.Of<IDataControllerService>();
        var getControllerCallCount = 0;

        Mock.Get(dataControllerService)
            .Setup(current => current.GetDataSourceControllerAsync(pipeline, It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(++getControllerCallCount == 1
                ? lookupController
                : streamingController));

        var catalogManager = Mock.Of<ICatalogManager>();

        Mock.Get(catalogManager)
            .Setup(current => current.GetCatalogContainersAsync(
                It.IsAny<CatalogContainer>(),
                It.IsAny<CancellationToken>()))
            .Returns<CatalogContainer, CancellationToken>((container, _) => Task.FromResult(container.Id switch
            {
                "/" => new[]
                {
                    new CatalogContainer(
                        new CatalogRegistration(catalog.Id, string.Empty),
                        default,
                        default,
                        pipeline,
                        default!,
                        default!,
                        catalogManager,
                        default!,
                        dataControllerService)
                },
                _ => throw new Exception("Unsupported catalog container.")
            }));

        var appState = new AppState
        {
            CatalogState = new CatalogState(
                CatalogContainer.CreateRoot(catalogManager, default!),
                new CatalogCache())
        };

        var memoryTracker = Mock.Of<IMemoryTracker>();

        Mock.Get(memoryTracker)
            .Setup(current => current.RegisterAllocationAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync<long, long, CancellationToken, IMemoryTracker, AllocationRegistration>(
                (_, maximum, _) => new AllocationRegistration(memoryTracker, maximum));

        var logger = Mock.Of<ILogger<DataService>>();
        var loggerFactory = Mock.Of<ILoggerFactory>();

        Mock.Get(loggerFactory)
            .Setup(current => current.CreateLogger(It.IsAny<string>()))
            .Returns(Mock.Of<ILogger<DataSourceController>>());

        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, nameof(NexusRoles.Administrator))],
            authenticationType: "test"));

        var dataService = new DataService(
            appState,
            user,
            dataControllerService,
            default!,
            memoryTracker,
            logger,
            loggerFactory);

        var stream = await dataService.ReadBatchAsStreamAsync(
            new BatchStreamRequest(begin, end, ["/A/B/C/T1/1_s"], Precision.Float32),
            CancellationToken.None);

        var sink = new MemoryStream();
        await stream.CopyToAsync(sink).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(sink.Length > 0);

        Mock.Get(logger).Verify(current => current.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((value, _) => value.ToString()!.Contains("Disposing batch data controller failed")),
            It.IsAny<InvalidOperationException>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task StreamsMultipleResourcesAsFramedBatchOutput()
    {
        var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var end = begin + TimeSpan.FromSeconds(1);
        var samplePeriod = TimeSpan.FromSeconds(1);
        var registration = new DataSourceRegistration(Type: "A", new Uri("a", UriKind.Relative), default, default);
        var pipeline = new DataSourcePipeline([registration]);
        var representation = new Representation(NexusDataType.FLOAT64, samplePeriod);
        var resource1 = new ResourceBuilder("T1").AddRepresentation(representation).Build();
        var resource2 = new ResourceBuilder("T2").AddRepresentation(representation).Build();
        var catalog = new ResourceCatalogBuilder("/A/B/C")
            .AddResource(resource1)
            .AddResource(resource2)
            .Build();

        catalog = catalog.EnsureAndSanitizeMandatoryProperties(0, []);

        var lookupController = Mock.Of<IDataSourceController>();

        Mock.Get(lookupController)
            .Setup(current => current.GetCatalogAsync(catalog.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalog);

        Mock.Get(lookupController)
            .Setup(current => current.GetTimeRangeAsync(catalog.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogTimeRange(begin, end));

        var streamingController = Mock.Of<IDataSourceController>();

        Mock.Get(streamingController)
            .Setup(current => current.ReadAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                samplePeriod,
                It.IsAny<Precision>(),
                It.IsAny<CatalogItemRequestPipeWriter[]>(),
                It.IsAny<ReadDataHandler>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()))
            .Returns<DateTime, DateTime, TimeSpan, Precision, CatalogItemRequestPipeWriter[], ReadDataHandler, IProgress<double>, CancellationToken>(
                async (_, _, _, _, writers, _, progress, cancellationToken) =>
                {
                    await writers[1].DataWriter.WriteAsync(BitConverter.GetBytes(2f), cancellationToken);
                    await writers[0].DataWriter.WriteAsync(BitConverter.GetBytes(1f), cancellationToken);
                    progress.Report(1);
                });

        var dataControllerService = Mock.Of<IDataControllerService>();
        var getControllerCallCount = 0;

        Mock.Get(dataControllerService)
            .Setup(current => current.GetDataSourceControllerAsync(pipeline, It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(++getControllerCallCount == 1
                ? lookupController
                : streamingController));

        var catalogManager = Mock.Of<ICatalogManager>();

        Mock.Get(catalogManager)
            .Setup(current => current.GetCatalogContainersAsync(
                It.IsAny<CatalogContainer>(),
                It.IsAny<CancellationToken>()))
            .Returns<CatalogContainer, CancellationToken>((container, _) => Task.FromResult(container.Id switch
            {
                "/" => new[]
                {
                    new CatalogContainer(
                        new CatalogRegistration(catalog.Id, string.Empty),
                        default,
                        default,
                        pipeline,
                        default!,
                        default!,
                        catalogManager,
                        default!,
                        dataControllerService)
                },
                _ => throw new Exception("Unsupported catalog container.")
            }));

        var appState = new AppState
        {
            CatalogState = new CatalogState(
                CatalogContainer.CreateRoot(catalogManager, default!),
                new CatalogCache())
        };

        var memoryTracker = Mock.Of<IMemoryTracker>();

        Mock.Get(memoryTracker)
            .Setup(current => current.RegisterAllocationAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync<long, long, CancellationToken, IMemoryTracker, AllocationRegistration>(
                (_, maximum, _) => new AllocationRegistration(memoryTracker, maximum));

        var loggerFactory = Mock.Of<ILoggerFactory>();

        Mock.Get(loggerFactory)
            .Setup(current => current.CreateLogger(It.IsAny<string>()))
            .Returns(Mock.Of<ILogger<DataSourceController>>());

        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, nameof(NexusRoles.Administrator))],
            authenticationType: "test"));

        var dataService = new DataService(
            appState,
            user,
            dataControllerService,
            default!,
            memoryTracker,
            Mock.Of<ILogger<DataService>>(),
            loggerFactory);

        var stream = await dataService.ReadBatchAsStreamAsync(
            new BatchStreamRequest(begin, end, ["/A/B/C/T1/1_s", "/A/B/C/T2/1_s"], Precision.Float32),
            CancellationToken.None);

        var sink = new MemoryStream();
        await stream.CopyToAsync(sink).WaitAsync(TimeSpan.FromSeconds(5));

        var frames = new Dictionary<int, float>();
        var bytes = sink.ToArray();

        for (var offset = 0; offset < bytes.Length; offset += 12)
        {
            var resourceIndex = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4));

            Assert.Equal(sizeof(float), payloadLength);
            frames.Add(resourceIndex, BitConverter.ToSingle(bytes, offset + 8));
        }

        Assert.Equal(2, frames.Count);
        Assert.Equal(1f, frames[0]);
        Assert.Equal(2f, frames[1]);
    }

    [Fact]
    public async Task StreamsReadyResourceBeforeLaterResource()
    {
        const int payloadLength = 4 * 1024 * 1024;
        var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var end = begin + TimeSpan.FromSeconds(1);
        var samplePeriod = TimeSpan.FromSeconds(1);
        var resource1Written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writeResource0 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var streamingController = Mock.Of<IDataSourceController>();

        Mock.Get(streamingController)
            .Setup(current => current.ReadAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                samplePeriod,
                It.IsAny<Precision>(),
                It.IsAny<CatalogItemRequestPipeWriter[]>(),
                It.IsAny<ReadDataHandler>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()))
            .Returns<DateTime, DateTime, TimeSpan, Precision, CatalogItemRequestPipeWriter[], ReadDataHandler, IProgress<double>, CancellationToken>(
                async (_, _, _, _, writers, _, progress, cancellationToken) =>
                {
                    await WriteRepeatedByteAsync(writers[1].DataWriter, 2, payloadLength, cancellationToken);
                    resource1Written.SetResult();
                    await writeResource0.Task.WaitAsync(cancellationToken);
                    await writers[0].DataWriter.WriteAsync(BitConverter.GetBytes(1f), cancellationToken);
                    progress.Report(1);
                });

        var dataService = CreateBatchDataService(begin, end, samplePeriod, streamingController, "T1", "T2");
        var stream = await dataService.ReadBatchAsStreamAsync(
            new BatchStreamRequest(begin, end, ["/A/B/C/T1/1_s", "/A/B/C/T2/1_s"], Precision.Float32),
            CancellationToken.None);

        await resource1Written.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var firstHeader = await ReadExactAsync(stream, 8).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(firstHeader.AsSpan(0, 4)));
        Assert.Equal(payloadLength, BinaryPrimitives.ReadInt32LittleEndian(firstHeader.AsSpan(4, 4)));

        await DiscardExactAsync(stream, payloadLength).WaitAsync(TimeSpan.FromSeconds(5));
        writeResource0.SetResult();

        var remaining = new MemoryStream();
        await stream.CopyToAsync(remaining).WaitAsync(TimeSpan.FromSeconds(5));

        var frames = ReadBatchFrames(remaining.ToArray());
        var frame = Assert.Single(frames);

        Assert.Equal(0, frame.ResourceIndex);
        Assert.Equal(sizeof(float), frame.PayloadLength);
        Assert.Equal(1f, BitConverter.ToSingle(frame.Payload));
    }

    [Fact]
    public async Task SplitsLargeBatchPayloadsIntoBoundedFrames()
    {
        const int maximumPayloadLength = 4 * 1024 * 1024;
        const int payloadLength = maximumPayloadLength + sizeof(float);
        var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var end = begin + TimeSpan.FromSeconds(1);
        var samplePeriod = TimeSpan.FromSeconds(1);
        var streamingController = Mock.Of<IDataSourceController>();

        Mock.Get(streamingController)
            .Setup(current => current.ReadAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                samplePeriod,
                It.IsAny<Precision>(),
                It.IsAny<CatalogItemRequestPipeWriter[]>(),
                It.IsAny<ReadDataHandler>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()))
            .Returns<DateTime, DateTime, TimeSpan, Precision, CatalogItemRequestPipeWriter[], ReadDataHandler, IProgress<double>, CancellationToken>(
                async (_, _, _, _, writers, _, progress, cancellationToken) =>
                {
                    await WriteRepeatedByteAsync(writers[0].DataWriter, 7, payloadLength, cancellationToken);
                    progress.Report(1);
                });

        var dataService = CreateBatchDataService(begin, end, samplePeriod, streamingController, "T1");
        var stream = await dataService.ReadBatchAsStreamAsync(
            new BatchStreamRequest(begin, end, ["/A/B/C/T1/1_s"], Precision.Float32),
            CancellationToken.None);

        var sink = new MemoryStream();
        await stream.CopyToAsync(sink).WaitAsync(TimeSpan.FromSeconds(5));

        var frames = ReadBatchFrames(sink.ToArray());

        Assert.True(frames.Count >= 2);
        Assert.All(frames, frame =>
        {
            Assert.Equal(0, frame.ResourceIndex);
            Assert.InRange(frame.PayloadLength, 1, maximumPayloadLength);
            Assert.All(frame.Payload, value => Assert.Equal(7, value));
        });
        Assert.Equal(payloadLength, frames.Sum(frame => frame.PayloadLength));
    }

    [Fact]
    public async Task CompletesBatchOutputWhenProducerFails()
    {
        var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var end = begin + TimeSpan.FromSeconds(1);
        var samplePeriod = TimeSpan.FromSeconds(1);
        var streamingController = Mock.Of<IDataSourceController>();

        Mock.Get(streamingController)
            .Setup(current => current.ReadAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                samplePeriod,
                It.IsAny<Precision>(),
                It.IsAny<CatalogItemRequestPipeWriter[]>(),
                It.IsAny<ReadDataHandler>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()))
            .Returns<DateTime, DateTime, TimeSpan, Precision, CatalogItemRequestPipeWriter[], ReadDataHandler, IProgress<double>, CancellationToken>(
                async (_, _, _, _, writers, _, _, cancellationToken) =>
                {
                    await writers[0].DataWriter.WriteAsync(BitConverter.GetBytes(1f), cancellationToken);
                    throw new InvalidOperationException("producer failed");
                });

        var dataService = CreateBatchDataService(begin, end, samplePeriod, streamingController, "T1", "T2");
        var stream = await dataService.ReadBatchAsStreamAsync(
            new BatchStreamRequest(begin, end, ["/A/B/C/T1/1_s", "/A/B/C/T2/1_s"], Precision.Float32),
            CancellationToken.None);

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            stream.CopyToAsync(Stream.Null).WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Contains("producer failed", exception.ToString(), StringComparison.Ordinal);
        Mock.Get(streamingController).Verify(current => current.Dispose(), Times.Once);
    }

    private static DataService CreateBatchDataService(
        DateTime begin,
        DateTime end,
        TimeSpan samplePeriod,
        IDataSourceController streamingController,
        params string[] resourceIds)
    {
        var registration = new DataSourceRegistration(Type: "A", new Uri("a", UriKind.Relative), default, default);
        var pipeline = new DataSourcePipeline([registration]);
        var representation = new Representation(NexusDataType.FLOAT64, samplePeriod);
        var catalogBuilder = new ResourceCatalogBuilder("/A/B/C");

        foreach (var resourceId in resourceIds)
            catalogBuilder.AddResource(new ResourceBuilder(resourceId).AddRepresentation(representation).Build());

        var catalog = catalogBuilder.Build().EnsureAndSanitizeMandatoryProperties(0, []);
        var lookupController = Mock.Of<IDataSourceController>();

        Mock.Get(lookupController)
            .Setup(current => current.GetCatalogAsync(catalog.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalog);

        Mock.Get(lookupController)
            .Setup(current => current.GetTimeRangeAsync(catalog.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogTimeRange(begin, end));

        var dataControllerService = Mock.Of<IDataControllerService>();
        var getControllerCallCount = 0;

        Mock.Get(dataControllerService)
            .Setup(current => current.GetDataSourceControllerAsync(pipeline, It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(++getControllerCallCount == 1
                ? lookupController
                : streamingController));

        var catalogManager = Mock.Of<ICatalogManager>();

        Mock.Get(catalogManager)
            .Setup(current => current.GetCatalogContainersAsync(
                It.IsAny<CatalogContainer>(),
                It.IsAny<CancellationToken>()))
            .Returns<CatalogContainer, CancellationToken>((container, _) => Task.FromResult(container.Id switch
            {
                "/" => new[]
                {
                    new CatalogContainer(
                        new CatalogRegistration(catalog.Id, string.Empty),
                        default,
                        default,
                        pipeline,
                        default!,
                        default!,
                        catalogManager,
                        default!,
                        dataControllerService)
                },
                _ => throw new Exception("Unsupported catalog container.")
            }));

        var appState = new AppState
        {
            CatalogState = new CatalogState(
                CatalogContainer.CreateRoot(catalogManager, default!),
                new CatalogCache())
        };

        var memoryTracker = Mock.Of<IMemoryTracker>();

        Mock.Get(memoryTracker)
            .Setup(current => current.RegisterAllocationAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync<long, long, CancellationToken, IMemoryTracker, AllocationRegistration>(
                (_, maximum, _) => new AllocationRegistration(memoryTracker, maximum));

        var loggerFactory = Mock.Of<ILoggerFactory>();

        Mock.Get(loggerFactory)
            .Setup(current => current.CreateLogger(It.IsAny<string>()))
            .Returns(Mock.Of<ILogger<DataSourceController>>());

        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, nameof(NexusRoles.Administrator))],
            authenticationType: "test"));

        return new DataService(
            appState,
            user,
            dataControllerService,
            default!,
            memoryTracker,
            Mock.Of<ILogger<DataService>>(),
            loggerFactory);
    }

    private static async Task WriteRepeatedByteAsync(PipeWriter writer, byte value, int byteCount, CancellationToken cancellationToken)
    {
        var span = writer.GetSpan(byteCount);
        span[..byteCount].Fill(value);
        writer.Advance(byteCount);
        await writer.FlushAsync(cancellationToken);
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int byteCount)
    {
        var bytes = new byte[byteCount];
        await stream.ReadExactlyAsync(bytes);
        return bytes;
    }

    private static async Task DiscardExactAsync(Stream stream, int byteCount)
    {
        var buffer = new byte[Math.Min(byteCount, 81920)];
        var remaining = byteCount;

        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)));

            if (read == 0)
                throw new EndOfStreamException();

            remaining -= read;
        }
    }

    private static List<(int ResourceIndex, int PayloadLength, byte[] Payload)> ReadBatchFrames(byte[] bytes)
    {
        var frames = new List<(int ResourceIndex, int PayloadLength, byte[] Payload)>();

        for (var offset = 0; offset < bytes.Length;)
        {
            var resourceIndex = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4));
            var payload = bytes.AsSpan(offset + 8, payloadLength).ToArray();

            frames.Add((resourceIndex, payloadLength, payload));
            offset += 8 + payloadLength;
        }

        return frames;
    }
}
