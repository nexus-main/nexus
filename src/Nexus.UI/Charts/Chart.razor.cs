using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Nexus.UI.Services;
using SkiaSharp;
using SkiaSharp.Views.Blazor;

namespace Nexus.UI.Charts;

public partial class Chart : IDisposable
{
    private SKGLView _skiaView = default!;
    private readonly string _chartId = Guid.NewGuid().ToString();
    private Dictionary<AxisInfo, LineSeries[]> _axesMap = default!;

    /* zoom */
    private bool _isDragging;
    private readonly DotNetObjectReference<Chart> _dotNetHelper;

    private SKRect _oldZoomBox;
    private SKRect _zoomBox;
    private Position _zoomStart;
    private Position _zoomEnd;

    private DateTime _zoomedBegin;
    private DateTime _zoomedEnd;

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
    private bool _beginAtZero;
    private readonly SKColor[] _colors;

    public Chart()
    {
        _dotNetHelper = DotNetObjectReference.Create(this);

        _timeAxisConfigs = new[]
        {
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
        };

        _timeAxisConfig = _timeAxisConfigs.First();

        _colors = new[] {
            new SKColor(0, 114, 189),
            new SKColor(217, 83, 25),
            new SKColor(237, 177, 32),
            new SKColor(126, 47, 142),
            new SKColor(119, 172, 48),
            new SKColor(77, 190, 238),
            new SKColor(162, 20, 47)
        };
    }

    [Inject]
    public TypeFaceService TypeFaceService { get; set; } = default!;

    [Inject]
    public IJSInProcessRuntime JSRuntime { get; set; } = default!;

    [Parameter]
    public LineSeriesData LineSeriesData { get; set; } = default!;

    [Parameter]
    public bool BeginAtZero
    {
        get
        {
            return _beginAtZero;
        }
        set
        {
            if (value != _beginAtZero)
            {
                _beginAtZero = value;

                Task.Run(() =>
                {
                    _axesMap = LineSeriesData.Series
                        .GroupBy(lineSeries => lineSeries.Unit)
                        .ToDictionary(group => GetAxisInfo(group.Key, group), group => group.ToArray());

                    _skiaView.Invalidate();
                });
            }
        }
    }

    protected override void OnInitialized()
    {
        /* line series color */
        for (int i = 0; i < LineSeriesData.Series.Count; i++)
        {
            var color = _colors[i % _colors.Length];
            LineSeriesData.Series[i].Color = color;
        }

        /* axes info */
        _axesMap = LineSeriesData.Series
            .GroupBy(lineSeries => lineSeries.Unit)
            .ToDictionary(group => GetAxisInfo(group.Key, group), group => group.ToArray());

        /* zoom */
        ResetZoom();
    }

    private void OnMouseDown(MouseEventArgs e)
    {
        var position = JSRuntime.Invoke<Position>("nexus.chart.toRelative", _chartId, e.ClientX, e.ClientY);
        _zoomStart = position;
        _zoomEnd = position;

        JSRuntime.InvokeVoid("nexus.util.addMouseUpEvent", _dotNetHelper);

        _isDragging = true;
    }

