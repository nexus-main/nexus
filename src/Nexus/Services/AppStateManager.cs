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
    IExtensionHive extensionHive,
    ICatalogManager catalogManager,
    IDatabaseService databaseService,
    ILogger<AppStateManager> logger)
{
    private readonly IPackageService _packageService = packageService;
    private readonly IExtensionHive _extensionHive = extensionHive;
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

                refreshDatabaseTask = _extensionHive
                    .LoadPackagesAsync(packageReferenceMap, progress, cancellationToken)
                    .ContinueWith(task =>
                    {
                        LoadDataWriters();
                        AppState.ReloadPackagesTask = default;
                        return Task.CompletedTask;
                    }, TaskScheduler.Default);
            }
        }
        finally
        {
            _refreshDatabaseSemaphore.Release();
        }
    }

    public async Task PutSystemConfigurationAsync(IReadOnlyDictionary<string, JsonElement>? configuration)
    {
        await _projectSemaphore.WaitAsync();

        try
        {
            var project = AppState.Project;

            var newProject = project with
            {
                SystemConfiguration = configuration
            };

            await SaveProjectAsync(newProject);

            AppState.Project = newProject;
        }
        finally
        {
            _projectSemaphore.Release();
        }
    }

    private void LoadDataWriters()
    {
        var labelsAndDescriptions = new List<(string Label, ExtensionDescription Description)>();

        /* for each data writer */
        foreach (var dataWriterType in _extensionHive.GetExtensions<IDataWriter>())
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

    private async Task SaveProjectAsync(NexusProject project)
    {
        using var stream = _databaseService.WriteProject();
        await JsonSerializerHelper.SerializeIndentedAsync(stream, project);
    }
}
