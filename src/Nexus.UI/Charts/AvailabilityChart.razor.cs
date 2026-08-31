// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Runtime.InteropServices;

namespace Nexus.UI.Charts;

public partial class AvailabilityChart : IAsyncDisposable
{
    private static readonly DateTime UnixEpoch = DateTime.UnixEpoch;

    private readonly string _chartId = Guid.NewGuid().ToString();
    private bool _isRendered;
    private AvailabilityData? _previousAvailabilityData;

    [Inject]
    public IJSRuntime JSRuntime { get; set; } = default!;

    [Parameter]
    public AvailabilityData AvailabilityData { get; set; } = default!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            _isRendered = true;

        if (!_isRendered)
            return;

        if (!ReferenceEquals(_previousAvailabilityData, AvailabilityData) || firstRender)
        {
            _previousAvailabilityData = AvailabilityData;
            await JSRuntime.InvokeVoidAsync("nexus.chartGpu.createOrUpdateAvailabilityChart", _chartId, CreatePayload());
        }
    }

    private object CreatePayload()
    {
        return new
        {
            OriginUnixNanoseconds = ToUnixNanoseconds(AvailabilityData.Begin.ToUniversalTime()).ToString(),
            StepNanoseconds = AvailabilityData.Step.Ticks * 100L,
            ValuesBytes = ToBytes(AvailabilityData.Data)
        };
    }

    private static long ToUnixNanoseconds(DateTime dateTime)
    {
        var utc = dateTime.Kind == DateTimeKind.Utc
            ? dateTime
            : dateTime.ToUniversalTime();

        return checked((utc.Ticks - UnixEpoch.Ticks) * 100L);
    }

    private static byte[] ToBytes(IReadOnlyList<double> values)
    {
        var array = values as double[] ?? values.ToArray();
        return MemoryMarshal.AsBytes(array.AsSpan()).ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isRendered)
            await JSRuntime.InvokeVoidAsync("nexus.chartGpu.dispose", _chartId);
    }
}
