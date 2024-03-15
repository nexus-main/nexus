using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using Nexus.Api;
using Nexus.UI.Components;
using Nexus.UI.Core;
using Nexus.UI.Services;
using System.Globalization;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

/* See App.razor for more info on why this line is required. */
builder.RootComponents.Add<LoadingScreen>("#loading-screen");

// TODO: Very large attachment upload is first loading into memory and only then to server (https://stackoverflow.com/questions/66770670/streaming-large-files-from-blazor-webassembly)

var isDemo = builder.HostEnvironment.BaseAddress.StartsWith("https://malstroem-labs.github.io/");

INexusClient client;

if (isDemo)
{
    Console.WriteLine("Running in demo mode.");
    client = new NexusDemoClient();
}

else
{
    var httpClient = new HttpClient
    {
        BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
    };

    client = new NexusClient(httpClient);
}

builder.Services
    .AddCascadingAuthenticationState()
    .AddAuthorizationCore()
    .AddSingleton(client)
    .AddSingleton(serviceProvider => (IJSInProcessRuntime)serviceProvider.GetRequiredService<IJSRuntime>())
    .AddSingleton(serviceProvider =>
    {
        var jsRuntime = serviceProvider.GetRequiredService<IJSInProcessRuntime>();
        var appState = new AppState(isDemo, client, jsRuntime);

        return appState;
    })

    /* MudBlazor */
    .AddMudServices(config =>
    {
        config.SnackbarConfiguration.SnackbarVariant = Variant.Outlined;
        config.SnackbarConfiguration.VisibleStateDuration = 4000;
        config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomCenter;
    })

    .AddSingleton<TypeFaceService>()
    .AddScoped<AuthenticationStateProvider, NexusAuthenticationStateProvider>();

await builder.Build().RunAsync();
