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
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;
using Xunit;

namespace Services;

public class DataStreamSessionManagerTests
{
    private static readonly ClaimsPrincipal Owner = CreatePrincipal("owner");

    [Fact]
    public void EnforcesAndReleasesAdmissionLimits()
    {
        var manager = new DataStreamSessionManager(
            maximumSessionCount: 2,
            maximumSessionCountPerOwner: 1,
            maximumChannelCount: 3);
        using var first = manager.Reserve("owner", 2);

        Assert.Throws<InvalidOperationException>(() => manager.Reserve("owner", 1));

        using var second = manager.Reserve("other", 1);

        Assert.Throws<InvalidOperationException>(() => manager.Reserve("third", 1));

        first.Dispose();
        using var replacement = manager.Reserve("owner", 2);
    }

    [Fact]
    public async Task StartsReadingAfterFirstChannelAttaches()
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
                It.IsAny<CancellationToken>(),
                It.IsAny<DataSourceErrorHandling>()))
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
            "owner",
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

        var firstLease = manager.Attach(session.Id, session.Channels[0].Id, Owner);

        Assert.NotNull(firstLease);
        Assert.Null(manager.Attach(session.Id, session.Channels[0].Id, Owner));

        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref callCount));

        var secondLease = manager.Attach(session.Id, session.Channels[1].Id, Owner);

        Assert.NotNull(secondLease);
        Assert.Equal(1, Volatile.Read(ref callCount));

        await session.CompleteChannelAsync(session.Channels[0].Id, faulted: false);
        await session.CompleteChannelAsync(session.Channels[1].Id, faulted: false);
    }

    [Fact]
    public async Task RecordsChannelFault()
    {
        var (manager, session) = CreateSession();

        Assert.NotNull(manager.Attach(session.Id, session.Channels[0].Id, Owner));
        Assert.NotNull(manager.Attach(session.Id, session.Channels[1].Id, Owner));

        var faultException = new Exception("Test fault");
        await session.CompleteChannelAsync(session.Channels[0].Id, faulted: true, faultException);
        await session.CompleteChannelAsync(session.Channels[1].Id, faulted: false);

        var status = manager.GetStatus(session.Id, Owner);

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
        var reads = session.Channels.Select(channel => channel.Reader.ReadAsync().AsTask()).ToArray();

        Assert.NotNull(manager.Attach(session.Id, session.Channels[0].Id, Owner));
        Assert.NotNull(manager.Attach(session.Id, session.Channels[1].Id, Owner));

        foreach (var read in reads)
            await Assert.ThrowsAsync<InvalidOperationException>(() => read);

        var status = manager.GetStatus(session.Id, Owner);

        await session.CompleteChannelAsync(session.Channels[0].Id, faulted: false);
        await session.CompleteChannelAsync(session.Channels[1].Id, faulted: false);

        Assert.NotNull(status);
        Assert.Equal(BatchStreamSessionState.Faulted, status!.State);
        Assert.Null(status.FaultedChannelId);
        Assert.Contains("Out of memory", status.FaultReason);
    }

    [Fact]
    public async Task PublishesSourceFailureBeforeBlockedSiblingUnwinds()
    {
        var failure = new InvalidOperationException("Source failed");
        var blockedReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishBlockedRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failedController = CreateController();
        var blockedController = CreateController();

        Mock.Get(failedController)
            .Setup(current => current.ReadAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CatalogItemRequestPipeWriter[]>(),
                It.IsAny<ReadDataHandler>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<DataSourceErrorHandling>()))
            .ThrowsAsync(failure);

        Mock.Get(blockedController)
            .Setup(current => current.ReadAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CatalogItemRequestPipeWriter[]>(),
                It.IsAny<ReadDataHandler>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<DataSourceErrorHandling>()))
            .Callback(() => blockedReadStarted.SetResult())
            .Returns(finishBlockedRead.Task);

        var (manager, session) = CreateSession(controllers: [failedController, blockedController]);
        var pendingRead = session.Channels[0].Reader.ReadAsync().AsTask();

        Assert.NotNull(manager.Attach(session.Id, session.Channels[0].Id, Owner));
        await blockedReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => pendingRead.WaitAsync(TimeSpan.FromSeconds(5)));

        var status = manager.GetStatus(session.Id, Owner);
        Assert.NotNull(status);
        Assert.Contains(failure.Message, status!.FaultReason);

        finishBlockedRead.SetResult();
        await session.CompleteChannelAsync(session.Channels[0].Id, faulted: false);
    }

    [Fact]
    public async Task NoAttachTimeoutCancelsDisposesAndRemovesSession()
    {
        var timeProvider = new ControlledTimeProvider();
        var controllerDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = CreateController();

        Mock.Get(controller)
            .Setup(current => current.Dispose())
            .Callback(() => controllerDisposed.SetResult());

        var manager = new DataStreamSessionManager(
            timeProvider,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));
        var session = CreateSession(controller: controller, manager: manager).Session;
        var pendingRead = session.Channels[0].Reader.ReadAsync().AsTask();

        timeProvider.Advance(TimeSpan.FromMinutes(1));

        await Assert.ThrowsAnyAsync<Exception>(() => pendingRead.WaitAsync(TimeSpan.FromSeconds(5)));
        await controllerDisposed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(manager.Attach(session.Id, session.Channels[1].Id, Owner));

        var status = manager.GetStatus(session.Id, Owner);

        Assert.NotNull(status);
        Assert.Equal(BatchStreamSessionState.Faulted, status!.State);
        Assert.Contains("timed out", status.FaultReason);

        await WaitUntilAsync(() => timeProvider.TimerCount == 2);
        timeProvider.Advance(TimeSpan.FromMinutes(1));

        await WaitUntilAsync(() => manager.GetStatus(session.Id, Owner) is null);
    }

    [Fact]
    public async Task MaximumDurationFaultsChannelsBeforeBlockedReadUnwinds()
    {
        var timeProvider = new ControlledTimeProvider();
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controllerDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = CreateController();

        Mock.Get(controller)
            .Setup(current => current.ReadAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CatalogItemRequestPipeWriter[]>(),
                It.IsAny<ReadDataHandler>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<DataSourceErrorHandling>()))
            .Callback(() => readStarted.SetResult())
            .Returns(finishRead.Task);

        Mock.Get(controller)
            .Setup(current => current.Dispose())
            .Callback(() => controllerDisposed.SetResult());

        var manager = new DataStreamSessionManager(
            timeProvider,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(2));
        var session = CreateSession(controller: controller, manager: manager).Session;
        var pendingRead = session.Channels[0].Reader.ReadAsync().AsTask();

        Assert.NotNull(manager.Attach(session.Id, session.Channels[0].Id, Owner));
        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        timeProvider.Advance(TimeSpan.FromMinutes(2));

        await Assert.ThrowsAnyAsync<Exception>(() => pendingRead.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Null(manager.Attach(session.Id, session.Channels[1].Id, Owner));

        var status = manager.GetStatus(session.Id, Owner);
        Assert.NotNull(status);
        Assert.Equal(BatchStreamSessionState.Faulted, status!.State);
        Assert.Contains("maximum duration", status.FaultReason);
        Assert.False(controllerDisposed.Task.IsCompleted);

        finishRead.SetResult();
        await controllerDisposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ControllerIsDisposedOnlyAfterBlockedReadUnwinds()
    {
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controllerDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = CreateController();

        Mock.Get(controller)
            .Setup(current => current.ReadAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CatalogItemRequestPipeWriter[]>(),
                It.IsAny<ReadDataHandler>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<DataSourceErrorHandling>()))
            .Callback(() => readStarted.SetResult())
            .Returns(finishRead.Task);

        Mock.Get(controller)
            .Setup(current => current.Dispose())
            .Callback(() => controllerDisposed.SetResult());

        var (manager, session) = CreateSession(controller: controller);

        Assert.NotNull(manager.Attach(session.Id, session.Channels[0].Id, Owner));
        Assert.NotNull(manager.Attach(session.Id, session.Channels[1].Id, Owner));
        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var completing = session.CompleteChannelAsync(
            session.Channels[0].Id,
            faulted: true,
            new InvalidOperationException("Channel failed"));

        Assert.False(controllerDisposed.Task.IsCompleted);

        finishRead.SetResult();

        await completing.WaitAsync(TimeSpan.FromSeconds(5));
        await controllerDisposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ReturnsCompletedOnSuccess()
    {
        var (manager, session) = CreateSession();

        Assert.NotNull(manager.Attach(session.Id, session.Channels[0].Id, Owner));
        Assert.NotNull(manager.Attach(session.Id, session.Channels[1].Id, Owner));

        await session.CompleteChannelAsync(session.Channels[0].Id, faulted: false);
        await session.CompleteChannelAsync(session.Channels[1].Id, faulted: false);

        var status = manager.GetStatus(session.Id, Owner);

        Assert.NotNull(status);
        Assert.Equal(BatchStreamSessionState.Completed, status!.State);
        Assert.Null(status.FaultedChannelId);
        Assert.Null(status.FaultReason);
    }

    [Fact]
    public async Task StatusAvailableAfterCompletion()
    {
        var (manager, session) = CreateSession();

        Assert.NotNull(manager.Attach(session.Id, session.Channels[0].Id, Owner));
        Assert.NotNull(manager.Attach(session.Id, session.Channels[1].Id, Owner));

        await session.CompleteChannelAsync(session.Channels[0].Id, faulted: false);
        await session.CompleteChannelAsync(session.Channels[1].Id, faulted: false);

        var status = manager.GetStatus(session.Id, Owner);

        Assert.NotNull(status);
    }

    [Fact]
    public void GetStatusReturnsNullForUnknownSession()
    {
        var manager = new DataStreamSessionManager();

        var status = manager.GetStatus(Guid.NewGuid(), Owner);

        Assert.Null(status);
    }

    [Fact]
    public void ForeignPrincipalCannotAccessSession()
    {
        var (manager, session) = CreateSession();
        var foreign = CreatePrincipal("foreign");

        Assert.Null(manager.Attach(session.Id, session.Channels[0].Id, foreign));
        Assert.Null(manager.GetStatus(session.Id, foreign));
        Assert.NotNull(manager.GetStatus(session.Id, Owner));
    }

    private static (DataStreamSessionManager Manager, DataStreamSession Session) CreateSession(
        IMemoryTracker? memoryTracker = default,
        IDataSourceController? controller = default,
        DataStreamSessionManager? manager = default,
        IDataSourceController[]? controllers = default)
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
        controllers ??= [controller ?? CreateController()];

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
            "owner",
            begin,
            end,
            samplePeriod,
            controllers.Length == 1
                ? [new DataReadingGroup(controllers[0],
                    [
                        new CatalogItemRequestPipeWriter(request1, pipe1.Writer),
                        new CatalogItemRequestPipeWriter(request2, pipe2.Writer)
                    ])]
                :
                [
                    new DataReadingGroup(controllers[0], [new CatalogItemRequestPipeWriter(request1, pipe1.Writer)]),
                    new DataReadingGroup(controllers[1], [new CatalogItemRequestPipeWriter(request2, pipe2.Writer)])
                ],
            [
                new DataStreamChannel(Guid.NewGuid(), request1.Item.ToPath(), pipe1.Reader, sizeof(double)),
                new DataStreamChannel(Guid.NewGuid(), request2.Item.ToPath(), pipe2.Reader, sizeof(double))
            ],
            default!,
            memoryTracker,
            default,
            NullLogger<DataSourceController>.Instance);

        manager ??= new DataStreamSessionManager();
        manager.Register(session);

        return (manager, session);
    }

    private static ClaimsPrincipal CreatePrincipal(string subject)
    {
        return new ClaimsPrincipal(new ClaimsIdentity([new Claim(Claims.Subject, subject)]));
    }

    private static IDataSourceController CreateController()
    {
        var controller = Mock.Of<IDataSourceController>();

        Mock.Get(controller)
            .Setup(current => current.ReadAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CatalogItemRequestPipeWriter[]>(),
                It.IsAny<ReadDataHandler>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<DataSourceErrorHandling>()))
            .Returns(Task.CompletedTask);

        return controller;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!condition())
            await Task.Delay(1, cts.Token);
    }

    private sealed class ControlledTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ControlledTimer> _timers = [];
        private DateTimeOffset _utcNow = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public int TimerCount
        {
            get
            {
                lock (_gate)
                    return _timers.Count;
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ControlledTimer(this, callback, state, _utcNow + dueTime, period);

            lock (_gate)
                _timers.Add(timer);

            return timer;
        }

        public void Advance(TimeSpan duration)
        {
            ControlledTimer[] timers;

            lock (_gate)
            {
                _utcNow += duration;
                timers = _timers.Where(timer => timer.IsDue(_utcNow)).ToArray();
            }

            foreach (var timer in timers)
                timer.Fire(_utcNow);
        }

        private sealed class ControlledTimer(
            ControlledTimeProvider provider,
            TimerCallback callback,
            object? state,
            DateTimeOffset dueAt,
            TimeSpan period) : ITimer
        {
            private bool _disposed;

            public bool IsDue(DateTimeOffset now) => !_disposed && dueAt <= now;

            public bool Change(TimeSpan dueTime, TimeSpan newPeriod)
            {
                dueAt = provider._utcNow + dueTime;
                period = newPeriod;
                return !_disposed;
            }

            public void Fire(DateTimeOffset now)
            {
                if (_disposed)
                    return;

                if (period == Timeout.InfiniteTimeSpan)
                    _disposed = true;
                else
                    dueAt = now + period;

                callback(state);
            }

            public void Dispose()
            {
                _disposed = true;
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
