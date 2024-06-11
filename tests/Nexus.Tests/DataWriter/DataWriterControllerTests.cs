// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using System.IO.Pipelines;
using Xunit;

namespace DataWriter;

public class DataWriterControllerTests(DataWriterFixture fixture)
    : IClassFixture<DataWriterFixture>
{
    private readonly DataWriterFixture _fixture = fixture;

    [Fact]
    public async Task CanWrite()
    {
        // prepare write
        var begin = new DateTime(2020, 01, 01, 1, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2020, 01, 01, 3, 0, 0, DateTimeKind.Utc);
        var samplePeriod = TimeSpan.FromMinutes(10);
        var filePeriod = TimeSpan.FromMinutes(30);

        var catalogItems = _fixture.Catalogs
           .SelectMany(catalog => catalog.Resources!
           .SelectMany(resource => resource.Representations!
           .Select(representation => new CatalogItem(
                catalog,
                resource,
                new Representation(representation.DataType, samplePeriod: TimeSpan.FromMinutes(10)),
                Parameters: default))))
           .ToArray();

        var catalogItemRequests = catalogItems
            .Select(catalogItem => new CatalogItemRequest(catalogItem, default, default!))
            .ToArray();

        var pipes = catalogItemRequests
            .Select(catalogItemRequest => new Pipe())
            .ToArray();

        var catalogItemRequestPipeReaders = catalogItemRequests
            .Zip(pipes)
            .Select((value) => new CatalogItemRequestPipeReader(value.First, value.Second.Reader))
            .ToArray();

        var random = new Random(Seed: 1);
        var totalLength = (end - begin).Ticks / samplePeriod.Ticks;

        var expectedDatasets = pipes
            .Select(pipe => Enumerable.Range(0, (int)totalLength).Select(value => random.NextDouble()).ToArray())
            .ToArray();

        var actualDatasets = pipes
           .Select(pipe => Enumerable.Range(0, (int)totalLength).Select(value => 0.0).ToArray())
           .ToArray();

        // mock IDataWriter
        var dataWriter = Mock.Of<IDataWriter>();

        var fileNo = -1;

        Mock.Get(dataWriter)
            .Setup(s => s.OpenAsync(
                It.IsAny<DateTime>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CatalogItem[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<DateTime, TimeSpan, TimeSpan, CatalogItem[], CancellationToken>((_, _, _, _, _) =>
            {
                fileNo++;
            });

        Mock.Get(dataWriter)
            .Setup(s => s.WriteAsync(
                It.IsAny<TimeSpan>(),
                It.IsAny<WriteRequest[]>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>())
            )
            .Callback<TimeSpan, WriteRequest[], IProgress<double>, CancellationToken>((fileOffset, requests, progress, cancellationToken) =>
            {
                var fileLength = (int)(filePeriod.Ticks / samplePeriod.Ticks);
                var fileElementOffset = (int)(fileOffset.Ticks / samplePeriod.Ticks);

                foreach (var ((catalogItem, source), target) in requests.Zip(actualDatasets))
                {
                    source.Span.CopyTo(target.AsSpan(fileElementOffset + fileNo * fileLength));
                }
            })
            .Returns(Task.CompletedTask);

        // instantiate controller
        var resourceLocator = new Uri("file:///empty");

        var controller = new DataWriterController(
            dataWriter,
            resourceLocator,
            default!,
            default!,
            NullLogger<DataWriterController>.Instance);

        await controller.InitializeAsync(default!, CancellationToken.None);

        // read data
        var chunkSize = 2;

        var reading = Task.Run(async () =>
        {
            var remaining = totalLength;
            var offset = 0;

            while (remaining > 0)
            {
                var currentChunk = (int)Math.Min(remaining, chunkSize);

                foreach (var (pipe, dataset) in pipes.Zip(expectedDatasets))
                {
                    var buffer = dataset
                        .AsMemory()
                        .Slice(offset, currentChunk)
                        .Cast<double, byte>();

                    await pipe.Writer.WriteAsync(buffer);
                }

                remaining -= currentChunk;
                offset += currentChunk;
            }

            foreach (var pipe in pipes)
            {
                await pipe.Writer.CompleteAsync();
            }
        });

        // write data
        var writing = controller.WriteAsync(begin, end, samplePeriod, filePeriod, catalogItemRequestPipeReaders, default, CancellationToken.None);

        // wait for completion
        await Task.WhenAll(writing, reading);

        // assert
        var begin1 = new DateTime(2020, 01, 01, 1, 0, 0, DateTimeKind.Utc);
        Mock.Get(dataWriter).Verify(dataWriter => dataWriter.OpenAsync(begin1, filePeriod, samplePeriod, catalogItems, default), Times.Once);

        var begin2 = new DateTime(2020, 01, 01, 1, 30, 0, DateTimeKind.Utc);
        Mock.Get(dataWriter).Verify(dataWriter => dataWriter.OpenAsync(begin2, filePeriod, samplePeriod, catalogItems, default), Times.Once);

        var begin3 = new DateTime(2020, 01, 01, 2, 0, 0, DateTimeKind.Utc);
        Mock.Get(dataWriter).Verify(dataWriter => dataWriter.OpenAsync(begin3, filePeriod, samplePeriod, catalogItems, default), Times.Once);

        var begin4 = new DateTime(2020, 01, 01, 2, 30, 0, DateTimeKind.Utc);
        Mock.Get(dataWriter).Verify(dataWriter => dataWriter.OpenAsync(begin4, filePeriod, samplePeriod, catalogItems, default), Times.Once);

        var begin5 = new DateTime(2020, 01, 01, 3, 00, 0, DateTimeKind.Utc);
        Mock.Get(dataWriter).Verify(dataWriter => dataWriter.OpenAsync(begin5, filePeriod, samplePeriod, catalogItems, default), Times.Never);

        Mock.Get(dataWriter).Verify(dataWriter => dataWriter.CloseAsync(default), Times.Exactly(4));

        foreach (var (expected, actual) in expectedDatasets.Zip(actualDatasets))
        {
            Assert.True(expected.SequenceEqual(actual));
        }
    }
}
