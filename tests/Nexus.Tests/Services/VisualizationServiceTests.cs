// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text.Json;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Nexus.Core;
using Nexus.Core.V1;
using Nexus.Core.V2;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using Nexus.Tests;
using Nexus.Utilities;
using Xunit;

namespace Services;

public class VisualizationServiceTests
{
    [Fact]
    public void ViewPlanFixedSpanHasSameStrideAtEveryAlignment()
    {
        const long span = 819200;

        foreach (long origin in new[] { 0L, DateTime.MaxValue.Ticks - span - 2048 })
        for (int shift = 0; shift < 2048; shift++)
        {
            long begin = origin + shift;
            var plan = new VisualizationService.ViewPlan(begin, begin + span, 4000);
            Assert.Equal(2048, plan.Stride);
        }
    }

    [Theory]
    [InlineData(4000, 1)]
    [InlineData(4001, 8)]
    public void ViewPlanPreservesRawBoundary(int span, long expectedStride)
    {
        foreach (long begin in new[] { 0L, 1L, 4095L, DateTime.MaxValue.Ticks - span })
        {
            var plan = new VisualizationService.ViewPlan(begin, begin + span, 4000);
            Assert.Equal(begin, plan.Begin);
            Assert.Equal(begin + span, plan.End);
            Assert.Equal(expectedStride, plan.Stride);
        }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(16)]
    [InlineData(4000)]
    [InlineData(32768)]
    public void ViewPlanCapsPointsAndChoosesSmallestPermittedBinaryStride(int maxPoints)
    {
        foreach (long span in new[] { 1L, maxPoints, maxPoints + 1L, 819200L, DateTime.MaxValue.Ticks })
        foreach (long begin in new[] { 0L, (DateTime.MaxValue.Ticks - span) / 2, DateTime.MaxValue.Ticks - span })
        {
            long end = begin + span;
            var plan = new VisualizationService.ViewPlan(begin, end, maxPoints);
            Assert.True(plan.Stride > 0 && (plan.Stride & (plan.Stride - 1)) == 0);
            long buckets = (end - 1) / plan.Stride - begin / plan.Stride + 1;
            Assert.InRange(buckets * (plan.Stride == 1 ? 1 : 5), 1, maxPoints);

            if (span <= maxPoints)
            {
                Assert.Equal(1, plan.Stride);
                continue;
            }

            int capacity = maxPoints / 5;
            long previousStride = plan.Stride / 2;

            if (capacity >= 2)
            {
                Assert.True((span - 2) / plan.Stride + 2 <= capacity);
                Assert.True((span - 2) / previousStride + 2 > capacity);
            }
            else
                Assert.True((end - 1) / previousStride - begin / previousStride + 1 > capacity);
        }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(9)]
    public void ViewPlanSingleSummaryBudgetRetainsAlignmentException(int maxPoints)
    {
        Assert.Equal(16, new VisualizationService.ViewPlan(0, 10, maxPoints).Stride);
        Assert.Equal(32, new VisualizationService.ViewPlan(15, 25, maxPoints).Stride);
        long boundary = 1L << 61;
        Assert.Equal(1L << 62, new VisualizationService.ViewPlan(boundary - 1, boundary + 9, maxPoints).Stride);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(16)]
    [InlineData(4000)]
    [InlineData(32768)]
    public void ViewPlanCoarseTransitionsAreAtMostTwofold(int maxPoints)
    {
        int capacity = maxPoints / 5;

        for (long stride = 2; stride <= 1L << 40; stride *= 2)
        {
            long span = (capacity - 1) * stride + 1;

            if (span <= maxPoints)
                continue;

            long begin = DateTime.MaxValue.Ticks - span - 1;
            var before = new VisualizationService.ViewPlan(begin, begin + span, maxPoints);
            var after = new VisualizationService.ViewPlan(begin, begin + span + 1, maxPoints);
            Assert.Equal(stride, before.Stride);
            Assert.Equal(before.Stride * 2, after.Stride);
        }
    }

    [Theory]
    [InlineData(400, 512, 0)]
    [InlineData(400, 512, 1)]
    [InlineData(100, 2048, 0)]
    [InlineData(100, 2048, 1)]
    [InlineData(25, 8192, 0)]
    [InlineData(25, 8192, 1)]
    public async Task IntermediateBinaryStridesOnlyReadRawViewBoundaries(int maxPoints, long stride, int shift)
    {
        using var fixture = new Fixture { Delay = TimeSpan.Zero };
        await fixture.RunAsync(fixture.Request([fixture.View("main", 0, 65536, 100)]));
        int samples = fixture.Samples;
        long origin = fixture.Begin.Ticks / TimeSpan.TicksPerSecond;
        int begin = 8192 + (int)((256 - origin % 256) % 256) + shift;
        int end = begin + 32768;
        var plan = new VisualizationService.ViewPlan(origin + begin, origin + end, maxPoints);
        Assert.Equal(stride, plan.Stride);
        var rows = await fixture.RunAsync(fixture.Request([fixture.View("detail", begin, end, maxPoints)]));
        Assert.Equal(2, rows.Last().Kind);
        Assert.Equal(shift == 0 ? 0 : 2 * 256 * 2, fixture.Samples - samples);
        var expected = new List<VisualizationPoint>();

        for (long start = plan.Begin; start < plan.End;)
        {
            long stop = Math.Min(plan.End, (start / stride + 1) * stride);
            var values = Enumerable.Range(0, (int)(stop - start)).Select(i => (float)((start - origin + i) % 1000)).ToArray();
            expected.AddRange(VisualizationReduction.Project(VisualizationReduction.Summarize(values, start)));
            start = stop;
        }

        var data = rows.Where(row => row.Kind == 0).ToArray();
        Assert.Equal(2, data.Length);

        foreach (var row in data)
        {
            Assert.InRange(row.Values.Length, 1, maxPoints);
            Assert.Equal(expected.Select(point => point.Index - origin), row.Indices);
            Assert.Equal(expected.Select(point => point.Value + row.Resource * 2000), row.Values);
        }
    }

    [Theory]
    [InlineData(1000, true)]
    [InlineData(2, false)]
    public async Task OverviewInvalidThresholdMatchesOnColdAndWarmCache(int dropoutPeriod, bool hasFinite)
    {
        using var fixture = new Fixture
        {
            Delay = TimeSpan.Zero,
            SampleValue = index => index % dropoutPeriod == 0 ? float.NaN : (float)(index % 1000)
        };
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);

        Row[]? cold = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var rows = await fixture.RunAsync(request);
            Assert.Equal(2, rows.Last().Kind);
            var data = rows.Where(row => row.Kind == 0).ToArray();
            Assert.Equal(2, data.Length);

