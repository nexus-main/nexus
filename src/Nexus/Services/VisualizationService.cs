// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using Nexus.Core;
using Nexus.Core.V2;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Utilities;

namespace Nexus.Services;

internal interface IVisualizationService
{
    Task<Func<Stream, CancellationToken, Task>> PrepareAsync(VisualizationRequest request, CancellationToken cancellationToken);
}

internal sealed class VisualizationService(
    AppState appState,
    ClaimsPrincipal user,
    IDataControllerService controllers,
    IAcceptedLicenseService licenses,
    VisualizationCache cache,
    ILogger<VisualizationService> logger) : IVisualizationService
{
    public async Task<Func<Stream, CancellationToken, Task>> PrepareAsync(VisualizationRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request, cache.Options);
        var catalogState = appState.CatalogState;
        var root = catalogState.Root;
        IReadOnlyDictionary<string, JsonElement>? configuration;

        try { configuration = controllers.CaptureRequestConfiguration(); }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new ValidationException("The Nexus-Configuration header is invalid.", ex);
        }
        var items = new CatalogItemRequest[request.ResourcePaths.Length];

        for (int i = 0; i < items.Length; i++)
            items[i] = await ResolveAsync(request.ResourcePaths[i], cancellationToken);

        var period = items[0].Item.Representation.SamplePeriod;

        if (items.Any(item => item.Item.Representation.SamplePeriod != period))
            throw new ValidationException("All representations must be of the same sample period.");

        DataSourceController.ValidateParameters(request.Begin, request.End, period);

        foreach (var view in request.Views)
            DataSourceController.ValidateParameters(view.Begin, view.End, period);

        long begin = request.Begin.Ticks / period.Ticks;
        long end = request.End.Ticks / period.Ticks;
        long baseCount = (end - 1) / 256 - begin / 256 + 1;
        if (end - begin > cache.Options.MaxSamples / items.Length)
            throw new ValidationException("The visualization domain exceeds the configured work or summary-memory limit.");

        VisualizationDataset.EstimateBytes(begin, end, items.Length, cache.Options.MaxDatasetBytes);
        long bytesPerSample;

        try { bytesPerSample = items.Sum(EstimateBytesPerSample); }
        catch (OverflowException ex) { throw new ValidationException("The representations exceed the visualization read budget.", ex); }
        int sliceSamples = (int)Math.Min(1_048_576, cache.Options.TargetReadBytes / bytesPerSample / 256 * 256);

        if (sliceSamples < 256)
            throw new ValidationException("The representations require more memory than the configured visualization read budget.");

        var views = request.Views.Select(view => new ViewPlan(
            view.Begin.Ticks / period.Ticks, view.End.Ticks / period.Ticks, view.MaxPoints)).ToArray();
        var rawBuckets = new HashSet<long>();

        foreach (var view in views)
        {
            if (view.Stride < 256)
            {
                for (long bucket = view.Begin / 256; bucket <= (view.End - 1) / 256; bucket++)
                    rawBuckets.Add(bucket);
            }
            else
            {
                if (view.Begin % 256 != 0)
                    rawBuckets.Add(view.Begin / 256);

                if (view.End % 256 != 0)
                    rawBuckets.Add((view.End - 1) / 256);
            }
        }

        if ((long)rawBuckets.Count * 256 * sizeof(float) * items.Length > cache.Options.MaxDatasetBytes)
            throw new ValidationException("The requested detail views exceed the visualization memory budget.");

        // Views deliberately do not enter the key: zooms reuse the domain pyramid.
        // A process epoch also prevents reuse after extension reload/restart with an
        // unversioned binary format; catalog/configuration and user claims isolate data.
        string key = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            cache.Epoch,
            CatalogEpoch = cache.GetCatalogEpoch(catalogState),
            Version = 2,
            Begin = begin,
            End = end,
            Period = period.Ticks,
            Configuration = configuration,
            User = user.Identities.Select(identity => new
            {
                identity.AuthenticationType, identity.IsAuthenticated, identity.NameClaimType, identity.RoleClaimType,
                Claims = identity.Claims.Select(claim => new
                {
                    claim.Type, claim.Value, claim.ValueType, claim.Issuer, claim.OriginalIssuer, claim.Properties
                }).OrderBy(claim => claim.Type).ThenBy(claim => claim.Value).ThenBy(claim => claim.Issuer)
            }),
            Items = items.Select(item => new
            {
                item.Item, item.BaseItem, item.SourceItem, item.SourceBaseItem,
                item.Container.Id, item.Container.BackingSourceId, item.Container.MinBegin, item.Container.MaxEnd,
                item.Container.PipelineId, item.Container.Pipeline, item.Container.PackageReferenceIds,
                item.Container.Metadata
            })
        })));

        return WriteAsync;

        async Task<CatalogItemRequest> ResolveAsync(string path, CancellationToken token)
        {
            var item = await root.TryFindAsync(root, path, token)
                ?? throw new Exception($"Could not find resource path {path}.");

            if (!await AuthUtilities.IsCatalogReadableAsync(item.Container, user, licenses, token))
                throw new Exception($"The current user is not permitted to access the catalog {item.Container.Id}.");

            return item;
        }

        async Task WriteAsync(Stream output, CancellationToken token)
        {
            if (!await cache.Requests.WaitAsync(0, token))
                throw new ValidationException("The visualization server is busy. Retry later.");

            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var (buildInterest, buildRegistration) = cache.JoinBuild(key, cancellation.Token);
            using var buildScope = buildRegistration;
            var buildToken = buildInterest.Cancellation.Token;
            using var writer = new ArrowStreamWriter(output, Schema, leaveOpen: true);
            Task? work = null;
            long completed = 0;
            long total = end - begin;
            int hasDynamicDependencies = 0;
            var raw = new ConcurrentDictionary<long, float[][]>();
            VisualizationDataset? dataset = null;
            VisualizationCache.Lease? datasetLease = null;

            try
            {
                await writer.WriteStartAsync(token);
                await EmitAsync(1, -1, -1, 0, [], 0, float.NaN, float.NaN, "", token);
                work = RunAsync(cancellation.Token);

                while (!work.IsCompleted)
                {
                    await Task.WhenAny(work, Task.Delay(250, token));
                    token.ThrowIfCancellationRequested();

                    if (!work.IsCompleted)
                        await EmitAsync(1, -1, -1, 0, [], Math.Min(1, Interlocked.Read(ref completed) / (double)total), float.NaN, float.NaN, "", token);
                }

                await work;

                for (int resource = 0; resource < items.Length; resource++)
                {
                    var range = dataset!.Levels[^1][resource][0];
                    await EmitAsync(1, resource, -1, 0, [], 1, range.Min, range.Max, "complete", token);

                    for (int viewIndex = 0; viewIndex < views.Length; viewIndex++)
                    {
                        var view = views[viewIndex];
                        var points = new List<VisualizationPoint>();
                        await cache.Compute.WaitAsync(token);

                        try
                        {
                            for (long start = view.Begin; start < view.End;)
                            {
                                token.ThrowIfCancellationRequested();
                                long stop = Math.Min(view.End, (start / view.Stride + 1) * view.Stride);
                                var summary = Query(dataset, raw, resource, start, stop);
                                points.AddRange(VisualizationReduction.Project(summary).Select(point => point with { Index = point.Index - begin }));
                                start = stop;
                            }
                        }
                        finally { cache.Compute.Release(); }

                        if (points.Count > request.Views[viewIndex].MaxPoints)
                            throw new InvalidOperationException("Visualization point budget exceeded.");

                        await EmitAsync(0, resource, viewIndex, view.Begin - begin, points.ToArray(), 1, range.Min, range.Max, "complete", token);
                    }
                }

                await EmitAsync(2, -1, -1, end - begin, [], 1, float.NaN, float.NaN, "complete", token);
                await writer.WriteEndAsync(token);
                await output.FlushAsync(token);
            }
            catch (Exception ex)
            {
                await cancellation.CancelAsync();

                if (work is not null)
                {
                    try { await work; }
                    catch { /* Observe workers before releasing their buffers and admission. */ }
                }

                if (token.IsCancellationRequested)
                    throw;

                logger.LogError(ex, "Visualization stream failed");
                await EmitAsync(3, -1, -1, 0, [], double.NaN, float.NaN, float.NaN, "Visualization read failed.", token);
                await writer.WriteEndAsync(token);
                await output.FlushAsync(token);
            }
            finally
            {
                datasetLease?.Dispose();
                buildScope.Dispose();
                cache.Requests.Release();
            }

            async Task EmitAsync(int kind, int resource, int view, long offset, VisualizationPoint[] points,
                double progress, float minimum, float maximum, string message, CancellationToken ct)
            {
                using var batch = CreateBatch(kind, resource, view, offset, points, progress, minimum, maximum, message);
                await writer.WriteRecordBatchAsync(batch, ct);
                await output.FlushAsync(ct);
            }

            async Task RunAsync(CancellationToken ct)
            {
                var buildGate = cache.Builds[Convert.ToByte(key[..2], 16) % cache.Builds.Length];
                await buildGate.WaitAsync(ct);

                try
                {
                    try { datasetLease = cache.Get(key); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        logger.LogWarning(ex, "Could not load visualization summaries");
                    }
                    dataset = datasetLease?.Dataset ?? buildInterest.Dataset;

                    if (dataset is null)
                    {
                        dataset = new VisualizationDataset(begin, end, items.Length);
                        dataset.AllocateBase();
                        await ScanAsync(cold: true, buildToken);
                        await cache.Compute.WaitAsync(buildToken);

                        try { dataset.BuildParents(buildToken); }
                        finally { cache.Compute.Release(); }

                        // Plugins may resolve arbitrary resources in readData callbacks.
                        // Their identity/authorization is not captured by the outer key.
                        if (Volatile.Read(ref hasDynamicDependencies) == 0)
                        {
                            buildInterest.Dataset = dataset;
                            try { datasetLease = cache.Put(key, dataset); }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                            {
                                logger.LogWarning(ex, "Could not persist visualization summaries");
                            }
                        }
                    }
                }
                finally
                {
                    buildGate.Release();
                }

                ct.ThrowIfCancellationRequested();

                if (rawBuckets.Any(bucket => !raw.ContainsKey(bucket)))
                {
                    Interlocked.Exchange(ref completed, 0);
                    total = Math.Max(1, rawBuckets.Count * 256L);
                    await ScanAsync(cold: false, ct);
                }
            }

            async Task ScanAsync(bool cold, CancellationToken ct)
            {
                long firstBucket = begin / 256;
                int bucketsPerSlice = sliceSamples / 256;
                long sliceCount = (baseCount + bucketsPerSlice - 1) / bucketsPerSlice;
                long next = -1;
                var missing = new List<(long First, long End)>();

                if (!cold)
                {
                    foreach (long bucket in rawBuckets.Where(bucket => !raw.ContainsKey(bucket)).Order())
                    {
                        if (missing.Count > 0 && missing[^1].End == bucket && bucket - missing[^1].First < bucketsPerSlice)
                            missing[^1] = (missing[^1].First, bucket + 1);
                        else
                            missing.Add((bucket, bucket + 1));
                    }
                }

                if (!cold)
                    sliceCount = missing.Count;

                using var scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var workers = Enumerable.Range(0, (int)Math.Min(sliceCount, cache.Options.MaxConcurrentReads))
                    .Select(_ => Task.Run(WorkerAsync)).ToArray();
                await Task.WhenAll(workers);

                async Task WorkerAsync()
                {
                    var owned = new List<(IDataSourceController Controller, int[] Indices)>();
                    using var dependencies = new SemaphoreSlim(1);
                    bool admitted = false;

                    try
                    {
                        await cache.Reads.WaitAsync(scanCancellation.Token);
                        admitted = true;

                        foreach (var group in items.Select((item, index) => (item, index)).GroupBy(entry => entry.item.Container.Pipeline))
                        {
                            var catalogs = group.SelectMany(entry => Catalogs(entry.item)).DistinctBy(catalog => catalog.Id).ToArray();
                            var controller = await controllers.GetVisualizationDataSourceControllerAsync(group.Key, configuration, catalogs, scanCancellation.Token);
                            owned.Add((controller, group.Select(entry => entry.index).ToArray()));
                        }

                        while (true)
                        {
                            long index = Interlocked.Increment(ref next);

                            if (index >= sliceCount)
                                break;

                            long start = Math.Max(begin, (cold ? firstBucket + index * bucketsPerSlice : missing[(int)index].First) * 256);
                            long stop = Math.Min(end, (cold ? firstBucket + (index + 1) * bucketsPerSlice : missing[(int)index].End) * 256);
                            var values = new float[items.Length][];

                            foreach (var (controller, indices) in owned)
                            {
                                var buffers = await ReadSliceAsync(controller, indices.Select(i => items[i]).ToArray(),
                                    new DateTime(start * period.Ticks, DateTimeKind.Utc), new DateTime(stop * period.Ticks, DateTimeKind.Utc),
                                    period, Precision.Float32, ReadDependencySerializedAsync, scanCancellation.Token);

                                for (int i = 0; i < indices.Length; i++)
                                    values[indices[i]] = MemoryMarshal.Cast<byte, float>(buffers[i]).ToArray();
                            }

                            await cache.Compute.WaitAsync(scanCancellation.Token);

                            try
                            {
                                for (long position = start; position < stop;)
                                {
                                    scanCancellation.Token.ThrowIfCancellationRequested();
                                    long bucket = position / 256;
                                    int count = (int)(Math.Min(stop, (bucket + 1) * 256) - position);
                                    var detail = rawBuckets.Contains(bucket) ? new float[items.Length][] : null;

                                    for (int resource = 0; resource < items.Length; resource++)
                                    {
                                        var span = values[resource].AsSpan((int)(position - start), count);

                                        if (cold)
                                            dataset!.Levels[0][resource][bucket - firstBucket] = VisualizationReduction.Summarize(span, position);

                                        if (detail is not null)
                                            detail[resource] = span.ToArray();
                                    }

                                    if (detail is not null)
                                        raw[bucket] = detail;

                                    position += count;
                                }
                            }
                            finally
                            {
                                cache.Compute.Release();
                            }

                            Interlocked.Add(ref completed, stop - start);
                        }
                    }
                    catch
                    {
                        await scanCancellation.CancelAsync();
                        throw;
                    }
                    finally
                    {
                        foreach (var (controller, _) in owned)
                        {
                            try { controller.Dispose(); }
                            catch (Exception ex) { logger.LogWarning(ex, "Disposing visualization controller failed"); }
                        }

                        if (admitted)
                            cache.Reads.Release();
                    }

                    async Task ReadDependencySerializedAsync(string path, DateTime from, DateTime to, Memory<double> buffer, CancellationToken cancellationToken)
                    {
                        await dependencies.WaitAsync(cancellationToken);

                        try { await ReadDependencyAsync(path, from, to, buffer, 0, cancellationToken); }
                        finally { dependencies.Release(); }
                    }
                }
            }

            async Task ReadDependencyAsync(string path, DateTime from, DateTime to, Memory<double> buffer, int depth, CancellationToken ct)
            {
                Interlocked.Exchange(ref hasDynamicDependencies, 1);

                // Recursive reads run inside the owning worker's permit, never acquire
                // another global read slot (which would deadlock at saturation).
                if (depth >= 8 || buffer.Length * 8L > cache.Options.TargetReadBytes)
                    throw new ValidationException("Visualization dependency exceeds the recursion or memory budget.");

                var item = await ResolveAsync(path, ct);
                var dependencyPeriod = item.Item.Representation.SamplePeriod;
                DataSourceController.ValidateParameters(from, to, dependencyPeriod);

                if ((to - from).Ticks / dependencyPeriod.Ticks != buffer.Length ||
                    buffer.Length > cache.Options.TargetReadBytes / EstimateBytesPerSample(item))
                    throw new ValidationException("Visualization dependency exceeds the memory budget.");

                using var controller = await controllers.GetVisualizationDataSourceControllerAsync(item.Container.Pipeline, configuration, Catalogs(item), ct);
                using var dependencies = new SemaphoreSlim(1);
                var values = await ReadSliceAsync(controller, [item], from, to, dependencyPeriod, Precision.Float64,
                    ReadNestedAsync, ct);
                MemoryMarshal.Cast<byte, double>(values[0]).CopyTo(buffer.Span);

                async Task ReadNestedAsync(string resource, DateTime start, DateTime stop, Memory<double> target, CancellationToken cancellationToken)
                {
                    await dependencies.WaitAsync(cancellationToken);

                    try { await ReadDependencyAsync(resource, start, stop, target, depth + 1, cancellationToken); }
                    finally { dependencies.Release(); }
                }
            }
        }
    }

    internal static void ValidateRequest(VisualizationRequest request, VisualizationOptions options)
    {
        DataService.ValidateResourcePaths(request.ResourcePaths);

        if (request.Begin >= request.End || request.Views is null || request.Views.Length is < 1 or > 3)
            throw new ValidationException("An increasing domain and one through three views are required.");

        if (request.Views.Any(view => view is null || string.IsNullOrWhiteSpace(view.Id) || view.MaxPoints is < 5 or > 32768 ||
            view.Begin < request.Begin || view.End > request.End || view.Begin >= view.End))
            throw new ValidationException("Views require unique IDs, contained increasing ranges, and 5 through 32768 points.");

        if (request.Views.Select(view => view.Id).Distinct(StringComparer.Ordinal).Count() != request.Views.Length ||
            request.Views.Sum(view => (long)view.MaxPoints) * request.ResourcePaths.Length > options.MaxAggregatePoints)
            throw new ValidationException("Duplicate view IDs or aggregate point budget exceeded.");
    }

    private static long EstimateBytesPerSample(CatalogItemRequest item)
    {
        var representation = item.Item.Representation;
        var source = (item.SourceBaseItem ?? item.BaseItem ?? item.SourceItem ?? item.Item).Representation;
        long ratio = Math.Max(1, Math.Max(representation.SamplePeriod.Ticks / source.SamplePeriod.Ticks,
            source.SamplePeriod.Ticks / representation.SamplePeriod.Ticks));
        // Include native/status, normalization, pipe, retained slice and resampling halos.
        return checked((ratio * (source.ElementSize + 1L) + 32) * 2);
    }

    private static ResourceCatalog[] Catalogs(CatalogItemRequest item) =>
        new[] { item.SourceItem?.Catalog, item.SourceBaseItem?.Catalog, item.Item.Catalog, item.BaseItem?.Catalog }
            .OfType<ResourceCatalog>().DistinctBy(catalog => catalog.Id).ToArray();

    private static async Task<byte[][]> ReadSliceAsync(IDataSourceController controller, CatalogItemRequest[] items,
        DateTime begin, DateTime end, TimeSpan period, Precision precision, ReadDataHandler readDependency, CancellationToken token)
    {
        int count = checked((int)((end - begin).Ticks / period.Ticks));
        // Slice size was admitted before allocation. Disabling pipe backpressure here
        // allows batched completion callbacks without a reader-per-channel task fan-out.
        var pipes = items.Select(_ => new Pipe(new PipeOptions(pauseWriterThreshold: 0, resumeWriterThreshold: 0))).ToArray();

        try
        {
            await controller.ReadAsync(begin, end, period, precision,
                items.Select((item, i) => new CatalogItemRequestPipeWriter(item, pipes[i].Writer)).ToArray(),
                readDependency, new SilentProgress(), token);
            var result = new byte[items.Length][];

            for (int i = 0; i < pipes.Length; i++)
            {
                await pipes[i].Writer.CompleteAsync();
                var read = await pipes[i].Reader.ReadAsync(token);

                if (read.Buffer.Length != (long)count * (int)precision)
                    throw new InvalidDataException("The visualization source returned an incomplete slice.");

                result[i] = new byte[count * (int)precision];
                System.Buffers.BuffersExtensions.CopyTo(read.Buffer, result[i].AsSpan());
                pipes[i].Reader.AdvanceTo(read.Buffer.End);
            }

            return result;
        }
        finally
        {
            foreach (var pipe in pipes)
            {
                await pipe.Writer.CompleteAsync();
                await pipe.Reader.CompleteAsync();
            }
        }
    }

    internal static VisualizationSummary Query(VisualizationDataset dataset, ConcurrentDictionary<long, float[][]> raw,
        int resource, long begin, long end)
    {
        if (begin < dataset.Begin || end > dataset.End || begin >= end || resource < 0 || resource >= dataset.Levels[0].Length)
            throw new ArgumentOutOfRangeException(nameof(begin), "Query must be contained in the dataset.");

        VisualizationSummary result = default;

        while (begin < end)
        {
            VisualizationSummary part = default;
            long stride = 256;

            for (int level = 0; level < dataset.Levels.Count; level++, stride *= 4)
            {
                long index = begin / stride - dataset.Begin / stride;

                if (index < 0 || index >= dataset.Levels[level][resource].Length)
                    break;

                var candidate = dataset.Levels[level][resource][index];

                if (candidate.Start != begin || candidate.Count == 0 || candidate.Start + candidate.Count > end)
                    break;

                part = candidate;
            }

            if (part.Count == 0)
            {
                long bucket = begin / 256;
                long start = Math.Max(dataset.Begin, bucket * 256);
                int count = (int)(Math.Min(end, (bucket + 1) * 256) - begin);
                part = VisualizationReduction.Summarize(raw[bucket][resource].AsSpan((int)(begin - start), count), begin);
            }

            result = VisualizationReduction.Merge(result, part);
            begin += part.Count;
        }

        return result;
    }

    private sealed class SilentProgress : IProgress<double>
    {
        public void Report(double value) { }
    }

    private sealed class ViewPlan
    {
        public long Begin { get; }
        public long End { get; }
        public long Stride { get; } = 1;

        public ViewPlan(long begin, long end, int maxPoints)
        {
            Begin = begin;
            End = end;

            while (((end - 1) / Stride - begin / Stride + 1) * (Stride == 1 ? 1 : 5) > maxPoints)
                Stride *= 4;
        }
    }

    internal static Schema Schema { get; } = new(new[]
    {
        new Field("kind", Int32Type.Default, false),
        new Field("resourceIndex", Int32Type.Default, false),
        new Field("viewIndex", Int32Type.Default, false),
        new Field("offset", Int64Type.Default, false),
        new Field("indices", new ListType(new Field("item", Int64Type.Default, false)), false),
        new Field("values", new ListType(new Field("item", FloatType.Default, false)), false),
        new Field("progress", DoubleType.Default, false),
        new Field("minimum", FloatType.Default, false),
        new Field("maximum", FloatType.Default, false),
        new Field("message", StringType.Default, false)
    }, new Dictionary<string, string> { ["visualizationVersion"] = "1" });

    internal static RecordBatch CreateBatch(int kind, int resource, int view, long offset, VisualizationPoint[] points,
        double progress, float minimum, float maximum, string message)
    {
        var offsets = new ArrowBuffer(MemoryMarshal.AsBytes(new[] { 0, points.Length }.AsSpan()).ToArray());
        var indices = new Int64Array.Builder().AppendRange(points.Select(point => point.Index)).Build();
        var values = new FloatArray.Builder().AppendRange(points.Select(point => point.Value)).Build();
        return new RecordBatch(Schema,
        [
            new Int32Array.Builder().Append(kind).Build(),
            new Int32Array.Builder().Append(resource).Build(),
            new Int32Array.Builder().Append(view).Build(),
            new Int64Array.Builder().Append(offset).Build(),
            new ListArray(Schema.GetFieldByName("indices").DataType, 1, offsets, indices, ArrowBuffer.Empty),
            new ListArray(Schema.GetFieldByName("values").DataType, 1, offsets, values, ArrowBuffer.Empty),
            new DoubleArray.Builder().Append(progress).Build(),
            new FloatArray.Builder().Append(minimum).Build(),
            new FloatArray.Builder().Append(maximum).Build(),
            new StringArray.Builder().Append(message).Build()
        ], 1);
    }
}
