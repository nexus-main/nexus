// MIT License
// Copyright (c) [2024] [nexus-main]

using SkiaSharp;

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
    public LineSeries(string name, string unit, TimeSpan samplePeriod, float[] data)
        : this(name, unit, samplePeriod, new LineSeriesSource(data))
    {
    }

    private LineSeries(string name, string unit, TimeSpan samplePeriod, LineSeriesSource source)
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
    internal int SyntheticLength { get; init; }
}

internal sealed class LineSeriesSource
{
    public LineSeriesSource(float[] values)
    {
        Values = values;
        Length = values.Length;
    }

    public int Length { get; }
    private float[] Values { get; }

    internal ReadOnlyMemory<float> Read(int offset, int count) => Values.AsMemory(offset, count);

    internal bool TryGetValue(int index, out float value)
    {
        if ((uint)index < (uint)Values.Length)
        {
            value = Values[index];
            return true;
        }

        value = 0;
        return false;
    }
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
