// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Globalization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Options;
using Nexus.Components;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.Services;
using Nexus.UI.Components;
using Serilog;

// culture
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

// configuration
var configuration = NexusOptionsBase.BuildConfiguration(args);

var generalOptions = configuration
    .GetRequiredSection(GeneralOptions.Section)
    .Get<GeneralOptions>() ?? throw new Exception("Unable to instantiate general options");

var securityOptions = configuration
    .GetRequiredSection(SecurityOptions.Section)
    .Get<SecurityOptions>() ?? throw new Exception("Unable to instantiate security options");

var pathsOptions = configuration
    .GetRequiredSection(PathsOptions.Section)
    .Get<PathsOptions>() ?? throw new Exception("Unable to instantiate path options");

// logging (https://nblumhardt.com/2019/10/serilog-in-aspnetcore-3/)
var applicationName = generalOptions.ApplicationName;

var loggerConfiguration = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration);

if (applicationName is not null)
    loggerConfiguration.Enrich.WithProperty("ApplicationName", applicationName);

Log.Logger = loggerConfiguration
    .CreateLogger();

// run
try
{
    // architecture
    if (!BitConverter.IsLittleEndian)
    {
        Log.Information("This software runs only on little-endian systems.");
        return;
    }

    // memory info
    var memoryInfo = GC.GetGCMemoryInfo();

    Console.WriteLine($"GC: Total available memory: {memoryInfo.TotalAvailableMemoryBytes / 1024 / 1024} MB");
    Console.WriteLine($"GC: High memory load threshold: {memoryInfo.HighMemoryLoadThresholdBytes / 1024 / 1024} MB");

    Log.Information("Start host");

    var builder = WebApplication.CreateBuilder(args);

    builder.Configuration.AddConfiguration(configuration);
    builder.Host.UseSerilog();

    // Add services to the container.
    AddServices(builder.Services, configuration, pathsOptions, securityOptions);

    // Build
    var app = builder.Build();

    // Configure the HTTP request pipeline.
    ConfigurePipeline(app);

    // initialize app state
    await InitializeAppAsync(app.Services);

    // Run
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

void AddServices(
    IServiceCollection services,
    IConfiguration configuration,
    PathsOptions pathsOptions,
    SecurityOptions securityOptions)
{
    // Forwarded headers
    services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.All;
        // TODO: replace this with proper external configuration
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });

    // MVC
    services.AddMvcCore(options =>
    {
        // do not return "204 No Content" when returning null (return empty JSON instead)
        var noContentFormatter = options.OutputFormatters
            .OfType<HttpNoContentOutputFormatter>()
            .FirstOrDefault();

        if (noContentFormatter is not null)
            noContentFormatter.TreatNullValueAsNoContent = false;
    });

    // Authentication
    services.AddNexusAuth(pathsOptions, securityOptions);

    // Open API
    services.AddNexusOpenApi();

    // Razor components
    services.AddRazorComponents()
        .AddInteractiveWebAssemblyComponents();

    // Routing
    services.AddRouting(options => options.LowercaseUrls = true);

    // HTTP context
    services.AddHttpContextAccessor();

    // Custom
    services.AddTransient<IDataService, DataService>();
    services.AddScoped(provider => provider.GetService<IHttpContextAccessor>()!.HttpContext!.User);

    services.AddSingleton<AppState>();
    services.AddSingleton<AppStateManager>();
    services.AddSingleton<IPipelineService, PipelineService>();
    services.AddSingleton<IUpgradeConfigurationService, UpgradeConfigurationService>();
    services.AddSingleton<ITokenService, TokenService>();
    services.AddSingleton<IAcceptedLicenseService, AcceptedLicenseService>();
    services.AddSingleton<IMemoryTracker, MemoryTracker>();
    services.AddSingleton<IJobService, JobService>();
    services.AddSingleton<IDataControllerService, DataControllerService>();
    services.AddSingleton<ICatalogManager, CatalogManager>();
    services.AddSingleton<IProcessingService, ProcessingService>();
    services.AddSingleton<ICacheService, CacheService>();
    services.AddSingleton<IDatabaseService, DatabaseService>();

    // Options
    services.Configure<GeneralOptions>(configuration.GetSection(GeneralOptions.Section));
    services.Configure<DataOptions>(configuration.GetSection(DataOptions.Section));
    services.Configure<PathsOptions>(configuration.GetSection(PathsOptions.Section));
    services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.Section));

    // Package management
    services.AddPackageManagement();
    services.AddExtensionHive<IDataSource>();
    services.AddExtensionHive<IDataWriter>();

    services.AddSingleton<IPackageManagementPathsOptions>(
        serviceProvider => serviceProvider.GetRequiredService<IOptions<PathsOptions>>().Value);
}

void ConfigurePipeline(WebApplication app)
{
    // https://docs.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-6.0

    app.UseForwardedHeaders();

    if (app.Environment.IsDevelopment())
        app.UseWebAssemblyDebugging();

    // static files
    app.MapStaticAssets();

    // Open API
    var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
    app.UseNexusOpenApi(provider, addExplorer: true);

    // Serilog Request Logging (https://andrewlock.net/using-serilog-aspnetcore-in-asp-net-core-3-reducing-log-verbosity/)
    // LogContext properties are not included by default in request logging, workaround: https://nblumhardt.com/2019/10/serilog-mvc-logging/
    app.UseSerilogRequestLogging();

    // routing (for REST API)
    app.UseRouting();

    // anti forgery
    app.UseAntiforgery();

    // workaround for chrome/edge browser: https://stackoverflow.com/a/69764358
    app.UseCookiePolicy(new CookiePolicyOptions
    {
        Secure = CookieSecurePolicy.Always
    });

    // default authentication
    app.UseAuthentication();

    // authorization
    app.UseAuthorization();

    // endpoints

    /* REST API */
    app.MapControllers();

    /* Debugging (print all routes) */
    app.MapGet("/debug/routes", (IEnumerable<EndpointDataSource> endpointSources) =>
        string.Join("\n", endpointSources.SelectMany(source => source.Endpoints)));

    // razor components
    app.MapRazorComponents<App>()
        .AddInteractiveWebAssemblyRenderMode()
        .AddAdditionalAssemblies(typeof(MainLayout).Assembly);
}

async Task InitializeAppAsync(IServiceProvider serviceProvider)
{
    var appStateManager = serviceProvider.GetRequiredService<AppStateManager>();

    // packages and catalogs
    await appStateManager.RefreshDatabaseAsync(new Progress<double>(), CancellationToken.None);
}
