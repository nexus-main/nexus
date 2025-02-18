using Microsoft.JSInterop;

namespace Nexus.UI.Services;

public partial class NexusJSInterop : IDisposable
{
    private IJSInProcessObjectReference _commonModule = default!;

    public NexusJSInterop(IJSInProcessRuntime jsRuntime)
    {
        Runtime = jsRuntime;
    }

    public IJSInProcessRuntime Runtime { get; }

    public async Task InitializeAsync()
    {
        _commonModule = await Runtime
            .InvokeAsync<IJSInProcessObjectReference>("import", "./js/interop.common.js");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _commonModule.Dispose();
    }
}
