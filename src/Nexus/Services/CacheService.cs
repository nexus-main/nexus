using Nexus.Core;
using Nexus.DataModel;
using Nexus.Utilities;
using System.Globalization;

namespace Nexus.Services;

internal interface ICacheService
{
    Task<List<Interval>> ReadAsync(
        CatalogItem catalogItem,
        DateTime begin,
        Memory<double> targetBuffer,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        CatalogItem catalogItem,
        DateTime begin,
        Memory<double> sourceBuffer,
        List<Interval> uncachedIntervals,
        CancellationToken cancellationToken);

    Task ClearAsync(
        string catalogId,
        DateTime begin,
        DateTime end,
        IProgress<double> progress,
        CancellationToken cancellationToken);
}

internal class CacheService : ICacheService
{
    private readonly IDatabaseService _databaseService;
    private readonly TimeSpan _largestSamplePeriod = TimeSpan.FromDays(1);

    public CacheService(
        IDatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<List<Interval>> ReadAsync(
        CatalogItem catalogItem,
        DateTime begin,
        Memory<double> targetBuffer,
        CancellationToken cancellationToken)
    {
        var samplePeriod = catalogItem.Representation.SamplePeriod;
        var end = begin + samplePeriod * targetBuffer.Length;
        var filePeriod = GetFilePeriod(samplePeriod);
        var uncachedIntervals = new List<Interval>();

        /* try read data from cache */
        await NexusUtilities.FileLoopAsync(begin, end, filePeriod, async (fileBegin, fileOffset, duration) =>
        {
            var actualBegin = fileBegin + fileOffset;
            var actualEnd = actualBegin + duration;

            if (_databaseService.TryReadCacheEntry(catalogItem, fileBegin, out var cacheEntry))
            {
                var slicedTargetBuffer = targetBuffer.Slice(
                   start: NexusUtilities.Scale(actualBegin - begin, samplePeriod),
                   length: NexusUtilities.Scale(duration, samplePeriod));

                try
                {
                    using var cacheEntryWrapper = new CacheEntryWrapper(
                        fileBegin, filePeriod, samplePeriod, cacheEntry);

                    var moreUncachedIntervals = await cacheEntryWrapper.ReadAsync(
                        actualBegin,
                        actualEnd,
                        slicedTargetBuffer,
                        cancellationToken);

                    uncachedIntervals.AddRange(moreUncachedIntervals);
                }
                catch
                {
                    uncachedIntervals.Add(new Interval(actualBegin, actualEnd));
                }
            }

            else
            {
                uncachedIntervals.Add(new Interval(actualBegin, actualEnd));
            }
        });

        var consolidatedIntervals = new List<Interval>();

        /* consolidate intervals */
        if (uncachedIntervals.Count >= 1)
        {
            consolidatedIntervals.Add(uncachedIntervals[0]);

            for (int i = 1; i < uncachedIntervals.Count; i++)
            {
                if (consolidatedIntervals[^1].End == uncachedIntervals[i].Begin)
                    consolidatedIntervals[^1] = consolidatedIntervals[^1] with { End = uncachedIntervals[i].End };

                else
                    consolidatedIntervals.Add(uncachedIntervals[i]);
            }
        }

        return consolidatedIntervals;
    }

    public async Task UpdateAsync(
        CatalogItem catalogItem,
        DateTime begin,
        Memory<double> sourceBuffer,
        List<Interval> uncachedIntervals,
        CancellationToken cancellationToken)
    {
        var samplePeriod = catalogItem.Representation.SamplePeriod;
        var filePeriod = GetFilePeriod(samplePeriod);

        /* try write data to cache */
        foreach (var interval in uncachedIntervals)
        {
            await NexusUtilities.FileLoopAsync(interval.Begin, interval.End, filePeriod, async (fileBegin, fileOffset, duration) =>
            {
                var actualBegin = fileBegin + fileOffset;

                if (_databaseService.TryWriteCacheEntry(catalogItem, fileBegin, out var cacheEntry))
                {
                    var slicedSourceBuffer = sourceBuffer.Slice(
                        start: NexusUtilities.Scale(actualBegin - begin, samplePeriod),
                        length: NexusUtilities.Scale(duration, samplePeriod));

                    try
                    {
                        using var cacheEntryWrapper = new CacheEntryWrapper(
                            fileBegin, filePeriod, samplePeriod, cacheEntry);

                        await cacheEntryWrapper.WriteAsync(
                            actualBegin,
                            slicedSourceBuffer,
                            cancellationToken);
                    }
                    catch
                    {
                        //
                    }
                }
            });
        }
    }

    public async Task ClearAsync(
        string catalogId,
        DateTime begin,
        DateTime end,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        var currentProgress = 0.0;
        var totalPeriod = end - begin;
        var folderPeriod = TimeSpan.FromDays(1);
        var timeout = TimeSpan.FromMinutes(1);

        await NexusUtilities.FileLoopAsync(begin, end, folderPeriod, async (folderBegin, folderOffset, duration) =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dateOnly = DateOnly.FromDateTime(folderBegin.Date);

            /* partial day */
            if (duration != folderPeriod)
                await _databaseService.ClearCacheEntriesAsync(catalogId, dateOnly, timeout, cacheEntryId =>
                {
                    var dateTimeString = Path
                        .GetFileName(cacheEntryId)[..27];

                    var cacheEntryDateTime = DateTime
                        .ParseExact(dateTimeString, "yyyy-MM-ddTHH-mm-ss-fffffff", CultureInfo.InvariantCulture);

                    return begin <= cacheEntryDateTime && cacheEntryDateTime < end;
                });

            /* full day */
            else
                await _databaseService.ClearCacheEntriesAsync(catalogId, dateOnly, timeout, cacheEntryId => true);

            var currentEnd = folderBegin + folderOffset + duration;
            currentProgress = (currentEnd - begin).Ticks / (double)totalPeriod.Ticks;
            progress.Report(currentProgress);
        });
    }

    private TimeSpan GetFilePeriod(TimeSpan samplePeriod)
    {
        if (samplePeriod > _largestSamplePeriod || TimeSpan.FromDays(1).Ticks % samplePeriod.Ticks != 0)
            throw new Exception("Caching is only supported for sample periods fit exactly into a single day.");

        return samplePeriod switch
        {
            _ when samplePeriod <= TimeSpan.FromSeconds(1e-9) => TimeSpan.FromSeconds(1e-3),
            _ when samplePeriod <= TimeSpan.FromSeconds(1e-6) => TimeSpan.FromSeconds(1e+0),
            _ when samplePeriod <= TimeSpan.FromSeconds(1e-3) => TimeSpan.FromHours(1),
            _ => TimeSpan.FromDays(1),
        };
    }
}
