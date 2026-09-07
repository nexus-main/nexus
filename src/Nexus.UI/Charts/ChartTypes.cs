// MIT License
// Copyright (c) [2024] [nexus-main]

using SkiaSharp;
using System.Runtime.InteropServices;

namespace Nexus.UI.Charts;

public record AvailabilityData(
    DateTime Begin,
    DateTime End,
    TimeSpan Step,
    IReadOnlyList<double> Data
);

public record LineSeriesData(
    DateTime Begin,
    DateTime End,
    IList<LineSeries> Series
);

public sealed class LineSeries
{
    public LineSeries(string name, string unit, TimeSpan samplePeriod, ReadOnlyMemory<float> data)
        : this(name, unit, samplePeriod, new LineSeriesSource(data))
    {
    }

    internal LineSeries(string name, string unit, TimeSpan samplePeriod, LineSeriesSource source)
    {
        Name = name;
        Unit = unit;
        SamplePeriod = samplePeriod;
        Source = source;
    }

    public string Name { get; }
    public string Unit { get; }
    public TimeSpan SamplePeriod { get; }
    internal LineSeriesSource Source { get; }
    public bool Show { get; set; } = true;
    internal string Id { get; } = Guid.NewGuid().ToString();
    internal SKColor Color { get; set; }
    internal SyntheticSeriesKind? SyntheticKind { get; init; }
    internal long SyntheticLength { get; init; }
}

internal sealed class LineSeriesSource
{
    private readonly object _gate = new();
    private readonly List<ReadOnlyMemory<float>> _chunks = [];
    private TaskCompletionSource _changed = CreateCompletionSource();
    private bool _completed;
    private long _availableLength;
    private int _version;

    public LineSeriesSource(ReadOnlyMemory<float> values)
    {
        _chunks.Add(values);
        _availableLength = values.Length;
        Length = values.Length;
        _completed = true;
    }

    public LineSeriesSource(long length)
    {
        Length = length;
    }

    public long Length { get; }
    internal int Version => _version;

    internal void AddChunk(ReadOnlyMemory<float> values)
    {
        TaskCompletionSource changed;

        lock (_gate)
        {
            if (_completed)
                throw new InvalidOperationException("The series source is already complete.");

            if (_availableLength + values.Length > Length)
                throw new InvalidOperationException("The series source contains more data than expected.");

            _chunks.Add(values);
            _availableLength += values.Length;
            _version++;
            changed = _changed;
            _changed = CreateCompletionSource();
        }

        changed.SetResult();
    }

    internal void Complete()
    {
        TaskCompletionSource changed;

        lock (_gate)
        {
            if (_availableLength != Length)
                throw new InvalidOperationException("The series source is incomplete.");

            _completed = true;
            _version++;
            changed = _changed;
            _changed = CreateCompletionSource();
        }

        changed.SetResult();
    }

    internal async Task<bool> WaitForRangeAsync(long offset, int count)
    {
        while (true)
        {
            Task changed;

            lock (_gate)
            {
                if (offset < 0 || count < 0 || offset > Length - count)
                    return false;

                if (offset + count <= _availableLength)
                    return true;

                if (_completed)
                    return false;

                changed = _changed.Task;
            }

            await changed.ConfigureAwait(false);
        }
    }

    internal void CopyTo(long offset, Span<float> destination)
    {
        lock (_gate)
        {
            if (offset < 0 || offset > _availableLength - destination.Length)
                throw new InvalidOperationException("The requested series data is not available.");

            var chunkOffset = 0L;
            var destinationOffset = 0;

            foreach (var chunk in _chunks)
            {
                if (offset >= chunkOffset + chunk.Length)
                {
                    chunkOffset += chunk.Length;
                    continue;
                }

                var sourceOffset = checked((int)(offset - chunkOffset));
                var count = Math.Min(chunk.Length - sourceOffset, destination.Length - destinationOffset);
                chunk.Span.Slice(sourceOffset, count).CopyTo(destination[destinationOffset..]);
                destinationOffset += count;

                if (destinationOffset == destination.Length)
                    return;

                offset += count;
                chunkOffset += chunk.Length;
            }
        }

        throw new InvalidOperationException("The requested series data is not available.");
    }

    internal void CopyBytesTo(long offset, Span<byte> destination)
    {
        if (destination.Length % sizeof(float) != 0)
            throw new ArgumentException("The destination length must be a multiple of the float size.", nameof(destination));

        CopyTo(offset, MemoryMarshal.Cast<byte, float>(destination));
    }

    internal bool TryGetContiguousArraySegment(long offset, int count, out ArraySegment<float> segment)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));

        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        lock (_gate)
        {
            if (offset + count > _availableLength)
                throw new ArgumentOutOfRangeException(nameof(count));

            var relativeOffset = offset;

            foreach (var chunk in _chunks)
            {
                if (relativeOffset >= chunk.Length)
                {
                    relativeOffset -= chunk.Length;
                    continue;
                }

                if (relativeOffset + count <= chunk.Length && MemoryMarshal.TryGetArray(chunk, out var chunkSegment))
                {
                    segment = new ArraySegment<float>(
                        chunkSegment.Array!,
                        chunkSegment.Offset + (int)relativeOffset,
                        count);

                    return true;
                }

                break;
            }
        }

        segment = default;
        return false;
    }

    internal bool TryGetValue(long index, out float value)
    {
        lock (_gate)
        {
            if ((ulong)index >= (ulong)_availableLength)
            {
                value = 0;
                return false;
            }

            var offset = 0L;

            foreach (var chunk in _chunks)
            {
                if (index < offset + chunk.Length)
                {
                    value = chunk.Span[checked((int)(index - offset))];
                    return true;
                }

                offset += chunk.Length;
            }
        }

        value = 0;
        return false;
    }

    private static TaskCompletionSource CreateCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal enum SyntheticSeriesKind
{
    WindSpeed,
    Temperature,
    Pressure
}

internal record struct Position(
    float X,
    float Y);

internal record AxisInfo(
    string Unit,
    float OriginalMin,
    float OriginalMax)
{
    public float Min { get; set; }
    public float Max { get; set; }
};

internal record TimeAxisConfig(

    /* The tick interval */
    TimeSpan TickInterval,

    /* The standard tick label format */
    string FastTickLabelFormat,

    /* Ticks where the TriggerPeriod changes will have a slow tick label attached */
    TriggerPeriod SlowTickTrigger,

    /* The slow tick format (row 1) */
    string? SlowTickLabelFormat1,

    /* The slow tick format (row 2) */
    string? SlowTickLabelFormat2,

    /* The cursor label format*/
    string CursorLabelFormat);

internal enum TriggerPeriod
{
    Second,
    Minute,
    Hour,
    Day,
    Month,
    Year
}
