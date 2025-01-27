// MIT License
// Copyright (c) [2024] [nexus-main]

using Apollo3zehn.PackageManagement.Services;
using Nexus.Core;
using Nexus.Core.V1;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Utilities;
using System.Reflection;
using System.Text.Json;

namespace Nexus.Services;

internal class AppStateManager(
    AppState appState,
    IPackageService packageService,
    IExtensionHive<IDataSource> sourcesExtensionHive,
    IExtensionHive<IDataWriter> writersExtensionHive,
    ICatalogManager catalogManager,
    IDatabaseService databaseService,
    ILogger<AppStateManager> logger)
{
    private readonly IPackageService _packageService = packageService;

    private readonly IExtensionHive<IDataWriter> _writersExtensionHive = writersExtensionHive;

    private readonly IExtensionHive<IDataSource> _sourcesExtensionHive = sourcesExtensionHive;

    private readonly ICatalogManager _catalogManager = catalogManager;

    private readonly IDatabaseService _databaseService = databaseService;

    private readonly ILogger<AppStateManager> _logger = logger;

    private readonly SemaphoreSlim _refreshDatabaseSemaphore = new(initialCount: 1, maxCount: 1);

    private readonly SemaphoreSlim _projectSemaphore = new(initialCount: 1, maxCount: 1);

    public AppState AppState { get; } = appState;

    public async Task RefreshDatabaseAsync(
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        await _refreshDatabaseSemaphore.WaitAsync(cancellationToken);

        try
        {
            // TODO: make atomic
            var refreshDatabaseTask = AppState.ReloadPackagesTask;

            if (refreshDatabaseTask is null)
            {
                /* create fresh app state */
                AppState.CatalogState = new CatalogState(
                    Root: CatalogContainer.CreateRoot(_catalogManager, _databaseService),
                    Cache: new CatalogCache()
                );

                /* load packages */
                _logger.LogInformation("Load packages");

                var packageReferenceMap = await _packageService.GetAllAsync();

                refreshDatabaseTask = Task.Run(async () =>
                {
                    await _sourcesExtensionHive
                        .LoadPackagesAsync(packageReferenceMap, progress, cancellationToken);

                    await _writersExtensionHive
                        .LoadPackagesAsync(packageReferenceMap, progress, cancellationToken);

                    LoadDataWriters();
                    AppState.ReloadPackagesTask = default;
                    return Task.CompletedTask;
                });
            }
        }
        finally
        {
            _refreshDatabaseSemaphore.Release();
        }
    }

    private void LoadDataWriters()
    {
        var labelsAndDescriptions = new List<(string Label, ExtensionDescription Description)>();

        /* for each data writer */
        foreach (var dataWriterType in _writersExtensionHive.GetExtensions())
        {
            var fullName = dataWriterType.FullName!;
            var attribute = dataWriterType.GetCustomAttribute<DataWriterDescriptionAttribute>();

            if (attribute is null)
            {
                _logger.LogWarning("Data writer {DataWriter} has no description attribute", fullName);
                continue;
            }

            var additionalInformation = attribute.Description;
            var label = (additionalInformation?.GetStringValue(UI.Core.Constants.DATA_WRITER_LABEL_KEY)) ?? throw new Exception($"The description of data writer {fullName} has no label property");
            var version = dataWriterType.Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                .InformationalVersion;

            var attribute2 = dataWriterType
                .GetCustomAttribute<ExtensionDescriptionAttribute>(inherit: false);

            if (attribute2 is null)
            {
                labelsAndDescriptions.Add((label, new ExtensionDescription(
                    fullName,
                    version,
                    default,
                    default,
                    default,
                    additionalInformation)));
            }

            else
            {
                labelsAndDescriptions.Add((label, new ExtensionDescription(
                    fullName,
                    version,
                    attribute2.Description,
                    attribute2.ProjectUrl,
                    attribute2.RepositoryUrl,
                    additionalInformation)));
            }
        }

        var dataWriterDescriptions = labelsAndDescriptions
            .OrderBy(current => current.Label)
            .Select(current => current.Description)
            .ToList();

        AppState.DataWriterDescriptions = dataWriterDescriptions;
    }
}
