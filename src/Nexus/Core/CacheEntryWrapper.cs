// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Utilities;

namespace Nexus.Core;

internal class CacheEntryWrapper : IDisposable
{
    private readonly DateTime _fileBegin;
    private readonly TimeSpan _filePeriod;
    private readonly TimeSpan _samplePeriod;
    private readonly Stream _stream;

    private readonly long _dataSectionLength;

    private Interval[] _cachedIntervals;

    public CacheEntryWrapper(DateTime fileBegin, TimeSpan filePeriod, TimeSpan samplePeriod, Stream stream)
    {
        _fileBegin = fileBegin;
        _filePeriod = filePeriod;
        _samplePeriod = samplePeriod;
        _stream = stream;

        var elementCount = filePeriod.Ticks / samplePeriod.Ticks;
        _dataSectionLength = elementCount * sizeof(double);

        // ensure a minimum length of data section + 1 x PeriodOfTime entry
        if (_stream.Length == 0)
            _stream.SetLength(_dataSectionLength + 1 + 2 * sizeof(long));

        // read cached periods
        _stream.Seek(_dataSectionLength, SeekOrigin.Begin);
        _cachedIntervals = ReadCachedIntervals(_stream);
    }

    public async Task<Interval[]> ReadAsync(
        DateTime begin,
        DateTime end,
        Memory<double> targetBuffer,
        CancellationToken cancellationToken)
    {
        /*
         * _____
         * |   |
         * |___|__ end      _________________
         * |   |            uncached period 3
         * |   |
         * |   |            _________________
         * |xxx|              cached period 2
         * |xxx|
         * |xxx|            _________________
         * |   |            uncached period 2
         * |   |            _________________
         * |xxx|              cached period 1
         * |xxx|            _________________
         * |   |            uncached period 1
         * |___|__ begin    _________________
         * |   |
         * |___|__ file begin
         *
         */

        var index = 0;
        var currentBegin = begin;
        var uncachedIntervals = new List<Interval>();

        var isCachePeriod = false;
        var isFirstIteration = true;

        while (currentBegin < end)
        {
            var cachedInterval = index < _cachedIntervals.Length
                ? _cachedIntervals[index]
                : new Interval(DateTime.MaxValue, DateTime.MaxValue);

            DateTime currentEnd;

            /* cached */
            if (cachedInterval.Begin <= currentBegin && currentBegin < cachedInterval.End)
            {
                currentEnd = new DateTime(Math.Min(cachedInterval.End.Ticks, end.Ticks), DateTimeKind.Utc);

                var cacheOffset = NexusUtilities.Scale(currentBegin - _fileBegin, _samplePeriod);
                var targetBufferOffset = NexusUtilities.Scale(currentBegin - begin, _samplePeriod);
                var length = NexusUtilities.Scale(currentEnd - currentBegin, _samplePeriod);

                var slicedTargetBuffer = targetBuffer.Slice(targetBufferOffset, length);
                var slicedByteTargetBuffer = new CastMemoryManager<double, byte>(slicedTargetBuffer).Memory;

                _stream.Seek(cacheOffset * sizeof(double), SeekOrigin.Begin);
                await _stream.ReadExactlyAsync(slicedByteTargetBuffer, cancellationToken);

                if (currentEnd >= cachedInterval.End)
                    index++;

                if (!(isFirstIteration || isCachePeriod))
                    uncachedIntervals[^1] = uncachedIntervals[^1] with { End = currentBegin };

                isCachePeriod = true;
            }

            /* uncached */
            else
            {
                currentEnd = new DateTime(Math.Min(cachedInterval.Begin.Ticks, end.Ticks), DateTimeKind.Utc);

                if (isFirstIteration || isCachePeriod)
                    uncachedIntervals.Add(new Interval(currentBegin, end));

                isCachePeriod = false;
            }

            isFirstIteration = false;
            currentBegin = currentEnd;
        }

        return uncachedIntervals
            .Where(period => (period.End - period.Begin) > TimeSpan.Zero)
            .ToArray();
    }

