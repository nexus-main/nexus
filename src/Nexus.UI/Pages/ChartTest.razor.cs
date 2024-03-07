using Nexus.UI.Charts;

namespace Nexus.UI.Pages;

public partial class ChartTest
{
    private readonly LineSeriesData _lineSeriesData;

    public ChartTest()
    {
        var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2020, 01, 01, 0, 1, 0, DateTimeKind.Utc);

        var random = new Random();

        var lineSeries = new LineSeries[]
        {
            new LineSeries(
                "Wind speed",
                "m/s",
                TimeSpan.FromMilliseconds(500),
                Enumerable.Range(0, 60*2).Select(value => value / 4.0).ToArray()),

            new LineSeries(
                "Temperature",
                "°C",
                TimeSpan.FromSeconds(1),
                Enumerable.Range(0, 60).Select(value => random.NextDouble() * 10 - 5).ToArray()),

            new LineSeries(
                "Pressure",
                "mbar",
                TimeSpan.FromSeconds(1),
                Enumerable.Range(0, 60).Select(value => random.NextDouble() * 100 + 1000).ToArray())
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
