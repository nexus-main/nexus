// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nexus.Core;
using Nexus.Core.V1;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using Nexus.Sources;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace DataSource;

public class DataSourceControllerTests(DataSourceControllerFixture fixture)
    : IClassFixture<DataSourceControllerFixture>
{
    private readonly DataSourceControllerFixture _fixture = fixture;

    [Fact]
    internal async Task CanGetAvailability()
    {
        using var controller = new DataSourceController(
            [_fixture.DataSource1, _fixture.DataSource2],
            [_fixture.Registration1, _fixture.Registration2],
            default!,
            default!,
            default!,
            default!,
            NullLogger<DataSourceController>.Instance);

        await controller.InitializeAsync(default!, new LoggerFactory(), CancellationToken.None);

        var catalogId = Sample.LocalCatalogId;
        var begin = new DateTime(2020, 01, 01, 00, 00, 00, DateTimeKind.Utc);
        var end = new DateTime(2020, 01, 03, 00, 00, 00, DateTimeKind.Utc);
        var actual = await controller.GetAvailabilityAsync(catalogId, begin, end, TimeSpan.FromDays(1), CancellationToken.None);

        var expectedData = new double[]
        {
            1,
            1
        };

        Assert.True(expectedData.SequenceEqual(actual.Data));
    }

    [Fact]
    public async Task CanGetTimeRange()
    {
        using var controller = new DataSourceController(
            [_fixture.DataSource1, _fixture.DataSource2],
            [_fixture.Registration1, _fixture.Registration2],
            default!,
            default!,
            default!,
            default!,
            NullLogger<DataSourceController>.Instance);

        await controller.InitializeAsync(default!, new LoggerFactory(), CancellationToken.None);

        var catalogId = Sample.LocalCatalogId;
        var actual = await controller.GetTimeRangeAsync(catalogId, CancellationToken.None);

        Assert.Equal(DateTime.MinValue, actual.Begin);
        Assert.Equal(DateTime.MaxValue, actual.End);
    }

    [Fact]
    public async Task CanRead()
    {
        using var controller = new DataSourceController(
            [_fixture.DataSource1, _fixture.DataSource2],
            [_fixture.Registration1, _fixture.Registration2],
            default!,
            default!,
            default!,
            default!,
            NullLogger<DataSourceController>.Instance);

        await controller.InitializeAsync(new ConcurrentDictionary<string, ResourceCatalog>(), new LoggerFactory(), CancellationToken.None);

        var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2020, 01, 02, 0, 0, 1, DateTimeKind.Utc);
        var samplePeriod = TimeSpan.FromSeconds(1);

        // resource 1
        var resourcePath1 = $"{Sample.LocalCatalogId}/V1_renamed/1_s";
        var catalogItem1 = (await controller.GetCatalogAsync(Sample.LocalCatalogId, CancellationToken.None)).Find(resourcePath1);
        var catalogItemRequest1 = new CatalogItemRequest(catalogItem1, default, default!);

        var pipe1 = new Pipe();
        var dataWriter1 = pipe1.Writer;

        // resource 2
        var resourcePath2 = $"{Sample.LocalCatalogId}/T1/1_s";
        var catalogItem2 = (await controller.GetCatalogAsync(Sample.LocalCatalogId, CancellationToken.None)).Find(resourcePath2);
        var catalogItemRequest2 = new CatalogItemRequest(catalogItem2, default, default!);

        var pipe2 = new Pipe();
        var dataWriter2 = pipe2.Writer;

        // resource 3
        var resourcePath3 = $"{Sample.LocalCatalogId}/foo/1_s";
        var catalogItem3 = (await controller.GetCatalogAsync(Sample.LocalCatalogId, CancellationToken.None)).Find(resourcePath3);
        var catalogItemRequest3 = new CatalogItemRequest(catalogItem3, default, default!);

        var pipe3 = new Pipe();
        var dataWriter3 = pipe3.Writer;

        // combine
        var catalogItemRequestPipeWriters = new CatalogItemRequestPipeWriter[]
        {
            new(catalogItemRequest1, dataWriter1),
            new(catalogItemRequest2, dataWriter2),
            new(catalogItemRequest3, dataWriter3)
        };

        var readingGroups = new DataReadingGroup[]
        {
            new(controller, catalogItemRequestPipeWriters)
        };

        // V1
        var result1 = new double[86401];

        var writing1 = Task.Run(async () =>
        {
            var resultBuffer1 = result1.AsMemory().Cast<double, byte>();
            var stream1 = pipe1.Reader.AsStream();

            while (resultBuffer1.Length > 0)
            {
                var readBytes1 = await stream1.ReadAsync(resultBuffer1);

                if (readBytes1 == 0)
                    throw new Exception("The stream stopped early.");

                resultBuffer1 = resultBuffer1[readBytes1..];
            }
        });

        // T1
        var result2 = new double[86401];

        var writing2 = Task.Run(async () =>
        {
            var resultBuffer2 = result2.AsMemory().Cast<double, byte>();
            var stream2 = pipe2.Reader.AsStream();

            while (resultBuffer2.Length > 0)
            {
                var readBytes2 = await stream2.ReadAsync(resultBuffer2);

                if (readBytes2 == 0)
                    throw new Exception("The stream stopped early.");

                resultBuffer2 = resultBuffer2[readBytes2..];
            }
        });

        // foo
        var result3 = new double[86401];

        var writing3 = Task.Run(async () =>
        {
            var resultBuffer3 = result3.AsMemory().Cast<double, byte>();
            var stream3 = pipe3.Reader.AsStream();

            while (resultBuffer3.Length > 0)
            {
                var readBytes3 = await stream3.ReadAsync(resultBuffer3);

                if (readBytes3 == 0)
                    throw new Exception("The stream stopped early.");

                resultBuffer3 = resultBuffer3[readBytes3..];
            }
        });

        var memoryTracker = Mock.Of<IMemoryTracker>();

        Mock.Get(memoryTracker)
            .Setup(memoryTracker => memoryTracker.RegisterAllocationAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync<long, long, CancellationToken, IMemoryTracker, AllocationRegistration>((minium, maximum, _) => new AllocationRegistration(memoryTracker, actualByteCount: maximum));

        var reading = DataSourceController.ReadAsync(
            begin,
            end,
            samplePeriod,
            readingGroups,
            default!,
            memoryTracker,
            progress: default,
            NullLogger<DataSourceController>.Instance,
            CancellationToken.None);

        await Task.WhenAll(writing1, writing2, writing3);

        // /SAMPLE/LOCAL/V1/1_s
        Assert.Equal(6.5, result1[0], precision: 1);
        Assert.Equal(6.7, result1[10 * 60 + 1], precision: 1);
        Assert.Equal(7.9, result1[01 * 60 * 60 + 2], precision: 1);
        Assert.Equal(8.1, result1[02 * 60 * 60 + 3], precision: 1);
        Assert.Equal(7.5, result1[10 * 60 * 60 + 4], precision: 1);

        // /SAMPLE/LOCAL/T1/1_s
        Assert.Equal(6.5, result2[0], precision: 1);
        Assert.Equal(6.7, result2[10 * 60 + 1], precision: 1);
        Assert.Equal(7.9, result2[01 * 60 * 60 + 2], precision: 1);
        Assert.Equal(8.1, result2[02 * 60 * 60 + 3], precision: 1);
        Assert.Equal(7.5, result2[10 * 60 * 60 + 4], precision: 1);

        // /SAMPLE/LOCAL/foo/1_s
        Assert.Equal(1, result3[0]);
        Assert.Equal(1, result3[10 * 60 + 1]);
        Assert.Equal(1, result3[01 * 60 * 60 + 2]);
        Assert.Equal(1, result3[02 * 60 * 60 + 3]);
        Assert.Equal(1, result3[10 * 60 * 60 + 4]);
    }

    [Fact]
    public async Task CanReadAsStream()
    {
        using var controller = new DataSourceController(
            [_fixture.DataSource1, _fixture.DataSource2],
            [_fixture.Registration1, _fixture.Registration2],
            default!,
            default!,
            default!,
            default!,
            NullLogger<DataSourceController>.Instance);

        await controller.InitializeAsync(new ConcurrentDictionary<string, ResourceCatalog>(), new LoggerFactory(), CancellationToken.None);

        var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2020, 01, 02, 0, 0, 1, DateTimeKind.Utc);
        var resourcePath = "/SAMPLE/LOCAL/T1/1_s";
        var catalogItem = (await controller.GetCatalogAsync(Sample.LocalCatalogId, CancellationToken.None)).Find(resourcePath);
        var catalogItemRequest = new CatalogItemRequest(catalogItem, default, default!);

        var memoryTracker = Mock.Of<IMemoryTracker>();

        Mock.Get(memoryTracker)
            .Setup(memoryTracker => memoryTracker.RegisterAllocationAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync<long, long, CancellationToken, IMemoryTracker, AllocationRegistration>((minium, maximum, _) => new AllocationRegistration(memoryTracker, actualByteCount: maximum));

        var stream = controller.ReadAsStream(
            begin,
            end,
            catalogItemRequest,
            default!,
            memoryTracker,
            NullLogger<DataSourceController>.Instance,
            CancellationToken.None);

        var result = new double[86401];

        await Task.Run(async () =>
        {
            var resultBuffer = result.AsMemory().Cast<double, byte>();

            while (resultBuffer.Length > 0)
            {
                var readBytes = await stream.ReadAsync(resultBuffer);

                if (readBytes == 0)
                    throw new Exception("This should never happen.");

                resultBuffer = resultBuffer[readBytes..];
            }
        });

        Assert.Equal(86401 * sizeof(double), stream.Length);
        Assert.Equal(6.5, result[0], precision: 1);
        Assert.Equal(6.7, result[10 * 60 + 1], precision: 1);
        Assert.Equal(7.9, result[01 * 60 * 60 + 2], precision: 1);
        Assert.Equal(8.1, result[02 * 60 * 60 + 3], precision: 1);
        Assert.Equal(7.5, result[10 * 60 * 60 + 4], precision: 1);
    }

    [Fact]
    public async Task CanReadResampled()
    {
        // Arrange
        var processingService = new Mock<IProcessingService>();

        using var controller = new DataSourceController(
            [_fixture.DataSource1, _fixture.DataSource2],
            [_fixture.Registration1, _fixture.Registration2],
            default!,
            processingService.Object,
            default!,
            new DataOptions(),
            NullLogger<DataSourceController>.Instance);

        await controller.InitializeAsync(new ConcurrentDictionary<string, ResourceCatalog>(), new LoggerFactory(), CancellationToken.None);

        var begin = new DateTime(2020, 01, 01, 0, 0, 0, 200, DateTimeKind.Utc);
        var end = new DateTime(2020, 01, 01, 0, 0, 1, 700, DateTimeKind.Utc);
        var pipe = new Pipe();
        var baseItem = (await controller.GetCatalogAsync(Sample.LocalCatalogId, CancellationToken.None)).Find("/SAMPLE/LOCAL/T1/1_s");

        var item = baseItem with
        {
            Representation = new Representation(
                NexusDataType.FLOAT64,
                TimeSpan.FromMilliseconds(100),
                parameters: default,
                RepresentationKind.Resampled)
        };

        var catalogItemRequest = new CatalogItemRequest(item, baseItem, default!);

        var memoryTracker = Mock.Of<IMemoryTracker>();

        Mock.Get(memoryTracker)
            .Setup(memoryTracker => memoryTracker.RegisterAllocationAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AllocationRegistration(memoryTracker, actualByteCount: 20000));

        // Act
        await controller.ReadSingleAsync(
            begin,
            end,
            catalogItemRequest,
            pipe.Writer,
            default!,
            memoryTracker,
            new Progress<double>(),
            NullLogger<DataSourceController>.Instance,
            CancellationToken.None);

        // Assert
        processingService
            .Verify(processingService => processingService.Resample(
               NexusDataType.FLOAT64,
               It.IsAny<ReadOnlyMemory<byte>>(),
               It.IsAny<ReadOnlyMemory<byte>>(),
               It.IsAny<Memory<double>>(),
               10,
               2), Times.Exactly(1));
    }

    [Fact]
    public async Task CanReadCached()
    {
        // Arrange
        var expected1 = new double[] { 65, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 101 };
        var expected2 = new double[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25 };

        var begin = new DateTime(2020, 01, 01, 23, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2020, 01, 03, 1, 0, 0, DateTimeKind.Utc);
        var samplePeriod = TimeSpan.FromHours(1);

        var representationBase1 = new Representation(NexusDataType.INT32, TimeSpan.FromMinutes(30), parameters: default, RepresentationKind.Original);
        var representation1 = new Representation(NexusDataType.INT32, TimeSpan.FromHours(1), parameters: default, RepresentationKind.Mean);
        var representation2 = new Representation(NexusDataType.INT32, TimeSpan.FromHours(1), parameters: default, RepresentationKind.Original);

        var resource1 = new ResourceBuilder("id1")
            .AddRepresentation(representationBase1)
            .Build();

        var resource2 = new ResourceBuilder("id2")
            .AddRepresentation(representation2)
            .Build();

        var catalog = new ResourceCatalogBuilder("/C1")
            .AddResource(resource1)
            .AddResource(resource2)
            .Build();

        // update immutable catalog and resources with mandatory properties
        catalog = catalog
            .EnsureAndSanitizeMandatoryProperties(pipelinePosition: 0, dataSources: Array.Empty<IDataSource>());

        resource1 = catalog.Resources![0];
        resource2 = catalog.Resources![1];

        // continue
        var baseItem1 = new CatalogItem(catalog, resource1, representationBase1, Parameters: default);
        var catalogItem1 = new CatalogItem(catalog, resource1, representation1, Parameters: default);
        var catalogItem2 = new CatalogItem(catalog, resource2, representation2, Parameters: default);

        var request1 = new CatalogItemRequest(catalogItem1, baseItem1, default!);
        var request2 = new CatalogItemRequest(catalogItem2, default, default!);

        var pipe1 = new Pipe();
        var pipe2 = new Pipe();

        var catalogItemRequestPipeWriters = new[]
        {
            new CatalogItemRequestPipeWriter(request1, pipe1.Writer),
            new CatalogItemRequestPipeWriter(request2, pipe2.Writer)
        };

        /* IDataSource */
        var dataSource = Mock.Of<IDataSource<object?>>();

        Mock.Get(dataSource)
           .Setup(dataSource => dataSource.ReadAsync(
               It.IsAny<DateTime>(),
               It.IsAny<DateTime>(),
               It.IsAny<ReadRequest[]>(),
               It.IsAny<ReadDataHandler>(),
               It.IsAny<IProgress<double>>(),
               It.IsAny<CancellationToken>())
           )
           .Callback<DateTime, DateTime, ReadRequest[], ReadDataHandler, IProgress<double>, CancellationToken>(
            (currentBegin, currentEnd, requests, readDataHandler, progress, cancellationToken) =>
           {
               var request = requests[0];
               var intData = MemoryMarshal.Cast<byte, int>(request.Data.Span);

               if (request.CatalogItem.Resource.Id == catalogItem1.Resource.Id &&
                   currentBegin == begin)
               {
                   Assert.Equal(2, intData.Length);
                   intData[0] = 33; request.Status.Span[0] = 1;
                   intData[1] = 97; request.Status.Span[1] = 1;

               }
               else if (request.CatalogItem.Resource.Id == catalogItem1.Resource.Id &&
                        currentBegin == new DateTime(2020, 01, 03, 0, 0, 0, DateTimeKind.Utc))
               {
                   Assert.Equal(2, intData.Length);
                   intData[0] = 100; request.Status.Span[0] = 1;
                   intData[1] = 102; request.Status.Span[1] = 1;
               }
               else if (request.CatalogItem.Resource.Id == "id2")
               {
                   Assert.Equal(26, intData.Length);

                   for (int i = 0; i < intData.Length; i++)
                   {
                       intData[i] = i;
                       request.Status.Span[i] = 1;
                   }
               }
               else
               {
                   throw new Exception("This should never happen.");
               }
           })
           .Returns(Task.CompletedTask);

        /* IProcessingService */
        var processingService = Mock.Of<IProcessingService>();

        Mock.Get(processingService)
            .Setup(processingService => processingService.Aggregate(
               It.IsAny<NexusDataType>(),
               It.IsAny<RepresentationKind>(),
               It.IsAny<Memory<byte>>(),
               It.IsAny<ReadOnlyMemory<byte>>(),
               It.IsAny<Memory<double>>(),
               It.IsAny<int>()))
            .Callback<NexusDataType, RepresentationKind, Memory<byte>, ReadOnlyMemory<byte>, Memory<double>, int>(
            (dataType, kind, data, status, targetBuffer, blockSize) =>
            {
                Assert.Equal(NexusDataType.INT32, dataType);
                Assert.Equal(RepresentationKind.Mean, kind);
                Assert.Equal(8, data.Length);
                Assert.Equal(2, status.Length);
                Assert.Equal(1, targetBuffer.Length);
                Assert.Equal(2, blockSize);

                targetBuffer.Span[0] = (MemoryMarshal.Cast<byte, int>(data.Span)[0] + MemoryMarshal.Cast<byte, int>(data.Span)[1]) / 2.0;
            });

        /* ICacheService */
        var uncachedIntervals = new List<Interval>
        {
            new(begin, new DateTime(2020, 01, 02, 0, 0, 0, DateTimeKind.Utc)),
            new(new DateTime(2020, 01, 03, 0, 0, 0, DateTimeKind.Utc), end)
        };

        var cacheService = new Mock<ICacheService>();

        cacheService
            .Setup(cacheService => cacheService.ReadAsync(
               It.IsAny<CatalogItem>(),
               It.IsAny<DateTime>(),
               It.IsAny<Memory<double>>(),
               It.IsAny<CancellationToken>())
            )
            .Callback<CatalogItem, DateTime, Memory<double>, CancellationToken>((item, begin, targetBuffer, cancellationToken) =>
            {
                var offset = 1;
                var length = 24;
                targetBuffer.Span.Slice(offset, length).Fill(-1);
            })
            .Returns(Task.FromResult(uncachedIntervals));

        /* DataSourceController */
        var registration = new DataSourceRegistration(
            "a",
            new Uri("http://xyz"),
            default,
            default
        );

        var dataSourceController = new DataSourceController(
            [dataSource],
            [registration],
            default!,
            processingService,
            cacheService.Object,
            new DataOptions(),
            NullLogger<DataSourceController>.Instance
        );

        var catalogCache = new ConcurrentDictionary<string, ResourceCatalog>() { [catalog.Id] = catalog };

        await dataSourceController.InitializeAsync(catalogCache, new LoggerFactory(), CancellationToken.None);

        // Act
        await dataSourceController.ReadAsync(
            begin,
            end,
            samplePeriod,
            catalogItemRequestPipeWriters,
            default!,
            new Progress<double>(),
            CancellationToken.None);

        // Assert
        var actual1 = MemoryMarshal.Cast<byte, double>((await pipe1.Reader.ReadAsync()).Buffer.First.Span).ToArray();
        var actual2 = MemoryMarshal.Cast<byte, double>((await pipe2.Reader.ReadAsync()).Buffer.First.Span).ToArray();

        Assert.True(expected1.SequenceEqual(actual1));
        Assert.True(expected2.SequenceEqual(actual2));

        cacheService
            .Verify(cacheService => cacheService.UpdateAsync(
               catalogItem1,
               new DateTime(2020, 01, 01, 23, 0, 0, DateTimeKind.Utc),
               It.IsAny<Memory<double>>(),
               uncachedIntervals,
               It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task CanSetGenericContext()
    {
        // Arrange
        var expectedSettings = new GenericSourceSettings("bar");
        var genericSource = Mock.Of<IDataSource<GenericSourceSettings>>();

        var configuration = JsonSerializer
            .SerializeToElement(expectedSettings);

        var registration = new DataSourceRegistration
        (
            Type: "foo",
            ResourceLocator: default,
            Configuration: configuration
        );

        var dataSourceController = new DataSourceController(
            dataSources: [genericSource],
            registrations: [registration],
            requestConfiguration: default!,
            processingService: default!,
            cacheService: default!,
            new DataOptions(),
            NullLogger<DataSourceController>.Instance
        );

        var loggerFactory = new LoggerFactory();

        // Act
        await dataSourceController.InitializeAsync(
            catalogCache: default!,
            loggerFactory,
            CancellationToken.None
        );

        // Assert
        Mock.Get(genericSource)
            .Verify(
                x => x.SetContextAsync(
                    It.Is<DataSourceContext<GenericSourceSettings>>(x => x.SourceConfiguration == expectedSettings),
                    It.IsAny<ILogger>(),
                    It.IsAny<CancellationToken>()
                ),
                Times.Exactly(1)
            );
    }
}