    // https://www.geeksforgeeks.org/merging-intervals/
    class SortHelper : IComparer<Interval>
    {
        public int Compare(Interval x, Interval y)
        {
            long result;

            if (x.Begin == y.Begin)
                result = x.End.Ticks - y.End.Ticks;

            else
                result = x.Begin.Ticks - y.Begin.Ticks;

            return result switch
            {
                < 0 => -1,
                > 0 => +1,
                _ => 0
            };
        }
    }

    public async Task WriteAsync(
        DateTime begin,
        Memory<double> sourceBuffer,
        CancellationToken cancellationToken)
    {
        var end = begin + _samplePeriod * sourceBuffer.Length;
        var cacheOffset = NexusUtilities.Scale(begin - _fileBegin, _samplePeriod);
        var byteSourceBuffer = new CastMemoryManager<double, byte>(sourceBuffer).Memory;

        _stream.Seek(cacheOffset * sizeof(double), SeekOrigin.Begin);
        await _stream.WriteAsync(byteSourceBuffer, cancellationToken);

        /* update the list of cached intervals */
        var cachedIntervals = _cachedIntervals
            .Concat(new[] { new Interval(begin, end) })
            .ToArray();

        /* merge list of intervals */
        if (cachedIntervals.Length > 1)
        {
            /* sort list of intervals */
            Array.Sort(cachedIntervals, new SortHelper());

            /* stores index of last element */
            var index = 0;

            for (int i = 1; i < cachedIntervals.Length; i++)
            {
                /* if this is not first interval and overlaps with the previous one */
                if (cachedIntervals[index].End >= cachedIntervals[i].Begin)
                {
                    /* merge previous and current intervals */
                    cachedIntervals[index] = cachedIntervals[index] with
                    {
                        End = new DateTime(
                            Math.Max(
                                cachedIntervals[index].End.Ticks,
                                cachedIntervals[i].End.Ticks),
                            DateTimeKind.Utc)
                    };
                }

                /* just add interval */
                else
                {
                    index++;
                    cachedIntervals[index] = cachedIntervals[i];
                }
            }

            _cachedIntervals = cachedIntervals
                .Take(index + 1)
                .ToArray();
        }

        else
        {
            _cachedIntervals = cachedIntervals;
        }

        _stream.Seek(_dataSectionLength, SeekOrigin.Begin);
        WriteCachedIntervals(_stream, _cachedIntervals);
    }

    public void Dispose()
    {
        _stream.Dispose();
    }

    public static Interval[] ReadCachedIntervals(Stream stream)
    {
        var cachedPeriodCount = stream.ReadByte();
        var cachedIntervals = new Interval[cachedPeriodCount];

        Span<byte> buffer = stackalloc byte[8];

        for (int i = 0; i < cachedPeriodCount; i++)
        {
            stream.ReadExactly(buffer);
            var beginTicks = BitConverter.ToInt64(buffer);

            stream.ReadExactly(buffer);
            var endTicks = BitConverter.ToInt64(buffer);

            cachedIntervals[i] = new Interval(
                Begin: new DateTime(beginTicks, DateTimeKind.Utc),
                End: new DateTime(endTicks, DateTimeKind.Utc));
        }

        return cachedIntervals;
    }

    public static void WriteCachedIntervals(Stream stream, Interval[] cachedIntervals)
    {
        if (cachedIntervals.Length > byte.MaxValue)
            throw new Exception("Only 256 cache periods per file are supported.");

        stream.WriteByte((byte)cachedIntervals.Length);

        Span<byte> buffer = stackalloc byte[8];

        foreach (var cachedPeriod in cachedIntervals)
        {
            BitConverter.TryWriteBytes(buffer, cachedPeriod.Begin.Ticks);
            stream.Write(buffer);

            BitConverter.TryWriteBytes(buffer, cachedPeriod.End.Ticks);
            stream.Write(buffer);
        }
    }
}