    [JSInvokable]
    public void OnMouseUp()
    {
        _isDragging = false;

        JSRuntime.InvokeVoid("nexus.chart.resize", _chartId, "selection", 0, 1, 0, 0);

        var zoomBox = CreateZoomBox(_zoomStart, _zoomEnd);

        if (zoomBox.Width > 0 &&
            zoomBox.Height > 0)
        {
            ApplyZoom(zoomBox);
            _skiaView.Invalidate();
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

        var relativePosition = JSRuntime.Invoke<Position>("nexus.chart.toRelative", _chartId, e.ClientX, e.ClientY);
        DrawAuxiliary(relativePosition);

        _skiaView.Invalidate();
    }

    private void OnWheel(WheelEventArgs e)
    {
        const float FACTOR = 0.25f;

        var relativePosition = JSRuntime.Invoke<Position>("nexus.chart.toRelative", _chartId, e.ClientX, e.ClientY);

        var zoomBox = new SKRect
        {
            Left = relativePosition.X * (e.DeltaY < 0
            ? +FACTOR          // +0.25
            : -FACTOR),        // -0.25

            Top = relativePosition.Y * (e.DeltaY < 0
            ? +FACTOR          // +0.25
            : -FACTOR),        // -0.25

            Right = relativePosition.X + (1 - relativePosition.X) * (e.DeltaY < 0
            ? (1 - FACTOR)      // +0.75
            : (1 + FACTOR)),    // +1.25

            Bottom = relativePosition.Y + (1 - relativePosition.Y) * (e.DeltaY < 0
            ? (1 - FACTOR)      // +0.75
            : (1 + FACTOR))    // +1.25
        };


        ApplyZoom(zoomBox);
        DrawAuxiliary(relativePosition);

        _skiaView.Invalidate();
    }

    private void ToggleSeriesEnabled(LineSeries series)
    {
        series.Show = !series.Show;
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

        using (var canvasRestore = new SKAutoCanvasRestore(canvas))
        {
            canvas.ClipRect(dataBox);

            /* for each axis */
            foreach (var axesEntry in _axesMap)
            {
                var axisInfo = axesEntry.Key;
                var lineSeries = axesEntry.Value;

                /* for each dataset */
                foreach (var series in lineSeries)
                {
                    var zoomInfo = GetZoomInfo(dataBox, _zoomBox, series.Data);
                    DrawSeries(canvas, zoomInfo, series, axisInfo);
                }
            }
        }

        /* overlay */
        JSRuntime.InvokeVoid(
            "nexus.chart.resize",
            _chartId,
            "overlay",
            dataBox.Left / surfaceSize.Width,
            dataBox.Top / surfaceSize.Height,
            dataBox.Right / surfaceSize.Width,
            dataBox.Bottom / surfaceSize.Height);
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
                var indexLeft = _zoomBox.Left * series.Data.Length;
                var indexRight = _zoomBox.Right * series.Data.Length;
                var indexRange = indexRight - indexLeft;
                var index = indexLeft + relativePosition.X * indexRange;
                var snappedIndex = (int)Math.Round(index, MidpointRounding.AwayFromZero);

                if (series.Show && snappedIndex < series.Data.Length)
                {
                    var x = (snappedIndex - indexLeft) / indexRange;
                    var value = (float)series.Data[snappedIndex];
                    var y = (value - axisInfo.Min) / (axisInfo.Max - axisInfo.Min);

                    if (float.IsFinite(x) && 0 <= x && x <= 1 &&
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

        // selection
        if (_isDragging)
        {
            _zoomEnd = relativePosition;
            var zoomBox = CreateZoomBox(_zoomStart, _zoomEnd);

            JSRuntime.InvokeVoid(
                "nexus.chart.resize",
                _chartId,
                "selection",
                zoomBox.Left,
                zoomBox.Top,
                zoomBox.Right,
                zoomBox.Bottom);
        }
    }

    private AxisInfo GetAxisInfo(string unit, IEnumerable<LineSeries> lineDatasets)
    {
        var min = float.PositiveInfinity;
        var max = float.NegativeInfinity;

        foreach (var lineDataset in lineDatasets)
        {
            var data = lineDataset.Data;
            var length = data.Length;

            for (int i = 0; i < length; i++)
            {
                var value = (float)data[i];

                if (!double.IsNaN(value))
                {
                    if (value < min)
                        min = value;

                    if (value > max)
                        max = value;
                }
            }
        }

        if (min == double.PositiveInfinity || max == double.NegativeInfinity)
        {
            min = 0;
            max = 0;
        }

        GetYLimits(min, max, out var minLimit, out var maxLimit, out var _);

        if (BeginAtZero)
        {
            if (minLimit > 0)
                minLimit = 0;

            if (maxLimit < 0)
                maxLimit = 0;
        }

        var axisInfo = new AxisInfo(unit, minLimit, maxLimit)
        {
            Min = minLimit,
            Max = maxLimit
        };

        return axisInfo;
    }

    #endregion

    #region Zoom

    private static ZoomInfo GetZoomInfo(SKRect dataBox, SKRect zoomBox, double[] data)
    {
        /* zoom x */
        var indexLeft = zoomBox.Left * data.Length;
        var indexRight = zoomBox.Right * data.Length;
        var indexRange = indexRight - indexLeft;

        /* left */
        /* --> find left index of zoomed data and floor the result to include enough data in the final plot */
        var indexLeftRounded = (int)Math.Floor(indexLeft);
        /* --> find how far left the most left data point is relative to the data box */
        var indexLeftShift = (indexLeft - indexLeftRounded) / indexRange;
        var zoomedLeft = dataBox.Left - dataBox.Width * indexLeftShift;

        /* right */
        /* --> find right index of zoomed data and ceil the result to include enough data in the final plot */
        var indexRightRounded = (int)Math.Ceiling(indexRight);
        /* --> find how far right the most right data point is relative to the data box */
        var indexRightShift = (indexRightRounded - indexRight) / indexRange;
        var zoomedRight = dataBox.Right + dataBox.Width * indexRightShift;

        /* create data array and data box */
        var intendedLength = (indexRightRounded + 1) - indexLeftRounded;
        var zoomedData = data[indexLeftRounded..Math.Min((indexRightRounded + 1), data.Length)];
        var zoomedDataBox = new SKRect(zoomedLeft, dataBox.Top, zoomedRight, dataBox.Bottom);

        /* A full series and a zoomed series are plotted differently:
         * Full: Plot all data from dataBox.Left to dataBox.Right - 1 sample (no more data available, so it is impossible to draw more)
         * Zoomed: Plot all data from dataBox.Left to dataBox.Right (this is possible because more data are available on the right)
         */
        var isClippedRight = zoomedData.Length < intendedLength;

        return new ZoomInfo(zoomedData, zoomedDataBox, isClippedRight);
    }

    private static SKRect CreateZoomBox(Position start, Position end)
    {
        var left = Math.Min(start.X, end.X);
        var top = 0;
        var right = Math.Max(start.X, end.X);
        var bottom = 1;

        return new SKRect(left, top, right, bottom);
    }

    private void ApplyZoom(SKRect zoomBox)
    {
        /* zoom box */
        var oldXRange = _oldZoomBox.Right - _oldZoomBox.Left;
        var oldYRange = _oldZoomBox.Bottom - _oldZoomBox.Top;

        var newZoomBox = new SKRect(
            left: Math.Max(0, _oldZoomBox.Left + oldXRange * zoomBox.Left),
            top: Math.Max(0, _oldZoomBox.Top + oldYRange * zoomBox.Top),
            right: Math.Min(1, _oldZoomBox.Left + oldXRange * zoomBox.Right),
            bottom: Math.Min(1, _oldZoomBox.Top + oldYRange * zoomBox.Bottom));

        if (newZoomBox.Width < 1e-6 || newZoomBox.Height < 1e-6)
            return;

        /* time range */
        var timeRange = LineSeriesData.End - LineSeriesData.Begin;

        _zoomedBegin = LineSeriesData.Begin + timeRange * newZoomBox.Left;
        _zoomedEnd = LineSeriesData.Begin + timeRange * newZoomBox.Right;

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
    }

    private void ResetZoom()
    {
        /* zoom box */
        _oldZoomBox = new SKRect(0, 0, 1, 1);
        _zoomBox = new SKRect(0, 0, 1, 1);

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

    #endregion

    #region Y axis

    private float DrawYAxes(SKCanvas canvas, float xMin, float yMin, float yMax, Dictionary<AxisInfo, LineSeries[]> axesMap)
    {
        using var axisLabelPaint = new SKPaint
        {
            Typeface = TypeFaceService.GetTTF("Courier New Bold"),
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
        var widthPerCharacter = axisLabelPaint.MeasureText(" ");

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
                canvas.DrawText(axisInfo.Unit, new SKPoint(xUnit, yUnit), axisLabelPaint);

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

                        canvas.DrawText(label, new SKPoint(x, y + HALF_LINE_HEIGHT), axisLabelPaint);

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
        using var axisLabelPaint = new SKPaint
        {
            Typeface = TypeFaceService.GetTTF("Courier New Bold"),
            TextAlign = SKTextAlign.Center,
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
            canvas.DrawText(tickLabel, x, yMax + TICK_SIZE + TIME_AXIS_MARGIN_TOP, axisLabelPaint);

            /* slow tick */
            var addSlowTick = IsSlowTickRequired(previousTick, tick, config.SlowTickTrigger);

            if (addSlowTick)
            {
                if (config.SlowTickLabelFormat1 is not null)
                {
                    var slowTickLabel1 = tick.ToString(config.SlowTickLabelFormat1);
                    canvas.DrawText(slowTickLabel1, x, yMax + TICK_SIZE + TIME_AXIS_MARGIN_TOP + TIME_FAST_LABEL_OFFSET, axisLabelPaint);
                }

                if (config.SlowTickLabelFormat2 is not null)
                {
                    var slowTickLabel2 = tick.ToString(config.SlowTickLabelFormat2);
                    canvas.DrawText(slowTickLabel2, x, yMax + TICK_SIZE + TIME_AXIS_MARGIN_TOP + TIME_FAST_LABEL_OFFSET * 2, axisLabelPaint);
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

    #region Series

    private static void DrawSeries(
        SKCanvas canvas,
        ZoomInfo zoomInfo,
        LineSeries series,
        AxisInfo axisInfo)
    {
        var dataBox = zoomInfo.DataBox;
        var data = zoomInfo.Data.Span;

        /* get y scale factor */
        var dataRange = axisInfo.Max - axisInfo.Min;
        var yScaleFactor = dataBox.Height / dataRange;

        /* get dx */
        var dx = zoomInfo.IsClippedRight
            ? dataBox.Width / data.Length
            : dataBox.Width / (data.Length - 1);

        /* draw */
        if (series.Show)
            DrawPath(canvas, axisInfo.Min, dataBox, dx, yScaleFactor, data, series.Color);
    }

    private static void DrawPath(
        SKCanvas canvas,
        float dataMin,
        SKRect dataArea,
        float dx,
        float yScaleFactor,
        Span<double> data,
        SKColor color)
    {
        using var strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            IsAntialias = false /* improves performance */
        };

        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = new SKColor(color.Red, color.Green, color.Blue, 0x19)
        };

        var consumed = 0;
        var length = data.Length;
        var zeroHeight = dataArea.Bottom - (0 - dataMin) * yScaleFactor;

        while (consumed < length)
        {
            /* create path */
            var stroke_path = new SKPath();
            var fill_path = new SKPath();
            var x = dataArea.Left + dx * consumed;
            var y0 = dataArea.Bottom - ((float)data[consumed] - dataMin) * yScaleFactor;

            stroke_path.MoveTo(x, y0);
            fill_path.MoveTo(x, zeroHeight);

            for (int i = consumed; i < length; i++)
            {
                var value = (float)data[i];

                if (float.IsNaN(value)) // all NaN's in a row will be consumed a few lines later
                    break;

                var y = dataArea.Bottom - (value - dataMin) * yScaleFactor;
                x = dataArea.Left + dx * consumed; // do NOT 'currentX += dx' because it constantly accumulates a small error

                stroke_path.LineTo(x, y);
                fill_path.LineTo(x, y);

                consumed++;
            }

            x = dataArea.Left + dx * consumed - dx;

            fill_path.LineTo(x, zeroHeight);
            fill_path.Close();

            /* draw path */
            canvas.DrawPath(stroke_path, strokePaint);
            canvas.DrawPath(fill_path, fillPaint);

            /* consume NaNs */
            for (int i = consumed; i < length; i++)
            {
                var value = (float)data[i];

                if (float.IsNaN(value))
                    consumed++;

                else
                    break;
            }
        }
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
        _dotNetHelper?.Dispose();
    }

    #endregion
}
