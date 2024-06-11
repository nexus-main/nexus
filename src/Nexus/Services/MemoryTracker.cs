// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.Extensions.Options;
using Nexus.Core;

namespace Nexus.Services;

internal class AllocationRegistration(
    IMemoryTracker tracker,
    long actualByteCount) : IDisposable
{
    private bool _disposedValue;
    private readonly IMemoryTracker _tracker = tracker;

    public long ActualByteCount { get; } = actualByteCount;

    public void Dispose()
    {
        if (!_disposedValue)
        {
            _tracker.UnregisterAllocation(this);
            _disposedValue = true;
        }
    }
}

internal interface IMemoryTracker
{
    Task<AllocationRegistration> RegisterAllocationAsync(long minimumByteCount, long maximumByteCount, CancellationToken cancellationToken);
    void UnregisterAllocation(AllocationRegistration allocationRegistration);
}

internal class MemoryTracker : IMemoryTracker
{
    private long _consumedBytes;
    private readonly DataOptions _dataOptions;
    private readonly List<SemaphoreSlim> _retrySemaphores = [];
    private readonly ILogger<IMemoryTracker> _logger;

    public MemoryTracker(IOptions<DataOptions> dataOptions, ILogger<IMemoryTracker> logger)
    {
        _dataOptions = dataOptions.Value;
        _logger = logger;

        _ = Task.Run(MonitorFullGC);
    }

    internal int Factor { get; set; } = 8;

    public async Task<AllocationRegistration> RegisterAllocationAsync(long minimumByteCount, long maximumByteCount, CancellationToken cancellationToken)
    {
        if (minimumByteCount > _dataOptions.TotalBufferMemoryConsumption)
            throw new Exception("The requested minimum byte count is greater than the total buffer memory consumption parameter.");

        var myRetrySemaphore = default(SemaphoreSlim);

        // loop until registration is successful
        while (true)
        {
            // get exclusive access to _consumedBytes and _retrySemaphores
            lock (this)
            {
                var fractionOfRemainingBytes = _consumedBytes >= _dataOptions.TotalBufferMemoryConsumption
                    ? 0
                    : (_dataOptions.TotalBufferMemoryConsumption - _consumedBytes) / Factor /* normal = 8, tests = 2 */;

                long actualByteCount = 0;

                if (fractionOfRemainingBytes >= maximumByteCount)
                    actualByteCount = maximumByteCount;

                else if (fractionOfRemainingBytes >= minimumByteCount)
                    actualByteCount = fractionOfRemainingBytes;

                // success
                if (actualByteCount >= minimumByteCount)
                {
                    // remove semaphore from list
                    if (myRetrySemaphore is not null)
                        _retrySemaphores.Remove(myRetrySemaphore);

                    _logger.LogTrace("Allocate {ByteCount} bytes ({MegaByteCount} MB)", actualByteCount, actualByteCount / 1024 / 1024);
                    SetConsumedBytesAndTriggerWaitingTasks(actualByteCount);

                    return new AllocationRegistration(this, actualByteCount);
                }

                // failure
                else
                {
                    // create retry semaphore if not already done
                    if (myRetrySemaphore is null)
                    {
                        myRetrySemaphore = new SemaphoreSlim(initialCount: 0, maxCount: 1);
                        _retrySemaphores.Add(myRetrySemaphore);
                    }
                }
            }

            // wait until _consumedBytes changes
            _logger.LogTrace("Wait until {ByteCount} bytes ({MegaByteCount} MB) are available", minimumByteCount, minimumByteCount / 1024 / 1024);
            await myRetrySemaphore.WaitAsync(timeout: TimeSpan.FromMinutes(1), cancellationToken);
        }
    }

    public void UnregisterAllocation(AllocationRegistration allocationRegistration)
    {
        // get exclusive access to _consumedBytes and _retrySemaphores
        lock (this)
        {
            _logger.LogTrace("Release {ByteCount} bytes ({MegaByteCount} MB)", allocationRegistration.ActualByteCount, allocationRegistration.ActualByteCount / 1024 / 1024);
            SetConsumedBytesAndTriggerWaitingTasks(-allocationRegistration.ActualByteCount);
        }
    }

    private void SetConsumedBytesAndTriggerWaitingTasks(long difference)
    {
        _consumedBytes += difference;

        // allow all other waiting tasks to continue
        foreach (var retrySemaphore in _retrySemaphores)
        {
            if (retrySemaphore.CurrentCount == 0)
                retrySemaphore.Release();
        }

        _logger.LogTrace("{ByteCount} bytes ({MegaByteCount} MB) are currently in use", _consumedBytes, _consumedBytes / 1024 / 1024);
    }

    private void MonitorFullGC()
    {
        _logger.LogDebug("Register for full GC notifications");
        GC.RegisterForFullGCNotification(1, 1);

        while (true)
        {
            var status = GC.WaitForFullGCApproach();

            if (status == GCNotificationStatus.Succeeded)
                _logger.LogDebug("Full GC is approaching");

            status = GC.WaitForFullGCComplete();

            if (status == GCNotificationStatus.Succeeded)
                _logger.LogDebug("Full GC has completed");
        }
    }
}