            foreach (var row in data)
            {
                Assert.InRange(row.Values.Length, 1, 100);
                Assert.Equal(hasFinite, row.Values.Any(float.IsFinite));
                Assert.Contains(row.Values, float.IsNaN);
                if (cold is not null)
                {
                    Assert.Equal(cold[row.Resource].Indices, row.Indices);
                    Assert.Equal(cold[row.Resource].Values, row.Values);
                }
            }
            cold = data;
        }

        Assert.Equal(65536 * 2, fixture.Samples);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingBuildOwnerDoesNotRestartOtherConsumersScan(bool disableCache)
    {
        using var fixture = new Fixture(memoryLimit: disableCache ? 0 : 16 * 1024 * 1024);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.BeforeRead = (_, token) => release.Task.WaitAsync(token);
        using var logs = new VisualizationTimingLogger();
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        logs.EntryWritten = entry =>
        {
            if (entry.Phase == "join-build" && entry.Milestone == "end")
                joined.TrySetResult();
        };
        var timing = new VisualizationTiming(logs.CreateLogger(VisualizationTiming.Category), Guid.NewGuid());

        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        using var cancellation = new CancellationTokenSource();
        var owner = fixture.RunAsync(request, cancellationToken: cancellation.Token);
        Task<List<Row>>? waiter = null;
        try
        {
            await fixture.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            waiter = fixture.RunAsync(request, timing);
            await joined.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, fixture.Cache.Requests.CurrentCount);
            await cancellation.CancelAsync();
            Assert.False(owner.IsCompleted);
            Assert.False(waiter.IsCompleted);
            release.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner);
            await waiter;
        }
        finally
        {
            release.TrySetResult();
            await cancellation.CancelAsync();
            await ObserveAsync(owner);
            if (waiter is not null)
                await ObserveAsync(waiter);
        }
        Assert.Equal(65536 * 2, fixture.Samples);
        fixture.AssertReleased();
        Assert.Equal(disableCache ? "shared" : "hit",
            Assert.Single(logs.Entries, entry => entry.Phase == "cache-get" && entry.Milestone == "end").Outcome);
        Assert.Contains(logs.Entries, entry => entry.Phase == "build-gate-wait" && entry.Milestone == "end");
        Assert.DoesNotContain(logs.Entries, entry => entry.Phase == "controller-read-inclusive");
    }

    [Fact]
    public async Task CancellingWaiterDoesNotCancelOwner()
    {
        using var fixture = new Fixture();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.BeforeRead = (_, token) => release.Task.WaitAsync(token);
        using var logs = new VisualizationTimingLogger();
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        logs.EntryWritten = entry =>
        {
            if (entry.Phase == "join-build" && entry.Milestone == "end")
                joined.TrySetResult();
        };
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        var owner = fixture.RunAsync(request);
        using var cancellation = new CancellationTokenSource();
        Task<List<Row>>? waiter = null;
        try
        {
            await fixture.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            waiter = fixture.RunAsync(request, new VisualizationTiming(logs.CreateLogger(VisualizationTiming.Category), Guid.NewGuid()), cancellationToken: cancellation.Token);
            await joined.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
            Assert.False(owner.IsCompleted);
            Assert.True(fixture.Active > 0);
            release.TrySetResult();
            await owner;
        }
        finally
        {
            release.TrySetResult();
            await cancellation.CancelAsync();
            await ObserveAsync(owner);
            if (waiter is not null)
                await ObserveAsync(waiter);
        }
        Assert.Equal(65536 * 2, fixture.Samples);
        fixture.AssertReleased();
    }

    [Fact]
    public void CachePinsRemainAccountedUntilConsumerReleasesThem()
    {
        var dataset = new VisualizationDataset(0, 65536, 1);
        dataset.AllocateBase();
        dataset.BuildParents(CancellationToken.None);
        using var fixture = new Fixture(memoryLimit: dataset.Bytes);
        using var first = fixture.Cache.Put("first", dataset);
        using var hit = fixture.Cache.Get("first");
        Assert.NotNull(hit);
        using var second = fixture.Cache.Put("second", dataset);
        Assert.Null(fixture.Cache.Get("second"));
        Assert.Equal(dataset.Bytes, fixture.Cache.ResidentBytes);
        first.Dispose();
        using var third = fixture.Cache.Put("third", dataset);
        Assert.Null(fixture.Cache.Get("third"));
        hit.Dispose();
        using var replacement = fixture.Cache.Put("replacement", dataset);
        Assert.Null(fixture.Cache.Get("first"));
        Assert.Equal(dataset.Bytes, fixture.Cache.ResidentBytes);
    }

    [Fact]
    public void CacheEvictsLeastRecentlyUsedUnpinnedDataset()
    {
        var dataset = new VisualizationDataset(0, 65536, 1);
        dataset.AllocateBase();
        dataset.BuildParents(CancellationToken.None);
        using var fixture = new Fixture(memoryLimit: dataset.Bytes * 2);
        fixture.Cache.Put("first", dataset).Dispose();
        fixture.Cache.Put("second", dataset).Dispose();
        fixture.Cache.Get("first")!.Dispose();
        using var third = fixture.Cache.Put("third", dataset);
        using var first = fixture.Cache.Get("first");
        Assert.NotNull(first);
        Assert.Null(fixture.Cache.Get("second"));
        Assert.Equal(dataset.Bytes * 2, fixture.Cache.ResidentBytes);
    }

    [Fact]
    public async Task ExpiredPinnedCacheEntriesRemainAccountedUntilReleased()
    {
        var dataset = new VisualizationDataset(0, 65536, 1);
        dataset.AllocateBase();
        dataset.BuildParents(CancellationToken.None);
        using var fixture = new Fixture(memoryLimit: dataset.Bytes);
        fixture.Cache.Options.CacheTtl = TimeSpan.FromMilliseconds(20);
        using var lease = fixture.Cache.Put("first", dataset);
        await Task.Delay(100);
        Assert.Null(fixture.Cache.Get("first"));
        Assert.Equal(dataset.Bytes, fixture.Cache.ResidentBytes);
        lease.Dispose();
        Assert.Null(fixture.Cache.Get("first"));
        Assert.Equal(0, fixture.Cache.ResidentBytes);
    }

    [Fact]
    public void HugeDomainIsRejectedWithoutOverflowOrAllocation()
    {
        Assert.Throws<ValidationException>(() => VisualizationDataset.EstimateBytes(0, DateTime.MaxValue.Ticks, 100, long.MaxValue));
        var dataset = new VisualizationDataset(1, 1000, 1);
        dataset.AllocateBase();
        Assert.Throws<ArgumentOutOfRangeException>(() => VisualizationService.Query(dataset, [], 0, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => VisualizationService.Query(dataset, [], 0, 1, 1001));
    }

    [Theory]
    [InlineData(0, 65536, 1)]
    [InlineData(13, 65539, 2)]
    public void SummaryEstimateMatchesAllocationAndEnforcesExactBudget(long begin, long end, int resources)
    {
        var dataset = new VisualizationDataset(begin, end, resources);
        dataset.AllocateBase();
        dataset.BuildParents(CancellationToken.None);
        Assert.Equal(dataset.Bytes, VisualizationDataset.EstimateBytes(begin, end, resources, dataset.Bytes));
        var exception = Assert.Throws<ValidationException>(() =>
            VisualizationDataset.EstimateBytes(begin, end, resources, dataset.Bytes - 1));
        Assert.Contains("Data:Visualization:MaxDatasetCacheBytes", exception.Message);
    }

    [Fact]
    public async Task LargeDomainPreparationUsesConfigurableSummaryBudgetNotSampleLimit()
    {
        using var fixture = new Fixture();
        Assert.Equal(1L << 30, fixture.Cache.Options.MaxDatasetCacheBytes);
        Assert.Equal(1L << 30, new VisualizationOptions().MaxTotalCacheBytes);
        const int count = 600_000_000;
        var request = fixture.Request([fixture.View("main", 0, count, 100)]) with { End = fixture.Begin.AddSeconds(count) };

        Assert.NotNull(await fixture.Service.PrepareAsync(request, CancellationToken.None));
        fixture.Cache.Options.MaxDatasetCacheBytes = 65536;
        var exception = await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.PrepareAsync(request, CancellationToken.None));
        Assert.Contains("Data:Visualization:MaxDatasetCacheBytes", exception.Message);
        const int largerCount = 2_000_000_000;
        request = request with { End = fixture.Begin.AddSeconds(largerCount), Views = [fixture.View("main", 0, largerCount, 100)] };
        fixture.Cache.Options.MaxDatasetCacheBytes = 1L << 30;
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.PrepareAsync(request, CancellationToken.None));
        fixture.Cache.Options.MaxDatasetCacheBytes = 20L * 1024 * 1024 * 1024;
        Assert.NotNull(await fixture.Service.PrepareAsync(request, CancellationToken.None));
        Assert.Equal(0, fixture.Calls);
    }

    [Fact]
    public async Task UserIdentitiesAreIsolatedButShareOneCacheQuota()
    {
        using var fixture = new Fixture { Delay = TimeSpan.Zero };
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        await fixture.RunAsync(request);
        long bytes = fixture.Cache.ResidentBytes;
        fixture.Cache.Options.MaxTotalCacheBytes = bytes;
        var other = fixture.CreateService(Fixture.User("other"));
        await fixture.RunAsync(request, service: other);
        Assert.Equal(65536 * 4, fixture.Samples);
        Assert.Equal(bytes, fixture.Cache.ResidentBytes);
        await fixture.RunAsync(request);
        Assert.Equal(65536 * 6, fixture.Samples);
        Assert.Equal(bytes, fixture.Cache.ResidentBytes);
    }

    [Fact]
    public async Task DisabledMemoryCacheDoesNotPersistCompletedDatasets()
    {
        using var fixture = new Fixture(memoryLimit: 0) { Delay = TimeSpan.Zero };
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        Assert.Equal(2, (await fixture.RunAsync(request)).Last().Kind);
        Assert.Equal(2, (await fixture.RunAsync(request)).Last().Kind);
        Assert.Equal(65536 * 4, fixture.Samples);
        Assert.Equal(0, fixture.Cache.ResidentBytes);
    }

    [Fact]
    public async Task WarmDetailCoalescesAdjacentBucketsIntoBudgetedSlices()
    {
        using var fixture = new Fixture(memoryGrantBytes: 2 * 1024 * 1024) { Delay = TimeSpan.Zero };
        await fixture.RunAsync(fixture.Request([fixture.View("main", 0, 65536, 100)]));
        int calls = fixture.Calls;
        Assert.Equal(2, (await fixture.RunAsync(fixture.Request([fixture.View("detail", 8192, 16384, 32768)]))).Last().Kind);
        Assert.Equal(1, fixture.Calls - calls); // One memory-tracked slice, not 32 individual buckets.
    }

    [Theory]
    [InlineData(13, 1307)]
    [InlineData(29, 817)]
    public async Task RawProjectionPreservesCanonicalBitsAcrossClippedBucketsColdAndWarm(int viewBegin, int viewEnd)
    {
        float[] values = [0f, -0f, 1.25f, -7.5f, float.Epsilon, -float.Epsilon, float.MaxValue,
            float.MinValue, float.PositiveInfinity, float.NegativeInfinity,
            BitConverter.Int32BitsToSingle(0x7fc12345), BitConverter.Int32BitsToSingle(unchecked((int)0xffc54321)), 42f];
        using var fixture = new Fixture { Delay = TimeSpan.Zero, SampleValue = index => values[index % values.Length] };
        long origin = fixture.Begin.Ticks / TimeSpan.TicksPerSecond;
        int alignment = (int)((256 - origin % 256) % 256);
        int domainBegin = alignment + 13;
        viewBegin += alignment;
        viewEnd += alignment;
        var request = fixture.Request([fixture.View("raw", viewBegin, viewEnd, viewEnd - viewBegin)]) with
        {
            Begin = fixture.Begin.AddSeconds(domainBegin),
            End = fixture.Begin.AddSeconds(alignment + 1307)
        };
        var expected = Enumerable.Range(viewBegin, viewEnd - viewBegin)
            .Select(index => new VisualizationPoint(origin + index,
                float.IsFinite(values[index % values.Length]) ? values[index % values.Length] : float.NaN))
            .ToArray();
        using var logs = new VisualizationTimingLogger();

        foreach (string cacheOutcome in new[] { "miss", "hit" })
        {
            var timing = new VisualizationTiming(logs.CreateLogger(VisualizationTiming.Category), Guid.NewGuid());
            var rows = await fixture.RunAsync(request, timing);
            Assert.Equal(2, rows.Last().Kind);
            Assert.Equal(cacheOutcome, logs.Entries.Last(entry => entry.Phase == "cache-get" && entry.Milestone == "end").Outcome);
            var data = rows.Where(row => row.Kind == 0).ToArray();
            Assert.Equal(2, data.Length);

            foreach (var row in data)
            {
                Assert.Equal(expected.Select(point => point.Index - origin - domainBegin), row.Indices);
                Assert.Equal(expected.Select(point => BitConverter.SingleToInt32Bits(point.Value)),
                    row.Values.Select(BitConverter.SingleToInt32Bits));

            }
        }
    }

    [Fact]
    public async Task CatalogBoundsAreEnforcedBelowVisualizationReads()
    {
        using var fixture = new Fixture(restrictRange: true);
        var rows = await fixture.RunAsync(fixture.Request([fixture.View("main", 0, 65536, 1000)]));
        Assert.Equal(2, rows.Last().Kind);
        Assert.Equal(1024 * 2, fixture.Samples);

        foreach (var row in rows.Where(row => row.Kind == 0))
        {
            Assert.Contains(row.Values, float.IsNaN);
            Assert.Contains(row.Values, float.IsFinite);

            foreach (var (value, index) in row.Values.Zip(row.Indices))
            {
                if (float.IsFinite(value))
                    Assert.InRange(index, 1024, 2047);
            }
        }
    }

    [Fact]
    public async Task DynamicDependenciesDoNotDeadlockOrEnterTheOuterCache()
    {
        using var fixture = new Fixture { DynamicDependencies = true };
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]) with
        {
            ResourcePaths = ["/A/B/C/T1/1_s"]
        };
        Assert.Equal(2, (await fixture.RunAsync(request)).Last().Kind);
        int samples = fixture.Samples;
        Assert.Equal(65536 * 2, samples);
        Assert.Equal(2, (await fixture.RunAsync(request)).Last().Kind);
        Assert.Equal(samples * 2, fixture.Samples);
        Assert.Equal(fixture.Instances, fixture.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecursiveSiblingReadsTerminateAtSaturation(bool cycle)
    {
        using var fixture = new Fixture(readWorkers: 1)
        {
            DynamicDependencies = true, SiblingDependencies = true, CyclicDependencies = cycle,
            Delay = TimeSpan.Zero
        };
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dependencyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int dependencyReads = 0;
        fixture.BeforeRead = async (requests, token) =>
        {
            if (requests.All(request => request.CatalogItem.Resource.Id == "T2"))
            {
                Interlocked.Increment(ref dependencyReads);
                dependencyStarted.TrySetResult();
                await release.Task.WaitAsync(token);
            }
        };
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]) with { ResourcePaths = ["/A/B/C/T1/1_s"] };
        var running = fixture.RunAsync(request, expectError: cycle);
        try
        {
            await dependencyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.SiblingsInvoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(running.IsCompleted);
            Assert.Equal(1, Volatile.Read(ref dependencyReads));
            release.TrySetResult();
            await running;
        }
        finally
        {
            release.TrySetResult();
            await ObserveAsync(running);
        }
        if (cycle)
            Assert.Contains(fixture.Errors, error => error is ValidationException && error.Message == "Visualization dependency exceeds the recursion or memory budget.");
        else
        {
            Assert.Empty(fixture.Errors);
            Assert.Equal(65536 * 3, fixture.Samples);
        }
        fixture.AssertReleased();
    }

    [Fact]
    public async Task MixedOriginalAndResampledResourcesPreserveValuesAndRouting()
    {
        using var fixture = new Fixture();
        var request = fixture.Request([fixture.View("main", 137, 211, 100)]) with
        {
            ResourcePaths = ["/A/B/C/T1/1_s", "/A/B/C/T2/1_s_resampled#base=2_s"]
        };
        var rows = await fixture.RunAsync(request);
        foreach (var row in rows.Where(row => row.Kind == 0))
            Assert.Equal(Enumerable.Range(137, 74).Select(index => row.Resource == 0 ? (float)index : index / 2 * 2 + 2000f), row.Values);
        Assert.Equal(65536 + 32768, fixture.Samples);
        fixture.AssertReleased();
    }

    [Fact]
    public void PyramidQueriesPreserveUnalignedBoundariesAndGaps()
    {
        const long begin = 63839664001;
        var values = Enumerable.Range(0, 10003).Select(i => i % 113 == 0 ? float.NaN : (float)Math.Sin(i)).ToArray();
        var dataset = new VisualizationDataset(begin, begin + values.Length, 1);
        dataset.AllocateBase();
        var raw = new ConcurrentDictionary<long, float[][]>();

        for (long start = begin; start < dataset.End;)
        {
            int count = (int)(Math.Min(dataset.End, (start / 256 + 1) * 256) - start);
            var fragment = values.AsSpan((int)(start - begin), count).ToArray();
            raw[start / 256] = [fragment];
            dataset.Levels[0][0][start / 256 - begin / 256] = VisualizationReduction.Summarize(fragment, start);
            start += count;
        }

        dataset.BuildParents(CancellationToken.None);
        var random = new Random(1);

        for (int i = 0; i < 100; i++)
        {
            int start = random.Next(values.Length);
            int count = random.Next(1, values.Length - start + 1);
            var expected = VisualizationReduction.Summarize(values.AsSpan(start, count), begin + start);
            var actual = VisualizationService.Query(dataset, raw, 0, begin + start, begin + start + count);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public async Task DerivedReadsBypassLowerCacheAndReuseOuterCache()
    {
        using var fixture = new Fixture();
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]) with
        {
            ResourcePaths = ["/A/B/C/T1/2_s_mean#base=1_s", "/A/B/C/T2/2_s_mean#base=1_s"]
        };
        var rows = await fixture.RunAsync(request);
        Assert.Equal(2, rows.Last().Kind);
        Assert.Equal(65536 * 2, fixture.Samples);
        Assert.Equal(fixture.Instances, fixture.Disposed);
        int samples = fixture.Samples;
        await fixture.RunAsync(request);
        Assert.Equal(samples, fixture.Samples);
        fixture.DerivedCache.VerifyNoOtherCalls();
        fixture.AssertReleased();
    }

    [Theory]
    [InlineData("/A/B/C/T1/2_s_mean#base=1_s")]
    [InlineData("/A/B/C/T2/1_s_resampled#base=2_s")]
    public async Task FailedDerivedReadsAreNotCachedAsGaps(string path)
    {
        using var fixture = new Fixture { Fail = true };
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]) with
        {
            ResourcePaths = [path]
        };
        Assert.Equal(3, (await fixture.RunAsync(request, expectError: true)).Last().Kind);
        Assert.Contains(fixture.Errors, error => error.Message == "source failed");
        fixture.AssertReleased();
        fixture.Fail = false;
        var rows = await fixture.RunAsync(request);
        Assert.Equal(2, rows.Last().Kind);
        Assert.Contains(rows.Where(row => row.Kind == 0).SelectMany(row => row.Values), float.IsFinite);
    }

    [Fact]
    public async Task DeduplicatesSimultaneousBuilds()
    {
        using var fixture = new Fixture();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.BeforeRead = (_, token) => release.Task.WaitAsync(token);
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        var first = fixture.RunAsync(request);
        Task<List<Row>>? second = null;
        try
        {
            await fixture.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            second = fixture.RunAsync(request);
            Assert.Equal(2, fixture.Cache.Requests.CurrentCount);
            release.TrySetResult();
            await Task.WhenAll(first, second);
        }
        finally
        {
            release.TrySetResult();
            await ObserveAsync(first);
            if (second is not null)
                await ObserveAsync(second);
        }
        Assert.Equal(65536 * 2, fixture.Samples);
        fixture.AssertReleased();
    }

    [Fact]
    public async Task ReadsOnceForAllViewsWithIndependentWorkersAndWarmZoom()
    {
        using var fixture = new Fixture();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.BeforeRead = async (_, token) =>
        {
            if (fixture.Active == 3)
                entered.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        var request = fixture.Request([
            fixture.View("main", 0, 65536, 1000),
            fixture.View("detail", 137, 211, 1000),
            fixture.View("nav", 0, 65536, 100)]);
        var running = fixture.RunAsync(request);
        List<Row> rows;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(3, fixture.Active);
            release.TrySetResult();
            rows = await running;
        }
        finally
        {
            release.TrySetResult();
            await ObserveAsync(running);
        }
        fixture.BeforeRead = null;

        Assert.Equal(65536 * 2, fixture.Samples);
        Assert.Equal(3, fixture.Peak);
        Assert.Equal(3, fixture.Instances);
        Assert.Equal(fixture.Instances, fixture.Disposed);
        Assert.Equal(6, rows.Count(row => row.Kind == 0));
        Assert.Equal(2, rows.Last().Kind);

        foreach (var row in rows.Where(row => row.Kind == 0))
        {
            Assert.True(row.Indices.SequenceEqual(row.Indices.Order()));
            Assert.InRange(row.Values.Length, 1, request.Views[row.View].MaxPoints);
            Assert.Equal("complete", row.Message);
            Assert.Equal(row.Resource * 2000, row.Minimum);
            Assert.Equal(999 + row.Resource * 2000, row.Maximum);
        }

        var detail = rows.Single(row => row.Kind == 0 && row.Resource == 0 && row.View == 1);
        Assert.Equal(Enumerable.Range(137, 74).Select(i => (long)i), detail.Indices);
        Assert.Equal(Enumerable.Range(137, 74).Select(i => (float)i), detail.Values);
        var otherDetail = rows.Single(row => row.Kind == 0 && row.Resource == 1 && row.View == 1);
        Assert.Equal(detail.Indices, otherDetail.Indices);
        Assert.Equal(Enumerable.Range(137, 74).Select(i => (float)i + 2000), otherDetail.Values);
        Assert.Contains(rows.Where(row => row.Kind == 0 && row.View == 0).SelectMany(row => row.Values), float.IsNaN);

        int samples = fixture.Samples;
        var warm = await fixture.RunAsync(fixture.Request([fixture.View("zoom", 8192, 32768, 50)]));
        Assert.Equal(samples, fixture.Samples);
        Assert.Equal(2, warm.Last().Kind);

        await fixture.RunAsync(fixture.Request([fixture.View("fine", 8193, 16397, 1000)]));
        Assert.Equal(33 * 256 * 2, fixture.Samples - samples);
    }

    [Fact]
    public async Task UsesSharedMemoryCacheAndIsolatesRequestConfiguration()
    {
        using var fixture = new Fixture();
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        await fixture.RunAsync(request);
        int samples = fixture.Samples;
        await fixture.RunAsync(request);
        Assert.Equal(samples, fixture.Samples);

        fixture.Configuration = new Dictionary<string, JsonElement> { ["tenant"] = JsonSerializer.SerializeToElement("other") };
        await fixture.RunAsync(request);
        Assert.Equal(samples * 2, fixture.Samples);
    }

    [Fact]
    public async Task ErrorsDoNotBecomeCachedNaNsOrSuccess()
    {
        using var fixture = new Fixture { Fail = true };
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        var failed = await fixture.RunAsync(request, expectError: true);
        Assert.Equal(3, failed.Last().Kind);
        Assert.DoesNotContain(failed, row => row.Kind is 0 or 2);
        fixture.Fail = false;
        var success = await fixture.RunAsync(request);
        Assert.Equal(2, success.Last().Kind);
        Assert.All(success.Where(row => row.Kind == 0), row => Assert.Contains(row.Values, float.IsFinite));
        Assert.Equal(fixture.Instances, fixture.Disposed);
    }

    [Fact]
    public async Task CancellationDrainsWorkersAndReleasesGlobalBudgets()
    {
        using var fixture = new Fixture(readWorkers: 1);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.BeforeRead = async (_, token) =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                await release.Task;
                throw;
            }
        };
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        using var cancellation = new CancellationTokenSource();
        var write = await fixture.Service.PrepareAsync(request, cancellation.Token);
        using var output = new MemoryStream();
        var running = write(output, cancellation.Token);
        try
        {
            await fixture.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cancellation.CancelAsync();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(running.IsCompleted);
            Assert.Equal(1, fixture.Active);
            Assert.Equal(0, fixture.Disposed);
            Assert.NotEmpty(fixture.Allocations);
            Assert.Equal(0, fixture.Cache.Reads.CurrentCount);
            Assert.Equal(3, fixture.Cache.Requests.CurrentCount);
            release.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            release.TrySetResult();
            await cancellation.CancelAsync();
            await ObserveAsync(running);
        }
        fixture.AssertReleased();
        fixture.BeforeRead = null;
        Assert.Equal(2, (await fixture.RunAsync(request)).Last().Kind);
        fixture.AssertReleased();
    }

    [Fact]
    public async Task FlushesProgressBeforeSlowReadFinishes()
    {
        using var fixture = new Fixture();
        fixture.BeforeRead = (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token);
        using var cancellation = new CancellationTokenSource();
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        var write = await fixture.Service.PrepareAsync(request, cancellation.Token);
        var pipe = new System.IO.Pipelines.Pipe();
        var running = write(pipe.Writer.AsStream(), cancellation.Token);
        using var reader = new ArrowStreamReader(pipe.Reader.AsStream());
        try
        {
            using var first = await reader.ReadNextRecordBatchAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            using var next = await reader.ReadNextRecordBatchAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, ((Int32Array)first.Column(0)).GetValue(0));
            Assert.Equal(0, ((DoubleArray)first.Column(6)).GetValue(0));
            Assert.Equal(1, ((Int32Array)next.Column(0)).GetValue(0));
            Assert.Equal(0, ((DoubleArray)next.Column(6)).GetValue(0));
            Assert.False(running.IsCompleted);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        }
        finally
        {
            await cancellation.CancelAsync();
            await ObserveAsync(running);
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
        }
        fixture.AssertReleased();
    }

    [Fact]
    public async Task EndpointStreamsFixedNonnullableSchemaAndRejectsInvalidInput()
    {
        using var fixture = new Fixture();
        var context = new DefaultHttpContext();
        using var output = new MemoryStream();
        context.Response.Body = output;
        var controller = new Nexus.Controllers.V2.DataController(Mock.Of<IDataService>(), NullLoggerFactory.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        Assert.IsType<EmptyResult>(await controller.GetVisualizationAsync(request, fixture.Service, CancellationToken.None));
        Assert.Equal("application/vnd.apache.arrow.stream", context.Response.ContentType);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        Fixture.AssertSuccess(request, await Fixture.ReadRowsAsync(output));
        context.Response.Body.Position = 0;
        using var reader = new ArrowStreamReader(context.Response.Body, leaveOpen: true);
        using var batch = await reader.ReadNextRecordBatchAsync();
        Assert.Equal("1", reader.Schema.Metadata["visualizationVersion"]);
        Assert.Equal(new[] { "kind", "resourceIndex", "viewIndex", "offset", "indices", "values", "progress", "minimum", "maximum", "message" }, reader.Schema.FieldsList.Select(field => field.Name));
        Assert.All(reader.Schema.FieldsList, field => Assert.False(field.IsNullable));
        for (int column = 0; column < batch.ColumnCount; column++)
            Assert.Equal(0, batch.Column(column).NullCount);
        Assert.Equal(new[] { ArrowTypeId.Int32, ArrowTypeId.Int32, ArrowTypeId.Int32, ArrowTypeId.Int64,
            ArrowTypeId.List, ArrowTypeId.List, ArrowTypeId.Double, ArrowTypeId.Float, ArrowTypeId.Float, ArrowTypeId.String },
            reader.Schema.FieldsList.Select(field => field.DataType.TypeId));
        foreach (int index in new[] { 4, 5 })
        {
            var list = Assert.IsType<ListType>(reader.Schema.FieldsList[index].DataType);
            Assert.False(list.ValueField.IsNullable);
            Assert.Equal(index == 4 ? ArrowTypeId.Int64 : ArrowTypeId.Float, list.ValueField.DataType.TypeId);
        }
        Assert.IsType<Int64Array>(((ListArray)batch.Column(4)).Values);
        Assert.IsType<FloatArray>(((ListArray)batch.Column(5)).Values);
        Assert.Equal(0, ((ListArray)batch.Column(4)).Values.Length);

        var invalid = request with { Views = [fixture.View("bad", 0, 65536, 32769)] };
        var invalidController = new Nexus.Controllers.V2.DataController(Mock.Of<IDataService>(), NullLoggerFactory.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var invalidResult = Assert.IsType<ContentResult>(await invalidController.GetVisualizationAsync(invalid, fixture.Service, CancellationToken.None));
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, invalidResult.StatusCode);
        Assert.Equal("text/plain", invalidResult.ContentType);
        Assert.Throws<ValidationException>(() => VisualizationService.ValidateRequest(request with { Views = [request.Views[0], request.Views[0]] }, fixture.Cache.Options));
        Assert.Throws<ValidationException>(() => VisualizationService.ValidateRequest(request with { Views = [fixture.View("bad", -1, 2, 100)] }, fixture.Cache.Options));
    }

    [Fact]
    public async Task CacheHitsStillRequireAuthorization()
    {
        using var fixture = new Fixture();
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        var service = fixture.CreateService(Fixture.User("licensed", administrator: false));
        fixture.LicenseAccepted = true;
        await fixture.RunAsync(request, service: service);
        int samples = fixture.Samples;
        long bytes = fixture.Cache.ResidentBytes;
        fixture.LicenseAccepted = false;
        var exception = await Assert.ThrowsAsync<Exception>(() => service.PrepareAsync(request, CancellationToken.None));
        Assert.StartsWith("The current user is not permitted", exception.Message);
        Assert.Equal(samples, fixture.Samples);
        Assert.Equal(bytes, fixture.Cache.ResidentBytes);
        fixture.LicenseAccepted = true;
        await fixture.RunAsync(request, service: service);
        Assert.Equal(samples, fixture.Samples);
    }

    [Fact]
    public async Task SliceSizingUsesMemoryTrackerGrant()
    {
        using var fixture = new Fixture(readWorkers: 1, memoryGrantBytes: 65536) { Delay = TimeSpan.Zero };
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        int bytesPerSample = ((sizeof(float) + 1 + 32) * 2 + sizeof(float)) * request.ResourcePaths.Length;
        int expectedSliceSamples = 65536 / bytesPerSample / 256 * 256;
        int expectedCalls = (65536 + expectedSliceSamples - 1) / expectedSliceSamples;

        await fixture.RunAsync(request);

        Assert.Equal(expectedCalls, fixture.Calls);
    }

    [Fact]
    public async Task DifferentKeyScansShareTheGlobalReadBudget()
    {
        using var fixture = new Fixture();
        // Do not let random hash-stripe collisions serialize the two distinct datasets.
        foreach (var gate in fixture.Cache.Builds)
            gate.Release();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saturated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allWorkersWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        using var logs = new VisualizationTimingLogger();
        logs.EntryWritten = entry =>
        {
            if (entry.Phase == "read-permit-wait" && entry.Milestone == "start" && Interlocked.Increment(ref attempts) == 6)
                allWorkersWaiting.TrySetResult();
        };
        fixture.BeforeRead = async (_, token) =>
        {
            if (fixture.Active == 3)
                saturated.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        var first = fixture.RunAsync(request, new VisualizationTiming(logs.CreateLogger(VisualizationTiming.Category), Guid.NewGuid()));
        var second = fixture.RunAsync(request with { End = fixture.Begin.AddSeconds(65792) },
            new VisualizationTiming(logs.CreateLogger(VisualizationTiming.Category), Guid.NewGuid()));
        try
        {
            await allWorkersWaiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await saturated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(3, fixture.Active);
            Assert.Equal(0, fixture.Cache.Reads.CurrentCount);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            release.TrySetResult();
            await Task.WhenAll(first, second);
        }
        finally
        {
            release.TrySetResult();
            await ObserveAsync(Task.WhenAll(first, second));
        }
        Assert.Equal(3, fixture.Peak);
        Assert.Equal((65536 + 65792) * 2, fixture.Samples);
        fixture.AssertReleased();
    }

    [Theory]
    [InlineData(100, 110, 0, 10)]
    [InlineData(1, 2, 0, 10)]
    public async Task RestrictedResamplingWithoutCompleteBaseIntervalEmitsGaps(int minimum, int maximum, int begin, int end)
    {
        using var fixture = new Fixture(minBegin: minimum, maxEnd: maximum);
        var request = fixture.Request([fixture.View("raw", begin, end, 100)]) with
        {
            Begin = fixture.Begin.AddSeconds(begin), End = fixture.Begin.AddSeconds(end),
            ResourcePaths = ["/A/B/C/T2/1_s_resampled#base=2_s"]
        };
        var rows = await fixture.RunAsync(request);
        Assert.All(Assert.Single(rows, row => row.Kind == 0).Values, value => Assert.True(float.IsNaN(value)));
        Assert.Equal(0, fixture.Calls);
        fixture.AssertReleased();
    }

    [Fact]
    public async Task CatalogStateReplacementInvalidatesCompletedDatasets()
    {
        using var fixture = new Fixture();
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        await fixture.RunAsync(request);
        await fixture.RunAsync(request);
        Assert.Equal(65536 * 2, fixture.Samples);
        fixture.ReloadCatalogs();
        await fixture.RunAsync(request);
        Assert.Equal(65536 * 4, fixture.Samples);
        fixture.AssertReleased();
    }

    [Fact]
    public async Task InvalidRequestsAreRejectedBeforeSourceWork()
    {
        using var fixture = new Fixture();
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        VisualizationRequest[] invalid =
        [
            null!, request with { ResourcePaths = [] }, request with { ResourcePaths = ["", "x"] },
            request with { ResourcePaths = [request.ResourcePaths[0], request.ResourcePaths[0]] },
            request with { Begin = request.End }, request with { Views = [] },
            request with { Views = null! }, request with { Views = [null!] },
            request with { Views = [fixture.View("", 0, 1, 5)] },
            request with { Views = [fixture.View("small", 0, 1, 4)] },
            request with { Views = [fixture.View("large", 0, 1, 32769)] },
            request with { Views = [request.Views[0], request.Views[0]] },
            request with { Views = [fixture.View("outside", 0, 65537, 100)] },
            request with { Views = [fixture.View("empty", 1, 1, 100)] },
            request with { Views = [fixture.View("a", 0, 1, 5), fixture.View("b", 0, 1, 5), fixture.View("c", 0, 1, 5), fixture.View("d", 0, 1, 5)] },
            request with { ResourcePaths = ["/A/B/C/T1/1_s", "/A/B/C/T2/2_s"] },
            request with { Begin = request.Begin.AddTicks(1), Views = [fixture.View("inside", 1, 10, 100)] },
            request with { End = request.End.AddTicks(1) },
            request with { Views = [request.Views[0] with { Begin = request.Begin.AddTicks(1) }] }
        ];
        foreach (var value in invalid)
            await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.PrepareAsync(value, CancellationToken.None));
        fixture.Cache.Options.MaxAggregatePoints = 199;
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.PrepareAsync(request, CancellationToken.None));
        fixture.Cache.Options.MaxAggregatePoints = 200;
        Assert.NotNull(await fixture.Service.PrepareAsync(request, CancellationToken.None));
        Assert.Equal(0, fixture.Instances);
        Assert.Equal(0, fixture.Calls);
        fixture.AssertReleased();
    }

    [Fact]
    public async Task BusyAdmissionRejectsWithoutOutputOrSourceWork()
    {
        using var fixture = new Fixture();
        using var stream = new MemoryStream();
        for (int i = 0; i < fixture.Cache.Options.MaxConcurrentRequests; i++)
            await fixture.Cache.Requests.WaitAsync();
        try
        {
            var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
            var controller = new Nexus.Controllers.V2.DataController(Mock.Of<IDataService>(), NullLoggerFactory.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            controller.Response.Body = stream;
            var result = Assert.IsType<ContentResult>(await controller.GetVisualizationAsync(request, fixture.Service, CancellationToken.None));
            Assert.Equal(422, result.StatusCode);
            Assert.Contains("busy", result.Content);
            Assert.Equal(0, stream.Length);
            Assert.Equal(0, fixture.Instances);
            Assert.Equal(0, fixture.Calls);
            Assert.Equal(0, fixture.Cache.Requests.CurrentCount);
        }
        finally { fixture.Cache.Requests.Release(fixture.Cache.Options.MaxConcurrentRequests); }
        fixture.AssertReleased();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RealPipelineTraceIsOptInRedactedAndAggregated(bool enabled, bool fail)
    {
        using var fixture = new Fixture { Fail = fail };
        fixture.Configuration = new Dictionary<string, JsonElement> { ["secret-key"] = JsonSerializer.SerializeToElement("secret-value") };
        using var logs = new VisualizationTimingLogger();
        var request = fixture.Request([fixture.View("secret-view", 0, 65536, 100)]);
        for (int attempt = 0; attempt < (fail ? 1 : 2); attempt++)
        {
            using var output = new MemoryStream();
            var controller = new Nexus.Controllers.V2.DataController(Mock.Of<IDataService>(), logs)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            controller.Response.Body = output;
            string id = Guid.NewGuid().ToString("D");
            if (enabled)
                controller.Request.Headers[VisualizationTiming.Header] = id;
            int calls = fixture.Calls;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Assert.IsType<EmptyResult>(await controller.GetVisualizationAsync(request, fixture.Service, timeout.Token));
            var rows = await Fixture.ReadRowsAsync(output);
            if (fail)
                Assert.Equal(3, rows.Last().Kind);
            else
                Fixture.AssertSuccess(request, rows);
            if (!enabled)
            {
                Assert.Empty(logs.Entries);
                Assert.False(controller.Response.Headers.ContainsKey(VisualizationTiming.Header));
                continue;
            }
            Assert.Equal(id, controller.Response.Headers[VisualizationTiming.Header]);
            var entries = logs.Entries.Where(entry => Equals(entry.Fields["RequestId"], id)).ToArray();
            Assert.Equal(fail ? "stream-error" : "success", Assert.Single(entries, entry => entry.Phase == "request" && entry.Milestone == "end").Outcome);
            Assert.All(entries, entry =>
            {
                Assert.Null(entry.Exception);
                Assert.DoesNotContain("secret", entry.Message);
                Assert.DoesNotContain("/A/B/C", entry.Message);
                Assert.DoesNotContain("Administrator", entry.Message);
                Assert.DoesNotContain("source failed", entry.Message);
            });
            var reads = entries.Where(entry => entry.Phase == "controller-read-inclusive" && entry.Milestone == "aggregate").ToArray();
            Assert.Equal(fixture.Calls - calls, reads.Sum(entry => (long)entry.Fields["Count"]!));
            Assert.True(reads.Length <= fixture.Cache.Options.MaxConcurrentReads);
            if (!fail && attempt == 0)
            {
                Assert.True(fixture.Calls > 100);
                Assert.True(entries.Length < fixture.Calls);
            }
            if (attempt == 1)
                Assert.Empty(reads);
        }
        fixture.AssertReleased();
    }

    private static async Task ObserveAsync(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(35)); }
        catch (OperationCanceledException) { }
    }

    private sealed record Row(int Kind, int Resource, int View, long Offset, long[] Indices, float[] Values, double Progress, float Minimum, float Maximum, string Message);

    private sealed class Fixture : IDisposable
    {
        public DateTime Begin { get; } = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public VisualizationCache Cache { get; }
        public VisualizationService Service { get; }
        public int Samples;
        public int Calls;
        public int Instances;
        public int Disposed;
        public int Active;
        public int Peak;
        public bool Fail;
        public bool DynamicDependencies;
        public bool SiblingDependencies;
        public bool CyclicDependencies;
        public bool LicenseAccepted;
        public Func<ReadRequest[], CancellationToken, Task>? BeforeRead;
        public TaskCompletionSource SiblingsInvoked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<Exception> Errors { get; } = new();
        public Mock<ICacheService> DerivedCache { get; } = new(MockBehavior.Strict);
        public ConcurrentDictionary<AllocationRegistration, byte> Allocations { get; } = new();
        public Func<long, float>? SampleValue;
        public TimeSpan Delay = TimeSpan.Zero;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyDictionary<string, JsonElement>? Configuration;
        private readonly Mock<IDataControllerService> _controllers = new();
        private readonly Mock<IMemoryTracker> _memoryTracker = new();
        private readonly Mock<IAcceptedLicenseService> _licenses = new();
        private readonly AppState _state;

        public Fixture(long memoryLimit = 16 * 1024 * 1024, bool restrictRange = false,
            int readWorkers = 3, int memoryGrantBytes = 65536, int? minBegin = null, int? maxEnd = null)
        {
            var options = new DataOptions
            {
                Visualization = new VisualizationOptions { MaxConcurrentReads = readWorkers,
                    MaxComputeWorkers = 2, MaxTotalCacheBytes = memoryLimit }
            };
            Cache = new VisualizationCache(Options.Create(options));
            _memoryTracker.Setup(current => current.RegisterAllocationAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync<long, long, CancellationToken, IMemoryTracker, AllocationRegistration>((minimum, maximum, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    var allocation = new AllocationRegistration(_memoryTracker.Object, Math.Max(minimum, Math.Min(memoryGrantBytes, maximum)));
                    Assert.True(Allocations.TryAdd(allocation, 0));
                    return allocation;
                });
            _memoryTracker.Setup(current => current.UnregisterAllocation(It.IsAny<AllocationRegistration>()))
                .Callback<AllocationRegistration>(allocation => Assert.True(Allocations.TryRemove(allocation, out _)));
            _licenses.Setup(current => current.HasAccepted(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<string?>()))
                .Returns(() => LicenseAccepted);
            minBegin ??= restrictRange ? 1024 : null;
            maxEnd ??= restrictRange ? 2048 : null;
            var pipeline = new DataSourcePipeline([new DataSourceRegistration("test", null, JsonSerializer.SerializeToElement<object?>(null))]);
            var representation = new Representation(NexusDataType.Float32, TimeSpan.FromSeconds(1));
            var coarseRepresentation = new Representation(NexusDataType.Float32, TimeSpan.FromSeconds(2));
            var catalog = new ResourceCatalogBuilder("/A/B/C")
                .AddResource(new ResourceBuilder("T1").AddRepresentation(representation).Build())
                .AddResource(new ResourceBuilder("T2").AddRepresentation(representation).AddRepresentation(coarseRepresentation).Build())
                .Build().EnsureAndSanitizeMandatoryProperties(0, []);
            var lookup = new Mock<IDataSourceController>();
            lookup.Setup(current => current.GetCatalogAsync(catalog.Id, It.IsAny<CancellationToken>())).ReturnsAsync(catalog);
            _controllers.Setup(current => current.GetDataSourceControllerAsync(pipeline, It.IsAny<CancellationToken>())).ReturnsAsync(lookup.Object);
            _controllers.Setup(current => current.CaptureRequestConfiguration()).Returns(() => Configuration);
            _controllers.Setup(current => current.GetVisualizationDataSourceControllerAsync(pipeline,
                    It.IsAny<IReadOnlyDictionary<string, JsonElement>?>(), It.IsAny<ResourceCatalog[]>(), It.IsAny<CancellationToken>()))
                .Returns<DataSourcePipeline, IReadOnlyDictionary<string, JsonElement>?, ResourceCatalog[], CancellationToken>(
                    async (_, configuration, catalogs, token) =>
                    {
                        Interlocked.Increment(ref Instances);
                        int reading = 0;
                        var source = new Mock<IDataSource<object?>>();
                        source.As<IDisposable>().Setup(current => current.Dispose()).Callback(() => Interlocked.Increment(ref Disposed));
                        source.Setup(current => current.ReadAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<ReadRequest[]>(),
                                It.IsAny<ReadDataHandler>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
                            .Returns<DateTime, DateTime, ReadRequest[], ReadDataHandler, IProgress<double>, CancellationToken>(async (from, to, requests, readData, _, ct) =>
                            {
                                int concurrent = Interlocked.Increment(ref reading);
                                Interlocked.Increment(ref Calls);
                                int active = Interlocked.Increment(ref Active);
                                int previous;

                                do { previous = Peak; }
                                while (active > previous && Interlocked.CompareExchange(ref Peak, active, previous) != previous);

                                try
                                {
                                    Assert.Equal(1, concurrent);
                                    if (minBegin is not null)
                                        Assert.True(from >= Begin.AddSeconds(minBegin.Value));
                                    if (maxEnd is not null)
                                        Assert.True(to <= Begin.AddSeconds(maxEnd.Value));

                                    Started.TrySetResult();
                                    if (BeforeRead is not null)
                                        await BeforeRead(requests, ct);
                                    await Task.Delay(Delay, ct);

                                    if (Fail)
                                        throw new InvalidOperationException("source failed");

                                    int count = (int)(to - from).TotalSeconds;
                                    Interlocked.Add(ref Samples, requests.Sum(request => request.Data.Length / request.CatalogItem.Representation.ElementSize));

                                    if (DynamicDependencies && (CyclicDependencies || requests.Any(request => request.CatalogItem.Resource.Id == "T1")))
                                    {
                                        var firstValues = new double[count];
                                        var secondValues = new double[count];
                                        var first = readData("/A/B/C/T2/1_s", from, to, firstValues, ct);

                                        if (SiblingDependencies)
                                        {
                                            var second = readData("/A/B/C/T2/1_s", from, to, secondValues, ct);
                                            SiblingsInvoked.TrySetResult();
                                            await Task.WhenAll(first, second);
                                            Assert.Equal(firstValues, secondValues);
                                        }
                                        else
                                            await first;
                                        Assert.Equal(Enumerable.Range(0, count).Select(i => (double)Value("T2", (long)(from - Begin).TotalSeconds + i)), firstValues);
                                    }

                                    foreach (var request in requests)
                                    {
                                        var values = MemoryMarshal.Cast<byte, float>(request.Data.Span);

                                        for (int i = 0; i < values.Length; i++)
                                        {
                                            long index = (long)(from - Begin).TotalSeconds + i * (long)request.CatalogItem.Representation.SamplePeriod.TotalSeconds;
                                            values[i] = Value(request.CatalogItem.Resource.Id, index);
                                        }

                                        request.Status.Span.Fill(1);
                                    }
                                }
                                finally
                                {
                                    Interlocked.Decrement(ref Active);
                                    Interlocked.Decrement(ref reading);
                                }
                            });
                        var controller = new DataSourceController([source.Object], pipeline.Registrations, configuration,
                            new ProcessingService(Options.Create(options)), DerivedCache.Object, options,
                            NullLogger<DataSourceController>.Instance, strictReads: true);
                        await controller.InitializeAsync(new ConcurrentDictionary<string, ResourceCatalog>(catalogs.Select(current => KeyValuePair.Create(current.Id, current))), NullLoggerFactory.Instance, token);
                        return controller;
                    });
            var manager = new Mock<ICatalogManager>();
            manager.Setup(current => current.GetCatalogContainersAsync(It.IsAny<CatalogContainer>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new CatalogContainer(new CatalogRegistration(catalog.Id, "",
                    MinBegin: minBegin is null ? null : Begin.AddSeconds(minBegin.Value),
                    MaxEnd: maxEnd is null ? null : Begin.AddSeconds(maxEnd.Value)), Guid.Empty, pipeline, [],
                    new CatalogMetadata(null, null, null), manager.Object, default!, _controllers.Object)]);
            _state = new AppState { CatalogState = new CatalogState(CatalogContainer.CreateRoot(manager.Object, default!), new CatalogCache()) };
            Service = CreateService(User("first"));
        }

        public static ClaimsPrincipal User(string subject, bool administrator = true) => new(new ClaimsIdentity(
            administrator ? [new Claim(NexusClaimTypes.Subject, subject), new Claim(ClaimTypes.Role, nameof(NexusRoles.Administrator))]
                : [new Claim(NexusClaimTypes.Subject, subject)], "test"));

        public float Value(string resource, long index) => SampleValue is not null ? SampleValue(index)
            : index is >= 500 and < 510 ? float.PositiveInfinity : index % 1000 + (resource == "T2" ? 2000 : 0);

        public void ReloadCatalogs() => _state.CatalogState = new CatalogState(_state.CatalogState.Root, new CatalogCache());

        public VisualizationService CreateService(ClaimsPrincipal user) => new(_state, user, _controllers.Object,
            _licenses.Object, Cache, _memoryTracker.Object, new ErrorLogger(Errors));

        public VisualizationView View(string id, int begin, int end, int points) => new(id, Begin.AddSeconds(begin), Begin.AddSeconds(end), points);
        public VisualizationRequest Request(VisualizationView[] views) => new(Begin, Begin.AddSeconds(65536), ["/A/B/C/T1/1_s", "/A/B/C/T2/1_s"], views);

        public async Task<List<Row>> RunAsync(VisualizationRequest request, VisualizationTiming? timing = null,
            bool expectError = false, VisualizationService? service = null, CancellationToken cancellationToken = default)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var write = await (service ?? Service).PrepareAsync(request, timeout.Token, timing);
            using var stream = new MemoryStream();
            await write(stream, timeout.Token);
            var rows = await ReadRowsAsync(stream);
            if (expectError)
            {
                Assert.Equal(3, rows.Last().Kind);
                Assert.DoesNotContain(rows, row => row.Kind is 0 or 2);
            }
            else
                AssertSuccess(request, rows);
            return rows;
        }

        public static async Task<List<Row>> ReadRowsAsync(Stream stream)
        {
            stream.Position = 0;
            using var reader = new ArrowStreamReader(stream, leaveOpen: true);
            var rows = new List<Row>();
            RecordBatch batch;

            while ((batch = await reader.ReadNextRecordBatchAsync()) is not null)
            {
                using (batch)
                {
                    Assert.Equal(1, batch.Length);
                    rows.Add(new Row(((Int32Array)batch.Column(0)).GetValue(0)!.Value,
                        ((Int32Array)batch.Column(1)).GetValue(0)!.Value, ((Int32Array)batch.Column(2)).GetValue(0)!.Value,
                        ((Int64Array)batch.Column(3)).GetValue(0)!.Value,
                        ((Int64Array)((ListArray)batch.Column(4)).Values).Values.ToArray(),
                        ((FloatArray)((ListArray)batch.Column(5)).Values).Values.ToArray(),
                        ((DoubleArray)batch.Column(6)).GetValue(0)!.Value,
                        ((FloatArray)batch.Column(7)).GetValue(0)!.Value, ((FloatArray)batch.Column(8)).GetValue(0)!.Value,
                        ((StringArray)batch.Column(9)).GetString(0)));
                }
            }

            return rows;
        }

        public static void AssertSuccess(VisualizationRequest request, List<Row> rows)
        {
            Assert.DoesNotContain(rows, row => row.Kind == 3);
            Assert.Equal(2, rows.Last().Kind);
            Assert.Single(rows, row => row.Kind == 2);
            Assert.Equal(1, rows[0].Kind);
            Assert.Equal(0, rows[0].Progress);
            Assert.All(rows, row => Assert.InRange(row.Progress, 0, 1));
            Assert.All(rows, row => Assert.InRange(row.Kind, 0, 2));
            var data = rows.Where(row => row.Kind == 0).ToArray();
            Assert.Equal(request.ResourcePaths.Length * request.Views.Length, data.Length);
            // Derived and original resource paths both encode the requested period before '#'.
            Assert.True(DataModelUtilities.TryParseResourcePath(request.ResourcePaths[0], out var parsed));
            long period = parsed.SamplePeriod.Ticks;
            Assert.Equal((request.End - request.Begin).Ticks / period, rows.Last().Offset);
            Assert.Equal(1, rows.Last().Progress);
            var progress = rows.Where(row => row.Kind == 1 && row.Resource == -1).Select(row => row.Progress).ToArray();
            Assert.True(progress.Zip(progress.Skip(1)).All(pair => pair.First <= pair.Second));
            for (int resource = 0; resource < request.ResourcePaths.Length; resource++)
            {
                var completed = Assert.Single(rows, row => row.Kind == 1 && row.Resource == resource);
                Assert.Equal(-1, completed.View);
                Assert.Equal(1, completed.Progress);
                Assert.Equal("complete", completed.Message);
            }
            for (int resource = 0; resource < request.ResourcePaths.Length; resource++)
            for (int view = 0; view < request.Views.Length; view++)
            {
                var row = Assert.Single(data, row => row.Resource == resource && row.View == view);
                long begin = (request.Views[view].Begin - request.Begin).Ticks / period;
                long end = (request.Views[view].End - request.Begin).Ticks / period;
                Assert.Equal(begin, row.Offset);
                Assert.Equal(1, row.Progress);
                Assert.Equal("complete", row.Message);
                Assert.Equal(row.Indices.Length, row.Values.Length);
                Assert.InRange(row.Values.Length, 1, request.Views[view].MaxPoints);
                Assert.All(row.Indices, index => Assert.InRange(index, begin, end - 1));
                Assert.True(row.Indices.Zip(row.Indices.Skip(1)).All(pair => pair.First < pair.Second));
            }
        }

        public void AssertReleased()
        {
            Assert.Equal(0, Active);
            Assert.Equal(Instances, Disposed);
            Assert.Empty(Allocations);
            Assert.Equal(Cache.Options.MaxConcurrentReads, Cache.Reads.CurrentCount);
            Assert.Equal(Cache.Options.MaxComputeWorkers, Cache.Compute.CurrentCount);
            Assert.Equal(Cache.Options.MaxConcurrentRequests, Cache.Requests.CurrentCount);
        }

        public void Dispose()
        {
            try
            {
                AssertReleased();
                DerivedCache.VerifyNoOtherCalls();
            }
            finally { Cache.Dispose(); }
        }
    }

    private sealed class ErrorLogger(ConcurrentQueue<Exception> errors) : ILogger<VisualizationService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is not null)
                errors.Enqueue(exception);
        }
    }
}
