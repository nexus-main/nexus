using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Utilities;
using System.Reflection;
using System.Text.Json;

namespace Nexus.Services
{
    internal class AppStateManager
    {
        #region Fields

        private readonly IExtensionHive _extensionHive;
        private readonly ICatalogManager _catalogManager;
        private readonly IDatabaseService _databaseService;
        private readonly ILogger<AppStateManager> _logger;
        private readonly SemaphoreSlim _refreshDatabaseSemaphore = new(initialCount: 1, maxCount: 1);
        private readonly SemaphoreSlim _projectSemaphore = new(initialCount: 1, maxCount: 1);

        #endregion

        #region Constructors

        public AppStateManager(
            AppState appState,
            IExtensionHive extensionHive,
            ICatalogManager catalogManager,
            IDatabaseService databaseService,
            ILogger<AppStateManager> logger)
        {
            AppState = appState;
            _extensionHive = extensionHive;
            _catalogManager = catalogManager;
            _databaseService = databaseService;
            _logger = logger;
        }

        #endregion

        #region Properties

        public AppState AppState { get; }

        #endregion

        #region Methods

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

                    refreshDatabaseTask = _extensionHive
                        .LoadPackagesAsync(AppState.Project.PackageReferences.Values, progress, cancellationToken)
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

        public async Task PutPackageReferenceAsync(
            InternalPackageReference packageReference)
        {
            await _projectSemaphore.WaitAsync();

            try
            {
                var project = AppState.Project;

                var newPackageReferences = project.PackageReferences
                    .ToDictionary(current => current.Key, current => current.Value);

                newPackageReferences[packageReference.Id] = packageReference;

                var newProject = project with
                {
                    PackageReferences = newPackageReferences
                };

                await SaveProjectAsync(newProject);

                AppState.Project = newProject;
            }
            finally
            {
                _projectSemaphore.Release();
            }
        }

        public async Task DeletePackageReferenceAsync(
            Guid packageReferenceId)
        {
            await _projectSemaphore.WaitAsync();

            try
            {
                var project = AppState.Project;

                var newPackageReferences = project.PackageReferences
                    .ToDictionary(current => current.Key, current => current.Value);

                newPackageReferences.Remove(packageReferenceId);

                var newProject = project with
                {
                    PackageReferences = newPackageReferences
                };

                await SaveProjectAsync(newProject);

                AppState.Project = newProject;
            }
            finally
            {
                _projectSemaphore.Release();
            }
        }

        public async Task PutDataSourceRegistrationAsync(string userId, InternalDataSourceRegistration registration)
        {
            await _projectSemaphore.WaitAsync();

            try
            {
                var project = AppState.Project;

                if (!project.UserConfigurations.TryGetValue(userId, out var userConfiguration))
                    userConfiguration = new UserConfiguration(new Dictionary<Guid, InternalDataSourceRegistration>());

                var newDataSourceRegistrations = userConfiguration.DataSourceRegistrations
                    .ToDictionary(current => current.Key, current => current.Value);

                newDataSourceRegistrations[registration.Id] = registration;

                var newUserConfiguration = userConfiguration with
                {
                    DataSourceRegistrations = newDataSourceRegistrations
                };

                var userConfigurations = project.UserConfigurations
                    .ToDictionary(current => current.Key, current => current.Value);

                userConfigurations[userId] = newUserConfiguration;

                var newProject = project with
                {
                    UserConfigurations = userConfigurations
                };

                await SaveProjectAsync(newProject);

                AppState.Project = newProject;
            }
            finally
            {
                _projectSemaphore.Release();
            }
        }

        public async Task DeleteDataSourceRegistrationAsync(string username, Guid registrationId)
        {
            await _projectSemaphore.WaitAsync();

            try
            {
                var project = AppState.Project;

                if (!project.UserConfigurations.TryGetValue(username, out var userConfiguration))
                    return;

                var newDataSourceRegistrations = userConfiguration.DataSourceRegistrations
                    .ToDictionary(current => current.Key, current => current.Value);

                newDataSourceRegistrations.Remove(registrationId);

                var newUserConfiguration = userConfiguration with
                {
                    DataSourceRegistrations = newDataSourceRegistrations
                };

                var userConfigurations = project.UserConfigurations
                    .ToDictionary(current => current.Key, current => current.Value);

                userConfigurations[username] = newUserConfiguration;

                var newProject = project with
                {
                    UserConfigurations = userConfigurations
                };

                await SaveProjectAsync(newProject);

                AppState.Project = newProject;
            }
            finally
            {
                _projectSemaphore.Release();
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
                var label = additionalInformation?.GetStringValue(Nexus.UI.Core.Constants.DATA_WRITER_LABEL_KEY);

                if (label is null)
                    throw new Exception($"The description of data writer {fullName} has no label property");

                var version = dataWriterType.Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                    .InformationalVersion;

                var attribute2 = dataWriterType
                    .GetCustomAttribute<ExtensionDescriptionAttribute>(inherit: false);

                if (attribute2 is null)
                    labelsAndDescriptions.Add((label, new ExtensionDescription(
                        fullName,
                        version,
                        default,
                        default,
                        default,
                        additionalInformation)));

                else
                    labelsAndDescriptions.Add((label, new ExtensionDescription(
                        fullName,
                        version,
                        attribute2.Description,
                        attribute2.ProjectUrl,
                        attribute2.RepositoryUrl,
                        additionalInformation)));
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

        #endregion
    }
}
