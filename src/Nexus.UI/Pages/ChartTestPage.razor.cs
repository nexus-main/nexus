// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.UI.Charts;
namespace Nexus.UI.Pages;

public partial class ChartTestPage
{
    private LineSeriesData _lineSeriesData = default!;

    private int _selectedCount = 100_000;

    private int SelectedCount
    {
        get => _selectedCount;
        set
        {
            if (_selectedCount == value)
                return;

            _selectedCount = value;
            RegenerateData();
        }
    }

    protected override void OnInitialized()
    {
        RegenerateData();
    }

    private void RegenerateData()
    {
        var pointCount = _selectedCount;
        var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var end = begin.AddMilliseconds((pointCount - 1L) * 500);

        var lineSeries = new LineSeries[]
        {
            new(
                "Wind speed",
                "m/s",
                TimeSpan.FromMilliseconds(500),
                []) { SyntheticKind = SyntheticSeriesKind.WindSpeed, SyntheticLength = pointCount },

            new(
                "Temperature",
                "°C",
                TimeSpan.FromSeconds(1),
                []) { SyntheticKind = SyntheticSeriesKind.Temperature, SyntheticLength = pointCount },

            new(
                "Pressure",
                "mbar",
                TimeSpan.FromSeconds(1),
                []) { SyntheticKind = SyntheticSeriesKind.Pressure, SyntheticLength = pointCount }
        };

        _lineSeriesData = new LineSeriesData(begin, end, lineSeries);
    }
}
