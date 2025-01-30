// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Collections.Concurrent;
using System.Text.Json;
using Apollo3zehn.PackageManagement.Services;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.Core.V1;
using Nexus.DataModel;
using Nexus.Extensibility;

namespace Nexus.Services;

internal interface IDataControllerService
{
    Task<IDataSourceController> GetDataSourceControllerAsync(
        DataSourcePipeline pipeline,
        CancellationToken cancellationToken);

    Task<IDataWriterController> GetDataWriterControllerAsync(
        Uri resourceLocator,
        ExportParameters exportParameters,
        CancellationToken cancellationToken);
}

internal class DataControllerService(
    AppState appState,
    IHttpContextAccessor httpContextAccessor,
    IExtensionHive<IDataSource> sourcesExtensionHive,
    IExtensionHive<IDataWriter> writersExtensionHive,
    IProcessingService processingService,
    ICacheService cacheService,
    IOptions<DataOptions> dataOptions,
    ILoggerFactory loggerFactory) : IDataControllerService
{
    public const string NexusConfigurationHeaderKey = "Nexus-Configuration";

    private readonly AppState _appState = appState;
    private readonly DataOptions _dataOptions = dataOptions.Value;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly IExtensionHive<IDataSource> _sourcesExtensionHive = sourcesExtensionHive;
    private readonly IExtensionHive<IDataWriter> _writersExtensionHive = writersExtensionHive;
    private readonly IProcessingService _processingService = processingService;
    private readonly ICacheService _cacheService = cacheService;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;

    public async Task<IDataSourceController> GetDataSourceControllerAsync(
        DataSourcePipeline pipeline,
        CancellationToken cancellationToken)
    {
        var dataSources = pipeline.Registrations
            .Select(registration => (IDataSource)Activator.CreateInstance(_sourcesExtensionHive.GetExtensionType(registration.Type))!)
            .ToArray();

        var requestConfiguration = GetRequestConfiguration();

        var controller = new DataSourceController(
            dataSources,
            pipeline.Registrations,
            requestConfiguration,
            _processingService,
            _cacheService,
            _dataOptions,
            _loggerFactory.CreateLogger<DataSourceController>()
        );

        var catalogCache = _appState.CatalogState.Cache.GetOrAdd(
            pipeline,
            registration => new ConcurrentDictionary<string, ResourceCatalog>());

        await controller.InitializeAsync(catalogCache, _loggerFactory, cancellationToken);

        return controller;
    }

    public async Task<IDataWriterController> GetDataWriterControllerAsync(Uri resourceLocator, ExportParameters exportParameters, CancellationToken cancellationToken)
    {
        var logger1 = _loggerFactory.CreateLogger<DataWriterController>();
        var logger2 = _loggerFactory.CreateLogger($"{exportParameters.Type} - {resourceLocator}");
        var dataWriterType = _writersExtensionHive.GetExtensionType(exportParameters.Type ?? throw new Exception("The type must not be null."));
        var dataWriter = (IDataWriter)Activator.CreateInstance(dataWriterType)!;
        var requestConfiguration = exportParameters.Configuration;

        var controller = new DataWriterController(
            dataWriter,
            resourceLocator,
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
