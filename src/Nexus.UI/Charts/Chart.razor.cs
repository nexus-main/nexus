// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Nexus.UI.Core;
using Nexus.UI.Services;
using SkiaSharp;
using SkiaSharp.Views.Blazor;
using System.Globalization;

namespace Nexus.UI.Charts;

public partial class Chart : IDisposable
{
    private SKGLView _skiaView = default!;
    private readonly string _chartId = Guid.NewGuid().ToString();
    private Dictionary<AxisInfo, LineSeries[]> _axesMap = default!;

    /* tracks which (DataVersion, Length) was last transmitted to the JS/WebGPU side
     * per series id, so unchanged data is not re-serialized and re-marshaled on
     * every redraw (zoom, pan, resize, series toggle). */
    private readonly Dictionary<string, (int Version, int Length)> _sentSeriesVersions = new();
    private readonly Dictionary<string, (int Version, int Length)> _sendingSeriesVersions = new();
    private readonly Dictionary<string, SeriesRange> _seriesRanges = new();
    private LineSeriesData? _axisData;
    private bool _axisBeginAtZero;
    private bool _disposed;
    private int _gpuCacheBudgetMiB;
    private int _webGpuGeneration;
    private string? _webGpuErrorTitle;
    private string? _webGpuErrorMessage;
    private bool _webGpuRetrying;

    /* zoom */
    private readonly DotNetObjectReference<Chart> _dotNetHelper;

    private SKRect _oldZoomBox;
    private SKRect _zoomBox;
    private double _oldZoomLeft;
    private double _oldZoomRight = 1;
    private double _zoomLeft;
    private double _zoomRight = 1;
    private DateTime _zoomedBegin;
    private DateTime _zoomedEnd;

    private readonly SKRect _defaultZoomBox = new SKRect(0, 0, 1, 1);

    /* navigator */
    private string NavigatorWindowLeftStyle =>
        $"{(_zoomLeft * 100).ToString("0.##", CultureInfo.InvariantCulture)}%";

    private string NavigatorWindowWidthStyle =>
        $"{((_zoomRight - _zoomLeft) * 100).ToString("0.##", CultureInfo.InvariantCulture)}%";

    private string NavigatorWindowRightStyle =>
        $"{(_zoomRight * 100).ToString("0.##", CultureInfo.InvariantCulture)}%";

    private string NavigatorDataLeft =>
        _zoomLeft.ToString("R", CultureInfo.InvariantCulture);

    private string NavigatorDataRight =>
        _zoomRight.ToString("R", CultureInfo.InvariantCulture);

    private double DetailLeft
    {
        get
        {
            var width = _zoomRight - _zoomLeft;
            var detailWidth = Math.Min(1, width * 8);
            return Math.Clamp((_zoomLeft + _zoomRight - detailWidth) / 2, 0, 1 - detailWidth);
        }
    }

    private double DetailRight => Math.Min(1, DetailLeft + (_zoomRight - _zoomLeft) * 8);
    private bool ShowDetailNavigator => _zoomRight - _zoomLeft < 0.125;
    private double DetailWindowLeft => (_zoomLeft - DetailLeft) / (DetailRight - DetailLeft);
    private double DetailWindowRight => (_zoomRight - DetailLeft) / (DetailRight - DetailLeft);
    private string DetailDataLeft => DetailLeft.ToString("R", CultureInfo.InvariantCulture);
    private string DetailDataRight => DetailRight.ToString("R", CultureInfo.InvariantCulture);
    private string DetailWindowLeftStyle => $"{(DetailWindowLeft * 100).ToString("0.##", CultureInfo.InvariantCulture)}%";
    private string DetailWindowRightStyle => $"{(DetailWindowRight * 100).ToString("0.##", CultureInfo.InvariantCulture)}%";
    private string DetailWindowWidthStyle => $"{((DetailWindowRight - DetailWindowLeft) * 100).ToString("0.##", CultureInfo.InvariantCulture)}%";
    private string NavigatorDurationLabel => FormatDuration(_zoomedEnd - _zoomedBegin);
    private string NavigatorRangeLabel => FormatRange(_zoomedBegin, _zoomedEnd);
    private string DetailRangeLabel => FormatRange(ToTime(DetailLeft), ToTime(DetailRight));

    /* Common */
    private const float TICK_SIZE = 10;

    /* Y-Axis */
    private const float Y_PADDING_LEFT = 10;
    private const float Y_PADDING_TOP = 20;
    private const float Y_PADDING_Bottom = 25 + TIME_FAST_LABEL_OFFSET * 2;
    private const float Y_UNIT_OFFSET = 30;
    private const float TICK_MARGIN_LEFT = 5;

    private const float AXIS_MARGIN_RIGHT = 5;
    private const float HALF_LINE_HEIGHT = 3.5f;

    private readonly int[] _factors = [2, 5, 10, 20, 50];

    /* Time-Axis */
    private const float TIME_AXIS_MARGIN_TOP = 15;
    private const float TIME_FAST_LABEL_OFFSET = 15;
    private TimeAxisConfig _timeAxisConfig;
    private readonly TimeAxisConfig[] _timeAxisConfigs;

    /* Others */
    private readonly SKColor[] _colors;

