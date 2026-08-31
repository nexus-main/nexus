// MIT License
// Copyright (c) [2024] [nexus-main]

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

public record LineSeries(
    string Name,
    string Unit,
    TimeSpan SamplePeriod,
    double[] Data)
{
    public bool Show { get; set; } = true;
    internal string Id { get; } = Guid.NewGuid().ToString();
    internal string Color { get; set; } = "#000000";
}
