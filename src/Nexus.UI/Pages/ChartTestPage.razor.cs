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
        var end = begin.AddMilliseconds((pointCount - 1) * 500);

        var random = new Random();
        var windSpeed = new double[pointCount];
        var temperature = new double[pointCount];
        var pressure = new double[pointCount];

        for (var i = 0; i < pointCount; i++)
        {
            windSpeed[i] = i / 4.0;
            temperature[i] = random.NextDouble() * 10 - 5;
            pressure[i] = random.NextDouble() * 100 + 1000;
        }

        var lineSeries = new LineSeries[]
        {
            new(
                "Wind speed",
                "m/s",
                TimeSpan.FromMilliseconds(500),
                windSpeed),

            new(
                "Temperature",
                "°C",
                TimeSpan.FromSeconds(1),
                temperature),

            new(
                "Pressure",
                "mbar",
                TimeSpan.FromSeconds(1),
                pressure)
        };

        lineSeries[0].Data[0] = double.NaN;

        lineSeries[0].Data[5] = double.NaN;
        lineSeries[0].Data[6] = double.NaN;

        lineSeries[0].Data[10] = double.NaN;
        lineSeries[0].Data[11] = double.NaN;
        lineSeries[0].Data[12] = double.NaN;

        lineSeries[0].Data[15] = double.NaN;
        lineSeries[0].Data[16] = double.NaN;
        lineSeries[0].Data[17] = double.NaN;
        lineSeries[0].Data[18] = double.NaN;

        _lineSeriesData = new LineSeriesData(begin, end, lineSeries);
    }
}
