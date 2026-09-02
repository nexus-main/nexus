// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nexus.Core;
using Nexus.Core.V2;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using System.IO.Pipelines;
using Xunit;

namespace Services;

public class DataStreamSessionManagerTests
{
    [Fact]
    public async Task StartsReadingOnlyAfterAllChannelsAttach()
    {
        var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var end = begin.AddSeconds(1);
        var samplePeriod = TimeSpan.FromSeconds(1);
        var catalog = new ResourceCatalog("/A");
        var representation = new Representation(NexusDataType.FLOAT64, samplePeriod);
        var request1 = new CatalogItemRequest(new CatalogItem(catalog, new Resource("X"), representation, default), default, default!);
        var request2 = new CatalogItemRequest(new CatalogItem(catalog, new Resource("Y"), representation, default), default, default!);
        var pipe1 = new Pipe();
        var pipe2 = new Pipe();
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        var controller = Mock.Of<IDataSourceController>();

        Mock.Get(controller)
            .Setup(current => current.ReadAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CatalogItemRequestPipeWriter[]>(),
                It.IsAny<ReadDataHandler>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                Interlocked.Increment(ref callCount);
                readStarted.SetResult();
            })
            .Returns(Task.CompletedTask);

        var memoryTracker = Mock.Of<IMemoryTracker>();

        Mock.Get(memoryTracker)
            .Setup(current => current.RegisterAllocationAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync<long, long, CancellationToken, IMemoryTracker, AllocationRegistration>((_, maximum, _) => new AllocationRegistration(memoryTracker, maximum));

        var session = new DataStreamSession(
            Guid.NewGuid(),
            begin,
            end,
            samplePeriod,
            [
                new DataReadingGroup(controller,
                [
                    new CatalogItemRequestPipeWriter(request1, pipe1.Writer),
                    new CatalogItemRequestPipeWriter(request2, pipe2.Writer)
                ])
            ],
            [
                new DataStreamChannel(Guid.NewGuid(), request1.Item.ToPath(), pipe1.Reader, sizeof(double)),
                new DataStreamChannel(Guid.NewGuid(), request2.Item.ToPath(), pipe2.Reader, sizeof(double))
            ],
            default!,
            memoryTracker,
            default,
            NullLogger<DataSourceController>.Instance);

        var manager = new DataStreamSessionManager();
        manager.Register(session);

        var firstLease = manager.Attach(session.Id, session.Channels[0].Id);

        Assert.NotNull(firstLease);
        Assert.Null(manager.Attach(session.Id, session.Channels[0].Id));

        await Task.Delay(100);

        Assert.Equal(0, Volatile.Read(ref callCount));

        var secondLease = manager.Attach(session.Id, session.Channels[1].Id);

        Assert.NotNull(secondLease);

        var completedTask = await Task.WhenAny(readStarted.Task, Task.Delay(5000));

        Assert.Same(readStarted.Task, completedTask);
        Assert.Equal(1, Volatile.Read(ref callCount));

        await session.CompleteChannelAsync(session.Channels[0].Id, faulted: false);
        await session.CompleteChannelAsync(session.Channels[1].Id, faulted: false);
    }

    [Fact]
    public async Task RecordsChannelFault()
    {
        var (manager, session) = CreateSession();

        Assert.NotNull(manager.Attach(session.Id, session.Channels[0].Id));
        Assert.NotNull(manager.Attach(session.Id, session.Channels[1].Id));

        var faultException = new Exception("Test fault");
        await session.CompleteChannelAsync(session.Channels[0].Id, faulted: true, faultException);
        await session.CompleteChannelAsync(session.Channels[1].Id, faulted: false);

        var status = manager.GetStatus(session.Id);

        Assert.NotNull(status);
        Assert.Equal(BatchStreamSessionState.Faulted, status!.State);
        Assert.Equal(session.Channels[0].Id, status.FaultedChannelId);
        Assert.Equal(session.Channels[0].ResourcePath, status.FaultedChannelResourcePath);
        Assert.Equal("Test fault", status.FaultReason);
    }

