// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;

namespace Nexus.Services;

internal interface IDataControllerService
{
    Task<IDataSourceController> GetDataSourceControllerAsync(
        InternalDataSourceRegistration registration,
        CancellationToken cancellationToken);

    Task<IDataWriterController> GetDataWriterControllerAsync(
        Uri resourceLocator,
        ExportParameters exportParameters,
        CancellationToken cancellationToken);
}

internal class DataControllerService(
    AppState appState,
    IHttpContextAccessor httpContextAccessor,
    IExtensionHive extensionHive,
    IProcessingService processingService,
    ICacheService cacheService,
    IOptions<DataOptions> dataOptions,
    ILogger<DataControllerService> logger,
    ILoggerFactory loggerFactory) : IDataControllerService
{
    public const string NexusConfigurationHeaderKey = "Nexus-Configuration";

    private readonly AppState _appState = appState;
    private readonly DataOptions _dataOptions = dataOptions.Value;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly IExtensionHive _extensionHive = extensionHive;
    private readonly IProcessingService _processingService = processingService;
    private readonly ICacheService _cacheService = cacheService;
    private readonly ILogger _logger = logger;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;

    public async Task<IDataSourceController> GetDataSourceControllerAsync(
        InternalDataSourceRegistration registration,
        CancellationToken cancellationToken)
    {
        var logger1 = _loggerFactory.CreateLogger<DataSourceController>();
        var logger2 = _loggerFactory.CreateLogger($"{registration.Type} - {registration.ResourceLocator?.ToString() ?? "<null>"}");

        var dataSource = _extensionHive.GetInstance<IDataSource>(registration.Type);
        var requestConfiguration = GetRequestConfiguration();

        var clonedSystemConfiguration = _appState.Project.SystemConfiguration is null
            ? default
            : _appState.Project.SystemConfiguration.ToDictionary(entry => entry.Key, entry => entry.Value.Clone());

        var controller = new DataSourceController(
            dataSource,
            registration,
            systemConfiguration: clonedSystemConfiguration,
            requestConfiguration: requestConfiguration,
            _processingService,
            _cacheService,
            _dataOptions,
            logger1);

        var actualCatalogCache = _appState.CatalogState.Cache.GetOrAdd(
            registration,
            registration => new ConcurrentDictionary<string, ResourceCatalog>());

        await controller.InitializeAsync(actualCatalogCache, logger2, cancellationToken);

        return controller;
    }

    public async Task<IDataWriterController> GetDataWriterControllerAsync(Uri resourceLocator, ExportParameters exportParameters, CancellationToken cancellationToken)
    {
        var logger1 = _loggerFactory.CreateLogger<DataWriterController>();
        var logger2 = _loggerFactory.CreateLogger($"{exportParameters.Type} - {resourceLocator}");
        var dataWriter = _extensionHive.GetInstance<IDataWriter>(exportParameters.Type ?? throw new Exception("The type must not be null."));
        var requestConfiguration = exportParameters.Configuration;

        var clonedSystemConfiguration = _appState.Project.SystemConfiguration is null
            ? default
            : _appState.Project.SystemConfiguration.ToDictionary(entry => entry.Key, entry => entry.Value.Clone());

        var controller = new DataWriterController(
            dataWriter,
            resourceLocator,
            systemConfiguration: clonedSystemConfiguration,
            requestConfiguration: requestConfiguration,
            logger1);

        await controller.InitializeAsync(logger2, cancellationToken);

        return controller;
    }

    private IReadOnlyDictionary<string, JsonElement>? GetRequestConfiguration()
    {
        var httpContext = _httpContextAccessor.HttpContext;

        if (httpContext is not null &&
            httpContext.Request.Headers.TryGetValue(NexusConfigurationHeaderKey, out var encodedRequestConfiguration))
        {
            var firstEncodedRequestConfiguration = encodedRequestConfiguration.First();

            if (firstEncodedRequestConfiguration is null)
                return default;

            var requestConfiguration = JsonSerializer
                .Deserialize<IReadOnlyDictionary<string, JsonElement>>(Convert.FromBase64String(firstEncodedRequestConfiguration));

            return requestConfiguration;
        }

        return default;
    }
}
