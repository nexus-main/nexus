// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Runtime.InteropServices;

namespace Nexus.UI.Charts;

public partial class Chart : IAsyncDisposable
{
    private static readonly DateTime UnixEpoch = DateTime.UnixEpoch;

    private readonly string _chartId = Guid.NewGuid().ToString();
    private bool _isRendered;
    private LineSeriesData? _previousLineSeriesData;

    private readonly string[] _colors =
    [
        "#0072bd",
        "#d95319",
        "#edb120",
        "#7e2f8e",
        "#77ac30",
        "#4dbeee",
        "#a2142f"
    ];

    [Inject]
    public IJSRuntime JSRuntime { get; set; } = default!;

    [Parameter]
    public LineSeriesData LineSeriesData { get; set; } = default!;

    [Parameter]
    public bool BeginAtZero { get; set; }

    protected override void OnParametersSet()
    {
        for (var i = 0; i < LineSeriesData.Series.Count; i++)
            LineSeriesData.Series[i].Color = _colors[i % _colors.Length];
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            _isRendered = true;

        if (!_isRendered)
            return;

        if (!ReferenceEquals(_previousLineSeriesData, LineSeriesData) || firstRender)
        {
            _previousLineSeriesData = LineSeriesData;
            await JSRuntime.InvokeVoidAsync("nexus.chartGpu.createOrUpdateLineChart", _chartId, CreatePayload());
        }
    }

    private object CreatePayload()
    {
        var beginUtc = LineSeriesData.Begin.ToUniversalTime();
        var endUtc = LineSeriesData.End.ToUniversalTime();
        var originUnixNanoseconds = ToUnixNanoseconds(beginUtc).ToString();
        var durationNanoseconds = Math.Max(0, ToUnixNanoseconds(endUtc) - ToUnixNanoseconds(beginUtc));

        var series = LineSeriesData.Series
            .Where(lineSeries => lineSeries.Show)
            .Select((lineSeries, index) => new
            {
                lineSeries.Id,
                lineSeries.Name,
                lineSeries.Unit,
                lineSeries.Color,
                SamplePeriodNanoseconds = lineSeries.SamplePeriod.Ticks * 100L,
                ValuesBytes = ToBytes(lineSeries.Data)
            })
            .ToArray();

        return new
        {
            OriginUnixNanoseconds = originUnixNanoseconds,
            DurationNanoseconds = durationNanoseconds,
            BeginAtZero,
            Series = series
        };
    }

    private static long ToUnixNanoseconds(DateTime dateTime)
    {
        var utc = dateTime.Kind == DateTimeKind.Utc
            ? dateTime
            : dateTime.ToUniversalTime();

        return checked((utc.Ticks - UnixEpoch.Ticks) * 100L);
    }

    private static byte[] ToBytes(double[] values)
    {
        return MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isRendered)
            await JSRuntime.InvokeVoidAsync("nexus.chartGpu.dispose", _chartId);
    }
}
