using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.EntityFrameworkCore;
using Nexus.Components;
using Nexus.Core;
using Nexus.Services;
using Nexus.UI.Components;
using Serilog;
using ILogger = Microsoft.Extensions.Logging.ILogger;

// culture
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

// configuration
var configuration = NexusOptionsBase.BuildConfiguration(args);

var generalOptions = configuration
    .GetSection(GeneralOptions.Section)
    .Get<GeneralOptions>() ?? throw new Exception("Unable to instantiate general options");

var securityOptions = configuration
    .GetSection(SecurityOptions.Section)
    .Get<SecurityOptions>() ?? throw new Exception("Unable to instantiate security options");

var pathsOptions = configuration
    .GetSection(PathsOptions.Section)
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
    await InitializeAppAsync(app.Services, pathsOptions, securityOptions, app.Logger);

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
    // database
    Directory.CreateDirectory(pathsOptions.Config);
    var filePath = Path.Combine(pathsOptions.Config, "users.db");

    services.AddDbContext<UserDbContext>(
        options => options.UseSqlite($"Data Source={filePath}"));

    // forwarded headers
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

    // authentication
    services.AddNexusAuth(pathsOptions, securityOptions);

    // Open API
    services.AddNexusOpenApi();

    // default Identity Provider
    if (!securityOptions.OidcProviders.Any())
        services.AddNexusIdentityProvider();

    // razor components
    services.AddRazorComponents()
        .AddInteractiveWebAssemblyComponents();

    // razor pages (for login view)
    services.AddRazorPages();

    // routing
    services.AddRouting(options => options.LowercaseUrls = true);

    // HTTP context
    services.AddHttpContextAccessor();

    // custom
    services.AddTransient<IDataService, DataService>();

    services.AddScoped<IDBService, DbService>();
    services.AddScoped(provider => provider.GetService<IHttpContextAccessor>()!.HttpContext!.User);

    services.AddSingleton<AppState>();
    services.AddSingleton<AppStateManager>();
    services.AddSingleton<ITokenService, TokenService>();
    services.AddSingleton<IMemoryTracker, MemoryTracker>();
    services.AddSingleton<IJobService, JobService>();
    services.AddSingleton<IDataControllerService, DataControllerService>();
    services.AddSingleton<ICatalogManager, CatalogManager>();
    services.AddSingleton<IProcessingService, ProcessingService>();
    services.AddSingleton<ICacheService, CacheService>();
    services.AddSingleton<IDatabaseService, DatabaseService>();
    services.AddSingleton<IExtensionHive, ExtensionHive>();

    services.Configure<GeneralOptions>(configuration.GetSection(GeneralOptions.Section));
    services.Configure<DataOptions>(configuration.GetSection(DataOptions.Section));
    services.Configure<PathsOptions>(configuration.GetSection(PathsOptions.Section));
    services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.Section));
}

void ConfigurePipeline(WebApplication app)
{
    // https://docs.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-6.0

    app.UseForwardedHeaders();

    if (app.Environment.IsDevelopment())
    {
        app.UseWebAssemblyDebugging();
    }

    else
    {
        // TODO: write error page HTML here without razor page (example: app.UseExceptionHandler)

        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }

    // static files
    app.UseStaticFiles();

    // Open API
    var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
    app.UseNexusOpenApi(provider, addExplorer: true);

    // default Identity Provider
    if (!securityOptions.OidcProviders.Any())
        app.UseNexusIdentityProvider();

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

    /* Login view */
    app.MapRazorPages();

    /* Debugging (print all routes) */
    app.MapGet("/debug/routes", (IEnumerable<EndpointDataSource> endpointSources) =>
        string.Join("\n", endpointSources.SelectMany(source => source.Endpoints)));

    // razor components
    app.MapRazorComponents<App>()
        .AddInteractiveWebAssemblyRenderMode()
        .AddAdditionalAssemblies(typeof(MainLayout).Assembly);
}

async Task InitializeAppAsync(
    IServiceProvider serviceProvider,
    PathsOptions pathsOptions,
    SecurityOptions securityOptions,
    ILogger logger)
{
    var appState = serviceProvider.GetRequiredService<AppState>();
    var appStateManager = serviceProvider.GetRequiredService<AppStateManager>();
    var databaseService = serviceProvider.GetRequiredService<IDatabaseService>();

    // database
    using var scope = serviceProvider.CreateScope();
    using var userContext = scope.ServiceProvider.GetRequiredService<UserDbContext>();

    await userContext.Database.EnsureCreatedAsync();

    // project
    if (databaseService.TryReadProject(out var project))
        appState.Project = JsonSerializer.Deserialize<NexusProject>(project) ?? throw new Exception("project is null");

    else
        appState.Project = new NexusProject(
            default,
            new Dictionary<Guid, InternalPackageReference>(),
            new Dictionary<string, UserConfiguration>());

    // packages and catalogs
    await appStateManager.RefreshDatabaseAsync(new Progress<double>(), CancellationToken.None);
}