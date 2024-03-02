using Microsoft.AspNetCore.Components;
using Nexus.UI.Services;
using SkiaSharp;
using SkiaSharp.Views.Blazor;

namespace Nexus.UI.Charts
{
    public partial class AvailabilityChart
    {
        private const float LINE_HEIGHT = 7.0f;
        private const float HALF_LINE_HEIGHT = LINE_HEIGHT / 2;

        [Inject]
        public TypeFaceService TypeFaceService { get; set; } = default!;

        [Parameter]
        public AvailabilityData AvailabilityData { get; set; } = default!;

        private void PaintSurface(SKPaintGLSurfaceEventArgs e)
        {
            /* sizes */
            var canvas = e.Surface.Canvas;
            var surfaceSize = e.BackendRenderTarget.Size;

            var yMin = LINE_HEIGHT * 2;
            var yMax = (float)surfaceSize.Height;

            var xMin = 0.0f;
            var xMax = (float)surfaceSize.Width;

            /* colors */
            using var barStrokePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = new SKColor(249, 115, 22)
            };

            using var barFillPaint = new SKPaint
            {
                Color = new SKColor(249, 115, 22, 0x19)
            };

            using var axisTitlePaint = new SKPaint
            {
                TextSize = 17,
                IsAntialias = true,
                Color = new SKColor(0x55, 0x55, 0x55),
                TextAlign = SKTextAlign.Center
            };

            using var axisLabelPaint = new SKPaint
            {
                IsAntialias = true,
                Typeface = TypeFaceService.GetTTF("Courier New Bold"),
                Color = new SKColor(0x55, 0x55, 0x55)
            };

            using var axisLabelCenteredPaint = new SKPaint
            {
                IsAntialias = true,
                Typeface = TypeFaceService.GetTTF("Courier New Bold"),
                Color = new SKColor(0x55, 0x55, 0x55),
                TextAlign = SKTextAlign.Center
            };

            using var axisTickPaint = new SKPaint
            {
                Color = new SKColor(0xDD, 0xDD, 0xDD)
            };

            /* y-axis */
            var yRange = yMax - (yMin + 40);

            xMin += 20;

            using (var canvasRestore = new SKAutoCanvasRestore(canvas))
            {
                canvas.RotateDegrees(270, xMin, yMin + yRange / 2);
                canvas.DrawText("Availability / %", new SKPoint(xMin, yMin + yRange / 2), axisTitlePaint);
            }

            xMin += 10;

            var widthPerCharacter = axisLabelPaint.MeasureText(" ");
            var desiredYLabelCount = 11;
            var maxYLabelCount = yRange / 50;
            var ySkip = (int)(desiredYLabelCount / (float)maxYLabelCount) + 1;

            for (int i = 0; i < desiredYLabelCount; i++)
            {
                if ((i + ySkip) % ySkip == 0)
                {
                    var relative = i / 10.0f;
                    var y = yMin + (1 - relative) * yRange;
                    var label = $"{(int)(relative * 100),3:D0}";
                    var lineOffset = widthPerCharacter * 3;

                    canvas.DrawText(label, new SKPoint(xMin, y + HALF_LINE_HEIGHT), axisLabelPaint);
                    canvas.DrawLine(new SKPoint(xMin + lineOffset, y), new SKPoint(xMax, y), axisTickPaint);
                }
            }

            xMin += widthPerCharacter * 4;

            /* x-axis + data */
            var count = AvailabilityData.Data.Count;
            var xRange = xMax - xMin;
            var valueWidth = xRange / count;

            var maxXLabelCount = xRange / 200;
            var xSkip = (int)(count / (float)maxXLabelCount) + 1;
            var lastBegin = DateTime.MinValue;

            for (int i = 0; i < count; i++)
            {
                var availability = AvailabilityData.Data[i];

                var x = xMin + i * valueWidth + valueWidth * 0.1f;
                var y = yMin + yRange;
                var w = valueWidth * 0.8f;
                var h = -yRange * (float)availability;

                canvas.DrawRect(x, y, w, h, barFillPaint);

                var path = new SKPath();

                path.MoveTo(x, y);
                path.RLineTo(0, h);
                path.RLineTo(w, 0);
                path.RLineTo(0, -h);

                canvas.DrawPath(path, barStrokePaint);

                if ((i + xSkip) % xSkip == 0)
                {
                    var currentBegin = AvailabilityData.Begin.AddDays(i);
                    canvas.DrawText(currentBegin.ToString("dd.MM"), xMin + (i + 0.5f) * valueWidth, yMax - 20, axisLabelCenteredPaint);

                    if (lastBegin.Year != currentBegin.Year)
                        canvas.DrawText(currentBegin.ToString("yyyy"), xMin + (i + 0.5f) * valueWidth, yMax, axisLabelCenteredPaint);

                    lastBegin = currentBegin;
                }
            }
        }
    }
}
