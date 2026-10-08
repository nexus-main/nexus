// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text.Json;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
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

public class VisualizationServiceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EndpointTimingIsOptInCorrelatedAndBounded(bool enabled)
    {
        using var fixture = new Fixture { Delay = TimeSpan.Zero };
        fixture.Configuration = new Dictionary<string, JsonElement> { ["secret-key"] = JsonSerializer.SerializeToElement("secret-value") };
        using var logs = new VisualizationTimingLogger();
        var request = fixture.Request([fixture.View("secret-view", 0, 65536, 100)]);

        foreach (string cacheOutcome in new[] { "miss", "hit" })
        {
            using var stream = new MemoryStream();
            var context = new DefaultHttpContext();
            context.Response.Body = stream;
            string id = Guid.NewGuid().ToString("D");

            if (enabled)
                context.Request.Headers[VisualizationTiming.Header] = id;

            var controller = new Nexus.Controllers.V2.DataController(Mock.Of<IDataService>(), logs)
            {
                ControllerContext = new ControllerContext { HttpContext = context }
            };
            Assert.IsType<EmptyResult>(await controller.GetVisualizationAsync(request, fixture.Service, CancellationToken.None));

            if (!enabled)
            {
                Assert.Empty(logs.Entries);
                continue;
            }

            var entries = logs.Entries.Where(entry => Equals(entry.Fields["RequestId"], id)).ToArray();
            Assert.Equal(id, context.Response.Headers[VisualizationTiming.Header]);
            Assert.Equal(cacheOutcome, Assert.Single(entries, entry => entry.Phase == "cache-get" && entry.Milestone == "end").Outcome);
            Assert.Equal("request", entries.Last().Phase);
            Assert.Equal("success", entries.Last().Outcome);
            Assert.All(entries, entry =>
            {
                Assert.Equal(VisualizationTiming.Category, entry.Category);
                Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, entry.Level);
                Assert.Null(entry.Exception);
                Assert.Contains(id, entry.Message);
                Assert.Contains(entry.Phase, entry.Message);
                Assert.Contains(entry.Outcome, entry.Message);
                Assert.DoesNotContain("secret", entry.Message);
                Assert.DoesNotContain("/A/B/C", entry.Message);
                Assert.DoesNotContain("Administrator", entry.Message);
                Assert.InRange((double)entry.Fields["DurationMs"]!, 0, (double)entry.Fields["ElapsedMs"]!);
            });

            foreach (string phase in new[] { "prepare", "request-admission", "join-build", "initial-progress-flush",
                "build-gate-wait", "first-data-flush", "completion-flush", "resources-released" })
                Assert.Contains(entries, entry => entry.Phase == phase && entry.Outcome != "pending");

            if (cacheOutcome == "miss")
            {
                Assert.Contains(entries, entry => entry.Phase == "cache-put" && entry.Outcome == "success");
                var reads = entries.Where(entry => entry.Phase == "controller-read-inclusive" && entry.Milestone == "aggregate").ToArray();
                Assert.Equal(fixture.Calls, reads.Sum(entry => (long)entry.Fields["Count"]!));
                Assert.Equal(fixture.Instances, reads.Length);
                Assert.Equal(reads.Length, reads.Select(entry => entry.Fields["WorkerId"]).Distinct().Count());
                Assert.InRange(entries.Length, 1, 120); // Independent of the hundreds of source reads.

                foreach (var read in reads)
                {
                    int worker = (int)read.Fields["WorkerId"]!;
                    foreach (string phase in new[] { "read-permit-wait", "source-initialize", "controller-read-inclusive",
                        "slice-compute-wait", "slice-compute", "source-dispose" })
                    {
                        Assert.Single(entries, entry => Equals(entry.Fields["WorkerId"], worker) && entry.Phase == phase && entry.Milestone == "start");
                        Assert.Single(entries, entry => Equals(entry.Fields["WorkerId"], worker) && entry.Phase == phase && entry.Milestone == "end");
                    }
                }
            }
            else
                Assert.DoesNotContain(entries, entry => entry.Phase == "controller-read-inclusive");
        }
    }

    [Fact]
    public async Task TimingRecordsCancellationBeforeDrainAndResourceRelease()
    {
        using var fixture = new Fixture { Delay = TimeSpan.FromSeconds(30), HoldCancelledReads = true };
        using var logs = new VisualizationTimingLogger();
        using var cancellation = new CancellationTokenSource();
        using var stream = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Request.Headers[VisualizationTiming.Header] = Guid.NewGuid().ToString("D");
        context.Response.Body = stream;
        var controller = new Nexus.Controllers.V2.DataController(Mock.Of<IDataService>(), logs)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
        var running = controller.GetVisualizationAsync(fixture.Request([fixture.View("main", 0, 65536, 100)]), fixture.Service, cancellation.Token);
        await fixture.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            await cancellation.CancelAsync();
            await fixture.ReadCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(logs.Entries, entry => entry.Phase == "cancellation-requested");
            Assert.DoesNotContain(logs.Entries, entry => entry.Phase == "resources-released" || entry.Phase == "request");
            Assert.False(running.IsCompleted);
            Assert.True(fixture.Active > 0);
            Assert.True(fixture.Cache.Reads.CurrentCount < 3);
            Assert.Equal(3, fixture.Cache.Requests.CurrentCount);
        }
        finally
        {
            fixture.ReleaseCancelledReads.TrySetResult();
        }

        Assert.IsType<Microsoft.AspNetCore.Mvc.EmptyResult>(await running.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(StatusCodes.Status499ClientClosedRequest, context.Response.StatusCode);
        var entries = logs.Entries.ToArray();
        int requested = System.Array.FindIndex(entries, entry => entry.Phase == "cancellation-requested");
        int drained = System.Array.FindIndex(entries, entry => entry.Phase == "cancellation-drain" && entry.Outcome == "drained");
        int released = System.Array.FindIndex(entries, entry => entry.Phase == "resources-released");
        Assert.True(requested < drained && drained < released && released < entries.Length - 1);
        Assert.Equal("request", entries.Last().Phase);
        Assert.Equal("cancelled", entries.Last().Outcome);
        Assert.Contains(entries, entry => entry.Phase == "controller-read-inclusive" && entry.Outcome == "cancelled");
        Assert.DoesNotContain(entries, entry => entry.Phase == "first-data-flush" || entry.Phase == "completion-flush");
        Assert.Equal(0, fixture.Active);
        Assert.Equal(fixture.Instances, fixture.Disposed);
        Assert.Equal(3, fixture.Cache.Reads.CurrentCount);
        Assert.Equal(4, fixture.Cache.Requests.CurrentCount);
    }

    [Fact]
    public async Task TimingReportsStreamFailureRatherThanSuccess()
    {
        using var fixture = new Fixture { Fail = true, Delay = TimeSpan.Zero };
        using var logs = new VisualizationTimingLogger();
        using var stream = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Request.Headers[VisualizationTiming.Header] = Guid.NewGuid().ToString("D");
        context.Response.Body = stream;
        var controller = new Nexus.Controllers.V2.DataController(Mock.Of<IDataService>(), logs)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
        await controller.GetVisualizationAsync(fixture.Request([fixture.View("main", 0, 65536, 100)]), fixture.Service, CancellationToken.None);
        Assert.Equal("stream-error", logs.Entries.Last().Outcome);
        Assert.Contains(logs.Entries, entry => entry.Phase == "controller-read-inclusive" && entry.Outcome == "error");
        Assert.All(logs.Entries, entry =>
        {
            Assert.Null(entry.Exception);
            Assert.DoesNotContain("source failed", entry.Message);
        });
    }

    [Fact]
    public async Task TimingRecordsBusyAdmissionWithoutStartingWork()
    {
        using var fixture = new Fixture();
        using var logs = new VisualizationTimingLogger();
        using var stream = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Request.Headers[VisualizationTiming.Header] = Guid.NewGuid().ToString("D");
        context.Response.Body = stream;
        var controller = new Nexus.Controllers.V2.DataController(Mock.Of<IDataService>(), logs)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

        for (int i = 0; i < 4; i++)
            await fixture.Cache.Requests.WaitAsync();

        try
        {
            var result = await controller.GetVisualizationAsync(fixture.Request([fixture.View("main", 0, 65536, 100)]), fixture.Service, CancellationToken.None);
            Assert.Equal(422, Assert.IsType<ContentResult>(result).StatusCode);
            Assert.Contains(logs.Entries, entry => entry.Phase == "request-admission" && entry.Outcome == "busy");
            Assert.Equal("rejected", logs.Entries.Last().Outcome);
            Assert.DoesNotContain(logs.Entries, entry => entry.Phase == "join-build" || entry.Phase == "resources-released");
            Assert.Equal(0, fixture.Instances);
            Assert.Equal(0, fixture.Cache.Requests.CurrentCount);
            Assert.Equal(0, stream.Length);
        }
        finally
        {
            fixture.Cache.Requests.Release(4);
        }
    }

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
            Assert.Equal(expected.Select(point => point.Value), row.Values);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingBuildOwnerDoesNotRestartOtherConsumersScan(bool disableCache)
    {
        using var fixture = new Fixture(memoryLimit: disableCache ? 0 : 16 * 1024 * 1024) { Delay = TimeSpan.FromMilliseconds(10) };
        using var logs = new VisualizationTimingLogger();
        var timing = new VisualizationTiming(logs.CreateLogger(VisualizationTiming.Category), Guid.NewGuid());

        if (disableCache)
            fixture.Cache.Options.DiskLimitBytes = 0;

        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        using var cancellation = new CancellationTokenSource();
        var write = await fixture.Service.PrepareAsync(request, cancellation.Token);
        using var stream = new MemoryStream();
        var owner = write(stream, cancellation.Token);
        await fixture.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var waiter = fixture.RunAsync(request, timing);
        Assert.Equal(2, fixture.Cache.Requests.CurrentCount);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(2, (await waiter).Last().Kind);
        Assert.Equal(65536 * 2, fixture.Samples);
        Assert.Equal(fixture.Instances, fixture.Disposed);
        Assert.Equal(disableCache ? "shared" : "hit",
            Assert.Single(logs.Entries, entry => entry.Phase == "cache-get" && entry.Milestone == "end").Outcome);
        Assert.Contains(logs.Entries, entry => entry.Phase == "build-gate-wait" && entry.Milestone == "end");
        Assert.DoesNotContain(logs.Entries, entry => entry.Phase == "controller-read-inclusive");
    }

    [Fact]
    public async Task CancellingWaiterDoesNotCancelOwner()
    {
        using var fixture = new Fixture();
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        var owner = fixture.RunAsync(request);
        await fixture.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var write = await fixture.Service.PrepareAsync(request, cancellation.Token);
        using var stream = new MemoryStream();
        var waiter = write(stream, cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        Assert.Equal(2, (await owner).Last().Kind);
        Assert.Equal(65536 * 2, fixture.Samples);
    }

    [Fact]
    public void CachePinsRemainAccountedUntilConsumerReleasesThem()
    {
        var dataset = new VisualizationDataset(0, 65536, 1);
        dataset.AllocateBase();
        dataset.BuildParents(CancellationToken.None);
        using var fixture = new Fixture(memoryLimit: dataset.Bytes);
        fixture.Cache.Options.DiskLimitBytes = 0;
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

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(40)]
    [InlineData(-1)]
    public async Task MalformedSnapshotsAreDiscardedAndRebuilt(int offset)
    {
        using var fixture = new Fixture(memoryLimit: 0) { Delay = TimeSpan.Zero };
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        await fixture.RunAsync(request);
        string path = Assert.Single(Directory.GetFiles(Path.Combine(fixture.Directory, "visualization-v1")));
        using (var file = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
        {
            if (offset < 0)
                file.SetLength(25);
            else
            {
                file.Position = offset;
                int value = file.ReadByte();
                file.Position = offset;
                file.WriteByte((byte)(value ^ 1));
            }
        }

        Assert.Equal(2, (await fixture.RunAsync(request)).Last().Kind);
        Assert.Equal(65536 * 4, fixture.Samples);
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

    [Fact]
    public async Task AuthenticationSchemeIsPartOfCacheIdentity()
    {
        using var fixture = new Fixture { Delay = TimeSpan.Zero };
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        await fixture.RunAsync(request);
        var other = fixture.CreateService(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, nameof(NexusRoles.Administrator))], "other")));
        var write = await other.PrepareAsync(request, CancellationToken.None);
        using var stream = new MemoryStream();
        await write(stream, CancellationToken.None);
        Assert.Equal(65536 * 4, fixture.Samples);
    }

    [Fact]
    public async Task UnavailableDiskCacheDoesNotFailSourceRead()
    {
        using var fixture = new Fixture { Delay = TimeSpan.Zero };
        Directory.CreateDirectory(fixture.Directory);
        using (File.Create(Path.Combine(fixture.Directory, "visualization-v1"))) { }
        Assert.Equal(2, (await fixture.RunAsync(fixture.Request([fixture.View("main", 0, 65536, 100)]))).Last().Kind);
    }

    [Fact]
    public async Task SnapshotFromAnotherIdentityCannotBeReusedUnderDifferentKey()
    {
        using var fixture = new Fixture(memoryLimit: 0) { Delay = TimeSpan.Zero };
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        await fixture.RunAsync(request);
        string directory = Path.Combine(fixture.Directory, "visualization-v1");
        string first = Assert.Single(Directory.GetFiles(directory));
        fixture.Configuration = new Dictionary<string, JsonElement> { ["tenant"] = JsonSerializer.SerializeToElement("other") };
        await fixture.RunAsync(request);
        string second = Assert.Single(Directory.GetFiles(directory), path => path != first);
        File.Copy(first, second, overwrite: true);
        Assert.Equal(2, (await fixture.RunAsync(request)).Last().Kind);
        Assert.Equal(65536 * 6, fixture.Samples);
    }

    [Fact]
    public async Task WarmDetailCoalescesAdjacentBucketsIntoBudgetedSlices()
    {
        using var fixture = new Fixture(targetReadBytes: 131072) { Delay = TimeSpan.Zero };
        await fixture.RunAsync(fixture.Request([fixture.View("main", 0, 65536, 100)]));
        int calls = fixture.Calls;
        Assert.Equal(2, (await fixture.RunAsync(fixture.Request([fixture.View("detail", 8192, 16384, 32768)]))).Last().Kind);
        Assert.Equal(1, fixture.Calls - calls); // One output-budgeted slice, not 32 individual buckets.
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
            .SelectMany(index => VisualizationReduction.Project(
                VisualizationReduction.Summarize([values[index % values.Length]], origin + index)))
            .ToArray();
        using var logs = new VisualizationTimingLogger();
        Row[]? cold = null;

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

                if (cold is not null)
                {
                    Assert.Equal(cold[row.Resource].Indices, row.Indices);
                    Assert.Equal(cold[row.Resource].Values.Select(BitConverter.SingleToInt32Bits),
                        row.Values.Select(BitConverter.SingleToInt32Bits));
                }
            }

            cold = data;
        }
    }

    [Fact]
    public async Task SyntheticSourceThroughputBenchmark()
    {
        if (Environment.GetEnvironmentVariable("NEXUS_VISUALIZATION_BENCHMARK") != "1")
            return;

        using (var warmup = new Fixture { Delay = TimeSpan.Zero })
            await warmup.RunAsync(warmup.Request([warmup.View("main", 0, 65536, 100)]));

        foreach (int delay in new[] { 0, 2 })
        foreach (int reads in new[] { 1, 2, 4, 8 })
        foreach (int compute in new[] { 1, 2, 4 })
        {
            var measurements = new List<double>();

            for (int iteration = 0; iteration < 3; iteration++)
            {
                using var fixture = new Fixture(readWorkers: reads, computeWorkers: compute, targetReadBytes: 1024 * 1024)
                {
                    Delay = TimeSpan.FromMilliseconds(delay)
                };
                fixture.Cache.Options.DiskLimitBytes = 0;
                const int count = 4 * 1024 * 1024;
                var request = fixture.Request([fixture.View("main", 0, count, 1000)]) with { End = fixture.Begin.AddSeconds(count) };
                var watch = Stopwatch.StartNew();
                Assert.Equal(2, (await fixture.RunAsync(request)).Last().Kind);
                watch.Stop();
                Assert.Equal(count * 2, fixture.Samples);
                Assert.Equal(fixture.Instances, fixture.Disposed);
                measurements.Add(watch.Elapsed.TotalMilliseconds);
            }

            double median = measurements.Order().ElementAt(1);
            output.WriteLine($"delay={delay}ms reads={reads} compute={compute}: median={median:F1}ms, {8 * 1024 * 1024 / median / 1000:F2} Msamples/s");
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
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]) with { ResourcePaths = ["/A/B/C/T1/1_s"] };
        Assert.Equal(cycle ? 3 : 2, (await fixture.RunAsync(request)).Last().Kind);
        Assert.Equal(fixture.Instances, fixture.Disposed);
        Assert.Equal(1, fixture.Cache.Reads.CurrentCount);
        Assert.Equal(0, fixture.Active);
    }

    [Fact]
    public async Task MixedOriginalAndDerivedResourcesNeverReadSameInstanceConcurrently()
    {
        using var fixture = new Fixture();
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]) with
        {
            ResourcePaths = ["/A/B/C/T1/1_s", "/A/B/C/T2/1_s_resampled#base=2_s"]
        };
        Assert.Equal(2, (await fixture.RunAsync(request)).Last().Kind);
        Assert.Equal(fixture.Instances, fixture.Disposed);
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
    public async Task SerializesDerivedReadsAndBypassesUnsafeDerivedCache()
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
    }

    [Fact]
    public async Task FailedDerivedReadsAreNotCachedAsGaps()
    {
        using var fixture = new Fixture { Fail = true };
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]) with
        {
            ResourcePaths = ["/A/B/C/T1/2_s_mean#base=1_s"]
        };
        Assert.Equal(3, (await fixture.RunAsync(request)).Last().Kind);
        fixture.Fail = false;
        var rows = await fixture.RunAsync(request);
        Assert.Equal(2, rows.Last().Kind);
        Assert.Contains(rows.Where(row => row.Kind == 0).SelectMany(row => row.Values), float.IsFinite);
    }

    [Fact]
    public async Task DeduplicatesSimultaneousBuildsAndHonorsGlobalReadLimit()
    {
        using var fixture = new Fixture();
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        var results = await Task.WhenAll(fixture.RunAsync(request), fixture.RunAsync(request));
        Assert.Equal(65536 * 2, fixture.Samples);
        Assert.InRange(fixture.Peak, 1, 3);
        Assert.All(results, rows => Assert.Equal(2, rows.Last().Kind));
    }

    [Fact]
    public async Task ReadsOnceForAllViewsWithIndependentWorkersAndWarmZoom()
    {
        using var fixture = new Fixture();
        var request = fixture.Request([
            fixture.View("main", 0, 65536, 1000),
            fixture.View("detail", 137, 211, 1000),
            fixture.View("nav", 0, 65536, 100)]);
        var rows = await fixture.RunAsync(request);

        Assert.Equal(65536 * 2, fixture.Samples);
        Assert.InRange(fixture.Peak, 2, 3);
        Assert.InRange(fixture.Instances, 2, 3);
        Assert.Equal(fixture.Instances, fixture.Disposed);
        Assert.Equal(6, rows.Count(row => row.Kind == 0));
        Assert.Equal(2, rows.Last().Kind);

        foreach (var row in rows.Where(row => row.Kind == 0))
        {
            Assert.True(row.Indices.SequenceEqual(row.Indices.Order()));
            Assert.InRange(row.Values.Length, 1, request.Views[row.View].MaxPoints);
            Assert.Equal("complete", row.Message);
            Assert.Equal(0, row.Minimum);
            Assert.Equal(999, row.Maximum);
        }

        var detail = rows.Single(row => row.Kind == 0 && row.Resource == 0 && row.View == 1);
        Assert.Equal(Enumerable.Range(137, 74).Select(i => (long)i), detail.Indices);
        Assert.Equal(Enumerable.Range(137, 74).Select(i => (float)i), detail.Values);
        Assert.Contains(rows.Where(row => row.Kind == 0 && row.View == 0).SelectMany(row => row.Values), float.IsNaN);

        int samples = fixture.Samples;
        var warm = await fixture.RunAsync(fixture.Request([fixture.View("zoom", 8192, 32768, 50)]));
        Assert.Equal(samples, fixture.Samples);
        Assert.Equal(2, warm.Last().Kind);

        await fixture.RunAsync(fixture.Request([fixture.View("fine", 8193, 16397, 1000)]));
        Assert.InRange(fixture.Samples - samples, 1, 17000);
    }

    [Fact]
    public async Task UsesDiskCacheAndExpiresAndIsolatesRequestConfiguration()
    {
        using var fixture = new Fixture(memoryLimit: 0);
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        await fixture.RunAsync(request);
        int samples = fixture.Samples;
        await fixture.RunAsync(request);
        Assert.Equal(samples, fixture.Samples);

        fixture.Configuration = new Dictionary<string, JsonElement> { ["tenant"] = JsonSerializer.SerializeToElement("other") };
        await fixture.RunAsync(request);
        Assert.Equal(samples * 2, fixture.Samples);

        foreach (string path in Directory.GetFiles(Path.Combine(fixture.Directory, "visualization-v1")))
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow - TimeSpan.FromHours(1));

        await fixture.RunAsync(request);
        Assert.Equal(samples * 3, fixture.Samples);
    }

    [Fact]
    public async Task ErrorsDoNotBecomeCachedNaNsOrSuccess()
    {
        using var fixture = new Fixture { Fail = true };
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        var failed = await fixture.RunAsync(request);
        Assert.Equal(3, failed.Last().Kind);
        Assert.DoesNotContain(failed, row => row.Kind is 0 or 2);
        fixture.Fail = false;
        var success = await fixture.RunAsync(request);
        Assert.Equal(2, success.Last().Kind);
        Assert.Equal(fixture.Instances, fixture.Disposed);
    }

    [Fact]
    public async Task CancellationDrainsWorkersAndReleasesGlobalBudgets()
    {
        using var fixture = new Fixture { Delay = TimeSpan.FromSeconds(30) };
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        using var cancellation = new CancellationTokenSource();
        var write = await fixture.Service.PrepareAsync(request, cancellation.Token);
        using var output = new MemoryStream();
        var running = write(output, cancellation.Token);
        await fixture.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, fixture.Active);
        Assert.Equal(fixture.Instances, fixture.Disposed);
        Assert.Equal(3, fixture.Cache.Reads.CurrentCount);
        Assert.Equal(4, fixture.Cache.Requests.CurrentCount);

        fixture.Delay = TimeSpan.Zero;
        Assert.Equal(2, (await fixture.RunAsync(request)).Last().Kind);
    }

    [Fact]
    public async Task FlushesProgressBeforeSlowReadFinishes()
    {
        using var fixture = new Fixture { Delay = TimeSpan.FromSeconds(30) };
        using var cancellation = new CancellationTokenSource();
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        var write = await fixture.Service.PrepareAsync(request, cancellation.Token);
        var pipe = new System.IO.Pipelines.Pipe();
        var running = write(pipe.Writer.AsStream(), cancellation.Token);
        using var reader = new ArrowStreamReader(pipe.Reader.AsStream());
        using var first = await reader.ReadNextRecordBatchAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        using var next = await reader.ReadNextRecordBatchAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, ((Int32Array)first.Column(0)).GetValue(0));
        Assert.Equal(1, ((Int32Array)next.Column(0)).GetValue(0));
        Assert.False(running.IsCompleted);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task EndpointStreamsFixedNonnullableSchemaAndRejectsInvalidInput()
    {
        using var fixture = new Fixture();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var controller = new Nexus.Controllers.V2.DataController(Mock.Of<IDataService>(), NullLoggerFactory.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        Assert.IsType<EmptyResult>(await controller.GetVisualizationAsync(request, fixture.Service, CancellationToken.None));
        Assert.Equal("application/vnd.apache.arrow.stream", context.Response.ContentType);
        context.Response.Body.Position = 0;
        using var reader = new ArrowStreamReader(context.Response.Body);
        using var batch = await reader.ReadNextRecordBatchAsync();
        Assert.Equal("1", reader.Schema.Metadata["visualizationVersion"]);
        Assert.Equal(new[] { "kind", "resourceIndex", "viewIndex", "offset", "indices", "values", "progress", "minimum", "maximum", "message" }, reader.Schema.FieldsList.Select(field => field.Name));
        Assert.All(reader.Schema.FieldsList, field => Assert.False(field.IsNullable));
        Assert.IsType<Int64Array>(((ListArray)batch.Column(4)).Values);
        Assert.IsType<FloatArray>(((ListArray)batch.Column(5)).Values);
        Assert.Empty(((ListArray)batch.Column(4)).Values.Data.Buffers[1].Span.ToArray());

        var invalid = request with { Views = [fixture.View("bad", 0, 65536, 32769)] };
        var invalidResult = Assert.IsType<ContentResult>(await controller.GetVisualizationAsync(invalid, fixture.Service, CancellationToken.None));
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
        await fixture.RunAsync(request);
        var denied = fixture.CreateService(new ClaimsPrincipal(new ClaimsIdentity([], "test")));
        var exception = await Assert.ThrowsAsync<Exception>(() => denied.PrepareAsync(request, CancellationToken.None));
        Assert.StartsWith("The current user is not permitted", exception.Message);
    }

    [Fact]
    public async Task SliceSizingUsesOutputBytesInsteadOfWorkingMemoryEstimate()
    {
        using var fixture = new Fixture(readWorkers: 1, targetReadBytes: 65536) { Delay = TimeSpan.Zero };
        var request = fixture.Request([fixture.View("main", 0, 65536, 100)]);
        int expectedSliceSamples = 65536 / (sizeof(float) * request.ResourcePaths.Length) / 256 * 256;
        int expectedCalls = (65536 + expectedSliceSamples - 1) / expectedSliceSamples;

        await fixture.RunAsync(request);

        Assert.Equal(expectedCalls, fixture.Calls);
    }

    private sealed record Row(int Kind, int Resource, int View, long[] Indices, float[] Values, float Minimum, float Maximum, string Message);

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "nexus-visualization-test-" + Guid.NewGuid());
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
        public bool HoldCancelledReads;
        public Func<long, float>? SampleValue;
        public TimeSpan Delay = TimeSpan.FromMilliseconds(5);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReadCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCancelledReads { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyDictionary<string, JsonElement>? Configuration;
        private readonly Mock<IDataControllerService> _controllers = new();
        private readonly AppState _state;

        public Fixture(long memoryLimit = 16 * 1024 * 1024, bool restrictRange = false,
            int readWorkers = 3, int computeWorkers = 2, int targetReadBytes = 65536)
        {
            var options = new DataOptions
            {
                Visualization = new VisualizationOptions { TargetReadBytes = targetReadBytes, MaxConcurrentReads = readWorkers,
                    MaxComputeWorkers = computeWorkers, MemoryLimitBytes = memoryLimit }
            };
            Cache = new VisualizationCache(Options.Create(options), Options.Create(new PathsOptions { Cache = Directory }));
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
                                Assert.Equal(1, Interlocked.Increment(ref reading));
                                Interlocked.Increment(ref Calls);
                                int active = Interlocked.Increment(ref Active);
                                int previous;

                                do { previous = Peak; }
                                while (active > previous && Interlocked.CompareExchange(ref Peak, active, previous) != previous);

                                try
                                {
                                    if (restrictRange)
                                    {
                                        Assert.True(from >= Begin.AddSeconds(1024));
                                        Assert.True(to <= Begin.AddSeconds(2048));
                                    }

                                    Started.TrySetResult();
                                    try { await Task.Delay(Delay, ct); }
                                    catch (OperationCanceledException) when (HoldCancelledReads)
                                    {
                                        ReadCancelled.TrySetResult();
                                        await ReleaseCancelledReads.Task;
                                        throw;
                                    }

                                    if (Fail)
                                        throw new InvalidOperationException("source failed");

                                    int count = (int)(to - from).TotalSeconds;
                                    Interlocked.Add(ref Samples, count * requests.Length);

                                    if (DynamicDependencies && (CyclicDependencies || requests.Any(request => request.CatalogItem.Resource.Id == "T1")))
                                    {
                                        var first = readData("/A/B/C/T2/1_s", from, to, new double[count], ct);

                                        if (SiblingDependencies)
                                            await Task.WhenAll(first, readData("/A/B/C/T2/1_s", from, to, new double[count], ct));
                                        else
                                            await first;
                                    }

                                    foreach (var request in requests)
                                    {
                                        var values = MemoryMarshal.Cast<byte, float>(request.Data.Span);

                                        for (int i = 0; i < values.Length; i++)
                                        {
                                            long index = (long)(from - Begin).TotalSeconds + i;
                                            values[i] = SampleValue is not null ? SampleValue(index)
                                                : index is >= 500 and < 510 ? float.PositiveInfinity : index % 1000;
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
                            new ProcessingService(Options.Create(options)), Mock.Of<ICacheService>(), options,
                            NullLogger<DataSourceController>.Instance, strictReads: true);
                        await controller.InitializeAsync(new ConcurrentDictionary<string, ResourceCatalog>(catalogs.Select(current => KeyValuePair.Create(current.Id, current))), NullLoggerFactory.Instance, token);
                        return controller;
                    });
            var manager = new Mock<ICatalogManager>();
            manager.Setup(current => current.GetCatalogContainersAsync(It.IsAny<CatalogContainer>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new CatalogContainer(new CatalogRegistration(catalog.Id, "",
                    MinBegin: restrictRange ? Begin.AddSeconds(1024) : null,
                    MaxEnd: restrictRange ? Begin.AddSeconds(2048) : null), Guid.Empty, pipeline, [],
                    new CatalogMetadata(null, null, null), manager.Object, default!, _controllers.Object)]);
            _state = new AppState { CatalogState = new CatalogState(CatalogContainer.CreateRoot(manager.Object, default!), new CatalogCache()) };
            Service = CreateService(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, nameof(NexusRoles.Administrator))], "test")));
        }

        public VisualizationService CreateService(ClaimsPrincipal user) => new(_state, user, _controllers.Object,
            Mock.Of<IAcceptedLicenseService>(), Cache, NullLogger<VisualizationService>.Instance);

        public VisualizationView View(string id, int begin, int end, int points) => new(id, Begin.AddSeconds(begin), Begin.AddSeconds(end), points);
        public VisualizationRequest Request(VisualizationView[] views) => new(Begin, Begin.AddSeconds(65536), ["/A/B/C/T1/1_s", "/A/B/C/T2/1_s"], views);

        public async Task<List<Row>> RunAsync(VisualizationRequest request, VisualizationTiming? timing = null)
        {
            var write = await Service.PrepareAsync(request, CancellationToken.None, timing);
            using var stream = new MemoryStream();
            await write(stream, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15));
            stream.Position = 0;
            using var reader = new ArrowStreamReader(stream);
            var rows = new List<Row>();
            RecordBatch batch;

            while ((batch = await reader.ReadNextRecordBatchAsync()) is not null)
            {
                using (batch)
                {
                    rows.Add(new Row(((Int32Array)batch.Column(0)).GetValue(0)!.Value,
                        ((Int32Array)batch.Column(1)).GetValue(0)!.Value, ((Int32Array)batch.Column(2)).GetValue(0)!.Value,
                        ((Int64Array)((ListArray)batch.Column(4)).Values).Values.ToArray(),
                        ((FloatArray)((ListArray)batch.Column(5)).Values).Values.ToArray(),
                        ((FloatArray)batch.Column(7)).GetValue(0)!.Value, ((FloatArray)batch.Column(8)).GetValue(0)!.Value,
                        ((StringArray)batch.Column(9)).GetString(0)));
                }
            }

            return rows;
        }

        public void Dispose()
        {
            Cache.Dispose();

            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