    [Fact]
    public async Task RecordsSourceReadError()
    {
        var memoryTracker = Mock.Of<IMemoryTracker>();

        Mock.Get(memoryTracker)
            .Setup(current => current.RegisterAllocationAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("Out of memory"));

        var (manager, session) = CreateSession(memoryTracker);

        Assert.NotNull(manager.Attach(session.Id, session.Channels[0].Id));
        Assert.NotNull(manager.Attach(session.Id, session.Channels[1].Id));

        await Task.Delay(500);

        await session.CompleteChannelAsync(session.Channels[0].Id, faulted: false);
        await session.CompleteChannelAsync(session.Channels[1].Id, faulted: false);

        var status = manager.GetStatus(session.Id);

        Assert.NotNull(status);
        Assert.Equal(BatchStreamSessionState.Faulted, status!.State);
        Assert.Null(status.FaultedChannelId);
        Assert.Contains("Out of memory", status.FaultReason);
    }

    [Fact]
    public async Task ReturnsCompletedOnSuccess()
    {
        var (manager, session) = CreateSession();

        Assert.NotNull(manager.Attach(session.Id, session.Channels[0].Id));
        Assert.NotNull(manager.Attach(session.Id, session.Channels[1].Id));

        await session.CompleteChannelAsync(session.Channels[0].Id, faulted: false);
        await session.CompleteChannelAsync(session.Channels[1].Id, faulted: false);

        var status = manager.GetStatus(session.Id);

        Assert.NotNull(status);
        Assert.Equal(BatchStreamSessionState.Completed, status!.State);
        Assert.Null(status.FaultedChannelId);
        Assert.Null(status.FaultReason);
    }

    [Fact]
    public async Task StatusAvailableAfterCompletion()
    {
        var (manager, session) = CreateSession();

        Assert.NotNull(manager.Attach(session.Id, session.Channels[0].Id));
        Assert.NotNull(manager.Attach(session.Id, session.Channels[1].Id));

        await session.CompleteChannelAsync(session.Channels[0].Id, faulted: false);
        await session.CompleteChannelAsync(session.Channels[1].Id, faulted: false);

        var status = manager.GetStatus(session.Id);

        Assert.NotNull(status);
    }

    [Fact]
    public void GetStatusReturnsNullForUnknownSession()
    {
        var manager = new DataStreamSessionManager();

        var status = manager.GetStatus(Guid.NewGuid());

        Assert.Null(status);
    }

    private static (DataStreamSessionManager Manager, DataStreamSession Session) CreateSession(
        IMemoryTracker? memoryTracker = default)
    {
        var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var end = begin.AddSeconds(1);
        var samplePeriod = TimeSpan.FromSeconds(1);
        var catalog = new ResourceCatalog("/A");
        var representation = new Representation(NexusDataType.FLOAT64, samplePeriod);
        var request1 = new CatalogItemRequest(new CatalogItem(catalog, new Resource("X"), representation, default), default, default!);
        var request2 = new CatalogItemRequest(new CatalogItem(catalog, new Resource("Y"), representation, default), default, default!);
        var pipe1 = new Pipe();
        var pipe2 = new Pipe();
        var controller = Mock.Of<IDataSourceController>();

        Mock.Get(controller)
            .Setup(current => current.ReadAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CatalogItemRequestPipeWriter[]>(),
                It.IsAny<ReadDataHandler>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var useDefaultMemoryTracker = memoryTracker is null;
        memoryTracker ??= Mock.Of<IMemoryTracker>();

        if (useDefaultMemoryTracker)
        {
            Mock.Get(memoryTracker)
                .Setup(current => current.RegisterAllocationAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync<long, long, CancellationToken, IMemoryTracker, AllocationRegistration>((_, maximum, _) => new AllocationRegistration(memoryTracker, maximum));
        }

        var session = new DataStreamSession(
            Guid.NewGuid(),
            begin,
            end,
            samplePeriod,
            [
                new DataReadingGroup(controller,
                [
                    new CatalogItemRequestPipeWriter(request1, pipe1.Writer),
                    new CatalogItemRequestPipeWriter(request2, pipe2.Writer)
                ])
            ],
            [
                new DataStreamChannel(Guid.NewGuid(), request1.Item.ToPath(), pipe1.Reader, sizeof(double)),
                new DataStreamChannel(Guid.NewGuid(), request2.Item.ToPath(), pipe2.Reader, sizeof(double))
            ],
            default!,
            memoryTracker,
            default,
            NullLogger<DataSourceController>.Instance);

        var manager = new DataStreamSessionManager();
        manager.Register(session);

        return (manager, session);
    }
}
