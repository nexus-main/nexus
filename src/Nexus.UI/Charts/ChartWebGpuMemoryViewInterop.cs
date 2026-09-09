// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Runtime.InteropServices.JavaScript;

namespace Nexus.UI.Charts;

internal static partial class ChartWebGpuMemoryViewInterop
{
    [JSImport("globalThis.nexus.chartWebGpu.appendChunkedSeries")]
    internal static partial void AppendChunkedSeries(
        string chartId,
        [JSMarshalAs<JSType.Number>] long token,
        double offset,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> data,
        int byteLength);

    [JSImport("globalThis.nexus.chartWebGpu.appendSeriesChunk")]
    internal static partial void AppendSeriesChunk(
        string chartId,
        [JSMarshalAs<JSType.Number>] long requestId,
        double offset,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> data,
        int byteLength);
}