    public Chart()
    {
        _dotNetHelper = DotNetObjectReference.Create(this);

        _timeAxisConfigs =
        [
            /* nanoseconds */
            new TimeAxisConfig(TimeSpan.FromSeconds(100e-9), ".fffffff", TriggerPeriod.Second, "HH:mm.ss", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss.fffffff"),

            /* microseconds */
            new TimeAxisConfig(TimeSpan.FromSeconds(1e-6), ".ffffff", TriggerPeriod.Second, "HH:mm.ss", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss.fffffff"),
            new TimeAxisConfig(TimeSpan.FromSeconds(5e-6), ".ffffff", TriggerPeriod.Second, "HH:mm.ss", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss.fffffff"),
            new TimeAxisConfig(TimeSpan.FromSeconds(10e-6), ".ffffff", TriggerPeriod.Second, "HH:mm.ss", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss.fffffff"),
            new TimeAxisConfig(TimeSpan.FromSeconds(50e-6), ".ffffff", TriggerPeriod.Second, "HH:mm.ss", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss.fffffff"),
            new TimeAxisConfig(TimeSpan.FromSeconds(100e-6), ".ffffff", TriggerPeriod.Second, "HH:mm.ss", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss.fffffff"),
            new TimeAxisConfig(TimeSpan.FromSeconds(500e-6), ".ffffff", TriggerPeriod.Second, "HH:mm.ss", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss.fffffff"),

            /* milliseconds */
            new TimeAxisConfig(TimeSpan.FromSeconds(1e-3), ".fff", TriggerPeriod.Minute, "HH:mm:ss", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss.ffffff"),
            new TimeAxisConfig(TimeSpan.FromSeconds(5e-3), ".fff", TriggerPeriod.Minute, "HH:mm:ss", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss.ffffff"),
            new TimeAxisConfig(TimeSpan.FromSeconds(10e-3), ".fff", TriggerPeriod.Minute, "HH:mm:ss", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss.fffff"),
            new TimeAxisConfig(TimeSpan.FromSeconds(50e-3), ".fff", TriggerPeriod.Minute, "HH:mm:ss", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss.fffff"),
            new TimeAxisConfig(TimeSpan.FromSeconds(100e-3), ".fff", TriggerPeriod.Minute, "HH:mm:ss", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss.ffff"),
            new TimeAxisConfig(TimeSpan.FromSeconds(500e-3), ".fff", TriggerPeriod.Minute, "HH:mm:ss", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss.ffff"),

            /* seconds */
            new TimeAxisConfig(TimeSpan.FromSeconds(1), "HH:mm:ss", TriggerPeriod.Hour, "yyyy-MM-dd", default, "yyyy-MM-dd HH:mm:ss.fff"),
            new TimeAxisConfig(TimeSpan.FromSeconds(5), "HH:mm:ss", TriggerPeriod.Hour, "yyyy-MM-dd", default, "yyyy-MM-dd HH:mm:ss.fff"),
            new TimeAxisConfig(TimeSpan.FromSeconds(10), "HH:mm:ss", TriggerPeriod.Hour, "yyyy-MM-dd", default, "yyyy-MM-dd HH:mm:ss.fff"),
            new TimeAxisConfig(TimeSpan.FromSeconds(30), "HH:mm:ss", TriggerPeriod.Hour, "yyyy-MM-dd", default, "yyyy-MM-dd HH:mm:ss.fff"),

            /* minutes */
            new TimeAxisConfig(TimeSpan.FromMinutes(1), "HH:mm", TriggerPeriod.Day, "yyyy-MM-dd", default, "yyyy-MM-dd HH:mm:ss"),
            new TimeAxisConfig(TimeSpan.FromMinutes(5), "HH:mm", TriggerPeriod.Day, "yyyy-MM-dd", default, "yyyy-MM-dd HH:mm:ss"),
            new TimeAxisConfig(TimeSpan.FromMinutes(10), "HH:mm", TriggerPeriod.Day, "yyyy-MM-dd", default, "yyyy-MM-dd HH:mm:ss"),
            new TimeAxisConfig(TimeSpan.FromMinutes(30), "HH:mm", TriggerPeriod.Day, "yyyy-MM-dd", default, "yyyy-MM-dd HH:mm:ss"),

            /* hours */
            new TimeAxisConfig(TimeSpan.FromHours(1), "HH", TriggerPeriod.Day, "yyyy-MM-dd", default, "yyyy-MM-dd HH:mm"),
            new TimeAxisConfig(TimeSpan.FromHours(3), "HH", TriggerPeriod.Day, "yyyy-MM-dd", default, "yyyy-MM-dd HH:mm"),
            new TimeAxisConfig(TimeSpan.FromHours(6), "HH", TriggerPeriod.Day, "yyyy-MM-dd", default, "yyyy-MM-dd HH:mm"),
            new TimeAxisConfig(TimeSpan.FromHours(12), "HH", TriggerPeriod.Day, "yyyy-MM-dd", default, "yyyy-MM-dd HH:mm"),

            /* days */
            new TimeAxisConfig(TimeSpan.FromDays(1), "dd", TriggerPeriod.Month, "yyyy-MM", default, "yyyy-MM-dd HH:mm"),
            new TimeAxisConfig(TimeSpan.FromDays(10), "dd", TriggerPeriod.Month, "yyyy-MM", default, "yyyy-MM-dd HH"),
            new TimeAxisConfig(TimeSpan.FromDays(30), "dd", TriggerPeriod.Month, "yyyy-MM", default, "yyyy-MM-dd HH"),
            new TimeAxisConfig(TimeSpan.FromDays(90), "dd", TriggerPeriod.Month, "yyyy-MM", default, "yyyy-MM-dd HH"),

            /* years */
            new TimeAxisConfig(TimeSpan.FromDays(365), "yyyy", TriggerPeriod.Year, default, default, "yyyy-MM-dd"),
        ];

        _timeAxisConfig = _timeAxisConfigs.First();

        _colors = [
            new SKColor(0, 114, 189),
            new SKColor(217, 83, 25),
            new SKColor(237, 177, 32),
            new SKColor(126, 47, 142),
            new SKColor(119, 172, 48),
            new SKColor(77, 190, 238),
            new SKColor(162, 20, 47)
        ];
    }

    [Inject]
    public TypeFaceService TypeFaceService { get; set; } = default!;

    [Inject]
    public IJSInProcessRuntime JSRuntime { get; set; } = default!;

    [Inject]
    public AppState AppState { get; set; } = default!;

    [Parameter]
    public LineSeriesData LineSeriesData { get; set; } = default!;

    [Parameter]
    public bool BeginAtZero { get; set; }

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(_axisData, LineSeriesData) && _axisBeginAtZero == BeginAtZero)
            return;

        var dataChanged = !ReferenceEquals(_axisData, LineSeriesData);
        _axisData = LineSeriesData;
        _axisBeginAtZero = BeginAtZero;
        RebuildAxes(LineSeriesData);

        if (dataChanged)
            ResetZoom();
    }

    protected override void OnInitialized()
    {
        _gpuCacheBudgetMiB = AppState.UISettings.EffectiveChartGpuCacheBudgetMiB;
        AppState.PropertyChanged += OnAppStatePropertyChanged;

        /* line series color */
        for (int i = 0; i < LineSeriesData.Series.Count; i++)
        {
            var color = _colors[i % _colors.Length];
            LineSeriesData.Series[i].Color = color;
        }

    }

    private void OnAppStatePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppState.UISettings) || _disposed)
            return;

        _gpuCacheBudgetMiB = AppState.UISettings.EffectiveChartGpuCacheBudgetMiB;

        if (OperatingSystem.IsBrowser())
            JSRuntime.InvokeVoid("nexus.chartWebGpu.setCacheBudget", _chartId, (long)_gpuCacheBudgetMiB * 1024 * 1024);
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender && OperatingSystem.IsBrowser())
        {
            JSRuntime.InvokeVoid("nexus.chartWebGpu.initialize", _chartId, _dotNetHelper);
            JSRuntime.InvokeVoid("nexus.chartWebGpu.setCacheBudget", _chartId, (long)_gpuCacheBudgetMiB * 1024 * 1024);
            JSRuntime.InvokeVoid("nexus.chart.initInteractions", _chartId, _dotNetHelper);
        }
    }

    private void OnMouseMove(MouseEventArgs e)
    {
        var relativePosition = JSRuntime.Invoke<Position>("nexus.chart.toRelative", _chartId, e.ClientX, e.ClientY);
        DrawAuxiliary(relativePosition);
    }

    private void OnMouseLeave(MouseEventArgs e)
    {
        JSRuntime.InvokeVoid("nexus.chart.hide", _chartId, "crosshairs-x");
        JSRuntime.InvokeVoid("nexus.chart.hide", _chartId, "crosshairs-y");

        foreach (var series in LineSeriesData.Series)
        {
            JSRuntime.InvokeVoid("nexus.chart.hide", _chartId, $"pointer_{series.Id}");
            JSRuntime.InvokeVoid("nexus.chart.setTextContent", _chartId, $"value_{series.Id}", "--");
        }
    }

    private void OnDoubleClick(MouseEventArgs e)
    {
        ResetZoom();
        StateHasChanged();

        var relativePosition = JSRuntime.Invoke<Position>("nexus.chart.toRelative", _chartId, e.ClientX, e.ClientY);
        DrawAuxiliary(relativePosition);

        if (OperatingSystem.IsBrowser())
            _skiaView.Invalidate();
    }

    [JSInvokable]
    public void WheelZoom(double x, double y, double deltaY, bool shiftKey)
    {
        const float FACTOR = 0.15f;

        var relativePosition = new Position((float)x, (float)y);

        var zoomHorizontal = !shiftKey;
        var zoomVertical = shiftKey;

        var zoomIn = deltaY < 0;

        var zoomBox = new SKRect
        {
            Left = zoomHorizontal
                ? relativePosition.X * (zoomIn ? +FACTOR : -FACTOR)
                : 0,

            Top = zoomVertical
                ? relativePosition.Y * (zoomIn ? +FACTOR : -FACTOR)
                : 0,

            Right = zoomHorizontal
                ? relativePosition.X + (1 - relativePosition.X) * (zoomIn ? (1 - FACTOR) : (1 + FACTOR))
                : 1,

            Bottom = zoomVertical
                ? relativePosition.Y + (1 - relativePosition.Y) * (zoomIn ? (1 - FACTOR) : (1 + FACTOR))
                : 1
        };

        ApplyZoom(zoomBox);
        DrawAuxiliary(relativePosition);
        StateHasChanged();

        if (OperatingSystem.IsBrowser())
            _skiaView.Invalidate();
    }

    private void ToggleSeriesEnabled(LineSeries series)
    {
        series.Show = !series.Show;

        if (OperatingSystem.IsBrowser())
            _skiaView.Invalidate();
    }

    #region Draw

    private void PaintSurface(SKPaintGLSurfaceEventArgs e)
    {
        /* sizes */
        var canvas = e.Surface.Canvas;
        var surfaceSize = e.BackendRenderTarget.Size;

        var yMin = Y_PADDING_TOP;
        var yMax = surfaceSize.Height - Y_PADDING_Bottom;
        var xMin = Y_PADDING_LEFT;
        var xMax = surfaceSize.Width;

        /* y-axis */
        xMin = DrawYAxes(canvas, xMin, yMin, yMax, _axesMap);
        yMin += Y_UNIT_OFFSET;

        /* time-axis */
        DrawTimeAxis(canvas, xMin, yMin, xMax, yMax, _zoomedBegin, _zoomedEnd);

        /* series */
        var dataBox = new SKRect(xMin, yMin, xMax, yMax);

        /* overlay */
        JSRuntime.InvokeVoid(
            "nexus.chart.resize",
            _chartId,
            "overlay",
            dataBox.Left / surfaceSize.Width,
            dataBox.Top / surfaceSize.Height,
            dataBox.Right / surfaceSize.Width,
            dataBox.Bottom / surfaceSize.Height);

        RenderSeries(dataBox, surfaceSize.Width, surfaceSize.Height);
    }

    private void RenderSeries(SKRect dataBox, float surfaceWidth, float surfaceHeight)
    {
        if (!OperatingSystem.IsBrowser() || _webGpuErrorTitle is not null)
            return;

        var webGpuGeneration = _webGpuGeneration;

        JSRuntime.InvokeVoid(
            "nexus.chartWebGpu.synchronizeSeries",
            _chartId,
            LineSeriesData.Series.Select(lineSeries => lineSeries.Id).ToArray());

        var transfers = new List<Task<SeriesRange>>();
        var series = _axesMap
            .SelectMany(entry => entry.Value
                .Where(lineSeries => lineSeries.Show)
                .Select(lineSeries => (AxisInfo: entry.Key, LineSeries: lineSeries)))
            .Select(item =>
            {
                var lineSeries = item.LineSeries;
                var dataVersion = GetSeriesVersion(lineSeries);
                var length = GetSeriesLength(lineSeries);

                if (!HasSeriesVersion(_sentSeriesVersions, lineSeries.Id, dataVersion, length) &&
                    !HasSeriesVersion(_sendingSeriesVersions, lineSeries.Id, dataVersion, length))
                {
                    _sendingSeriesVersions[lineSeries.Id] = (dataVersion, length);
                    transfers.Add(lineSeries.SyntheticKind.HasValue
                        ? GenerateSyntheticSeriesAsync(lineSeries, dataVersion, length, webGpuGeneration)
                        : SendSeriesBytesAsync(lineSeries.Id, lineSeries.Data, dataVersion, length, webGpuGeneration));
                }

                return new
                {
                    lineSeries.Id,
                    lineSeries.Show,
                    Color = new
                    {
                        lineSeries.Color.Red,
                        lineSeries.Color.Green,
                        lineSeries.Color.Blue,
                        lineSeries.Color.Alpha
                    },
                    AxisMin = item.AxisInfo.Min,
                    AxisMax = item.AxisInfo.Max,
                    OverviewAxisMin = item.AxisInfo.OriginalMin,
                    OverviewAxisMax = item.AxisInfo.OriginalMax,
                    DataVersion = dataVersion,
                    Length = length
                };
            })
            .ToArray();

        JSRuntime.InvokeVoid(
            "nexus.chartWebGpu.renderSeries",
            _chartId,
            new
            {
                Plot = new
                {
                    Left = dataBox.Left / surfaceWidth,
                    Top = dataBox.Top / surfaceHeight,
                    Right = dataBox.Right / surfaceWidth,
                    Bottom = dataBox.Bottom / surfaceHeight
                },
                Zoom = new
                {
                    Left = _zoomLeft,
                    Right = _zoomRight
                },
                LineWidth = 0.7,
                FillOpacity = 0.10,
                Series = series
            });

        JSRuntime.InvokeVoid(
            "nexus.chartWebGpu.renderSeries",
            _chartId,
            new
            {
                Target = "navigator-overview-series",
                Preview = true,
                Plot = new { Left = 0, Top = 0, Right = 1, Bottom = 1 },
                Zoom = new { Left = 0, Right = 1 },
                LineWidth = 0.65,
                FillOpacity = 0.08,
                Series = series
            });

        if (ShowDetailNavigator)
        {
            JSRuntime.InvokeVoid(
                "nexus.chartWebGpu.renderSeries",
                _chartId,
                new
                {
                    Target = "navigator-detail-series",
                    Preview = true,
                    Plot = new { Left = 0, Top = 0, Right = 1, Bottom = 1 },
                    Zoom = new { Left = DetailLeft, Right = DetailRight },
                    LineWidth = 0.65,
                    FillOpacity = 0.08,
                    Series = series
                });
        }

        if (transfers.Count > 0)
            _ = ApplyRangesAfterTransfersAsync(transfers, LineSeriesData, webGpuGeneration);
    }

    private async Task ApplyRangesAfterTransfersAsync(List<Task<SeriesRange>> transfers, LineSeriesData data, int webGpuGeneration)
    {
        SeriesRange[] ranges;

        try
        {
            ranges = await Task.WhenAll(transfers);
        }
        catch (Exception exception) when (!_disposed)
        {
            Console.Error.WriteLine($"[chart-webgpu] series upload or GPU range calculation failed: {exception}");
            return;
        }

        if (_disposed || webGpuGeneration != _webGpuGeneration || !ReferenceEquals(_axisData, data))
            return;

        foreach (var range in ranges)
        {
            if (HasSeriesVersion(_sentSeriesVersions, range.SeriesId, range.Version, range.Length))
                _seriesRanges[range.SeriesId] = range;
        }

        RebuildAxes(data);

        if (OperatingSystem.IsBrowser())
            _skiaView.Invalidate();
    }

    private void RebuildAxes(LineSeriesData data)
    {
        _axesMap = data.Series
            .GroupBy(series => series.Unit)
            .ToDictionary(
                group =>
                {
                    var validRanges = group
                        .Select(series => (Series: series, Range: _seriesRanges.GetValueOrDefault(series.Id)))
                        .Where(item => item.Range.HasValue &&
                            item.Range.Version == GetSeriesVersion(item.Series) &&
                            item.Range.Length == GetSeriesLength(item.Series))
                        .Select(item => item.Range)
                        .ToArray();
                    var minimum = validRanges.Length == 0 ? 0 : validRanges.Min(range => range.Minimum);
                    var maximum = validRanges.Length == 0 ? 0 : validRanges.Max(range => range.Maximum);
                    return CreateAxisInfo(group.Key, minimum, maximum);
                },
                group => group.ToArray());

        foreach (var axisInfo in _axesMap.Keys)
        {
            axisInfo.Min = axisInfo.OriginalMin;
            axisInfo.Max = axisInfo.OriginalMax;
        }
    }

    private async Task<SeriesRange> SendSeriesBytesAsync(string seriesId, double[] data, int dataVersion, int length, int webGpuGeneration)
    {
        long? uploadToken = null;

        try
        {
            uploadToken = await JSRuntime.InvokeAsync<long>(
                "nexus.chartWebGpu.beginSeriesUpload",
                _chartId,
                seriesId,
                dataVersion,
                length);

            const int chunkLength = 256 * 1024;
            var buffer = GC.AllocateUninitializedArray<byte>(chunkLength * sizeof(float));
            long byteOffset = 0;

            for (var offset = 0; offset < data.Length; offset += chunkLength)
            {
                if (_disposed || webGpuGeneration != _webGpuGeneration)
                    return new SeriesRange(seriesId, dataVersion, length, false, 0, 0);

                var count = Math.Min(chunkLength, data.Length - offset);
                var byteCount = count * sizeof(float);
                FillFloatBuffer(buffer, data, offset, count);
                using var stream = new MemoryStream(buffer, 0, byteCount, writable: false, publiclyVisible: true);
                using var streamReference = new DotNetStreamReference(stream);
                await JSRuntime.InvokeVoidAsync(
                    "nexus.chartWebGpu.appendSeriesUpload",
                    _chartId,
                    uploadToken.Value,
                    byteOffset,
                    streamReference);
                byteOffset += byteCount;
            }

            var range = await JSRuntime.InvokeAsync<GpuRange>(
                "nexus.chartWebGpu.completeSeriesUpload",
                _chartId,
                uploadToken.Value);
            uploadToken = null;
            if (webGpuGeneration == _webGpuGeneration)
                _sentSeriesVersions[seriesId] = (dataVersion, length);

            return new SeriesRange(seriesId, dataVersion, length, range.HasValue, range.Minimum, range.Maximum);
        }
        catch (ObjectDisposedException) when (_disposed)
        {
            return new SeriesRange(seriesId, dataVersion, length, false, 0, 0);
        }
        catch (JSDisconnectedException) when (_disposed)
        {
            return new SeriesRange(seriesId, dataVersion, length, false, 0, 0);
        }
        catch (JSException exception)
        {
            throw new InvalidOperationException($"WebGPU upload or range calculation failed for series '{seriesId}'.", exception);
        }
        finally
        {
            if (uploadToken.HasValue && OperatingSystem.IsBrowser())
                JSRuntime.InvokeVoid("nexus.chartWebGpu.abortSeriesUpload", _chartId, uploadToken.Value);

            if (webGpuGeneration == _webGpuGeneration &&
                HasSeriesVersion(_sendingSeriesVersions, seriesId, dataVersion, length))
            {
                _sendingSeriesVersions.Remove(seriesId);
            }
        }
    }

    private static void FillFloatBuffer(byte[] buffer, double[] values, int offset, int count)
    {
        var floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, count * sizeof(float)));
        for (var index = 0; index < count; index++)
            floats[index] = (float)values[offset + index];
    }

    private async Task<SeriesRange> GenerateSyntheticSeriesAsync(LineSeries series, int dataVersion, int length, int webGpuGeneration)
    {
        try
        {
            var range = await JSRuntime.InvokeAsync<GpuRange>(
                "nexus.chartWebGpu.generateSyntheticSeries",
                _chartId,
                series.Id,
                dataVersion,
                length,
                series.SyntheticKind!.Value.ToString());
            if (webGpuGeneration == _webGpuGeneration)
                _sentSeriesVersions[series.Id] = (dataVersion, length);

            return new SeriesRange(series.Id, dataVersion, length, range.HasValue, range.Minimum, range.Maximum);
        }
        catch (ObjectDisposedException) when (_disposed)
        {
            return new SeriesRange(series.Id, dataVersion, length, false, 0, 0);
        }
        catch (JSDisconnectedException) when (_disposed)
        {
            return new SeriesRange(series.Id, dataVersion, length, false, 0, 0);
        }
        catch (JSException exception)
        {
            throw new InvalidOperationException($"WebGPU synthetic generation failed for series '{series.Id}'.", exception);
        }
        finally
        {
            if (webGpuGeneration == _webGpuGeneration &&
                HasSeriesVersion(_sendingSeriesVersions, series.Id, dataVersion, length))
            {
                _sendingSeriesVersions.Remove(series.Id);
            }
        }
    }

    [JSInvokable]
    public Task WebGpuFailed(string title, string message)
    {
        return InvokeAsync(() =>
        {
            if (_disposed)
                return;

            ResetWebGpuState();
            _webGpuErrorTitle = title;
            _webGpuErrorMessage = message;
            _webGpuRetrying = false;
            StateHasChanged();
        });
    }

    private async Task RetryWebGpuAsync()
    {
        if (_disposed || _webGpuRetrying)
            return;

        ResetWebGpuState();
        _webGpuRetrying = true;
        var webGpuGeneration = _webGpuGeneration;

        try
        {
            var initialized = await JSRuntime.InvokeAsync<bool>("nexus.chartWebGpu.retry", _chartId);

            if (_disposed || webGpuGeneration != _webGpuGeneration || !initialized)
                return;

            _webGpuErrorTitle = null;
            _webGpuErrorMessage = null;
        }
        catch (JSException exception) when (!_disposed)
        {
            if (webGpuGeneration == _webGpuGeneration)
            {
                _webGpuErrorTitle = "WebGPU initialization failed";
                _webGpuErrorMessage = $"The chart could not initialize WebGPU: {exception.Message}";
            }
        }
        finally
        {
            if (!_disposed && webGpuGeneration == _webGpuGeneration)
            {
                _webGpuRetrying = false;
                StateHasChanged();
                if (OperatingSystem.IsBrowser())
                    _skiaView.Invalidate();
            }
        }
    }

    private void ResetWebGpuState()
    {
        _webGpuGeneration++;
        _sentSeriesVersions.Clear();
        _sendingSeriesVersions.Clear();
        _seriesRanges.Clear();

        if (_axisData is not null)
            RebuildAxes(_axisData);
    }

    private static int GetSeriesLength(LineSeries series) =>
        series.SyntheticKind.HasValue ? series.SyntheticLength : series.Data.Length;

    private static int GetSeriesVersion(LineSeries series) =>
        series.SyntheticKind.HasValue
            ? HashCode.Combine(series.SyntheticKind.Value, series.SyntheticLength)
            : series.DataVersion;

    private static bool HasSeriesVersion(
        Dictionary<string, (int Version, int Length)> versions,
        string seriesId,
        int dataVersion,
        int length)
    {
        return versions.TryGetValue(seriesId, out var version) &&
               version.Version == dataVersion &&
               version.Length == length;
    }

    private void DrawAuxiliary(Position relativePosition)
    {
        // datetime
        var zoomedTimeRange = _zoomedEnd - _zoomedBegin;
        var currentTimeBegin = _zoomedBegin + zoomedTimeRange * relativePosition.X;
        var currentTimeBeginString = currentTimeBegin.ToString(_timeAxisConfig.CursorLabelFormat);

        JSRuntime.InvokeVoid("nexus.chart.setTextContent", _chartId, $"value_datetime", currentTimeBeginString);

        // crosshairs
        JSRuntime.InvokeVoid("nexus.chart.translate", _chartId, "crosshairs-x", 0, relativePosition.Y);
        JSRuntime.InvokeVoid("nexus.chart.translate", _chartId, "crosshairs-y", relativePosition.X, 0);

        // points
        foreach (var axesEntry in _axesMap)
        {
            var axisInfo = axesEntry.Key;
            var lineSeries = axesEntry.Value;
            var dataRange = axisInfo.Max - axisInfo.Min;
            var decimalDigits = Math.Max(0, -(int)Math.Round(Math.Log10(dataRange), MidpointRounding.AwayFromZero) + 2);
            var formatString = $"F{decimalDigits}";

            foreach (var series in lineSeries)
            {
                var seriesLength = GetSeriesLength(series);
                var lastIndex = seriesLength - 1;
                var indexLeft = _zoomLeft * lastIndex;
                var indexRight = _zoomRight * lastIndex;
                var indexRange = indexRight - indexLeft;
                var index = indexLeft + relativePosition.X * indexRange;
                var snappedIndex = (int)Math.Round(index, MidpointRounding.AwayFromZero);

                if (series.Show && snappedIndex >= 0 && snappedIndex < seriesLength)
                {
                    var x = (snappedIndex - indexLeft) / indexRange;
                    var value = series.SyntheticKind.HasValue
                        ? GetSyntheticValue(series.SyntheticKind.Value, snappedIndex)
                        : (float)series.Data[snappedIndex];
                    var y = (value - axisInfo.Min) / (axisInfo.Max - axisInfo.Min);

                    if (double.IsFinite(x) && 0 <= x && x <= 1 &&
                        float.IsFinite(y) && 0 <= y && y <= 1)
                    {
                        JSRuntime.InvokeVoid("nexus.chart.translate", _chartId, $"pointer_{series.Id}", x, 1 - y);

                        var valueString = string.IsNullOrWhiteSpace(series.Unit)
                            ? value.ToString(formatString)
                            : $"{value.ToString(formatString)} {@series.Unit}";

                        JSRuntime.InvokeVoid("nexus.chart.setTextContent", _chartId, $"value_{series.Id}", valueString);

                        continue;
                    }
                }

                JSRuntime.InvokeVoid("nexus.chart.hide", _chartId, $"pointer_{series.Id}");
                JSRuntime.InvokeVoid("nexus.chart.setTextContent", _chartId, $"value_{series.Id}", "--");
            }
        }

    }

    private AxisInfo CreateAxisInfo(string unit, float min, float max)
    {
        GetYLimits(min, max, out var minLimit, out var maxLimit, out var _);

        if (BeginAtZero)
        {
            if (minLimit > 0)
                minLimit = 0;

            if (maxLimit < 0)
                maxLimit = 0;
        }

        return new AxisInfo(unit, minLimit, maxLimit)
        {
            Min = minLimit,
            Max = maxLimit
        };
    }

    private static float GetSyntheticValue(SyntheticSeriesKind kind, int index)
    {
        if (kind == SyntheticSeriesKind.WindSpeed)
        {
            if (index is 0 or 5 or 6 or 10 or 11 or 12 or 15 or 16 or 17 or 18)
                return float.NaN;

            return index / 4f;
        }

        var value = unchecked((uint)(index + 1));
        value = unchecked((value ^ (value >> 16)) * 0x7feb352dU);
        value = unchecked((value ^ (value >> 15)) * 0x846ca68bU);
        var random = (value ^ (value >> 16)) / 4294967296d;
        return kind == SyntheticSeriesKind.Temperature
            ? (float)(random * 10 - 5)
            : (float)(random * 100 + 1000);
    }

    private readonly record struct GpuRange(bool HasValue, float Minimum, float Maximum);
    private readonly record struct SeriesRange(string SeriesId, int Version, int Length, bool HasValue, float Minimum, float Maximum);

    #endregion

    #region Zoom

    [JSInvokable]
    public void DragZoom(double left, double top, double right, double bottom)
    {
        var zoomBox = new SKRect((float)left, (float)top, (float)right, (float)bottom);

        if (zoomBox.Width <= 0 || zoomBox.Height <= 0)
            return;

        ApplyZoom(zoomBox);
        StateHasChanged();

        if (OperatingSystem.IsBrowser())
            _skiaView.Invalidate();
    }

    private void ApplyZoom(SKRect zoomBox)
    {
        /* zoom box */
        var oldXRange = _oldZoomRight - _oldZoomLeft;
        var oldYRange = _oldZoomBox.Bottom - _oldZoomBox.Top;

        var newLeft = Math.Max(0, _oldZoomLeft + oldXRange * zoomBox.Left);
        var newRight = Math.Min(1, _oldZoomLeft + oldXRange * zoomBox.Right);
        var newZoomBox = new SKRect(
            left: (float)newLeft,
            top: Math.Max(0, _oldZoomBox.Top + oldYRange * zoomBox.Top),
            right: (float)newRight,
            bottom: Math.Min(1, _oldZoomBox.Top + oldYRange * zoomBox.Bottom));

        if (newRight - newLeft < MinimumHorizontalZoom || newZoomBox.Height < 1e-6)
            return;

        /* time range */
        _zoomedBegin = ToTime(newLeft);
        _zoomedEnd = ToTime(newRight);

        /* data range */
        foreach (var axesEntry in _axesMap)
        {
            var axisInfo = axesEntry.Key;
            var originalDataRange = axisInfo.OriginalMax - axisInfo.OriginalMin;

            axisInfo.Min = axisInfo.OriginalMin + (1 - newZoomBox.Bottom) * originalDataRange;
            axisInfo.Max = axisInfo.OriginalMax - newZoomBox.Top * originalDataRange;
        }

        _oldZoomBox = newZoomBox;
        _zoomBox = newZoomBox;
        _oldZoomLeft = _zoomLeft = newLeft;
        _oldZoomRight = _zoomRight = newRight;
    }

    private void ResetZoom()
    {
        /* zoom box */
        _oldZoomBox = _defaultZoomBox;
        _zoomBox = _defaultZoomBox;
        _oldZoomLeft = _zoomLeft = 0;
        _oldZoomRight = _zoomRight = 1;

        /* time range */
        _zoomedBegin = LineSeriesData.Begin;
        _zoomedEnd = LineSeriesData.End;

        /* data range */
        foreach (var axesEntry in _axesMap)
        {
            var axisInfo = axesEntry.Key;

            axisInfo.Min = axisInfo.OriginalMin;
            axisInfo.Max = axisInfo.OriginalMax;
        }
    }

    private void SetHorizontalZoom(double left, double right)
    {
        left = Math.Clamp(left, 0, 1);
        right = Math.Clamp(right, 0, 1);

        if (right - left < MinimumHorizontalZoom)
            return;

        var newZoomBox = new SKRect((float)left, _zoomBox.Top, (float)right, _zoomBox.Bottom);

        _zoomedBegin = ToTime(left);
        _zoomedEnd = ToTime(right);

        _oldZoomBox = newZoomBox;
        _zoomBox = newZoomBox;
        _oldZoomLeft = _zoomLeft = left;
        _oldZoomRight = _zoomRight = right;
    }

    [JSInvokable]
    public void NavigatorZoom(double left, double right)
    {
        SetHorizontalZoom(left, right);

        StateHasChanged();

        if (OperatingSystem.IsBrowser())
            _skiaView.Invalidate();
    }

    [JSInvokable]
    public void SetViewport(double left, double top, double right, double bottom)
    {
        left = Math.Clamp(left, 0, 1);
        top = Math.Clamp(top, 0, 1);
        right = Math.Clamp(right, 0, 1);
        bottom = Math.Clamp(bottom, 0, 1);

        if (right - left < MinimumHorizontalZoom || bottom - top < 1e-6)
            return;

        var newZoomBox = new SKRect((float)left, (float)top, (float)right, (float)bottom);
        _oldZoomBox = newZoomBox;
        _zoomBox = newZoomBox;
        _oldZoomLeft = _zoomLeft = left;
        _oldZoomRight = _zoomRight = right;
        _zoomedBegin = ToTime(left);
        _zoomedEnd = ToTime(right);

        foreach (var axisInfo in _axesMap.Keys)
        {
            var range = axisInfo.OriginalMax - axisInfo.OriginalMin;
            axisInfo.Min = axisInfo.OriginalMin + (1 - newZoomBox.Bottom) * range;
            axisInfo.Max = axisInfo.OriginalMax - newZoomBox.Top * range;
        }

        StateHasChanged();

        if (OperatingSystem.IsBrowser())
            _skiaView.Invalidate();
    }

    private double MinimumHorizontalZoom => 1d / Math.Max(1, (LineSeriesData.End - LineSeriesData.Begin).Ticks);

    private DateTime ToTime(double position)
    {
        var rangeTicks = (LineSeriesData.End - LineSeriesData.Begin).Ticks;
        return LineSeriesData.Begin.AddTicks((long)Math.Round(rangeTicks * position));
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
            return $"{duration.TotalDays:0.##} d";
        if (duration.TotalHours >= 1)
            return $"{duration.TotalHours:0.##} h";
        if (duration.TotalMinutes >= 1)
            return $"{duration.TotalMinutes:0.##} min";
        if (duration.TotalSeconds >= 1)
            return $"{duration.TotalSeconds:0.##} s";
        if (duration.TotalMilliseconds >= 1)
            return $"{duration.TotalMilliseconds:0.###} ms";
        if (duration.TotalMicroseconds >= 1)
            return $"{duration.TotalMicroseconds:0.###} us";

        return $"{duration.Ticks * 100} ns";
    }

    private static string FormatRange(DateTime begin, DateTime end) =>
        $"{begin:yyyy-MM-dd HH:mm:ss.fffffff}  -  {end:yyyy-MM-dd HH:mm:ss.fffffff}";

    #endregion

    #region Y axis

    private float DrawYAxes(SKCanvas canvas, float xMin, float yMin, float yMax, Dictionary<AxisInfo, LineSeries[]> axesMap)
    {
        using var axisLabelFont = new SKFont
        {
            Typeface = TypeFaceService.GetTTF("Courier New Bold")
        };

        using var axisLabelPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0x55, 0x55, 0x55)
        };

        using var axisTickPaint = new SKPaint
        {
            Color = new SKColor(0xDD, 0xDD, 0xDD),
            IsAntialias = true
        };

        var currentOffset = xMin;
        var canvasRange = yMax - yMin;
        var maxTickCount = Math.Max(1, (int)Math.Round(canvasRange / 50, MidpointRounding.AwayFromZero));
        var widthPerCharacter = axisLabelFont.MeasureText(" ");

        foreach (var axesEntry in axesMap)
        {
            var axisInfo = axesEntry.Key;

            /* get ticks */
            var ticks = GetYTicks(axisInfo.Min, axisInfo.Max, maxTickCount);
            var dataRange = axisInfo.Max - axisInfo.Min;

            /* get labels */
            var maxChars = axisInfo.Unit.Length;

            var labels = ticks
                .Select(tick =>
                {
                    var engineeringTick = ToEngineering(tick);
                    maxChars = Math.Max(maxChars, engineeringTick.Length);
                    return engineeringTick;
                })
                .ToArray();

            var textWidth = widthPerCharacter * maxChars;
            var skipDraw = !axesEntry.Value.Any(lineSeries => lineSeries.Show);

            if (!skipDraw)
            {
                /* draw unit */
                var localUnitOffset = maxChars - axisInfo.Unit.Length;
                var xUnit = currentOffset + localUnitOffset * widthPerCharacter;
                var yUnit = yMin;
                canvas.DrawText(axisInfo.Unit, new SKPoint(xUnit, yUnit), axisLabelFont, axisLabelPaint);

                /* draw labels and ticks */
                for (int i = 0; i < ticks.Length; i++)
                {
                    var tick = ticks[i];

                    if (axisInfo.Min <= tick && tick <= axisInfo.Max)
                    {
                        var label = labels[i];
                        var scaleFactor = (canvasRange - Y_UNIT_OFFSET) / dataRange;
                        var localLabelOffset = maxChars - label.Length;
                        var x = currentOffset + localLabelOffset * widthPerCharacter;
                        var y = yMax - (tick - axisInfo.Min) * scaleFactor;

                        canvas.DrawText(label, new SKPoint(x, y + HALF_LINE_HEIGHT), axisLabelFont, axisLabelPaint);

                        var tickX = currentOffset + textWidth + TICK_MARGIN_LEFT;
                        canvas.DrawLine(tickX, y, tickX + TICK_SIZE, y, axisTickPaint);
                    }
                }
            }

            /* update offset */
            currentOffset += textWidth + TICK_MARGIN_LEFT + TICK_SIZE + AXIS_MARGIN_RIGHT;
        }

        return currentOffset - AXIS_MARGIN_RIGHT;
    }

    private static void GetYLimits(double min, double max, out float minLimit, out float maxLimit, out float step)
    {
        /* There are a minimum of 10 ticks and a maximum of 40 ticks with the following approach:
         *
         *          Min   Max   Range   Significant   Min-Rounded   Max-Rounded  Start Step_1  ...   End  Count
         *
         *   Min      0    32      32             2             0           100      0     10  ...   100     10
         *          968  1000      32             2           900          1000    900    910  ...  1000     10
         *
         *   Max     0     31      31             1             0            40      0      1  ...    40     40
         *         969   1000      31             1           960          1000    960    961  ...  1000     40
         */

        /* special case: min == max */
        if (min == max)
        {
            min -= 0.5f;
            max += 0.5f;
        }

        /* range and position of first significant digit */
        var range = max - min;
        var significant = (int)Math.Round(Math.Log10(range), MidpointRounding.AwayFromZero);

        /* get limits */
        minLimit = (float)RoundDown(min, decimalPlaces: -significant);
        maxLimit = (float)RoundUp(max, decimalPlaces: -significant);

        /* special case: min == minLimit */
        if (min == minLimit)
        {
            min -= range / 8;
            minLimit = (float)RoundDown(min, decimalPlaces: -significant);
        }

        /* special case: max == maxLimit */
        if (max == maxLimit)
        {
            max += range / 8;
            maxLimit = (float)RoundUp(max, decimalPlaces: -significant);
        }

        /* get tick step */
        step = (float)Math.Pow(10, significant - 1);
    }

    private float[] GetYTicks(float min, float max, int maxTickCount)
    {
        GetYLimits(min, max, out var minLimit, out var maxLimit, out var step);

        var range = maxLimit - minLimit;
        var tickCount = (int)Math.Ceiling((range / step) + 1);

        /* ensure there are not too many ticks */
        if (tickCount > maxTickCount)
        {
            var originalStep = step;
            var originalTickCount = tickCount;

            for (int i = 0; i < _factors.Length; i++)
            {
                var factor = _factors[i];

                tickCount = (int)Math.Ceiling(originalTickCount / (float)factor);
                step = originalStep * factor;

                if (tickCount <= maxTickCount)
                    break;
            }
        }

        if (tickCount > maxTickCount)
            throw new Exception("Unable to calculate Y-axis ticks.");

        /* calculate actual steps */
        return Enumerable
            .Range(0, tickCount)
            .Select(tickNumber => (float)(minLimit + tickNumber * step))
            .ToArray();
    }

    #endregion

    #region Time axis

    private void DrawTimeAxis(SKCanvas canvas, float xMin, float yMin, float xMax, float yMax, DateTime begin, DateTime end)
    {
        using var axisLabelFont = new SKFont
        {
            Typeface = TypeFaceService.GetTTF("Courier New Bold")
        };

        using var axisLabelPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0x55, 0x55, 0x55)
        };

        using var axisTickPaint = new SKPaint
        {
            Color = SKColors.LightGray,
            IsAntialias = true
        };

        var canvasRange = xMax - xMin;
        var maxTickCount = Math.Max(1, (int)Math.Round(canvasRange / 130, MidpointRounding.AwayFromZero));
        var (config, ticks) = GetTimeTicks(begin, end, maxTickCount);

        _timeAxisConfig = config;

        var timeRange = (end - begin).Ticks;
        var scalingFactor = canvasRange / timeRange;
        var previousTick = DateTime.MinValue;

        foreach (var tick in ticks)
        {
            /* vertical line */
            var x = xMin + (tick - begin).Ticks * scalingFactor;
            canvas.DrawLine(x, yMin, x, yMax + TICK_SIZE, axisTickPaint);

            /* fast tick */
            var tickLabel = tick.ToString(config.FastTickLabelFormat);

            canvas.DrawText(
                tickLabel,
                x, yMax + TICK_SIZE + TIME_AXIS_MARGIN_TOP,
                SKTextAlign.Center,
                axisLabelFont,
                axisLabelPaint
            );

            /* slow tick */
            var addSlowTick = IsSlowTickRequired(previousTick, tick, config.SlowTickTrigger);

            if (addSlowTick)
            {
                if (config.SlowTickLabelFormat1 is not null)
                {
                    var slowTickLabel1 = tick.ToString(config.SlowTickLabelFormat1);

                    canvas.DrawText(
                        slowTickLabel1,
                        x,
                        yMax + TICK_SIZE + TIME_AXIS_MARGIN_TOP + TIME_FAST_LABEL_OFFSET,
                        SKTextAlign.Center,
                        axisLabelFont,
                        axisLabelPaint
                    );
                }

                if (config.SlowTickLabelFormat2 is not null)
                {
                    var slowTickLabel2 = tick.ToString(config.SlowTickLabelFormat2);

                    canvas.DrawText(
                        slowTickLabel2,
                        x,
                        yMax + TICK_SIZE + TIME_AXIS_MARGIN_TOP + TIME_FAST_LABEL_OFFSET * 2,
                        SKTextAlign.Center,
                        axisLabelFont,
                        axisLabelPaint
                    );
                }
            }

            /* */
            previousTick = tick;
        }
    }

    private (TimeAxisConfig, DateTime[]) GetTimeTicks(DateTime begin, DateTime end, int maxTickCount)
    {
        static long GetTickCount(DateTime begin, DateTime end, TimeSpan tickInterval)
            => (long)Math.Ceiling((end - begin) / tickInterval);

        /* find TimeAxisConfig */
        TimeAxisConfig? selectedConfig = default;

        foreach (var config in _timeAxisConfigs)
        {
            var currentTickCount = GetTickCount(begin, end, config.TickInterval);

            if (currentTickCount <= maxTickCount)
            {
                selectedConfig = config;
                break;
            }
        }

        /* ensure TIME_MAX_TICK_COUNT is not exceeded */
        selectedConfig ??= _timeAxisConfigs.Last();

        var tickInterval = selectedConfig.TickInterval;
        var tickCount = GetTickCount(begin, end, tickInterval);

        while (tickCount > maxTickCount)
        {
            tickInterval *= 2;
            tickCount = GetTickCount(begin, end, tickInterval);
        }

        /* calculate ticks */
        var firstTick = RoundUp(begin, tickInterval);

        var ticks = Enumerable
            .Range(0, (int)tickCount)
            .Select(tickIndex => firstTick + tickIndex * tickInterval)
            .Where(tick => tick < end)
            .ToArray();

        return (selectedConfig, ticks);
    }

    private static bool IsSlowTickRequired(DateTime previousTick, DateTime tick, TriggerPeriod trigger)
    {
        return trigger switch
        {
            TriggerPeriod.Second => previousTick.Date != tick.Date ||
                                    previousTick.Hour != tick.Hour ||
                                    previousTick.Minute != tick.Minute ||
                                    previousTick.Second != tick.Second,

            TriggerPeriod.Minute => previousTick.Date != tick.Date ||
                                    previousTick.Hour != tick.Hour ||
                                    previousTick.Minute != tick.Minute,

            TriggerPeriod.Hour => previousTick.Date != tick.Date ||
                                    previousTick.Hour != tick.Hour,

            TriggerPeriod.Day => previousTick.Date != tick.Date,

            TriggerPeriod.Month => previousTick.Year != tick.Year ||
                                    previousTick.Month != tick.Month,

            TriggerPeriod.Year => previousTick.Year != tick.Year,

            _ => throw new Exception("Unsupported trigger period."),
        };
    }

    #endregion

    #region Helpers

    private static string ToEngineering(double value)
    {
        if (value == 0)
            return "0";

        if (Math.Abs(value) < 1000)
            return value.ToString("G4");

        var exponent = (int)Math.Floor(Math.Log10(Math.Abs(value)));

        var pattern = (exponent % 3) switch
        {
            +1 => "##.##e0",
            -2 => "##.##e0",
            +2 => "###.#e0",
            -1 => "###.#e0",
            _ => "#.###e0"
        };

        return value.ToString(pattern);
    }

    private static DateTime RoundUp(DateTime value, TimeSpan roundTo)
    {
        var modTicks = value.Ticks % roundTo.Ticks;

        var delta = modTicks == 0
            ? 0
            : roundTo.Ticks - modTicks;

        return new DateTime(value.Ticks + delta, value.Kind);
    }

    private static double RoundDown(double number, int decimalPlaces)
    {
        return Math.Floor(number * Math.Pow(10, decimalPlaces)) / Math.Pow(10, decimalPlaces);
    }

    private static double RoundUp(double number, int decimalPlaces)
    {
        return Math.Ceiling(number * Math.Pow(10, decimalPlaces)) / Math.Pow(10, decimalPlaces);
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _disposed = true;
        AppState.PropertyChanged -= OnAppStatePropertyChanged;

        if (OperatingSystem.IsBrowser())
        {
            JSRuntime.InvokeVoid("nexus.chart.dispose", _chartId);
            JSRuntime.InvokeVoid("nexus.chartWebGpu.dispose", _chartId);
        }

        _sentSeriesVersions.Clear();
        _sendingSeriesVersions.Clear();
        _seriesRanges.Clear();
        _dotNetHelper?.Dispose();
    }

    #endregion
}
