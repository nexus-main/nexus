// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;

namespace Nexus.UI.Charts;

internal static partial class ChartWebGpuMemoryViewInterop
{
    [JSImport("globalThis.nexus.chartWebGpu.appendChunkedSeriesMemoryView")]
    internal static partial void AppendChunkedSeriesMemoryView(
        string chartId,
        [JSMarshalAs<JSType.Number>] long token,
        double offset,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> data,
        int byteLength);
}
